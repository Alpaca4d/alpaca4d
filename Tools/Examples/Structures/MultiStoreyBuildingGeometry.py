#! python3
# The geometry of the multi-storey building (MultiStoreyBuilding.py): a reinforced concrete frame on a grid of
# bays, a column at every corner of the grid and a beam along every line of it at every floor, and a slab on
# each floor.
#
# Every member is a straight line between two nodes. The beams are cut where the slab's mesh has a node on
# them, so that slab and beams share their nodes and work together; the columns run storey to storey.
#
# The definition's GhPython component runs building(). Everything before it is plain Python, Rhino or not, so
# that it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None


def frame(bays_x, bays_y, bay_x, bay_y, storeys, storey, split):
    """The building as plain tuples: columns, the windward columns and the width of wall each one catches the
    wind on, beams, each floor's quads, and the feet.

    The wind blows along +x, onto the face at x = 0: a column there takes the wall halfway to its
    neighbours, half a bay at a corner and a whole one between."""
    def at(i, j, k):
        return (i * bay_x, j * bay_y, k * storey)

    m = {"Columns": [], "WindwardColumns": [], "WindWidths": [], "Beams": [], "Floors": [], "Feet": []}
    for i in range(bays_x + 1):
        for j in range(bays_y + 1):
            m["Feet"].append(at(i, j, 0))
            for k in range(storeys):
                column = (at(i, j, k), at(i, j, k + 1))
                if i == 0:
                    m["WindwardColumns"].append(column)
                    m["WindWidths"].append(bay_y / (2.0 if j in (0, bays_y) else 1.0))
                else:
                    m["Columns"].append(column)

    def step(a, b, n):
        """The points from a to b in n equal steps, both ends included."""
        return [tuple(a[c] + (b[c] - a[c]) * s / n for c in range(3)) for s in range(n + 1)]

    for k in range(1, storeys + 1):
        # Beams along x on every line of y, and along y on every line of x, each bay in split pieces.
        for j in range(bays_y + 1):
            for i in range(bays_x):
                points = step(at(i, j, k), at(i + 1, j, k), split)
                m["Beams"] += list(zip(points[:-1], points[1:]))
        for i in range(bays_x + 1):
            for j in range(bays_y):
                points = step(at(i, j, k), at(i, j + 1, k), split)
                m["Beams"] += list(zip(points[:-1], points[1:]))
        # The floor: split by split quads a bay, their corners on the beams' cuts.
        xs = [i * bay_x / split for i in range(bays_x * split + 1)]
        ys = [j * bay_y / split for j in range(bays_y * split + 1)]
        m["Floors"].append((xs, ys, k * storey))
    return m


def _mesh(xs, ys, z):
    """A mesh of quads over xs by ys at height z."""
    mesh = rg.Mesh()
    for y in ys:
        for x in xs:
            mesh.Vertices.Add(x, y, z)
    cols = len(xs)
    for r in range(len(ys) - 1):
        for c in range(cols - 1):
            a = r * cols + c
            mesh.Faces.AddFace(a, a + 1, a + cols + 1, a + cols)
    return mesh


def building(BaysX, BaysY, BayX, BayY, Storeys, StoreyHeight, Split):
    """The GhPython component's code: the frame's columns - the windward ones apart, with the width of wall
    each catches the wind on - its beams, a slab mesh a floor and the feet, as Rhino lines, meshes and
    points."""
    bays_x, bays_y, storeys, split = max(1, int(BaysX)), max(1, int(BaysY)), max(1, int(Storeys)), max(1, int(Split))
    m = frame(bays_x, bays_y, BayX, BayY, storeys, StoreyHeight, split)

    def lines(key):
        return [rg.Line(rg.Point3d(*a), rg.Point3d(*b)) for a, b in m[key]]

    slabs = [_mesh(*floor) for floor in m["Floors"]]
    quads = sum(s.Faces.Count for s in slabs)
    info = "{0} x {1} m, {2:.1f} m tall\n{3} columns, {4} beams, {5} slab quads".format(
        bays_x * BayX, bays_y * BayY, storeys * StoreyHeight, len(m["Columns"]) + len(m["WindwardColumns"]),
        len(m["Beams"]), quads)
    return (lines("Columns"), lines("WindwardColumns"), m["WindWidths"], lines("Beams"), slabs,
            [rg.Point3d(*p) for p in m["Feet"]], info)


if __name__ == "__main__":
    m = frame(5, 3, 6.0, 5.0, 5, 3.5, 4)
    for k in sorted(m):
        print(k, len(m[k]))
