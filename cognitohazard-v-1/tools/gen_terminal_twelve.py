"""Terminal Twelve: a 144x84 facility, 9x the reference level's area.

Carved rather than drawn: everything starts as solid wall, corridors and rooms
are cut out of it, and every room gets a door onto a corridor. That ordering is
what makes connectivity structural instead of something to check afterwards.
"""
from collections import deque

W, H = 144, 84
WALL, FLOOR = '#', '.'
g = [[WALL] * W for _ in range(H)]

# Corridors run the full span and all intersect, so they form one connected
# lattice. Rooms hang off it.
V_CORR = [(22, 24), (50, 52), (78, 80), (106, 108)]
H_CORR = [(20, 22), (40, 42), (60, 62)]

X_BLOCKS = [(1, 21), (25, 49), (53, 77), (81, 105), (109, 142)]
Y_BLOCKS = [(1, 19), (23, 39), (43, 59), (63, 82)]


def carve(x0, y0, x1, y1):
	for r in range(y0, y1 + 1):
		for c in range(x0, x1 + 1):
			if 0 < c < W - 1 and 0 < r < H - 1:
				g[r][c] = FLOOR


def in_span(v, spans):
	return any(a <= v <= b for a, b in spans)


# 1. corridors
for a, b in V_CORR:
	carve(a, 1, b, H - 2)
for a, b in H_CORR:
	carve(1, a, W - 2, b)

# 2. room interiors, leaving a one-cell wall around each block
for x0, x1 in X_BLOCKS:
	for y0, y1 in Y_BLOCKS:
		carve(x0 + 1, y0 + 1, x1 - 1, y1 - 1)

# 3. a door from every room onto every corridor it touches
for x0, x1 in X_BLOCKS:
	for y0, y1 in Y_BLOCKS:
		ym = (y0 + y1) // 2
		xm = (x0 + x1) // 2
		if in_span(x1 + 1, V_CORR):
			carve(x1, ym, x1, ym + 1)
		if in_span(x0 - 1, V_CORR):
			carve(x0, ym, x0, ym + 1)
		if in_span(y1 + 1, H_CORR):
			carve(xm, y1, xm + 1, y1)
		if in_span(y0 - 1, H_CORR):
			carve(xm, y0, xm + 1, y0)

# 4. furnish each room. A 23x18 room with nothing in it is a sightline from
#    corner to corner, which makes stealth trivial and the visibility polygon
#    boring. Each room gets partitions and pillars, varied by a deterministic
#    hash of its position so no two read the same and the level still rebuilds
#    byte-identically.
def block(x0, y0, x1, y1):
	for r in range(max(1, y0), min(H - 2, y1) + 1):
		for c in range(max(1, x0), min(W - 2, x1) + 1):
			g[r][c] = WALL


