#! python3
# The geometry of the silo (Silo.py): a feed silo of glass fibre on four steel legs, as farms have, and
# the two sudden loads it meets - a dust explosion inside it and a tractor reversing into a leg.
#
# The silo is a surface of revolution about the Z axis: a hopper, a cone Hopper angle steep, from the
# outlet, Outlet height above the ground, up to the cylinder; the cylinder, Radius and Cylinder height;
# and a roof, a cone of 30 degrees up to the manhole at its top, left open: its lid is what blows open in
# an explosion, and lets the pressure out. It is one mesh of quads, a ring of Around faces a row; the
# rows are about Element size tall, and shorter down the hopper and up the roof, where the rings get
# small, so that no face is much longer than it is wide.
#
# Four steel legs stand round it, on the X and Y axes, a little out from the wall, from the ground to
# the top of the cylinder. The wall's joints at the bottom and the top of the cylinder have a steel ring
# round them; a bracket holds each leg to each ring, three short bars fanning out to three of its nodes,
# as a plate bolted on would. A ring of ties joins the legs low down, under the hopper.
#
# The feed inside is mass, not load: Filled per cent of the bin, from the outlet up, at Density kg/m3.
# Each ring of nodes takes the feed of the slice of the bin around it, shared between its nodes.
#
# The definition's GhPython components run silo(), explosion() and impact(). Everything before them is
# plain Python, Rhino or not, so that it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None

# The roof: a cone of 30 degrees, up to a manhole of this radius, open.
ROOF_SLOPE = 30.0
MANHOLE = 0.3
# The outlet at the hopper's bottom, its radius.
OUTLET = 0.15
# The legs: how far out from the wall, and the longest piece of leg between two nodes.
LEG_OUT = 0.15
LEG_PIECE = 0.5
# The ring of ties joining the legs, this far below the outlet.
TIES_BELOW = 0.1
# A face no more than this many times as tall as it is wide.
ASPECT = 2.0


def _march(r_from, r_to, angle, size, around):
    """The radii of the rings down a cone from r_from to r_to, angle from the horizontal, each step
    along the slope about size, or less where the ring is small: at most ASPECT times the width of a
    face there. Stretched to land on r_to exactly."""
    cos = math.cos(math.radians(angle))
    sign = 1.0 if r_to > r_from else -1.0
    radii = [r_from]
    while (r_to - radii[-1]) * sign > 1e-9:
        width = 2.0 * math.pi * max(radii[-1], 1e-3) / around
        radii.append(radii[-1] + sign * min(size, ASPECT * width) * cos)
    # The last ring overshoots: stretch the steps so that it lands on r_to.
    if len(radii) > 2 and abs(radii[-1] - r_to) > 1e-9:
        share = (r_to - r_from) / (radii[-1] - r_from)
        radii = [r_from + (r - r_from) * share for r in radii]
    radii[-1] = r_to
    return radii


def profile(radius, cylinder, hopper_angle, outlet_height, around, size):
    """The rings from the outlet up to the manhole, as (r, z), and the places in that list of the
    bottom and the top of the cylinder."""
    tan_h = math.tan(math.radians(hopper_angle))
    tan_r = math.tan(math.radians(ROOF_SLOPE))
    # The hopper, from the cylinder down to the outlet: the rings get smaller going down.
    hopper = _march(radius, OUTLET, hopper_angle, size, around)
    rings = [(r, outlet_height + (r - OUTLET) * tan_h) for r in reversed(hopper)]
    bottom = len(rings) - 1
    z_bottom = rings[-1][1]
    rows = max(1, int(math.ceil(cylinder / size - 1e-9)))
    for k in range(1, rows + 1):
        rings.append((radius, z_bottom + cylinder * k / rows))
    top = len(rings) - 1
    z_top = rings[-1][1]
    for r in _march(radius, MANHOLE, ROOF_SLOPE, size, around)[1:]:
        rings.append((r, z_top + (radius - r) * tan_r))
    return rings, bottom, top


