"""Cognitohazard Black Site: the last floor. A research complex dug into a mountain.

The identity is DEPTH. Every other level is a floor you cross; this one is a
floor you go DOWN into. The surface complex is open to three sides -- the west
yard, the south yard and the east yard -- and every way in leads the same
direction: north, into the rock, toward the vault. The mountain is the fourth
side. Nothing comes at the vault from behind, and nothing gets out that way
either: whatever you take from the cage you carry back through everything you
passed on the way in.

It is also the biggest floor the format allows (Level.MaxCells), carries every
guard letter there is, pays its guards like officers and stocks its chests
like a treasury. Legendary gear is the point of coming here, and it is the
point of every guard between you and it.

                         MOUNTAIN (solid rock)
         armory |W|  labs  [    VAULT   [cage !]    ]  labs |E| reactor
       barracks |T|  labs  [     vault hall         ]  labs |T| data
                |U|  labs =[   antechamber (officers)]= labs |U|
                |N|==========  the gallery  =================|N|
           mess |N| stores | booth=[checkpoint]=booth | stores |N| med bay
                |E|        |       [ portal ]        |        |E|
    ~~~ face ~~~+-+--------+------[blast doors]------+--------+-+~~~ face ~~~
    W   |  west wing rooms | atrium [glass]   [glass] | east wing rooms |  E
    Y  +door  corridor  door        the atrium         door corridor  door+ Y
    A   |  west wing rooms | ======= lobby =========  | east wing rooms |  A
    R   +------door--------+--------main doors--------+------door-------+  R
    D   EXIT     tower          SOUTH YARD      @         tower     EXIT   D
    EXIT                                                              EXIT

Four extracts: the west and east yards each have one against the rock face,
and the south yard has one in each corner. Three ways into the mountain: the
west and east service tunnels, and the portal behind the atrium. All three
meet in the gallery in front of the vault.

Run from the project root:  python3 tools/gen_black_site.py
Deterministic; checks itself (tools/levelkit.py) before writing.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from levelkit import Grid, WALL, FLOOR, GLASS, GUARD_GLYPHS  # noqa: E402

# 168 x 128 = 21,504 cells: exactly Level.MaxCells (48 x 28 x 16).
W, H = 168, 128
L = Grid(W, H)


def m(c):
    """The column mirrored across the level's north-south axis."""
    return W - 1 - c


def carve(c0, r0, c1, r1):
    L.fill(c0, r0, c1, r1, FLOOR)


def carve2(c0, r0, c1, r1):
    """Carve a west-half rectangle and its mirror."""
    carve(c0, r0, c1, r1)
    carve(m(c1), r0, m(c0), r1)


def block2(c0, r0, c1, r1):
    """A free-standing block (equipment, cover) and its mirror."""
    L.fill(c0, r0, c1, r1, WALL)
    L.fill(m(c1), r0, m(c0), r1, WALL)


def hwall2(r, c0, c1, ch=WALL):
    L.hline(r, c0, c1, ch)
    L.hline(r, m(c1), m(c0), ch)


def vwall2(c, r0, r1, ch=WALL):
    L.vline(c, r0, r1, ch)
    L.vline(m(c), r0, r1, ch)


def door_h2(c, r, n=2):
    """A door in a horizontal wall and its mirror (leftmost cells)."""
    L.door_h(c, r, n)
    L.door_h(m(c + n - 1), r, n)


def door_v2(c, r, n=2):
    L.door_v(c, r, n)
    L.door_v(m(c), r, n)


def put2(c, r, ch):
    L.put(c, r, ch)
    L.put(m(c), r, ch)


# =================================================================== the rock
# Everything north of the surface complex is mountain. The deep site is CARVED
# out of it, so every wall in there is a wall of rock.
L.fill(1, 1, W - 2, 55, WALL)

# The mountain face over the two side yards, jagged: the rock comes down
# further toward the building and stands back toward the level's edge.
FACE = [46, 46, 47, 47, 45, 45, 46, 48, 48, 49, 50, 50, 51, 52, 53, 54, 55]
for i, face in enumerate(FACE):
    c = 1 + i
    if face < 55:
        carve2(c, face + 1, c, 55)

# ================================================================ the deep site
# --- the service tunnels: four wide, from the building's north wall up to the
# armory and the reactor, the long way in on either flank.
carve2(43, 8, 46, 55)

# --- the gallery: the cross passage in front of the vault. All three ways in
# meet here.
carve(43, 33, m(43), 36)

# --- the portal: the main way into the mountain, straight behind the atrium.
carve(79, 47, 88, 55)

