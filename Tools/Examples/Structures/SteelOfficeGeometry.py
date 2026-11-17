#! python3
# The geometry of the steel office (SteelOffice.py): a steel frame on a grid of bays, a column at every
# crossing of the grid, at every floor a beam along x on every line of the grid, which carry the floor,
# and a tie along y on every line of it.
#
# The floor spans along y, from one beam along x to the next: each beam carries the width of floor
# halfway to its neighbours, half a bay at the edge and a whole one inside. The beams along x are cut
# at their middle, so that their sag shows at a node. The wind blows along +x, onto the face at x = 0:
# a column there catches the wall halfway to its neighbours.
#
# The definition's GhPython component runs office(). Everything before it is plain Python, Rhino or
# not, so that it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None


def frame(bays_x, bays_y, bay_x, bay_y, storeys, storey):
    """The frame as plain tuples: the columns, the windward ones apart with the width of wall each
    catches the wind on; the floor beams and the roof beams along x, each with the width of floor it
    carries; the ties along y; and the feet."""
    def at(i, j, k):
        return (i * bay_x, j * bay_y, k * storey)

    def width(j):
        return bay_y / (2.0 if j in (0, bays_y) else 1.0)

    m = {"Columns": [], "WindwardColumns": [], "WindWidths": [], "FloorBeams": [], "FloorWidths": [],
         "RoofBeams": [], "RoofWidths": [], "Ties": [], "Feet": []}
    for i in range(bays_x + 1):
        for j in range(bays_y + 1):
            m["Feet"].append(at(i, j, 0))
            for k in range(storeys):
                column = (at(i, j, k), at(i, j, k + 1))
                if i == 0:
                    m["WindwardColumns"].append(column)
                    m["WindWidths"].append(width(j))
                else:
                    m["Columns"].append(column)

    for k in range(1, storeys + 1):
        beams, widths = ("RoofBeams", "RoofWidths") if k == storeys else ("FloorBeams", "FloorWidths")
        for j in range(bays_y + 1):
            for i in range(bays_x):
                a, b = at(i, j, k), at(i + 1, j, k)
                middle = tuple((a[c] + b[c]) / 2.0 for c in range(3))
                for piece in ((a, middle), (middle, b)):
                    m[beams].append(piece)
                    m[widths].append(width(j))
        for i in range(bays_x + 1):
            for j in range(bays_y):
                m["Ties"].append((at(i, j, k), at(i, j + 1, k)))
    return m


def office(BaysX, BaysY, BayX, BayY, Storeys, StoreyHeight):
    """The GhPython component's code: the frame's members as Rhino lines, the widths each loaded one
    carries, the feet as points, and a line about its size."""
    bays_x, bays_y, storeys = max(1, int(BaysX)), max(1, int(BaysY)), max(1, int(Storeys))
    m = frame(bays_x, bays_y, BayX, BayY, storeys, StoreyHeight)

    def lines(key):
        return [rg.Line(rg.Point3d(*a), rg.Point3d(*b)) for a, b in m[key]]

    columns = len(m["Columns"]) + len(m["WindwardColumns"])
    beams = len(m["FloorBeams"]) + len(m["RoofBeams"])
    info = "{0:g} x {1:g} m on plan, {2:.1f} m tall\n{3} columns, {4} beam pieces, {5} ties: {6} elements".format(
        bays_x * BayX, bays_y * BayY, storeys * StoreyHeight, columns, beams, len(m["Ties"]),
        columns + beams + len(m["Ties"]))
    return (lines("Columns"), lines("WindwardColumns"), m["WindWidths"], lines("FloorBeams"), m["FloorWidths"],
            lines("RoofBeams"), m["RoofWidths"], lines("Ties"), [rg.Point3d(*p) for p in m["Feet"]], info)


if __name__ == "__main__":
    m = frame(4, 2, 6.0, 5.0, 4, 3.5)
    for k in sorted(m):
        print(k, len(m[k]))
    print("floor width", sum(m["FloorWidths"]) / 2.0, "m of beam-bay per floor level x3")
