#! python3
# The geometry of the high-tension tower (HighTensionTower.py): a lattice pylon that carries four
# conductors, two a side, and an earth wire. A square body tapers from its feet to a waist, a straight shaft rises from
# there, and three pairs of arms stand out of the shaft, two for the conductors and one at the top for the
# earth wire.
#
# Every member is a line between two nodes of the lattice: the legs and the arms' chords run unbroken
# through the nodes, as beams; the rest - the faces' crossed bracing, the struts round each level, the
# arms' rings and diagonals - are bars between those nodes, as trusses. A bar never ends on another bar
# alone: a node only bars meet would be free to turn, and the model would not solve.
#
# The definition's GhPython component runs pylon(). Everything before it is plain Python, Rhino or not, so
# that it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None


# How much of a face of the lattice the wind meets: the solidity ratio, about a fifth to a third for a
# tower of angles or tubes. The rest blows through.
SOLIDITY = 0.25


def _levels(bottom, top, most):
    """Levels from bottom to top, evenly spaced and no more than most apart."""
    n = max(1, int(math.ceil((top - bottom) / most - 1e-9)))
    return [bottom + (top - bottom) * k / n for k in range(n + 1)]


def _corners(half, z):
    """The four corners of a square level, half its width from the axis, counter-clockwise from -x -y."""
    return [(-half, -half, z), (half, -half, z), (half, half, z), (-half, half, z)]


def lattice(height, base, waist, waist_height, panels, spacing, arm):
    """The tower as plain tuples: a dict of lists of (start, end) members and of points, and its levels.

    height        to the top of the shaft [m]
    base          width of the square at the feet [m]
    waist         width of the shaft [m]
    waist_height  where the body stops tapering and the shaft begins [m]
    panels        panels in the body
    spacing       between the arms, level to level [m]
    arm           reach of a conductor arm, from the tower's axis [m]
    """
    depth = waist                      # an arm is as deep, at the shaft, as the shaft is wide
    top_arm = height - depth           # the earth wire's arms, their bottom chords
    arms = [top_arm - 2 * spacing, top_arm - spacing, top_arm]
    notes = []
    if arms[0] < waist_height + 0.5 * waist:
        # The lower arms would be in the body: lift them all, into the shaft.
        lift = waist_height + 0.5 * waist - arms[0]
        notes.append("Arms {0:.1f} m apart do not fit above the waist: spaced {1:.1f} m".format(
            spacing, spacing - lift / 2.0))
        spacing -= lift / 2.0
        arms = [top_arm - 2 * spacing, top_arm - spacing, top_arm]

    # The levels: the body's panels, then the shaft's, with a level at the bottom and top of every arm and
    # panels no taller than the shaft is wide.
    levels = [waist_height * k / panels for k in range(panels + 1)]
    marks = sorted(set([waist_height] + arms + [a + depth for a in arms] + [height]))
    for lo, hi in zip(marks[:-1], marks[1:]):
        levels += _levels(lo, hi, waist)[1:]

    def half_width(z):
        if z >= waist_height:
            return waist / 2.0
        return base / 2.0 + (waist - base) / 2.0 * z / waist_height

    rings = [_corners(half_width(z), z) for z in levels]
    m = {"Legs": [], "WindwardLegs": [], "WindWidths": [], "Bracing": [], "Struts": [], "Chords": [], "Feet": rings[0],
         "WirePoints": [], "EarthPoints": []}

    for k in range(len(levels) - 1):
        lo, hi = rings[k], rings[k + 1]
        # The windward face is at -x: its two legs, corners 0 and 3, take the wind on half the face each.
        face = (half_width(levels[k]) + half_width(levels[k + 1]))
        for c in range(4):
            leg = (lo[c], hi[c])
            if c in (0, 3):
                m["WindwardLegs"].append(leg)
                m["WindWidths"].append(SOLIDITY * face / 2.0)
            else:
                m["Legs"].append(leg)
        # Each face crossed: two bars from corner to corner, which pass each other without a node.
        for c in range(4):
            d = (c + 1) % 4
            m["Bracing"].append((lo[c], hi[d]))
            m["Bracing"].append((lo[d], hi[c]))
    # A strut round each level, and a diagonal across every arm's level to keep the square square.
    for k, ring in enumerate(rings):
        if k == 0:
            continue
        for c in range(4):
            m["Struts"].append((ring[c], ring[(c + 1) % 4]))
        if any(abs(levels[k] - x) < 1e-6 for x in marks):
            m["Struts"].append((ring[0], ring[2]))

    # The arms: on each side of the shaft, four chords from its corners - two level, two falling from the
    # level above - meeting at the tip; a ring of bars where they are divided, and a diagonal in each face.
    half = waist / 2.0
    for n, z in enumerate(arms):
        reach = arm if n < 2 else 0.6 * arm
        for side in (1, -1):
            tip = (side * reach, 0.0, z)
            roots = [(side * half, -half, z), (side * half, half, z), (side * half, half, z + depth),
                     (side * half, -half, z + depth)]
            count = max(2, int(round((reach - half) / (0.8 * waist))))
            points = []
            for root in roots:
                chord = [tuple(root[i] + (tip[i] - root[i]) * j / count for i in range(3)) for j in range(count + 1)]
                for a, b in zip(chord[:-1], chord[1:]):
                    m["Chords"].append((a, b))
                points.append(chord)
            for j in range(1, count):
                ring = [p[j] for p in points]
                for c in range(4):
                    m["Bracing"].append((ring[c], ring[(c + 1) % 4]))
            for j in range(count - 1):
                # One diagonal in each of the four faces of each panel, from the inner ring out.
                for c in range(4):
                    m["Bracing"].append((points[c][j], points[(c + 1) % 4][j + 1]))
            (m["EarthPoints"] if n == 2 else m["WirePoints"]).append(tip)

    notes.append("Arms at {0}".format(", ".join("{0:.1f} m".format(z) for z in arms)))
    return m, levels, notes


