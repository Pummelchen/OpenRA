#!/usr/bin/env python3
"""Self-play batch evaluation for the OpenRA AI mod.

Runs headless skirmishes via the --simulate utility command across seeds
(and optionally parameter configurations) and aggregates the outcomes:
winners, game-overs, and the AI event counts from the match telemetry.

Usage (run from the repo root):
  ai/selfplay.py --map mods/ra/maps/shattered-mountain --bots 4 --teams 2 --ticks 6000 --runs 4
  ai/selfplay.py --map <uid> --runs 6 --seed-base 100   # seeds 100..105
  ai/selfplay.py --sweep-reserve 4,6,8 --runs 3          # reserve fraction grid
  ai/selfplay.py --maps a,b,c --runs 4                   # cross-map overfitting check
"""

import argparse
import os
import re
import statistics
import subprocess
import sys

if sys.version_info < (3, 11):
    sys.exit("Python 3.11 or newer is required (found %d.%d)." % (sys.version_info[0], sys.version_info[1]))

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AI_YAML = os.path.join(REPO, "mods", "ra", "rules", "ai.yaml")


def run_sim(map_arg: str, bots: int, teams: int, ticks: int, seed: int, bot_types=None, intelligence=None) -> dict:
    map_arg = os.path.abspath(map_arg)
    bot_spec = f'BOT_TYPES={",".join(bot_types)}' if bot_types else f'BOTS={bots}'
    intel_spec = f'INTELLIGENCE={intelligence}' if intelligence is not None else ''
    cmd = [
        "bash", "-lc",
        f'cd "{REPO}/mods/ra" && PATH="$HOME/.dotnet:$PATH" '
        f'../../utility.sh ra --simulate MAP="{map_arg}" {bot_spec} TEAMS={teams} TICKS={ticks} SEED={seed} {intel_spec}'.rstrip(),
    ]
    out = subprocess.run(cmd, capture_output=True, text=True, timeout=1200).stdout

    result = {"seed": seed, "game_over": False, "winners": [], "events": {}}
    m = re.search(r"Finished: (\d+) ticks, (game over|time limit reached), (\d+) actors", out)
    if m:
        result["ticks"] = int(m.group(1))
        result["game_over"] = m.group(2) == "game over"
        result["actors"] = int(m.group(3))
    w = re.search(r"Winners: (.+)", out)
    if w:
        result["winners"] = [x.strip() for x in w.group(1).split(",")]

    # The commander's own predicted win ratio, from the last match-metrics sample.
    ratios = re.findall(r"predicted win ratio (\d+\.\d+)", out)
    result["predicted_win_ratio"] = float(ratios[-1]) if ratios else None

    # Final combat exchange (enemy value destroyed / friendly value lost), from the last metrics.
    exchanges = re.findall(r"exchange [\d.]+ \(enemy (\d+) / friendly (\d+) lost\)", out)
    if exchanges:
        enemy_destroyed, friendly_lost = int(exchanges[-1][0]), int(exchanges[-1][1])
        result["enemy_destroyed"] = enemy_destroyed
        result["friendly_lost"] = friendly_lost
        result["exchange"] = enemy_destroyed / max(1, friendly_lost)

    # Map client names to teams so a head-to-head can attribute the winner.
    name_to_team = {}
    for line in out.splitlines():
        m = re.search(r"team (\d+)\s+faction\s+\S+\s+(.+)$", line)
        if m:
            name_to_team[m.group(2).strip()] = int(m.group(1))
    result["winner_teams"] = sorted({name_to_team[n] for n in result["winners"] if n in name_to_team})

    # Ground-truth kill/death cost per team (fog-independent, from PlayerStatistics).
    kill_costs = {}
    death_costs = {}
    for line in out.splitlines():
        m = re.search(r"team (\d+).*kills_cost=(\d+)\s+deaths_cost=(\d+)", line)
        if m:
            team, kills, deaths = int(m.group(1)), int(m.group(2)), int(m.group(3))
            kill_costs[team] = kill_costs.get(team, 0) + kills
            death_costs[team] = death_costs.get(team, 0) + deaths
    result["kill_costs"] = kill_costs
    result["death_costs"] = death_costs

    in_events = False
    for line in out.splitlines():
        if "Match telemetry:" in line:
            in_events = True
            continue
        if in_events and re.match(r"\s+[a-z_]+\s+\d+", line):
            key, value = line.split()
            result["events"][key] = int(value)

    return result


