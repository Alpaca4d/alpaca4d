"""Makes InigoTheDragon.txt, the tetrahedra of the InigoTheDragon.py example, from Inigo the Dragon.

    python3 InigoTheDragonMesh.py [element size, m] [out.txt]

Inigo the Dragon is Keenan Crane's (2026), a dragon built of signed distance functions, in the public
domain (CC0): https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/. Its archive carries a dense
triangle mesh of it, which this starts from, with its units taken as metres: it stands 5.5 m tall.

Its wings are membranes a few centimetres thick, and its fingers, its smile and the roots of its
wings leave gaps narrower than an element. A surface remeshed to the element size passes through
itself there, and gmsh cannot fill it. So the dragon is rebuilt on a grid first: grown by GROW all
over, which thickens the wings; closed, every gap narrower than twice CLOSE filled; its soles cut
flat. Marching cubes makes a surface of that, smoothed; it is remeshed to the element size, finer
where it curves (pymeshlab's isotropic remeshing), and gmsh fills it as it is, with tetrahedra that
grow larger away from the surface (TetMesh.py).

Needs numpy, scipy, scikit-image and pymeshlab (`pip install numpy scipy scikit-image pymeshlab`),
and gmsh (`brew install gmsh`)."""
import io, os, sys, time, urllib.request, zipfile
import numpy as np
import scipy.sparse
import scipy.sparse.csgraph
from scipy import ndimage
from scipy.spatial import cKDTree
from skimage import measure
import pymeshlab
import TetMesh

URL = "https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/inigo.zip"
OBJ = "inigo-the-dragon/dragon-meshed.obj"
GRID = 0.03     # m, the grid the dragon is rebuilt on
GROW = 0.045    # m, how much it grows all over: a wing becomes 9 cm thicker
CLOSE = 0.09    # m, every gap narrower than twice this is filled
SOLE = 0.03     # m, cut off the bottom of the feet, so that they stand on flat soles
INSIDE = 0.5    # how fast the tetrahedra grow away from the surface: 0.5 m larger a metre in


def dragon():
    """The dense mesh of the archive, turned to stand on Z and face -Y: it has Y up and faces +Z."""
    with urllib.request.urlopen(URL) as response:
        text = zipfile.ZipFile(io.BytesIO(response.read())).read(OBJ).decode()
    rows = [line.split() for line in text.splitlines()]
    v = np.array([r[1:4] for r in rows if r and r[0] == "v"], float)
    f = np.array([r[1:4] for r in rows if r and r[0] == "f"], int) - 1
    return np.c_[v[:, 0], -v[:, 2], v[:, 1]], f


def solid(v, f, h):
    """The surface of the dragon grown by GROW, closed by CLOSE, its soles cut SOLE up: marching
    cubes on a grid h, keeping the largest piece, facing out."""
    # The distance to the surface, to points strewn over it every h / 4 or so: only up to a few
    # cells, which is as far as it matters.
    a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    rng = np.random.default_rng(0)
    count = int(area.sum() / (h / 4) ** 2)
    pick = rng.choice(len(f), count, p=area / area.sum())
    s, t = rng.random(count), rng.random(count)
    over = s + t > 1
    s[over], t[over] = 1 - s[over], 1 - t[over]
    samples = np.vstack([v, a[pick] + s[:, None] * (b - a)[pick] + t[:, None] * (c - a)[pick]])
    pad = GROW + CLOSE + 4 * h
    low, high = v.min(0) - pad, v.max(0) + pad
    axes = [np.arange(low[i], high[i] + h, h) for i in range(3)]
    shape = [len(x) for x in axes]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1).reshape(-1, 3)
    far = GROW + 3 * h
    distance, _ = cKDTree(samples).query(grid, workers=-1, distance_upper_bound=far)
    distance = np.minimum(distance, far).reshape(shape)

    # Outside is what can be reached from the corner of the grid without coming within GROW of
    # the surface; the rest is the dragon, grown. A level set: distance - GROW outside and within
    # GROW, GROW - distance deeper in.
    pieces, _ = ndimage.label(distance > GROW)
    outside = pieces == pieces[0, 0, 0]
    level = np.where(outside | (distance <= GROW), distance - GROW, GROW - distance)

    # Closed: grown by CLOSE and shrunk back by as much, which fills what is narrower than twice it.
    dragon = level < 0
    grown = ndimage.distance_transform_edt(~dragon, sampling=h) <= CLOSE
    closed = ndimage.distance_transform_edt(grown, sampling=h) > CLOSE
    level = np.where(closed & ~dragon, -0.5 * h, level)

    level = np.maximum(level, v[:, 2].min() + SOLE - axes[2][None, None, :])
    vertices, triangles, _, _ = measure.marching_cubes(level, 0.0, spacing=(h, h, h))
    vertices += low

    # The largest piece: a crumb the closing left beside it is not the dragon.
    n = len(vertices)
    links = scipy.sparse.coo_matrix((np.ones(2 * len(triangles)),
                                     (np.r_[triangles[:, 0], triangles[:, 0]], np.r_[triangles[:, 1], triangles[:, 2]])),
                                    shape=(n, n))
    _, piece = scipy.sparse.csgraph.connected_components(links, directed=False)
    triangles = triangles[piece[triangles[:, 0]] == np.bincount(piece[triangles[:, 0]]).argmax()]
    used = np.unique(triangles)
    renumber = np.zeros(n, int)
    renumber[used] = np.arange(len(used))
    vertices, triangles = vertices[used], renumber[triangles]
    volume = np.einsum("ij,ij->i", vertices[triangles[:, 0]],
                       np.cross(vertices[triangles[:, 1]], vertices[triangles[:, 2]])).sum() / 6
    if volume < 0:
        triangles = triangles[:, ::-1]
    return vertices, triangles


