#!/bin/bash
# Build and stage the engine binaries.
#
# MSBuild's copy into bin/ fails on this machine because the repository sits inside a
# Dropbox-synced folder: Dropbox races the delete-then-create that the copy step does,
# so the destination is removed and then cannot be written. Compilation itself is fine.
# This compiles, ignores the copy failure, and stages the outputs with plain cp, which
# Dropbox does not interfere with.
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"

fail=0
for proj in OpenRA.Game OpenRA.Mods.Common OpenRA.Mods.Cnc OpenRA.Utility OpenRA.Server OpenRA.Platforms.Default; do
  [ -d "$proj" ] || continue
  out=$(dotnet build "$proj/$proj.csproj" --no-dependencies -v q --nologo 2>&1)
  if echo "$out" | grep -q ": error CS"; then
    echo "=== $proj FAILED ==="
    echo "$out" | grep ": error CS" | head -8
    fail=1
  fi
  [ -f "$proj/obj/Debug/$proj.dll" ] && cp "$proj/obj/Debug/$proj.dll" bin/ 2>/dev/null
done

[ $fail -eq 0 ] && echo "build ok" || echo "build had compile errors"
exit $fail