def set_ai_param(pattern: str, value: str) -> None:
    """Patches a scalar YAML field matching the given capture group (restored on exit)."""
    with open(AI_YAML, encoding="utf-8") as f:
        content = f.read()
    content = re.sub(pattern, rf"\g<1>{value}", content)
    with open(AI_YAML, "w", encoding="utf-8") as f:
        f.write(content)


def set_reserve(fraction: int) -> None:
    """Patches ReserveFraction in ai.yaml (restored on exit)."""
    set_ai_param(r"(ReserveFraction:\s*)\d+", str(fraction))


def set_retreat(precision: int) -> None:
    """Patches MicroPrecision (0-3), which drives the retreat threshold via difficulty."""
    set_ai_param(r"(MicroPrecision:\s*)-?\d+", str(precision))


def set_coordinated(minimum: int) -> None:
    set_ai_param(r"(CoordinatedAttackMinimum:\s*)\d+", str(minimum))


def set_feint(fraction: int) -> None:
    """Patches FeintFraction in ai.yaml (restored on exit)."""
    set_ai_param(r"(FeintFraction:\s*)\d+", str(fraction))


def set_threat(scale: float) -> None:
    """Sets the THREAT_WEIGHT_SCALE env var for the simulation (req 719).

    The C# ComputeThreats method reads this env var to scale all threat values.
    Unlike YAML patches, env vars are set per-process so no restore is needed.
    """
    os.environ["THREAT_WEIGHT_SCALE"] = str(scale)
set_threat._env_var = "THREAT_WEIGHT_SCALE"


def set_target(profile: str) -> None:
    """Sets the TARGET_WEIGHT_PROFILE env var for the simulation (req 723).

    The C# PostureSelection.TargetWeightsFor reads this env var to override
    the target-scoring profile. Values: "balanced", "breakthrough", "raiding".
    """
    os.environ["TARGET_WEIGHT_PROFILE"] = profile
set_target._env_var = "TARGET_WEIGHT_PROFILE"


def set_specialops(threshold: float) -> None:
    """Sets the SPECIALOPS_RISK_THRESHOLD env var for the simulation (req 725).

    The C# SpecialOpsTarget reads this env var to cap the maximum acceptable
    risk for special-operations insertions.
    """
    os.environ["SPECIALOPS_RISK_THRESHOLD"] = str(threshold)
set_specialops._env_var = "SPECIALOPS_RISK_THRESHOLD"


def set_capability(scale: float) -> None:
    """Sets the PRODUCTION_CAPABILITY_WEIGHT_SCALE env var for the simulation (req 722).

    The C# ProductionContract.CapabilityWeightScale reads this env var to scale the
    capability threat profile that drives production contracts.
    """
    os.environ["PRODUCTION_CAPABILITY_WEIGHT_SCALE"] = str(scale)
set_capability._env_var = "PRODUCTION_CAPABILITY_WEIGHT_SCALE"


def summarize_head_to_head(label: str, results: list) -> None:
    """Reports the coalition's decisive result and ground-truth exchange vs a scripted opponent."""
    coalition_wins = opponent_wins = stalemates = 0
    ground_truths = []
    ratios = []
    for r in results:
        winner_teams = r.get("winner_teams", [])
        if 1 in winner_teams:
            coalition_wins += 1
        elif 2 in winner_teams:
            opponent_wins += 1
        else:
            stalemates += 1
        kills = r.get("kill_costs", {}).get(1, 0)
        deaths = r.get("death_costs", {}).get(1, 0)
        if deaths > 0:
            ground_truths.append(kills / deaths)
        if r.get("predicted_win_ratio") is not None:
            ratios.append(r["predicted_win_ratio"])

    mean_truth = statistics.mean(ground_truths) if ground_truths else float("nan")
    mean_ratio = statistics.mean(ratios) if ratios else float("nan")
    print(f"  {label}: W {coalition_wins}/L {opponent_wins}/D {stalemates}, "
          f"ground-truth exchange {mean_truth:.2f}, predicted {mean_ratio:.2f}")


def run_sweep(label: str, setter, values: list, args) -> None:
    original = open(AI_YAML, encoding="utf-8").read()
    # Track env vars that need clearing (for env-var-based sweeps like threat/target/specialops).
    env_vars_used = []
    if hasattr(setter, "_env_var"):
        env_vars_used.append(setter._env_var)
    try:
        for value in values:
            setter(value)
            if args.vs:
                # Tune against a scripted opponent so the games resolve; otherwise symmetric
                # self-play just stalemates and yields no win-rate signal.
                results = [run_sim(args.map, 2, 2, args.ticks, args.seed_base + i,
                                   bot_types=["ai", args.vs, "ai", args.vs], intelligence=args.intelligence)
                           for i in range(args.runs)]
                summarize_head_to_head(f"{label} {value}", results)
            else:
                results = [run_sim(args.map, args.bots, args.teams, args.ticks, args.seed_base + i)
                           for i in range(args.runs)]
                summarize(f"{label} {value}", results)
    finally:
        with open(AI_YAML, "w", encoding="utf-8") as f:
            f.write(original)
        # Clear any env vars set by the sweep functions.
        for var in env_vars_used:
            os.environ.pop(var, None)
        print("\n(ai.yaml restored)")


