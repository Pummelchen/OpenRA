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

import json
import http.server

import torch

import common
import train_stage4

STATE = {"model": None, "device": None, "search": True, "sims": 64, "support": 0.3}


def health():
    return {"ok": STATE["model"] is not None,
            "device": str(STATE["device"]),
            "search": STATE["search"]}


def evaluate(position):
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
        "entities": position.get("entities", []),
        "regions": position.get("regions", []),
        "globals": position.get("globals") or [0.0] * common.GLOBAL_FIELDS,
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

        # Q first when the model has it. Predicting the outcome conditioned on the action
        # is what the latent dynamics model failed to do - it learned to ignore the action,
        # because against a 288-dimensional vector the action's effect is a rounding error.
        # Q asks only for the part that varies, and the data says that part is worth
        # 0.2-0.8 standard deviations depending on the outcome measured.
        if getattr(model, "use_q", False):
            q, outcome = model.q_values(h)
            qv = torch.sigmoid(q).squeeze(0)

            # Only among actions the data actually supports.
            #
            # Taking a plain argmax over Q was measured and it plays worse than the scripted
            # chief it replaces: 0.88 -> 0.58 exchange ratio over twelve matches. The reason is
            # the standard one for offline value learning. Q is fitted only on the action that
            # was taken, so its estimate for a rarely-taken action is barely constrained by
            # anything - and argmax then seeks out exactly those actions, because an
            # unconstrained estimate is usually an overestimate. Q settled on "Build", which
            # appears in 5% of the data and means never attacking.
            #
            # Restricting the choice to actions the imitation policy gives real probability to
            # is the core of BCQ, and it is the cheapest correct version of the fix: the network
            # may still disagree with the scripted chief, but only about actions it has seen
            # enough of to have an opinion worth acting on.
            support = prior >= STATE["support"] * prior.max()
            masked = torch.where(support, qv, torch.full_like(qv, -1.0))
            chosen = int(masked.argmax().item())
            searched = [round(v, 4) for v in qv.tolist()]
        elif STATE["search"] and model.use_dynamics:
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


class Handler(http.server.BaseHTTPRequestHandler):
    """Plain stdlib HTTP.

    Deliberately no framework: torch lives in one interpreter here and fastapi in
    another, and the server does two things at a cadence of a few requests a minute.
    Adding a dependency to bridge two environments would be a worse trade than
    twenty lines of BaseHTTPRequestHandler.
    """

    def _send(self, payload, status=200):
        body = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.rstrip("/") == "/health":
            self._send(health())
        else:
            self._send({"error": "not found"}, 404)

    def do_POST(self):
        if self.path.rstrip("/") != "/evaluate":
            self._send({"error": "not found"}, 404)
            return
        try:
            length = int(self.headers.get("Content-Length", 0))
            position = json.loads(self.rfile.read(length) or b"{}")
            self._send(evaluate(position))
        except (ValueError, KeyError, RuntimeError) as e:
            # A bad request must not take the server down: the bot falls back to its
            # scripted chief on any failure, and a dead server would make that permanent.
            self._send({"error": str(e)}, 400)

    def log_message(self, *args):
        pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="ml/commander_net_full.pt")
    ap.add_argument("--port", type=int, default=8766)
    ap.add_argument("--simulations", type=int, default=64)
    ap.add_argument("--no-search", action="store_true")
    ap.add_argument("--support", type=float, default=0.3,
                    help="An action is eligible if the policy gives it at least this fraction "
                         "of the most likely action's probability. Lower trusts Q further out "
                         "of distribution; 0 restores the plain argmax that measured worse.")
    args = ap.parse_args()

    device = common.device_of()
    path = pathlib.Path(args.model)
    if not path.exists():
        raise SystemExit(f"no model at {path} - train one first")

    blob = torch.load(path, map_location=device, weights_only=False)
    # Which heads the checkpoint carries decides how it is built.
    keys = blob["state_dict"].keys()
    model = common.CommanderNet(
        use_aux=True, use_policy=True,
        use_dynamics=any(k.startswith("dynamics.") for k in keys),
        use_q=any(k.startswith("q.") for k in keys)).to(device)
    model.load_state_dict(blob["state_dict"])
    model.eval()

    STATE.update(model=model, device=device,
                 search=not args.no_search, sims=args.simulations, support=args.support)

    heads = "Q" if model.use_q else ("search" if model.use_dynamics else "policy")
    print(f"commander network on {device}; deciding by {heads}; port {args.port}", flush=True)
    http.server.ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
