#!/bin/bash
# Which of the staff's decisions actually reach the game?
#
# Sever one channel, run ONE match, compare the outcome to the untouched run. The simulation is
# deterministic, so an identical result means the commander played exactly the same game without
# that kind of decision - which means the decision was never reaching the game.
#
# One match, not twenty-four: this asks whether anything changed at all, not whether it improved.
set -u
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
YAML=mods/ra/rules/ai.yaml
BACKUP=$(mktemp); cp "$YAML" "$BACKUP"
trap 'cp "$BACKUP" "$YAML"; rm -f "$BACKUP"' EXIT INT TERM
sed -i '' -e 's/^\(\t*\)InstantBuild: true/\1InstantBuild: false/' \
  -e 's/^\(\t*\)BuildAnywhere: true/\1BuildAnywhere: false/' \
  -e 's/^\(\t*\)UnlimitedPower: true/\1UnlimitedPower: false/' \
  -e 's/^\(\t*\)AllTech: true/\1AllTech: false/' \
  -e 's/^\(\t*\)CashPerInterval: 2000/\1CashPerInterval: 0/' "$YAML"

run() {  # one match -> outcome fingerprint, plus how many intents were actually severed
  OPENRA_SUPPRESS_INTENTS="$1" ./utility.sh ra --simulate MAP=shattered-mountain BOTS=2 TEAMS=2 \
    TICKS=20000 SEED=808 BOT_TYPES=ai,normal START_CASH=0 > /tmp/ch.$$ 2>&1
  FP=$(grep "team 1" /tmp/ch.$$ | grep -oE "kills_cost=[0-9]+ deaths_cost=[0-9]+ structures=[0-9]+")
  N=$(grep -oE "SUPPRESSED [A-Za-z]+ #[0-9]+" /tmp/ch.$$ | grep -oE "#[0-9]+" | tr -d '#' \
    | sort -n | tail -1)
  echo "${N:-0}|$FP"
}

BASE=$(run "" | cut -d'|' -f2)
printf "%-22s %s\n" "control" "$BASE"
for ch in RepairIntent RelocateIntent SetAttackModeIntent EscortIntent CovertTransitIntent ProduceUnitIntent; do
  OUT=$(run "$ch"); N=$(echo "$OUT" | cut -d'|' -f1); FP=$(echo "$OUT" | cut -d'|' -f2)
  if [ "$N" = "0" ]; then V="NOT EXERCISED (none emitted)"
  elif [ "$FP" = "$BASE" ]; then V="DEAD ($N severed, no effect)"
  else V="live ($N severed)"; fi
  printf "%-22s %-30s %s\n" "-$ch" "$V" "$FP"
done
rm -f /tmp/ch.$$
