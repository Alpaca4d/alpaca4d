"""The geometry of the conventional structures of Structures.py, one function a structure.

Structures.py puts one of them into a GhPython component, whose inputs are the function's arguments
and whose outputs are what it returns, in order: lines for each kind of member, the points the
structure is held at, and the widths its loads are worked out from. Every member is a straight line
between two nodes, cut wherever another member meets it, so that the elements share their nodes.

Runs in GhPython (Rhino 7 and 8) and in Rhino 8's Python 3 component alike.
"""
from __future__ import division

import math
import Rhino.Geometry as rg


def P(x, y, z):
    return rg.Point3d(x, y, z)


def L(a, b):
    return rg.Line(a, b)


def pieces(a, b, n):
    """The line from a to b in n pieces."""
    n = max(1, int(n))
    return [L(a + (b - a) * (i / n), a + (b - a) * ((i + 1) / n)) for i in range(n)]


def chain(points):
    """The lines between one point and the next."""
    return [L(points[k], points[k + 1]) for k in range(len(points) - 1)]


def grid_mesh(xs, ys, z):
    """A mesh of quads over xs by ys at height z, its vertices where the lines of xs and ys cross."""
    mesh = rg.Mesh()
    for y in ys:
        for x in xs:
            mesh.Vertices.Add(P(x, y, z))
    cols = len(xs)
    for r in range(len(ys) - 1):
        for c in range(cols - 1):
            a = r * cols + c
            mesh.Faces.AddFace(a, a + 1, a + cols + 1, a + cols)
    return mesh


# ---------------------------------------------------------------- 1 - a steel hall roofed by trusses

