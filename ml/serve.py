#!/usr/bin/env python3
"""Serves the trained commander network to the game.

The bot posts the same position it would otherwise have written to the state log,
and gets back a value, a policy over stances, and — when search is enabled — the
action search preferred after unrolling the learned dynamics.

Deliberately a separate process rather than in-process inference. The strategic
decision is made a few times a minute, so a local HTTP round trip is free at this
cadence, and keeping the model out of the simulation means a bad model cannot
desync or crash a match. The micro policy, which is consulted orders of magnitude
more often, is the one that would need to be exported and run in-process.

    ml/serve.py --model ml/commander_net_full.pt --port 8766
"""

import argparse
import pathlib

import torch
from fastapi import FastAPI
from pydantic import BaseModel

import common
import train_stage4

app = FastAPI(title="commander")
STATE = {"model": None, "device": None, "search": True, "sims": 64}


class Position(BaseModel):
    entities: list[list[float]] = []
    regions: list[list[float]] = []
    globals: list[float] = []


@app.get("/health")
def health():
    return {"ok": STATE["model"] is not None,
            "device": str(STATE["device"]),
            "search": STATE["search"]}


@app.post("/evaluate")
def evaluate(position: Position):
    """Returns the network's read of one position.

    `value` is the predicted graded outcome in 0..1 — above 0.5 means finishing
    ahead. `stance` is what to do about it. When search is on, `stance` is what
    search chose, which is allowed to differ from the policy's own favourite; both
    are returned so the caller can see when they disagree.
    """
    model, device = STATE["model"], STATE["device"]
    if model is None:
        return {"error": "no model loaded"}

    row = {
        "entities": position.entities,
        "regions": position.regions,
        "globals": position.globals or [0.0] * common.GLOBAL_FIELDS,
        "margin": 0.0,
        "action": [-1, -1, -1],
    }
    common.add_targets({0: [row]})

    ents, mask, regs, glob, _, _, _ = common.featurise([row], device)

    with torch.no_grad():
        h = model.encode(ents, mask, regs, glob)
        value = torch.sigmoid(model.value(h)).item()
        prior = torch.softmax(model.policy(h), dim=-1).squeeze(0)
        greedy = int(prior.argmax().item())

        chosen, searched = greedy, None
        if STATE["search"] and model.use_dynamics:
            counts, values = train_stage4.puct_search(model, h, simulations=STATE["sims"])
            chosen = int(counts.argmax().item())
            searched = [round(v, 4) for v in values.tolist()]

    return {
        "value": round(value, 4),
        "stance": chosen,
        "policyStance": greedy,
        "policy": [round(p, 4) for p in prior.tolist()],
        "searchValues": searched,
        "disagreed": chosen != greedy,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="ml/commander_net_full.pt")
    ap.add_argument("--port", type=int, default=8766)
    ap.add_argument("--simulations", type=int, default=64)
    ap.add_argument("--no-search", action="store_true")
    args = ap.parse_args()

    import uvicorn

    device = common.device_of()
    path = pathlib.Path(args.model)
    if not path.exists():
        raise SystemExit(f"no model at {path} - train one first")

    blob = torch.load(path, map_location=device, weights_only=False)
    model = common.CommanderNet(use_aux=True, use_policy=True, use_dynamics=True).to(device)
    model.load_state_dict(blob["state_dict"])
    model.eval()

    STATE.update(model=model, device=device,
                 search=not args.no_search, sims=args.simulations)

    print(f"commander network on {device}; search={'on' if STATE['search'] else 'off'}")
    uvicorn.run(app, host="127.0.0.1", port=args.port, log_level="warning")


if __name__ == "__main__":
    main()
