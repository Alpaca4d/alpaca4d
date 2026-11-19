#! python3
# The geometry of the wave roof hall (WaveRoofHall.py): a timber roof on a steel frame, 50 m clear span.
#
# Glulam arches span across the hall, x from 0 to Span, one every Bay along y: the two at the ends apart
# from the rest, as they carry the wind on the gables. Each is a parabola
# springing from the heads of two steel columns, Height above the ground, and rising to its crown by
# Rise - more or less, as a sine wave of Wave along the hall goes, so that the roof rolls in waves from
# one end to the other. A steel tie rod joins each arch's two springings and takes its thrust. Glulam
# purlins run along the hall from arch to arch, every PurlinEvery-th node of the arches, and steel eave
# beams along the column heads.
#
# Steel holds it all up and steady: the columns built in at their feet across the hall, and braced along
# it by crosses of steel bars in the walls of the two end bays; in the roof, crosses of steel bars in
# those bays carry the wind on the gables to the walls.
#
# Loads reach the frame as typed per square metre: the roof's on the purlins and the eave beams, each
# over the width of plan it carries; the wind on the long walls on the columns, each over the width of
# wall it catches; the wind on the gables on the end arches, each piece over half the height of gable
# under it.
#
# The definition's GhPython component runs hall(). Everything before it is plain Python, Rhino or not,
# so that it can be tried outside Rhino too: python3 WaveRoofHallGeometry.py.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None


def frame(span, length, bay, height, rise, wave, wavelength, segments, purlin_every):
    """The hall as plain tuples, every list in its own order: the members by kind, the widths each
    loaded one carries, the feet, and the lengths the checks need."""
    arches = max(2, int(round(length / bay)) + 1)
    segments = max(2, int(segments))
    every = max(1, int(purlin_every))
    segments += segments % every and every - segments % every   # a whole number of purlin steps

    def crown(j):
        return rise + wave * math.sin(2.0 * math.pi * j * bay / wavelength)

    def node(j, i):
        x = span * i / segments
        return (x, j * bay, height + crown(j) * 4.0 * x * (span - x) / (span * span))

    def gap(a, b):
        return math.sqrt(sum((a[k] - b[k]) ** 2 for k in range(3)))

    def bay_share(j):
        return bay / (2.0 if j in (0, arches - 1) else 1.0)

    lines = ("Arches", "Purlins", "Eaves", "WindwardColumns", "LeewardColumns", "Ties", "WallBraces", "RoofBraces",
             "WindwardGable", "LeewardGable")
    m = dict((k, []) for k in lines)
    m.update({"PurlinWidths": [], "EaveWidths": [], "ColumnWidths": [], "WindwardHeights": [], "LeewardHeights": [],
              "Feet": []})

    plan = span / segments * every                 # the plan width a purlin carries
    stops = list(range(0, segments + 1, every))     # the nodes purlins and eave beams sit on, across the hall

    for j in range(arches):
        pieces = [(node(j, i), node(j, i + 1)) for i in range(segments)]
        # The end arches apart, as they carry the wind on the gables: half the gable under each piece,
        # from the ground to the piece's middle.
        heights = [0.5 * (a[2] + b[2]) / 2.0 for a, b in pieces]
        if j == 0:
            m["WindwardGable"] += pieces
            m["WindwardHeights"] += heights
        elif j == arches - 1:
            m["LeewardGable"] += pieces
            m["LeewardHeights"] += heights
        else:
            m["Arches"] += pieces
        for x, kind in ((0.0, "WindwardColumns"), (span, "LeewardColumns")):
            m[kind].append(((x, j * bay, 0.0), (x, j * bay, height)))
            m["Feet"].append((x, j * bay, 0.0))
        m["ColumnWidths"].append(bay_share(j))
        m["Ties"].append(((0.0, j * bay, height), (span, j * bay, height)))

    for j in range(arches - 1):
        for i in stops:
            a, b = node(j, i), node(j + 1, i)
            if i in (0, segments):
                m["Eaves"].append((a, b))
                m["EaveWidths"].append(plan / 2.0)
            else:
                m["Purlins"].append((a, b))
                m["PurlinWidths"].append(plan)

    for j in (0, arches - 2):
        # The walls of the end bays: a cross in each, foot of one column to head of the next.
        for x in (0.0, span):
            m["WallBraces"] += [((x, j * bay, 0.0), (x, (j + 1) * bay, height)),
                                ((x, (j + 1) * bay, 0.0), (x, j * bay, height))]
        # The roof of the end bays: a cross in every panel between purlins.
        for k in range(len(stops) - 1):
            i0, i1 = stops[k], stops[k + 1]
            m["RoofBraces"] += [(node(j, i0), node(j + 1, i1)), (node(j + 1, i0), node(j, i1))]

    m["Lengths"] = dict((k, [gap(a, b) for a, b in m[k]]) for k in lines)
    longest = max(sum(gap(node(j, i), node(j, i + 1)) for i in range(segments)) for j in range(arches))
    purlin_gap = max(sum(gap(node(j, i), node(j, i + 1)) for i in range(i0, i0 + every))
                     for j in range(arches) for i0 in range(0, segments, every))
    m["ArchLength"] = longest
    m["PurlinSpacing"] = purlin_gap
    m["Crowns"] = [crown(j) for j in range(arches)]
    m["Counts"] = (arches, segments)
    return m


