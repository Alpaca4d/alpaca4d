"""A cantilever 10 x 1 x 1, fixed at x = 0 and loaded at x = 10 by a shear of 1 along -z spread over
the end face as the element would spread it, meshed into nx x ny x nz cells of six tetrahedra each.
E = 1e4 and nu = 0, so beam theory gives P L^3 / 3 E I + P L / (5/6) G A = 0.4 + 0.0024 = 0.4024.

    python3 cantilever.py tet4|tet10 nx ny nz deck.tcl

The deck prints TIP_UZ, the mean vertical displacement of the nodes at the free end."""
import sys, itertools

kind, nx, ny, nz, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
L, B, H, E, NU, P = 10.0, 1.0, 1.0, 1.0e4, 0.0, 1.0
quadratic = kind == "tet10"

coords, tags = [], {}
def node(p):
    key = tuple(round(c, 9) for c in p)
    if key not in tags:
        tags[key] = len(coords) + 1
        coords.append(key)
    return tags[key]

def sub(a, b): return [a[i] - b[i] for i in range(3)]
def cross(a, b): return [a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0]]
def dot(a, b): return sum(a[i] * b[i] for i in range(3))

# A cube cut into six tetrahedra round its main diagonal, the same way in every cell, so the
# faces of neighbouring cells are cut the same way and the mesh is conforming.
cube = [(0,0,0), (1,0,0), (1,1,0), (0,1,0), (0,0,1), (1,0,1), (1,1,1), (0,1,1)]
six = [(0,1,2,6), (0,2,3,6), (0,3,7,6), (0,7,4,6), (0,4,5,6), (0,5,1,6)]
# TenNodeTetrahedronStd's edge order: 1-2, 2-3, 3-1, 1-4, 2-4, 3-4.
edges = [(0,1), (1,2), (2,0), (0,3), (1,3), (2,3)]

elements = []
for i, j, k in itertools.product(range(nx), range(ny), range(nz)):
    corners = [((i + c[0]) * L / nx, (j + c[1]) * B / ny, (k + c[2]) * H / nz) for c in cube]
    for t in six:
        c = [corners[q] for q in t]
        if dot(sub(c[1], c[0]), cross(sub(c[2], c[0]), sub(c[3], c[0]))) < 0:
            c[1], c[2] = c[2], c[1]
        ids = [node(p) for p in c]
        if quadratic:
            ids += [node(tuple((c[a][q] + c[b][q]) / 2 for q in range(3))) for a, b in edges]
        elements.append(ids)

# The end load, triangle by triangle of the end face: a third of each triangle's share on each of
# its corners for the linear element, on each of its edge nodes for the quadratic one.
load = {}
for ids in elements:
    on = [q for q in range(4) if coords[ids[q] - 1][0] == L]
    if len(on) != 3:
        continue
    a, b, c = (coords[ids[q] - 1] for q in on)
    share = P * 0.5 * dot(cross(sub(b, a), sub(c, a)), cross(sub(b, a), sub(c, a))) ** 0.5 / (B * H)
    if quadratic:
        targets = [ids[4 + e] for e, (p, q) in enumerate(edges) if p in on and q in on]
    else:
        targets = [ids[q] for q in on]
    for n in targets:
        load[n] = load.get(n, 0.0) + share / 3.0

with open(out, "w") as f:
    f.write("wipe\nmodel BasicBuilder -ndm 3 -ndf 3\n")
    for n, p in enumerate(coords, 1):
        f.write(f"node {n} {p[0]} {p[1]} {p[2]}\n")
    for n, p in enumerate(coords, 1):
        if p[0] == 0.0:
            f.write(f"fix {n} 1 1 1\n")
    f.write(f"nDMaterial ElasticIsotropic 1 {E} {NU} 0.0\n")
    name = "TenNodeTetrahedronStd" if quadratic else "FourNodeTetrahedron"
    for e, ids in enumerate(elements, 1):
        f.write(f"element {name} {e} {' '.join(map(str, ids))} 1\n")
    f.write("timeSeries Linear 1\npattern Plain 1 1 {\n")
    for n, v in sorted(load.items()):
        f.write(f"    load {n} 0.0 0.0 {-v}\n")
    f.write("}\n")
    f.write("constraints Plain\nnumberer RCM\nsystem BandSPD\ntest NormDispIncr 1.0e-10 10\n"
            "algorithm Linear\nintegrator LoadControl 1.0\nanalysis Static\nanalyze 1\n")
    tip = [n for n, p in enumerate(coords, 1) if p[0] == L]
    f.write("set sum 0.0\n")
    for n in tip:
        f.write(f"set sum [expr {{$sum + [nodeDisp {n} 3]}}]\n")
    f.write(f"puts \"TIP_UZ [expr {{$sum / {len(tip)}}}] NODES {len(coords)} ELEMENTS {len(elements)}\"\nwipe\n")
