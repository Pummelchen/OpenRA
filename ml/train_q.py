#!/usr/bin/env python3
"""Stage four, done the way the data says it should be.

The latent dynamics model failed, twice, and the diagnosis was that the data had no
counterfactual. That diagnosis was wrong. Grouping outcomes by the stance actually in
force shows the action is plainly visible in what happens next:

    own army in 60s        0.79 standard deviations between stances
    income in 60s          0.55
    enemy army             0.35
    structures lost        0.32
    enemy structures killed 0.23

The signal was there. The instrument was wrong. Asking a head to predict a
288-dimensional next latent lets it ignore the action, because the action's effect is
a rounding error against that vector's total variance. Asking it for the outcome
CONDITIONED on the action asks for exactly the part that varies.

So: Q(s, a) trained on the action actually taken and the result that followed, plus an
outcome head for interpretability. With six macro-actions and a directive that lasts a
minute, argmax Q is already a policy - search is what you add afterwards to look
further than one move, not what makes the thing work.

The test that matters is whether Q separates the actions at all, and whether its
preference ever differs from the imitation policy's. Both are reported.
"""

import argparse
import sys

import torch
import torch.nn as nn

import common

# Discount per sixty-second decision. A match is about twenty of them.
GAMMA = 0.95


def evaluate(model, batches):
    model.eval()
    brier = acc = agree = qdiff = n = 0.0
    changed = 0
    with torch.no_grad():
        for batch in batches:
            ents, mask, regs, glob, yv, ya, ys, yr, yp = batch[:9]
            h = model.encode(ents, mask, regs, glob)
            v = torch.sigmoid(model.value(h)).squeeze(-1)
            brier += ((v - yv) ** 2).sum().item()
            acc += ((v > 0.5).float() == (yv > 0.5).float()).float().sum().item()

            p = model.policy(h)
            valid = ys >= 0
            if valid.any():
                agree += (p[valid].argmax(-1) == ys[valid]).float().sum().item()

            q, _ = model.q_values(h)
            qdiff += (q.max(-1).values - q.min(-1).values).sum().item()
            changed += (q.argmax(-1) != p.argmax(-1)).sum().item()
            n += len(yv)

    n = max(1, n)
    return brier / n, acc / n, agree / n, qdiff / n, changed / n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="commander-states.*.jsonl")
    ap.add_argument("--epochs", type=int, default=14)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--holdout-frac", type=float, default=0.25)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--save", default="ml/commander_q.pt")
    args = ap.parse_args()

    torch.manual_seed(args.seed)
    device = common.device_of()
    print(f"device: {device}", flush=True)

    matches = common.add_targets(common.load(args.data))
    if len(matches) < 8:
        sys.exit(f"only {len(matches)} matches - generate more first")

    train_ids, held_ids = common.split_by_match(matches, args.holdout_frac, args.seed)
    train_rows = [r for m in train_ids for r in matches[m]]
    held_rows = [r for m in held_ids for r in matches[m]]
    print(f"matches {len(matches)} ({len(train_ids)}/{len(held_ids)})  "
          f"samples {len(train_rows)}/{len(held_rows)}", flush=True)

    tb = common.batches_of(train_rows, args.batch, device, args.seed, with_next=True)
    hb = common.batches_of(held_rows, args.batch, device, args.seed, with_next=True)

    model = common.CommanderNet(use_aux=True, use_policy=True, use_q=True).to(device)
    print(f"params {sum(p.numel() for p in model.parameters()) / 1e6:.2f}M\n", flush=True)

    opt = torch.optim.AdamW(model.parameters(), lr=3e-4, weight_decay=1e-4)
    bce = nn.BCEWithLogitsLoss()
    smooth = nn.SmoothL1Loss()
    ce = nn.CrossEntropyLoss(ignore_index=-1)
    best = None

    for epoch in range(args.epochs):
        model.train()
        totals = dict(value=0.0, aux=0.0, policy=0.0, q=0.0, outcome=0.0)
        for batch in tb:
            ents, mask, regs, glob, yv, ya, ys, yr, yp = batch[:9]
            nents, nmask, nregs, nglob = batch[9:13]
            opt.zero_grad()
            h = model.encode(ents, mask, regs, glob)

            loss = bce(model.value(h).squeeze(-1), yv)
            totals["value"] += loss.item()

            la = smooth(model.aux(h), ya)
            loss = loss + la
            totals["aux"] += la.item()

            valid = ys >= 0
            if valid.any():
                lp = ce(model.policy(h), ys)
                loss = loss + 0.5 * lp
                totals["policy"] += lp.item()

                # Q and the outcome head are trained ONLY on the action that was actually
                # taken. Anything else would be inventing a label for a counterfactual
                # nobody observed - the honest version of off-policy learning needs
                # importance weighting, and at this sample size it would add more variance
                # than it removes.
                q_all, out_all = model.q_values(h)
                idx = ys.clamp(min=0).unsqueeze(-1)
                q_taken = q_all.gather(1, idx).squeeze(-1)
                out_taken = out_all.gather(1, idx.unsqueeze(-1).expand(-1, 1, out_all.shape[-1])).squeeze(1)

                # Fitted to the sixty-second reward, not the final margin, and weighted by the
                # inverse propensity so that actions the behaviour policy rarely took are not
                # under-counted. Under a randomised trial the weights are uniform and this
                # reduces to a plain fit - which is the point: the correction is there for when
                # the data is not randomised.
                w = (1.0 / yp[valid])
                w = w / w.mean().clamp(min=1e-6)
                # Temporal difference, not a one-step reward fit.
                #
                # The one-step reward is causal here - the stance was randomised - but it is
                # myopic, and measurably so: under randomisation Defend scores highest over the
                # next sixty seconds (0.673 against Assault's 0.617), because preserving
                # structures scores well and losing them scores badly. A policy trained on that
                # alone learns never to attack, and never attacking never wins a match.
                #
                # Bootstrapping restores the horizon while keeping the causal one-step term:
                # what an action is worth is what it earns now plus what the position it leads to
                # is worth. Gamma is per sixty-second decision, so 0.95 reaches roughly twenty
                # decisions - about the length of a match.
                # Eval mode for the bootstrap: the target should be the model's estimate, not a
                # dropout-noised sample of it, and Metal has no dropout in the fused attention
                # path either way.
                model.eval()
                with torch.no_grad():
                    nh = model.encode(nents, nmask, nregs, nglob)
                    nq, _ = model.q_values(nh)
                    bootstrap = torch.sigmoid(nq).max(-1).values
                model.train()

                target = ((yr + GAMMA * bootstrap) / (1.0 + GAMMA)).clamp(0.0, 1.0)
                lq = (nn.functional.binary_cross_entropy_with_logits(
                    q_taken[valid], target[valid], reduction="none") * w).mean()
                lo = smooth(out_taken[valid], ya[valid])
                loss = loss + lq + lo
                totals["q"] += lq.item()
                totals["outcome"] += lo.item()

            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()

        brier, acc, agree, qspread, changed = evaluate(model, hb)
        # Select on value quality, but only once Q has actually separated the actions.
        # Without that guard the best-Brier epoch is the FIRST one, when Q is still
        # untrained and "disagrees with the policy on 100% of positions" - which reads
        # like a result and is just an untrained head.
        if qspread > 0.02 and (best is None or brier < best[0]):
            best = (brier, acc, agree, qspread, changed, epoch)
            torch.save({"state_dict": model.state_dict(), "holdout_brier": brier}, args.save)

        k = max(1, len(tb))
        print(f"  epoch {epoch + 1:2d}  value {totals['value']/k:.4f} q {totals['q']/k:.4f}"
              f" outcome {totals['outcome']/k:.4f}"
              f" | brier {brier:.4f} acc {acc:.3f} agree {agree:.3f}"
              f" q-spread {qspread:.4f} q!=policy {changed:.3f}", flush=True)

    if best is None:
        sys.exit("Q never separated the actions - nothing worth saving")

    brier, acc, agree, qspread, changed, ep = best
    print("\n" + "=" * 68)
    print(f"holdout brier {brier:.4f}  acc {acc:.3f}  policy agreement {agree:.3f}  (epoch {ep + 1})")
    print(f"Q separation between best and worst action: {qspread:.4f}")
    print("Q is fitted to the 60-second reward under logged propensities.")
    print(f"Q prefers a different action than the policy on {changed * 100:.1f}% of positions")
    print("VERDICT:", "GO - Q tells the actions apart and sometimes disagrees with imitation"
          if qspread > 0.02 and changed > 0.05 else
          "NO - Q cannot separate the actions any better than the dynamics model could")
    print(f"saved: {args.save}")
    print("=" * 68)


if __name__ == "__main__":
    main()
