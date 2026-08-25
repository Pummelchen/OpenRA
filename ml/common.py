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
            # The genuinely next position in this match, for the dynamics head. Pairing has to
            # happen here, while the match sequence is still intact: batches are shuffled across
            # matches later, so "the next row in the batch" is an unrelated game.
            row["next"] = rows[min(i + 1, n - 1)]
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

    e, r, g = normalise(ents, regs, glob)
    return e, mask, r, g, yv, ya, ys


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

    def __init__(self, use_aux=True, use_policy=False, use_dynamics=False,
                 n_types=128, d=96, **kw):
        super().__init__()
        self.encoder = Encoder(n_types=n_types, d=d, **kw)
        self.use_aux, self.use_policy, self.use_dynamics = use_aux, use_policy, use_dynamics
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

        if use_dynamics:
            # Given the current latent and a macro-action, predict the NEXT latent. Search then
            # runs in this space rather than over a hand-written forward model - the last one of
            # those could not represent winning, which is why the planner it fed is switched off.
            self.action_embed = nn.Embedding(N_STANCES + 1, d)
            self.dynamics = nn.Sequential(
                nn.LayerNorm(w + d), nn.Linear(w + d, w), nn.GELU(), nn.Linear(w, w))

    def encode(self, ents, mask, regs, glob):
        return self.encoder(ents, mask, regs, glob)

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
