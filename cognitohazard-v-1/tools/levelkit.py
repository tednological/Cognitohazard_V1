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

Everything here is deterministic: the same script writes the same bytes.
"""
from collections import deque

WALL, FLOOR, GLASS, DOOR = '#', '.', '=', '+'
HEADER = ("# glyphs  # wall  . floor  = glass  + door  @ spawn  X exit  $ records"
          "  C chest  ! objective  a-z guard start  * sweep node")

# Must match sim/Level.cs.
GLASS_PANE_CELLS = 4
DOOR_LEAF_CELLS = 3


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
        for c, r in self.find(lambda x: 'a' <= x <= 'z'):
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
        for cell in self.find(lambda x: 'a' <= x <= 'z'):
            if cell not in walk:
                problems.append(f"guard at {cell} is sealed in")

        open_cells = set(self.find(lambda x: x not in (WALL, GLASS)))
        sealed = open_cells - walk
        if sealed:
            problems.append(f"{len(sealed)} floor cell(s) are sealed off, e.g. {sorted(sealed)[:3]}")

        assert not problems, f"{name}:\n  " + "\n  ".join(problems)
        return walk

    # ------------------------------------------------------------- output
    def write(self, path, name):
        self.check(name)
        out = [f"name: {name}", HEADER, "grid:"]
        out += ["".join(row) for row in self.g]
        for gid in sorted(self.routes):
            pts = self.routes[gid]
            if pts:
                out.append("> " + gid + " " + " ".join(f"{c},{r}" for c, r in pts))
        with open(path, "w") as f:
            f.write("\n".join(out) + "\n")

        count = lambda pred: len(self.find(pred))
        panes = len(self.runs(GLASS))
        doors = len(self.runs(DOOR))
        print(f"{name}: {self.W}x{self.H}, "
              f"{count(lambda x: 'a' <= x <= 'z')} guards, "
              f"{count(lambda x: x == 'C')} chests, {count(lambda x: x == '$')} caches, "
              f"{count(lambda x: x == '!')} objective(s), "
              f"{doors} door runs, {panes} glass runs -> {path}")
