#!/bin/bash
# Parallel self-play data generation. Each worker is its own process, so each writes
# its own state log; the trainer globs them back together.
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
WORKERS=${WORKERS:-6}
SEEDS_PER=${SEEDS_PER:-13}
MAPS=${MAPS:-"shattered-mountain chernobyl snow-town code-19"}
OPPS=${OPPS:-"turtle rush normal naval"}
TICKS=${TICKS:-30000}

run_worker() {
  local w=$1 i=0
  for map in $MAPS; do
    for opp in $OPPS; do
      for ((s=0; s<SEEDS_PER; s++)); do
        i=$((i+1))
        [ $(( i % WORKERS )) -ne "$w" ] && continue
        seed=$(( 900 + s * 7 + w * 101 ))
        ./utility.sh ra --simulate MAP=$map BOTS=2 TEAMS=2 TICKS=$TICKS \
          SEED=$seed BOT_TYPES=ai,$opp START_CASH=0 >/dev/null 2>&1
      done
    done
  done
}

for ((w=0; w<WORKERS; w++)); do run_worker "$w" & done
wait
echo "generation complete: $(cat commander-states.*.jsonl 2>/dev/null | wc -l) samples"
