#! python3
# The geometry of the reciprocal vault (ReciprocalVault.py): plywood planks laid in a pinwheel pattern over a
# barrel vault, each resting on its neighbours and bolted where it crosses them. The pattern is drawn flat, the
# vault unrolled, then wrapped round the vault; the planks are then moved until every crossing is one plank
# thick, as they are once they are built - the plank on top resting on the one under it, their axes a
# thickness apart, square to both.
#
# The definition's GhPython components run vault() and capacity_curve(). Everything before them is plain
# Python, Rhino or not, so that it can be tried outside Rhino too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

from __future__ import division

import math

try:
    import Rhino.Geometry as rg
except ImportError:
    rg = None


# ---------------------------------------------------------------- vectors, as tuples

def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _mul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _length(a):
    return math.sqrt(_dot(a, a))


def _unit(a):
    n = _length(a)
    return _mul(a, 1.0 / n) if n > 0.0 else a


def _at(p, q, u):
    return _add(p, _mul(_sub(q, p), u))


# ---------------------------------------------------------------- the pattern, flat

def _meet(p, q, r, s):
    """Where segment p-q crosses segment r-s, flat: their parameters (u along p-q, v along r-s), or None."""
    dx, dy = q[0] - p[0], q[1] - p[1]
    ex, ey = s[0] - r[0], s[1] - r[1]
    den = dx * ey - dy * ex
    if abs(den) < 1e-12:
        return None
    fx, fy = r[0] - p[0], r[1] - p[1]
    u = (fx * ey - fy * ex) / den
    v = (fx * dy - fy * dx) / den
    if 0.0 <= u <= 1.0 and 0.0 <= v <= 1.0:
        return u, v
    return None