def run_cross_map(maps: list, args) -> None:
    """Runs one configuration across several maps and flags map-specific overfitting.

    A configuration that wins on one map but loses everywhere else is overfit to that
    map, so the cross-map win-rate spread is reported as an overfitting signal.
    """
    per_map = {}
    for map_path in maps:
        results = [run_sim(map_path, args.bots, args.teams, args.ticks, args.seed_base + i)
                   for i in range(args.runs)]
        per_map[map_path] = results
        summarize(f"map {os.path.basename(map_path)}", results)

    win_rates = {m: (sum(1 for r in rs if r["winners"]) / len(rs)) for m, rs in per_map.items()}
    print("\n=== cross-map win rates ===")
    for m, rate in sorted(win_rates.items(), key=lambda kv: -kv[1]):
        print(f"  {os.path.basename(m)}: {rate:.0%}")

    rates = list(win_rates.values())
    spread = max(rates) - min(rates) if rates else 0
    print(f"cross-map spread: {spread:.0%} (lower = less overfit to any one map)")
    if len(rates) >= 2 and spread >= 0.5:
        best = max(win_rates, key=win_rates.get)
        print(f"WARNING: overfit risk — best result is map-specific ({os.path.basename(best)}).")


def run_combat_accuracy(args) -> None:
    """Correlates the commander's predicted win ratio with the actual match outcome.

    A useful combat estimator predicts higher win ratios for games it wins. This reports the
    mean predicted ratio for won vs lost games as a coarse accuracy signal (a real accuracy
    benchmark needs recorded per-engagement outcomes, which the replay harness can add later).
    """
    results = [run_sim(args.map, args.bots, args.teams, args.ticks, args.seed_base + i)
               for i in range(args.runs)]

    def mean_ratio(rs):
        vals = [r["predicted_win_ratio"] for r in rs if r["predicted_win_ratio"] is not None]
        return statistics.mean(vals) if vals else float("nan")

    won = [r for r in results if r["winners"]]
    lost = [r for r in results if not r["winners"]]

    print(f"\n=== combat-estimator accuracy: {len(results)} runs ===")
    print(f"wins: {len(won)}, losses: {len(lost)}")
    print(f"mean predicted win ratio when WON: {mean_ratio(won):.2f}")
    print(f"mean predicted win ratio when LOST: {mean_ratio(lost):.2f}")
    if mean_ratio(won) > mean_ratio(lost):
        print("estimator discriminates wins from losses (higher prediction when winning)")
    else:
        print("WARNING: estimator does not discriminate wins from losses")


def run_head_to_head(opponents: list, args) -> None:
    """Coalition "ai" vs each scripted opponent (1v1), reporting the coalition's combat results.

    The coalition bot is always team 1 and the scripted opponent team 2. Reports the decisive
    result distribution (coalition wins / opponent wins / stalemates) and both the fog-limited
    exchange (from the commander's telemetry) and the ground-truth exchange (from PlayerStatistics,
    which counts every kill regardless of fog).
    """
    print(f"\n=== head-to-head: coalition 'ai' vs scripted bots ({args.runs} runs each) ===")
    for opponent in opponents:
        coalition_wins = 0
        opponent_wins = 0
        stalemates = 0
        exchanges = []
        ratios = []
        ground_truths = []
        for i in range(args.runs):
            result = run_sim(args.map, 2, 2, args.ticks, args.seed_base + i,
                             bot_types=["ai", opponent], intelligence=args.intelligence)
            winner_teams = result.get("winner_teams", [])
            if 1 in winner_teams:
                coalition_wins += 1
            elif 2 in winner_teams:
                opponent_wins += 1
            else:
                stalemates += 1
            if "exchange" in result:
                exchanges.append(result["exchange"])
            if result.get("predicted_win_ratio") is not None:
                ratios.append(result["predicted_win_ratio"])
            kills = result.get("kill_costs", {}).get(1, 0)
            deaths = result.get("death_costs", {}).get(1, 0)
            if deaths > 0:
                ground_truths.append(kills / deaths)

        mean_exchange = statistics.mean(exchanges) if exchanges else float("nan")
        mean_ratio = statistics.mean(ratios) if ratios else float("nan")
        mean_truth = statistics.mean(ground_truths) if ground_truths else float("nan")
        print(f"  ai vs {opponent}: W {coalition_wins}/L {opponent_wins}/D {stalemates}, "
              f"fog exchange {mean_exchange:.2f}, ground-truth exchange {mean_truth:.2f}, "
              f"predicted ratio {mean_ratio:.2f}")


