#!/usr/bin/env bash
# Builds self-contained, shareable copies of Angels (no .NET install needed on the friend's machine)
# and zips each into dist/. Run from the repo root:  ./publish.sh            (all platforms)
#                                                    ./publish.sh osx-arm64  (just one)
set -euo pipefail
cd "$(dirname "$0")"

PROJ="Angels_Proj/Angels_Proj/Angels_Proj.csproj"
RIDS=("$@")
[ ${#RIDS[@]} -eq 0 ] && RIDS=(osx-arm64 osx-x64 win-x64)

rm -rf dist && mkdir -p dist
for rid in "${RIDS[@]}"; do
  out="dist/Angels-$rid"
  echo "==> $rid"
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true -o "$out"
  [ "${rid%%-*}" = "osx" ] && chmod +x "$out/Angels_Proj" 2>/dev/null || true
  (cd dist && zip -qr "Angels-$rid.zip" "Angels-$rid")
done
echo; echo "Done. Zips:"; ls -lh dist/*.zip
