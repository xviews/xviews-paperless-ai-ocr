#!/bin/bash
# Baut "../app/mac/XviewsPaperlessAiOcr-mac-arm64.zip" (Apple-Chip) und "-mac-x64.zip" (Intel),
# dazu je eine .dmg zum Hineinziehen in den Programme-Ordner. Die Update-Suche der App lädt die ZIP.
# Läuft nur auf einem Mac (iconutil, codesign, ditto, hdiutil), sonst über GitHub Actions: .github/workflows/mac.yml
# Braucht das .NET 10 SDK: https://dotnet.microsoft.com/download
set -euo pipefail
cd "$(dirname "$0")"

name="Xviews Paperless AI-OCR"
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' XviewsPaperlessAiOcr.csproj)
out=../app/mac
rm -rf bin/mac "$out"
mkdir -p bin/mac "$out"

# Symbol für Finder und Launchpad aus dem großen Logo
iconset=bin/mac/app.iconset
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size ../assets/logo-512.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  if [ $double -le 512 ]; then
    sips -z $double $double ../assets/logo-512.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  fi
done
iconutil -c icns "$iconset" -o bin/mac/app.icns

for arch in arm64 x64; do
  publish=bin/mac/publish-$arch
  dotnet publish XviewsPaperlessAiOcr.csproj -c Release -r osx-$arch -o "$publish" --nologo

  app="bin/mac/$arch/$name.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish/." "$app/Contents/MacOS/"
  sed "s/@VERSION@/$version/g" mac/Info.plist > "$app/Contents/Info.plist"
  cp bin/mac/app.icns "$app/Contents/Resources/"

  # Ohne Apple-Entwicklerkonto: Ad-hoc-Signatur. Apple-Chips starten nur signierten Code,
  # und eine stimmige Signatur verhindert die Meldung "App ist beschädigt".
  codesign --force --deep --sign - "$app"
  codesign --verify --deep --strict "$app"

  ditto -c -k --keepParent "$app" "$out/XviewsPaperlessAiOcr-mac-$arch.zip"

  # Disk-Image: die App und ein Verweis auf den Programme-Ordner, damit man sie hinüberziehen kann
  stage=bin/mac/dmg-$arch
  mkdir -p "$stage"
  cp -R "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  hdiutil create -volname "$name" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$out/XviewsPaperlessAiOcr-mac-$arch.dmg" >/dev/null

  echo "Gebaut: $out/XviewsPaperlessAiOcr-mac-$arch.zip und .dmg ($version)"
done
