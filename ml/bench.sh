#!/bin/bash
# Fair-economy benchmark: no cheat income, no free tech, no starting cash.
#
# Six seeds per matchup rather than three. The three-seed version could not resolve a difference
# below roughly +/-0.1 exchange ratio, and several results this project acted on sat inside that
# band - including at least one that was later shown to be noise.
#
# The cheat flags live in ai.yaml and the test suite depends on the shipped values, so this toggles
# them and restores them on any exit. Leaving them off silently breaks tests that have nothing to do
# with the benchmark, which cost an afternoon of misdiagnosis once already.
set -u
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

# One runner at a time. Two overlapping runs share one output file and one ai.yaml, and the
# result is a results file with duplicate rows and a config left half-restored - which happened,
# and produced a benchmark that could not be trusted either way.
LOCK=.bench.lock
if ! mkdir "$LOCK" 2>/dev/null; then
  echo "A benchmark is already running (remove $LOCK if it is not)." >&2
  exit 1
fi

YAML=mods/ra/rules/ai.yaml
BACKUP=$(mktemp)
cp "$YAML" "$BACKUP"
restore() { cp "$BACKUP" "$YAML"; rm -f "$BACKUP" /tmp/bench.$$; rmdir "$LOCK" 2>/dev/null; }
trap restore EXIT INT TERM

sed -i '' \
  -e 's/^\(\t*\)InstantBuild: true/\1InstantBuild: false/' \
  -e 's/^\(\t*\)BuildAnywhere: true/\1BuildAnywhere: false/' \
  -e 's/^\(\t*\)UnlimitedPower: true/\1UnlimitedPower: false/' \
  -e 's/^\(\t*\)AllTech: true/\1AllTech: false/' \
  -e 's/^\(\t*\)CashPerInterval: 2000/\1CashPerInterval: 0/' \
  "$YAML"

OUT=${OUT:-bench.txt}
SEEDS=${SEEDS:-"808 809 810 811 812 813"}
MAP=${MAP:-shattered-mountain}
: > "$OUT"
for opp in turtle rush normal naval; do
  for seed in $SEEDS; do
    ./utility.sh ra --simulate MAP=$MAP BOTS=2 TEAMS=2 TICKS=30000 \
      SEED=$seed BOT_TYPES=ai,$opp START_CASH=0 >/tmp/bench.$$ 2>&1
    k=$(grep "team 1" /tmp/bench.$$ | grep -oE 'buildings_killed=[0-9]+' | cut -d= -f2)
    l=$(grep "team 2" /tmp/bench.$$ | grep -oE 'buildings_killed=[0-9]+' | cut -d= -f2)
    echo "$opp/$seed killed=${k:-0} lost=${l:-0}" >> "$OUT"
  done
done

awk -F'[= ]' '/killed/{k+=$3;l+=$5}END{printf "TOTAL killed=%d lost=%d exchange=%.3f over %d matches\n",k,l,(l?k/l:0),NR}' "$OUT" | tee -a "$OUT"