# --- the checkpoint between the portal and the gallery, with a security booth
# either side watching the floor through glass.
carve(68, 38, m(68), 46)
L.door_h(80, 37, 3)                       # inner blast doors onto the gallery
L.door_h(85, 37, 3)
vwall2(75, 38, 44)                        # booth walls
hwall2(44, 68, 75)
vwall2(75, 39, 42, GLASS)                 # booth windows onto the checkpoint
door_h2(70, 44)                           # booth doors, from the strip below
block2(78, 41, 79, 42)                    # cover either side of the lane

# --- the stores: cold storage off each tunnel, a side door into the checkpoint.
carve2(48, 40, 66, 53)
door_v2(47, 45, 3)                        # from the tunnel
door_v2(67, 45)                           # into the checkpoint, under the booth
block2(51, 43, 52, 46)                    # racks
block2(56, 43, 57, 46)
block2(51, 49, 52, 50)
block2(56, 49, 57, 50)
block2(61, 43, 62, 44)

# --- off the far side of each tunnel: armory / reactor, barracks / data
# centre, mess / med bay. Dead ends in the rock.
carve2(30, 8, 41, 17)                     # armory (W), reactor (E)
door_v2(42, 12)
block2(33, 11, 34, 12)
block2(37, 11, 38, 12)
block2(33, 14, 36, 14)                    # a bench, four long
carve2(24, 20, 41, 31)                    # barracks (W), data centre (E)
door_v2(42, 25)
for c in (26, 29, 32, 35):                # bunks / server rows
    block2(c, 22, c, 25)
    block2(c, 27, c, 30)
carve2(24, 38, 41, 52)                    # mess (W), med bay (E)
door_v2(42, 44)
block2(28, 41, 31, 41)
block2(28, 48, 31, 48)
block2(35, 44, 36, 45)

# --- the labs: three rooms a side, between the tunnels and the vault, joined
# to each other, to the tunnel and (the bottom one) to the gallery. The bottom
# lab looks into the antechamber through an observation window.
carve2(48, 5, 65, 11)
carve2(48, 13, 65, 21)
carve2(48, 23, 65, 31)
door_v2(47, 8)                            # each lab to its tunnel
door_v2(47, 16)
door_v2(47, 26)
door_h2(56, 12)                           # lab 1 to lab 2
door_h2(50, 22)                           # lab 2 to lab 3
door_h2(55, 32)                           # lab 3 to the gallery
vwall2(66, 26, 29, GLASS)                 # observation window, antechamber
block2(51, 6, 54, 6)                      # benches and analysers
block2(60, 8, 61, 9)
block2(59, 15, 62, 15)
block2(52, 18, 53, 19)
block2(60, 25, 61, 26)
block2(51, 28, 54, 28)

# --- the vault. The rock is its back wall: the one side of it nobody can come
# from. A vault hall of deposit racks, and at the very back of it the cage
# holding the sealed case, behind its own door.
carve(67, 5, m(67), 23)
L.door_h(82, 24, 3)                       # the vault door
L.vline(76, 5, 12)                        # the cage
L.vline(m(76), 5, 12)
L.hline(12, 76, m(76))
L.door_h(82, 12, 3)                       # the cage door
block2(69, 7, 70, 10)                     # deposit racks
block2(69, 14, 70, 17)
block2(79, 15, 80, 18)

# --- the antechamber, where the vault officers stand.
carve(67, 25, m(67), 31)
L.door_h(82, 32, 3)                       # from the gallery
block2(74, 27, 75, 28)                    # cover

# ============================================================ the surface
# The building runs c 18..149, rows 56..100. Its north wall is the rock face.
L.box(18, 56, m(18), 100)
door_h2(44, 56)                           # the tunnel mouths
L.door_h(80, 56, 3)                       # the portal's blast doors
L.door_h(85, 56, 3)

# --- the wings.
vwall2(59, 57, 99)                        # wing / atrium walls
hwall2(76, 19, 58)                        # north rooms / corridor
hwall2(81, 19, 58)                        # corridor / south rooms
vwall2(32, 57, 75)                        # north room dividers
vwall2(47, 57, 75)
vwall2(32, 82, 99)                        # south room dividers
vwall2(47, 82, 99)

door_v2(18, 78)                           # the side entrance off each yard
door_v2(59, 78)                           # corridor to the atrium
door_v2(18, 90)                           # a second side door, south room
for c in (24, 38, 52):                    # every room onto the corridor
    door_h2(c, 76)
    door_h2(c, 81)
door_v2(32, 66)                           # north rooms joined
door_v2(47, 62)
door_v2(32, 90)                           # south rooms joined
door_v2(47, 90)
door_h2(38, 100)                          # south doors onto the yard
door_h2(52, 100)
hwall2(76, 27, 30, GLASS)                 # rooms looking onto the corridor
hwall2(76, 55, 57, GLASS)
hwall2(81, 41, 44, GLASS)

