"""Makes Femur.txt, the tetrahedra of the Femur.py example, from the Standardised Femur.

    python3 FemurMesh.py [mesh size, m] [out.txt]

The Standardised Femur is the surface of a composite femur (Sawbones, Pacific Research Labs), put
forward by Viceconti et al. (J. Biomech. 1996) as the reference geometry for finite element studies
of the femur. The University of Sheffield publishes it under CC BY 4.0, by Marco Viceconti:
https://doi.org/10.15131/shef.data.3839766.v1.

Its IGES does not close into a solid: its faces stand up to a millimetre apart, so CAD will not sew
them. This tessellates the outer faces, rebuilds a closed surface from them - a signed distance on a
grid, its sign from the generalised winding number, which a gap that small does not upset, and
marching cubes - smooths the voxel steps off, turns it into metres standing on its distal end at
Z = 0, and fills it with tetrahedra with gmsh (TetMesh.py).

Needs numpy, scipy, scikit-image and gmsh's Python module: `pip install numpy scipy scikit-image
gmsh`, or gmsh from Homebrew, whose module this finds."""
import io, os, sys, tempfile, time, urllib.request, zipfile
import numpy as np
from scipy.spatial import cKDTree
from skimage import measure
import TetMesh

try:
    import gmsh
except ImportError:
    sys.path.append("/opt/homebrew/opt/gmsh/lib")
    import gmsh

URL = "https://ndownloader.figshare.com/files/6000003"      # std2_3_IGES_Surfaces.igs.zip
# The bone's outer surface, by the order gmsh reads the IGES's faces in: the distal end 9 to 12, the
# head, neck and trochanters 13 to 17, the shaft 18 to 21. 22 to 34 are the canal, the boundary of
# the foam core; 1 to 8 are four surfaces, each twice, whose trimming gmsh does not read.
OUTER = range(9, 22)
GRID = 1.5      # mm, the grid the surface is rebuilt on


def outer_triangles(iges):
    """The outer faces, tessellated, each turned to face out."""
    gmsh.initialize()
    gmsh.option.setNumber("General.Verbosity", 1)
    gmsh.model.occ.importShapes(iges, highestDimOnly=True)
    gmsh.model.occ.remove([(2, s) for s in range(1, 35) if s not in OUTER], recursive=True)
    gmsh.model.occ.synchronize()
    gmsh.option.setNumber("Mesh.MeshSizeMax", 2.5)
    gmsh.option.setNumber("Mesh.MeshSizeMin", 0.8)
    gmsh.model.mesh.generate(2)
    triangles, face = [], []
    for _, s in gmsh.model.getEntities(2):
        tags, xyz, _ = gmsh.model.mesh.getNodes(2, s, includeBoundary=True)
        at = dict(zip(tags, np.array(xyz).reshape(-1, 3)))
        types, _, nodes = gmsh.model.mesh.getElements(2, s)
        for kind, n in zip(types, nodes):
            if kind == 2:                       # three node triangles
                triangles += [[at[q] for q in n[k:k + 3]] for k in range(0, len(n), 3)]
                face += [s] * (len(n) // 3)
    gmsh.finalize()
    T, face = np.array(triangles), np.array(face)

    # Each face is tessellated along its own normal, which points in or out as the file has it. A
    # ray from just off a face along its normal leaves the bone through an even number of faces if
    # the normal points out; a few rays a face decide it.
    def crossings(origin, direction):
        e1, e2 = T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]
        p = np.cross(direction, e2)
        det = np.einsum("ij,ij->i", e1, p)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        s = origin - T[:, 0]
        u = np.einsum("ij,ij->i", s, p) * inv
        q = np.cross(s, e1)
        v = (q @ direction) * inv
        t = np.einsum("ij,ij->i", e2, q) * inv
        return int((ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-6)).sum())

    rng = np.random.default_rng(1)
    for s in np.unique(face):
        mine = np.nonzero(face == s)[0]
        votes = 0
        for k in rng.choice(mine, min(15, len(mine)), replace=False):
            a, b, c = T[k]
            n = np.cross(b - a, c - a)
            n /= np.linalg.norm(n)
            votes += 1 if crossings((a + b + c) / 3 + 1e-3 * n, n) % 2 == 0 else -1
        if votes < 0:
            T[mine] = T[mine][:, ::-1]
    return T


