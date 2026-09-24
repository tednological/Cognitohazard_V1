"""Shared building blocks for the procedural level generators.

A level is carved and furnished as a grid of glyphs, then CHECKED before it is
written: every door is a real doorway, every window sits in a wall, every guard
and waypoint has room to stand, and everything that matters is reachable. The
generator refuses to write a level that fails, so a broken level is a failed
command, not a failed playtest.

Glyphs (sim/Level.cs is the authority):
    #  wall      .  floor     =  glass     +  door
    @  spawn     X  exit      $  records   C  chest    !  objective
    a-z guard start      *  sweep node (Guard AI: where a hunting squad looks)
    L  lamp      S  light switch (lighting: floor to everything, they only light)

Everything here is deterministic: the same script writes the same bytes.
"""
from collections import deque

WALL, FLOOR, GLASS, DOOR = '#', '.', '=', '+'
LAMP, SWITCH = 'L', 'S'
HEADER = ("# glyphs  # wall  . floor  = glass  + door  @ spawn  X exit  $ records"
          "  C chest  ! objective  a-z guard start  * sweep node  L lamp  S switch")

# Must match sim/Level.cs.
# The guard alphabet (Level.GuardGlyphs): 'a'..'z', then the Latin-1 letters
# U+00C0..U+00FF without the multiplication and division signs. 88 guards.
GUARD_GLYPHS = "abcdefghijklmnopqrstuvwxyz" + "".join(
    chr(c) for c in range(0xC0, 0x100) if c not in (0xD7, 0xF7))


def is_guard(ch):
    return ch in GUARD_GLYPHS


GLASS_PANE_CELLS = 4
DOOR_LEAF_CELLS = 3
MAX_LOOT = 1_000_000          # Level.MaxLoot


