"""Shared pieces for the commander's learning stages.

Everything here is deliberately small and explicit. The encoder is the one part
that matters and it is the same object in every stage — stage two adds heads to
it, stage four adds more, and none of them get to change the representation
underneath, because then the stages would not be comparable.
"""

import glob
import json
import random

import torch
import torch.nn as nn

ENTITY_FIELDS = 10   # type, x, y, health, side, structure, armed, cost, staleSeconds, region
REGION_FIELDS = 7
GLOBAL_FIELDS = 13
MAX_ENTITIES = 512
MAX_REGIONS = 64

# Globals are sampled every 250 ticks (10 s), so six steps ahead is a minute.
HORIZON_STEPS = 6

# Column indices into the globals vector, matching StateExport.GlobalFields.
G_SECONDS, G_CASH, G_EARNED, G_SPENT, G_BANKED, G_POWER = 0, 1, 2, 3, 4, 5
G_HARV, G_PROC, G_OURARMY, G_ENEMYARMY, G_OURSTRUCT, G_ENEMYSTRUCT, G_IDLE = 6, 7, 8, 9, 10, 11, 12

AUX_NAMES = ["enemy_army_60s", "own_army_60s", "income_60s", "structs_lost_60s", "enemy_structs_60s"]

# Stance ids as the chief emits them: Build, Probe, Pressure, Assault, Defend, Recover.
N_STANCES = 6


def load(pattern):
    """Reads every matching JSONL export, grouped by match and ordered by tick.

    Ordering matters: the auxiliary targets are read off the future of each match,
    so a shuffled match is a silently wrong label rather than an error.
    """
    matches = {}
    for path in sorted(glob.glob(pattern)):
        with open(path) as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    # A partially-written final line from a worker killed mid-append.
                    continue
                matches.setdefault(row["match"], []).append(row)

    for rows in matches.values():
        rows.sort(key=lambda r: r["tick"])
    return matches


def add_targets(matches):
    """Attaches the value target and the five auxiliary targets to every sample.

    The auxiliaries are the whole reason this can be trained at all on a few
    hundred matches. One outcome per match is a handful of labels a day; these are
    read off the sample sequence, cost nothing to produce, and each forces the
    encoder to understand something concrete — what the opponent is about to have,
    what we are about to earn, what we are about to lose. A representation that can
    answer those is a representation that can evaluate a position.
    """
    for rows in matches.values():
        n = len(rows)
        for i, row in enumerate(rows):
            g = row["globals"]
            j = min(i + HORIZON_STEPS, n - 1)
            future = rows[j]["globals"]

            row["y_value"] = (float(row.get("margin", 0.0)) + 1.0) / 2.0

            # The macro-action the scripted chief actually chose here. A policy head trained
            # to reproduce it starts from a competent player instead of from noise, which on
            # a few hundred matches is the difference between learning and flailing.
            action = row.get("action") or [-1.0, -1.0, -1.0]
            row["y_stance"] = int(action[0]) if 0 <= int(action[0]) < N_STANCES else -1
            # The position ONE DIRECTIVE LATER, not one sample later, for the dynamics head.
            #
            # Pairing has to happen here while the match sequence is intact - batches are shuffled
            # across matches later, so "the next row in the batch" is an unrelated game. But the
            # horizon matters as much as the pairing, and getting it wrong was measured: with the
            # immediately-next sample (10 s) the dynamics head learned to ignore the action
            # entirely, because over ten seconds the state's own drift dwarfs anything a change of
            # stance does. Action sensitivity came out at 0.3-0.8% of signal and search never left
            # the prior on 48 of 48 positions.
            #
            # A stance is held for 1500 ticks. Predicting that far ahead is asking the question the
            # action actually answers.
            row["next"] = rows[min(i + HORIZON_STEPS, n - 1)]
            # A dense, short-horizon reward for the decision actually taken.
            #
            # Q was previously fitted to the FINAL match margin, twenty decisions and twenty
            # minutes away. Almost all of that signal is other decisions and luck, so the
            # action's own contribution is buried. The stance's measured effect is on the next
            # sixty seconds - 0.79 standard deviations on army, 0.55 on income, 0.32 on
            # structures lost - so that is what it should be asked to predict.
            #
            # Structures are the win condition, so they lead; army and economy are the means and
            # are weighted below them.
            killed = max(0.0, future[G_ENEMYSTRUCT] - g[G_ENEMYSTRUCT])
            lost = max(0.0, g[G_OURSTRUCT] - future[G_OURSTRUCT])
            army_swing = (future[G_OURARMY] - g[G_OURARMY]) - (future[G_ENEMYARMY] - g[G_ENEMYARMY])
            earned = max(0.0, future[G_EARNED] - g[G_EARNED])

            advantage = (killed - lost) / 5.0 + army_swing / 40000.0 + earned / 40000.0
            row["y_reward"] = min(1.0, max(0.0, 0.5 + advantage))

            # How likely the behaviour policy was to take this action. Uniform under a
            # randomised trial, and the weight that corrects for it otherwise.
            row["y_propensity"] = float(action[3]) if len(action) > 3 else 1.0

            row["y_aux"] = [
                future[G_ENEMYARMY] / 40000.0,
                future[G_OURARMY] / 40000.0,
                max(0.0, future[G_EARNED] - g[G_EARNED]) / 20000.0,
                max(0.0, g[G_OURSTRUCT] - future[G_OURSTRUCT]) / 10.0,
                max(0.0, future[G_ENEMYSTRUCT] - g[G_ENEMYSTRUCT]) / 10.0,
            ]
    return matches


