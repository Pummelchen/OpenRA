#!/usr/bin/env python3
"""Stage one: can the full position predict the result better than nine scalars?

This is deliberately a go/no-go and not a product. The commander already ships a
logistic regression over nine hand-picked features; if an entity-set encoder over
every actor on record cannot beat it on matches it has never seen, then the state
export is missing something and no amount of search built on top will rescue it.
Finding that out costs an afternoon here and costs weeks later.

Both models are trained on the same samples, from the same matches, labelled by
the same result, and scored on held-out *matches* rather than held-out rows -
splitting by row puts samples from one game on both sides and scores memorisation.
"""

import argparse
import json
import math
import pathlib
import random
import sys

import torch
import torch.nn as nn

ENTITY_FIELDS = 10   # type, x, y, health, side, structure, armed, cost, staleSeconds, region
REGION_FIELDS = 7
GLOBAL_FIELDS = 13
MAX_ENTITIES = 512


def load(path):
    """Reads the JSONL export, grouped by match so the split can be honest."""
    matches = {}
    with open(path) as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            matches.setdefault(row["match"], []).append(row)
    return matches


def featurise(rows, device, cap=MAX_ENTITIES):
    """Packs a batch of samples into padded tensors plus a mask."""
    n = len(rows)
    ents = torch.zeros(n, cap, ENTITY_FIELDS)
    mask = torch.zeros(n, cap, dtype=torch.bool)
    regs = torch.zeros(n, 64, REGION_FIELDS)
    glob = torch.zeros(n, GLOBAL_FIELDS)
    y = torch.zeros(n)

    for i, row in enumerate(rows):
        e = row["entities"][:cap]
        if e:
            ents[i, : len(e)] = torch.tensor(e, dtype=torch.float32)
            mask[i, : len(e)] = True
        r = row["regions"][:64]
        if r:
            regs[i, : len(r)] = torch.tensor(r, dtype=torch.float32)
        glob[i] = torch.tensor(row["globals"], dtype=torch.float32)
        # The graded outcome, mapped to 0..1. Binary "won" is zero for essentially every
        # match in this dataset - almost all of them end at the time limit - so training on
        # it teaches a model only the base rate. How far ahead the commander finished is
        # defined for every match and is monotone in the thing worth predicting.
        y[i] = (float(row.get("margin", 0.0)) + 1.0) / 2.0

    return ents.to(device), mask.to(device), regs.to(device), glob.to(device), y.to(device)


def normalise(ents, regs, glob):
    """Scales the raw numbers into a range a network can actually use.

    Positions are in map cells, costs in credits, army values in tens of thousands.
    Handed over raw, the large columns dominate every gradient and the small ones
    are never learned. Type stays integral because it is an embedding index.
    """
    e = ents.clone()
    e[..., 1] /= 128.0                      # x
    e[..., 2] /= 128.0                      # y
    e[..., 7] /= 2000.0                     # cost
    e[..., 8] /= 120.0                      # staleness, seconds
    e[..., 9] /= 64.0                       # region id

    r = regs.clone()
    r[..., 0:2] /= 20000.0                  # army value in region
    r[..., 2:4] /= 20.0                     # structures in region

    g = glob.clone()
    g[..., 0] /= 1200.0                     # seconds
    g[..., 1:4] /= 200000.0                 # cash, earned, spent
    g[..., 6:8] /= 20.0                     # harvesters, refineries
    g[..., 8:10] /= 40000.0                 # army values
    g[..., 10:12] /= 50.0                   # structure counts
    return e, r, g


class EntityValueNet(nn.Module):
    """Entity-set encoder with a value head.

    Attention over the actor tokens is the whole point: the position is a
    variable-length *set*, so the encoder must be permutation-invariant and
    indifferent to how many units are on the field. A fixed-width vector of
    summary statistics cannot represent "their armour is massed at one choke
    while their refineries sit open", which is exactly the distinction the
    prediction depends on.
    """

    def __init__(self, n_types=128, d=96, heads=4, layers=3):
        super().__init__()
        self.type_embed = nn.Embedding(n_types, d)
        self.entity_in = nn.Linear(ENTITY_FIELDS - 1, d)
        self.region_in = nn.Linear(REGION_FIELDS, d)
        self.global_in = nn.Sequential(nn.Linear(GLOBAL_FIELDS, d), nn.GELU(), nn.Linear(d, d))

        layer = nn.TransformerEncoderLayer(
            d_model=d, nhead=heads, dim_feedforward=d * 4,
            batch_first=True, norm_first=True, dropout=0.1)
        self.encoder = nn.TransformerEncoder(layer, num_layers=layers)

        self.head = nn.Sequential(
            nn.LayerNorm(d * 3), nn.Linear(d * 3, d), nn.GELU(), nn.Dropout(0.1), nn.Linear(d, 1))

    def forward(self, ents, mask, regs, glob):
        type_id = ents[..., 0].long().clamp(0, self.type_embed.num_embeddings - 1)
        rest = torch.cat([ents[..., 1:]], dim=-1)
        tokens = self.entity_in(rest) + self.type_embed(type_id)

        region_tokens = self.region_in(regs)
        seq = torch.cat([tokens, region_tokens], dim=1)

        pad = torch.cat([~mask, torch.zeros(regs.shape[0], regs.shape[1],
                                            dtype=torch.bool, device=mask.device)], dim=1)

        # A sample with no entities at all would be entirely padding, and attention
        # over an all-masked row produces NaN rather than an error. Keep one slot live.
        pad[:, 0] = False

        encoded = self.encoder(seq, src_key_padding_mask=pad)

        live = (~pad).unsqueeze(-1).float()
        pooled = (encoded * live).sum(1) / live.sum(1).clamp(min=1.0)
        peak = encoded.masked_fill(pad.unsqueeze(-1), -1e4).max(dim=1).values

        return self.head(torch.cat([pooled, peak, self.global_in(glob)], dim=-1)).squeeze(-1)