# The members in the order the definition hands them to Assemble Model, and so the order of the model's beams:
# what the Section Optimiser's per-beam inputs follow.
ORDER = ("Arches", "WindwardGable", "LeewardGable", "Purlins", "Eaves", "WindwardColumns", "LeewardColumns", "Ties",
         "WallBraces", "RoofBraces")


def per_beam(m, height, in_plane):
    """The buckling lengths about local y and z, the spacing of lateral restraints and where the load acts, one
    per beam in the model's order: the arches held sideways at every purlin and buckling in their plane over
    in_plane, loaded on top; the purlins and eaves over their own span, the purlins loaded on top; the columns
    a cantilever across the hall, 2 x height about their strong axis, and held at the eaves along it; the bars
    their own length, which a bar in tension never buckles over."""
    ys, zs, lts, loads = [], [], [], []
    spacing = m["PurlinSpacing"]
    for kind in ORDER:
        for length in m["Lengths"][kind]:
            if kind in ("Arches", "WindwardGable", "LeewardGable"):
                y, z, lt, load = spacing, in_plane, spacing, "Top"
            elif kind in ("WindwardColumns", "LeewardColumns"):
                y, z, lt, load = height, 2.0 * height, height, "ShearCentre"
            else:
                y, z, lt, load = length, length, length, "Top" if kind == "Purlins" else "ShearCentre"
            ys.append(y)
            zs.append(z)
            lts.append(lt)
            loads.append(load)
    return ys, zs, lts, loads


# The colours of the utilisation plot, a band each of 0.2 up to 1, and red past it; grey where a member could
# not be checked.
BANDS = [(0.2, (66, 136, 247)), (0.4, (91, 192, 235)), (0.6, (175, 201, 48)), (0.8, (242, 193, 78)),
         (1.0, (235, 108, 63)), (float("inf"), (215, 38, 61))]
UNCHECKED = (190, 194, 199)


def band(u):
    if u is None:
        return UNCHECKED
    for top, colour in BANDS:
        if u <= top:
            return colour
    return BANDS[-1][1]


def section_size(section):
    """How deep and how wide to draw a member: its section's own, and no thinner than 8 cm, so a bar shows."""
    h = w = 0.2
    for depth, width in (("Height", "Width"), ("Height", "TopWidth"), ("Diameter", "Diameter")):
        try:
            h, w = float(getattr(section, depth)), float(getattr(section, width))
            break
        except Exception:
            pass
    return max(h, 0.08), max(w, 0.08)


