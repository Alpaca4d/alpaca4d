"""Makes PerforatedBeam.txt, the tetrahedra of the PerforatedBeam.py example.

    python3 PerforatedBeamMesh.py [mesh size, m] [out.txt]

A steel beam 2.4 m long, 300 mm deep and 80 mm thick, with six round holes 180 mm across along its
middle: the depth of a cellular beam's openings, 60% of the beam. A box less six cylinders in gmsh's
OpenCASCADE kernel, filled with four node tetrahedra, finer round the holes, where it yields first.
The example makes them ten node ones.

The file holds the nodes, one "x y z" a line, then the tetrahedra, one "a b c d" a line of node
indices from 0; a line "nodes N" or "tetrahedra N" starts each part - as TetMesh.py writes.

Needs gmsh's Python module: `pip install gmsh`, or gmsh from Homebrew, whose module this finds."""
import sys

try:
    import gmsh
except ImportError:
    sys.path.append("/opt/homebrew/opt/gmsh/lib")
    import gmsh

LENGTH, DEPTH, THICKNESS = 2.4, 0.3, 0.08      # m; the beam runs along X, its depth along Z
HOLE = 0.18                                     # diameter, m
PITCH = 0.34                                    # from hole centre to hole centre, m
HOLES = 6


def hole_centres():
    first = LENGTH / 2.0 - PITCH * (HOLES - 1) / 2.0
    return [first + i * PITCH for i in range(HOLES)]


def mesh(size, fine):
    gmsh.initialize()
    gmsh.option.setNumber("General.Verbosity", 1)
    gmsh.model.add("beam")
    occ = gmsh.model.occ
    box = occ.addBox(0, 0, 0, LENGTH, THICKNESS, DEPTH)
    cylinders = [occ.addCylinder(x, 0, DEPTH / 2.0, 0, THICKNESS, 0, HOLE / 2.0) for x in hole_centres()]
    occ.cut([(3, box)], [(3, c) for c in cylinders])
    occ.synchronize()

    # Finer within a hole's radius of each hole's edge, growing to size beyond.
    holes = [s for _, s in gmsh.model.getEntities(2)
             if abs(gmsh.model.occ.getMass(2, s) - 3.141592653589793 * HOLE * THICKNESS) < 1e-6]
    field = gmsh.model.mesh.field
    field.add("Distance", 1)
    field.setNumbers(1, "SurfacesList", holes)
    field.add("Threshold", 2)
    field.setNumber(2, "InField", 1)
    field.setNumber(2, "SizeMin", fine)
    field.setNumber(2, "SizeMax", size)
    field.setNumber(2, "DistMin", 0.0)
    field.setNumber(2, "DistMax", HOLE / 2.0)
    field.setAsBackgroundMesh(2)
    gmsh.option.setNumber("Mesh.MeshSizeExtendFromBoundary", 0)
    gmsh.option.setNumber("Mesh.MeshSizeFromPoints", 0)
    gmsh.option.setNumber("Mesh.MeshSizeFromCurvature", 0)
    gmsh.option.setNumber("Mesh.Algorithm3D", 10)       # HXT
    gmsh.option.setNumber("Mesh.Optimize", 1)
    gmsh.model.mesh.generate(3)

    tags, xyz, _ = gmsh.model.mesh.getNodes()
    index = {int(t): i for i, t in enumerate(tags)}
    nodes = [(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]) for i in range(len(tags))]
    types, _, connectivity = gmsh.model.mesh.getElements(3)
    tets = []
    for kind, conn in zip(types, connectivity):
        if kind != 4:       # four node tetrahedra only
            continue
        for k in range(0, len(conn), 4):
            tets.append([index[int(n)] for n in conn[k:k + 4]])
    gmsh.finalize()

    # Only the nodes the tetrahedra use, numbered again from 0.
    used = sorted({n for t in tets for n in t})
    renumber = {old: new for new, old in enumerate(used)}
    return [nodes[n] for n in used], [[renumber[n] for n in t] for t in tets]


def main():
    size = float(sys.argv[1]) if len(sys.argv) > 1 else 0.03
    out = sys.argv[2] if len(sys.argv) > 2 else "PerforatedBeam.txt"
    nodes, tets = mesh(size, size * 0.6)
    with open(out, "w") as f:
        f.write("nodes %d\n" % len(nodes))
        for x, y, z in nodes:
            f.write("%.5f %.5f %.5f\n" % (x, y, z))
        f.write("tetrahedra %d\n" % len(tets))
        for t in tets:
            f.write("%d %d %d %d\n" % tuple(t))
    print("%d nodes, %d tetrahedra -> %s" % (len(nodes), len(tets), out))


if __name__ == "__main__":
    main()
