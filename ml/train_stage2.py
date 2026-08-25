#!/usr/bin/env python3
"""Stage two: do the auxiliary heads make the value head better?

Stage one established that the full position beats hand-picked scalars, and that
the encoder overfits after a single epoch on a few dozen matches. The design's
answer is not a bigger dataset alone — it is more *targets per sample*.

So this is an ablation and nothing else. The same encoder, the same data, the same
split, trained twice: once predicting only the outcome, once also predicting five
things about the next sixty seconds that cost nothing to label. If the auxiliary
version's VALUE head is better on held-out matches, the representation is learning
the game rather than memorising which matches were won.
"""

import argparse
import sys

import torch
import torch.nn as nn

import common


def evaluate(model, batches):
    model.eval()
    brier, acc, n = 0.0, 0.0, 0
    with torch.no_grad():
        for ents, mask, regs, glob, yv, _, _ in batches:
            v, _ = model(ents, mask, regs, glob)
            p = torch.sigmoid(v)
            brier += ((p - yv) ** 2).sum().item()
            acc += ((p > 0.5).float() == (yv > 0.5).float()).float().sum().item()
            n += len(yv)
    return brier / max(1, n), acc / max(1, n)


def train(model, train_batches, held_batches, epochs, lr, aux_weight, label):
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    value_loss = nn.BCEWithLogitsLoss()
    aux_loss = nn.SmoothL1Loss()
    best = (1e9, 0.0, -1)

    for epoch in range(epochs):
        model.train()
        total = 0.0
        for ents, mask, regs, glob, yv, ya, _ in train_batches:
            opt.zero_grad()
            v, a = model(ents, mask, regs, glob)
            loss = value_loss(v, yv)
            if a is not None:
                loss = loss + aux_weight * aux_loss(a, ya)
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
            total += loss.item()

        brier, acc = evaluate(model, held_batches)
        if brier < best[0]:
            best = (brier, acc, epoch)
        print(f"  {label} epoch {epoch + 1:2d}  train {total / max(1, len(train_batches)):.4f}"
              f"  holdout brier {brier:.4f}  acc {acc:.3f}", flush=True)

    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="commander-states.*.jsonl")
    ap.add_argument("--epochs", type=int, default=20)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--holdout-frac", type=float, default=0.25)
    ap.add_argument("--aux-weight", type=float, default=1.0)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--save", default="ml/commander_net.pt")
    args = ap.parse_args()

    torch.manual_seed(args.seed)
    device = common.device_of()
    print(f"device: {device}", flush=True)

    matches = common.add_targets(common.load(args.data))
    if len(matches) < 8:
        sys.exit(f"only {len(matches)} matches - generate more before trusting a number")

    train_ids, held_ids = common.split_by_match(matches, args.holdout_frac, args.seed)
    train_rows = [r for m in train_ids for r in matches[m]]
    held_rows = [r for m in held_ids for r in matches[m]]

    print(f"matches {len(matches)} ({len(train_ids)} train / {len(held_ids)} holdout)")
    print(f"samples {len(train_rows)} / {len(held_rows)}", flush=True)

    base = sum(r["y_value"] for r in train_rows) / max(1, len(train_rows))
    floor = sum((base - r["y_value"]) ** 2 for r in held_rows) / max(1, len(held_rows))
    print(f"floor (predict {base:.3f} always): brier {floor:.4f}\n", flush=True)

    tb = common.batches_of(train_rows, args.batch, device, args.seed)
    hb = common.batches_of(held_rows, args.batch, device, args.seed)

    print("A: value head only")
    a_brier, a_acc, a_ep = train(
        common.CommanderNet(use_aux=False).to(device), tb, hb,
        args.epochs, 3e-4, 0.0, "value-only")

    print("\nB: value head + five auxiliary heads")
    model = common.CommanderNet(use_aux=True).to(device)
    b_brier, b_acc, b_ep = train(
        model, tb, hb, args.epochs, 3e-4, args.aux_weight, "value+aux")

    torch.save({"state_dict": model.state_dict(),
                "aux_names": common.AUX_NAMES,
                "holdout_brier": b_brier}, args.save)

    print("\n" + "=" * 64)
    print(f"floor                        brier {floor:.4f}")
    print(f"A  value head only           brier {a_brier:.4f}  acc {a_acc:.3f}  (epoch {a_ep + 1})")
    print(f"B  value + auxiliary heads   brier {b_brier:.4f}  acc {b_acc:.3f}  (epoch {b_ep + 1})")
    gain = (a_brier - b_brier) / a_brier * 100 if a_brier else 0.0
    print(f"\nauxiliary heads move the VALUE head {gain:+.1f}% on Brier")
    print("VERDICT:", "GO - dense targets improve the representation"
          if b_brier < a_brier else
          "NO GAIN - the auxiliaries are not helping the value head here")
    print(f"saved: {args.save}")
    print("=" * 64)


if __name__ == "__main__":
    main()
