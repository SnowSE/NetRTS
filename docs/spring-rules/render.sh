#!/usr/bin/env bash
# Renders each spring-rules-*.html here to a PNG of the same name, at twice its size (sharp on any screen).
# Needs Google Chrome or Chromium; fonts come from Google Fonts, so it needs the network.
#   docs/spring-rules/render.sh
set -euo pipefail
cd "$(dirname "$0")"
chrome=$(command -v google-chrome || command -v chromium || command -v chromium-browser) || { echo "No Chrome or Chromium found." >&2; exit 1; }

for html in spring-rules-*.html; do
  # The poster's own size, from the root element's style: "width: 1080px; height: 1350px".
  size=$(grep -m1 -o 'width: [0-9]*px; height: [0-9]*px' "$html" | sed -E 's/width: ([0-9]+)px; height: ([0-9]+)px/\1,\2/')
  "$chrome" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 \
    --virtual-time-budget=8000 --window-size="$size" --screenshot="$PWD/${html%.html}.png" "file://$PWD/$html" 2>/dev/null
  echo "${html%.html}.png ($size at 2x)"
done
