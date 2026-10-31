#! python3
# Principal Stress Lines against three closed-form solutions of elasticity, each solved by OpenSees
# and drawn beside the exact trajectories through the same seeds.
#
# Custom Preview, which colours the lines, cannot be made outside Rhino, so make.sh cannot write
# this one: run it in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the
# ScriptEditor (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with
# _RunPythonScript - and save the definition it opens.
#
# The three are the ones Alpaca4d.Test/PrincipalStressLines measures the component against:
#
# * Timoshenko and Goodier's cantilever, 8 x 2 m, in 32 x 8 quads: loaded at both ends by the exact
#   tractions, lumped to the nodes, and held only against rigid-body motion, so the exact answer is
#   the closed form everywhere. In-plane lines.
# * Navier's simply supported plate, 10 x 10 m, in 20 x 20 quads, under a uniform load: bending
#   lines. A toggle turns half its shells over, in a checkerboard.
# * Kirsch's plate with a hole: a hole of radius 1 m in an infinite plate pulled along X, seen through
#   a 10 x 10 m window, in 64 x 20 quads round the hole. The window's edges carry the exact
#   tractions of the infinite plate. In-plane lines.
#
# Each is drawn in Rhino's Top view, side by side: single lines through six seeds, with the exact
# trajectories through the same seeds dashed, and a field of lines from one seed. Red is SL1, the
# largest principal direction; blue is SL2. The exact trajectories are worked out here, when the
# definition is built, and kept in it: move a seed and its dashed line stays where it was.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import math
import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle, GH_ColourSwatch
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Vector, GH_Boolean, GH_Mesh, GH_String, GH_Plane, GH_Colour
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d, Plane, Mesh
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
VECTOR = "16EF3E75-E315-4899-B531-D3166B42DAC9"             # Params > Geometry > Vector
MESH = "1E936DF3-0EEA-4246-8549-514CB8862B7A"               # Params > Geometry > Mesh
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
MODULUS = "431BC610-8AE1-4090-B217-1A9D9C519FE2"            # Maths > Operators > Modulus
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
DASH = "95866BBE-648E-4E2B-A97C-7D04679E94E0"               # Curve > Division > Dash Pattern
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
MESH_QUAD = "1CB59C86-7F6B-4E52-9A0C-6441850E9520"          # Mesh > Primitive > Mesh Quad
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
DECONSTRUCT_FACE = "AAB142B1-B870-46DE-8E86-654C9A554D90"   # Mesh > Analysis > Deconstruct Face
MESH_EDGES = "2B9BF01D-5FE5-464C-B0B3-B469EB5F2EFB"         # Mesh > Analysis > Mesh Edges
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
CUSTOM_PREVIEW = "537B0419-BBC2-4FF4-BF08-AFE526367B2C"     # Display > Preview > Custom Preview
TEXT_TAG = "5A41528B-12B9-40DC-A3F2-842034D267C4"           # Display > Dimensions > Text Tag 3D

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
STRESS_LINES = "E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2"       # Principal Stress Lines

# Principal Stress Lines' inputs, by place. Poisson's ratio to Test Dist. are plugs of its
# Advanced menu.
PSL_MODEL, PSL_SEED, PSL_STEP, PSL_SINGLE, PSL_BENDING, PSL_SEPARATION, PSL_TEST = 0, 2, 3, 4, 6, 7, 8

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)

# What the lines are drawn in.
SL1_COLOUR = Color.FromArgb(214, 39, 40)
SL2_COLOUR = Color.FromArgb(31, 119, 180)
EXACT_COLOUR = Color.FromArgb(0, 0, 0)
MESH_COLOUR = Color.FromArgb(205, 205, 205)


# ---------------------------------------------------------------- the closed-form solutions

# Each gives the stress, or for the plate the bending moment, whose principal directions the lines
# must follow, as (sx, sy, txy); and says whether a point is inside the shape.

# Timoshenko and Goodier's cantilever: x from the loaded end, y from the axis. TIP is the load at
# the free end, which the cantilever carries to the far end; P is it per metre of thickness.
CANT_LENGTH, CANT_HALF, CANT_THICKNESS, TIP = 8.0, 1.0, 0.1, 10.0
CANT_P = TIP / CANT_THICKNESS
CANT_I = 2.0 * CANT_HALF ** 3 / 3.0


def cantilever_stress(x, y):
    return -CANT_P * x * y / CANT_I, 0.0, -CANT_P / (2.0 * CANT_I) * (CANT_HALF ** 2 - y * y)


def cantilever_inside(x, y):
    return -1e-12 <= x <= CANT_LENGTH + 1e-12 and abs(y) <= CANT_HALF + 1e-12


# Navier's simply supported square plate under a uniform load, z up. The moment has the sign of the
# bending lines: positive where the top face is stretched. Its scale does not matter for its
# directions, so neither do the load and the plate's stiffness. Odd terms up to SERIES.
SLAB, SLAB_NU, SERIES = 10.0, 0.2, 39
ODD = list(range(1, SERIES + 1, 2))
NAVIER = [[1.0 / (m * n * (m * m + n * n) ** 2) for n in ODD] for m in ODD]
NAVIER_N2 = [[c * n * n for c, n in zip(row, ODD)] for row in NAVIER]
NAVIER_N = [[c * n for c, n in zip(row, ODD)] for row in NAVIER]


def navier_moment(x, y):
    k = math.pi / SLAB
    sin_y = [math.sin(n * k * y) for n in ODD]
    cos_y = [math.cos(n * k * y) for n in ODD]
    wxx = wyy = wxy = 0.0
    for i, m in enumerate(ODD):
        sin_x, cos_x = math.sin(m * k * x), math.cos(m * k * x)
        wxx += m * m * sin_x * sum(c * s for c, s in zip(NAVIER[i], sin_y))
        wyy += sin_x * sum(c * s for c, s in zip(NAVIER_N2[i], sin_y))
        wxy -= m * cos_x * sum(c * s for c, s in zip(NAVIER_N[i], cos_y))
    kx, ky, kxy = -wxx, -wyy, -wxy
    return kx + SLAB_NU * ky, ky + SLAB_NU * kx, (1.0 - SLAB_NU) * kxy