class Vault(object):
    """The vault, solved: its planks, their crossings, and what the definition needs of them.

    The pattern is a pinwheel on a grid of cells, the vault unrolled - x along it, s round its arch from one
    foot. Round every node of the grid four planks turn about a small square opening, each resting on the
    next: one along the vault leaving the node, one round the arch leaving it, and the two arriving. A plank
    runs from one node's pinwheel to the next, so it crosses two planks at each end - its own end resting on
    one, the end of the other resting on it - and spans free between. The planks round the arch that start or
    end at a foot stand on the ground; those at the two open ends of the vault cross only the planks along it.
    """

    def __init__(self, length, span, rise, cell, fan, overhang, thickness):
        if rise <= 0.0 or span <= 0.0 or length <= 0.0 or cell <= 0.0:
            raise ValueError("Length, Span, Rise and Cell must all be more than 0.")
        self.length, self.span, self.rise, self.thickness = length, span, rise, thickness
        # The arch: a circle through both feet and the crown.
        self.radius = (span * span / 4.0 + rise * rise) / (2.0 * rise)
        half = math.asin(min(1.0, span / 2.0 / self.radius))
        if rise > self.radius:
            half = math.pi - half
        self.half = half
        self.arc = 2.0 * half * self.radius
        nx = max(1, int(round(length / cell)))
        ny = max(2, int(round(self.arc / cell)))
        ax, ay = length / float(nx), self.arc / float(ny)
        e = fan
        if not 0.0 < e < 0.4 * min(ax, ay):
            raise ValueError("Fan must be more than 0 and well under half a Cell.")

        # Each plank flat: its two ends, and which of them stands on the ground.
        flat, feet = [], []
        for i in range(nx):
            for j in range(1, ny):
                flat.append([(i * ax - e, j * ay - e), ((i + 1) * ax + e, j * ay + e)])
                feet.append((False, False))
        for i in range(nx + 1):
            for j in range(ny):
                p, q = (i * ax + e, j * ay - e), (i * ax - e, (j + 1) * ay + e)
                foot0, foot1 = j == 0, j == ny - 1
                # Cut where they reach the ground, s = 0 and s = arc.
                if foot0:
                    p = _at2(p, q, (0.0 - p[1]) / (q[1] - p[1]))
                if foot1:
                    q = _at2(p, q, (self.arc - p[1]) / (q[1] - p[1]))
                flat.append([p, q])
                feet.append((foot0, foot1))

        # Where they cross, flat.
        crossings = []
        for a in range(len(flat)):
            for b in range(a + 1, len(flat)):
                hit = _meet(flat[a][0], flat[a][1], flat[b][0], flat[b][1])
                if hit is not None:
                    crossings.append([a, hit[0], b, hit[1]])

        # Each plank cut to Overhang past its first and last crossings, or kept to the ground at a foot; a plank
        # crossing nothing dropped.
        uses = [[] for _ in flat]
        for k, (a, u, b, v) in enumerate(crossings):
            uses[a].append(u)
            uses[b].append(v)
        keep, cut = [], []
        for n, (p, q) in enumerate(flat):
            if not uses[n]:
                keep.append(False)
                cut.append((0.0, 1.0))
                continue
            size = math.hypot(q[0] - p[0], q[1] - p[1])
            u0 = 0.0 if feet[n][0] else max(0.0, min(uses[n]) - overhang / size)
            u1 = 1.0 if feet[n][1] else min(1.0, max(uses[n]) + overhang / size)
            keep.append(True)
            cut.append((u0, u1))
        index, self.feet, planks = {}, [], []
        for n in range(len(flat)):
            if keep[n]:
                index[n] = len(planks)
                u0, u1 = cut[n]
                planks.append((_at2(flat[n][0], flat[n][1], u0), _at2(flat[n][0], flat[n][1], u1)))
                self.feet.append(feet[n])
        self.flat = planks

        # The crossings on the cut planks: which plank is on top - the one whose end is nearer, its end
        # resting on the other - and where along each.
        self.crossings = []
        for a, u, b, v in crossings:
            ua = (u - cut[a][0]) / (cut[a][1] - cut[a][0])
            vb = (v - cut[b][0]) / (cut[b][1] - cut[b][0])
            la = _flat_length(self.flat[index[a]])
            lb = _flat_length(self.flat[index[b]])
            to_end_a = min(ua, 1.0 - ua) * la
            to_end_b = min(vb, 1.0 - vb) * lb
            if to_end_a <= to_end_b:
                self.crossings.append((index[a], index[b]))
            else:
                self.crossings.append((index[b], index[a]))

        # Wrapped round the vault, every end on the arch, then moved apart where they cross.
        self.ends = [[self.wrap(p), self.wrap(q)] for p, q in self.flat]
        self.residual = self.relax()

    def wrap(self, point):
        """A point of the flat pattern on the vault: x along it, s round its arch from the foot at y < 0."""
        x, s = point
        angle = -self.half + s / self.radius
        return (x, self.radius * math.sin(angle), self.radius * math.cos(angle) - (self.radius - self.rise))

    def outward(self, point):
        """Out of the vault at a point, square to its axis."""
        return _unit((0.0, point[1], point[2] + (self.radius - self.rise)))

    @staticmethod
    def closest(p0, p1, q0, q1):
        """The parameters of the closest points of lines p0-p1 and q0-q1, unbounded."""
        d1, d2, r = _sub(p1, p0), _sub(q1, q0), _sub(p0, q0)
        a, e, f = _dot(d1, d1), _dot(d2, d2), _dot(d2, r)
        b, c = _dot(d1, d2), _dot(d1, r)
        den = a * e - b * b
        if den < 1e-14:
            return 0.0, f / e
        s = (b * f - c * e) / den
        t = (a * f - b * c) / den
        return s, t

    def gap(self, top, bottom):
        """How far plank top's axis is over plank bottom's where they cross, along the line square to both,
        out of the vault; the two closest points and their parameters."""
        p0, p1 = self.ends[top]
        q0, q1 = self.ends[bottom]
        s, t = self.closest(p0, p1, q0, q1)
        pt, pb = _at(p0, p1, s), _at(q0, q1, t)
        n = _unit(_cross(_sub(p1, p0), _sub(q1, q0)))
        if _dot(n, self.outward(_mul(_add(pt, pb), 0.5))) < 0.0:
            n = _mul(n, -1.0)
        return _dot(_sub(pt, pb), n), n, s, t, pt, pb

    def relax(self, sweeps=400, tolerance=1e-6):
        """Moves the planks' ends until each crossing is Thickness deep: the top plank's axis Thickness over the
        bottom one's, along the line square to both. A foot stays where it stands. Returns the largest error
        left, m."""
        worst = 0.0
        for sweep in range(sweeps):
            worst = 0.0
            for top, bottom in self.crossings:
                g, n, s, t, pt, pb = self.gap(top, bottom)
                miss = self.thickness - g
                worst = max(worst, abs(miss))
                self._nudge(top, s, _mul(n, 0.5 * miss))
                self._nudge(bottom, t, _mul(n, -0.5 * miss))
            if worst < tolerance:
                break
        return worst

    def _nudge(self, plank, u, move):
        """Moves plank's point at u by move, sharing it between its two ends as a lever would; a foot holds."""
        w0, w1 = 1.0 - u, u
        if self.feet[plank][0]:
            w0 = 0.0
        if self.feet[plank][1]:
            w1 = 0.0
        norm = w0 * w0 + w1 * w1
        if norm < 1e-12:
            return
        p0, p1 = self.ends[plank]
        self.ends[plank] = [_add(p0, _mul(move, w0 / norm)), _add(p1, _mul(move, w1 / norm))]

    # ------------------------------------------------------------ what the definition takes

    def solve(self, density, width, push_along):
        """The model's pieces: the planks cut where they cross into Segments, each with its ZAxis (out of
        the plank's face); a Bolt line a crossing, from the plank under to the plank on top; the Feet; the
        Loads - a point a node, and its share of the planks' weight along push_along, so that the lateral
        loads add up to the weight and the pushover's load factor reads in g; and the Control node, the
        crossing nearest the crown at mid-length."""
        cuts = [[0.0, 1.0] for _ in self.ends]
        self.bolts = []
        for top, bottom in self.crossings:
            g, n, s, t, pt, pb = self.gap(top, bottom)
            cuts[top].append(s)
            cuts[bottom].append(t)
            self.bolts.append((pb, pt))
        weight_per_m = density * 9.81 * width * self.thickness / 1000.0   # kN/m
        self.segments, self.zaxes, self.planks = [], [], []
        share = {}
        for n, (p0, p1) in enumerate(self.ends):
            axis = _sub(p1, p0)
            mid = _mul(_add(p0, p1), 0.5)
            z = self.outward(mid)
            z = _unit(_sub(z, _mul(_unit(axis), _dot(z, _unit(axis)))))
            params = sorted(set(round(u, 9) for u in cuts[n]))
            points = [_at(p0, p1, u) for u in params]
            self.planks.append((p0, p1, z))
            for a, b in zip(points[:-1], points[1:]):
                if _length(_sub(b, a)) < 1e-6:
                    continue
                self.segments.append((a, b))
                self.zaxes.append(z)
                half = 0.5 * weight_per_m * _length(_sub(b, a))
                for p in (a, b):
                    key = _key(p)
                    share[key] = share.get(key, (p, 0.0))
                    share[key] = (p, share[key][1] + half)
        feet = set()
        for n, (foot0, foot1) in enumerate(self.feet):
            if foot0:
                feet.add(_key(self.ends[n][0]))
            if foot1:
                feet.add(_key(self.ends[n][1]))
        self.foot_points = [share[k][0] for k in sorted(feet) if k in share]
        self.load_points, self.load_forces = [], []
        self.weight = 0.0
        for key in sorted(share):
            p, w = share[key]
            self.weight += w
            if key in feet:
                continue
            self.load_points.append(p)
            self.load_forces.append(_mul(push_along, w))
        # Whatever stands on the ground goes into the reactions, not the push: scale the rest up so that the
        # lateral loads still add up to the whole weight.
        pushed = sum(_length(f) for f in self.load_forces)
        if pushed > 0.0:
            self.load_forces = [_mul(f, self.weight / pushed) for f in self.load_forces]
        crown = (self.length / 2.0, 0.0, self.rise)
        nodes = [b for bolt in self.bolts for b in bolt]
        self.control = min(nodes, key=lambda p: _length(_sub(p, crown)))
        return self


