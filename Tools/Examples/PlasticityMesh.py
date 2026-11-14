"""Makes the meshes of the Plasticity.py examples, steel that yields (J2Plasticity).

    python3 PlasticityMesh.py tjoint|yieldlines|platehole|pipe [out.txt]

tjoint      PlasticityTJoint.txt      a T-joint of two steel tubes as shells: a chord 300 x 10 mm,
                                      1.8 m long, and a brace 150 x 8 mm standing on it at midspan,
                                      its top 0.6 m above the chord's axis
yieldlines  PlasticityYieldLines.txt  a square steel plate 1 x 1 m as shells, 40 x 40 quads
platehole   PlasticityPlateHole.txt   a steel plate 400 x 200 mm with a hole 60 mm across in its
                                      middle, as shells, finer round the hole
pipe        PlasticityPipe.txt        a quarter of a thick-walled steel pipe as tetrahedra: bore 200 mm,
                                      outside 400 mm, a slice 20 mm long

A shell mesh holds the nodes, one "x y z" a line, then the faces, one "part a b c [d]" a line of
node indices from 0, a face of three corners or four; a line "nodes N" or "faces N" starts each
part. A tetrahedron mesh holds "tetrahedra N" and four node indices a line, as TetMesh.py writes.
The tube parts are 0 for the chord, 1 for the brace and 2 for the brace's top 60 mm, the end the
load comes in through, which the example keeps elastic and stiff, as an end plate would make it.

Needs gmsh's Python module: `pip install gmsh`, or gmsh from Homebrew, whose module this finds."""
import math
import sys

try:
    import gmsh
except ImportError:
    sys.path.append("/opt/homebrew/opt/gmsh/lib")
    import gmsh

FILES = {"tjoint": "PlasticityTJoint.txt", "yieldlines": "PlasticityYieldLines.txt",
         "platehole": "PlasticityPlateHole.txt", "pipe": "PlasticityPipe.txt"}


def start():
    gmsh.initialize()
    gmsh.option.setNumber("General.Verbosity", 1)
    gmsh.option.setNumber("Mesh.MeshSizeFromPoints", 0)
    gmsh.option.setNumber("Mesh.MeshSizeFromCurvature", 0)
    gmsh.option.setNumber("Mesh.MeshSizeExtendFromBoundary", 0)


def finer_near(surfaces_or_curves, dim, fine, coarse, reach):
    """Elements fine within reach of the given surfaces (dim 2) or curves (dim 1), coarse beyond."""
    field = gmsh.model.mesh.field
    field.add("Distance", 1)
    field.setNumbers(1, "SurfacesList" if dim == 2 else "CurvesList", surfaces_or_curves)
    field.add("Threshold", 2)
    field.setNumber(2, "InField", 1)
    field.setNumber(2, "SizeMin", fine)
    field.setNumber(2, "SizeMax", coarse)
    field.setNumber(2, "DistMin", 0.0)
    field.setNumber(2, "DistMax", reach)
    field.setAsBackgroundMesh(2)


def nodes_used(index_of, xyz, cells):
    """The nodes the cells use, numbered again from 0, and the cells in the new numbers."""
    used = sorted({n for c in cells for n in c[-1]})
    renumber = {old: new for new, old in enumerate(used)}
    return [xyz[n] for n in used], [c[:-1] + ([renumber[n] for n in c[-1]],) for c in cells]


def read_nodes():
    tags, coords, _ = gmsh.model.mesh.getNodes()
    index = {int(t): i for i, t in enumerate(tags)}
    xyz = [(coords[3 * i], coords[3 * i + 1], coords[3 * i + 2]) for i in range(len(tags))]
    return index, xyz


def surface_faces(surfaces_by_part):
    """Every triangle and quad of the surfaces, by part, as (part, [nodes])."""
    index, xyz = read_nodes()
    cells = []
    for part, surfaces in surfaces_by_part.items():
        for s in surfaces:
            types, _, connectivity = gmsh.model.mesh.getElements(2, s)
            for kind, conn in zip(types, connectivity):
                corners = {2: 3, 3: 4}.get(int(kind))
                if corners is None:
                    continue
                for k in range(0, len(conn), corners):
                    cells.append((part, [index[int(n)] for n in conn[k:k + corners]]))
    return nodes_used(index, xyz, cells)