def plot(Elements, Utilisation, Shift):
    """The GhPython component of a utilisation plot: every member a box as deep and wide as its section, coloured
    by its utilisation in bands of 0.2, red past 1, moved by Shift; and the Legend's values and colours."""
    import System.Drawing as sd
    shift = Shift if Shift is not None else rg.Vector3d.Zero
    mesh = rg.Mesh()
    for element, u in zip(Elements, Utilisation):
        a = element.Curve.PointAtStart + shift
        b = element.Curve.PointAtEnd + shift
        x = b - a
        if not x.Unitize():
            continue
        side = rg.Vector3d.CrossProduct(rg.Vector3d.ZAxis, x) if abs(x.Z) < 0.9 else rg.Vector3d.YAxis
        side.Unitize()
        up = rg.Vector3d.CrossProduct(x, side)
        h, w = section_size(element.Section)
        r, g, bl = band(None if u is None else float(u))
        colour = sd.Color.FromArgb(r, g, bl)
        start = mesh.Vertices.Count
        for end in (a, b):
            for sy, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                mesh.Vertices.Add(end + side * (sy * w / 2.0) + up * (sz * h / 2.0))
                mesh.VertexColors.Add(colour)
        for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            mesh.Faces.AddFace(start + f[0], start + f[1], start + f[2], start + f[3])
    mesh.Normals.ComputeNormals()
    values = [top if top != float("inf") else 1.2 for top, colour in BANDS]
    colours = [sd.Color.FromArgb(*colour) for top, colour in BANDS]
    return mesh, values, colours


def hall(Span, Length, Bay, Height, Rise, Wave, Wavelength, Segments, PurlinEvery):
    """The GhPython component's code: the members as Rhino lines, the widths each loaded one carries,
    the feet as points, the lengths the checks need, and a line about the hall."""
    m = frame(Span, Length, Bay, Height, Rise, Wave, Wavelength, Segments, PurlinEvery)

    def lines(key):
        return [rg.Line(rg.Point3d(*a), rg.Point3d(*b)) for a, b in m[key]]

    arches, segments = m["Counts"]
    # In-plane buckling of an arch: about 0.6 of its length, between a two-hinged arch's 0.6 and a
    # fixed one's 0.35 for the antisymmetric mode, on the safe side for arches held by cantilever columns.
    in_plane = 0.6 * m["ArchLength"]
    members = sum(len(m[k]) for k in ("Arches", "WindwardGable", "LeewardGable", "Purlins", "Eaves", "WindwardColumns",
                                      "LeewardColumns", "Ties", "WallBraces", "RoofBraces"))
    info = ("{0:g} m clear span, {1:g} m long, {2} arches {3:g} m apart\n"
            "crowns {4:.1f} to {5:.1f} m above the column heads, {6:.1f} to {7:.1f} m above the ground\n"
            "arches {8:.1f} m long at most, purlins every {9:.2f} m along them\n"
            "{10} members").format(Span, (arches - 1) * Bay, arches, Bay, min(m["Crowns"]), max(m["Crowns"]),
                                   Height + min(m["Crowns"]), Height + max(m["Crowns"]), m["ArchLength"],
                                   m["PurlinSpacing"], members)
    ys, zs, lts, loads = per_beam(m, Height, in_plane)
    return (lines("Arches"), lines("Purlins"), m["PurlinWidths"], lines("Eaves"), m["EaveWidths"],
            lines("WindwardColumns"), lines("LeewardColumns"), m["ColumnWidths"], lines("Ties"),
            lines("WallBraces"), lines("RoofBraces"), lines("WindwardGable"), m["WindwardHeights"], lines("LeewardGable"),
            m["LeewardHeights"], [rg.Point3d(*p) for p in m["Feet"]], m["PurlinSpacing"], in_plane, ys, zs, lts, loads, info)


if __name__ == "__main__":
    m = frame(50.0, 60.0, 5.0, 6.0, 7.5, 2.5, 30.0, 20, 2)
    for k in sorted(m):
        v = m[k]
        print(k, len(v) if isinstance(v, list) else v)
    print("crowns", ["{0:.2f}".format(c) for c in m["Crowns"]])
    print("plan widths: purlins", sum(m["PurlinWidths"]), "eaves", sum(m["EaveWidths"]), "of", 50 * 60)
    ys, zs, lts, loads = per_beam(m, 6.0, 0.6 * m["ArchLength"])
    print("per beam:", len(ys), "first arch", ys[0], zs[0], loads[0], "first column", ys[400], zs[400], loads[400])