def hall(Span, Bays, Spacing, Eaves, Depth, Panels):
    """Frames of a Pratt truss on two columns, Spacing apart, joined by purlins at the truss's top nodes.
    The columns rise to the top chord, the bottom chord meeting them at the eaves, so that each frame
    stands on pinned feet. The two end bays are braced: crosses in the roof, between the top chords,
    and in the long walls, with a strut along each wall at the eaves. PurlinWidths: the width of roof
    each purlin carries, a panel, or half of one at the eaves."""
    n = max(2, int(Panels) // 2 * 2)
    bays = max(1, int(Bays))
    dx = Span / n
    top = Eaves + Depth
    Columns, Chords, Webs, Purlins, PurlinWidths, Bracing, Struts, Feet = [], [], [], [], [], [], [], []
    for i in range(bays + 1):
        y = i * Spacing
        for x in (0.0, Span):
            Columns += [L(P(x, y, 0), P(x, y, Eaves)), L(P(x, y, Eaves), P(x, y, top))]
            Feet.append(P(x, y, 0))
        for k in range(n):
            x0, x1 = k * dx, (k + 1) * dx
            Chords += [L(P(x0, y, Eaves), P(x1, y, Eaves)), L(P(x0, y, top), P(x1, y, top))]
            # Pratt: the diagonals fall towards the middle, in tension under the roof's weight.
            if k < n // 2:
                Webs.append(L(P(x0, y, top), P(x1, y, Eaves)))
            else:
                Webs.append(L(P(x0, y, Eaves), P(x1, y, top)))
        Webs += [L(P(k * dx, y, Eaves), P(k * dx, y, top)) for k in range(1, n)]
    for i in range(bays):
        y0, y1 = i * Spacing, (i + 1) * Spacing
        for k in range(n + 1):
            Purlins.append(L(P(k * dx, y0, top), P(k * dx, y1, top)))
            PurlinWidths.append(dx / 2 if k in (0, n) else dx)
        for x in (0.0, Span):
            Struts.append(L(P(x, y0, Eaves), P(x, y1, Eaves)))
        if i in (0, bays - 1):
            for k in range(n):
                Bracing += [L(P(k * dx, y0, top), P((k + 1) * dx, y1, top)),
                            L(P((k + 1) * dx, y0, top), P(k * dx, y1, top))]
            for x in (0.0, Span):
                Bracing += [L(P(x, y0, 0), P(x, y1, Eaves)), L(P(x, y1, 0), P(x, y0, Eaves))]
    return Columns, Chords, Webs, Purlins, PurlinWidths, Bracing, Struts, Feet


# ---------------------------------------------------------------- 2 - a steel tower with a stair

def tower(Width, Levels, Storey):
    """Four legs Width apart, a landing at every level and a crossed bracing on every face of every
    storey. A straight flight of stairs climbs each storey along a strip a metre wide across the
    middle, turning back at each landing: two stringers, from one side of the landing below to the
    other side of the landing above. A landing is its four edge beams and two cross beams along the
    strip, its Strip, which the stringers start and end on. The legs on the windward side, x = 0, are
    WindwardLegs, for the wind."""
    levels = max(1, int(Levels))
    w = Width
    y1, y2 = 0.5 * w - 0.5, 0.5 * w + 0.5
    corners = [(0, 0), (w, 0), (w, w), (0, w)]
    Legs, WindwardLegs, Bracing, Landings, Strip, Stringers, Feet = [], [], [], [], [], [], []
    for x, y in corners:
        Feet.append(P(x, y, 0))
        legs = pieces(P(x, y, 0), P(x, y, levels * Storey), levels)
        (WindwardLegs if x == 0 else Legs).extend(legs)
    for j in range(levels):
        z0, z1 = j * Storey, (j + 1) * Storey
        for k in range(4):
            (ax, ay), (bx, by) = corners[k], corners[(k + 1) % 4]
            Bracing += [L(P(ax, ay, z0), P(bx, by, z1)), L(P(bx, by, z0), P(ax, ay, z1))]
        # The landing at the top of the storey: the sides along y cut where the strip meets them.
        Landings += [L(P(0, 0, z1), P(w, 0, z1)), L(P(0, w, z1), P(w, w, z1))]
        for x in (0.0, w):
            Landings += [L(P(x, 0, z1), P(x, y1, z1)), L(P(x, y1, z1), P(x, y2, z1)), L(P(x, y2, z1), P(x, w, z1))]
        Strip += [L(P(0, y1, z1), P(w, y1, z1)), L(P(0, y2, z1), P(w, y2, z1))]
        # The flight: up along +x in even storeys, back along -x in odd ones.
        a, b = (0.0, w) if j % 2 == 0 else (w, 0.0)
        Stringers += [L(P(a, y, z0), P(b, y, z1)) for y in (y1, y2)]
    # The ground has no landing beams: the first flight starts on its own footing.
    Feet += [P(0.0, y, 0) for y in (y1, y2)]
    return Legs, WindwardLegs, Bracing, Landings, Strip, Stringers, Feet


# ---------------------------------------------------------------- 3 - a canopy on T-shaped frames

def canopy(Frames, Spacing, Height, Cantilever, Rise, Purlins):
    """A row of frames, Spacing apart: a column built in at its foot and an arm cantilevering each way
    from its head, rising Rise to its tip. Purlins join the frames at the head and at Purlins points
    along each arm. A cross between two columns in the middle bay holds the row lengthways.
    LeftWidths and RightWidths: the width of roof, on plan, each purlin carries on the left (-x) and
    the right (+x) of the frames, nothing on the other side: a purlin's share, or half at a tip or the
    head."""
    frames = max(2, int(Frames))
    m = max(1, int(Purlins))
    s = Cantilever / m
    Columns, Arms, Purlins, LeftWidths, RightWidths, Bracing, Feet = [], [], [], [], [], [], []
    for i in range(frames):
        y = i * Spacing
        Columns.append(L(P(0, y, 0), P(0, y, Height)))
        Feet.append(P(0, y, 0))
        for side in (-1, 1):
            Arms += pieces(P(0, y, Height), P(side * Cantilever, y, Height + Rise), m)
    for i in range(frames - 1):
        y0, y1 = i * Spacing, (i + 1) * Spacing
        Purlins.append(L(P(0, y0, Height), P(0, y1, Height)))
        LeftWidths.append(s / 2)
        RightWidths.append(s / 2)
        for side in (-1, 1):
            for k in range(1, m + 1):
                Purlins.append(L(P(side * k * s, y0, Height + k * Rise / m), P(side * k * s, y1, Height + k * Rise / m)))
                width = s / 2 if k == m else s
                LeftWidths.append(width if side < 0 else 0.0)
                RightWidths.append(width if side > 0 else 0.0)
    middle = (frames - 1) // 2
    y0, y1 = middle * Spacing, (middle + 1) * Spacing
    Bracing += [L(P(0, y0, 0), P(0, y1, Height)), L(P(0, y1, 0), P(0, y0, Height))]
    return Columns, Arms, Purlins, LeftWidths, RightWidths, Bracing, Feet


# ---------------------------------------------------------------- 4 - a facade stiffened by vertical trusses

def facade(Bays, BayWidth, Height, Levels, Depth, TrussEvery):
    """A glass wall Height tall in the plane y = 0, held only at its foot and its top, as a lobby's is:
    mullions every BayWidth and transoms between them at Levels heights. Behind every TrussEvery-th
    mullion a vertical truss stiffens it against the wind: the mullion is its outer chord, a tube
    Depth behind it the inner chord, with a post at every transom and diagonals zig-zagging between.
    The mullions stand on their feet and are held across the wall at the top; the inner chords too,
    and along the wall at the top as well, which nothing else holds them in.
    MullionWidths: the width of wall each piece of mullion catches the wind over, a bay or half of one
    at the ends. PanelHeight: the height of glass each transom carries."""
    bays, levels, every = max(1, int(Bays)), max(2, int(Levels)), max(1, int(TrussEvery))
    dz = Height / levels
    Mullions, MullionWidths, Transoms, Chords, Posts, Diagonals = [], [], [], [], [], []
    Base, Tops, ChordBase, ChordTops = [], [], [], []
    for i in range(bays + 1):
        x = i * BayWidth
        Mullions += pieces(P(x, 0, 0), P(x, 0, Height), levels)
        MullionWidths += [BayWidth / 2 if i in (0, bays) else BayWidth] * levels
        Base.append(P(x, 0, 0))
        Tops.append(P(x, 0, Height))
        if i % every:
            continue
        Chords += pieces(P(x, Depth, 0), P(x, Depth, Height), levels)
        Posts += [L(P(x, 0, k * dz), P(x, Depth, k * dz)) for k in range(levels + 1)]
        for k in range(levels):
            z0, z1 = k * dz, (k + 1) * dz
            Diagonals.append(L(P(x, 0, z0), P(x, Depth, z1)) if k % 2 == 0 else L(P(x, Depth, z0), P(x, 0, z1)))
        ChordBase.append(P(x, Depth, 0))
        ChordTops.append(P(x, Depth, Height))
    for k in range(1, levels):
        Transoms += [L(P(i * BayWidth, 0, k * dz), P((i + 1) * BayWidth, 0, k * dz)) for i in range(bays)]
    PanelHeight = dz
    return Mullions, MullionWidths, Transoms, Chords, Posts, Diagonals, Base, Tops, ChordBase, ChordTops, PanelHeight


# ---------------------------------------------------------------- 5 - a barrel vault under wind

def vault(Span, Rise, Length, Strips, Divisions, Windward, Crown, Leeward):
    """A barrel vault spanning Span across x, Rise high, Length long along y: the arc of a circle
    through its two feet and its crown, cut into Strips strips along its length, each a mesh of quads
    Divisions long. A strip's faces all face the same way, so one pressure on a strip acts along one
    direction. The pressure coefficients of the windward quarter, the middle half and the leeward
    quarter, wind blowing along +x, are given to each strip by where its middle is, with the strip's
    outward Normal: a positive coefficient pushes on the vault, along -Normal, a negative one pulls it
    away. The feet are held along both long edges."""
    strips, divisions = max(4, int(Strips)), max(1, int(Divisions))
    r = (0.25 * Span * Span + Rise * Rise) / (2 * Rise)
    zc = Rise - r
    # Half the angle the arc spans, from the crown to a foot: past 90 degrees for more than a semicircle.
    half = math.atan2(0.5 * Span, r - Rise)

    def at(t, y):
        a = -half + 2 * half * t
        return P(0.5 * Span + r * math.sin(a), y, zc + r * math.cos(a))

    Strip, Names, Coefficients, Normals, Feet = [], [], [], [], []
    for s in range(strips):
        mesh = rg.Mesh()
        for k in range(divisions + 1):
            y = Length * k / divisions
            mesh.Vertices.Add(at(s / strips, y))
            mesh.Vertices.Add(at((s + 1) / strips, y))
        for k in range(divisions):
            mesh.Faces.AddFace(2 * k, 2 * k + 1, 2 * k + 3, 2 * k + 2)
        mesh.Normals.ComputeNormals()
        Strip.append(mesh)
        Names.append("Strip_{0}".format(s))
        middle = (s + 0.5) / strips
        Coefficients.append(Windward if middle < 0.25 else Leeward if middle > 0.75 else Crown)
        a = -half + 2 * half * middle
        Normals.append(rg.Vector3d(math.sin(a), 0, math.cos(a)))
    for k in range(divisions + 1):
        y = Length * k / divisions
        Feet += [at(0, y), at(1, y)]
    return Strip, Names, Coefficients, Normals, Feet


# ---------------------------------------------------------------- 6 - a facade held by wind trusses

def windtruss(Width, Height, Spacing, Depth, Trusses):
    """A tall glass facade Width wide and Height high in the plane y = 0, its mullions Spacing apart
    standing on their feet and held across the facade at the top, and at Trusses levels between by
    horizontal trusses behind it. Each truss spans the whole width between the building's columns at
    its two ends: its outer chord runs through the mullions, its inner chord Depth behind, with a
    post at every mullion and diagonals between, falling towards the middle. MullionWidths: the width
    of facade each piece of mullion catches the wind over, its spacing or half of it at the ends."""
    n = max(2, int(round(Width / Spacing)))
    s = Width / n
    trusses = max(1, int(Trusses))
    levels = [Height * (k + 1) / (trusses + 1) for k in range(trusses)]
    heights = [0.0] + levels + [Height]
    Mullions, MullionWidths, Chords, Posts, Diagonals, Base, Tops, TrussEnds = [], [], [], [], [], [], [], []
    for i in range(n + 1):
        x = i * s
        Mullions += [L(P(x, 0, heights[k]), P(x, 0, heights[k + 1])) for k in range(len(heights) - 1)]
        MullionWidths += [s / 2 if i in (0, n) else s] * (len(heights) - 1)
        Base.append(P(x, 0, 0))
        Tops.append(P(x, 0, Height))
    for z in levels:
        for i in range(n):
            x0, x1 = i * s, (i + 1) * s
            Chords += [L(P(x0, 0, z), P(x1, 0, z)), L(P(x0, Depth, z), P(x1, Depth, z))]
            if i < n // 2:
                Diagonals.append(L(P(x0, Depth, z), P(x1, 0, z)))
            else:
                Diagonals.append(L(P(x0, 0, z), P(x1, Depth, z)))
        Posts += [L(P(i * s, 0, z), P(i * s, Depth, z)) for i in range(n + 1)]
        TrussEnds += [P(0, 0, z), P(0, Depth, z), P(Width, 0, z), P(Width, Depth, z)]
    return Mullions, MullionWidths, Chords, Posts, Diagonals, Base, Tops, TrussEnds


# ---------------------------------------------------------------- 7 - a girder bridge on piers

def girder(SideSpan, MainSpan, PierHeight, Segment):
    """A continuous concrete deck over three spans - SideSpan, MainSpan, SideSpan - on two piers
    PierHeight tall. The deck is a slab 12 m wide on two girders 6 m apart, joined by a cross girder
    every other Segment; each pier is two round columns, one under each girder, braced by a strut
    halfway up and joined on top by a deeper cross girder, its cap. The deck is built into the piers
    and slides along on bearings at its two ends, held there across. Watch: the deck in the middle
    of the main span, whose movement the pictures plot."""
    H, g, w = PierHeight, 3.0, 6.0
    length = 2 * SideSpan + MainSpan
    piers = (SideSpan, SideSpan + MainSpan)
    xs = [0.0]
    for a, b in [(0.0, SideSpan), (SideSpan, SideSpan + MainSpan), (SideSpan + MainSpan, length)]:
        n = max(1, int(round((b - a) / Segment)))
        xs += [a + (b - a) * (i + 1) / n for i in range(n)]
    ys = [-w, -g, 0.0, g, w]
    Girders = []
    for y in (-g, g):
        Girders += chain([P(x, y, H) for x in xs])
    Slab = [grid_mesh(xs, ys, H)]
    CrossGirders, PierCaps = [], []
    for k, x in enumerate(xs):
        across = chain([P(x, y, H) for y in (-g, 0.0, g)])
        if any(abs(x - p) < 1e-6 for p in piers):
            PierCaps += across
        elif k % 2 == 0 or k == len(xs) - 1:
            CrossGirders += across
    Columns, Struts, Feet = [], [], []
    for x in piers:
        for y in (-g, g):
            Columns += pieces(P(x, y, 0), P(x, y, 0.5 * H), 3) + pieces(P(x, y, 0.5 * H), P(x, y, H), 3)
            Feet.append(P(x, y, 0))
        Struts += chain([P(x, y, 0.5 * H) for y in (-g, 0.0, g)])
    Bearings = [P(x, y, H) for x in (0.0, length) for y in (-g, g)]
    Watch = [P(0.5 * length, 0, H)]
    return Girders, CrossGirders, PierCaps, Columns, Struts, Slab, Feet, Bearings, Watch


# ---------------------------------------------------------------- 8 - a cable-stayed bridge

def cablestayed(Span, DeckHeight, PylonHeight, Width, Stays):
    """Two spans of Span each side of an H-shaped pylon: two legs Width apart, from the ground to
    PylonHeight above the deck, joined at the top. The deck is two edge girders joined by floor beams
    wherever a stay holds them, Stays a side in each span, the stays in two planes, fanning from the
    top quarter of each leg. The girders are built into the legs, and slide along their abutments.
    Watch: the deck in the middle of a span, and the top of the pylon."""
    n = max(2, int(Stays))
    w = 0.5 * Width
    top = DeckHeight + PylonHeight
    spacing = Span / (n + 1)
    xs = [-Span + k * spacing for k in range(2 * n + 3)]
    Girders, FloorBeams, Legs, CrossBeams, Cables, Feet, Bearings = [], [], [], [], [], [], []
    for y in (-w, w):
        Girders += [L(P(xs[k], y, DeckHeight), P(xs[k + 1], y, DeckHeight)) for k in range(len(xs) - 1)]
        Legs += pieces(P(0, y, 0), P(0, y, DeckHeight), 4)
        anchors = [DeckHeight + PylonHeight * (0.75 + 0.25 * j / (n - 1)) for j in range(n)]
        levels = [DeckHeight] + sorted(set(anchors))
        Legs += [L(P(0, y, levels[k]), P(0, y, levels[k + 1])) for k in range(len(levels) - 1)]
        Feet.append(P(0, y, 0))
        for side in (-1, 1):
            for j in range(n):
                # The outermost stay from the highest anchor, the innermost from the lowest.
                x = side * spacing * (j + 1)
                Cables.append(L(P(0, y, anchors[j]), P(x, y, DeckHeight)))
        Bearings += [P(-Span, y, DeckHeight), P(Span, y, DeckHeight)]
    FloorBeams += [L(P(x, -w, DeckHeight), P(x, w, DeckHeight)) for x in xs]
    CrossBeams += [L(P(0, -w, top), P(0, w, top))]
    Watch = [P(0.5 * Span, 0, DeckHeight), P(0, -w, top)]
    return Girders, FloorBeams, Legs, CrossBeams, Cables, Feet, Bearings, Watch


# ---------------------------------------------------------------- 9 - a deck arch bridge

def arch(Span, Rise, Columns, DeckAbove):
    """Two concrete arch ribs Span across and Rise high, parabolas springing from the ground 12 m
    apart and leaning in to 7 m apart at the crown, braced by a strut at every column. They carry a
    deck DeckAbove the crown on Columns pairs of spandrel columns, evenly spaced, built into both: a
    slab 11 m wide on two girders, joined by a floor beam on every pair of columns. The deck runs on
    to the abutments at its two ends, where it slides along, held across; the ribs are built in at
    their springings. Watch: the deck at the crown."""
    n = max(2, int(Columns))
    deck = Rise + DeckAbove
    spring, crown, g, w = 6.0, 3.5, 4.0, 5.5
    zs = lambda x: 4.0 * Rise * x * (Span - x) / (Span * Span)
    bs = lambda x: spring - (spring - crown) * zs(x) / Rise
    xs = [Span * k / (n + 1) for k in range(n + 2)]
    # The ribs and the deck, cut at every column and halfway between.
    ts = sorted(set([x for x in xs] + [0.5 * (xs[k] + xs[k + 1]) for k in range(len(xs) - 1)]))
    Ribs, Girders, SpandrelColumns = [], [], []
    for side in (-1, 1):
        Ribs += chain([P(t, side * bs(t), zs(t)) for t in ts])
        Girders += chain([P(t, side * g, deck) for t in ts])
        SpandrelColumns += [L(P(x, side * bs(x), zs(x)), P(x, side * g, deck)) for x in xs[1:-1]]
    Struts = [L(P(x, -bs(x), zs(x)), P(x, bs(x), zs(x))) for x in xs[1:-1]]
    FloorBeams = []
    for x in xs:
        FloorBeams += chain([P(x, y, deck) for y in (-g, 0.0, g)])
    Slab = [grid_mesh(ts, [-w, -g, 0.0, g, w], deck)]
    Springings = [P(x, y, 0) for x in (0.0, Span) for y in (-spring, spring)]
    Abutments = [P(x, y, deck) for x in (0.0, Span) for y in (-g, g)]
    Watch = [P(0.5 * Span, 0, deck)]
    return Ribs, Struts, Girders, FloorBeams, SpandrelColumns, Slab, Springings, Abutments, Watch


# ---------------------------------------------------------------- 10 - a building, and a tiny city of them

def _building(x0, y0, bay, height, floors, balconies=False, depth=1.5, split=1):
    """A steel frame on a grid of bay, x0, y0 its first corner: floors, from the first up, are the
    cells (i, j) each floor covers, every one inside the one under it, so that a column stands
    under every corner of a floor. Each floor has beams round its cells, cut split times a bay, and
    a slab mesh of quads on the beams' nodes; balconies: on every floor with the same floor over it,
    a slab depth deep along the south faces, on cantilevers at the columns and an edge beam."""
    Columns, Beams, Slabs, Feet = [], [], [], []
    n = max(1, int(split))
    pt = lambda i, j, k: P(x0 + i * bay, y0 + j * bay, k * height)
    corners = lambda cells: sorted(set((i + a, j + b) for (i, j) in cells for a in (0, 1) for b in (0, 1)))
    edges = lambda cells: sorted(set(e for (i, j) in cells for e in
                                     [((i, j), (i + 1, j)), ((i, j + 1), (i + 1, j + 1)), ((i, j), (i, j + 1)), ((i + 1, j), (i + 1, j + 1))]))
    Feet += [pt(i, j, 0) for (i, j) in corners(floors[0])]
    for k, cells in enumerate(floors, 1):
        Columns += [L(pt(i, j, k - 1), pt(i, j, k)) for (i, j) in corners(cells)]
        for a, b in edges(cells):
            Beams += pieces(pt(a[0], a[1], k), pt(b[0], b[1], k), n)
        mesh, at = rg.Mesh(), {}

        def v(point):
            key = (round(point.X, 6), round(point.Y, 6))
            if key not in at:
                at[key] = mesh.Vertices.Count
                mesh.Vertices.Add(point)
            return at[key]

        for (i, j) in sorted(cells):
            for a in range(n):
                for b in range(n):
                    q = [pt(i + (a + da) / n, j + (b + db) / n, k) for da, db in ((0, 0), (1, 0), (1, 1), (0, 1))]
                    mesh.Faces.AddFace(*[v(p) for p in q])
        if balconies and k < len(floors) and floors[k] == cells:
            out = rg.Vector3d(0, -depth, 0)
            south = [(i, j) for (i, j) in sorted(cells) if (i, j - 1) not in cells]
            for (i, j) in sorted(set(c for (i, j) in south for c in ((i, j), (i + 1, j)))):
                Beams.append(L(pt(i, j, k), pt(i, j, k) + out))
            for (i, j) in south:
                for a in range(n):
                    p, q = pt(i + a / n, j, k), pt(i + (a + 1) / n, j, k)
                    Beams.append(L(p + out, q + out))
                    mesh.Faces.AddFace(v(p + out), v(q + out), v(q), v(p))
        Slabs.append(mesh)
    return Columns, Beams, Slabs, Feet


def building(BaysX, BaysY, Bay, Storeys, StoreyHeight):
    """An office building: a steel frame BaysX by BaysY bays of Bay, Storeys storeys of StoreyHeight,
    columns built in at the ground, and a concrete slab at every floor on the beams, meshed twice a
    bay. Its mass is its members' and its slabs' own. Watch: a corner of the roof."""
    cells = _rect(max(1, int(BaysX)), max(1, int(BaysY)))
    storeys = max(1, int(Storeys))
    Columns, Beams, Slabs, Feet = _building(0.0, 0.0, Bay, StoreyHeight, [cells] * storeys, split=2)
    Watch = [P(0, 0, storeys * StoreyHeight)]
    return Columns, Beams, Slabs, Feet, Watch


def _rect(a, b):
    """The cells of a rectangle a bays by b."""
    return frozenset((i, j) for i in range(a) for j in range(b))


def _ell(a, b):
    """An L: the rectangle a by b without its far corner."""
    return _rect(a, b) - frozenset([(a - 1, b - 1)])


# The buildings of the city, row by row, low next to tall as a city grows: the footprint of each, in
# bays, its storeys, what tops it - a smaller footprint for its last few storeys - what it stands on
# - a wider one for its first few - and whether its south face has balconies.
CITY = [
    (_rect(3, 2), 2, None, None, False),
    (_ell(2, 2), 9, None, None, True),
    (_rect(2, 2), 4, (_rect(1, 1), 1), None, False),
    (_rect(2, 1), 16, None, None, True),
    (_ell(3, 2), 6, None, None, False),
    (_rect(2, 2), 12, None, None, True),
    (_rect(3, 1), 3, None, None, False),
    (_rect(2, 2), 20, (_rect(1, 1), 2), (_rect(3, 3), 2), False),
    (_rect(1, 2), 14, None, None, True),
    (_rect(2, 1), 5, (_rect(1, 1), 1), None, False),
    (_ell(2, 2), 10, (_rect(1, 1), 2), None, False),
    (_rect(2, 2), 2, None, None, False),
    (_rect(3, 1), 7, None, None, True),
    (_rect(2, 1), 18, None, (_rect(3, 2), 3), False),
    (_ell(2, 2), 3, None, None, False),
    (_rect(2, 2), 11, (_rect(2, 1), 2), None, True),
]


def city(Rows, ColumnsPerRow, Street, Bay, StoreyHeight):
    """A tiny city: Rows by ColumnsPerRow buildings of CITY, steel frames on a grid of Bay with a
    slab at every floor, Street between their plots of three bays square. Rectangles, Ls, towers
    on podiums and with penthouses, some with balconies: on the same ground each shakes in its own
    way, a low one stiff and quick, a tall one slow and soft, an L or a tower off its podium's
    middle twisting as it sways. Watch: a corner of each building's roof; Storeys: how many it has."""
    Columns, Beams, Slabs, Feet, Watch, Storeys = [], [], [], [], [], []
    rows, cols = max(1, int(Rows)), max(1, int(ColumnsPerRow))
    plot = 3 * Bay + Street
    for r in range(rows):
        for c in range(cols):
            cells, storeys, top, podium, balconies = CITY[(r * cols + c) % len(CITY)]
            low, high = podium[1] if podium else 0, top[1] if top else 0
            floors = ([podium[0]] * low if podium else []) + [cells] * (storeys - low - high) + \
                ([top[0]] * high if top else [])
            parts = _building(c * plot, r * plot, Bay, StoreyHeight, floors, balconies)
            Columns += parts[0]
            Beams += parts[1]
            Slabs += parts[2]
            Feet += parts[3]
            i, j = min(floors[-1])
            Watch.append(P(c * plot + i * Bay, r * plot + j * Bay, storeys * StoreyHeight))
            Storeys.append(storeys)
    return Columns, Beams, Slabs, Feet, Watch, Storeys


# ---------------------------------------------------------------- the ground motion

# El Centro, 18 May 1940, the north-south component recorded at the Imperial Valley Irrigation District
# substation: a ground acceleration every 0.02 s, in g - the version OpenSees's examples use.
EL_CENTRO_DT = 0.02
EL_CENTRO = [
    0.00630, 0.00364, 0.00099, 0.00428, 0.00758, 0.01087, 0.00682, 0.00277, -0.00128, 0.00368,
    0.00864, 0.01360, 0.00727, 0.00094, 0.00420, 0.00221, 0.00021, 0.00444, 0.00867, 0.01290,
    0.01713, -0.00343, -0.02400, -0.00992, 0.00416, 0.00528, 0.01653, 0.02779, 0.03904, 0.02449,
    0.00995, 0.00961, 0.00926, 0.00892, -0.00486, -0.01864, -0.03242, -0.03365, -0.05723, -0.04534,
    -0.03346, -0.03201, -0.03056, -0.02911, -0.02766, -0.04116, -0.05466, -0.06816, -0.08166, -0.06846,
    -0.05527, -0.04208, -0.04259, -0.04311, -0.02428, -0.00545, 0.01338, 0.03221, 0.05104, 0.06987,
    0.08870, 0.04524, 0.00179, -0.04167, -0.08513, -0.12858, -0.17204, -0.12908, -0.08613, -0.08902,
    -0.09192, -0.09482, -0.09324, -0.09166, -0.09478, -0.09789, -0.12902, -0.07652, -0.02401, 0.02849,
    0.08099, 0.13350, 0.18600, 0.23850, 0.21993, 0.20135, 0.18277, 0.16420, 0.14562, 0.16143,
    0.17725, 0.13215, 0.08705, 0.04196, -0.00314, -0.04824, -0.09334, -0.13843, -0.18353, -0.22863,
    -0.27372, -0.31882, -0.25024, -0.18166, -0.11309, -0.04451, 0.02407, 0.09265, 0.16123, 0.22981,
    0.29839, 0.23197, 0.16554, 0.09912, 0.03270, -0.03372, -0.10014, -0.16656, -0.23299, -0.29941,
    -0.00421, 0.29099, 0.22380, 0.15662, 0.08943, 0.02224, -0.04495, 0.01834, 0.08163, 0.14491,
    0.20820, 0.18973, 0.17125, 0.13759, 0.10393, 0.07027, 0.03661, 0.00295, -0.03071, -0.00561,
    0.01948, 0.04458, 0.06468, 0.08478, 0.10487, 0.05895, 0.01303, -0.03289, -0.07882, -0.03556,
    0.00771, 0.05097, 0.01013, -0.03071, -0.07156, -0.11240, -0.15324, -0.11314, -0.07304, -0.03294,
    0.00715, -0.06350, -0.13415, -0.20480, -0.12482, -0.04485, 0.03513, 0.11510, 0.19508, 0.12301,
    0.05094, -0.02113, -0.09320, -0.02663, 0.03995, 0.10653, 0.17311, 0.11283, 0.05255, -0.00772,
    0.01064, 0.02900, 0.04737, 0.06573, 0.02021, -0.02530, -0.07081, -0.04107, -0.01133, 0.00288,
    0.01709, 0.03131, -0.02278, -0.07686, -0.13095, -0.18504, -0.14347, -0.10190, -0.06034, -0.01877,
    0.02280, -0.00996, -0.04272, -0.02147, -0.00021, 0.02104, -0.01459, -0.05022, -0.08585, -0.12148,
    -0.15711, -0.19274, -0.22837, -0.18145, -0.13453, -0.08761, -0.04069, 0.00623, 0.05316, 0.10008,
    0.14700, 0.09754, 0.04808, -0.00138, 0.05141, 0.10420, 0.15699, 0.20979, 0.26258, 0.16996,
    0.07734, -0.01527, -0.10789, -0.20051, -0.06786, 0.06479, 0.01671, -0.03137, -0.07945, -0.12753,
    -0.17561, -0.22369, -0.27177, -0.15851, -0.04525, 0.06802, 0.18128, 0.14464, 0.10800, 0.07137,
    0.03473, 0.09666, 0.15860, 0.22053, 0.18296, 0.14538, 0.10780, 0.07023, 0.03265, 0.06649,
    0.10033, 0.13417, 0.10337, 0.07257, 0.04177, 0.01097, -0.01983, 0.04438, 0.10860, 0.17281,
    0.10416, 0.03551, -0.03315, -0.10180, -0.07262, -0.04344, -0.01426, 0.01492, -0.02025, -0.05543,
    -0.09060, -0.12578, -0.16095, -0.19613, -0.14784, -0.09955, -0.05127, -0.00298, -0.01952, -0.03605,
    -0.05259, -0.04182, -0.03106, -0.02903, -0.02699, 0.02515, 0.01770, 0.02213, 0.02656, 0.00419,
    -0.01819, -0.04057, -0.06294, -0.02417, 0.01460, 0.05337, 0.02428, -0.00480, -0.03389, -0.00557,
    0.02274, 0.00679, -0.00915, -0.02509, -0.04103, -0.05698, -0.01826, 0.02046, 0.00454, -0.01138,
    -0.00215, 0.00708, 0.00496, 0.00285, 0.00074, -0.00534, -0.01141, 0.00361, 0.01863, 0.03365,
    0.04867, 0.03040, 0.01213, -0.00614, -0.02441, 0.01375, 0.01099, 0.00823, 0.00547, 0.00812,
    0.01077, -0.00692, -0.02461, -0.04230, -0.05999, -0.07768, -0.09538, -0.06209, -0.02880, 0.00448,
    0.03777, 0.01773, -0.00231, -0.02235, 0.01791, 0.05816, 0.03738, 0.01660, -0.00418, -0.02496,
    -0.04574, -0.02071, 0.00432, 0.02935, 0.01526, 0.01806, 0.02086, 0.00793, -0.00501, -0.01795,
    -0.03089, -0.01841, -0.00593, 0.00655, -0.02519, -0.05693, -0.04045, -0.02398, -0.00750, 0.00897,
    0.00384, -0.00129, -0.00642, -0.01156, -0.02619, -0.04082, -0.05545, -0.04366, -0.03188, -0.06964,
    -0.05634, -0.04303, -0.02972, -0.01642, -0.00311, 0.01020, 0.02350, 0.03681, 0.05011, 0.02436,
    -0.00139, -0.02714, -0.00309, 0.02096, 0.04501, 0.06906, 0.05773, 0.04640, 0.03507, 0.03357,
    0.03207, 0.03057, 0.03250, 0.03444, 0.03637, 0.01348, -0.00942, -0.03231, -0.02997, -0.03095,
    -0.03192, -0.02588, -0.01984, -0.01379, -0.00775, -0.01449, -0.02123, 0.01523, 0.05170, 0.08816,
    0.12463, 0.16109, 0.12987, 0.09864, 0.06741, 0.03618, 0.00495, 0.00420, 0.00345, 0.00269,
    -0.05922, -0.12112, -0.18303, -0.12043, -0.05782, 0.00479, 0.06740, 0.13001, 0.08373, 0.03745,
    0.06979, 0.10213, -0.03517, -0.17247, -0.13763, -0.10278, -0.06794, -0.03310, -0.03647, -0.03984,
    -0.00517, 0.02950, 0.06417, 0.09883, 0.13350, 0.05924, -0.01503, -0.08929, -0.16355, -0.06096,
    0.04164, 0.01551, -0.01061, -0.03674, -0.06287, -0.08899, -0.05430, -0.01961, 0.01508, 0.04977,
    0.08446, 0.05023, 0.01600, -0.01823, -0.05246, -0.08669, -0.06769, -0.04870, -0.02970, -0.01071,
    0.00829, -0.00314, 0.02966, 0.06246, -0.00234, -0.06714, -0.04051, -0.01388, 0.01274, 0.00805,
    0.03024, 0.05243, 0.02351, -0.00541, -0.03432, -0.06324, -0.09215, -0.12107, -0.08450, -0.04794,
    -0.01137, 0.02520, 0.06177, 0.04028, 0.01880, 0.04456, 0.07032, 0.09608, 0.12184, 0.06350,
    0.00517, -0.05317, -0.03124, -0.00930, 0.01263, 0.03457, 0.03283, 0.03109, 0.02935, 0.04511,
    0.06087, 0.07663, 0.09239, 0.05742, 0.02245, -0.01252, 0.00680, 0.02611, 0.04543, 0.01571,
    -0.01402, -0.04374, -0.07347, -0.03990, -0.00633, 0.02724, 0.06080, 0.03669, 0.01258, -0.01153,
    -0.03564, -0.00677, 0.02210, 0.05098, 0.07985, 0.06915, 0.05845, 0.04775, 0.03706, 0.02636,
    0.05822, 0.09009, 0.12196, 0.10069, 0.07943, 0.05816, 0.03689, 0.01563, -0.00564, -0.02690,
    -0.04817, -0.06944, -0.09070, -0.11197, -0.11521, -0.11846, -0.12170, -0.12494, -0.16500, -0.20505,
    -0.15713, -0.10921, -0.06129, -0.01337, 0.03455, 0.08247, 0.07576, 0.06906, 0.06236, 0.08735,
    0.11235, 0.13734, 0.12175, 0.10616, 0.09057, 0.07498, 0.08011, 0.08524, 0.09037, 0.06208,
    0.03378, 0.00549, -0.02281, -0.05444, -0.04030, -0.02615, -0.01201, -0.02028, -0.02855, -0.06243,
    -0.03524, -0.00805, -0.04948, -0.03643, -0.02337, -0.03368, -0.01879, -0.00389, 0.01100, 0.02589,
    0.01446, 0.00303, -0.00840, 0.00463, 0.01766, 0.03069, 0.04372, 0.02165, -0.00042, -0.02249,
    -0.04456, -0.03638, -0.02819, -0.02001, -0.01182, -0.02445, -0.03707, -0.04969, -0.05882, -0.06795,
    -0.07707, -0.08620, -0.09533, -0.06276, -0.03018, 0.00239, 0.03496, 0.04399, 0.05301, 0.03176,
    0.01051, -0.01073, -0.03198, -0.05323, 0.00186, 0.05696, 0.01985, -0.01726, -0.05438, -0.01204,
    0.03031, 0.07265, 0.11499, 0.07237, 0.02975, -0.01288, 0.01212, 0.03711, 0.03517, 0.03323,
    0.01853, 0.00383, 0.00342, -0.02181, -0.04704, -0.07227, -0.09750, -0.12273, -0.08317, -0.04362,
    -0.00407, 0.03549, 0.07504, 0.11460, 0.07769, 0.04078, 0.00387, 0.00284, 0.00182, -0.05513,
    0.04732, 0.05223, 0.05715, 0.06206, 0.06698, 0.07189, 0.02705, -0.01779, -0.06263, -0.10747,
    -0.15232, -0.12591, -0.09950, -0.07309, -0.04668, -0.02027, 0.00614, 0.03255, 0.00859, -0.01537,
    -0.03932, -0.06328, -0.03322, -0.00315, 0.02691, 0.01196, -0.00300, 0.00335, 0.00970, 0.01605,
    0.02239, 0.04215, 0.06191, 0.08167, 0.03477, -0.01212, -0.01309, -0.01407, -0.05274, -0.02544,
    0.00186, 0.02916, 0.05646, 0.08376, 0.01754, -0.04869, -0.02074, 0.00722, 0.03517, -0.00528,
    -0.04572, -0.08617, -0.06960, -0.05303, -0.03646, -0.01989, -0.00332, 0.01325, 0.02982, 0.01101,
    -0.00781, -0.02662, -0.00563, 0.01536, 0.03635, 0.05734, 0.03159, 0.00584, -0.01992, -0.00201,
    0.01589, -0.01024, -0.03636, -0.06249, -0.04780, -0.03311, -0.04941, -0.06570, -0.08200, -0.04980,
    -0.01760, 0.01460, 0.04680, 0.07900, 0.04750, 0.01600, -0.01550, -0.00102, 0.01347, 0.02795,
    0.04244, 0.05692, 0.03781, 0.01870, -0.00041, -0.01952, -0.00427, 0.01098, 0.02623, 0.04148,
    0.01821, -0.00506, -0.00874, -0.03726, -0.06579, -0.02600, 0.01380, 0.05359, 0.09338, 0.05883,
    0.02429, -0.01026, -0.04480, -0.01083, -0.01869, -0.02655, -0.03441, -0.02503, -0.01564, -0.00626,
    -0.01009, -0.01392, 0.01490, 0.04372, 0.03463, 0.02098, 0.00733, -0.00632, -0.01997, 0.00767,
    0.03532, 0.03409, 0.03287, 0.03164, 0.02403, 0.01642, 0.00982, 0.00322, -0.00339, 0.02202,
    -0.01941, -0.06085, -0.10228, -0.07847, -0.05466, -0.03084, -0.00703, 0.01678, 0.01946, 0.02214,
    0.02483, 0.01809, -0.00202, -0.02213, -0.00278, 0.01656, 0.03590, 0.05525, 0.07459, 0.06203,
    0.04948, 0.03692, -0.00145, 0.04599, 0.04079, 0.03558, 0.03037, 0.03626, 0.04215, 0.04803,
    0.05392, 0.04947, 0.04502, 0.04056, 0.03611, 0.03166, 0.00614, -0.01937, -0.04489, -0.07040,
    -0.09592, -0.07745, -0.05899, -0.04052, -0.02206, -0.00359, 0.01487, 0.01005, 0.00523, 0.00041,
    -0.00441, -0.00923, -0.01189, -0.01523, -0.01856, -0.02190, -0.00983, 0.00224, 0.01431, 0.00335,
    -0.00760, -0.01856, -0.00737, 0.00383, 0.01502, 0.02622, 0.01016, -0.00590, -0.02196, -0.00121,
    0.01953, 0.04027, 0.02826, 0.01625, 0.00424, 0.00196, -0.00031, -0.00258, -0.00486, -0.00713,
    -0.00941, -0.01168, -0.01396, -0.01750, -0.02104, -0.02458, -0.02813, -0.03167, -0.03521, -0.04205,
    -0.04889, -0.03559, -0.02229, -0.00899, 0.00431, 0.01762, 0.00714, -0.00334, -0.01383, 0.01314,
    0.04011, 0.06708, 0.04820, 0.02932, 0.01043, -0.00845, -0.02733, -0.04621, -0.03155, -0.01688,
    -0.00222, 0.01244, 0.02683, 0.04121, 0.05559, 0.03253, 0.00946, -0.01360, -0.01432, -0.01504,
    -0.01576, -0.04209, -0.02685, -0.01161, 0.00363, 0.01887, 0.03411, 0.03115, 0.02819, 0.02917,
    0.03015, 0.03113, 0.00388, -0.02337, -0.05062, -0.03820, -0.02579, -0.01337, -0.00095, 0.01146,
    0.02388, 0.03629, 0.01047, -0.01535, -0.04117, -0.06699, -0.05207, -0.03715, -0.02222, -0.00730,
    0.00762, 0.02254, 0.03747, 0.04001, 0.04256, 0.04507, 0.04759, 0.05010, 0.04545, 0.04080,
    0.02876, 0.01671, 0.00467, -0.00738, -0.00116, 0.00506, 0.01128, 0.01750, -0.00211, -0.02173,
    -0.04135, -0.06096, -0.08058, -0.06995, -0.05931, -0.04868, -0.03805, -0.02557, -0.01310, -0.00063,
    0.01185, 0.02432, 0.03680, 0.04927, 0.02974, 0.01021, -0.00932, -0.02884, -0.04837, -0.06790,
    -0.04862, -0.02934, -0.01006, 0.00922, 0.02851, 0.04779, 0.02456, 0.00133, -0.02190, -0.04513,
    -0.06836, -0.04978, -0.03120, -0.01262, 0.00596, 0.02453, 0.04311, 0.06169, 0.08027, 0.09885,
    0.06452, 0.03019, -0.00414, -0.03848, -0.07281, -0.05999, -0.04717, -0.03435, -0.03231, -0.03028,
    -0.02824, -0.00396, 0.02032, 0.00313, -0.01406, -0.03124, -0.04843, -0.06562, -0.05132, -0.03702,
    -0.02272, -0.00843, 0.00587, 0.02017, 0.02698, 0.03379, 0.04061, 0.04742, 0.05423, 0.03535,
    0.01647, 0.01622, 0.01598, 0.01574, 0.00747, -0.00080, -0.00907, 0.00072, 0.01051, 0.02030,
    0.03009, 0.03989, 0.03478, 0.02967, 0.02457, 0.03075, 0.03694, 0.04313, 0.04931, 0.05550,
    0.06168, -0.00526, -0.07220, -0.06336, -0.05451, -0.04566, -0.03681, -0.03678, -0.03675, -0.03672,
    -0.01765, 0.00143, 0.02051, 0.03958, 0.05866, 0.03556, 0.01245, -0.01066, -0.03376, -0.05687,
    -0.04502, -0.03317, -0.02131, -0.00946, 0.00239, -0.00208, -0.00654, -0.01101, -0.01548, -0.01200,
    -0.00851, -0.00503, -0.00154, 0.00195, 0.00051, -0.00092, 0.01135, 0.02363, 0.03590, 0.04818,
    0.06045, 0.07273, 0.02847, -0.01579, -0.06004, -0.05069, -0.04134, -0.03199, -0.03135, -0.03071,
    -0.03007, -0.01863, -0.00719, 0.00425, 0.01570, 0.02714, 0.03858, 0.02975, 0.02092, 0.02334,
    0.02576, 0.02819, 0.03061, 0.03304, 0.01371, -0.00561, -0.02494, -0.02208, -0.01923, -0.01638,
    -0.01353, -0.01261, -0.01170, -0.00169, 0.00833, 0.01834, 0.02835, 0.03836, 0.04838, 0.03749,
    0.02660, 0.01571, 0.00482, -0.00607, -0.01696, -0.00780, 0.00136, 0.01052, 0.01968, 0.02884,
    -0.00504, -0.03893, -0.02342, -0.00791, 0.00759, 0.02310, 0.00707, -0.00895, -0.02498, -0.04100,
    -0.05703, -0.02920, -0.00137, 0.02645, 0.05428, 0.03587, 0.01746, -0.00096, -0.01937, -0.03778,
    -0.02281, -0.00784, 0.00713, 0.02210, 0.03707, 0.05204, 0.06701, 0.08198, 0.03085, -0.02027,
    -0.07140, -0.12253, -0.08644, -0.05035, -0.01426, 0.02183, 0.05792, 0.09400, 0.13009, 0.03611,
    -0.05787, -0.04802, -0.03817, -0.02832, -0.01846, -0.00861, -0.03652, -0.06444, -0.06169, -0.05894,
    -0.05618, -0.06073, -0.06528, -0.04628, -0.02728, -0.00829, 0.01071, 0.02970, 0.03138, 0.03306,
    0.03474, 0.03642, 0.04574, 0.05506, 0.06439, 0.07371, 0.08303, 0.03605, -0.01092, -0.05790,
    -0.04696, -0.03602, -0.02508, -0.01414, -0.03561, -0.05708, -0.07855, -0.06304, -0.04753, -0.03203,
    -0.01652, -0.00102, 0.00922, 0.01946, 0.02970, 0.03993, 0.05017, 0.06041, 0.07065, 0.08089,
    -0.00192, -0.08473, -0.07032, -0.05590, -0.04148, -0.05296, -0.06443, -0.07590, -0.08738, -0.09885,
    -0.06798, -0.03710, -0.00623, 0.02465, 0.05553, 0.08640, 0.11728, 0.14815, 0.08715, 0.02615,
    -0.03485, -0.09584, -0.07100, -0.04616, -0.02132, 0.00353, 0.02837, 0.05321, -0.00469, -0.06258,
    -0.12048, -0.09960, -0.07872, -0.05784, -0.03696, -0.01608, 0.00480, 0.02568, 0.04656, 0.06744,
    0.08832, 0.10920, 0.13008, 0.10995, 0.08982, 0.06969, 0.04955, 0.04006, 0.03056, 0.02107,
    0.01158, 0.00780, 0.00402, 0.00024, -0.00354, -0.00732, -0.01110, -0.00780, -0.00450, -0.00120,
    0.00210, 0.00540, -0.00831, -0.02203, -0.03575, -0.04947, -0.06319, -0.05046, -0.03773, -0.02500,
    -0.01227, 0.00046, 0.00482, 0.00919, 0.01355, 0.01791, 0.02228, 0.00883, -0.00462, -0.01807,
    -0.03152, -0.02276, -0.01401, -0.00526, 0.00350, 0.01225, 0.02101, 0.01437, 0.00773, 0.00110,
    0.00823, 0.01537, 0.02251, 0.01713, 0.01175, 0.00637, 0.01376, 0.02114, 0.02852, 0.03591,
    0.04329, 0.03458, 0.02587, 0.01715, 0.00844, -0.00027, -0.00898, -0.00126, 0.00645, 0.01417,
    0.02039, 0.02661, 0.03283, 0.03905, 0.04527, 0.03639, 0.02750, 0.01862, 0.00974, 0.00086,
    -0.01333, -0.02752, -0.04171, -0.02812, -0.01453, -0.00094, 0.01264, 0.02623, 0.01690, 0.00756,
    -0.00177, -0.01111, -0.02044, -0.02977, -0.03911, -0.02442, -0.00973, 0.00496, 0.01965, 0.03434,
    0.02054, 0.00674, -0.00706, -0.02086, -0.03466, -0.02663, -0.01860, -0.01057, -0.00254, -0.00063,
    0.00128, 0.00319, 0.00510, 0.00999, 0.01488, 0.00791, 0.00093, -0.00605, 0.00342, 0.01288,
    0.02235, 0.03181, 0.04128, 0.02707, 0.01287, -0.00134, -0.01554, -0.02975, -0.04395, -0.03612,
    -0.02828, -0.02044, -0.01260, -0.00476, 0.00307, 0.01091, 0.00984, 0.00876, 0.00768, 0.00661,
    0.01234, 0.01807, 0.02380, 0.02953, 0.03526, 0.02784, 0.02042, 0.01300, -0.03415, -0.00628,
    -0.00621, -0.00615, -0.00609, -0.00602, -0.00596, -0.00590, -0.00583, -0.00577, -0.00571, -0.00564,
    -0.00558, -0.00552, -0.00545, -0.00539, -0.00532, -0.00526, -0.00520, -0.00513, -0.00507, -0.00501,
    -0.00494, -0.00488, -0.00482, -0.00475, -0.00469, -0.00463, -0.00456, -0.00450, -0.00444, -0.00437,
    -0.00431, -0.00425, -0.00418, -0.00412, -0.00406, -0.00399, -0.00393, -0.00387, -0.00380, -0.00374,
    -0.00368, -0.00361, -0.00355, -0.00349, -0.00342, -0.00336, -0.00330, -0.00323, -0.00317, -0.00311,
    -0.00304, -0.00298, -0.00292, -0.00285, -0.00279, -0.00273, -0.00266, -0.00260, -0.00254, -0.00247,
    -0.00241, -0.00235, -0.00228, -0.00222, -0.00216, -0.00209, -0.00203, -0.00197, -0.00190, -0.00184,
    -0.00178, -0.00171, -0.00165, -0.00158, -0.00152, -0.00146, -0.00139, -0.00133, -0.00127, -0.00120,
    -0.00114, -0.00108, -0.00101, -0.00095, -0.00089, -0.00082, -0.00076, -0.00070, -0.00063, -0.00057,
    -0.00051, -0.00044, -0.00038, -0.00032, -0.00025, -0.00019, -0.00013, -0.00006, 0.00000,
]


def elcentro(Duration, Scale):
    """The El Centro record's first Duration seconds, its accelerations times Scale, in m/s2: the
    Times and the Accelerations a Path Time Series takes, and how many steps of 0.02 s they make.
    Its peak, 0.32 g, comes 2.02 s in."""
    n = min(len(EL_CENTRO), int(round(Duration / EL_CENTRO_DT)) + 1)
    Times = [k * EL_CENTRO_DT for k in range(n)]
    Accelerations = [9.81 * Scale * a for a in EL_CENTRO[:n]]
    Steps = n - 1
    return Times, Accelerations, Steps
