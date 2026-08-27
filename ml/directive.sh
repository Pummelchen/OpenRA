#!/bin/bash
# Which fields of the chief's directive does the game actually read?
#
# Same method as channels.sh, applied to the directive instead of the intents: flatten one field
# before any consumer sees it, run one match, compare. Identical outcome means nobody read it.
# This is what ReserveFraction would have failed for months, had it been asked.
set -u
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
YAML=mods/ra/rules/ai.yaml
BACKUP=$(mktemp); cp "$YAML" "$BACKUP"
trap 'cp "$BACKUP" "$YAML"; rm -f "$BACKUP" /tmp/dir.$$' EXIT INT TERM
sed -i '' -e 's/^\(\t*\)InstantBuild: true/\1InstantBuild: false/' \
  -e 's/^\(\t*\)BuildAnywhere: true/\1BuildAnywhere: false/' \
  -e 's/^\(\t*\)UnlimitedPower: true/\1UnlimitedPower: false/' \
  -e 's/^\(\t*\)AllTech: true/\1AllTech: false/' \
  -e 's/^\(\t*\)CashPerInterval: 2000/\1CashPerInterval: 0/' "$YAML"

run() {
  OPENRA_PERTURB_DIRECTIVE="$1" ./utility.sh ra --simulate MAP=shattered-mountain BOTS=2 TEAMS=2 \
    TICKS=30000 SEED=808 BOT_TYPES=ai,rush START_CASH=0 > /tmp/dir.$$ 2>&1
  grep "team 1" /tmp/dir.$$ | grep -oE "kills_cost=[0-9]+ deaths_cost=[0-9]+ structures=[0-9]+"
}

BASE=$(run "")
printf "%-28s %s\n" "control" "$BASE"
for f in Stance MainEffortRegion FeintRegion ReserveFraction AuthoriseSpecialOperations; do
  R=$(run "$f")
  if [ "$R" = "$BASE" ]; then V="NOT READ"; else V="read"; fi
  printf "%-28s %-9s %s\n" "-$f" "$V" "$R"
done