def featurise(rows, device, with_next=False):
    """Packs samples into tensors. With `with_next`, also packs each sample's successor
    so the dynamics head has a real target rather than a shuffled neighbour."""
    if with_next:
        base = _pack(rows)
        nxt = _pack([r.get("next", r) for r in rows])
        return tuple(t.to(device) for t in base) + tuple(t.to(device) for t in nxt[:4])

    return tuple(t.to(device) for t in _pack(rows))


def _pack(rows):
    n = len(rows)
    ents = torch.zeros(n, MAX_ENTITIES, ENTITY_FIELDS)
    mask = torch.zeros(n, MAX_ENTITIES, dtype=torch.bool)
    regs = torch.zeros(n, MAX_REGIONS, REGION_FIELDS)
    glob = torch.zeros(n, GLOBAL_FIELDS)
    yv = torch.zeros(n)
    ya = torch.zeros(n, len(AUX_NAMES))
    ys = torch.full((n,), -1, dtype=torch.long)
    yr = torch.zeros(n)
    yp = torch.ones(n)

    for i, row in enumerate(rows):
        e = row["entities"][:MAX_ENTITIES]
        if e:
            ents[i, : len(e)] = torch.tensor(e, dtype=torch.float32)
            mask[i, : len(e)] = True
        r = row["regions"][:MAX_REGIONS]
        if r:
            regs[i, : len(r)] = torch.tensor(r, dtype=torch.float32)
        glob[i] = torch.tensor(row["globals"], dtype=torch.float32)
        yv[i] = row["y_value"]
        ya[i] = torch.tensor(row["y_aux"], dtype=torch.float32)
        ys[i] = row.get("y_stance", -1)
        yr[i] = row.get("y_reward", 0.5)
        yp[i] = max(1e-3, row.get("y_propensity", 1.0))

    e, r, g = normalise(ents, regs, glob)
    return e, mask, r, g, yv, ya, ys, yr, yp


def normalise(ents, regs, glob):
    """Scales raw game units into a range gradients can use.

    Cells, credits and tens of thousands of army value in one tensor means the big
    columns own every gradient and the small ones are never learned. Type is left
    integral because it indexes an embedding.
    """
    e = ents.clone()
    e[..., 1] /= 128.0
    e[..., 2] /= 128.0
    e[..., 7] /= 2000.0
    e[..., 8] /= 120.0
    e[..., 9] /= 64.0

    r = regs.clone()
    r[..., 0:2] /= 20000.0
    r[..., 2:4] /= 20.0

    g = glob.clone()
    g[..., G_SECONDS] /= 1200.0
    g[..., G_CASH:G_BANKED] /= 200000.0
    g[..., G_HARV:G_OURARMY] /= 20.0
    g[..., G_OURARMY:G_OURSTRUCT] /= 40000.0
    g[..., G_OURSTRUCT:G_IDLE] /= 50.0
    return e, r, g


class Encoder(nn.Module):
    """Attention over the actor set, plus region tokens and the globals.

    Permutation-invariant and size-agnostic by construction, which is the property
    that lets one network handle five units or five hundred without being told
    which to expect.
    """

    def __init__(self, n_types=128, d=96, heads=4, layers=3, dropout=0.1):
        super().__init__()
        self.d = d
        self.type_embed = nn.Embedding(n_types, d)
        self.entity_in = nn.Linear(ENTITY_FIELDS - 1, d)
        self.region_in = nn.Linear(REGION_FIELDS, d)
        self.global_in = nn.Sequential(nn.Linear(GLOBAL_FIELDS, d), nn.GELU(), nn.Linear(d, d))
        layer = nn.TransformerEncoderLayer(
            d_model=d, nhead=heads, dim_feedforward=d * 4,
            batch_first=True, norm_first=True, dropout=dropout)
        self.encoder = nn.TransformerEncoder(layer, num_layers=layers)

    def forward(self, ents, mask, regs, glob):
        type_id = ents[..., 0].long().clamp(0, self.type_embed.num_embeddings - 1)
        tokens = self.entity_in(ents[..., 1:]) + self.type_embed(type_id)
        seq = torch.cat([tokens, self.region_in(regs)], dim=1)

        pad = torch.cat([~mask, torch.zeros(regs.shape[0], regs.shape[1],
                                            dtype=torch.bool, device=mask.device)], dim=1)
        # An all-padding row makes attention produce NaN rather than raise. Keep one live.
        pad[:, 0] = False

        encoded = self.encoder(seq, src_key_padding_mask=pad)
        live = (~pad).unsqueeze(-1).float()
        pooled = (encoded * live).sum(1) / live.sum(1).clamp(min=1.0)
        peak = encoded.masked_fill(pad.unsqueeze(-1), -1e4).max(dim=1).values
        return torch.cat([pooled, peak, self.global_in(glob)], dim=-1)


