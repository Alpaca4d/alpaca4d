"""Twisted ring truss: the geometry, as polylines of chords, frames and bracing.

A closed ring of slices, each slice a polygon: Sides corners on an ellipse Width wide and Depth deep, in a
vertical plane through the centre of the ring. From one slice to the next the polygon turns a little
more, so that the profile twists as it goes round. Chords run from slice to slice, the frames are the
polygons and bracing crosses the faces between two slices.

The twist is counted in steps, each the smallest turn that brings the profile back onto itself, so
that the ring closes: 360/Sides degrees when Width = Depth, otherwise 180 with an even number of sides
and 360 with an odd one. With 3 sides and 1 step the three chords join into one that goes round three
times.

The members are joined into polylines, end to end, a polyline breaking wherever more or fewer than two
members meet:
    Chords - each chord round the ring, closed.
    Frames - each slice's polygon, closed.
    Bracing - the diagonals, as they run on from one face to the next.
An Alpaca4d element is a straight line between two nodes: explode the polylines before making elements.
Nodes are the points the members end at, each once; MemberCount, NodeCount and TotalLength count the
straight members, and Info says it all in words.

Runs in GhPython (Rhino 7 and 8) and in Rhino 8's Python 3 component alike.
"""
from __future__ import division

import math
import Rhino.Geometry as rg

BRACING = ["None", "Diagonal", "Zigzag", "Checkerboard", "Cross", "K"]
GROUPS = ["Chords", "Frames", "Bracing"]


def given(name, default):
    """An input's value, or its default when nothing is connected to it."""
    value = globals().get(name)
    return default if value is None else value


def choice(name, default, names):
    """A drop-down's choice, by its number or by its name."""
    value = given(name, default)
    try:
        return max(0, min(len(names) - 1, int(value)))
    except ValueError:
        wanted = str(value).strip().lower()
        for i, n in enumerate(names):
            if n.lower() == wanted:
                return i
        raise ValueError("{0} is one of {1}, or its number from 0.".format(name, ", ".join(names)))


def message(level, text):
    """A balloon on the component, in Grasshopper; nothing outside it."""
    if "ghenv" in globals():
        import Grasshopper
        ghenv.Component.AddRuntimeMessage(getattr(Grasshopper.Kernel.GH_RuntimeMessageLevel, level), text)


# ---------------------------------------------------------------- the inputs

radius_x = float(given("RadiusX", 20.0))         # the ring in plan, an ellipse [m]
radius_y = float(given("RadiusY", 20.0))
wave = float(given("WaveHeight", 0.0))           # how far it rises and falls [m]...
waves = int(given("Waves", 2))                   # ...and how many times round
swell = float(given("Swell", 0.0))               # how much the profile grows and shrinks [%]...
swells = int(given("Swells", 2))                 # ...and how many times round
sides = int(given("Sides", 4))                   # corners of the profile
width = float(given("Width", 1.5))               # of the ellipse the corners sit on [m]
depth = float(given("Depth", 3.0))
corner_start = float(given("CornerStart", 0.0))  # where the first corner sits round it [deg]
steps = int(given("TwistSteps", 2))              # the twist, in steps
twist_start = float(given("TwistStart", 0.0))    # how far the first slice is turned [deg]
slices = int(given("Slices", 48))
bracing = BRACING[choice("Bracing", 2, BRACING)]

if sides < 3:
    raise ValueError("Sides must be 3 or more.")
if slices < 3:
    raise ValueError("Slices must be 3 or more.")
if width <= 0 or depth <= 0:
    raise ValueError("Width and Depth must be more than 0.")
if swell <= -100:
    raise ValueError("Swell must be more than -100 %: the profile would vanish.")

# ---------------------------------------------------------------- the nodes

regular = abs(width - depth) < 1e-9
step = 360.0 / sides if regular else (180.0 if sides % 2 == 0 else 360.0)
twist = steps * step
# How many corners the last slice has turned past the first: the slice after the last is the first,
# its corner j landing on the first slice's corner j + shift.
shift = steps * int(round(step * sides / 360.0)) % sides

points = []   # every node, by its number: the corners slice by slice, then the nodes K bracing adds
for s in range(slices):
    around = 2.0 * math.pi * s / slices
    turned = math.radians(twist_start + twist * s / slices)
    size = 1.0 + swell / 100.0 * math.cos(swells * around) if swells else 1.0
    cx, cy = radius_x * math.cos(around), radius_y * math.sin(around)
    cz = wave * math.sin(waves * around)
    for j in range(sides):
        at = math.radians(corner_start + 360.0 * j / sides)
        u, v = width / 2.0 * math.cos(at), depth / 2.0 * math.sin(at)
        outwards = size * (u * math.cos(turned) - v * math.sin(turned))
        up = size * (u * math.sin(turned) + v * math.cos(turned))
        points.append(rg.Point3d(cx + outwards * math.cos(around), cy + outwards * math.sin(around), cz + up))


