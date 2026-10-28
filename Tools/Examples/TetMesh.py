"""Fills a closed triangle surface with tetrahedra, with gmsh, and writes them for the tetrahedron
examples to read: SpotTheCowMesh.py, FemurMesh.py and InigoTheDragonMesh.py use it.

gmsh (https://gmsh.info, `brew install gmsh`) is run from its command line. Standard library only.

The file it writes holds the nodes, one "x y z" a line, then the tetrahedra, one "a b c d" a line
of node indices from 0; a line "nodes N" or "tetrahedra N" starts each part."""
import os, subprocess, tempfile


def tetrahedra(vertices, triangles, size, angle=40.0, grow=0.0):
    """gmsh's tetrahedra of a closed, outward facing surface: remeshed to about size, then filled.
    The surface is split into patches where it folds by more than angle degrees, and each patch is
    remeshed on its own, so a sharp edge stays sharp.

    With angle None the triangles are kept as they are, as the faces of the tetrahedra on the
    surface: for a surface already meshed to size, which gmsh cannot cut into patches. grow makes
    the tetrahedra inside larger, by that much per metre from the surface, so a thick part is not
    filled at the surface's size."""
    work = tempfile.mkdtemp()
    with open(os.path.join(work, "surface.stl"), "w") as f:
        f.write("solid surface\n")
        for tri in triangles:
            f.write("facet normal 0 0 0\nouter loop\n")
            for v in tri:
                f.write("vertex {0:.9g} {1:.9g} {2:.9g}\n".format(*vertices[v]))
            f.write("endloop\nendfacet\n")
        f.write("endsolid surface\n")
    with open(os.path.join(work, "volume.geo"), "w") as f:
        f.write('Merge "surface.stl";\n')
        if angle is None:
            f.write("Surface Loop(1) = {1};\n")
        else:
            f.write("ClassifySurfaces{%g * Pi / 180, 1, 1, Pi};\n"
                    "CreateGeometry;\n"
                    "Surface Loop(1) = Surface{:};\n" % angle)
        f.write("Volume(1) = {1};\n"
                "Mesh.MeshSizeMax = %g;\n" % size)
        if grow:
            f.write("Field[1] = Distance;\n"
                    "Field[1].SurfacesList = {1};\n"
                    "Field[1].Sampling = 4;\n"
                    "Field[2] = MathEval;\n"
                    'Field[2].F = "%g + %g * F1";\n'
                    "Background Field = 2;\n"
                    "Mesh.MeshSizeMax = 1e22;\n"
                    "Mesh.MeshSizeExtendFromBoundary = 0;\n"
                    "Mesh.MeshSizeFromPoints = 0;\n" % (size, grow))
        f.write("Mesh.Algorithm = 6;\n"
                "Mesh.Algorithm3D = 1;\n"
                "Mesh.Optimize = 1;\n"
                "Mesh.OptimizeNetgen = 1;\n")
    subprocess.run(["gmsh", "volume.geo", "-3", "-format", "msh2", "-o", "volume.msh"], cwd=work, check=True,
                   stdout=subprocess.DEVNULL)

    lines = open(os.path.join(work, "volume.msh")).read().split("\n")
    at = lines.index("$Nodes")
    tag, nodes = {}, []
    for line in lines[at + 2:at + 2 + int(lines[at + 1])]:
        p = line.split()
        tag[p[0]] = len(nodes)
        nodes.append([float(q) for q in p[1:4]])
    at = lines.index("$Elements")
    tets = []
    for line in lines[at + 2:at + 2 + int(lines[at + 1])]:
        p = line.split()
        if p[1] == "4":                          # a four node tetrahedron
            tets.append([tag[q] for q in p[3 + int(p[2]):]])

    # Only the nodes the tetrahedra use, in the order they first use them.
    used = {}
    for tet in tets:
        for n in tet:
            used.setdefault(n, len(used))
    order = sorted(used, key=used.get)
    return [nodes[n] for n in order], [[used[n] for n in tet] for tet in tets]


def write(path, nodes, tets, decimals=4):
    with open(path, "w") as f:
        f.write("nodes %d\n" % len(nodes))
        row = " ".join(["%%.%df" % decimals] * 3) + "\n"
        for p in nodes:
            f.write(row % tuple(p))
        f.write("tetrahedra %d\n" % len(tets))
        for t in tets:
            f.write("%d %d %d %d\n" % tuple(t))
    print("%s: %d nodes, %d tetrahedra" % (path, len(nodes), len(tets)))
