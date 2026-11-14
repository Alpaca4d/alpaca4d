"""Makes SpotTheCow.txt, the tetrahedra of the SpotTheCow.py example, from Spot the cow.

    python3 SpotTheCowMesh.py [mesh size, m] [out.txt]

Spot is Keenan Crane's cow, a mesh used throughout geometry processing, released into the public
domain (CC0): https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/. This downloads it, turns it
to Rhino's axes - Z up, nose towards -X - scales it to a 2.4 m long cow, cuts its hooves flat to
stand on, and fills it with tetrahedra with gmsh (TetMesh.py). Standard library only.

SpotTheCow.txt holds the nodes, one "x y z" a line, then the tetrahedra, one "a b c d" a line of
node indices from 0; a line "nodes N" or "tetrahedra N" starts each part."""
import io, os, sys, urllib.request, zipfile
import TetMesh

URL = "https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/spot.zip"
SCALE = 1.4     # Spot is 1.72 long; a cow is about 2.4 m
SOLE = 0.04     # the hooves are cut flat this high above their lowest point, before scaling


def spot():
    """Spot's vertices in Rhino's axes, scaled, and its triangles."""
    with urllib.request.urlopen(URL) as response:
        archive = zipfile.ZipFile(io.BytesIO(response.read()))
    vertices, triangles = [], []
    for line in archive.read("spot/spot_triangulated.obj").decode().splitlines():
        p = line.split()
        if p and p[0] == "v":
            x, y, z = map(float, p[1:4])
            # Spot is Y up and looks along +Z; X = z, Y = x, Z = y is a turn, not a mirror.
            vertices.append([SCALE * z, SCALE * x, SCALE * y])
        elif p and p[0] == "f":
            triangles.append([int(q.split("/")[0]) - 1 for q in p[1:4]])
    return vertices, triangles


def cut_flat(vertices, triangles, height):
    """The surface above Z = lowest + height, closed by a flat cap where each hoof is cut, and
    moved down to stand on Z = 0. The caps face down, as the rest of the surface faces out."""
    low = min(v[2] for v in vertices) + height
    above = [v[2] > low for v in vertices]
    crossing = {}

    def cut(a, b):
        key = (min(a, b), max(a, b))
        if key not in crossing:
            pa, pb = vertices[a], vertices[b]
            t = (low - pa[2]) / (pb[2] - pa[2])
            crossing[key] = len(vertices)
            vertices.append([pa[i] + t * (pb[i] - pa[i]) for i in range(3)])
        return crossing[key]

    kept = []
    for tri in triangles:
        polygon = []
        for i in range(3):
            a, b = tri[i], tri[(i + 1) % 3]
            if above[a]:
                polygon.append(a)
            if above[a] != above[b]:
                polygon.append(cut(a, b))
        kept += [[polygon[0], polygon[i], polygon[i + 1]] for i in range(1, len(polygon) - 1)]

    # The open edges are the cut, as loops: each edge runs one way in the triangle it borders, so
    # the cap's triangles take it the other way round.
    edges = {}
    for tri in kept:
        for i in range(3):
            edges[(tri[i], tri[(i + 1) % 3])] = True
    following = {}
    for a, b in edges:
        if (b, a) not in edges:
            following[b] = a
    while following:
        start = next(iter(following))
        loop = [start]
        while following[loop[-1]] != start:
            loop.append(following[loop[-1]])
        for v in loop:
            del following[v]
        centre = len(vertices)
        vertices.append([sum(vertices[v][i] for v in loop) / len(loop) for i in range(3)])
        kept += [[loop[i], loop[(i + 1) % len(loop)], centre] for i in range(len(loop))]

    used = sorted(set(v for tri in kept for v in tri))
    index = dict((v, i) for i, v in enumerate(used))
    return [[vertices[v][0], vertices[v][1], vertices[v][2] - low] for v in used], \
           [[index[v] for v in tri] for tri in kept]


def main():
    size = float(sys.argv[1]) if len(sys.argv) > 1 else 0.14
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "SpotTheCow.txt")
    TetMesh.write(out, *TetMesh.tetrahedra(*cut_flat(*spot(), height=SOLE * SCALE), size=size))


if __name__ == "__main__":
    main()