def node(s, j):
    """Corner j of slice s. The slice after the last is the first, turned on by the twist."""
    if s == slices:
        s, j = 0, j + shift
    return s * sides + j % sides


split = {}    # the frame members K bracing meets, by their two nodes: the node at their middle


def middle(a, b):
    key = (min(a, b), max(a, b))
    if key not in split:
        p, q = points[a], points[b]
        points.append(rg.Point3d((p.X + q.X) / 2.0, (p.Y + q.Y) / 2.0, (p.Z + q.Z) / 2.0))
        split[key] = len(points) - 1
    return split[key]


# ---------------------------------------------------------------- the members, as pairs of nodes

members = dict((g, []) for g in GROUPS)

# Chords: each corner to the same corner of the next slice.
for s in range(slices):
    for j in range(sides):
        members["Chords"].append((node(s, j), node(s + 1, j)))

# Bracing, in the face between corners j and j + 1 of two slices. Diagonal crosses each face the same
# way, Zigzag turns from one bay to the next, Checkerboard from one face to the next as well, Cross
# takes both diagonals, and K runs from both corners of the face to the middle of the next slice's
# frame member. Done before the frames, which K splits.
if bracing != "None":
    for s in range(slices):
        for j in range(sides):
            a, b = node(s, j), node(s, j + 1)
            d, c = node(s + 1, j), node(s + 1, j + 1)
            if bracing == "K":
                m = middle(d, c)
                members["Bracing"].extend([(a, m), (b, m)])
            elif bracing == "Cross":
                members["Bracing"].extend([(a, c), (b, d)])
            elif (bracing == "Zigzag" and s % 2) or (bracing == "Checkerboard" and (s + j) % 2):
                members["Bracing"].append((b, d))
            else:
                members["Bracing"].append((a, c))

# Frames: the polygon of every slice; a member K bracing meets is two, to its middle and on.
for s in range(slices):
    for j in range(sides):
        a, b = node(s, j), node(s, j + 1)
        m = split.get((min(a, b), max(a, b)))
        members["Frames"].extend([(a, b)] if m is None else [(a, m), (m, b)])

# ---------------------------------------------------------------- joined into polylines


def join(pairs):
    """The members as runs of nodes, end to end: a run starts and stops where other than two members
    meet, and what is left after those is closed loops, each ending on the node it starts from."""
    ends = {}
    for i, (a, b) in enumerate(pairs):
        ends.setdefault(a, []).append(i)
        ends.setdefault(b, []).append(i)
    used = [False] * len(pairs)

    def walk(start, i):
        run = [start]
        while True:
            used[i] = True
            a, b = pairs[i]
            here = b if a == run[-1] else a
            run.append(here)
            if len(ends[here]) != 2:
                return run
            onward = [k for k in ends[here] if not used[k]]
            if not onward:
                return run
            i = onward[0]

    runs = []
    for n in sorted(ends):
        if len(ends[n]) != 2:
            runs.extend(walk(n, i) for i in ends[n] if not used[i])
    for i in range(len(pairs)):
        if not used[i]:
            runs.append(walk(pairs[i][0], i))
    return runs


def polylines(group):
    curves = []
    for run in join(members[group]):
        line = rg.Polyline()
        for n in run:
            line.Add(points[n])
        curves.append(rg.PolylineCurve(line))
    return curves


Chords = polylines("Chords")
Frames = polylines("Frames")
Bracing = polylines("Bracing")

used = sorted(set(n for g in GROUPS for pair in members[g] for n in pair))
Nodes = [points[n] for n in used]
MemberCount = sum(len(members[g]) for g in GROUPS)
NodeCount = len(used)
TotalLength = sum(points[a].DistanceTo(points[b]) for g in GROUPS for a, b in members[g])

Info = "\n".join(
    ["Twist {0:g} deg: {1} step{2} of {3:g}.".format(twist, steps, "" if abs(steps) == 1 else "s", step),
     "{0} members, {1} nodes, {2:.1f} m of members in all.".format(MemberCount, NodeCount, TotalLength)] +
    ["{0}: {1} members in {2} polyline{3}".format(g, len(members[g]), len(globals()[g]),
                                                   "" if len(globals()[g]) == 1 else "s") for g in GROUPS])

if "ghenv" in globals():
    ghenv.Component.Message = "{0} members".format(MemberCount)
if slices % 2 and bracing in ("Zigzag", "Checkerboard"):
    message("Remark", "With an odd number of slices, {0} does not meet up round the ring.".format(bracing))