def slab_inside(x, y):
    return -1e-12 <= x <= SLAB + 1e-12 and -1e-12 <= y <= SLAB + 1e-12


# Kirsch's infinite plate with a hole of radius HOLE, pulled along X by PULL, seen through a window
# reaching WINDOW from the hole's centre. The stresses do not depend on Poisson's ratio.
HOLE, WINDOW, PULL, HOLE_THICKNESS = 1.0, 5.0, 10000.0, 0.1


def kirsch_stress(x, y):
    r2 = x * x + y * y
    theta = math.atan2(y, x)
    a2 = HOLE * HOLE / r2
    a4 = a2 * a2
    c2, s2 = math.cos(2.0 * theta), math.sin(2.0 * theta)
    srr = PULL / 2.0 * (1.0 - a2) + PULL / 2.0 * (1.0 - 4.0 * a2 + 3.0 * a4) * c2
    stt = PULL / 2.0 * (1.0 + a2) - PULL / 2.0 * (1.0 + 3.0 * a4) * c2
    srt = -PULL / 2.0 * (1.0 + 2.0 * a2 - 3.0 * a4) * s2
    c, s = math.cos(theta), math.sin(theta)
    return (srr * c * c + stt * s * s - 2.0 * srt * s * c,
            srr * s * s + stt * c * c + 2.0 * srt * s * c,
            (srr - stt) * s * c + srt * (c * c - s * s))


def kirsch_inside(x, y):
    return x * x + y * y >= HOLE * HOLE * (1.0 - 1e-9) and abs(x) <= WINDOW + 1e-12 and abs(y) <= WINDOW + 1e-12


def principal(stress, x, y, family):
    """The direction of family 1 (the largest principal value) or 2 at a point, and half the
    difference of the two principal values."""
    sx, sy, txy = stress(x, y)
    theta = 0.5 * math.atan2(2.0 * txy, sx - sy)
    if family == 2:
        theta += math.pi / 2.0
    return math.cos(theta), math.sin(theta), math.sqrt(0.25 * (sx - sy) ** 2 + txy * txy)


def largest_difference(stress, inside, x0, y0, x1, y1):
    """Half the largest principal difference over a grid of the shape."""
    largest = 0.0
    for i in range(41):
        for j in range(41):
            x, y = x0 + (x1 - x0) * i / 40.0, y0 + (y1 - y0) * j / 40.0
            if inside(x, y):
                largest = max(largest, principal(stress, x, y, 1)[2])
    return largest


def trajectory(stress, inside, seed, family, step, largest):
    """The exact trajectory through seed, both ways, by fourth-order Runge-Kutta in steps of step.
    Each way ends where it leaves the shape, or where the two principal values come within 2% of
    the largest difference, where the direction is no longer unique - as the tests measure it."""
    halves = []
    for sign in (1.0, -1.0):
        x, y = seed
        ux, uy, r = principal(stress, x, y, family)
        px, py = sign * ux, sign * uy
        points = [(x, y)]
        length = 0.0
        while length < 100.0:
            def along(qx, qy, rx, ry):
                ux, uy, r = principal(stress, qx, qy, family)
                return (-ux, -uy) if ux * rx + uy * ry < 0 else (ux, uy)
            k1 = along(x, y, px, py)
            k2 = along(x + k1[0] * step / 2, y + k1[1] * step / 2, k1[0], k1[1])
            k3 = along(x + k2[0] * step / 2, y + k2[1] * step / 2, k1[0], k1[1])
            k4 = along(x + k3[0] * step, y + k3[1] * step, k1[0], k1[1])
            qx = x + (k1[0] + 2 * k2[0] + 2 * k3[0] + k4[0]) / 6.0 * step
            qy = y + (k1[1] + 2 * k2[1] + 2 * k3[1] + k4[1]) / 6.0 * step
            if not inside(qx, qy):
                lo, hi = 0.0, 1.0
                for _ in range(40):
                    mid = (lo + hi) / 2.0
                    if inside(x + (qx - x) * mid, y + (qy - y) * mid):
                        lo = mid
                    else:
                        hi = mid
                points.append((x + (qx - x) * lo, y + (qy - y) * lo))
                break
            d = math.hypot(qx - x, qy - y)
            px, py = (qx - x) / d, (qy - y) / d
            x, y = qx, qy
            points.append((x, y))
            length += step
            if principal(stress, x, y, family)[2] < 0.02 * largest:
                break
        halves.append(points)
    return list(reversed(halves[1]))[:-1] + halves[0]


def trajectories(stress, inside, seeds, step, box, keep):
    """Both trajectories through each seed, one after the other, every keep-th point of each kept."""
    largest = largest_difference(stress, inside, *box)
    lines = [trajectory(stress, inside, seed, family, step, largest) for seed in seeds for family in (1, 2)]
    return [line[:-1:keep] + line[-1:] for line in lines]


def lumped(segments, traction, thickness):
    """Consistent nodal forces of a traction along the edge segments (a, b) of a mesh: Simpson's rule
    on each, exact for a traction up to quadratic. Returns {node: (fx, fy)}."""
    forces = {}
    for a, b in segments:
        (xa, ya), (xb, yb) = a, b
        xm, ym = (xa + xb) / 2.0, (ya + yb) / 2.0
        length = math.hypot(xb - xa, yb - ya)
        ta, tm, tb = traction(xa, ya, xm, ym), traction(xm, ym, xm, ym), traction(xb, yb, xm, ym)
        for node, ends in ((a, (ta, tm)), (b, (tb, tm))):
            fx = length / 6.0 * (ends[0][0] + 2.0 * ends[1][0]) * thickness
            fy = length / 6.0 * (ends[0][1] + 2.0 * ends[1][1]) * thickness
            old = forces.get(node, (0.0, 0.0))
            forces[node] = (old[0] + fx, old[1] + fy)
    return forces