def _at2(p, q, u):
    return (p[0] + (q[0] - p[0]) * u, p[1] + (q[1] - p[1]) * u)


def _flat_length(plank):
    (p, q) = plank
    return math.hypot(q[0] - p[0], q[1] - p[1])


def _key(p):
    return (round(p[0], 6), round(p[1], 6), round(p[2], 6))


# ---------------------------------------------------------------- the definition's components

def _P(p):
    return rg.Point3d(p[0], p[1], p[2])


def _V(v):
    return rg.Vector3d(v[0], v[1], v[2])


def _direction(Direction):
    """X, Y or Z, and the way the push goes: along +X or +Y, or down, -Z."""
    d = str(Direction).strip().upper()
    if d not in ("X", "Y", "Z"):
        raise ValueError("Direction is X, Y or Z.")
    return d, {"X": (1.0, 0.0, 0.0), "Y": (0.0, 1.0, 0.0), "Z": (0.0, 0.0, -1.0)}[d]


def vault(Length, Span, Rise, Cell, Fan, Thickness, Width, Density, Direction):
    """The GhPython component: the vault from the sliders, lengths in m, Thickness and Width in mm, Density in
    kg/m3. Direction: X or Y to push it sideways, Z to press it down. Sign: +1, or -1 for Z, whose push goes
    down - Pushover Analysis' Target is signed."""
    name, along = _direction(Direction)
    # Floats: an integer slider gives an int, which IronPython will not format with a precision.
    Thickness, Width, Density = float(Thickness), float(Width), float(Density)
    v = Vault(float(Length), float(Span), float(Rise), float(Cell), float(Fan), 0.06,
              Thickness / 1000.0).solve(Density, Width / 1000.0, along)
    Planks = [rg.Line(_P(a), _P(b)) for a, b in v.segments]
    ZAxes = [_V(z) for z in v.zaxes]
    Bolts = [rg.Line(_P(a), _P(b)) for a, b in v.bolts]
    Feet = [_P(p) for p in v.foot_points]
    LoadPoints = [_P(p) for p in v.load_points]
    Loads = [_V(f) for f in v.load_forces]
    Control = _P(v.control)
    Sign = -1.0 if name == "Z" else 1.0
    lengths = [_length(_sub(p1, p0)) for p0, p1, z in v.planks]
    Info = ("{0} planks, {1:.0f} to {2:.0f} mm long; {3} bolts; {4} pieces of plank between the bolts.\n"
            "{5:.2f} kN of plywood. Every crossing {6:.1f} mm deep, to within {7:.4f} mm.").format(
        len(v.planks), 1000 * min(lengths), 1000 * max(lengths), len(v.bolts), len(v.segments), v.weight,
        Thickness, v.residual * 1000.0)
    return Planks, ZAxes, Bolts, Feet, LoadPoints, Loads, Control, Sign, v.weight, Info


