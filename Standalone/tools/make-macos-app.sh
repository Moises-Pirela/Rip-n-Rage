#!/usr/bin/env bash
# Wraps a published macOS build (dist/osx-*) in Rip-n-Rage.app: Contents/MacOS holds the published files (the game
# finds assets/, shaders/, native/ beside its executable), Info.plist opts into the Retina framebuffer and automatic
# GPU switching. Signed ad-hoc so Apple-silicon Macs run it; distribute with a Developer ID signature + notarization.
set -e
SRC="${1:?usage: make-macos-app.sh dist/osx-arm64}"
APP="$SRC/Rip-n-Rage.app"
VERSION="${RIPNRAGE_VERSION:-1.0.0}"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# everything except a previous bundle
for f in "$SRC"/* "$SRC"/.[!.]*; do
  [ -e "$f" ] || continue
  [ "$f" = "$APP" ] && continue
  cp -R "$f" "$APP/Contents/MacOS/"
done

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Rip-n-Rage</string>
  <key>CFBundleDisplayName</key><string>Rip-n-Rage</string>
  <key>CFBundleIdentifier</key><string>com.protomutant.ripnrage</string>
  <key>CFBundleExecutable</key><string>RipNRage</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.action-games</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSSupportsAutomaticGraphicsSwitching</key><true/>
  <key>GCSupportsControllerUserInteraction</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# downloaded / copied dylibs may carry the quarantine flag, which blocks dlopen
xattr -cr "$APP" 2>/dev/null || true
if command -v codesign >/dev/null; then
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1 && echo "Ad-hoc signed $APP" || echo "warning: codesign failed; the app may not launch on Apple silicon" >&2
fi
echo "macOS app bundle: $APP"
