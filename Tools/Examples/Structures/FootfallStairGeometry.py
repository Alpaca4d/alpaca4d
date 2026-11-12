# The code of FootfallStair.py's GhPython components: the stairs' geometry - a U-shaped stair (stair), a
# helical one of shells (helical) and a straight one of three flights (straight) - the map of the response
# factor Footfall Analysis finds where people walk, and the title of a mode.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import math

import Rhino.Geometry as rg
import System.Drawing


def _key(p):
    """A point, to a tenth of a millimetre: the same node, whichever element it was made for. Not to the
    millimetre: a going of 275 mm puts the middle of a tread at 137.5 mm, on the edge between two
    millimetres, where the same point worked out two ways rounds either way."""
    return (int(round(p.X * 10000.0)), int(round(p.Y * 10000.0)), int(round(p.Z * 10000.0)))


def _quads(mesh, corners):
    """Adds a quad of four points to mesh, its vertices shared with the faces already there."""
    index = []
    for p in corners:
        index.append(mesh.Vertices.Add(p))
    mesh.Faces.AddFace(index[0], index[1], index[2], index[3])


def _grid(origin, u, v, nu, nv, us=None):
    """A mesh of quads over the parallelogram origin + a u + b v, a and b from 0 to 1, in nu by nv faces;
    us, when given, are the steps along u instead of even ones."""
    mesh = rg.Mesh()
    steps_u = us if us is not None else [i / float(nu) for i in range(nu + 1)]
    steps_v = [j / float(nv) for j in range(nv + 1)]
    for i in range(len(steps_u) - 1):
        for j in range(nv):
            a0, a1 = steps_u[i], steps_u[i + 1]
            b0, b1 = steps_v[j], steps_v[j + 1]
            _quads(mesh, [origin + u * a0 + v * b0, origin + u * a1 + v * b0,
                          origin + u * a1 + v * b1, origin + u * a0 + v * b1])
    mesh.Vertices.CombineIdentical(True, True)
    mesh.Normals.ComputeNormals()
    return mesh


def _segments(points):
    return [rg.Line(points[i], points[i + 1]) for i in range(len(points) - 1)]


