#!/bin/bash
# One fair-economy match, cheats off, config restored on exit. For diagnosis, not measurement.
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
./utility.sh ra --simulate MAP=${MAP:-shattered-mountain} BOTS=2 TEAMS=2 \
  TICKS=${TICKS:-30000} SEED=${SEED:-808} BOT_TYPES=ai,${OPP:-rush} START_CASH=0
