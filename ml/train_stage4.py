#!/usr/bin/env python3
"""Stage four: policy head, dynamics head, and search over them.

The policy head is trained by imitation — reproduce the macro-action the scripted
chief actually chose. That is not the goal, it is the starting point: a policy that
begins as a competent player and is then improved is a completely different
proposition from one that begins as noise, when the scarce resource is matches.

The dynamics head predicts the NEXT latent given the current latent and an action,
so search can run without a hand-written forward model. The last hand-written one
could not represent winning — an attack looked like all cost and no benefit — which
is exactly why the planner it fed is switched off today.

Search is then PUCT over the macro-actions, using the policy as prior and the value
as leaf evaluation, unrolled through the dynamics head. Reported here as the mean
depth-limited improvement over the raw policy so it can be seen to be doing
something rather than assumed to be.
"""

import argparse
import math
import sys

import torch
import torch.nn as nn
import torch.nn.functional as F

import common


def evaluate(model, batches):
    """Value quality, plus how often the policy agrees with the scripted chief."""
    model.eval()
    brier, acc, agree, n = 0.0, 0.0, 0.0, 0
    with torch.no_grad():
        for batch in batches:
            ents, mask, regs, glob, yv, ya, ys = batch[:7]
            h = model.encode(ents, mask, regs, glob)
            v, _, p, _ = model.heads(h, None)
            prob = torch.sigmoid(v)
            brier += ((prob - yv) ** 2).sum().item()
            acc += ((prob > 0.5).float() == (yv > 0.5).float()).float().sum().item()
            if p is not None:
                valid = ys >= 0
                if valid.any():
                    agree += (p[valid].argmax(-1) == ys[valid]).float().sum().item()
            n += len(yv)
    return brier / max(1, n), acc / max(1, n), agree / max(1, n)


def train(model, tb, hb, epochs, lr, weights):
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    bce, smooth, ce = nn.BCEWithLogitsLoss(), nn.SmoothL1Loss(), nn.CrossEntropyLoss(ignore_index=-1)
    best = (1e9, 0.0, 0.0, -1)

    for epoch in range(epochs):
        model.train()
        totals = dict(value=0.0, aux=0.0, policy=0.0, dyn=0.0)
        for batch in tb:
            ents, mask, regs, glob, yv, ya, ys = batch[:7]
            nents, nmask, nregs, nglob = batch[7:11]
            opt.zero_grad()
            h = model.encode(ents, mask, regs, glob)
            v, a, p, d = model.heads(h, ys)

            loss = bce(v, yv)
            totals["value"] += loss.item()

            if a is not None:
                la = smooth(a, ya)
                loss = loss + weights["aux"] * la
                totals["aux"] += la.item()

            if p is not None and (ys >= 0).any():
                lp = ce(p, ys)
                loss = loss + weights["policy"] * lp
                totals["policy"] += lp.item()

            if d is not None:
                # Predict the encoder's latent for THE NEXT POSITION IN THIS MATCH. The first
                # version of this rolled the batch by one, which - because batches are shuffled
                # across matches - made the target an unrelated game. The dynamics loss never
                # moved and search was measured inert on 24 of 24 positions, because unrolling a
                # model of nothing goes nowhere.
                # Encoded in eval mode on purpose: the target should be the representation
                # itself, not a dropout-noised sample of it. (It is also the only way this runs
                # on Metal, which has no dropout in the fused attention path.)
                model.eval()
                with torch.no_grad():
                    target = model.encode(nents, nmask, nregs, nglob)
                model.train()
                ld = F.smooth_l1_loss(d, target)
                loss = loss + weights["dyn"] * ld
                totals["dyn"] += ld.item()

            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()

        brier, acc, agree = evaluate(model, hb)
        if brier < best[0]:
            best = (brier, acc, agree, epoch)
        k = max(1, len(tb))
        print(f"  epoch {epoch + 1:2d}  value {totals['value']/k:.4f} aux {totals['aux']/k:.4f}"
              f" policy {totals['policy']/k:.4f} dyn {totals['dyn']/k:.4f}"
              f" | holdout brier {brier:.4f} acc {acc:.3f} policy-agree {agree:.3f}", flush=True)

    return best