# Furniture in the wing rooms.
block2(22, 60, 25, 60)
block2(22, 64, 25, 64)
block2(28, 69, 29, 70)
block2(36, 60, 37, 61)
block2(42, 69, 43, 70)
block2(51, 60, 54, 60)
block2(51, 70, 52, 71)
block2(23, 85, 24, 86)
block2(23, 95, 26, 95)
block2(38, 86, 41, 86)
block2(39, 95, 40, 96)
block2(52, 85, 53, 86)
block2(51, 95, 54, 95)

# --- the atrium: the big room at the foot of the mountain. Two glass-walled
# labs stand in it, and the lobby watches it through windows.
hwall2(81, 60, 79)                        # lobby wall, gap c 80..87 in the middle
hwall2(81, 70, 73, GLASS)
door_h2(64, 81, 3)

# The glasshouse labs: glass on the faces toward the middle, doors at the back.
for c0, c1 in ((64, 73), (m(73), m(64))):
    L.box(c0, 62, c1, 72)
    L.hline(62, c0 + 2, c0 + 5, GLASS)
    L.hline(72, c0 + 2, c0 + 5, GLASS)
L.vline(73, 64, 67, GLASS)
L.vline(m(73), 64, 67, GLASS)
door_v2(64, 67)
block2(67, 66, 68, 67)                    # an analyser in each

# Pillars down the atrium.
for r in (60, 74):
    block2(76, r, 77, r + 1)

# The lobby: a reception desk and the main doors.
block2(74, 88, 77, 88)
block2(66, 94, 67, 95)
L.door_h(80, 100, 3)                      # the main doors
L.door_h(85, 100, 3)

# ================================================================== the yards
# Guard towers at the south corners of the building: glass on three sides.
for c0 in (20, m(26)):
    L.box(c0, 106, c0 + 6, 112)
    L.hline(106, c0 + 2, c0 + 4, GLASS)
    L.vline(c0, 108, 110, GLASS)
    L.vline(c0 + 6, 108, 110, GLASS)
    L.door_h(c0 + 2, 112, 3)

# Cover in the south yard: parked vehicles and crate stacks.
block2(34, 108, 37, 109)
block2(50, 116, 53, 117)
block2(66, 108, 67, 109)
block2(12, 118, 13, 119)
block2(40, 121, 41, 122)
block2(74, 116, 75, 117)
# The side yards: crates by the rock.
block2(8, 64, 9, 65)
block2(12, 84, 13, 87)
block2(6, 98, 7, 99)

# ================================================================= markers
L.put(83, 123, '@')
for c0, r0 in ((2, 70), (2, 123)):        # west yard and south-west corner
    L.fill(c0, r0, c0 + 2, r0 + 2, 'X')
    L.fill(m(c0 + 2), r0, m(c0), r0 + 2, 'X')

L.put(83, 7, '!')                         # the sealed case, in the cage

CHESTS = [
    # the deep site
    (31, 9), (40, 16), (35, 16),           # armory
    (25, 21), (40, 30),                    # barracks
    (25, 51),                              # mess
    (49, 52), (65, 41),                    # stores
    (64, 6), (49, 20), (64, 30),           # labs
    (68, 22), (73, 6),                     # vault hall
    (80, 9),                               # the cage, beside the case
    # the surface
    (20, 58), (46, 74), (57, 58),          # north wing rooms
    (20, 98), (46, 83),                    # south wing rooms
    (65, 71),                              # glasshouse
    (21, 107),                             # tower
]
for c, r in CHESTS:
    put2(c, r, 'C')

for c, r in ((39, 9), (48, 30), (19, 75)):
    put2(c, r, '$')

# Where a hunting squad looks first: the back rooms of the mountain.
for c, r in ((36, 26), (57, 17), (70, 12), (83, 42), (32, 45)):
    put2(c, r, '*')

