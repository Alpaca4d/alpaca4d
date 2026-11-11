"""The load-displacement curve of the Plasticity.py examples, in a GhPython component.

Reads, at every step, how far one node has moved along a direction and the largest Von Mises
stress, and draws the curve in a plane: blue while every point is elastic, orange from the step the
steel starts to yield, in the plane through Origin along XAxis and YAxis. Shell stresses come a branch per {step; element; layer} and brick stresses a
branch per {step}: the stresses are grouped by the first number of their path. It also hands out the
numbers - the displacement (mm) and the load at rest and at every step, in Unit, and first yield's -
for a chart. Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7)."""
import Rhino.Geometry as rg


def load_displacement(Positions, Displacements, VonMises, Load, Lambda, Fy, Origin, XAxis, YAxis, Width, Height, Point, Direction, Unit, Scale):
    if Positions is None or Positions.BranchCount == 0 or Displacements is None or Displacements.BranchCount == 0:
        return None, None, None, "Run the analysis first.", [], [], None, None
    scale = Scale if Scale else 1.0
    unit = Unit or "kN"

    # The node nearest Point, the same at every step, and how far it moves along Direction.
    nodes = list(Positions.Branch(0))
    node = min(range(len(nodes)), key=lambda i: nodes[i].DistanceTo(Point))
    along = rg.Vector3d(Direction)
    along.Unitize()
    steps = Displacements.BranchCount
    moved = [0.0] + [1000.0 * (Displacements.Branch(s)[node] * along) for s in range(steps)]       # mm
    load = [0.0] + [(s + 1) * Lambda * Load * scale for s in range(steps)]

    # The largest stress at each step, whatever else the path holds after the step.
    peak = [0.0] * (steps + 1)
    for i in range(VonMises.BranchCount):
        s = VonMises.Paths[i].Indices[0]
        values = list(VonMises.Branch(i))
        if values and s < steps:
            peak[s + 1] = max(peak[s + 1], max(values))

    first = next((s for s in range(1, steps + 1) if peak[s] >= 0.999 * Fy), None)

    # Made here, in Rhino: a Plane takes Rhino's own geometry code to be made.
    plane = rg.Plane(Origin, XAxis, YAxis) if Origin is not None and XAxis is not None and YAxis is not None else rg.Plane.WorldZX
    sx = Width / max(max(moved), 1e-12)
    sy = Height / max(max(load), 1e-12)

    def at(d, p):
        return plane.PointAt(d * sx, p * sy)

    axes = [rg.Line(at(0.0, 0.0), at(max(moved) * 1.08, 0.0)), rg.Line(at(0.0, 0.0), at(0.0, max(load) * 1.08))]

    if first is None:
        elastic = rg.PolylineCurve([at(d, p) for d, p in zip(moved, load)])
        return (elastic, None, axes, "Elastic all the way to {0:.4g} {1}: no point reached Fy.".format(load[-1], unit),
                moved, load, None, None)

    before = first - 1
    k = Fy / peak[before] if before > 0 and peak[before] > 0 else 1.0
    yield_load = load[before] * k if before > 0 else load[first]
    yield_moved = moved[before] * k if before > 0 else moved[first]
    # Curves, not Polylines: GhPython hands a Polyline out as its list of points.
    elastic = rg.PolylineCurve([at(moved[s], load[s]) for s in range(first)] + [at(yield_moved, yield_load)])
    plastic = rg.PolylineCurve([at(yield_moved, yield_load)] + [at(moved[s], load[s]) for s in range(first, steps + 1)])

    summary = ("First yield at {0:.4g} {9}, {1:.3g} mm, between steps {2} and {3}.\n"
               "Pushed on to {4:.4g} {9}, {5:.0%} more, where it has moved {6:.3g} mm: {7:.1f} times as far as at first "
               "yield, {8:.1f} times as far as it would if it stayed elastic.").format(
        yield_load, yield_moved, before, first, load[-1], load[-1] / yield_load - 1.0, moved[-1],
        moved[-1] / yield_moved, moved[-1] / (yield_moved * load[-1] / yield_load), unit)
    return elastic, plastic, axes, summary, moved, load, yield_moved, yield_load
