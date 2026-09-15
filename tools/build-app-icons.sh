#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Rebuilds the packaged app icons from the two masters in src/LizTerm.App/Assets/Icons:
#
#   lizterm.png        the full icon, "3270" lettered above Liz
#   lizterm-small.png  the same tile with no lettering and a larger Liz
#
# The lettering is legible from 64 px up and a green smear below that, so every image of 48 px or less comes from
# lizterm-small.png and every larger one from lizterm.png. It writes lizterm.icns (Parcel's macOS AppIcon),
# lizterm.ico (the Win32 icon resource and the installer's icon) and lizterm-256.png (every Window.Icon, the splash,
# About and the README). lizterm.png itself is Parcel's Linux AppIcon and is used as it is.
#
# macOS only, for iconutil. Needs ImageMagick (magick) and python3.
set -euo pipefail
ICONS=$(cd "$(dirname "$0")/../src/LizTerm.App/Assets/Icons" && pwd)
LARGE="$ICONS/lizterm.png"
SMALL="$ICONS/lizterm-small.png"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

for s in 16 24 32 48 64 128 256 512 1024; do
  if [ "$s" -le 48 ]; then src=$SMALL; else src=$LARGE; fi
  magick "$src" -filter Lanczos -resize "${s}x${s}" -strip "PNG32:$WORK/$s.png"
done

# An iconset names images by point size, and an @2x image is twice that in pixels, so icon_16x16@2x is 32 px and
# takes the small master while icon_32x32@2x is 64 px and takes the large one.
set_dir="$WORK/lizterm.iconset"
mkdir "$set_dir"
for pair in 16x16:16 16x16@2x:32 32x32:32 32x32@2x:64 128x128:128 128x128@2x:256 256x256:256 256x256@2x:512 \
            512x512:512 512x512@2x:1024; do
  cp "$WORK/${pair#*:}.png" "$set_dir/icon_${pair%%:*}.png"
done
iconutil -c icns "$set_dir" -o "$ICONS/lizterm.icns"

# ImageMagick writes every .ico entry below 256 px as an uncompressed bitmap, three times the size of the same icon
# with PNG entries, which every Windows since Vista reads. So the directory is written here, around the PNGs as
# they are.
python3 - "$WORK" "$ICONS/lizterm.ico" <<'EOF'
import struct, sys
work, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
images = [open(f"{work}/{s}.png", "rb").read() for s in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = len(header) + 16 * len(sizes)
entries = b""
for size, image in zip(sizes, images):
    edge = 0 if size >= 256 else size  # an .ico directory spells 256 as 0
    entries += struct.pack("<BBBBHHII", edge, edge, 0, 0, 1, 32, len(image), offset)
    offset += len(image)
with open(out, "wb") as f:
    f.write(header + entries + b"".join(images))
EOF

cp "$WORK/256.png" "$ICONS/lizterm-256.png"
echo "Wrote lizterm.icns, lizterm.ico and lizterm-256.png in $ICONS"
