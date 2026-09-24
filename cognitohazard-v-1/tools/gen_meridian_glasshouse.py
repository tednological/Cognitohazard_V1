"""Meridian Glasshouse: a research lab where everyone can see everyone.

The identity is GLASS. Offices front the atrium with glass walls, so a guard
walking the atrium sees into every one of them and a player in an office is
lit up to anyone outside. A glasshouse stands in the middle of the floor. The
security office watches the atrium through a window. The only rooms that hide
you are the ones behind DOORS -- the server hall, where the objective is --
and every window is also a way through, if you are willing to be heard.

    lobby | north offices (glass fronts)          | stair + EXIT
          |------------------------------------------------------
          |   atrium      [ glasshouse ]          | security (window)
          |                                       |------ door ------
          |------------------------------------------ server hall  !
    spawn | south offices (glass fronts)      door|

Run from the project root:  python3 tools/gen_meridian_glasshouse.py
Deterministic; checks itself (tools/levelkit.py) before writing.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from levelkit import Grid, WALL, GLASS  # noqa: E402

W, H = 64, 40
L = Grid(W, H)

# ------------------------------------------------------------ office rows
OFFICES = [7, 18, 29, 40]          # left interior column of each office, 10 wide


def office_row(front, back_r0, back_r1):
    """Four offices between rows back_r0..back_r1, fronted by `front`."""
    L.vline(6, back_r0, back_r1)
    for x0 in OFFICES[1:]:
        L.vline(x0 - 1, back_r0, back_r1)
    L.vline(50, back_r0, back_r1)
    L.hline(front, 6, 50)
    for x0 in OFFICES:
        # '#', door, door, '#', glass x4, '#', '#' -- a door and a window per office
        L.door_h(x0 + 1, front)
        L.hline(front, x0 + 4, x0 + 7, GLASS)


office_row(9, 1, 9)        # north: rows 1-8, front wall row 9
office_row(30, 30, 38)     # south: rows 31-38, front wall row 30

# ---------------------------------------------------------- secure wing
# East of the atrium, rows 12-38. Solid walls: the one part of the floor you
# cannot be seen into. Security office on top, server hall below.
L.vline(50, 12, 38)
L.hline(12, 50, 62)
L.hline(21, 51, 62)

# The security office watches the atrium through a window, and is entered by a
# door just south of it.
L.vline(50, 14, 17, GLASS)
L.door_v(50, 19)
# Office to server hall.
L.door_h(55, 21)
# South-east office to server hall: the back way in, through someone's desk.
L.door_v(50, 34)

# Server racks: two long rows, leaving three aisles joined top and bottom.
L.vline(53, 24, 33)
L.vline(57, 24, 33)

# ---------------------------------------------------------------- glasshouse
# A glass box in the middle of the atrium. Mullions every four panes so a
# window breaks a section at a time; a door on each side.
GX0, GY0, GX1, GY1 = 20, 15, 36, 24
L.box(GX0, GY0, GX1, GY1, WALL)
for r in (GY0, GY1):
    for c0 in (21, 26, 31):
        L.hline(r, c0, c0 + 3, GLASS)
for c in (GX0, GX1):
    L.vline(c, 16, 17, GLASS)
    L.door_v(c, 19)
    L.vline(c, 22, 23, GLASS)

# Planters and a pool: cover inside the glass, so the glasshouse is somewhere
# to cross rather than a lit box to avoid.
for c0, r0 in ((23, 17), (32, 17), (23, 22), (32, 22)):
    L.hline(r0, c0, c0 + 1)
L.fill(27, 19, 29, 20, WALL)

# ------------------------------------------------------------- atrium cover
L.fill(9, 19, 12, 20, WALL)                      # reception desk
for c, r in ((13, 14), (43, 14), (13, 25), (43, 25)):
    L.put(c, r, WALL)                            # columns

# The entrance vestibule. The run starts behind a shut door, so the first
# thing the player does on this floor is choose when to open it.
L.hline(30, 1, 5)
L.door_h(2, 30)

# Crates by the stair: the last stretch to the exit is not a bare room.
L.hline(8, 55, 56)
L.hline(8, 60, 61)

# ------------------------------------------------------------------ markers
L.put(3, 35, '@')
L.fill(59, 2, 61, 4, 'X')
L.put(60, 36, '!')

for c, r in ((10, 3), (61, 19), (52, 37), (11, 36), (34, 36)):
    L.put(c, r, 'C')
for c, r in ((33, 4), (25, 20), (14, 33)):
    L.put(c, r, '$')

# ------------------------------------------------------------------- guards
# Every fourth letter (a, e, i, m) carries a record: Tune.GuardRecordEvery.
GUARDS = {
    'a': [(8, 11), (46, 11)],                          # atrium, north walk
    'b': [(46, 28), (8, 28)],                          # atrium, south walk
    'c': [(28, 17), (34, 20), (28, 22), (22, 19)],     # inside the glasshouse
    'd': None, 'e': None, 'g': None, 'j': None,        # sentries, placed below
    'f': [(60, 16), (53, 16)],                         # security: paces TO the window
    'h': [(3, 11), (3, 27)],                           # the lobby, to the vestibule door
    'i': [(17, 13), (39, 13), (39, 26), (17, 26)],     # round the glasshouse
    'k': [(9, 5), (9, 12)],                            # office to atrium, via its door
    'l': [(20, 34), (20, 27)],                         # the same, south side
    'm': [(55, 24), (55, 36)],                         # down the middle aisle
}
SENTRIES = {'d': (22, 5), 'e': (44, 35), 'g': (60, 25), 'j': (54, 6)}

for gid, pts in GUARDS.items():
    if pts is None:
        continue
    L.put(*pts[0], gid)
    L.route(gid, *pts)
for gid, cell in SENTRIES.items():
    L.put(*cell, gid)

L.write("levels/meridian_glasshouse.txt", "Meridian Glasshouse", theme="scientific")
