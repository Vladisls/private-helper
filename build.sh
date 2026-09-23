#!/usr/bin/env bash
# Build a release: bump version, run tests, build the Windows exe, write release/version.txt.
# Usage: ./build.sh            (bumps the patch number, e.g. 2.1.0 -> 2.1.1)
#        ./build.sh 2.2.0      (sets an explicit version)
# Then commit release/ + src/ and push to main. Helpers update themselves on next start.
set -euo pipefail
cd "$(dirname "$0")"
DOTNET=$(command -v dotnet || echo /usr/local/share/dotnet/dotnet)

current=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/CAHelper.csproj)
if [ $# -ge 1 ]; then new=$1; else
  IFS=. read -r ma mi pa <<< "$current"; new="$ma.$mi.$((pa + 1))"
fi

echo "== tests"
(cd test && "$DOTNET" run) | tail -1 | grep -q "ALL PASS" || { echo "tests failed"; (cd test && "$DOTNET" run); exit 1; }
rm -rf test/bin test/obj

echo "== version $current -> $new"
sed -i.bak "s#<Version>$current</Version>#<Version>$new</Version>#" src/CAHelper.csproj && rm src/CAHelper.csproj.bak

echo "== build"
rm -rf src/bin src/obj
(cd src && "$DOTNET" build -c Release -nologo -v q)
mkdir -p release
cp src/bin/Release/net48/CabalHelper.exe release/CabalHelper.exe
sha=$(shasum -a 256 release/CabalHelper.exe | cut -d' ' -f1)
printf '%s\n%s\n' "$new" "$sha" > release/version.txt
echo "== release/version.txt"; cat release/version.txt
