#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Rebuilds the built app icons from the two masters in src/LizTerm.App/Assets/Icons:
#
#   lizterm.png        the full icon, "3270" lettered above Liz
#   lizterm-small.png  the same tile with no lettering and a larger Liz
#
# The lettering is legible from 64 px up and a green smear below that, so every packaged image of 48 px or less
# comes from lizterm-small.png and every larger one from lizterm.png. It writes lizterm.icns (Parcel's macOS
# AppIcon), lizterm.ico (the Win32 icon resource and the installer's icon), lizterm-256.png (the splash's and
# About's mark, and the README's) and lizterm-window.ico (every window's own icon). lizterm.png itself is Parcel's
# Linux AppIcon and is used as it is.
#
# macOS only, for iconutil. Needs ImageMagick (magick) and python3.
set -euo pipefail
ICONS=$(cd "$(dirname "$0")/../src/LizTerm.App/Assets/Icons" && pwd)
LARGE="$ICONS/lizterm.png"
SMALL="$ICONS/lizterm-small.png"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# resize SOURCE SIZE OUT
resize() {
  magick "$1" -filter Lanczos -resize "$2x$2" -strip "PNG32:$3"
}

# write_ico OUT PNG... writes an .ico directory around the PNGs as they are. ImageMagick writes every entry below
# 256 px as an uncompressed bitmap, three times the size of the same icon with PNG entries, which every Windows
# since Vista reads.
write_ico() {
  python3 - "$@" <<'EOF'
import struct, sys
out, pngs = sys.argv[1], sys.argv[2:]
images = [open(p, "rb").read() for p in pngs]
header = struct.pack("<HHH", 0, 1, len(images))
offset = len(header) + 16 * len(images)
entries = b""
for image in images:
    size = struct.unpack(">I", image[16:20])[0]  # the IHDR width; both masters are square
    edge = 0 if size >= 256 else size  # an .ico directory spells 256 as 0
    entries += struct.pack("<BBBBHHII", edge, edge, 0, 0, 1, 32, len(image), offset)
    offset += len(image)
with open(out, "wb") as f:
    f.write(header + entries + b"".join(images))
EOF
}

for s in 16 24 32 48 64 128 256 512 1024; do
  if [ "$s" -le 48 ]; then src=$SMALL; else src=$LARGE; fi
  resize "$src" "$s" "$WORK/$s.png"
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

write_ico "$ICONS/lizterm.ico" "$WORK"/{16,24,32,48,64,128,256}.png
cp "$WORK/256.png" "$ICONS/lizterm-256.png"

# The window icon is only ever drawn small, so every entry comes from the small master. Avalonia's Win32 backend
# takes the nearest entry to 16 px (title bar) and 24 px (taskbar; 32 px before Windows 10) times the display scale,
# hence the in-between sizes. X11 decodes only the largest entry and caps a window icon at 128 px, hence the top one.
# macOS ignores window icons.
mkdir "$WORK/window"
for s in 16 20 24 32 40 48 64 128; do
  resize "$SMALL" "$s" "$WORK/window/$s.png"
done
write_ico "$ICONS/lizterm-window.ico" "$WORK"/window/{16,20,24,32,40,48,64,128}.png

echo "Wrote lizterm.icns, lizterm.ico, lizterm-256.png and lizterm-window.ico in $ICONS"