def cantilever_loads(divisions):
    """The exact tractions at both ends of the cantilever, on its nodes: the parabolic shear at the
    loaded end, and the bending stress and shear at the far end."""
    ys = [-CANT_HALF + 2.0 * CANT_HALF * k / divisions for k in range(divisions + 1)]
    segments = [((x, ys[k]), (x, ys[k + 1])) for x in (0.0, CANT_LENGTH) for k in range(divisions)]

    def traction(x, y, xm, ym):
        sx, sy, txy = cantilever_stress(x, y)
        n = -1.0 if xm < CANT_LENGTH / 2.0 else 1.0    # the end's outward normal, along X
        return n * sx, n * txy
    return lumped(segments, traction, CANT_THICKNESS)


def kirsch_mesh(around, out):
    """Quads round the hole: around it, and out from it to the window's square edge along rays,
    closer together near the hole. Returns the vertices, the faces and the window's edge segments."""
    vertices = []
    for j in range(out + 1):
        for i in range(around):
            theta = -2.0 * math.pi * i / around
            c, s = math.cos(theta), math.sin(theta)
            t = WINDOW / max(abs(c), abs(s))
            w = (float(j) / out) ** 1.5
            vertices.append((HOLE * c + (t * c - HOLE * c) * w, HOLE * s + (t * s - HOLE * s) * w))

    def index(i, j):
        return j * around + i % around
    faces = [(index(i, j), index(i + 1, j), index(i + 1, j + 1), index(i, j + 1)) for j in range(out) for i in range(around)]
    edge = [(vertices[index(i, out)], vertices[index(i + 1, out)]) for i in range(around)]
    return vertices, faces, edge


def kirsch_loads(edge):
    """The infinite plate's stress, as a traction on the window's edge."""
    def traction(x, y, xm, ym):
        sx, sy, txy = kirsch_stress(x, y)
        if abs(abs(xm) - WINDOW) < 1e-9:
            n = (1.0 if xm > 0 else -1.0, 0.0)
        else:
            n = (0.0, 1.0 if ym > 0 else -1.0)
        return sx * n[0] + txy * n[1], txy * n[0] + sy * n[1]
    return lumped(edge, traction, HOLE_THICKNESS)


# ---------------------------------------------------------------- building blocks

def open_canvas():
    """Grasshopper's editor, open, with a new empty document on its canvas."""
    gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    if gh is None:
        raise Exception("Grasshopper is not available in this Rhino.")
    if not gh.IsEditorLoaded():
        gh.LoadEditor()
    gh.ShowEditor()
    canvas = Grasshopper.Instances.ActiveCanvas
    if canvas is None:
        raise Exception("Open the Grasshopper editor once, then run this script again.")
    doc = GH_Document()
    Grasshopper.Instances.DocumentServer.AddDocument(doc)
    canvas.Document = doc
    return doc


def move(obj, left, top):
    """Moves an object so that its bounds - its input and output names included - start at
    (left, top). A component's pivot is not its corner: its inputs hang to the left of it."""
    a = obj.Attributes
    b = a.Bounds
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


class Grid(object):
    """Lays objects out in columns, left to right in the order the data flows, and rows, one per
    part of the model, so that each part's chain of components sits on one line. Column widths
    and row heights are the objects' own, measured once Grasshopper has laid them out."""

    def __init__(self, doc):
        self.doc = doc
        self.items = []

    def put(self, obj, col, row=None, gap=0):
        obj.Attributes.ExpireLayout()
        obj.Attributes.PerformLayout()
        self.doc.AddObject(obj, False)
        self.items.append((obj, col, row, gap))
        return obj

    def arrange(self, x0, y0, stages=(), col_gap=40.0, row_gap=36.0, stack_gap=18.0, stage_gap=80.0):
        """Places everything; returns the bottom edge of the last row."""
        def width(o):
            return o.Attributes.Bounds.Width

        def height(o):
            return o.Attributes.Bounds.Height

        cells, order = {}, []
        for obj, col, row, gap in self.items:
            if (col, row) not in cells:
                cells[(col, row)] = []
                order.append((col, row))
            cells[(col, row)].append((obj, gap))

        def cell_height(key):
            objs = cells[key]
            return sum(height(o) for o, g in objs) + sum(g + stack_gap for o, g in objs[1:])

        cols = sorted(set(c for c, r in cells))
        rows = sorted(set(r for c, r in cells if r is not None))
        col_width = {}
        for c in cols:
            col_width[c] = max(width(o) for key in cells if key[0] == c for o, g in cells[key])
        row_height = {}
        for r in rows:
            row_height[r] = max(cell_height(key) for key in cells if key[1] == r)

        col_x, x = {}, float(x0)
        for c in cols:
            if c in stages and c != cols[0]:
                x += stage_gap
            col_x[c] = x
            x += col_width[c] + col_gap
        row_y, y = {}, float(y0)
        for r in rows:
            row_y[r] = y
            y += row_height[r] + row_gap

        for key in order:
            c, r = key
            top = float(y0) if r is None else row_y[r] + (row_height[r] - cell_height(key)) / 2.0
            for i, (o, g) in enumerate(cells[key]):
                if i:
                    top += g
                move(o, col_x[c], top)
                top += height(o) + stack_gap
        return y - row_gap


# Makes an object from its GUID. Inside Rhino, Grasshopper's component server does; a script that
# builds the definition another way sets its own.
make_object = None


def emit(guid, what):
    if make_object is not None:
        obj = make_object(guid)
    else:
        obj = Grasshopper.Instances.ComponentServer.EmitObject(System.Guid(guid))
    if obj is None:
        raise Exception("{0} is not installed in this Grasshopper.".format(what))
    # Python 3 in Rhino 8 hands back an object by the interface a method returns; the component
    # itself is needed for its own members.
    obj = getattr(obj, "__implementation__", obj)
    obj.CreateAttributes()
    return obj


