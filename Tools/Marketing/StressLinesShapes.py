# The shapes of the principal stress lines studies (StressLinesStudies.py): each study's mesh, its
# supports, its seed and the rest, as plain numbers. Plain Python, with nothing of Rhino's or .NET's,
# so that the same shapes can be tried outside Rhino - the Kangaroo forms relaxed, the lifts tuned -
# before a definition is written.
#
# Written for both Python 3 and IronPython 2.7: no f-strings, ASCII only.

import math


# ---------------------------------------------------------------- meshes

def grid(x0, y0, width, height, nx, ny):
    """A rectangle in nx x ny quads: (x, y) vertices row by row, and the quads."""
    vertices = [(x0 + width * i / float(nx), y0 + height * j / float(ny)) for j in range(ny + 1) for i in range(nx + 1)]
    at = lambda i, j: j * (nx + 1) + i
    quads = [(at(i, j), at(i + 1, j), at(i + 1, j + 1), at(i, j + 1)) for j in range(ny) for i in range(nx)]
    return vertices, quads, []


def grid_creased(half, n):
    """A square from -half to half in n x n cells, each cut into two triangles along the diagonal
    parallel to the square's diagonal through that quarter: edges along both diagonals, where a
    groin vault folds."""
    vertices = [(-half + 2 * half * i / float(n), -half + 2 * half * j / float(n)) for j in range(n + 1) for i in range(n + 1)]
    at = lambda i, j: j * (n + 1) + i
    tris = []
    for j in range(n):
        for i in range(n):
            a, b, c, d = at(i, j), at(i + 1, j), at(i + 1, j + 1), at(i, j + 1)
            same_sign = (i < n // 2) == (j < n // 2)
            if same_sign:
                tris += [(a, b, c), (a, c, d)]
            else:
                tris += [(a, b, d), (b, c, d)]
    return vertices, [], tris


def polar(radius, rings, segments):
    """A disc: a vertex in the middle, rings rings of segments vertices out to radius, a fan of
    triangles round the middle and quads beyond."""
    vertices = [(0.0, 0.0)]
    for r in range(1, rings + 1):
        for s in range(segments):
            angle = 2 * math.pi * s / segments
            vertices.append((radius * r / float(rings) * math.cos(angle), radius * r / float(rings) * math.sin(angle)))
    at = lambda r, s: 1 + (r - 1) * segments + s % segments
    tris = [(0, at(1, s), at(1, s + 1)) for s in range(segments)]
    quads = [(at(r, s), at(r + 1, s), at(r + 1, s + 1), at(r, s + 1)) for r in range(1, rings) for s in range(segments)]
    return vertices, quads, tris


def triangulated(corners, n):
    """A polygon fanned into triangles from its middle, each cut into n x n smaller ones, the
    vertices shared: corners as (x, y), in order round."""
    middle = (sum(c[0] for c in corners) / len(corners), sum(c[1] for c in corners) / len(corners))
    index, vertices, tris = {}, [], []

    def vertex(p):
        key = (round(p[0], 6), round(p[1], 6))
        if key not in index:
            index[key] = len(vertices)
            vertices.append(p)
        return index[key]

    for k in range(len(corners)):
        a, b = corners[k], corners[(k + 1) % len(corners)]
        point = lambda i, j: (middle[0] + (a[0] - middle[0]) * i / float(n) + (b[0] - a[0]) * j / float(n),
                             middle[1] + (a[1] - middle[1]) * i / float(n) + (b[1] - a[1]) * j / float(n))
        for i in range(n):
            for j in range(i + 1):
                p, q, r = vertex(point(i, j)), vertex(point(i + 1, j)), vertex(point(i + 1, j + 1))
                tris.append((p, q, r))
                if j < i:
                    tris.append((p, r, vertex(point(i, j + 1))))
    return vertices, [], tris


def regular(sides, radius, start=90.0):
    """The corners of a regular polygon."""
    return [(radius * math.cos(math.radians(start + 360.0 * k / sides)), radius * math.sin(math.radians(start + 360.0 * k / sides)))
            for k in range(sides)]


def nearest(vertices, point):
    return min(range(len(vertices)), key=lambda i: (vertices[i][0] - point[0]) ** 2 + (vertices[i][1] - point[1]) ** 2)


def within(vertices, point, distance):
    return [i for i, v in enumerate(vertices) if (v[0] - point[0]) ** 2 + (v[1] - point[1]) ** 2 <= distance ** 2 + 1e-9]


def neighbours(quads, tris, index):
    """The vertices sharing an edge with vertex index."""
    found = set()
    for face in list(quads) + list(tris):
        if index in face:
            k = face.index(index)
            found.add(face[k - 1])
            found.add(face[(k + 1) % len(face)])
    return sorted(found)


def on_boundary(quads, tris):
    """The vertices on the mesh's edge: on an edge that only one face has."""
    count = {}
    for face in list(quads) + list(tris):
        for k in range(len(face)):
            e = tuple(sorted((face[k], face[(k + 1) % len(face)])))
            count[e] = count.get(e, 0) + 1
    return set(v for e, c in count.items() if c == 1 for v in e)


def feet(vertices, quads, tris, points):
    """A footing at each of points: the nearest vertex and its neighbours on the mesh's edge."""
    edge = on_boundary(quads, tris)
    held = []
    for p in points:
        i = nearest(vertices, p)
        held += [i] + [k for k in neighbours(quads, tris, i) if k in edge]
    return sorted(set(held))


def lifted(vertices, height):
    return [(x, y, height(x, y)) for x, y in vertices]


# ---------------------------------------------------------------- the studies

def polar_point(r, degrees):
    return (r * math.cos(math.radians(degrees)), r * math.sin(math.radians(degrees)))


def study(name):
    """A study by name: a dict of what StressLinesStudies.py builds it from. A Kangaroo study's seed
    lies under its net: Principal Stress Lines takes the nearest point of the form, and from above
    that would be the crown, where on a symmetric form the two principal stresses are the same.

    kind: "slab" (bending lines, ribs under it and columns down to a floor), "shell" (a surface
    given, membrane lines, on the ground) or "kangaroo" (a flat net form-found, then as a shell).
    vertices (x, y, z), quads and tris; held (vertex indexes) and fixed (built in, or else pinned);
    load: "udl" or "gravity"; columns: (x, y) under a slab, or under a shell's support; height,
    the columns' height down to the floor; copies: offsets of a hall of copies, for the pictures."""
    s = {"copies": [(0.0, 0.0)], "columns": [], "height": 0.0, "lift": 0.0, "rib": 0.3, "fixed": False,
         "column_radius": 0.35, "centre": (0.0, 0.0), "symmetry": 1, "height_of": None}

    if name == "corner-slab":
        v, q, t = grid(0, 0, 12, 12, 48, 48)
        s.update(title="Slab on four corner columns", kind="slab", thickness=0.25, load="udl", udl=10.0,
                 held=[nearest(v, c) for c in ((0, 0), (12, 0), (12, 12), (0, 12))],
                 columns=[(0, 0), (12, 0), (12, 12), (0, 12)], height=4.0, column_radius=0.3,
                 seed=(7.3, 4.1, 0), centre=(6, 6), symmetry=4, separation=0.45, plan="12 x 12 m",
                 supported="pinned on four corner columns")
    elif name == "round-umbrella":
        v, q, t = polar(8.0, 32, 96)
        s.update(title="Nervi round umbrella", kind="slab", thickness=0.2, load="udl", udl=5.0, fixed=True,
                 held=within(v, (0, 0), 0.5), columns=[(0, 0)], height=6.0, column_radius=0.45, rib=0.35,
                 seed=polar_point(4.3, 17) + (0,), symmetry=4, separation=0.55, plan="16 m across",
                 supported="built into one column over a head 1 m across")
    elif name == "round-slab":
        v, q, t = polar(10.0, 40, 96)
        columns = [polar_point(7.0, 45 * k) for k in range(8)]
        s.update(title="Round slab on eight columns", kind="slab", thickness=0.25, load="udl", udl=10.0,
                 held=[nearest(v, c) for c in columns], columns=columns, height=5.0,
                 seed=polar_point(3.1, 11) + (0,), symmetry=8, separation=0.5, plan="20 m across",
                 supported="pinned on eight columns in a ring 14 m across")
    elif name == "candela-umbrella":
        v, q, t = grid(-6, -6, 12, 12, 48, 48)
        c = 2.4 / 36.0
        s.update(title="Candela umbrella", kind="shell", thickness=0.08, load="gravity", fixed=True,
                 held=[i for i, p in enumerate(v) if abs(p[0]) <= 0.5 and abs(p[1]) <= 0.5],
                 columns=[(0, 0)], height=4.0, column_radius=0.3,
                 seed=(2.9, 1.7, 0), symmetry=4, height_of=lambda x, y: c * abs(x) * abs(y), separation=0.35,
                 plan="12 x 12 m",
                 supported="built into one column over a head 1 m square",
                 copies=[(12.5 * a, 12.5 * b) for a in (-1, 0, 1) for b in (-1, 0, 1)])
        v = lifted(v, lambda x, y: c * abs(x) * abs(y))
    elif name == "groin-vault":
        v, q, t = grid_creased(6.0, 48)
        rise = 4.0
        height = lambda x, y: rise * max(1 - (x / 6.0) ** 2, 1 - (y / 6.0) ** 2)
        s.update(title="Groin vault", kind="shell", thickness=0.15, load="gravity",
                 held=feet(v, q, t, [(-6, -6), (6, -6), (6, 6), (-6, 6)]),
                 seed=(2.3, -0.9, 0), symmetry=4, height_of=height, separation=0.35, plan="12 x 12 m",
                 supported="pinned at its four corners")
        v = lifted(v, height)
    elif name == "palazzetto":
        v, q, t = polar(15.0, 20, 96)
        rim = [polar_point(15.0, 360.0 * k / 16) for k in range(16)]
        held = sorted(set(nearest(v, p) for p in rim) | set(nearest(v, polar_point(14.25, 360.0 * k / 16)) for k in range(16)))
        s.update(title="Nervi Palazzetto", kind="kangaroo", thickness=0.12, load="gravity", lift=0.0015,
                 held=held, seed=polar_point(6.1, 13) + (0,), symmetry=16, separation=0.6, plan="30 m across",
                 supported="pinned on sixteen footings round its rim")
    elif name == "isler-shell":
        v, q, t = triangulated(regular(3, 24 / math.sqrt(3)), 16)
        corners = regular(3, 24 / math.sqrt(3))
        s.update(title="Isler three-point shell", kind="kangaroo", thickness=0.1, load="gravity", lift=0.008,
                 held=feet(v, q, t, corners), seed=(1.9, -2.6, 0), symmetry=3, separation=0.5, plan="24 m a side",
                 supported="pinned at its three corners")
    elif name == "hexagonal-shell":
        v, q, t = triangulated(regular(6, 12.0), 12)
        s.update(title="Hexagonal shell", kind="kangaroo", thickness=0.1, load="gravity", lift=0.015,
                 held=feet(v, q, t, regular(6, 12.0)), seed=(3.3, 1.4, 0), symmetry=6, separation=0.5, plan="24 m across",
                 supported="pinned at its six corners")
    else:
        raise Exception("no study " + name)

    if len(v[0]) == 2:
        v = [(x, y, 0.0) for x, y in v]
    # The seed turned round the middle once for each of the shape's symmetries: a field grown from
    # them comes out as symmetric as the shape. On a surface, each lifted onto it.
    cx, cy = s["centre"]
    seeds = []
    for k in range(s["symmetry"]):
        a = 2 * math.pi * k / s["symmetry"]
        x, y = s["seed"][0] - cx, s["seed"][1] - cy
        x, y = cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a)
        seeds.append((x, y, s["height_of"](x, y) if s["height_of"] else s["seed"][2]))
    s.update(name=name, vertices=v, quads=q, tris=t, seeds=seeds)
    return s


STUDIES = ["corner-slab", "round-umbrella", "round-slab", "candela-umbrella", "groin-vault", "palazzetto",
           "isler-shell", "hexagonal-shell"]
