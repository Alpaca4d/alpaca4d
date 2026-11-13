#! python3
# The geometry of the connection (ConnectionDesign.py): the cast steel node of a tree column, where a trunk tube
# splits into branches, as the surface its shell elements are meshed on - and, once it is meshed, the nodes at the
# end of each tube, where it is held and loaded.
#
# The node is a sphere with a tube welded on for the trunk, down, and one for each branch, up and out. The
# solids are joined into one, and the flat ends of the tubes are cut away: what is left is the steel's surface,
# open at the tube ends, its edges where tube meets sphere kept as the weld lines.
#
# The definition's GhPython components run node() and ends().
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

import Rhino
import Rhino.Geometry as rg


def _axes(trunk, branch, lean, branches):
    """The tubes' axes from the node's centre, as (direction, length, radius): the trunk straight down, the
    branches lean degrees off vertical, evenly round."""
    out = [(rg.Vector3d(0, 0, -1), trunk[0], trunk[1])]
    a = math.radians(lean)
    for k in range(branches):
        b = 2.0 * math.pi * k / branches
        out.append((rg.Vector3d(math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a)), branch[0], branch[1]))
    return out


def node(TrunkDiameter, TrunkLength, BranchDiameter, BranchLength, Lean, Branches, Sphere):
    """The GhPython component's code: the node's surface, a Brep open at the tubes' ends, and a plane at the
    end of each tube - the trunk's first - square to it, facing out."""
    # In metres, as the model is, whatever units the Rhino document is in.
    tol = 0.0001
    angle = math.radians(1.0)
    axes = _axes((TrunkLength, TrunkDiameter / 2.0), (BranchLength, BranchDiameter / 2.0), Lean, int(Branches))
    solids = [rg.Brep.CreateFromSphere(rg.Sphere(rg.Point3d.Origin, Sphere / 2.0))]
    ends = []
    for direction, length, radius in axes:
        end = rg.Point3d.Origin + direction * length
        # Each tube starts inside the sphere, so that their joins are clean circles round it.
        rail = rg.LineCurve(rg.Point3d.Origin + direction * (0.25 * Sphere), end)
        pipe = rg.Brep.CreatePipe(rail, radius, False, rg.PipeCapMode.Flat, True, tol, angle)[0]
        solids.append(pipe)
        ends.append(rg.Plane(end, direction))
    joined = rg.Brep.CreateBooleanUnion(solids, tol)
    if not joined:
        raise Exception("The tubes and the sphere could not be joined: try other sizes or angles.")
    solid = joined[0]
    # The flat ends of the tubes, cut away: a planar face lying on one of the ends' planes.
    keep = []
    for f in solid.Faces:
        centre = rg.AreaMassProperties.Compute(f.DuplicateFace(False)).Centroid
        cap = f.IsPlanar(tol) and any(abs(p.DistanceTo(centre)) < tol * 10 for p in ends)
        if not cap:
            keep.append(f.FaceIndex)
    surface = solid.DuplicateSubBrep(keep)
    return surface, ends


def ends(Mesh, Ends, Load, Sideways, Tolerance):
    """The GhPython component's code: the mesh's nodes at the end of each tube. The trunk's are where the node is
    held; the branches' share the load on each branch - Load down and Sideways along +x - out evenly."""
    held, loaded, forces = [], [], []
    points = [rg.Point3d(v) for v in Mesh.Vertices]
    for k, plane in enumerate(Ends):
        at = [p for p in points if abs(plane.DistanceTo(p)) < Tolerance]
        if k == 0:
            held += at
        elif at:
            share = rg.Vector3d(Sideways, 0, -Load) / len(at)
            loaded += at
            forces += [share] * len(at)
    info = "{0} nodes held at the trunk, {1} loaded at {2} branch ends".format(len(held), len(loaded), len(Ends) - 1)
    return held, loaded, forces, info