def tjoint():
    """A chord 300 x 10 mm along X, 1.8 m long, and a brace 150 x 8 mm up Z at midspan, as the
    surfaces through the middle of their walls; the chord's wall runs on inside the brace's foot."""
    L, d0, t0, d1, t1, top = 1.8, 0.3, 0.01, 0.15, 0.008, 0.6
    END = 0.06          # the brace's top, a part of its own: the end the load comes in through
    r0, r1 = (d0 - t0) / 2.0, (d1 - t1) / 2.0
    start()
    occ = gmsh.model.occ
    chord = occ.addCylinder(0, 0, 0, L, 0, 0, r0)
    brace = occ.addCylinder(L / 2.0, 0, 0, 0, 0, top - END, r1)
    end = occ.addCylinder(L / 2.0, 0, top - END, 0, 0, END, r1)
    occ.synchronize()
    # The lateral walls alone, then cut into pieces where they cross.
    walls = []
    for vol in (chord, brace, end):
        for dim, s in gmsh.model.getBoundary([(3, vol)], oriented=False):
            if abs(occ.getMass(2, s) - math.pi * r0 * r0) > 1e-9 and abs(occ.getMass(2, s) - math.pi * r1 * r1) > 1e-9:
                walls.append((dim, s))
    occ.remove([(3, chord), (3, brace), (3, end)])
    occ.remove([(2, s) for dim, s in gmsh.model.getEntities(2) if (2, s) not in walls], recursive=True)
    pieces, _ = occ.fragment(walls[:1], walls[1:])
    occ.synchronize()
    # Each piece by a point on it: on the chord's radius from its axis, or on the brace's from its
    # own; of the brace, only what stands above the chord.
    chord_part, brace_part, end_part = [], [], []
    for dim, s in gmsh.model.getEntities(2):
        lo, hi = gmsh.model.getParametrizationBounds(2, s)
        x, y, z = gmsh.model.getValue(2, s, [(lo[0] + hi[0]) / 2.0, (lo[1] + hi[1]) / 2.0])
        if abs(math.hypot(y, z) - r0) < 1e-6:
            chord_part.append(s)
        elif abs(math.hypot(x - L / 2.0, y) - r1) < 1e-6 and z > top - END:
            end_part.append(s)
        elif abs(math.hypot(x - L / 2.0, y) - r1) < 1e-6 and z > 0.0 and math.hypot(y, z) > r0:
            brace_part.append(s)
    keep = set(chord_part + brace_part + end_part)
    occ.remove([(2, s) for dim, s in gmsh.model.getEntities(2) if s not in keep], recursive=True)
    occ.synchronize()
    # Finer round the line the two walls meet on.
    seam = []
    for s in brace_part:
        for dim, c in gmsh.model.getBoundary([(2, s)], oriented=False):
            bx = occ.getBoundingBox(1, c)
            if bx[5] < top - 1e-6:
                seam.append(c)
    finer_near(seam, 1, 0.012, 0.04, 0.25)
    gmsh.option.setNumber("Mesh.RecombineAll", 1)
    gmsh.option.setNumber("Mesh.Algorithm", 8)          # frontal quads
    gmsh.model.mesh.generate(2)
    gmsh.model.mesh.removeDuplicateNodes()
    result = surface_faces({0: chord_part, 1: brace_part, 2: end_part})
    gmsh.finalize()
    return result


def yieldlines():
    """A square plate 1 x 1 m in the XY plane, 40 x 40 quads: no mesher needed."""
    n, L = 40, 1.0
    xyz = [(i * L / n, j * L / n, 0.0) for j in range(n + 1) for i in range(n + 1)]
    faces = []
    for j in range(n):
        for i in range(n):
            a = j * (n + 1) + i
            faces.append((0, [a, a + 1, a + n + 2, a + n + 1]))
    return xyz, faces