def _count(m):
    return sum(len(m[k]) for k in ("Legs", "WindwardLegs", "Bracing", "Struts", "Chords"))


def pylon(Height, Base, Waist, WaistHeight, Panels, Spacing, Arm, Weight, Wind):
    """The GhPython component's code: the tower's members, its feet, and the conductors' and earth wire's
    pull on the arms' tips - their Weight down, and the Wind on them across the line, along +x - as Rhino
    lines, points and vectors. The earth wire is lighter than a conductor and catches less wind: half of
    each."""
    m, levels, notes = lattice(Height, Base, Waist, WaistHeight, int(Panels), Spacing, Arm)

    def line(member):
        return rg.Line(rg.Point3d(*member[0]), rg.Point3d(*member[1]))

    def lines(key):
        return [line(x) for x in m[key]]

    def points(key):
        return [rg.Point3d(*p) for p in m[key]]

    wires = points("WirePoints") + points("EarthPoints")
    share = [1.0] * len(m["WirePoints"]) + [0.5] * len(m["EarthPoints"])
    weights = [rg.Vector3d(0, 0, -Weight * s) for s in share]
    winds = [rg.Vector3d(Wind * s, 0, 0) for s in share]
    info = "{0} members, {1} levels, {2:.1f} m tall\n{3}".format(_count(m), len(levels), Height, "\n".join(notes))
    return (lines("Legs"), lines("WindwardLegs"), m["WindWidths"], lines("Bracing"), lines("Struts"),
            lines("Chords"), points("Feet"), wires, weights, winds, info)


if __name__ == "__main__":
    m, levels, notes = lattice(42.0, 8.0, 2.4, 22.0, 7, 6.0, 7.0)
    print(_count(m), "members,", len(levels), "levels")
    for k in sorted(m):
        print(k, len(m[k]))
    print("\n".join(notes))