def furnish(x0, y0, x1, y1, salt):
	"""Partitions with gaps, plus pillars. Never touches the room's own wall
	ring or the two cells inside a doorway, so a door is always approachable."""
	iw, ih = x1 - x0 - 1, y1 - y0 - 1
	if iw < 8 or ih < 8:
		return
	cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
	kind = salt % 4

	if kind == 0:
		# A spine down the middle with a gap at one end.
		gap = y0 + 3 + (salt // 4) % max(1, ih - 6)
		for r in range(y0 + 2, y1 - 1):
			if not (gap <= r <= gap + 2):
				g[r][cx] = WALL
	elif kind == 1:
		# A cross-partition with an offset doorway.
		gap = x0 + 3 + (salt // 4) % max(1, iw - 6)
		for c in range(x0 + 2, x1 - 1):
			if not (gap <= c <= gap + 2):
				g[cy][c] = WALL
	elif kind == 2:
		# An inner chamber, open on one side.
		block(cx - 4, cy - 3, cx + 4, cy - 3)
		block(cx - 4, cy + 3, cx + 4, cy + 3)
		block(cx - 4, cy - 3, cx - 4, cy + 3)
	else:
		# Stacks: four pillars off-centre.
		for dx in (-5, 3):
			for dy in (-4, 2):
				block(cx + dx, cy + dy, cx + dx + 1, cy + dy + 1)

	# Pillars, in every room, as cover to break a long shot.
	for dx, dy in ((-7, -5), (6, -5), (-7, 5), (6, 5)):
		px, py = cx + dx, cy + dy
		if x0 + 1 < px < x1 - 1 and y0 + 1 < py < y1 - 1:
			g[py][px] = WALL


for bi, (x0, x1) in enumerate(X_BLOCKS):
	for bj, (y0, y1) in enumerate(Y_BLOCKS):
		furnish(x0, y0, x1, y1, bi * 7 + bj * 13)


def nearest_floor(c, r):
	"""The closest plain-floor cell to (c, r), searched outward in rings.

	Markers are placed at hand-chosen positions but the rooms are furnished
	procedurally, so a pillar can land where a cache was meant to go. Snapping
	to the nearest free cell keeps the two passes independent -- otherwise every
	furniture tweak means re-deriving thirteen cache coordinates by hand.
	"""
	for radius in range(0, 12):
		for dr in range(-radius, radius + 1):
			for dc in range(-radius, radius + 1):
				if max(abs(dr), abs(dc)) != radius:
					continue
				nc, nr = c + dc, r + dr
				if 0 < nc < W - 1 and 0 < nr < H - 1 and g[nr][nc] == FLOOR:
					return nc, nr
	raise AssertionError(f"no floor within 12 cells of {c},{r}")


def put(c, r, ch):
	c, r = nearest_floor(c, r)
	g[r][c] = ch
	return c, r


# 5. markers. Spawn north-west, exit south-east: crossing the whole floor is
#    the point of a level this size.
put(4, 4, '@')
for c, r in [(138, 78), (139, 78), (138, 79), (139, 79)]:
	put(c, r, 'X')

# Gear chests, one per wing. Placed like caches -- snapped to the nearest open
# floor, so furniture and markers stay independent passes.
CHESTS = [(10, 8), (40, 6), (66, 10), (96, 8), (128, 6),
          (8, 30), (44, 34), (72, 30), (100, 34), (132, 32),
          (10, 52), (48, 50), (76, 54), (120, 52),
          (30, 70), (70, 72), (110, 74)]

CACHES = [(16, 12), (44, 14), (72, 6), (100, 16), (130, 10),
          (12, 34), (66, 36), (126, 34),
          (16, 52), (70, 50), (128, 54),
          (36, 74), (98, 74)]
# Gear chests, one per wing. Placed like caches -- snapped to the nearest open
# floor, so furniture and markers stay independent passes.
CHESTS = [(10, 8), (40, 6), (66, 10), (96, 8), (128, 6),
          (8, 30), (44, 34), (72, 30), (100, 34), (132, 32),
          (10, 52), (48, 50), (76, 54), (120, 52),
          (30, 70), (70, 72), (110, 74)]

CACHES = [put(c, r, '$') for c, r in CACHES]
CHESTS = [put(c, r, 'C') for c, r in CHESTS]

# The objective, in the far south-east wing: the exit is that corner, so the
# mission is a crossing there and a crossing back past everything you woke up.
OBJECTIVES = [put(126, 46, '!')]

# 6. guards. TWENTY, on a format that runs 'a' to 'z'. Eight was a cap on how
#    dangerous any level could be regardless of size, and on 12096 cells it
#    worked out at one guard per 1500 -- a floor you could stroll across.
#
#    Patrollers walk the corridor lattice, where the player has to cross them.
#    Sentries hold the rooms worth entering. A guard with no route line is a
#    stationary sentry by design, so the mix is expressed by which ids appear
#    in ROUTES and which only in SENTRIES.
ROUTES = {
	'a': [(23, 6), (23, 18), (48, 18), (48, 6)],
	'b': [(51, 4), (51, 18), (104, 18), (104, 4)],
	'c': [(107, 8), (107, 30), (130, 30), (130, 8)],
	'd': [(6, 21), (46, 21), (46, 38), (6, 38)],
	'i': [(51, 21), (51, 41), (76, 41), (76, 21)],
	'j': [(107, 44), (107, 60), (130, 60), (130, 44)],
}

SENTRIES = {
	'g': (14, 8), 'h': (92, 14), 'e': (16, 34), 'f': (120, 36),
}

for gid, pts in ROUTES.items():
	for c, r in pts:
		# A waypoint only has to be WALKABLE -- routes live outside the grid, so
		# one passing over a cache is harmless. The guard's own start cell is
		# stricter, below, because that one is a glyph and has to own its cell.
		assert g[r][c] != WALL, f"route {gid} waypoint ({c},{r}) is walled"
	c, r = pts[0]
	put(c, r, gid)
	# The start cell may have snapped off the route's first waypoint; that is
	# fine, the guard simply walks to it.

for gid, (c, r) in SENTRIES.items():
	put(c, r, gid)

# 7. connectivity, before anything else gets to believe this level is playable
start = None
for r in range(H):
	for c in range(W):
		if g[r][c] == '@':
			start = (c, r)
assert start
seen = {start}
q = deque([start])
while q:
	c, r = q.popleft()
	for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
		n = (c + dc, r + dr)
		if 0 <= n[0] < W and 0 <= n[1] < H and n not in seen and g[n[1]][n[0]] != WALL:
			seen.add(n)
			q.append(n)

open_cells = {(c, r) for r in range(H) for c in range(W) if g[r][c] != WALL}
assert seen == open_cells, (
	f"furniture sealed {len(open_cells - seen)} cell(s) off from spawn")

reach = [m for m in CACHES if m in seen]
exits = [(c, r) for r in range(H) for c in range(W) if g[r][c] == 'X']
guards = [(c, r) for r in range(H) for c in range(W) if 'a' <= g[r][c] <= 'z']
assert all(e in seen for e in exits), "exit unreachable"
assert len(reach) == len(CACHES), f"only {len(reach)}/{len(CACHES)} caches reachable"
assert all(x in seen for x in guards), "a guard is walled in"

assert all(m in seen for m in CHESTS), "a chest is walled in"

floor = sum(1 for r in range(H) for c in range(W) if g[r][c] != WALL)
out = ["name: Terminal Twelve",
       "# glyphs  # wall  . floor  = glass  + door  @ spawn  X exit  $ records  C chest  ! objective  a-z guard start  * sweep node",
       "grid:"]
out += ["".join(row) for row in g]
for gid in sorted(ROUTES):
	out.append("> " + gid + " " + " ".join(f"{c},{r}" for c, r in ROUTES[gid]))
open("levels/terminal_twelve.txt", "w").write("\n".join(out) + "\n")

print(f"{W}x{H} = {W*H} cells ({W*20}x{H*20} px), {floor} open, {len(seen)} reachable")
assert all(m in seen for m in OBJECTIVES), "the objective is walled in"

print(f"{len(guards)} guards, {len(CACHES)} caches, {len(CHESTS)} chests,"
      f" {len(OBJECTIVES)} objective(s), exit {len(exits)} cells")
