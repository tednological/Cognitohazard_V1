"""Vault Row, Night Shift: the same archive after hours, with the lights down.

The identity is LIGHT (cognitohazard_lighting_plan.md L7). Vault Row by day is
the floor where you cannot see round a door; by night you cannot see across a
room either, unless someone has left a lamp on. Ambient is 25%, below
Tune.GuardTorchAmbient, so every guard walks his round with a torch: you read a
patrol by its beam long before you see the man, and a beam that finds you
lights you for everyone watching.

What the dark gives you, and what it costs:
  - The hall is a string of pools of light with dark between the pillars.
    Crossing it is timing: pool, dark, pool, with two torches walking it.
  - Its SWITCH is by the foyer door. Kill the hall lights and the whole run is
    dark -- and both hall guards saw it happen, and one of them is coming to
    the switch to put them back on. Standing in the dark beside it is the
    oldest trick there is.
  - R3's stacks have no lamp at all. The dark room is the quiet way east.
  - R2's stacks and R5 have lamps with NO switch: shoot them out (loud), or
    walk round the light.
  - The supervisor's office is lit and glass-fronted, so its light spills into
    the antechamber and the man pacing to the window is a silhouette.
  - The exit stair is lit. The last twenty cells are the brightest on the floor.
  - The vault has one lamp by the door and a switch inside it: the objective
    sits in the dark unless you turn the light on to see what you are doing.

Run from the project root:  python3 tools/gen_vault_row_night.py
Deterministic; checks itself (tools/levelkit.py) before writing.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen_vault_row import L  # noqa: E402  -- the day floor, built but not written

AMBIENT = 25

# --- the hall: pools between the pillars (24, 36, 48), a switch by the foyer.
for c, r in ((18, 20), (30, 23), (42, 20), (54, 23), (66, 21)):
    L.lamp(c, r)
L.switch(13, 19)

# --- the foyer: a desk lamp over the start, so the first room is readable.
L.lamp(9, 23)

# --- north rooms. R1 reading: one lamp, a switch by its door.
L.lamp(6, 8)
L.switch(4, 17)
# R2 stacks: lamps, no switch.
L.lamp(18, 6)
L.lamp(18, 14)
# R3 stacks: NO lamp. The dark room.
# R4 records office: two lamps and a switch.
L.lamp(44, 4)
L.lamp(40, 14)
L.switch(37, 17)
# R5 stacks: one lamp, no switch.
L.lamp(54, 10)
# R6, the stair: lit, and switchable.
L.lamp(64, 7)
L.switch(61, 17)

# --- south wing. SW stack hall: three lamps down the aisles, a switch.
for c, r in ((17, 27), (7, 35), (28, 39)):
    L.lamp(c, r)
L.switch(35, 27)
# Supervisor's office, glass-fronted: lit.
L.lamp(42, 29)
L.switch(37, 26)
# Break room.
L.lamp(44, 40)
# Antechamber: one lamp in the far corner, a switch.
L.lamp(66, 27)
L.switch(70, 28)
# The vault: a lamp by the door, a switch inside it.
L.lamp(60, 35)
L.switch(49, 35)

L.write("levels/vault_row_night.txt", "Vault Row, Night Shift", theme="industrial",
        ambient=AMBIENT)