def component(grid, guid, what, nickname, col, row=None, gap=0):
    c = emit(guid, what)
    if nickname:
        c.NickName = nickname
    return grid.put(c, col, row, gap)


def inp(c, i):
    return c.Params.Input[i]


def out(c, i=0):
    return c.Params.Output[i]


def wire(param, *sources):
    for source in sources:
        param.AddSource(source)
    return param


def flat(param):
    """Takes whatever tree arrives as one list."""
    param.DataMapping = GH_DataMapping.Flatten
    return param


def graft(param):
    param.DataMapping = GH_DataMapping.Graft
    return param


def faint(param):
    """Draws the wires into param faintly: for the long ones that cross the canvas."""
    param.WireDisplay = GH_ParamWireDisplay.faint
    return param


def number(param, *values):
    """Types numbers into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Number(float(v)))
    return param


def integer(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def boolean(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Boolean(value))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def points(grid, name, values, col, row=None, gap=0):
    """A Point parameter holding points typed in: (x, y) pairs, or lists of them, one branch each."""
    p = component(grid, POINT, "Point", name, col, row, gap)
    p.PersistentData.Clear()
    for i, value in enumerate(values):
        if isinstance(value, list):
            for x, y in value:
                p.PersistentData.Append(GH_Point(Point3d(x, y, 0.0)), GH_Path(i))
        else:
            p.PersistentData.Append(GH_Point(Point3d(value[0], value[1], 0.0)))
    return p


def vectors(grid, name, values, col, row=None, gap=0):
    v = component(grid, VECTOR, "Vector", name, col, row, gap)
    v.PersistentData.Clear()
    for x, y, z in values:
        v.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return v


def slider(grid, name, lo, hi, value, decimals, col, row=None, gap=0):
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    s.Slider.Type = GH_SliderAccuracy.Integer if decimals == 0 else GH_SliderAccuracy.Float
    s.Slider.DecimalPlaces = decimals
    s.Slider.Minimum = System.Decimal(float(lo))
    s.Slider.Maximum = System.Decimal(float(hi))
    s.Slider.Value = System.Decimal(float(value))
    return grid.put(s, col, row, gap)


def toggle(grid, name, value, col, row=None, gap=0):
    t = GH_BooleanToggle()
    t.CreateAttributes()
    t.NickName = name
    t.Value = value
    return grid.put(t, col, row, gap)


def swatch(grid, name, colour, col, row=None, gap=0):
    s = GH_ColourSwatch()
    s.CreateAttributes()
    s.NickName = name
    s.SwatchColour = colour
    return grid.put(s, col, row, gap)


def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a section name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def note(doc, title, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = title
    p.SetUserText(text_)
    p.Attributes.Pivot = PointF(float(x), float(y))
    p.Attributes.Bounds = RectangleF(float(x), float(y), float(width), float(height))
    doc.AddObject(p, False)
    return p


def group(doc, name, objects, colour):
    g = GH_Group()
    g.CreateAttributes()
    g.NickName = name
    g.Colour = colour
    for o in objects:
        g.AddObject(o.InstanceGuid)
    doc.AddObject(g, False)
    return g


def open_menus(c):
    """Opens the fold-out menus of a component that has them, as a click on their headers would."""
    unit = getattr(c, "ActiveUnit", None)
    if unit is not None:
        for menu in unit.Context.Collection.Menus:
            menu.Expand()


# ---------------------------------------------------------------- the parts every model has

# Columns, in the order the data flows...
DATA, SHAPE, MESHED, TEST, PART, MODEL, LINES = range(7)
# ...and rows, one per part of a model.
GEOMETRY, TURN, SHELLS, HELD, LOADED, SOLVED, SINGLE, FIELD, FIELD_2, EXACT = range(10)
# What draws, in a grid of its own to the right: a row per colour.
EDGES, MOVES, SWATCHES, PREVIEWS = range(4)
SL1_ROW, SL2_ROW, EXACT_ROW, MESH_ROW, LABEL_ROW = range(5)


def material(g, name, e, nu, density):
    """An nD elastic material from panels."""
    values = [text(g, "Material", name, DATA, SHELLS),
              text(g, "E [kN/m2]", "%.0f" % e, DATA, SHELLS),
              text(g, "G [kN/m2]", "%.0f" % (e / (2.0 * (1.0 + nu))), DATA, SHELLS),
              text(g, "Poisson", str(nu), DATA, SHELLS),
              text(g, "Density [kg/m3]", str(density), DATA, SHELLS)]
    nd = component(g, ND_MATERIAL, "nD", name, SHAPE, SHELLS)
    for i, source in enumerate(values):
        wire(inp(nd, i), source)
    return values, nd


def shells(g, mesh_source, nd, thickness):
    """Plate Fiber Section and ASD Shell: one shell on each face of the mesh."""
    section = component(g, PLATE_FIBER, "Plate Fiber Section", "Plate", TEST, SHELLS)
    number(inp(section, 1), thickness)
    wire(inp(section, 2), out(nd))
    shell = component(g, SHELL, "ASD Shell", None, PART, SHELLS)
    wire(inp(shell, 0), mesh_source)
    wire(inp(shell, 1), out(section))
    return [section, shell]


def held(g, mesh_source, tests, codes, free):
    """A Support on every node of the mesh, holding the degrees of freedom whose test (an Expression
    of the node's x and y) is true, and the others unless listed as free. Returns the objects and
    the support."""
    nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Nodes", SHAPE, HELD)
    wire(inp(nodes, 0), mesh_source)
    xyz = component(g, DECONSTRUCT_POINT, "Deconstruct", "Node x, y", MESHED, HELD)
    wire(inp(xyz, 0), out(nodes, 0))
    held_ = toggle(g, "Held", True, TEST, HELD)
    free_ = toggle(g, "Free", False, TEST, HELD)
    objs = [nodes, xyz, held_, free_]
    support = component(g, SUPPORT, "Support", "Supports", PART, HELD)
    wire(inp(support, 0), out(nodes, 0))
    for i, dof in enumerate(("Tx", "Ty", "Tz", "Rx", "Ry", "Rz")):
        if dof in tests:
            e = expression(g, tests[dof], [out(xyz, 0), out(xyz, 1)], dof + ": " + codes[dof], TEST, HELD)
            objs.append(e)
            wire(inp(support, i + 1), out(e))
        else:
            wire(inp(support, i + 1), free_ if dof in free else held_)
    return objs + [support], support


def solved(g, elements, support, loads):
    """Load Pattern, Assemble Model, Analysis Settings and Run Analysis."""
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PART, SOLVED)
    flat(wire(inp(pattern, 1), *loads))
    assemble = component(g, ASSEMBLE, "Assemble Model", None, MODEL, SOLVED)
    flat(wire(inp(assemble, 0), elements))
    flat(wire(inp(assemble, 1), support))
    flat(wire(inp(assemble, 2), out(pattern)))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, PART, SOLVED, gap=40)
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, MODEL, SOLVED, gap=20)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    return [pattern, assemble, settings, run], run


def stress_lines(g, run, seeds, step, single, bending, separation, test, nickname, row):
    """A Principal Stress Lines component."""
    psl = component(g, STRESS_LINES, "Principal Stress Lines", nickname, LINES, row)
    faint(wire(inp(psl, PSL_MODEL), out(run)))
    wire(inp(psl, PSL_SEED), seeds)
    wire(inp(psl, PSL_STEP), step)
    boolean(inp(psl, PSL_SINGLE), single)
    boolean(inp(psl, PSL_BENDING), bending)
    if separation is not None:
        wire(inp(psl, PSL_SEPARATION), separation)
    if test is not None:
        wire(inp(psl, PSL_TEST), test)
    return psl


def drawn(d, mesh_source, panels, exact, labels, text_size):
    """Each Principal Stress Lines component's lines moved to its own place, beside the others, with
    the mesh's edges 5 cm under them and the exact trajectories 5 cm over the first, so that each
    shows where they cross, coloured; and a label under each. panels is [(component, (x, y))],
    labels [(text, (x, y))]. Returns the objects."""
    edges = component(d, MESH_EDGES, "Mesh Edges", None, EDGES, MESH_ROW)
    wire(inp(edges, 0), mesh_source)
    places = vectors(d, "Places", [(x, y, -0.05) for c, (x, y) in panels], EDGES, MESH_ROW)
    copies = component(d, MOVE, "Move", "Mesh under each", MOVES, MESH_ROW)
    flat(wire(inp(copies, 0), out(edges, 0), out(edges, 1)))
    graft(wire(inp(copies, 1), places))
    objs = [edges, places, copies]

    moved = {0: [], 1: []}
    for psl, (x, y) in panels:
        for family, row in ((0, SL1_ROW), (1, SL2_ROW)):
            m = component(d, MOVE, "Move", ("SL1" if family == 0 else "SL2") + " to its place", MOVES, row)
            faint(wire(inp(m, 0), out(psl, family)))
            vectors_into(inp(m, 1), [(x, y, 0.0)])
            moved[family].append(m)
            objs.append(m)
    exact_move = component(d, MOVE, "Move", "Exact to its place", MOVES, EXACT_ROW)
    faint(wire(inp(exact_move, 0), out(exact)))
    vectors_into(inp(exact_move, 1), [(panels[0][1][0], panels[0][1][1], 0.05)])
    objs.append(exact_move)

    for name, colour, row, sources in (("SL1", SL1_COLOUR, SL1_ROW, [out(m) for m in moved[0]]),
                                       ("SL2", SL2_COLOUR, SL2_ROW, [out(m) for m in moved[1]]),
                                       ("Exact", EXACT_COLOUR, EXACT_ROW, [out(exact_move)]),
                                       ("Mesh", MESH_COLOUR, MESH_ROW, [out(copies)])):
        s = swatch(d, name, colour, SWATCHES, row)
        preview = component(d, CUSTOM_PREVIEW, "Custom Preview", name, PREVIEWS, row)
        flat(wire(inp(preview, 0), *sources))
        wire(inp(preview, 1), s)
        objs += [s, preview]

    tag = component(d, TEXT_TAG, "Text Tag 3D", "Labels", PREVIEWS, LABEL_ROW)
    inp(tag, 0).PersistentData.Clear()
    for t, (x, y) in labels:
        inp(tag, 0).PersistentData.Append(GH_Plane(Plane(Point3d(x, y, 0.0), Vector3d.ZAxis)))
    inp(tag, 1).PersistentData.Clear()
    for t, (x, y) in labels:
        inp(tag, 1).PersistentData.Append(GH_String(t))
    number(inp(tag, 2), text_size)
    inp(tag, 3).PersistentData.Clear()
    inp(tag, 3).PersistentData.Append(GH_Colour(Color.Black))
    objs.append(tag)
    return objs


def vectors_into(param, values):
    param.PersistentData.Clear()
    for x, y, z in values:
        param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))


def dashed(g, exact_points, pattern):
    """The exact trajectories, one branch of points each, as dashed polylines."""
    polyline = component(g, POLYLINE, "PolyLine", "Exact", SHAPE, EXACT)
    wire(inp(polyline, 0), exact_points)
    dash = component(g, DASH, "Dash Pattern", "Dashed", MESHED, EXACT)
    wire(inp(dash, 0), out(polyline))
    number(inp(dash, 1), *pattern)
    return [polyline, dash], dash


def place(doc, g, d, y0):
    """Lays out a model's grid, and the grid of what draws it to its right; returns the bottom."""
    bottom = g.arrange(40, y0, stages=(PART, LINES), row_gap=60.0)
    right = max(o.Attributes.Bounds.Right for o, col, row, gap in g.items)
    return max(bottom, d.arrange(right + 120, y0, row_gap=60.0))


# Where each model's lines are drawn in Rhino, Top view.
CANT_AT = [(0.0, 0.0), (10.0, 0.0)]
SLAB_AT = [(0.0, 4.0), (12.0, 4.0), (24.0, 4.0)]
HOLE_AT = [(5.0, 22.0), (17.0, 22.0)]

CANT_SEEDS = [(2.0, 0.0), (5.0, 0.0), (3.1, 0.37), (5.3, -0.55), (1.2, 0.8), (7.0, -0.2)]
SLAB_SEEDS = [(2.5, 2.2), (1.0, 4.0), (3.0, 7.0), (7.5, 1.5), (4.0, 4.6), (6.3, 8.9)]
HOLE_SEEDS = [(0.05, 1.3), (1.5, 1.5), (3.0, 0.5), (-2.0, 3.0), (0.3, 4.0), (-1.2, -0.4)]


# ---------------------------------------------------------------- the three models

def cantilever(doc, y0):
    g, d = Grid(doc), Grid(doc)

    # A1 - The cantilever, 8 x 2 m, meshed 32 x 8: steel, 100 mm thick.
    outline = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "8 x 2 m", SHAPE, GEOMETRY)
    point(inp(outline, 0), 0, -CANT_HALF, 0)
    point(inp(outline, 1), CANT_LENGTH, -CANT_HALF, 0)
    point(inp(outline, 2), CANT_LENGTH, CANT_HALF, 0)
    mesh = component(g, MESH_PLANE, "Mesh Plane", "32 x 8 quads", MESHED, GEOMETRY)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), 32)
    integer(inp(mesh, 2), 8)
    values, steel = material(g, "Steel", 210000000.0, 0.3, 7850)
    parts = shells(g, out(mesh), steel, CANT_THICKNESS)

    # A2 - Held against rigid-body motion only: every node out of plane, one in X and Y at the far
    # end's middle and one more in X at its top corner.
    support_objs, support = held(g, out(mesh),
                                 {"Tx": "f(x,y)=Abs(x-8)+Min(Abs(y),Abs(y-1))<0.001", "Ty": "f(x,y)=Abs(x-8)+Abs(y)<0.001"},
                                 {"Tx": "(8, 0) and (8, 1)", "Ty": "(8, 0)"}, ("Rz",))

    # A3 - Timoshenko's end loads: the exact tractions, lumped to the nodes of the ends.
    forces = sorted(cantilever_loads(8).items())
    at = points(g, "End nodes", [p for p, f in forces], DATA, LOADED)
    pull = vectors(g, "End forces [kN]", [(f[0], f[1], 0.0) for p, f in forces], DATA, LOADED)
    load = component(g, POINT_LOAD, "Point Load", "End loads", TEST, LOADED)
    wire(inp(load, 0), at)
    wire(inp(load, 1), pull)
    model, run = solved(g, out(parts[1]), out(support), [out(load)])

    # A4 - Principal Stress Lines: single lines through six seeds, and a field from one.
    seeds = points(g, "Seeds", CANT_SEEDS, DATA, SINGLE)
    seed = points(g, "Field seed", [(4.1, 0.13)], DATA, FIELD)
    step = slider(g, "Step Tolerance", 0.01, 0.5, 0.1, 2, PART, SINGLE)
    separation = slider(g, "Separation", 0.05, 1.0, 0.25, 2, PART, FIELD)
    single = stress_lines(g, run, seeds, step, True, False, None, None, "Single lines", SINGLE)
    field = stress_lines(g, run, seed, step, False, False, separation, None, "Field", FIELD)

    # A5 - The exact trajectories through the six seeds.
    exact = points(g, "Exact trajectories", trajectories(cantilever_stress, cantilever_inside, CANT_SEEDS, 0.01,
                                                        (0.0, -CANT_HALF, CANT_LENGTH, CANT_HALF), 5), DATA, EXACT)
    dash_objs, dash = dashed(g, exact, (0.08, 0.06))

    # A6 - Drawn side by side.
    shown = drawn(d, out(mesh), [(single, CANT_AT[0]), (field, CANT_AT[1])], dash,
                  [("Cantilever: single lines (dashed: exact)", (CANT_AT[0][0], -1.6)),
                   ("Cantilever: field from one seed", (CANT_AT[1][0], -1.6))], 0.3)

    bottom = place(doc, g, d, y0)
    group(doc, "A1 - Timoshenko's cantilever: 8 x 2 m, 32 x 8 quads, steel 100 mm", [outline, mesh, steel] + values + parts, ORANGE)
    group(doc, "A2 - Held against rigid-body motion only", support_objs, GREEN)
    group(doc, "A3 - Loaded at both ends by the exact tractions, lumped to the nodes", [at, pull, load], RED)
    group(doc, "A4 - One load pattern, assembled and solved", model, BLUE)
    group(doc, "A5 - Principal Stress Lines: six single lines, and a field from one seed", [seeds, seed, step, separation, single, field], PURPLE)
    group(doc, "A6 - Exact trajectories through the six seeds, from the closed form", [exact] + dash_objs, GREY)
    group(doc, "A7 - Drawn side by side in Rhino's Top view", shown, GREY)
    return bottom


