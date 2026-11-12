#! python3
# The geometry of the vault of bricks (MeshLoftBricks.py): the polylines Mesh Loft lofts, layer by layer through
# the vault's thickness, and the nodes it stands on.
#
# The vault is an arc of a circle through its feet and its crown, swept along its length. Each layer is a
# surface of the vault at one depth of it, from its inside to its outside: a row of polylines across the vault,
# one every step along it, all with the same number of points - which Mesh Loft needs - lofted into a mesh. The
# layers' meshes then match face for face, which Mesh Series to Brick needs, and each pair of neighbours makes a
# layer of bricks.
#
# The definition's GhPython component runs layers(). Everything before it is plain Python, Rhino or not, so that
# it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

try:
    import Rhino.Geometry as rg
    from Grasshopper import DataTree
    from Grasshopper.Kernel.Data import GH_Path
except ImportError:
    rg = None


def arcs(span, rise, length, thickness, around, along, layers):
    """The polylines as plain tuples: a list per layer, inside first, of along + 1 polylines of around + 1
    points each; and the points at the vault's two springings, where it stands."""
    radius = (span * span / 4.0 + rise * rise) / (2.0 * rise)
    centre = rise - radius                     # the circle's centre, on the axis, below the crown
    sweep = math.asin(span / 2.0 / radius)     # half the angle the vault spans
    if rise > radius:                          # more than a half circle: the feet curl back in
        sweep = math.pi - sweep
    out = []
    feet = []
    for k in range(layers + 1):
        r = radius - thickness / 2.0 + thickness * k / layers
        rows = []
        for j in range(along + 1):
            y = length * j / along
            row = []
            for i in range(around + 1):
                a = -sweep + 2.0 * sweep * i / around
                row.append((r * math.sin(a), y, centre + r * math.cos(a)))
            rows.append(row)
            feet += [row[0], row[-1]]
        out.append(rows)
    return out, feet


def layers(Span, Rise, Length, Thickness, Around, Along, Layers):
    """The GhPython component's code: a branch of polylines per layer, inside first, and the springings'
    points, for Rhino and Grasshopper."""
    around, along, count = max(2, int(Around)), max(1, int(Along)), max(1, int(Layers))
    polylines, feet = arcs(Span, Rise, Length, Thickness, around, along, count)
    tree = DataTree[object]()
    for k, rows in enumerate(polylines):
        for row in rows:
            tree.Add(rg.PolylineCurve([rg.Point3d(*p) for p in row]), GH_Path(k))
    info = "{0} layers of {1} x {2} bricks, {3} in all".format(count, around, along, count * around * along)
    return tree, [rg.Point3d(*p) for p in feet], info


if __name__ == "__main__":
    p, f = arcs(8.0, 2.5, 6.0, 0.4, 24, 12, 2)
    print(len(p), "layers,", len(p[0]), "polylines of", len(p[0][0]), "points,", len(f), "feet")
    print(p[0][0][0], p[-1][0][len(p[0][0]) // 2])