def summarize(label: str, results: list) -> None:
    wins = sum(1 for r in results if r["winners"])
    over = sum(1 for r in results if r["game_over"])
    events = {}
    for r in results:
        for k, v in r["events"].items():
            events[k] = events.get(k, 0) + v

    print(f"\n=== {label}: {len(results)} runs ===")
    print(f"wins: {wins}, game overs: {over}, avg actors: {statistics.mean(r.get('actors', 0) for r in results):.0f}")
    if events:
        print("total events: " + ", ".join(f"{k}={v}" for k, v in sorted(events.items(), key=lambda x: -x[1])))


def main() -> None:
    parser = argparse.ArgumentParser(description="Headless self-play evaluation")
    parser.add_argument("--map", default="mods/ra/maps/shattered-mountain")
    parser.add_argument("--maps", help="comma-separated map paths for cross-map (overfitting) evaluation")
    parser.add_argument("--combat-accuracy", action="store_true",
                        help="correlate predicted win ratio with actual outcomes across runs")
    parser.add_argument("--vs", help="comma-separated scripted bot types to fight head-to-head, e.g. rush,turtle,naval")
    parser.add_argument("--intelligence", type=int, default=None,
                        help="override the coalition commander's fog advantage (0 = fair fog, 3 = omniscient)")
    parser.add_argument("--bots", type=int, default=4)
    parser.add_argument("--teams", type=int, default=2)
    parser.add_argument("--ticks", type=int, default=6000)
    parser.add_argument("--runs", type=int, default=4)
    parser.add_argument("--seed-base", type=int, default=1000)
    parser.add_argument("--sweep-reserve", help="comma-separated reserve fractions to compare, e.g. 4,6,8")
    parser.add_argument("--sweep-retreat", help="comma-separated micro-precision values (0-3, lower = retreat later), e.g. 0,1,2,3")
    parser.add_argument("--sweep-coordinated", help="comma-separated coordinated-attack minimums, e.g. 18,24,30")
    parser.add_argument("--sweep-threat", help="comma-separated threat-weight scale factors, e.g. 0.5,1.0,1.5 (req 719)")
    parser.add_argument("--sweep-target", help="comma-separated target-scoring profiles, e.g. balanced,breakthrough,raiding (req 723)")
    parser.add_argument("--sweep-feint", help="comma-separated feint fractions to compare, e.g. 4,6,8 (req 724)")
    parser.add_argument("--sweep-specialops", help="comma-separated special-ops risk thresholds, e.g. 1.0,2.0,3.0 (req 725)")
    parser.add_argument("--sweep-capability", help="comma-separated production capability weight scales, e.g. 0.5,1.0,1.5 (req 722)")
    args = parser.parse_args()

    sweeps = [
        (args.sweep_reserve, "reserve 1/", set_reserve, int),
        (args.sweep_retreat, "micro precision", set_retreat, int),
        (args.sweep_coordinated, "coordinated min", set_coordinated, int),
        (args.sweep_threat, "threat scale", set_threat, float),
        (args.sweep_target, "target profile", set_target, str),
        (args.sweep_feint, "feint fraction", set_feint, int),
        (args.sweep_specialops, "specialops threshold", set_specialops, float),
        (args.sweep_capability, "capability weight", set_capability, float),
    ]
    for raw, label, setter, cast in sweeps:
        if raw:
            run_sweep(label, setter, [cast(x) for x in raw.split(",")], args)
            return

    if args.maps:
        run_cross_map([m.strip() for m in args.maps.split(",") if m.strip()], args)
        return

    if args.combat_accuracy:
        run_combat_accuracy(args)
        return

    if args.vs:
        run_head_to_head([o.strip() for o in args.vs.split(",") if o.strip()], args)
        return

    results = [run_sim(args.map, args.bots, args.teams, args.ticks, args.seed_base + i)
               for i in range(args.runs)]
    summarize("self-play", results)


if __name__ == "__main__":
    main()