class ScalarBaseline(nn.Module):
    """Logistic regression on the globals - the incumbent, in spirit.

    The shipped evaluator uses nine hand-picked features; these thirteen globals are
    the same kind of quantity and, if anything, slightly more generous. Beating a
    baseline that has been made deliberately fair is the only result worth having.
    """

    def __init__(self):
        super().__init__()
        self.linear = nn.Linear(GLOBAL_FIELDS, 1)

    def forward(self, ents, mask, regs, glob):
        return self.linear(glob).squeeze(-1)


def brier_and_acc(logits, y):
    """Squared error on the graded outcome, plus agreement on which side finished ahead."""
    p = torch.sigmoid(logits)
    return ((p - y) ** 2).mean().item(), (((p > 0.5).float() == (y > 0.5).float()).float().mean().item())


def run(model, batches, device, epochs, lr, name, holdout):
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    # Soft targets in 0..1, so cross-entropy against a graded label rather than a class.
    loss_fn = nn.BCEWithLogitsLoss()
    best = None

    for epoch in range(epochs):
        model.train()
        random.shuffle(batches)
        total = 0.0
        for ents, mask, regs, glob, y in batches:
            opt.zero_grad()
            loss = loss_fn(model(ents, mask, regs, glob), y)
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
            total += loss.item()

        model.eval()
        with torch.no_grad():
            hb, ha, n = 0.0, 0.0, 0
            for ents, mask, regs, glob, y in holdout:
                b, a = brier_and_acc(model(ents, mask, regs, glob), y)
                hb += b * len(y); ha += a * len(y); n += len(y)
            hb /= n; ha /= n

        if best is None or hb < best[0]:
            best = (hb, ha, epoch)
        print(f"  {name} epoch {epoch + 1:2d}  train {total / max(1, len(batches)):.4f}"
              f"  holdout brier {hb:.4f}  acc {ha:.3f}")

    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="commander-states.jsonl")
    ap.add_argument("--epochs", type=int, default=12)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--holdout-frac", type=float, default=0.25)
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    torch.manual_seed(args.seed)
    random.seed(args.seed)

    device = torch.device("mps" if torch.backends.mps.is_available() else "cpu")
    print(f"device: {device}")

    path = pathlib.Path(args.data)
    if not path.exists():
        sys.exit(f"no data at {path}")

    matches = load(path)
    ids = sorted(matches)
    random.Random(args.seed).shuffle(ids)
    cut = max(1, int(len(ids) * args.holdout_frac))
    held, train_ids = set(ids[:cut]), ids[cut:]

    train_rows = [r for m in train_ids for r in matches[m]]
    held_rows = [r for m in held for r in matches[m]]
    wins = sum((r.get("margin", 0.0) + 1) / 2 for r in train_rows)
    print(f"matches {len(ids)} ({len(train_ids)} train / {len(held)} holdout)")
    print(f"samples {len(train_rows)} train / {len(held_rows)} holdout"
          f" | mean outcome {wins / max(1, len(train_rows)):.3f}")

    if len(held) < 2 or not held_rows:
        sys.exit("not enough matches to hold any out - generate more before trusting a number")

    def batched(rows):
        random.Random(args.seed).shuffle(rows)
        return [featurise(rows[i:i + args.batch], device)
                for i in range(0, len(rows), args.batch)]

    train_batches, held_batches = batched(train_rows), batched(held_rows)

    def prep(batches):
        out = []
        for ents, mask, regs, glob, y in batches:
            e, r, g = normalise(ents, regs, glob)
            out.append((e, mask, r, g, y))
        return out

    train_batches, held_batches = prep(train_batches), prep(held_batches)

    # Predicting the base rate is the floor any model must clear to have said anything.
    base = sum((r.get("margin", 0.0) + 1) / 2 for r in train_rows) / max(1, len(train_rows))
    floor = sum(((base - (r.get("margin", 0.0) + 1) / 2) ** 2) for r in held_rows) / max(1, len(held_rows))
    print(f"\nalways-predict-{base:.3f} holdout brier: {floor:.4f}  <- the floor\n")

    print("baseline: logistic over globals")
    b_brier, b_acc, b_ep = run(ScalarBaseline().to(device), train_batches, device,
                               args.epochs, 3e-2, "scalar", held_batches)

    print("\nentity-set encoder")
    n_types = 128
    e_brier, e_acc, e_ep = run(EntityValueNet(n_types=n_types).to(device), train_batches, device,
                               args.epochs, 3e-4, "entity", held_batches)

    print("\n" + "=" * 62)
    print(f"floor (base rate)      brier {floor:.4f}")
    print(f"scalar baseline        brier {b_brier:.4f}  acc {b_acc:.3f}  (epoch {b_ep + 1})")
    print(f"entity-set encoder     brier {e_brier:.4f}  acc {e_acc:.3f}  (epoch {e_ep + 1})")
    gain = (b_brier - e_brier) / b_brier * 100 if b_brier else 0.0
    print(f"\nentity encoder is {gain:+.1f}% on Brier against the scalar baseline")
    print("VERDICT:", "GO - the full position carries signal the scalars do not"
          if e_brier < b_brier * 0.97 else
          "NO-GO - the export is not giving the encoder anything to work with")
    print("=" * 62)


if __name__ == "__main__":
    main()
