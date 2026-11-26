#!/usr/bin/env sh
# Removes DeveloperTools (asks whether to keep your settings).
dest="$HOME/.local/share/developertools"
if [ -x "$dest/DeveloperTools" ]; then
  exec "$dest/DeveloperTools" --uninstall
fi
here="$(cd "$(dirname "$0")" && pwd)"
exec "$here/DeveloperTools" --uninstall