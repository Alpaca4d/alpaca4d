"""The load-deflection curve of the PerforatedBeam.py example, in a GhPython component.

Reads the deflection at the middle of the beam's underside at every step, and the largest Von Mises
stress at every step, and draws the curve: blue while every point is elastic, orange from the step
the steel starts to yield. Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7)."""
import Rhino.Geometry as rg

# The beam: where its midspan underside is, m.
MIDSPAN = rg.Point3d(1.2, 0.04, 0.0)


def branches(tree):
    """A tree's branches as lists, in path order."""
    return [list(tree.Branch(i)) for i in range(tree.BranchCount)]


def load_deflection(Positions, Displacements, VonMises, Load, Lambda, Fy, Origin, Width, Height):
    positions = branches(Positions)
    displacements = branches(Displacements)
    stresses = branches(VonMises)
    if not positions or not displacements:
        return None, None, None, "Run the analysis first.", [], [], None, None

    # The node nearest the middle of the underside, the same at every step.
    nodes = positions[0]
    node = min(range(len(nodes)), key=lambda i: nodes[i].DistanceTo(MIDSPAN))

    steps = len(displacements)
    deflection = [0.0] + [-1000.0 * displacements[s][node].Z for s in range(steps)]        # mm
    load = [0.0] + [(s + 1) * Lambda * Load for s in range(steps)]                         # kN
    peak = [0.0] + [max(stresses[s]) if s < len(stresses) and stresses[s] else 0.0 for s in range(steps)]

    # The first step any point reaches Fy, and where between it and the step before the first
    # point got there: the stresses grow with the load until then, so the last elastic step scales.
    first = next((s for s in range(1, steps + 1) if peak[s] >= 0.999 * Fy), None)

    origin = Origin if Origin is not None else rg.Point3d(0.0, 0.0, -1.0)
    sx = Width / max(deflection)
    sy = Height / max(load)

    def point(d, p):
        return rg.Point3d(origin.X + d * sx, origin.Y, origin.Z + p * sy)

    axes = [rg.Line(origin, point(max(deflection) * 1.08, 0.0)), rg.Line(origin, point(0.0, max(load) * 1.08))]

    if first is None:
        elastic = rg.PolylineCurve([point(d, p) for d, p in zip(deflection, load)])
        return (elastic, None, axes, "Elastic all the way to {0:.0f} kN: no point reached Fy.".format(load[-1]),
                deflection, load, None, None)

    before = first - 1
    scale = Fy / peak[before] if peak[before] > 0 else 1.0
    yield_load = load[before] * scale if before > 0 else load[first]
    yield_deflection = deflection[before] * scale if before > 0 else deflection[first]
    elastic = rg.PolylineCurve([point(deflection[s], load[s]) for s in range(first)] + [point(yield_deflection, yield_load)])
    # Curves, not Polylines: GhPython hands a Polyline out as its list of points.
    plastic = rg.PolylineCurve([point(yield_deflection, yield_load)] + [point(deflection[s], load[s]) for s in range(first, steps + 1)])

    summary = ("First yield at {0:.0f} kN, {1:.1f} mm, between steps {2} and {3}.\n"
               "Pushed on to {4:.0f} kN, {5:.0%} more, where it sags {6:.1f} mm: {7:.1f} times as far as at first yield, "
               "{8:.1f} times as far as it would if it stayed elastic.").format(
        yield_load, yield_deflection, before, first, load[-1], load[-1] / yield_load - 1.0, deflection[-1],
        deflection[-1] / yield_deflection, deflection[-1] / (yield_deflection * load[-1] / yield_load))
    return elastic, plastic, axes, summary, deflection, load, yield_deflection, yield_load
