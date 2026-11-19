#! python3
# The geometry of the flat slab (SlabWithOpenings.py): a slab on a grid of columns, with a stair opening in one
# bay and a round shaft in another, the columns as points for Mesh Surface to put a node on, and the openings'
# edges for it to refine along.
#
# The definition's GhPython component runs slab().
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

import Rhino.Geometry as rg


def slab(Length, Width, BaysX, BaysY, Stair, Shaft):
    """The GhPython component's code. The slab lies on XY from the origin, Length along X and Width along Y,
    on a column at every corner of a grid of BaysX by BaysY bays. The stair opening, Stair long and half as
    wide, sits in the middle of the second bay along X; the round shaft, Shaft across, in the middle of the
    last bay. Area is the slab's, the openings taken out."""
    l, w = float(Length), float(Width)
    nx, ny = max(1, int(BaysX)), max(1, int(BaysY))
    bx, by = l / nx, w / ny
    columns = [rg.Point3d(i * bx, j * by, 0) for j in range(ny + 1) for i in range(nx + 1)]

    outline = rg.Rectangle3d(rg.Plane.WorldXY, l, w).ToNurbsCurve()
    stair_centre = rg.Point3d(min(1.5, nx - 0.5) * bx, 0.5 * by, 0)
    stair = rg.Rectangle3d(rg.Plane(stair_centre, rg.Vector3d.ZAxis),
                           rg.Interval(-Stair / 2.0, Stair / 2.0), rg.Interval(-Stair / 4.0, Stair / 4.0)).ToNurbsCurve()
    shaft = rg.Circle(rg.Point3d((nx - 0.5) * bx, (ny - 0.5) * by, 0), Shaft / 2.0).ToNurbsCurve()
    surfaces = rg.Brep.CreatePlanarBreps([outline, stair, shaft], 1e-6)
    if not surfaces or len(surfaces) != 1:
        raise Exception("The openings do not fit in their bays: make them smaller, or the bays larger.")
    area = l * w - Stair * Stair / 2.0 - math.pi * Shaft * Shaft / 4.0
    return surfaces[0], columns, [stair, shaft], area
