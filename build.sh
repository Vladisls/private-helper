#!/usr/bin/env bash
# Build a release: bump version, run tests, build the Windows exe, zip the OCR package, write release/version.txt.
# Usage: ./build.sh            (bumps the patch number, e.g. 2.1.0 -> 2.1.1)
#        ./build.sh 2.2.0      (sets an explicit version)
# Then commit release/ + src/ + ocr/ and push to main. Helpers update themselves (and their ocr\ folder) on next start.
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

# OCR package: ocr/ (Tesseract DLLs + language data) zipped as release/ocr.zip, unpacked into ocr\ next to the exe
# by the updater. The zip is reproducible (sorted entries, fixed times and permissions, no extra attributes), so an
# unchanged ocr/ gives the same SHA-256 and helpers don't download it again.
make_ocr_zip() {   # make_ocr_zip <ocr dir> <zip path>
  local src=$1 out=$2 tmp
  tmp=$(mktemp -d)
  cp -R "$src" "$tmp/ocr"
  rm -f "$tmp/ocr/ocr.sha"; find "$tmp/ocr" -name .DS_Store -delete
  find "$tmp/ocr" -type d -exec chmod 755 {} +
  find "$tmp/ocr" -type f -exec chmod 644 {} +
  TZ=UTC find "$tmp/ocr" -exec touch -t 202601010000 {} +
  rm -f "$out"
  (cd "$tmp/ocr" && find . -type f | sed 's:^\./::' | LC_ALL=C sort | TZ=UTC zip -X -D -q -9 "$out" -@)
  rm -rf "$tmp"
}
echo "== OCR package"
make_ocr_zip "$PWD/ocr" "$PWD/release/ocr.zip"
ocrsha=$(shasum -a 256 release/ocr.zip | cut -d' ' -f1)
printf '%s\n%s\n%s\n' "$new" "$sha" "$ocrsha" > release/version.txt
echo "== release/version.txt"; cat release/version.txt