def stair(Width, LongRisers, MiddleRisers, Rise, Going, Finishes, Imposed):
    """A U-shaped steel stair between two floors: a long flight up along X, a quarter landing, a short
    flight across along Y, a second quarter landing and a long flight back along -X to the floor
    above. Width in m; Rise and Going in mm; Finishes and Imposed in kN/m2.

    Each flight hangs between two stringers, straight lines through the middle of every tread's ends,
    so that each tread is fixed to them at the middle of its two ends. A short upright piece, half a
    rise, takes each stringer's end down or up to the landing or the floor. The treads are open, with
    no risers. A landing is a plate on a frame of four edge beams, with a post under its free corner."""
    w = float(Width)
    r = float(Rise) / 1000.0
    g = float(Going) / 1000.0
    n_long, n_mid = int(LongRisers), int(MiddleRisers)
    across = max(2, int(round(w / 0.3)))    # elements across a tread or a landing, about 300 mm

    stringers, treads, landings, landing_beams, posts = [], [], [], [], []
    feet, tops = [], []
    mass_area = {}

    def area(mesh):
        """Spreads each face's area over its four corners, for the mass of the finishes."""
        for f in range(mesh.Faces.Count):
            face = mesh.Faces[f]
            corners = [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D]]
            p = [rg.Point3d(c) for c in corners]
            a = 0.5 * (rg.Vector3d.CrossProduct(p[1] - p[0], p[3] - p[0]).Length +
                       rg.Vector3d.CrossProduct(p[1] - p[2], p[3] - p[2]).Length)
            for c in p:
                k = _key(c)
                if k not in mass_area:
                    mass_area[k] = [c, 0.0]
                mass_area[k][1] += a / 4.0

    def flight(origin, d, s, z0, n, first=False, last=False):
        """n risers up from z0, going along d from origin, across s. Returns nothing; adds its
        stringers and treads."""
        run = (n - 1) * g
        for side in (0.0, w):
            base = origin + s * side
            # The stringer: through the middle of each tread's end, tread k at z0 + k r.
            line = [base + d * ((k - 0.5) * g) + rg.Vector3d(0, 0, z0 + k * r) for k in range(1, n)]
            start = base + d * (-0.5 * g) + rg.Vector3d(0, 0, z0) if first else base + rg.Vector3d(0, 0, z0)
            end = base + d * (run + 0.5 * g) + rg.Vector3d(0, 0, z0 + n * r) if last else base + d * run + rg.Vector3d(0, 0, z0 + n * r)
            points = [start]
            if not first:
                # Half a rise up from the landing to where the line starts.
                points.append(base + rg.Vector3d(0, 0, z0 + 0.5 * r))
            points += line
            if not last:
                points.append(base + d * run + rg.Vector3d(0, 0, z0 + (n - 0.5) * r))
            points.append(end)
            stringers.extend(_segments(points))
            if first:
                feet.append(start)
            if last:
                tops.append(end)
        for k in range(1, n):
            corner = origin + d * ((k - 1) * g) + rg.Vector3d(0, 0, z0 + k * r)
            mesh = _grid(corner, d * g, s * w, 2, across)
            treads.append(mesh)
            area(mesh)

    def landing(corner, size_u, size_v, z):
        """A square landing from corner, along size_u and size_v, at z: its plate and its edge beams."""
        origin = corner + rg.Vector3d(0, 0, z)
        mesh = _grid(origin, size_u, size_v, across, across)
        landings.append(mesh)
        area(mesh)
        for a, b in ((origin, size_u), (origin, size_v), (origin + size_u, size_v), (origin + size_v, size_u)):
            landing_beams.extend(_segments([a + b * (i / float(across)) for i in range(across + 1)]))

    x, y, z = rg.Vector3d(1, 0, 0), rg.Vector3d(0, 1, 0), rg.Vector3d(0, 0, 1)
    run_long, run_mid = (n_long - 1) * g, (n_mid - 1) * g
    z1 = n_long * r
    z2 = z1 + n_mid * r
    z3 = z2 + n_long * r

    # Up along X, onto the first landing.
    flight(rg.Point3d(0, 0, 0), x, y, 0.0, n_long, first=True)
    landing(rg.Point3d(run_long, 0, 0), x * w, y * w, z1)
    # Across along Y, onto the second.
    flight(rg.Point3d(run_long, w, 0), y, x, z1, n_mid)
    landing(rg.Point3d(run_long, w + run_mid, 0), x * w, y * w, z2)
    # Back along -X, to the floor above.
    flight(rg.Point3d(run_long, w + run_mid, 0), -x, y, z2, n_long, last=True)

    # A post under each landing's free corner, the one no stringer reaches.
    for corner, top in ((rg.Point3d(run_long + w, 0, 0), z1), (rg.Point3d(run_long + w, 2 * w + run_mid, 0), z2)):
        posts.append(rg.Line(corner, corner + z * top))
        feet.append(corner)

    # The finishes and a tenth of the imposed load, SCI P354's mass in use, at the nodes of the plates.
    per_area = (float(Finishes) + 0.1 * float(Imposed)) / 9.81 * 1000.0   # kg/m2
    mass_points, masses = [], []
    for k in sorted(mass_area):
        p, a = mass_area[k]
        m = per_area * a
        if m > 0.0:
            mass_points.append(p)
            masses.append(rg.Vector3d(m, m, m))

    steps = 2 * n_long + n_mid
    height = z3
    return stringers, treads, landings, landing_beams, posts, feet, tops, mass_points, masses, steps, height


# ---------------------------------------------------------------- a helical stair, all shells