def slab(doc, y0):
    g, d = Grid(doc), Grid(doc)

    # B1 - The slab, 10 x 10 m, meshed 20 x 20: concrete, 100 mm thick. Turned over, a third of
    # its shells face down: Mesh Quad makes the corners of every face whose first corner is a
    # multiple of three run the other way. Not half: with as many up as down, which way up the
    # slab is read is a toss-up, and SL1 and SL2 would swap.
    outline = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "10 x 10 m", SHAPE, GEOMETRY)
    point(inp(outline, 0), 0, 0, 0)
    point(inp(outline, 1), SLAB, 0, 0)
    point(inp(outline, 2), SLAB, SLAB, 0)
    plane = component(g, MESH_PLANE, "Mesh Plane", "20 x 20 quads", MESHED, GEOMETRY)
    wire(inp(plane, 0), out(outline))
    integer(inp(plane, 1), 20)
    integer(inp(plane, 2), 20)
    turned = toggle(g, "Turn a third of the shells over", False, DATA, TURN)
    parts_of = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Nodes and faces", SHAPE, TURN)
    wire(inp(parts_of, 0), out(plane))
    corners = component(g, DECONSTRUCT_FACE, "Deconstruct Face", "Corners", MESHED, TURN)
    wire(inp(corners, 0), out(parts_of, 1))
    third = component(g, MODULUS, "Modulus", "Every third", TEST, TURN)
    wire(inp(third, 0), out(corners, 0))
    number(inp(third, 1), 3)
    second = expression(g, "f(b,d,m,t)=If(t,If(m<0.5,d,b),b)", [out(corners, 1), out(corners, 3), out(third), turned], "Corner B", PART, TURN)
    fourth = expression(g, "f(b,d,m,t)=If(t,If(m<0.5,b,d),d)", [out(corners, 1), out(corners, 3), out(third), turned], "Corner D", PART, TURN)
    quads = component(g, MESH_QUAD, "Mesh Quad", "Faces", MODEL, TURN)
    wire(inp(quads, 0), out(corners, 0))
    wire(inp(quads, 1), out(second))
    wire(inp(quads, 2), out(corners, 2))
    wire(inp(quads, 3), out(fourth))
    mesh = component(g, CONSTRUCT_MESH, "Construct Mesh", "Slab", LINES, TURN)
    faint(wire(inp(mesh, 0), out(parts_of, 0)))
    wire(inp(mesh, 1), out(quads))
    values, concrete = material(g, "Concrete", 30000000.0, SLAB_NU, 2500)
    parts = shells(g, out(mesh), concrete, 0.1)
    faint(inp(parts[1], 0))
    mesh_parts = [outline, plane, turned, parts_of, corners, third, second, fourth, quads, mesh]

    # B2 - Simply supported on all four edges: every edge node held up, and the rotation along its
    # edge held. Every node is held in plane: only bending is wanted.
    support_objs, support = held(g, out(plane),
                                 {"Tz": "f(x,y)=Max(Abs(x-5),Abs(y-5))>4.999", "Rx": "f(x,y)=Abs(x-5)>4.999", "Ry": "f(x,y)=Abs(y-5)>4.999"},
                                 {"Tz": "on an edge", "Rx": "on an edge along Y", "Ry": "on an edge along X"}, ())

    # B3 - 10 kN/m2 down, on every shell.
    down = vectors(g, "10 kN/m2 down", [(0.0, 0.0, -10.0)], DATA, LOADED)
    load = component(g, MESH_LOAD, "Mesh Load", "Uniform load", TEST, LOADED)
    wire(inp(load, 1), down)
    model, run = solved(g, out(parts[1]), out(support), [out(load)])

    # B4 - Bending stress lines: single lines through six seeds, a field from one, and the same
    # field with Test Dist.
    seeds = points(g, "Seeds", SLAB_SEEDS, DATA, SINGLE)
    seed = points(g, "Field seed", [(2.6, 3.1)], DATA, FIELD)
    step = slider(g, "Step Tolerance", 0.01, 1.0, 0.2, 2, PART, SINGLE)
    separation = slider(g, "Separation", 0.1, 2.0, 0.8, 2, PART, FIELD)
    test = slider(g, "Test Dist.", 0.0, 1.0, 0.4, 2, PART, FIELD_2)
    single = stress_lines(g, run, seeds, step, True, True, None, None, "Single lines", SINGLE)
    field = stress_lines(g, run, seed, step, False, True, separation, None, "Field", FIELD)
    tested = stress_lines(g, run, seed, step, False, True, separation, test, "Field, Test Dist.", FIELD_2)

    # B5 - The exact trajectories through the six seeds.
    exact = points(g, "Exact trajectories", trajectories(navier_moment, slab_inside, SLAB_SEEDS, 0.02,
                                                        (0.0, 0.0, SLAB, SLAB), 2), DATA, EXACT)
    dash_objs, dash = dashed(g, exact, (0.12, 0.08))

    # B6 - Drawn side by side.
    shown = drawn(d, out(mesh), [(single, SLAB_AT[0]), (field, SLAB_AT[1]), (tested, SLAB_AT[2])], dash,
                  [("Slab, bending: single lines (dashed: exact)", (SLAB_AT[0][0], SLAB_AT[0][1] - 0.8)),
                   ("Slab, bending: field from one seed", (SLAB_AT[1][0], SLAB_AT[1][1] - 0.8)),
                   ("Slab, bending: field, Test Dist.", (SLAB_AT[2][0], SLAB_AT[2][1] - 0.8))], 0.35)

    bottom = place(doc, g, d, y0)
    group(doc, "B1 - Navier's slab: 10 x 10 m, 20 x 20 quads, concrete 100 mm. The toggle turns a third of its shells over",
          mesh_parts + [concrete] + values + parts, ORANGE)
    group(doc, "B2 - Simply supported: edges held up, and their rotation along the edge", support_objs, GREEN)
    group(doc, "B3 - 10 kN/m2 down", [down, load], RED)
    group(doc, "B4 - One load pattern, assembled and solved", model, BLUE)
    group(doc, "B5 - Bending stress lines: six single lines, a field, and a field with Test Dist.",
          [seeds, seed, step, separation, test, single, field, tested], PURPLE)
    group(doc, "B6 - Exact trajectories through the six seeds, from Navier's series", [exact] + dash_objs, GREY)
    group(doc, "B7 - Drawn side by side in Rhino's Top view", shown, GREY)
    return bottom