def _radius(rings, z):
    """The bin's radius at height z, from the rings' (r, z), straight between them."""
    for (r0, z0), (r1, z1) in zip(rings[:-1], rings[1:]):
        if z0 - 1e-9 <= z <= z1 + 1e-9:
            t = 0.0 if z1 == z0 else (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * t
    return rings[-1][0]


def feed_volume(rings, z_lo, z_hi, slices=40):
    """The volume of the bin between z_lo and z_hi, by the midpoint rule on thin slices."""
    if z_hi <= z_lo:
        return 0.0
    dz = (z_hi - z_lo) / slices
    return sum(math.pi * _radius(rings, z_lo + (k + 0.5) * dz) ** 2 * dz for k in range(slices))


def model(radius, cylinder, hopper_angle, outlet_height, around, size, filled, density, impact_height):
    """The silo as plain tuples: the wall's vertices and faces, each face's outward normal, the legs,
    brackets, steel rings and ties as pairs of points, the feet, the feed's masses at the wall's nodes, and the
    point the tractor hits."""
    around = max(8, 4 * int(round(around / 4.0)))
    rings, bottom, top = profile(radius, cylinder, hopper_angle, outlet_height, around, size)

    def at(k, i):
        r, z = rings[k]
        a = 2.0 * math.pi * i / around
        return (r * math.cos(a), r * math.sin(a), z)

    m = {"Vertices": [], "Faces": [], "Normals": [], "Legs": [], "Brackets": [], "Rings": [], "Ties": [], "Feet": [],
         "MassPoints": [], "Masses": []}
    for k in range(len(rings)):
        for i in range(around):
            m["Vertices"].append(at(k, i))
    for k in range(len(rings) - 1):
        for i in range(around):
            j = (i + 1) % around
            face = (k * around + i, k * around + j, (k + 1) * around + j, (k + 1) * around + i)
            m["Faces"].append(face)
            p = [m["Vertices"][v] for v in face]
            # The normal, from the diagonals, turned to face out: away from the axis, as every part
            # of this wall does - the hopper's down and out, the roof's up and out.
            d1 = [p[2][c] - p[0][c] for c in range(3)]
            d2 = [p[3][c] - p[1][c] for c in range(3)]
            n = (d1[1] * d2[2] - d1[2] * d2[1], d1[2] * d2[0] - d1[0] * d2[2], d1[0] * d2[1] - d1[1] * d2[0])
            length = math.sqrt(sum(c * c for c in n))
            n = tuple(c / length for c in n)
            centre = [sum(q[c] for q in p) / 4.0 for c in range(3)]
            if n[0] * centre[0] + n[1] * centre[1] < 0:
                n = tuple(-c for c in n)
            m["Normals"].append(n)

    # The legs, on the X and Y axes, LEG_OUT out from the wall, from the ground to the top of the
    # cylinder: a node where a bracket holds it, where the ties join it and where the tractor hits it,
    # and others between, no piece longer than LEG_PIECE.
    z_bottom, z_top = rings[bottom][1], rings[top][1]
    z_ties = outlet_height - TIES_BELOW
    stops = sorted(set([0.0, z_ties, impact_height, z_bottom, z_top]))
    stops = [z for z in stops if 0.0 <= z <= z_top]
    levels = [0.0]
    for z0, z1 in zip(stops[:-1], stops[1:]):
        pieces = max(1, int(math.ceil((z1 - z0) / LEG_PIECE - 1e-9)))
        levels += [z0 + (z1 - z0) * k / pieces for k in range(1, pieces + 1)]
    # A steel ring round the bottom and the top of the cylinder, where the joints of the wall are: the
    # brackets bolt to them, and they take the pull of the hopper and the roof inwards.
    for k in (bottom, top):
        for i in range(around):
            m["Rings"].append((at(k, i), at(k, (i + 1) % around)))
    r_leg = radius + LEG_OUT
    for q in range(4):
        i = q * around // 4
        a = 2.0 * math.pi * i / around
        c, s = math.cos(a), math.sin(a)

        def leg(z):
            return (r_leg * c, r_leg * s, z)

        m["Feet"].append(leg(0.0))
        for z0, z1 in zip(levels[:-1], levels[1:]):
            m["Legs"].append((leg(z0), leg(z1)))
        for k in (bottom, top):
            for di in (-1, 0, 1):
                m["Brackets"].append((leg(rings[k][1]), at(k, (i + di) % around)))
        # The tie to the next leg round.
        a2 = 2.0 * math.pi * ((q + 1) % 4) * (around // 4) / around
        m["Ties"].append((leg(z_ties), (r_leg * math.cos(a2), r_leg * math.sin(a2), z_ties)))
    # The tractor comes from +X and hits the leg there.
    impact = (r_leg, 0.0, impact_height)

    # The feed: up to Filled per cent of the bin's height, from the outlet to the top of the cylinder.
    # Each ring of nodes takes the slice of the bin halfway to the rings either side, shared between
    # its nodes, in all three directions: the feed moves with the wall.
    z_out = rings[0][1]
    z_fill = z_out + max(0.0, min(100.0, filled)) / 100.0 * (z_top - z_out)
    feed = 0.0
    for k in range(len(rings)):
        lo = rings[0][1] if k == 0 else (rings[k - 1][1] + rings[k][1]) / 2.0
        hi = rings[-1][1] if k == len(rings) - 1 else (rings[k][1] + rings[k + 1][1]) / 2.0
        mass = density * feed_volume(rings, lo, min(hi, z_fill))
        if mass <= 0.0:
            continue
        feed += mass
        for i in range(around):
            m["MassPoints"].append(at(k, i))
            m["Masses"].append((mass / around,) * 3)
    m["Impact"] = impact
    m["Feed"] = feed
    m["Around"] = around
    m["Profile"] = rings
    m["Fill"] = z_fill
    m["Bin"] = feed_volume(rings, z_out, z_top)
    return m


def explosion(Rise, Duration):
    """The dust explosion's pressure, as a share of its peak: from nothing to the peak in Rise ms, and
    back to nothing at Duration ms, as the vent lets it out. Times in s, for a Path Time Series."""
    rise = max(0.1, Rise) / 1000.0
    end = max(rise + 0.001, Duration / 1000.0)
    return [0.0, rise, end], [0.0, 1.0, 0.0]


def impact(Mass, Speed, Stiffness):
    """The tractor reversing into a leg, as a hard impact: its Mass in t at Speed in km/h, against a
    contact that gives like a spring of Stiffness kN/m. The force rises and falls as half a sine, its
    peak v sqrt(k m) in kN, over pi sqrt(m / k) in s. Times in s and the force as a share of its peak,
    for a Path Time Series."""
    m, k, v = max(0.01, Mass), max(1.0, Stiffness), max(0.0, Speed) / 3.6
    force = v * math.sqrt(k * m)
    contact = math.pi * math.sqrt(m / k)
    samples = 20
    times = [contact * j / samples for j in range(samples + 1)]
    values = [math.sin(math.pi * j / samples) for j in range(samples + 1)]
    return force, contact, times, values


def silo(Radius, CylinderHeight, HopperAngle, OutletHeight, Around, Size, Filled, Density, ImpactHeight):
    """The GhPython component's code: the wall as one mesh and each face's outward normal, in the
    mesh's face order; the legs, brackets, steel rings and ties as lines; the feet; the feed as Mass Points; the
    point the tractor hits; and a few lines about the silo."""
    m = model(Radius, CylinderHeight, HopperAngle, OutletHeight, Around, Size, Filled, Density, ImpactHeight)
    mesh = rg.Mesh()
    for v in m["Vertices"]:
        mesh.Vertices.Add(*v)
    for f in m["Faces"]:
        mesh.Faces.AddFace(*f)
    mesh.Normals.ComputeNormals()

    def lines(key):
        return [rg.Line(rg.Point3d(*a), rg.Point3d(*b)) for a, b in m[key]]

    info = ("{0:.1f} m across, {1:.2f} m tall; the bin holds {2:.1f} m3 to the top of the cylinder\n"
            "feed {3:.2f} t, up to {4:.2f} m above the ground\n"
            "{5} shells, {6} leg pieces, {7} bracket bars, {8} ring pieces, {9} ties").format(
        2 * Radius, m["Profile"][-1][1], m["Bin"], m["Feed"] / 1000.0, m["Fill"], len(m["Faces"]), len(m["Legs"]),
        len(m["Brackets"]), len(m["Rings"]), len(m["Ties"]))
    return ([mesh], [rg.Vector3d(*n) for n in m["Normals"]], lines("Legs"), lines("Brackets"), lines("Rings"),
            lines("Ties"),
            [rg.Point3d(*p) for p in m["Feet"]], [rg.Point3d(*p) for p in m["MassPoints"]],
            [rg.Vector3d(*w) for w in m["Masses"]], rg.Point3d(*m["Impact"]), info)


if __name__ == "__main__":
    m = model(1.1, 2.4, 60.0, 1.0, 32, 0.3, 75.0, 650.0, 1.25)
    print("rings", len(m["Profile"]), [("%.3f" % r, "%.3f" % z) for r, z in m["Profile"]])
    print("faces", len(m["Faces"]), "legs", len(m["Legs"]), "brackets", len(m["Brackets"]), "steel rings",
          len(m["Rings"]), "ties", len(m["Ties"]))
    print("feed t", m["Feed"] / 1000.0, "fill z", m["Fill"], "bin m3", m["Bin"])
    print("explosion", explosion(50, 150))
    f, c, t, v = impact(5, 4, 300)
    print("impact kN", f, "s", c)