def helical(Height, Risers, Turn, InnerRadius, Width, Parapet, Finishes, Imposed):
    """A helical stair of shells round an open well: a ribbon, the helicoid it climbs on, between two
    parapets, continuous curved walls Parapet tall that rise with it and carry it, the inner one round
    InnerRadius and the outer one Width further out. It turns Turn degrees as it climbs Height in Risers
    steps, and is built in at both ends: on the floor at its foot and into the floor it arrives at.

    The steps sit on the ribbon: drawn, for the map of the response factor, and their weight, with the
    finishes and a tenth of the imposed load, at the ribbon's nodes. Finishes and Imposed in kN/m2."""
    h = float(Height)
    n = int(Risers)
    turn = math.radians(float(Turn))
    ri, w, hp = float(InnerRadius), float(Width), float(Parapet)
    ro = ri + w
    rise = h / n
    pitch = h / turn                                   # height per radian
    per_step = 2
    na = n * per_step
    nr = max(2, int(round(w / 0.3)))
    nv = max(2, int(round(hp / 0.25)))
    angles = [turn * j / float(na) for j in range(na + 1)]
    radii = [ri + w * i / float(nr) for i in range(nr + 1)]

    def at(r, a, z):
        return rg.Point3d(r * math.cos(a), r * math.sin(a), z)

    def surface(rows, columns, point):
        """A mesh of quads over a grid of rows by columns, point(row, column) its nodes."""
        mesh = rg.Mesh()
        for i in range(rows):
            for j in range(columns):
                _quads(mesh, [point(i, j), point(i + 1, j), point(i + 1, j + 1), point(i, j + 1)])
        mesh.Vertices.CombineIdentical(True, True)
        mesh.Normals.ComputeNormals()
        return mesh

    ribbon = surface(nr, na, lambda i, j: at(radii[i], angles[j], pitch * angles[j]))
    inner = surface(nv, na, lambda i, j: at(ri, angles[j], pitch * angles[j] + hp * i / float(nv)))
    outer = surface(nv, na, lambda i, j: at(ro, angles[j], pitch * angles[j] + hp * i / float(nv)))

    # The steps, drawn only: tread k level at k rises, over its share of the turn, with its riser.
    steps = []
    da = turn / n
    for k in range(1, n + 1):
        a0, a1 = (k - 1) * da, k * da
        z = k * rise
        mesh = rg.Mesh()
        cuts = [a0 + (a1 - a0) * t / float(per_step) for t in range(per_step + 1)]
        for i in range(nr):
            for j in range(per_step):
                _quads(mesh, [at(radii[i], cuts[j], z), at(radii[i + 1], cuts[j], z),
                              at(radii[i + 1], cuts[j + 1], z), at(radii[i], cuts[j + 1], z)])
            # The riser, from the ribbon up to the tread.
            _quads(mesh, [at(radii[i], a0, pitch * a0), at(radii[i + 1], a0, pitch * a0),
                          at(radii[i + 1], a0, z), at(radii[i], a0, z)])
        mesh.Vertices.CombineIdentical(True, True)
        mesh.Normals.ComputeNormals()
        steps.append(mesh)

    # Built in at both ends: the ribbon's end and both parapets' end edges.
    supports = []
    for a in (0.0, turn):
        for r in radii:
            supports.append(at(r, a, pitch * a))
        for r in (ri, ro):
            for i in range(1, nv + 1):
                supports.append(at(r, a, pitch * a + hp * i / float(nv)))

    mass_points, masses = _masses([ribbon], Finishes, Imposed)
    return ribbon, inner, outer, steps, supports, mass_points, masses, n, h


# ---------------------------------------------------------------- a straight stair, three flights

