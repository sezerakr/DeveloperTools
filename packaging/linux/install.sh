#!/usr/bin/env sh
# Installs DeveloperTools for the current user (~/.local).
set -e
here="$(cd "$(dirname "$0")" && pwd)"
dest="$HOME/.local/share/developertools"
mkdir -p "$dest" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp -r "$here"/DeveloperTools "$here"/*.so "$dest"/
chmod +x "$dest/DeveloperTools"
ln -sf "$dest/DeveloperTools" "$HOME/.local/bin/DeveloperTools"
cp "$here/developertools.png" "$HOME/.local/share/icons/hicolor/256x256/apps/developertools.png"
sed "s|^Exec=.*|Exec=$dest/DeveloperTools|" "$here/developertools.desktop" > "$HOME/.local/share/applications/developertools.desktop"
echo "Installed. Start it from your app menu or run: DeveloperTools"