class CommanderNet(nn.Module):
    """The one network: shared encoder, one head per question asked of it."""

    def __init__(self, use_aux=True, use_policy=False, use_dynamics=False, use_q=False,
                 n_types=128, d=96, **kw):
        super().__init__()
        self.encoder = Encoder(n_types=n_types, d=d, **kw)
        self.use_aux, self.use_policy = use_aux, use_policy
        self.use_dynamics, self.use_q = use_dynamics, use_q
        w = d * 3
        self.width = w

        self.value = nn.Sequential(
            nn.LayerNorm(w), nn.Linear(w, d), nn.GELU(), nn.Dropout(0.1), nn.Linear(d, 1))

        if use_aux:
            self.aux = nn.Sequential(
                nn.LayerNorm(w), nn.Linear(w, d), nn.GELU(), nn.Linear(d, len(AUX_NAMES)))

        if use_policy:
            self.policy = nn.Sequential(
                nn.LayerNorm(w), nn.Linear(w, d), nn.GELU(), nn.Linear(d, N_STANCES))

        if use_q:
            # Q(s, a): what the outcome looks like if THIS action is taken here.
            #
            # This replaces the latent dynamics model, and the replacement is measured rather
            # than stylistic. Asking a head to predict the next 288-dimensional latent lets it
            # ignore the action entirely, because the action's real effect is small against the
            # latent's total variance - sensitivity came out at 0.3-0.8% of signal and search
            # never left its prior on 96 of 96 positions.
            #
            # But the effect is genuinely there in the data: grouping outcomes by the stance in
            # force, the spread between stances is 0.79 standard deviations on army sixty seconds
            # later, 0.55 on income, 0.32 on structures lost. A head asked for exactly that -
            # the outcome, conditioned on the action - is asked for the part that varies instead
            # of a vector where it is a rounding error.
            self.q_action = nn.Embedding(N_STANCES, d)
            self.q = nn.Sequential(
                nn.LayerNorm(w + d), nn.Linear(w + d, d), nn.GELU(), nn.Linear(d, 1))
            self.q_outcome = nn.Sequential(
                nn.LayerNorm(w + d), nn.Linear(w + d, d), nn.GELU(), nn.Linear(d, len(AUX_NAMES)))

        if use_dynamics:
            # Given the current latent and a macro-action, predict the NEXT latent. Search then
            # runs in this space rather than over a hand-written forward model - the last one of
            # those could not represent winning, which is why the planner it fed is switched off.
            self.action_embed = nn.Embedding(N_STANCES + 1, d)
            self.dynamics = nn.Sequential(
                nn.LayerNorm(w + d), nn.Linear(w + d, w), nn.GELU(), nn.Linear(w, w))

    def encode(self, ents, mask, regs, glob):
        return self.encoder(ents, mask, regs, glob)

    def q_values(self, h):
        """Q and predicted outcome for every action at once, for a batch of states."""
        n, a = h.shape[0], N_STANCES
        rep = h.unsqueeze(1).expand(n, a, h.shape[-1])
        acts = self.q_action(torch.arange(a, device=h.device)).unsqueeze(0).expand(n, a, -1)
        joint = torch.cat([rep, acts], dim=-1)
        return self.q(joint).squeeze(-1), self.q_outcome(joint)

    def heads(self, h, action=None):
        v = self.value(h).squeeze(-1)
        a = self.aux(h) if self.use_aux else None
        p = self.policy(h) if self.use_policy else None
        d = None
        if self.use_dynamics and action is not None:
            idx = action.clamp(min=0)
            d = self.dynamics(torch.cat([h, self.action_embed(idx)], dim=-1))
        return v, a, p, d

    def forward(self, ents, mask, regs, glob, action=None):
        h = self.encode(ents, mask, regs, glob)
        v, a, p, d = self.heads(h, action)
        return (v, a) if not (self.use_policy or self.use_dynamics) else (v, a, p, d, h)


def split_by_match(matches, holdout_frac, seed):
    """Holds out whole matches. Splitting by row scores memorisation, not skill."""
    ids = sorted(matches)
    random.Random(seed).shuffle(ids)
    cut = max(1, int(len(ids) * holdout_frac))
    return ids[cut:], ids[:cut]


def batches_of(rows, size, device, seed=0, with_next=False):
    rows = list(rows)
    random.Random(seed).shuffle(rows)
    return [featurise(rows[i:i + size], device, with_next)
            for i in range(0, len(rows), size)]


def device_of():
    return torch.device("mps" if torch.backends.mps.is_available() else "cpu")