def straight(Width, Risers, Flights, Rise, Going, Landing, Depth, Finishes, Imposed):
    """A straight stair of Flights flights of Risers risers each, in one line, with a landing Landing long
    between each two, held only at its two ends. Two steel plate stringers, Depth deep below the line
    through the middle of the treads and 100 mm above it, run its whole length; the treads, open, and the
    landings are plates between them. A tread is welded to the stringers along its ends: it shares the node
    at the middle of each end with them, and its four corners follow the stringer nodes half a rise below
    or above them, LinkRetained to LinkConstrained, through rigid links. Without them each tread would hang
    from one node a side, and the stringers, tall thin plates, would flap sideways.
    Width in m, Rise, Going and Depth in mm; Finishes and Imposed in kN/m2."""
    w = float(Width)
    n, flights = int(Risers), int(Flights)
    r, g = float(Rise) / 1000.0, float(Going) / 1000.0
    landing, depth = float(Landing), float(Depth) / 1000.0
    up = 0.1
    across = max(2, int(round(w / 0.3)))
    treads, landings = [], []
    retained, constrained = [], []

    # The stringers' line through the middle of the treads, station by station along X, with the
    # landings between the flights level on it.
    line = []
    x, z = 0.0, 0.0
    for f in range(flights):
        run = (n - 1) * g
        # From half a going before the first riser, at the level below, to half a going past the last,
        # at the level above: through the middle of every tread, and its ends.
        for k in range(0, 2 * n + 1):
            xs = x - 0.5 * g + 0.5 * k * g
            line.append((xs, z + r * ((xs - x) / g + 0.5)))
        for k in range(1, n):
            treads.append(_grid(rg.Point3d(x + (k - 1) * g, 0, z + k * r), rg.Vector3d(g, 0, 0), rg.Vector3d(0, w, 0), 2, across))
            for y in (0.0, w):
                # The front corner over the stringer node half a rise below it, the back one under the
                # node half a rise above.
                for xc, dz in ((x + (k - 1) * g, -0.5 * r), (x + k * g, 0.5 * r)):
                    retained.append(rg.Point3d(xc, y, z + k * r + dz))
                    constrained.append(rg.Point3d(xc, y, z + k * r))
        x_end, z = x + run, z + n * r
        if f < flights - 1:
            # The landing, its edges on the line between half a going in from either end.
            inner = landing - g
            count = max(1, int(round(inner / 0.3)))
            for k in range(1, count + 1):
                line.append((x_end + 0.5 * g + inner * k / float(count), z))
            steps_u = [0.0, 0.5 * g / landing] + [(0.5 * g + inner * k / float(count)) / landing for k in range(1, count)] + \
                      [(landing - 0.5 * g) / landing, 1.0]
            landings.append(_grid(rg.Point3d(x_end, 0, z), rg.Vector3d(landing, 0, 0), rg.Vector3d(0, w, 0), len(steps_u) - 1, across, steps_u))
            x = x_end + landing
        else:
            x = x_end
    # One station each, in order.
    stations = []
    for xs, zs in line:
        if not stations or abs(xs - stations[-1][0]) > 1e-6:
            stations.append((xs, zs))

    offsets = [-depth * (1.0 - k / float(max(1, int(round(depth / 0.15))))) for k in range(int(round(depth / 0.15)) + 1)] + [up]
    offsets = sorted(set(round(o, 6) for o in offsets))
    stringers = []
    for y in (0.0, w):
        mesh = rg.Mesh()
        for j in range(len(stations) - 1):
            (x0, z0), (x1, z1) = stations[j], stations[j + 1]
            for i in range(len(offsets) - 1):
                _quads(mesh, [rg.Point3d(x0, y, z0 + offsets[i]), rg.Point3d(x1, y, z1 + offsets[i]),
                              rg.Point3d(x1, y, z1 + offsets[i + 1]), rg.Point3d(x0, y, z0 + offsets[i + 1])])
        mesh.Vertices.CombineIdentical(True, True)
        mesh.Normals.ComputeNormals()
        stringers.append(mesh)

    # Held at the two ends of the stringers, every node of their end edges: on the floor and at the
    # floor above, and nowhere between.
    supports = []
    for xs, zs in (stations[0], stations[-1]):
        for y in (0.0, w):
            for o in offsets:
                supports.append(rg.Point3d(xs, y, zs + o))

    mass_points, masses = _masses(treads + landings, Finishes, Imposed)
    return stringers, treads, landings, supports, retained, constrained, mass_points, masses, flights * n, z


def _masses(meshes, Finishes, Imposed):
    """The finishes and a tenth of the imposed load as masses at the meshes' nodes, each node its share of
    the faces round it: SCI P354's mass in use. In kg, one per node."""
    per_area = (float(Finishes) + 0.1 * float(Imposed)) / 9.81 * 1000.0
    share = {}
    for mesh in meshes:
        for f in range(mesh.Faces.Count):
            face = mesh.Faces[f]
            p = [rg.Point3d(mesh.Vertices[k]) for k in (face.A, face.B, face.C, face.D)]
            a = 0.5 * (rg.Vector3d.CrossProduct(p[1] - p[0], p[3] - p[0]).Length +
                       rg.Vector3d.CrossProduct(p[1] - p[2], p[3] - p[2]).Length)
            for c in p:
                k = _key(c)
                if k not in share:
                    share[k] = [c, 0.0]
                share[k][1] += a / 4.0
    points, masses = [], []
    for k in sorted(share):
        m = per_area * share[k][1]
        if m > 0.0:
            points.append(share[k][0])
            masses.append(rg.Vector3d(m, m, m))
    return points, masses


# ---------------------------------------------------------------- the response map

# The response factor's colours, from still to the limit: a pale green, Alpaca4d's light green, a
# yellow and its orange; past the limit, red. Not the colours of the modes, which are blues.
SCALE = [(214, 236, 190), (175, 201, 48), (242, 201, 76), (235, 108, 63)]
# Past the limit, reds, light to dark, so that how far past it still shows.
OVER = [(229, 83, 61), (198, 40, 40), (122, 18, 28)]
DARK = (42, 45, 49)


def _ramp(stops, t):
    t = min(1.0, max(0.0, t))
    f = t * (len(stops) - 1)
    i = min(int(f), len(stops) - 2)
    u = f - i
    a, b = stops[i], stops[i + 1]
    return System.Drawing.Color.FromArgb(255, *[int(round(a[j] + (b[j] - a[j]) * u)) for j in range(3)])


def _colour(t):
    t = min(1.0, max(0.0, t))
    f = t * (len(SCALE) - 1)
    i = min(int(f), len(SCALE) - 2)
    u = f - i
    a, b = SCALE[i], SCALE[i + 1]
    return System.Drawing.Color.FromArgb(255, *[int(round(a[j] + (b[j] - a[j]) * u)) for j in range(3)])