def smoothed(vertices, triangles, rounds=20):
    """Taubin's smoothing: takes the grid's steps off without shrinking the surface. The soles stay
    flat."""
    n = len(vertices)
    i = np.r_[triangles[:, 0], triangles[:, 1], triangles[:, 2], triangles[:, 1], triangles[:, 2], triangles[:, 0]]
    j = np.r_[triangles[:, 1], triangles[:, 2], triangles[:, 0], triangles[:, 0], triangles[:, 1], triangles[:, 2]]
    around = scipy.sparse.coo_matrix((np.ones(len(i)), (i, j)), shape=(n, n)).tocsr()
    around.data[:] = 1.0
    mean = scipy.sparse.diags(1.0 / np.asarray(around.sum(1)).ravel()) @ around
    ground = vertices[:, 2].min()
    sole = vertices[:, 2] < ground + 1e-6
    for _ in range(rounds):
        for factor in (0.5, -0.53):
            vertices = vertices + factor * (mean @ vertices - vertices)
    vertices[sole, 2] = ground
    vertices[:, 2] = np.maximum(vertices[:, 2], ground)
    return vertices


def remeshed(vertices, triangles, length):
    """The surface in triangles of about length, smaller where it curves."""
    meshes = pymeshlab.MeshSet()
    meshes.add_mesh(pymeshlab.Mesh(vertices, triangles))
    meshes.meshing_isotropic_explicit_remeshing(iterations=10, targetlen=pymeshlab.PureValue(length),
                                               featuredeg=60, adaptive=True)
    meshes.compute_selection_by_self_intersections_per_face()
    crossing = meshes.current_mesh().selected_face_number()
    if crossing:
        raise Exception("The remeshed surface passes through itself at %d triangles: grow or close "
                        "the dragon more, or remesh it finer." % crossing)
    mesh = meshes.current_mesh()
    vertices, triangles = mesh.vertex_matrix().copy(), mesh.face_matrix()
    vertices[np.abs(vertices[:, 2]) < 1e-6, 2] = 0.0
    return vertices, triangles


def main():
    size = float(sys.argv[1]) if len(sys.argv) > 1 else 0.14
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "InigoTheDragon.txt")
    started = time.time()
    vertices, triangles = solid(*dragon(), GRID)
    vertices = smoothed(vertices, triangles)

    # On its soles at Z = 0, their middle on the Z axis.
    vertices[:, 2] -= vertices[:, 2].min()
    vertices[:, :2] -= vertices[vertices[:, 2] < 1e-9][:, :2].mean(0)

    vertices, triangles = remeshed(vertices, triangles, size)
    nodes, tets = TetMesh.tetrahedra(vertices.tolist(), triangles.tolist(), size=size, angle=None, grow=INSIDE)
    TetMesh.write(out, nodes, tets, decimals=4)
    print("%.0f s" % (time.time() - started))


if __name__ == "__main__":
    main()