def plate_with_hole(doc, y0):
    g, d = Grid(doc), Grid(doc)

    # C1 - The window, 10 x 10 m round a hole of radius 1 m, in 64 x 20 quads, kept in the
    # definition: steel, 100 mm thick.
    vertices, faces, edge = kirsch_mesh(64, 20)
    m = Mesh()
    for x, y in vertices:
        m.Vertices.Add(x, y, 0.0)
    for a, b, c, e in faces:
        m.Faces.AddFace(a, b, c, e)
    m.Normals.ComputeNormals()
    mesh = component(g, MESH, "Mesh", "64 x 20 quads", MESHED, GEOMETRY)
    mesh.PersistentData.Clear()
    mesh.PersistentData.Append(GH_Mesh(m))
    values, steel = material(g, "Steel", 210000000.0, 0.3, 7850)
    parts = shells(g, mesh, steel, HOLE_THICKNESS)

    # C2 - Held against rigid-body motion only: every node out of plane, (5, 0) in X and Y, and
    # (-5, 0) in Y.
    support_objs, support = held(g, mesh,
                                 {"Tx": "f(x,y)=Abs(x-5)+Abs(y)<0.001", "Ty": "f(x,y)=Abs(Abs(x)-5)+Abs(y)<0.001"},
                                 {"Tx": "(5, 0)", "Ty": "(5, 0) and (-5, 0)"}, ("Rz",))

    # C3 - The infinite plate's stress on the window's edge, pulled 10 MPa along X far away.
    forces = sorted(kirsch_loads(edge).items())
    at = points(g, "Edge nodes", [p for p, f in forces], DATA, LOADED)
    pull = vectors(g, "Edge forces [kN]", [(f[0], f[1], 0.0) for p, f in forces], DATA, LOADED)
    load = component(g, POINT_LOAD, "Point Load", "Edge loads", TEST, LOADED)
    wire(inp(load, 0), at)
    wire(inp(load, 1), pull)
    model, run = solved(g, out(parts[1]), out(support), [out(load)])

    # C4 - Principal Stress Lines: single lines through six seeds, and a field from one, with Test
    # Dist. so that the lines along X do not crowd onto the axis left of the hole.
    seeds = points(g, "Seeds", HOLE_SEEDS, DATA, SINGLE)
    seed = points(g, "Field seed", [(0.3, 1.8)], DATA, FIELD)
    step = slider(g, "Step Tolerance", 0.01, 0.5, 0.1, 2, PART, SINGLE)
    separation = slider(g, "Separation", 0.05, 1.0, 0.4, 2, PART, FIELD)
    test = slider(g, "Test Dist.", 0.0, 1.0, 0.2, 2, PART, FIELD, gap=10)
    single = stress_lines(g, run, seeds, step, True, False, None, None, "Single lines", SINGLE)
    field = stress_lines(g, run, seed, step, False, False, separation, test, "Field", FIELD)

    # C5 - The exact trajectories through the six seeds.
    exact = points(g, "Exact trajectories", trajectories(kirsch_stress, kirsch_inside, HOLE_SEEDS, 0.01,
                                                        (-WINDOW, -WINDOW, WINDOW, WINDOW), 5), DATA, EXACT)
    dash_objs, dash = dashed(g, exact, (0.12, 0.08))

    # C6 - Drawn side by side.
    shown = drawn(d, mesh, [(single, HOLE_AT[0]), (field, HOLE_AT[1])], dash,
                  [("Plate with a hole: single lines (dashed: exact)", (HOLE_AT[0][0] - WINDOW, HOLE_AT[0][1] - WINDOW - 0.8)),
                   ("Plate with a hole: field from one seed, Test Dist.", (HOLE_AT[1][0] - WINDOW, HOLE_AT[1][1] - WINDOW - 0.8))], 0.35)

    bottom = place(doc, g, d, y0)
    group(doc, "C1 - Kirsch's plate with a hole: a 10 x 10 m window round a 2 m hole, 64 x 20 quads, steel 100 mm",
          [mesh, steel] + values + parts, ORANGE)
    group(doc, "C2 - Held against rigid-body motion only", support_objs, GREEN)
    group(doc, "C3 - The infinite plate's stress on the window's edge, lumped to the nodes: 10 MPa along X far away",
          [at, pull, load], RED)
    group(doc, "C4 - One load pattern, assembled and solved", model, BLUE)
    group(doc, "C5 - Principal Stress Lines: six single lines, and a field from one seed with Test Dist.",
          [seeds, seed, step, separation, test, single, field], PURPLE)
    group(doc, "C6 - Exact trajectories through the six seeds, from the closed form", [exact] + dash_objs, GREY)
    group(doc, "C7 - Drawn side by side in Rhino's Top view", shown, GREY)
    return bottom


