"""The printed structure example's layers (Tools/Examples/Printing/PrintedStructureLayers.py) on shapes made here:
sections as polylines, as Printed Layers makes them from a Brep, without Rhino.

    python3 structure.py

Each shape is checked for bricks that fold through themselves - a corner where the brick's Jacobian is not
positive, which SSP Brick refuses - for bricks not joined to the print bed through their neighbours, and
for the bricks of a layer, the loops of a section and the print half way through, where they are known.
A tube bending over to 45 degrees is checked both ways: cut square to its axis, as a printer whose nozzle
tilts with it would lay it, and cut flat.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Tools", "Examples", "Printing"))
from PrintedStructureLayers import close_gaps, level_nodes, bricks_of, printed  # noqa: E402

H, SEGMENT, WIDTH = 0.01, 0.04, 0.04
fails = 0


def ok(what, good):
    global fails
    print("  [%s] %s" % ("ok" if good else "FAIL", what))
    if not good:
        fails += 1


def ellipse(cx, cy, a, b, z, n=400):
    return ([(cx + a * math.cos(2 * math.pi * i / n), cy + b * math.sin(2 * math.pi * i / n), z) for i in range(n)], True)


def circle(cx, cy, r, z):
    return ellipse(cx, cy, r, r, z)


def arc(r, z, a0, a1, n=200):
    return ([(r * math.cos(a0 + (a1 - a0) * i / (n - 1)), r * math.sin(a0 + (a1 - a0) * i / (n - 1)), z) for i in range(n)], False)


def rectangle(w, d, z, step=0.002):
    """A rectangle with sharp corners, w along x and d along y, as a polyline step apart."""
    corners = [(0, -d / 2), (w, -d / 2), (w, d / 2), (0, d / 2)]
    points = []
    for k in range(4):
        (x0, y0), (x1, y1) = corners[k], corners[(k + 1) % 4]
        n = max(1, int(round(math.hypot(x1 - x0, y1 - y0) / step)))
        points += [(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, z) for i in range(n)]
    return (points, True)


# The eight corners of a brick in its natural coordinates, as SSP Brick numbers them.
NATURAL = [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]


def jacobian(c, x, y, z):
    d = [[0.0] * 3 for _ in range(3)]
    for n, (a, b, g) in enumerate(NATURAL):
        dn = (a * (1 + b * y) * (1 + g * z) / 8, b * (1 + a * x) * (1 + g * z) / 8, g * (1 + a * x) * (1 + b * y) / 8)
        for i in range(3):
            for k in range(3):
                d[i][k] += dn[i] * c[n][k]
    (a, b, c_) = d
    return a[0] * (b[1] * c_[2] - b[2] * c_[1]) - a[1] * (b[0] * c_[2] - b[2] * c_[0]) + a[2] * (b[0] * c_[1] - b[1] * c_[0])


def shape(name, levels, layer_bricks=None, last_loops=None, normals=None):
    print(name)
    # As Printed Layers does: an open section whose ends are closer than the filament is wide is closed.
    nodes, carried = level_nodes(close_gaps(levels, WIDTH), SEGMENT, reach=3 * H + 0.5 * SEGMENT, normals=normals)
    bricks, base, paths, duration = bricks_of(nodes, carried, WIDTH, H, 1.0, 1, 2, 0.0, 5.0, normals)
    folded = sum(1 for b in bricks if min(jacobian(b[0], *n) for n in NATURAL) <= 0)
    ok("%d bricks, none folds through itself (%d do)" % (len(bricks), folded), bricks and folded == 0)

    # Joined to the bed: the bricks' corners, welded where they meet, all lead down to the base.
    key = lambda p: (round(p[0], 7), round(p[1], 7), round(p[2], 7))
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for b in bricks:
        keys = [key(p) for p in b[0]]
        for k in keys[1:]:
            parent[find(k)] = find(keys[0])
    bed = set(find(key(p)) for p in base)
    floating = sum(1 for b in bricks if find(key(b[0][0])) not in bed)
    ok("all joined to the print bed (%d not)" % floating, floating == 0)

    if layer_bricks is not None:
        counts = set(sum(1 for b in bricks if b[1] == k) for k in range(len(levels) - 1))
        ok("%s bricks in every layer: %s" % (layer_bricks, sorted(counts)), counts == {layer_bricks})
    if last_loops is not None:
        got = [closed for _, closed in nodes[-1]]
        ok("the last section's loops, closed or open: %s" % got, got == last_loops)
    done, nozzle, axis, layers = printed(bricks, paths, H, duration / 2, 5.0)
    ok("half way: %d of %d bricks, %.2f layers of %d" % (len(done), len(bricks), layers, len(levels) - 1),
       0 < len(done) < len(bricks) and abs(layers - (len(levels) - 1) / 2.0) < (len(levels) - 1) / 4.0)
    return bricks, nozzle, axis


def bent_tube(tilted, r=0.15, up=0.2, bend=0.4, angle=math.pi / 4, on=0.3, n=400):
    """A tube of radius r rising up, then bending over through angle on a bend of radius bend towards x,
    then on straight: its levels every H along its axis, square to it, with their normals, if tilted; else
    its flat sections every H up, a slanted rim at its top cut into open arcs."""
    total = up + bend * angle + on

    def axis(s):
        if s <= up:
            return (0.0, 0.0, s), 0.0
        if s <= up + bend * angle:
            a = (s - up) / bend
            return (bend * (1 - math.cos(a)), 0.0, up + bend * math.sin(a)), a
        (x, _, z), a = axis(up + bend * angle)
        d = s - up - bend * angle
        return (x + d * math.sin(angle), 0.0, z + d * math.cos(angle)), angle

    def point(s, phi):
        (x, y, z), a = axis(s)
        return (x + r * math.cos(phi) * math.cos(a), r * math.sin(phi), z - r * math.cos(phi) * math.sin(a))

    phis = [2 * math.pi * i / n for i in range(n)]
    if tilted:
        count = int((total - 2e-4) / H) + 1
        levels = [[([point(1e-4 + k * H, phi) for phi in phis], True)] for k in range(count)]
        normals = [(math.sin(axis(1e-4 + k * H)[1]), 0.0, math.cos(axis(1e-4 + k * H)[1])) for k in range(count)]
        return levels, normals
    levels, top = [], max(point(total, phi)[2] for phi in phis)
    for k in range(int((top - 2e-4) / H) + 1):
        z, runs, run = 1e-4 + k * H, [], []
        for phi in phis:
            if point(total, phi)[2] < z:
                if run:
                    runs.append(run)
                run = []
                continue
            lo, hi = 0.0, total
            for _ in range(50):
                mid = (lo + hi) / 2
                lo, hi = (mid, hi) if point(mid, phi)[2] < z else (lo, mid)
            run.append(point(lo, phi))
        if run:
            runs.append(run)
        if len(runs) == 1 and len(runs[0]) == n:
            levels.append([(runs[0], True)])
        else:
            if len(runs) > 1 and runs[0][0] == point(0, 0) or (len(runs) > 1 and phis[0] == 0 and point(total, 0)[2] >= z):
                runs = [runs[-1] + runs[0]] + runs[1:-1]
            levels.append([(run, False) for run in runs if len(run) > 1])
    return levels, None


TOP = 31
shape("An upright cylinder of radius 250 mm: the cylinder example's 40 bricks a lap, 2 up",
      [[circle(0, 0, 0.25, k * H)] for k in range(TOP)], layer_bricks=80, last_loops=[True])
shape("Leaning 3 mm a layer", [[circle(0.003 * k, 0, 0.25, k * H)] for k in range(TOP)], last_loops=[True])
shape("A cone, 250 to 120 mm", [[circle(0, 0, 0.25 - 0.13 * k / 30.0, k * H)] for k in range(TOP)], last_loops=[True])
shape("Flaring, 120 to 250 mm", [[circle(0, 0, 0.12 + 0.13 * k / 30.0, k * H)] for k in range(TOP)], last_loops=[True])
shape("Two rings merging into one at 10 layers",
      [[circle(-0.12, 0, 0.1, k * H), circle(0.12, 0, 0.1, k * H)] if k < 10 else [ellipse(0, 0, 0.225, 0.1, k * H)]
       for k in range(TOP)], last_loops=[True])
shape("A ring opening at 20 layers",
      [[circle(0, 0, 0.25, k * H)] if k < 20 else [arc(0.25, k * H, 0.3, 2 * math.pi - 0.3)] for k in range(TOP)],
      last_loops=[False])
shape("A narrow rectangle 95 x 430 mm with sharp corners, as the example's long leg",
      [[rectangle(0.095, 0.43, k * H)] for k in range(TOP)], last_loops=[True])
# Closest points crowd into the valleys of a twisting wall and part on its lobes: the nodes are carried
# round with the twist instead, evenly, every layer cut the same.
shape("A column of six lobes turning 60 degrees over 0.6 m: every layer the same bricks",
      [[([(r * math.cos(t), r * math.sin(t), k * H) for t, r in
          ((2 * math.pi * i / 600, 0.16 * (1 + 0.18 * math.cos(6 * (2 * math.pi * i / 600 - math.pi / 3 * k / 60.0))))
           for i in range(600))], True)] for k in range(61)], layer_bricks=64, last_loops=[True])
# A loft through curves that do not quite close cuts into open sections: a 10 mm slit, which the filament fills.
shape("A ring cut with a 10 mm slit: printed closed",
      [[arc(0.25, k * H, 0.02, 2 * math.pi - 0.02)] for k in range(TOP)], layer_bricks=80, last_loops=[True])
# Without the inside of a tight turn drawn in, 120 of these bricks fold.
shape("A pointed ellipse 60 x 430 mm, leaning: tight turns at both ends",
      [[ellipse(0.002 * k, 0, 0.03, 0.215, k * H)] for k in range(TOP)], last_loops=[True])

# Cut square to its axis, a bent tube is printed in layers that lean with it: every layer the same bricks, each
# thicker on the outside of the bend than on its inside - Height (1 +- r / bend) - and the nozzle tilting.
levels, normals = bent_tube(True)
bricks, nozzle, axis = shape("A tube of radius 150 mm rising 0.2 m, then bending over to 45 degrees: layers square to its axis",
                             levels, layer_bricks=48, last_loops=[True], normals=normals)
# Half way round the bend: how far the middle of each brick's top is from the middle of its bottom, along
# the level's normal - twice that of its lower brick of the two up.
middle = [b for b in bricks if b[1] == 35]


def centre(c, ids):
    return [sum(c[i][m] for i in ids) / len(ids) for m in range(3)]


thick = [2 * sum((centre(b[0], (4, 7))[m] - centre(b[0], (0, 3))[m]) * normals[35][m] for m in range(3)) for b in middle]
ok("half way round the bend, the layers %.1f to %.1f mm thick: 6.3 inside, 13.8 outside" % (1000 * min(thick), 1000 * max(thick)),
   abs(min(thick) - 0.00625) < 0.0006 and abs(max(thick) - 0.01375) < 0.0006)
ok("the nozzle half way leans %.0f degrees" % math.degrees(math.acos(axis[2])), 5 < math.degrees(math.acos(axis[2])) < 45)
levels, _ = bent_tube(False)
shape("The same tube cut flat: each layer over the one below by up to 10 mm, open arcs at its slanted rim", levels,
      last_loops=[False])

print("\nAll checks passed." if fails == 0 else "\n%d check(s) failed." % fails)
sys.exit(1 if fails else 0)
