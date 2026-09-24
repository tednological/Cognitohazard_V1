"""Vault Row: the archive under a bank, where every room is behind a door.

The identity is DOORS. Meridian Glasshouse is the floor where everyone sees
everyone; this is its opposite. Almost every room is behind a door, so you
never know what is on the other side until it swings -- and neither do they.
Guards walking their rounds open doors and leave them open, so a door standing
open that was shut is how you learn someone has been through. Shelving stacks
make aisles two cells wide: close quarters, short sightlines, sound travels.

There is ONE window on the floor, and it is the point: the vault supervisor's
office looks onto the antechamber, so the double door to the vault is watched
through glass.

    R1 reading | R2 stacks | R3 stacks | R4 office | R5 stacks | R6 stair EXIT
    ---door--------door--------door--------door--------door--------door------
    FOYER |door|              the hall (pillared)
    ---door-------------door-------door------------double door--------------
    SW stack hall            | supervisor =window= antechamber
                             | break room  |       ==vault door==
                             |             |       vault   !

Run from the project root:  python3 tools/gen_vault_row.py
Deterministic; checks itself (tools/levelkit.py) before writing.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from levelkit import Grid, WALL, GLASS  # noqa: E402

W, H = 72, 44
L = Grid(W, H)

# ---------------------------------------------------------------- the hall
L.hline(18, 1, 70)                  # north wall of the hall
L.hline(25, 1, 70)                  # south wall

# The foyer at the hall's west end, where the run starts: a door onto the hall.
L.vline(12, 19, 24)
L.door_v(12, 21)

# Pillars down the hall, so two patrols do not make it one long sightline.
for c in (24, 36, 48):
    L.fill(c, 21, c + 1, 22, WALL)

# ------------------------------------------------------------ north rooms
DIVIDERS = [12, 24, 36, 48, 60]
for c in DIVIDERS:
    L.vline(c, 1, 18)
# A door from every room onto the hall (R1's opens into the foyer).
for c in (5, 17, 29, 41, 53, 65):
    L.door_h(c, 18)
# Side doors between neighbours, so there is more than one way round.
L.door_v(24, 9)
L.door_v(48, 9)

# R1, reading room: tables.
for c0, r in ((3, 5), (7, 5), (3, 10), (7, 10)):
    L.hline(r, c0, c0 + 2)

# R2 and R5, stacks: long shelves with a cross aisle at each end.
for x0 in (13, 49):
    for r in (4, 8, 12, 15):
        L.hline(r, x0 + 2, x0 + 8)

# R3, stacks the other way: tall shelves, two-cell aisles, open top and bottom.
for c in (27, 30, 33):
    L.vline(c, 4, 13)

# R4, records office: two desks.
L.hline(5, 39, 41)
L.hline(12, 43, 45)

# R6, the stair: crates by the exit.
L.hline(8, 62, 63)
L.hline(13, 67, 68)

# ------------------------------------------------------------ south wing
L.vline(36, 25, 42)
L.vline(48, 25, 42)

# SW stack hall: doors from the foyer and from the hall, three shelf rows with
# two cross aisles.
L.door_h(8, 25)
L.door_h(28, 25)
for r in (29, 33, 37):
    L.hline(r, 3, 10)
    L.hline(r, 13, 21)
    L.hline(r, 24, 33)

# Supervisor's office over the break room, a door between them, and the
# WINDOW onto the antechamber.
L.door_h(41, 25)
L.hline(33, 37, 47)
L.door_h(41, 33)
L.vline(48, 27, 30, GLASS)
# Break room to the stacks: the long way round to the supervisor.
L.door_v(36, 39)

# The vault: a double door from the hall into the antechamber, and the vault
# door -- three cells -- out of it.
L.door_h(58, 25, 3)
L.hline(33, 49, 70)
L.door_h(58, 33, 3)
# Safe-deposit banks inside the vault.
L.vline(52, 36, 40)
L.vline(64, 36, 40)
L.hline(38, 55, 61)
# A desk in the antechamber: cover between the two doors.
L.hline(29, 55, 57)

# ------------------------------------------------------------------ markers
L.put(3, 22, '@')
L.fill(66, 3, 68, 5, 'X')
L.put(59, 41, '!')

for c, r in ((2, 2), (45, 3), (54, 14), (30, 41), (51, 41), (68, 41)):
    L.put(c, r, 'C')
for c, r in ((6, 14), (31, 8), (17, 35)):
    L.put(c, r, '$')

# ------------------------------------------------------------------- guards
# Every fourth letter (a, e, i, m) carries a record: Tune.GuardRecordEvery.
GUARDS = {
    'a': [(16, 20), (66, 20)],                         # the hall, north side
    'b': [(66, 23), (16, 23)],                         # the hall, south side
    'c': [(14, 6), (22, 6), (22, 10), (14, 10)],       # R2 stacks
    'f': [(50, 6), (58, 6), (58, 10), (50, 10)],       # R5 stacks
    'h': [(4, 31), (33, 31)],                          # SW stacks, upper aisle
    'i': [(33, 39), (4, 39)],                          # SW stacks, lower aisle
    'j': [(39, 29), (46, 29)],                         # supervisor: paces TO the window
    'l': [(51, 30), (68, 30)],                         # antechamber
    'n': [(29, 2), (29, 15)],                          # down an R3 aisle
}
SENTRIES = {'d': (32, 16), 'e': (42, 8), 'g': (64, 11), 'k': (42, 38), 'm': (66, 36)}

for gid, pts in GUARDS.items():
    L.put(*pts[0], gid)
    L.route(gid, *pts)
for gid, cell in SENTRIES.items():
    L.put(*cell, gid)

# Imported by gen_vault_row_night.py for the same floor after dark, so only
# write when run.
if __name__ == "__main__":
    L.write("levels/vault_row.txt", "Vault Row", theme="industrial")