def build(doc):
    bottom = cantilever(doc, 380)
    bottom = slab(doc, bottom + 160)
    plate_with_hole(doc, bottom + 160)

    # Only the Custom Previews and the labels draw; everything else stays out of the viewport.
    # Principal Stress Lines' Advanced menus are opened only now, once everything is in place:
    # opened before, a menu sits at the canvas's origin until the canvas lays it out, and the
    # component's bounds stretch to it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.Name not in ("Custom Preview", "Text Tag 3D"):
            obj.Hidden = True
        if str(obj.ComponentGuid).upper() == STRESS_LINES:
            open_menus(obj)

    note(doc, "Principal Stress Lines against closed-form solutions",
         "Principal Stress Lines against three closed-form solutions.\n\n"
         "Each model is solved by OpenSees, and its lines are drawn in Rhino's Top view beside one "
         "another: single lines through six seeds, with the exact trajectories through the same seeds "
         "dashed, and a field of lines from one seed. Red is SL1, the largest principal direction; blue "
         "is SL2.\n\n"
         "A - Timoshenko and Goodier's cantilever, loaded by the exact tractions at both ends: in-plane lines.\n"
         "B - Navier's simply supported slab under a uniform load: bending lines. Turn a third of the "
         "shells over to see the lines carry on across shells that face the other way.\n"
         "C - Kirsch's plate with a hole, pulled along X: in-plane lines.\n\n"
         "The exact trajectories are worked out when the definition is built and kept in it: move a "
         "seed and its dashed line stays where it was. Alpaca4d.Test/PrincipalStressLines measures the "
         "same lines against the same solutions.",
         40, 40, 620, 270)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Principal Stress Lines benchmarks built. Look at Rhino's Top view.")


if __name__ == "__main__":
    main()