# =================================================================== guards
# 78: the twenty-six letters here, fifty-two more below. The yards are
# watched, the building is walked, and the mountain is held. Every fourth letter (a, e, i, m, q, u, y)
# carries a record: Tune.GuardRecordEvery.
GUARDS = {
    # the yards
    'a': [(21, 104), (m(21), 104)],                     # the south yard, end to end
    'b': [(9, 58), (9, 124)],                           # the west yard
    'c': [(m(9), 124), (m(9), 58)],                     # the east yard
    # the surface building
    'g': [(21, 78), (57, 78)],                          # west wing corridor
    'h': [(m(57), 79), (m(21), 79)],                    # east wing corridor
    'i': [(62, 58), (m(62), 58), (m(62), 78), (62, 78)],  # round the atrium
    'k': [(62, 84), (m(62), 84)],                       # the lobby
    'l': [(22, 92), (56, 92)],                          # west wing south rooms
    'm': [(m(56), 93), (m(22), 93)],                    # east wing south rooms
    # the mountain
    'n': [(44, 10), (44, 53)],                          # west tunnel
    'o': [(m(44), 53), (m(44), 10)],                    # east tunnel
    'p': [(45, 34), (m(45), 35)],                       # the gallery
    't': [(83, 58), (83, 44)],                          # the portal, atrium to checkpoint
    'w': [(73, 14), (73, 22), (m(73), 22), (m(73), 14)],  # the vault hall
    'y': [(57, 9), (51, 26)],                           # west labs, room to room
    'z': [(m(51), 26), (m(57), 9)],                     # east labs
}
SENTRIES = {
    'd': (79, 104), 'e': (23, 109), 'f': (m(23), 109),  # main doors, towers
    'j': (83, 61),                                      # in front of the portal
    'q': (71, 40), 'r': (m(71), 40),                    # checkpoint booths
    's': (83, 43),                                      # checkpoint floor
    'u': (72, 29), 'v': (m(72), 29),                    # antechamber officers
    'x': (86, 8),                                       # in the cage
}

# ...and fifty-two more past 'z', three times the floor the letters alone
# could hold. Their glyphs are the Latin-1 letters that follow 'z' in the
# guard alphabet (Level.GuardGlyphs), handed out in the order listed. Mostly
# PAIRS, one per side of the complex, as the floor itself is.
def pair_route(*pts):
    return [list(pts), [(m(c), r) for c, r in pts]]


def pair_post(c, r):
    return [(c, r), (m(c), r)]


EXTRA_ROUTES = (
    # the yards: the ground in front of each wing
    pair_route((25, 114), (50, 114))
    # the mountain: a second walker in each tunnel, the mess, the stores aisle
    + pair_route((45, 53), (45, 10))
    + pair_route((26, 44), (39, 49))
    + pair_route((54, 41), (54, 52))
    # the atrium: the lanes between the wings and the glasshouses
    + pair_route((61, 62), (61, 74))
)
EXTRA_POSTS = (
    # the yards: every exit is watched, and so is each side door
    pair_post(6, 71) + pair_post(7, 121) + pair_post(14, 78)
    # the wings: a man in the rooms
    + pair_post(25, 67) + pair_post(40, 60) + pair_post(55, 65)
    + pair_post(27, 88) + pair_post(56, 88)
    # the glasshouses and the lobby's main doors
    + pair_post(70, 69) + pair_post(78, 97)
    # the mountain
    + pair_post(36, 9)                      # armory / reactor
    + pair_post(39, 25)                     # barracks / data centre
    + pair_post(62, 50)                     # stores
    + pair_post(79, 45)                     # checkpoint floor
    + pair_post(81, 50)                     # the portal
    + pair_post(60, 35)                     # the gallery
    + pair_post(52, 9) + pair_post(62, 19) + pair_post(57, 25)   # the labs
    + pair_post(79, 27)                     # antechamber: two more officers
    + pair_post(80, 20)                     # inside the vault door
)
EXTRA = GUARD_GLYPHS[26:]
assert len(EXTRA_ROUTES) + len(EXTRA_POSTS) == 52, len(EXTRA_ROUTES) + len(EXTRA_POSTS)
for k, pts in enumerate(EXTRA_ROUTES):
    GUARDS[EXTRA[k]] = pts
for k, cell in enumerate(EXTRA_POSTS):
    SENTRIES[EXTRA[len(EXTRA_ROUTES) + k]] = cell
OFFICERS = [EXTRA[len(EXTRA_ROUTES) + k] for k, cell in enumerate(EXTRA_POSTS)
            if cell in ((79, 27), (m(79), 27), (80, 20), (m(80), 20))]

for gid, pts in GUARDS.items():
    L.put(*pts[0], gid)
    L.route(gid, *pts)
for gid, cell in SENTRIES.items():
    L.put(*cell, gid)

# ===================================================================== loot
# Chests are a treasury: ~$27,000 a chest on average, which the pacing rule in
# LootTable.Buy can only spend on the top of the catalogue. Guards are paid
# enough that almost every one carries a legendary gun and most wear plate;
# the vault officers more than that.
LOOT = 1_000_000
GUARD_LOOT = 7_000
KITS = {'u': 14_000, 'v': 14_000, 'x': 14_000, 'w': 10_000, 's': 10_000}
KITS.update({gid: 14_000 for gid in OFFICERS})

if __name__ == "__main__":
    L.write("levels/zz_black_site.txt", "Cognitohazard Black Site", theme="scientific",
            loot=LOOT, guard_loot=GUARD_LOOT, kits=KITS)