@torch.no_grad()
def puct_search(model, h, simulations=64, c=1.6, depth=3, root_noise=0.25, alpha=0.5):
    """PUCT over macro-actions, unrolled through the learned dynamics.

    Two details here are not decoration, and leaving them out was measured: search
    picked the prior's favourite on 24 of 24 positions.

    FIRST-PLAY URGENCY. An unvisited action has no estimate, and scoring it zero in a
    0..1 value space says "certain loss" rather than "unknown". PUCT then never tries
    it, all simulations pile onto one action, and the other five keep their zero
    forever - which is exactly what the visit counts showed: [0,0,64,0,0,0]. Unvisited
    actions start at the parent's own value instead.

    ROOT NOISE. The policy is trained to imitate a nearly deterministic scripted chief,
    so its prior comes out at 0.997 on one stance. Multiplied into the exploration term
    that leaves nothing for the alternatives. Dirichlet noise at the root is what
    AlphaZero uses for the same reason and it is not optional here.
    """
    n_actions = common.N_STANCES
    counts = torch.zeros(n_actions, device=h.device)
    totals = torch.zeros(n_actions, device=h.device)

    prior = torch.softmax(model.policy(h), dim=-1).squeeze(0)
    if root_noise > 0:
        noise = torch.distributions.Dirichlet(
            torch.full((n_actions,), alpha, device=h.device)).sample()
        prior = (1 - root_noise) * prior + root_noise * noise

    parent_value = torch.sigmoid(model.value(h)).item()

    for _ in range(simulations):
        q = torch.where(counts > 0, totals / counts.clamp(min=1),
                        torch.full_like(totals, parent_value))
        u = q + c * prior * math.sqrt(max(1.0, counts.sum().item())) / (1 + counts)
        a = int(u.argmax().item())

        latent, value = h, parent_value
        for step in range(depth):
            action = torch.tensor([a if step == 0 else int(
                torch.softmax(model.policy(latent), dim=-1).argmax(-1).item())], device=h.device)
            latent = model.dynamics(torch.cat([latent, model.action_embed(action)], dim=-1))
            value = torch.sigmoid(model.value(latent)).item()

        counts[a] += 1
        totals[a] += value

    return counts, torch.where(counts > 0, totals / counts.clamp(min=1),
                               torch.full_like(totals, float("nan")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="commander-states.*.jsonl")
    ap.add_argument("--epochs", type=int, default=20)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--holdout-frac", type=float, default=0.25)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--save", default="ml/commander_net_full.pt")
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

    model = common.CommanderNet(use_aux=True, use_policy=True, use_dynamics=True).to(device)
    print(f"params {sum(p.numel() for p in model.parameters()) / 1e6:.2f}M\n", flush=True)

    brier, acc, agree, ep = train(
        model, tb, hb, args.epochs, 3e-4,
        {"aux": 1.0, "policy": 0.5, "dyn": 0.5})

    torch.save({"state_dict": model.state_dict(), "holdout_brier": brier}, args.save)

    # Does search move off the prior for a REASON? Measured with root noise switched off
    # and over a large enough sample to mean something.
    #
    # The first version of this test used 24 positions with noise on and reported "search is
    # active" twice. Both were false: with noise off and 96 positions the answer was 0/96
    # both times. Dirichlet noise tipping ties between six actions whose values differ by
    # 0.0009 is not search disagreeing with its prior, it is a coin landing on its edge.
    model.eval()
    ents, mask, regs, glob, yv, ya, ys = hb[0][:7]
    total = min(96, len(yv))
    changed = 0
    for i in range(total):
        h = model.encode(ents[i:i + 1], mask[i:i + 1], regs[i:i + 1], glob[i:i + 1])
        counts, _ = puct_search(model, h, root_noise=0.0)
        greedy = int(torch.softmax(model.policy(h), -1).argmax().item())
        if int(counts.argmax().item()) != greedy:
            changed += 1

    # And how much the action changes the predicted next latent at all - the quantity that
    # decides whether search CAN discriminate, independent of whether it happened to.
    with torch.no_grad():
        h0 = model.encode(ents[:1], mask[:1], regs[:1], glob[:1])
        rolled = torch.cat([
            model.dynamics(torch.cat([h0, model.action_embed(torch.tensor([a], device=h0.device))], dim=-1))
            for a in range(common.N_STANCES)], 0)
        sensitivity = ((rolled.max(0).values - rolled.min(0).values).mean()
                       / rolled.abs().mean().clamp(min=1e-9)).item() * 100

    print(f"action sensitivity of the dynamics head: {sensitivity:.2f}% of signal")

    print("\n" + "=" * 64)
    print(f"holdout brier {brier:.4f}  acc {acc:.3f}  policy agreement {agree:.3f}  (epoch {ep + 1})")
    print(f"search changed the chosen action on {changed}/{total} positions (noise off)")
    print("VERDICT:", "search is active - it disagrees with its prior on merit"
          if changed > total * 0.05 else
          "search is inert - the dynamics head cannot tell the actions apart")
    print(f"saved: {args.save}")
    print("=" * 64)


if __name__ == "__main__":
    main()