def response_map(Surfaces, Shown, Points, Factors, Limit, Bands, Lift):
    """Where people walk, coloured by the response factor: Surfaces, the analysed meshes they walk on, read
    at their nodes; drawn on Shown, the steps of a stair drawn over its ribbon, from the nearest of those
    nodes, or on Surfaces themselves when Shown is empty, raised Lift. Bands colours over 0 to the largest,
    pale green to orange, and red past Limit, the response factor accepted. Gives what a Legend needs - the value at each colour's
    edge and the colours - the node that shakes the most, a label for it and a marker on it."""
    factor = {}
    for p, f in zip(Points, Factors):
        factor[_key(p)] = f
    nodes = {}
    for m in Surfaces:
        for v in range(m.Vertices.Count):
            p = rg.Point3d(m.Vertices[v])
            k = _key(p)
            if k in factor:
                nodes[k] = (p, factor[k])
    found = list(nodes.values())
    limit = float(Limit) if Limit else 32.0
    bands = max(2, int(Bands))
    top = max([f for p, f in found]) if found else 0.0
    lift = rg.Vector3d(0, 0, float(Lift or 0.0))

    # Even bands, so that the Legend, which labels its edges evenly between the smallest and the largest,
    # reads them right. Under the limit they span 0 to the largest, pale green to orange. Past it the limit
    # is an edge, with at least three bands under it, green to orange, and reds over it, darker further
    # past; the last band holds everything above it, however far past.
    if top > limit:
        under = min(bands - 1, max(3, int(round(bands * limit / top))))
        width = limit / float(under)
        edges = [width * k for k in range(bands + 1)]
        colours = [_colour((k + 0.5) / under) for k in range(under)] + \
                  [_ramp(OVER, (k + 0.5) / (bands - under)) for k in range(bands - under)]
    else:
        span = top if top > 0.0 else limit
        edges = [span * k / float(bands) for k in range(bands + 1)]
        colours = [_colour((k + 0.5) / bands) for k in range(bands)]

    def colour_of(f):
        for k in range(len(colours)):
            if f <= edges[k + 1] + 1e-9:
                return colours[k]
        return colours[-1]

    def nearest(p, count=3):
        """The response factor at p from the nearest nodes, each weighed by the inverse of its distance."""
        best = sorted(found, key=lambda q: q[0].DistanceToSquared(p))[:count]
        weights = [1.0 / max(1e-6, q[0].DistanceTo(p)) for q in best]
        return sum(wt * q[1] for wt, q in zip(weights, best)) / sum(weights)

    coloured = []
    shown = [m for m in (Shown or []) if m is not None]
    for m in (shown if shown else Surfaces):
        mesh = m.DuplicateMesh()
        mesh.VertexColors.Clear()
        for v in range(mesh.Vertices.Count):
            p = rg.Point3d(mesh.Vertices[v])
            f = nearest(p) if shown else factor.get(_key(p), 0.0)
            mesh.VertexColors.Add(colour_of(f))
        mesh.Translate(lift)
        coloured.append(mesh)

    peak = marker = None
    if found:
        # A pin on the node that shakes the most: a dark ball on it and a stalk up to the label, 1.2 m
        # above, clear of a parapet.
        at = max(found, key=lambda q: q[1])[0] + lift
        peak = at + rg.Vector3d(0, 0, 1.25)
        marker = rg.Mesh.CreateFromSphere(rg.Sphere(at, 0.06), 16, 12)
        stalk = rg.Mesh.CreateFromCylinder(rg.Cylinder(rg.Circle(rg.Plane(at, rg.Vector3d.ZAxis), 0.012), 1.2), 1, 12)
        if stalk is not None:
            marker.Append(stalk)
        marker.VertexColors.CreateMonotoneMesh(System.Drawing.Color.FromArgb(255, *DARK))
    label = "R = {0:.1f} {1} {2:g}".format(top, ">" if top > limit else "<=", limit)
    title = "Response factor R, limit {0:g}{1}".format(limit, ", largest {0:.0f}".format(top) if top > edges[-1] else "")
    return coloured, edges, colours, peak, label, marker, title


def mode_title(Frequencies, Mode):
    """The title of a mode's legend: its number and its frequency."""
    m = int(Mode or 0)
    if Frequencies and 0 <= m < len(Frequencies):
        return "Mode {0}: {1:.2f} Hz".format(m, float(Frequencies[m]))
    return "Mode {0}".format(m)
