"""Makes RebelliousSlave.txt, the tetrahedra of the RebelliousSlave.py example, from SMK's scan of
the Rebellious Slave.

    python3 RebelliousSlaveMesh.py [element size, m] [out.txt]

The Rebellious Slave (or Rebellious Prisoner) is Michelangelo's marble of about 1513, made for the
tomb of Pope Julius II and now in the Louvre, Paris (MR 1589). This is the plaster cast of it in
SMK's Royal Cast Collection - SMK, Statens Museum for Kunst, the National Gallery of Denmark,
Copenhagen - KAS86, cast between 1852 and 1870, 213 cm tall. SMK scanned it and released the scan
in the public domain (Public Domain Mark 1.0): https://open.smk.dk/artwork/image/KAS86. This
downloads the lighter of SMK's two STL files of it, a closed surface of 400 000 triangles.

The scan is 130 units tall, so it is scaled to the cast's 2.13 m, which is life size: the marble is
given as 2.09 m to 2.15 m. It has Z up and faces +Y, so it is turned half round to face -Y, its
right foot raised on a block. The cast stands on a plinth, a plaster slab under the rough stone
Michelangelo left round the legs, and both are kept: the bottom of the slab, flat to 0.06 mm, is put
at Z = 0, its middle on the Z axis. The surface is remeshed to the element size, finer where it
curves, and finer still below the knees, LEGS times the size, where the legs carry the statue
(pymeshlab's isotropic remeshing, which keeps the points on the scan, so nothing is fattened; a
leg's section loses a few percent to the flat triangles, as a polygon inside a curve does). Its
edges are kept only where the surface folds by more than FOLD degrees, so the creases of the stone
leave no millimetre triangles; the edge round the bottom of the slab is rounded, so the points the
remeshing leaves within FLAT of the bottom are put back on it. gmsh fills the surface as it is,
with tetrahedra that grow larger away from it (TetMesh.py). Nothing is closed or thickened: up to
4.5 cm the gaps between the standing leg and the stone, and between the hip and the stone, stay
open. At 6 cm they close, the remeshed surface passes through itself, and this stops.

Needs numpy and pymeshlab (`pip install numpy pymeshlab`), and gmsh (`brew install gmsh`)."""
import os, sys, time, urllib.request
import numpy as np
import pymeshlab
import TetMesh

URL = "https://api.smk.dk/api/v1/download-3d/1544bt98d_KAS86_small.stl"
HEIGHT = 2.13   # m, the cast's height, plinth included
KNEES = 0.75    # m, below this the surface is meshed finer
LEGS = 0.8      # the size there, as a part of the element size
FOLD = 75       # degrees: the surface keeps an edge where it folds by more than this
FLAT = 0.005    # m: points the remeshing leaves this close to the bottom are put back on it
INSIDE = 0.5    # how fast the tetrahedra grow away from the surface: 0.5 m larger a metre in


def slave():
    """The scan, in metres, standing on Z = 0 and turned to face -Y, and its triangles."""
    with urllib.request.urlopen(URL) as response:
        data = response.read()
    count = int(np.frombuffer(data, np.uint32, 1, 80)[0])
    # A binary STL: 80 bytes of header, the count, then 50 bytes a triangle - its normal, its three
    # corners, two bytes more.
    rows = np.frombuffer(data, np.dtype([("normal", "<f4", 3), ("corners", "<f4", (3, 3)), ("more", "<u2")]),
                         count, 84)
    corners = rows["corners"].reshape(-1, 3).astype(float)
    v, index = np.unique(corners, axis=0, return_inverse=True)
    f = index.reshape(-1, 3)
    v[:, 2] -= v[:, 2].min()
    v *= HEIGHT / v[:, 2].max()
    # It faces +Y: half a turn about Z, a turn and not a mirror.
    v[:, :2] *= -1
    # The bottom of the slab is flat to 0.06 mm.
    v[v[:, 2] < 1e-3, 2] = 0.0
    return v, f


def remeshed(vertices, triangles, size):
    """The surface in triangles of about size, smaller where it curves, and LEGS * size below KNEES."""
    meshes = pymeshlab.MeshSet()
    meshes.add_mesh(pymeshlab.Mesh(vertices, triangles))
    meshes.meshing_isotropic_explicit_remeshing(iterations=10, targetlen=pymeshlab.PureValue(size),
                                               featuredeg=FOLD, adaptive=True)
    meshes.compute_selection_by_condition_per_face(condselect="(z0 < %g) && (z1 < %g) && (z2 < %g)" % ((KNEES,) * 3))
    meshes.meshing_isotropic_explicit_remeshing(iterations=10, targetlen=pymeshlab.PureValue(LEGS * size),
                                               featuredeg=FOLD, adaptive=True, selectedonly=True)
    mesh = meshes.current_mesh()
    vertices, triangles = mesh.vertex_matrix().copy(), mesh.face_matrix().copy()
    vertices[vertices[:, 2] < FLAT, 2] = 0.0

    meshes = pymeshlab.MeshSet()
    meshes.add_mesh(pymeshlab.Mesh(vertices, triangles))
    meshes.compute_selection_by_self_intersections_per_face()
    crossing = meshes.current_mesh().selected_face_number()
    if crossing:
        raise Exception("The remeshed surface passes through itself at %d triangles, where a gap is narrower "
                        "than the elements: remesh it finer." % crossing)
    return vertices, triangles


def main():
    size = float(sys.argv[1]) if len(sys.argv) > 1 else 0.03
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "RebelliousSlave.txt")
    started = time.time()
    vertices, triangles = slave()

    # The middle of the bottom on the Z axis: the middle of the triangles lying on Z = 0.
    bottom = triangles[(vertices[triangles, 2] == 0).all(1)]
    a, b, c = (vertices[bottom[:, i]] for i in range(3))
    area = 0.5 * np.abs(np.cross(b - a, c - a)[:, 2])
    vertices[:, :2] -= (area[:, None] * (a + b + c)[:, :2] / 3).sum(0) / area.sum()

    vertices, triangles = remeshed(vertices, triangles, size)
    nodes, tets = TetMesh.tetrahedra(vertices.tolist(), triangles.tolist(), size=size, angle=None, grow=INSIDE)
    TetMesh.write(out, nodes, tets, decimals=4)
    print("%.0f s" % (time.time() - started))


if __name__ == "__main__":
    main()