def platehole():
    """A plate 400 x 200 mm in the XY plane with a hole 60 mm across in its middle."""
    W, H, d = 0.4, 0.2, 0.06
    start()
    occ = gmsh.model.occ
    plate = occ.addRectangle(0, 0, 0, W, H)
    hole = occ.addDisk(W / 2.0, H / 2.0, 0, d / 2.0, d / 2.0)
    occ.cut([(2, plate)], [(2, hole)])
    # A node at the middle of each short edge, for the supports to hold.
    middles = [occ.addPoint(0, H / 2.0, 0), occ.addPoint(W, H / 2.0, 0)]
    occ.fragment(occ.getEntities(2), [(0, p) for p in middles])
    occ.synchronize()
    edge = [c for dim, c in gmsh.model.getEntities(1) if abs(occ.getMass(1, c) - math.pi * d) < 1e-6]
    finer_near(edge, 1, 0.003, 0.012, 0.06)
    gmsh.option.setNumber("Mesh.RecombineAll", 1)
    gmsh.option.setNumber("Mesh.Algorithm", 8)
    gmsh.model.mesh.generate(2)
    surfaces = [s for dim, s in gmsh.model.getEntities(2)]
    result = surface_faces({0: surfaces})
    gmsh.finalize()
    return result


def pipe():
    """A quarter of a thick-walled steel pipe, bore 200 mm and outside 400 mm across, a slice 20 mm
    long: the ring between X >= 0 and Y >= 0, along Z. Finer at the bore, where it yields first."""
    a, b, t = 0.1, 0.2, 0.02
    start()
    occ = gmsh.model.occ
    outer = occ.addCylinder(0, 0, 0, 0, 0, t, b)
    inner = occ.addCylinder(0, 0, 0, 0, 0, t, a)
    ring, _ = occ.cut([(3, outer)], [(3, inner)])
    quadrant = occ.addBox(0, 0, 0, b, b, t)
    occ.intersect(ring, [(3, quadrant)])
    occ.synchronize()
    bore = [s for dim, s in gmsh.model.getEntities(2) if abs(occ.getMass(2, s) - math.pi * a * t / 2.0) < 1e-7]
    finer_near(bore, 2, 0.005, 0.012, 0.1)
    gmsh.option.setNumber("Mesh.Algorithm3D", 10)
    gmsh.option.setNumber("Mesh.Optimize", 1)
    gmsh.model.mesh.generate(3)
    index, xyz = read_nodes()
    cells = []
    types, _, connectivity = gmsh.model.mesh.getElements(3)
    for kind, conn in zip(types, connectivity):
        if int(kind) != 4:
            continue
        for k in range(0, len(conn), 4):
            cells.append(([index[int(n)] for n in conn[k:k + 4]],))
    gmsh.finalize()
    return nodes_used(index, xyz, cells)


def main():
    case = sys.argv[1] if len(sys.argv) > 1 else ""
    if case not in FILES:
        raise SystemExit("python3 PlasticityMesh.py " + "|".join(FILES) + " [out.txt]")
    out = sys.argv[2] if len(sys.argv) > 2 else FILES[case]
    nodes, cells = {"tjoint": tjoint, "yieldlines": yieldlines, "platehole": platehole, "pipe": pipe}[case]()
    with open(out, "w") as f:
        f.write("nodes %d\n" % len(nodes))
        for x, y, z in nodes:
            f.write("%.6f %.6f %.6f\n" % (x, y, z))
        if case == "pipe":
            f.write("tetrahedra %d\n" % len(cells))
            for (c,) in cells:
                f.write("%d %d %d %d\n" % tuple(c))
        else:
            f.write("faces %d\n" % len(cells))
            for part, c in cells:
                f.write("%d %s\n" % (part, " ".join(str(n) for n in c)))
    print("%s: %d nodes, %d %s -> %s" % (case, len(nodes), len(cells), "tetrahedra" if case == "pipe" else "faces", out))


if __name__ == "__main__":
    main()