def corotational(Planks, ZAxes):
    """The GhPython component Corotational: a Corotational transformation a plank piece, local z along its
    ZAxis, for ForceBeamColumn's GeomTransf input - large displacements, which a vault this flexible goes
    through long before it gives way. No Grasshopper component makes one."""
    import clr
    clr.AddReference("Alpaca4d.Core")
    from Alpaca4d.Element import GeomTransf, GeomTransfType
    return [GeomTransf(GeomTransfType.Corotational, rg.LineCurve(line), z) for line, z in zip(Planks, ZAxes)]


def _nice(x):
    """A round number at or above x, for an axis: 1, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten."""
    if x <= 0:
        return 1.0
    power = 10 ** math.floor(math.log10(x))
    for step in (1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10):
        if step * power >= x - 1e-9:
            return step * power
    return 10 * power


def _divisions(top):
    """How many ticks split 0 to top into steps of 1, 2, 2.5, 3 or 5 times a power of ten: five if they can,
    then four, six, three - 2.5 in five of 0.5, not four of 0.625."""
    for n in (5, 4, 6, 3):
        step = top / n
        m = step / 10 ** math.floor(math.log10(step))
        if any(abs(m - x) < 1e-9 for x in (1.0, 2.0, 2.5, 3.0, 5.0, 10.0)):
            return n
    return 4