class Grid:
    def __init__(self, w, h):
        self.W, self.H = w, h
        self.g = [[FLOOR] * w for _ in range(h)]
        self.routes = {}
        for r in range(h):
            for c in range(w):
                if r in (0, h - 1) or c in (0, w - 1):
                    self.g[r][c] = WALL

    # ------------------------------------------------------------ drawing
    def at(self, c, r):
        if 0 <= c < self.W and 0 <= r < self.H:
            return self.g[r][c]
        return WALL

    def put(self, c, r, ch):
        assert 0 < c < self.W - 1 and 0 < r < self.H - 1, f"({c},{r}) is on the border"
        self.g[r][c] = ch

    def fill(self, c0, r0, c1, r1, ch):
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                if 0 <= c < self.W and 0 <= r < self.H:
                    self.g[r][c] = ch

    def hline(self, r, c0, c1, ch=WALL):
        self.fill(c0, r, c1, r, ch)

    def vline(self, c, r0, r1, ch=WALL):
        self.fill(c, r0, c, r1, ch)

    def box(self, c0, r0, c1, r1, ch=WALL):
        """The outline of a rectangle, inclusive."""
        self.hline(r0, c0, c1, ch)
        self.hline(r1, c0, c1, ch)
        self.vline(c0, r0, r1, ch)
        self.vline(c1, r0, r1, ch)

    def door_h(self, c, r, n=2):
        """A door of n cells in a HORIZONTAL wall, leftmost cell at (c, r)."""
        self.hline(r, c, c + n - 1, DOOR)

    def door_v(self, c, r, n=2):
        """A door of n cells in a VERTICAL wall, topmost cell at (c, r)."""
        self.vline(c, r, r + n - 1, DOOR)

    def lamp(self, c, r):
        """A ceiling lamp over a FLOOR cell. Refuses to bury anything."""
        assert self.at(c, r) == FLOOR, f"lamp at ({c},{r}) is on {self.at(c, r)!r}"
        self.put(c, r, LAMP)

    def switch(self, c, r):
        """A light switch: a floor cell against a wall."""
        assert self.at(c, r) == FLOOR, f"switch at ({c},{r}) is on {self.at(c, r)!r}"
        self.put(c, r, SWITCH)

    def room(self, cell):
        """The cells a switch at `cell` controls: its 4-connected region of
        anything but wall, glass and door (sim/Level.cs RoomOf)."""
        return self.flood(cell, lambda x: x not in (WALL, GLASS, DOOR))

    def route(self, gid, *pts):
        self.routes[gid] = list(pts)

    # ------------------------------------------------------------ queries
    def find(self, pred):
        return [(c, r) for r in range(self.H) for c in range(self.W) if pred(self.g[r][c])]

    def one(self, ch):
        cells = self.find(lambda x: x == ch)
        assert len(cells) == 1, f"expected exactly one {ch!r}, found {len(cells)}"
        return cells[0]

    def flood(self, start, passable):
        seen = {start}
        q = deque([start])
        while q:
            c, r = q.popleft()
            for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (c + dc, r + dr)
                if n in seen:
                    continue
                if not (0 <= n[0] < self.W and 0 <= n[1] < self.H):
                    continue
                if not passable(self.g[n[1]][n[0]]):
                    continue
                seen.add(n)
                q.append(n)
        return seen

    # ------------------------------------------------------------- checks
    def runs(self, ch):
        """Runs of one glyph as the sim merges them: horizontal first, then a
        lone cell tries downward. Lengths are NOT capped here, so a run longer
        than the cap is visible to the checks."""
        used = set()
        out = []
        for r in range(self.H):
            for c in range(self.W):
                if self.g[r][c] != ch or (c, r) in used:
                    continue
                c1 = c
                while self.at(c1 + 1, r) == ch and (c1 + 1, r) not in used:
                    c1 += 1
                r1 = r
                if c1 == c:
                    while self.at(c, r1 + 1) == ch and (c, r1 + 1) not in used:
                        r1 += 1
                cells = [(cc, rr) for rr in range(r, r1 + 1) for cc in range(c, c1 + 1)]
                used.update(cells)
                out.append((c, r, c1, r1))
        return out

    def check(self, name):
        problems = []

        # Doors: 2-3 cells, a leaf in a wall -- wall at both ends along it, and
        # walkable floor on both faces, or it is a door to nowhere.
        for c0, r0, c1, r1 in self.runs(DOOR):
            n = (c1 - c0 + 1) * (r1 - r0 + 1)
            if not 2 <= n <= DOOR_LEAF_CELLS:
                problems.append(f"door at ({c0},{r0}) is {n} cells")
            if r0 == r1 and c1 > c0:                        # horizontal leaf
                ends = [(c0 - 1, r0), (c1 + 1, r0)]
                faces = [(c, r0 - 1) for c in range(c0, c1 + 1)] + \
                        [(c, r0 + 1) for c in range(c0, c1 + 1)]
            else:                                           # vertical leaf
                ends = [(c0, r0 - 1), (c0, r1 + 1)]
                faces = [(c0 - 1, r) for r in range(r0, r1 + 1)] + \
                        [(c0 + 1, r) for r in range(r0, r1 + 1)]
            if any(self.at(*e) != WALL for e in ends):
                problems.append(f"door at ({c0},{r0}) is not set in a wall")
            if any(self.at(*f) in (WALL, GLASS, DOOR) for f in faces):
                problems.append(f"door at ({c0},{r0}) opens onto a wall")

        # Glass: set in a wall at both ends, like a window.
        for c0, r0, c1, r1 in self.runs(GLASS):
            if r0 == r1 and c1 > c0:
                ends = [(c0 - 1, r0), (c1 + 1, r0)]
            elif c0 == c1 and r1 > r0:
                ends = [(c0, r0 - 1), (c0, r1 + 1)]
            else:
                ends = []
            if any(self.at(*e) not in (WALL, GLASS) for e in ends):
                problems.append(f"window at ({c0},{r0}) is not set in a wall")

        # Guards and waypoints need a clear 3x3 to stand in: a guard is 22 px
        # across and a cell 20, so one spawned beside a wall in a one-cell gap
        # overlaps both sides and can never take a step.
        blocked = (WALL, GLASS, DOOR)
        def roomy(c, r):
            return all(self.at(c + dc, r + dr) not in blocked
                       for dc in (-1, 0, 1) for dr in (-1, 0, 1))
        for c, r in self.find(is_guard):
            if not roomy(c, r):
                problems.append(f"guard {self.g[r][c]} at ({c},{r}) is crowded by a wall")
        for gid, pts in self.routes.items():
            if not self.find(lambda x, gid=gid: x == gid):
                problems.append(f"route for absent guard {gid}")
            for c, r in pts:
                if not roomy(c, r):
                    problems.append(f"guard {gid} waypoint ({c},{r}) is crowded by a wall")

        # Reachability. With doors passable and glass NOT: nothing a mission
        # needs may be behind glass only -- a window is an option, not the way.
        spawn = self.one('@')
        walk = self.flood(spawn, lambda x: x not in (WALL, GLASS))
        for ch in 'X!$C':
            for cell in self.find(lambda x, ch=ch: x == ch):
                if cell not in walk:
                    problems.append(f"{ch!r} at {cell} is unreachable without breaking glass")
        for cell in self.find(is_guard):
            if cell not in walk:
                problems.append(f"guard at {cell} is sealed in")

        # Switches: against a wall, and wired to something. A switch whose room
        # holds no lamp does nothing, which is a bug a player would find first.
        for c, r in self.find(lambda x: x == SWITCH):
            if all(self.at(c + dc, r + dr) != WALL
                   for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                problems.append(f"switch at ({c},{r}) is not against a wall")
            if not any(self.at(*cell) == LAMP for cell in self.room((c, r))):
                problems.append(f"switch at ({c},{r}) controls no lamp")

        open_cells = set(self.find(lambda x: x not in (WALL, GLASS)))
        sealed = open_cells - walk
        if sealed:
            problems.append(f"{len(sealed)} floor cell(s) are sealed off, e.g. {sorted(sealed)[:3]}")

        assert not problems, f"{name}:\n  " + "\n  ".join(problems)
        return walk

    # ------------------------------------------------------------- output
    def write(self, path, name, theme="", ambient=None, loot=None, guard_loot=None,
              kits=None):
        """`theme` names the map kit game/level_art.gd dresses the level in
        (industrial, scientific). Art only: the sim never hashes it.
        `ambient` (0-100) makes it a DARK level (cognitohazard_lighting_plan.md);
        None writes no line, which is fully lit.
        `loot` is the dollars across every supply chest, `guard_loot` each
        guard's points and `kits` {guard: points} the exceptions (CLAUDE.md,
        "Loot as money"). None writes no line: the sim's defaults. Lines go
        where Level.ToText puts them, so an editor save changes nothing."""
        self.check(name)
        if ambient is not None:
            assert 0 <= ambient <= 100, f"ambient {ambient} is not a percentage"
        elif self.find(lambda x: x in (LAMP, SWITCH)):
            raise AssertionError(f"{name}: lamps on a fully lit level do nothing")
        kits = kits or {}
        for gid in kits:
            assert self.find(lambda x, gid=gid: x == gid), f"{name}: kit for absent guard {gid}"
        for v in [loot, guard_loot] + list(kits.values()):
            assert v is None or 0 <= v <= MAX_LOOT, f"{name}: loot figure {v} out of range"
        out = [f"name: {name}"] + ([f"theme: {theme}"] if theme else []) + \
              ([f"loot: {loot}"] if loot is not None else []) + \
              ([f"guard_loot: {guard_loot}"] if guard_loot is not None else []) + \
              ([f"ambient: {ambient}"] if ambient is not None else []) + [HEADER, "grid:"]
        out += ["".join(row) for row in self.g]
        for gid in sorted(self.routes):
            pts = self.routes[gid]
            if pts:
                out.append("> " + gid + " " + " ".join(f"{c},{r}" for c, r in pts))
        for gid in sorted(kits):
            out.append(f"kit: {gid} {kits[gid]}")
        # UTF-8 explicitly: guards past 'z' are Latin-1 letters, two bytes on
        # disk, which is what Level.FromText and Godot both read.
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(out) + "\n")

        count = lambda pred: len(self.find(pred))
        panes = len(self.runs(GLASS))
        doors = len(self.runs(DOOR))
        print(f"{name}: {self.W}x{self.H}, "
              f"{count(is_guard)} guards, "
              f"{count(lambda x: x == 'C')} chests, {count(lambda x: x == '$')} caches, "
              f"{count(lambda x: x == '!')} objective(s), "
              f"{doors} door runs, {panes} glass runs"
              + (f", ambient {ambient}%, {count(lambda x: x == LAMP)} lamps, "
                 f"{count(lambda x: x == SWITCH)} switches" if ambient is not None else "")
              + f" -> {path}")