def closed_surface(T, h):
    """A closed surface through the faces T: marching cubes on a signed distance sampled every h."""
    a, b, c = T[:, 0], T[:, 1], T[:, 2]

    # The distance, to points strewn over the faces about every 0.3 mm.
    rng = np.random.default_rng(0)
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    count = int(area.sum() / 0.1)
    pick = rng.choice(len(T), count, p=area / area.sum())
    u, v = rng.random(count), rng.random(count)
    over = u + v > 1
    u[over], v[over] = 1 - u[over], 1 - v[over]
    samples = np.vstack([a[pick] + u[:, None] * (b - a)[pick] + v[:, None] * (c - a)[pick], T.reshape(-1, 3)])
    low, high = T.reshape(-1, 3).min(0) - 3 * h, T.reshape(-1, 3).max(0) + 3 * h
    axes = [np.arange(low[i], high[i] + h, h) for i in range(3)]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1).reshape(-1, 3)
    distance, _ = cKDTree(samples).query(grid)

    # The sign, from the winding number where it decides the surface - within a cell or so of the
    # faces - and from the nearest of those points elsewhere. Inside, the faces wind once round.
    near = np.nonzero(distance < 1.2 * h)[0]
    a32, b32, c32 = (x.astype(np.float32) for x in (a, b, c))
    winding = np.zeros(len(near))
    for k in range(0, len(near), 256):
        p = grid[near[k:k + 256]][:, None, :].astype(np.float32)
        A, B, C = a32[None] - p, b32[None] - p, c32[None] - p
        la, lb, lc = (np.linalg.norm(X, axis=2) for X in (A, B, C))
        det = np.einsum("ijk,ijk->ij", A, np.cross(B, C))
        den = la * lb * lc + np.einsum("ijk,ijk->ij", A, B) * lc + np.einsum("ijk,ijk->ij", B, C) * la + \
            np.einsum("ijk,ijk->ij", C, A) * lb
        winding[k:k + 256] = np.arctan2(det, den).sum(1) / (2 * np.pi)
    inside = np.zeros(len(grid), bool)
    inside[near] = winding > 0.5
    far = np.setdiff1d(np.arange(len(grid)), near)
    _, nearest = cKDTree(grid[near]).query(grid[far])
    inside[far] = inside[near][nearest]

    signed = np.where(inside, -distance, distance).reshape([len(x) for x in axes])
    vertices, triangles, _, _ = measure.marching_cubes(signed, 0.0, spacing=(h, h, h))
    vertices += low
    volume = np.einsum("ij,ij->i", vertices[triangles[:, 0]],
                       np.cross(vertices[triangles[:, 1]], vertices[triangles[:, 2]])).sum() / 6
    if volume < 0:
        triangles = triangles[:, ::-1]
    return vertices, triangles


def smoothed(vertices, triangles, rounds=20):
    """Taubin's smoothing: takes the grid's steps off without shrinking the surface."""
    around = [set() for _ in vertices]
    for a, b, c in triangles:
        around[a].update((b, c))
        around[b].update((a, c))
        around[c].update((a, b))
    around = [np.array(sorted(s)) for s in around]
    for _ in range(rounds):
        for factor in (0.5, -0.53):
            mean = np.array([vertices[n].mean(0) for n in around])
            vertices = vertices + factor * (mean - vertices)
    return vertices


def main():
    size = float(sys.argv[1]) if len(sys.argv) > 1 else 0.010
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "Femur.txt")
    started = time.time()
    work = tempfile.mkdtemp()
    with urllib.request.urlopen(URL) as response:
        zipfile.ZipFile(io.BytesIO(response.read())).extract("std2_3_IGES_Surfaces.igs", work)
    T = outer_triangles(os.path.join(work, "std2_3_IGES_Surfaces.igs"))
    vertices, triangles = closed_surface(T, GRID)
    vertices = smoothed(vertices, triangles)

    # Millimetres to metres, the distal end on Z = 0, the middle of the shaft on the Z axis.
    vertices = vertices / 1000.0
    vertices[:, 2] -= vertices[:, 2].min()
    shaft = vertices[(vertices[:, 2] > 0.15) & (vertices[:, 2] < 0.3)]
    vertices[:, :2] -= shaft[:, :2].mean(0)

    # The surface has no sharp edges, only the grid's steps, so it is remeshed in large patches.
    nodes, tets = TetMesh.tetrahedra(vertices.tolist(), triangles.tolist(), size=size, angle=60.0)
    TetMesh.write(out, nodes, tets, decimals=5)
    print("%.0f s" % (time.time() - started))


if __name__ == "__main__":
    main()