def capacity_curve(Displacement, LoadFactor, Steps, Point, Weight, Direction, Origin, Size):
    """The capacity curve, drawn upright in the XZ plane from Origin, Size across and Size high: the push up -
    the load factor, which reads in g for a push sideways, its weight sideways, and in times its own weight for
    a push down - and the control point's displacement along, in mm. Curve: the whole of it; SoFar: up to
    Point; Dot: a ring round Point on it; Axes: the axes and their ticks; TagAt and Tags: where each label goes
    and what it says; Step: Point's recorded step, for Model View."""
    n = len(Displacement)
    if n == 0 or len(LoadFactor) != n:
        return None, None, None, [], [], [], 0
    name, along = _direction(Direction)
    i = max(0, min(n - 1, int(Point)))
    d = [abs(x) * 1000.0 for x in Displacement]
    f = [abs(x) for x in LoadFactor]
    top_d, top_f = _nice(max(d)), _nice(max(f))
    o = Origin
    at = lambda x, v: rg.Point3d(o.X + Size * x / top_d, o.Y, o.Z + Size * v / top_f)
    Curve = rg.Polyline([at(x, v) for x, v in zip(d, f)]).ToNurbsCurve()
    SoFar = rg.Polyline([at(x, v) for x, v in zip(d[:i + 1], f[:i + 1])]).ToNurbsCurve() if i > 0 else None
    tick = Size / 40.0
    Dot = rg.Circle(rg.Plane(at(d[i], f[i]), rg.Vector3d.XAxis, rg.Vector3d.ZAxis), tick).ToNurbsCurve()
    Axes = [rg.Line(at(0, 0), at(top_d, 0)), rg.Line(at(0, 0), at(0, top_f))]
    upright = lambda p: rg.Plane(p, rg.Vector3d.XAxis, rg.Vector3d.ZAxis)
    TagAt, Tags = [], []
    along, up = _divisions(top_d), _divisions(top_f)
    for k in range(along + 1):
        x = top_d * k / along
        Axes.append(rg.Line(at(x, 0), at(x, 0) + rg.Vector3d(0, 0, -tick)))
        TagAt.append(upright(at(x, 0) + rg.Vector3d(-tick, 0, -5 * tick)))
        Tags.append("{0:g}".format(round(x, 6)))
    for k in range(up + 1):
        v = top_f * k / up
        Axes.append(rg.Line(at(0, v), at(0, v) + rg.Vector3d(-tick, 0, 0)))
        TagAt.append(upright(at(0, v) + rg.Vector3d(-9 * tick, 0, -tick)))
        Tags.append("{0:g}".format(round(v, 6)))
    sideways = name != "Z"
    TagAt += [upright(at(top_d * 0.3, 0) + rg.Vector3d(0, 0, -10 * tick)), upright(at(0, top_f) + rg.Vector3d(-tick, 0, 3 * tick)),
              upright(at(0, top_f) + rg.Vector3d(-tick, 0, 8 * tick))]
    Tags += ["Crown displacement [mm]",
             "Push [g]: its weight, sideways" if sideways else "Pressed down [times its own weight]",
             ("{0:.0f} mm, {1:.2f} g, {2:.2f} kN" if sideways else "{0:.0f} mm, {1:.2f} times, {2:.2f} kN").format(
                 d[i], f[i], f[i] * Weight)]
    Step = Steps[i] if i < len(Steps) else 0
    return Curve, SoFar, Dot, Axes, TagAt, Tags, Step
