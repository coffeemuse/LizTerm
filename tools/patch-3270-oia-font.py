#!/usr/bin/env python3
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause
"""Give the 3270 font's x3270 OIA glyphs private-use code points.

Upstream 3270font carries the Operator Information Area symbols (the boxed 4, the underlined A and B, the jagged
no-connection wire, the clock, the human, the lock X, ...) as glyphs with no code point at all, so no text can
reach them. This maps them to U+E180 onward, in the font's own glyph order, which is the block
src/LizTerm.App/Status/OiaGlyphs.cs spells out and tests/LizTerm.App.Tests/Status/OiaGlyphsTests.cs pins.

Run it on a fresh upstream file after a font refresh:

    pip install fonttools
    tools/patch-3270-oia-font.py src/LizTerm.App/Assets/Fonts/3270-Regular.otf

Idempotent: a font that already carries the block is left alone. Refuses a font where the block is taken by
something else, because the code points in OiaGlyphs.cs would then draw the wrong symbols.
"""
import sys

from fontTools.ttLib import TTFont

FIRST_CODE_POINT = 0xE180
GLYPHS = [
    "boxA", "insert", "boxB", "box6", "rightarrow", "upshift", "human", "underB", "downshift", "boxquestion",
    "boxsolid", "badcommhi", "commhi", "commjag", "commlo", "clockleft", "clockright", "lock", "leftarrow",
    "keyleft", "keyright", "box4", "underA", "magcard", "boxhuman",
]


def main(path: str) -> int:
    font = TTFont(path)
    order = set(font.getGlyphOrder())
    missing = [name for name in GLYPHS if name not in order]
    if missing:
        print(f"{path}: no such glyphs: {', '.join(missing)}", file=sys.stderr)
        return 1
    mapping = {FIRST_CODE_POINT + i: name for i, name in enumerate(GLYPHS)}
    changed = False
    for table in font["cmap"].tables:
        if not table.isUnicode():
            continue
        for code_point, name in mapping.items():
            current = table.cmap.get(code_point)
            if current == name:
                continue
            if current is not None:
                print(f"{path}: U+{code_point:04X} already maps to {current}, not {name}", file=sys.stderr)
                return 1
            table.cmap[code_point] = name
            changed = True
    if not changed:
        print(f"{path}: already patched")
        return 0
    font.save(path)
    print(f"{path}: mapped {len(mapping)} OIA glyphs at U+{FIRST_CODE_POINT:04X}..U+{FIRST_CODE_POINT + len(mapping) - 1:04X}")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
