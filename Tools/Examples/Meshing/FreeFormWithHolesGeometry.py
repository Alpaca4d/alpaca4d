#! python3
# The geometry of the free-form surface (FreeFormWithHoles.py): a doubly curved NURBS surface with a round hole
# and a rounded slot cut through it, two points on it, two curves on it, and the holes' edges.
#
# The definition's GhPython component runs freeform().
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

import Rhino.Geometry as rg


def freeform(Length, Width, Rise, Hole, Slot):
    """The GhPython component's code. The surface spans Length along X and Width along Y, rising Rise in its
    middle and twisting a little; a round hole Hole across and a slot Slot long, rounded at its corners, are
    cut through it from above. Points: two points on it, for columns or point loads. Curves: an isocurve
    across it near one long edge, and a circle drawn on it. Edges: the holes' edges, to refine along."""
    l, w = float(Length), float(Width)
    grid = []
    for i in range(7):
        for j in range(5):
            x, y = l * i / 6.0, w * j / 4.0
            z = Rise * math.sin(math.pi * x / l) * math.cos(math.pi * (y - w / 2.0) / (1.25 * w)) + 0.15 * Rise * x * y / (l * w)
            grid.append(rg.Point3d(x, y, z))
    surface = rg.NurbsSurface.CreateThroughPoints(grid, 7, 5, 3, 3, False, False)
    whole = surface.ToBrep()

    below = -10.0 * Rise - 10.0
    round_hole = rg.Circle(rg.Point3d(l / 3.0, w / 2.0, below), Hole / 2.0).ToNurbsCurve()
    slot = rg.Rectangle3d(rg.Plane(rg.Point3d(0.77 * l, 0.37 * w, below), rg.Vector3d.ZAxis),
                          rg.Interval(-Slot / 2.0, Slot / 2.0), rg.Interval(-Slot / 4.0, Slot / 4.0)).ToNurbsCurve()
    slot = rg.Curve.CreateFilletCornersCurve(slot, Slot / 8.0, 1e-6, 1e-3)
    cutters = []
    for c in (round_hole, slot):
        cutters.extend(rg.Curve.ProjectToBrep(c, whole, rg.Vector3d.ZAxis, 1e-3) or [])
    pieces = whole.Split(cutters, 1e-3)
    if not pieces:
        raise Exception("The holes could not be cut: make them smaller.")
    areas = [rg.AreaMassProperties.Compute(p).Area for p in pieces]
    cut = pieces[areas.index(max(areas))]

    def uv(x, y):
        ok, u, v = surface.ClosestPoint(rg.Point3d(x, y, 0))
        return u, v

    points = [surface.PointAt(*uv(l / 6.0, 0.75 * w)), surface.PointAt(*uv(0.87 * l, 0.17 * w))]
    u, v = uv(l / 2.0, 0.875 * w)
    ridge = surface.IsoCurve(0, v)
    ring = rg.Curve.ProjectToBrep(rg.Circle(rg.Point3d(0.77 * l, 0.77 * w, below), 0.1 * w).ToNurbsCurve(), cut,
                                  rg.Vector3d.ZAxis, 1e-3)[0]
    edges = [loop.To3dCurve() for loop in cut.Loops if loop.LoopType == rg.BrepLoopType.Inner]
    return cut, points, [ridge, ring], edges
