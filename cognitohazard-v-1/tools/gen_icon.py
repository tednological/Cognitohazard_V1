#!/usr/bin/env python3
"""Generate the application icon: icon.png (window/editor) and icon.ico (the
Windows executable's embedded icon).

Procedural primitives only, per spec 0 -- no asset hunting. Deterministic:
running it twice writes identical bytes, so the icon can be regenerated from
the repository rather than kept as an opaque binary nobody can edit.

The mark is the title screen's own palette: a dark plate, the amber reticle
that sits on the muzzle line, and one pale record card inside it. Evidence
inside a sight is what the game is about.

    python3 tools/gen_icon.py
"""

import os
from PIL import Image, ImageDraw

# Drawn at 8x and downsampled, which is the whole of the anti-aliasing.
SS = 8
BASE = 256
SIZES = [256, 128, 64, 48, 32, 16]

C_PLATE = (18, 22, 28, 255)      # title screen C_BG, lifted slightly
C_EDGE = (42, 49, 60, 255)       # C_RULE
C_RETICLE = (255, 242, 140, 255)  # C_SEL
C_CARD = (220, 230, 242, 255)    # C_TEXT
C_CARD_EDGE = (96, 112, 132, 255)


def draw_icon() -> Image.Image:
	n = BASE * SS
	img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
	d = ImageDraw.Draw(img)

	# The plate. Rounded so it reads as an application tile at 16px, where
	# every other feature here has dissolved into a smudge.
	pad = 6 * SS
	d.rounded_rectangle([pad, pad, n - pad, n - pad], radius=36 * SS,
		fill=C_PLATE, outline=C_EDGE, width=3 * SS)

	# The reticle: a ring broken by four gaps, with ticks growing inward from
	# them. A solid ring reads as a letter O.
	cx = cy = n // 2
	r = 84 * SS
	w = 7 * SS
	for start in (12, 102, 192, 282):
		d.arc([cx - r, cy - r, cx + r, cy + r], start, start + 66,
			fill=C_RETICLE, width=w)

	tick_out = r + 4 * SS
	tick_in = r - 16 * SS
	for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
		d.line([cx + dx * tick_in, cy + dy * tick_in,
			cx + dx * tick_out, cy + dy * tick_out], fill=C_RETICLE, width=w)

	# The record card, centred in the sight. Two ruled lines are enough to say
	# "document" and few enough to survive the 32px downsample.
	cw, ch = 62 * SS, 46 * SS
	box = [cx - cw // 2, cy - ch // 2, cx + cw // 2, cy + ch // 2]
	d.rounded_rectangle(box, radius=4 * SS, fill=C_CARD, outline=C_CARD_EDGE,
		width=2 * SS)
	for i, frac in enumerate((0.34, 0.56, 0.78)):
		y = box[1] + ch * frac
		x2 = box[2] - (10 * SS if i < 2 else 24 * SS)
		d.line([box[0] + 10 * SS, y, x2, y], fill=C_PLATE, width=3 * SS)

	return img.resize((BASE, BASE), Image.LANCZOS)


def main() -> None:
	root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
	icon = draw_icon()
	icon.save(os.path.join(root, "icon.png"))
	icon.save(os.path.join(root, "icon.ico"),
		sizes=[(s, s) for s in SIZES])
	print("wrote icon.png (%dx%d) and icon.ico (%s)"
		% (BASE, BASE, ", ".join(str(s) for s in SIZES)))


if __name__ == "__main__":
	main()
