#! python3
# Four examples of steel that yields - the nD component's J2Plasticity unit - pushed past first
# yield, a definition each (CASE):
#
#   tjoint      PlasticityTJoint.gh      a T-joint of two steel tubes: a brace 150 x 8 mm pushed into a
#                                        chord 300 x 10 mm, as ASD Shells; the chord's wall yields round
#                                        the brace's foot. EN 1993-1-8's chord face resistance is 388 kN.
#   yieldlines  PlasticityYieldLines.gh  a square S355 plate 1 x 1 m, 25 mm thick, simply supported on
#                                        its four edges under a pressure, as ASD Shells: it yields along
#                                        its diagonals, as yield-line theory draws them, at 24 mp / L2.
#   platehole   PlasticityPlateHole.gh   a plate 400 x 200 x 10 mm with a hole 60 mm across, pulled in
#                                        its own plane, as ASD Shells: first yield at the edge of the
#                                        hole, then across the net section, at Fy times its area.
#   pipe        PlasticityPipe.gh        a thick-walled pipe, bore 200 mm and outside 400 mm, under an
#                                        inner pressure, a quarter of a slice in plane strain as Ten Node
#                                        Tetrahedra: the steel yields from the bore out, a ring at a time,
#                                        to 2 Fy / sqrt(3) ln(b / a) = 284 MPa.
#
# Each reads its mesh from the .txt next to this script, which PlasticityMesh.py made with gmsh, and
# keeps it in the definition. Each is J2Plasticity S355, perfectly plastic, Newton and 20 steps of Load
# Control on a Linear Time Series; a Model View draws the Von Mises stress in Alpaca4d's colours, the
# yielding steel orange, and a GhPython component (PlasticityCurve.py) the load-displacement curve,
# blue while elastic and orange after, through Custom Preview.
#
# Custom Preview takes Rhino's own display code to be made, so make.sh cannot write these: run the
# script in Rhino 8 (the ScriptEditor, or _-RunPythonScript) with CASE set, and SAVE set to a path to
# save what it opens there as a .gh.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import math
import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay, GH_ParameterSide, GH_ParamAccess
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Parameters.Hints import GH_Point3dHint, GH_Vector3dHint, GH_StringHint_CS
from Grasshopper.Kernel.Special import GH_Group, GH_Panel, GH_BooleanToggle, GH_NumberSlider
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_MeshFace, GH_Boolean, GH_String, GH_Vector, GH_Colour, GH_Material
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d, MeshFace
from System.Drawing import PointF, RectangleF, Color, Bitmap
from System.Reflection import BindingFlags

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
INTEGER = "2E3AB970-8545-46BB-836C-1C11E5610BCE"            # Params > Primitive > Integer
NUMBER = "3E8CA6BE-FDA8-4AAF-B5C0-3C54C8BB7312"             # Params > Primitive > Number
VECTOR = "16EF3E75-E315-4899-B531-D3166B42DAC9"             # Params > Geometry > Vector
MESH_FACE = "E02B3DA5-543A-46AC-A867-0BA6B0A524DE"          # Params > Geometry > Mesh Face
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
LIST_LENGTH = "1817FD29-20AE-4503-B542-F0FB651E67D7"        # Sets > List > List Length
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
MULTIPLY = "63FFF845-7C61-4DFB-BA12-44D481B4BF0F"           # Vector > Vector > Multiply
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern
CULL_DUPLICATES = "6EAFFBB2-3392-441A-8556-2DC126AA8910"    # Vector > Point > Cull Duplicates
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script
CUSTOM_PREVIEW = "537B0419-BBC2-4FF4-BF08-AFE526367B2C"     # Display > Preview > Custom Preview

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD, on its J2Plasticity unit
LAYERED_SHELL = "3A7E1C64-9D0B-4F52-8C71-2E6B5A9D41F8"      # Layered Shell
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
ASD_SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"          # ASD Shell
TEN_NODE = "1F38CDFC-F909-4F3F-969E-9E2C63F9A321"           # Ten Node Tetrahedron
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
LINEAR_SERIES = "05134721-D2DC-483E-BAD7-1A1AF0238A3D"      # Linear Time Series
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
TEST = "F8A3B2C1-D4E5-4F6A-B7C8-9D0E1F2A3B4C"               # Test
INTEGRATOR = "A5F2B8C3-D1E4-4F5A-B6C7-8D9E0F1A2B3C"         # Integrator, on its LoadControl unit
ANALYSIS_STEP = "825E700F-52D7-433D-92F6-8B6730F0A217"      # Analysis Step
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NODAL_DISPLACEMENTS = "25085EAB-6C66-487E-B83B-BAB5AB02B1BB"  # Nodal Displacements
SHELL_STRESSES = "9B38E0D7-5A40-528F-A0E0-404AB291CE01"     # Shell Stresses
BRICK_STRESSES_ = "DF03EC57-E1FA-4A3D-82BB-7F65526CF0B6"    # Brick Stresses
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend

J2_PLASTICITY = "J2Plasticity (Alpaca4d)"
LOAD_CONTROL = "LoadControl"
NORM_DISP_INCR = "NormDispIncr"

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)

# Alpaca4d's palette at full strength, for what the viewport draws.
LIGHT_BLUE = Color.FromArgb(255, 66, 136, 247)
FULL_ORANGE = Color.FromArgb(255, 235, 108, 63)
DARK_TECH = Color.FromArgb(255, 42, 45, 49)

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
ICONS = System.IO.Path.Combine(HERE, "..", "..", "Assets", "icon-candidates")
CURVE = "PlasticityCurve.py"

STEPS, LAMBDA = 20, 0.05    # Load Control: 20 steps of 5% of the load
FY = 355.0                  # MPa
BANDS = 12                  # iso-surfaces: eleven shades of the elastic range, and the top one
SHELL_STRESS, BRICK_STRESS, VON_MISES_SHELL, VON_MISES_BRICK = 5, 6, 5, 6
E = 1e-6                    # m: a coordinate this close to a line is on it

# Each case: its mesh, its parts' thickness, local x and whether they yield; its load, in what unit,
# shown in what; the node the curve follows and which way; the plane the curve is drawn in, its width
# and height; and what the note says.
CASES = {
    "tjoint": {
        "title": "Pipe T-joint", "data": "PlasticityTJoint.txt", "kind": "shell",
        "parts": {0: ("Chord 300 x 10", 0.01, (1, 0, 0), True), 1: ("Brace 150 x 8", 0.008, (0, 0, 1), True),
                  2: ("Brace end, elastic", 0.03, (0, 0, 1), False)},
        "load": 640.0, "unit": "kN", "scale": 1.0, "load_name": "Load [kN]",
        "follow": (0.9, 0.0, 0.6), "along": (0, 0, -1),
        "plane": ((2.1, 0.0, -0.145), (1, 0, 0), (0, 0, 1)), "width": 1.0, "height": 0.75,
        "deform": 10.0,
        # Where the yielding first spreads round the brace's foot, Newton closes in slowly: a
        # displacement increment of 1e-6 m - a micrometre - rather than 1e-8.
        "tolerance": 1e-6,
    },
    "yieldlines": {
        "title": "Yield lines", "data": "PlasticityYieldLines.txt", "kind": "shell",
        "parts": {0: ("Plate 25 mm", 0.025, (1, 0, 0), True)},
        "load": 1300.0, "unit": "kN/m2", "scale": 1.0, "load_name": "Pressure [kN/m2]",
        "follow": (0.5, 0.5, 0.0), "along": (0, 0, -1),
        "plane": ((1.25, 0.0, 0.0), (1, 0, 0), (0, 1, 0)), "width": 0.9, "height": 0.7,
        "deform": 4.0,
    },
    "platehole": {
        "title": "Plate with a hole", "data": "PlasticityPlateHole.txt", "kind": "shell",
        "parts": {0: ("Plate 10 mm", 0.01, (1, 0, 0), True)},
        "load": 500.0, "unit": "kN", "scale": 1.0, "load_name": "Load [kN]",
        "follow": (0.4, 0.1, 0.0), "along": (1, 0, 0),
        "plane": ((0.5, 0.0, 0.0), (1, 0, 0), (0, 1, 0)), "width": 0.32, "height": 0.2,
        "deform": 30.0,
    },
    "pipe": {
        "title": "Thick pipe under pressure", "data": "PlasticityPipe.txt", "kind": "solid",
        "load": 280000.0, "unit": "MPa", "scale": 0.001, "load_name": "Pressure [kN/m2]",
        "follow": (0.1, 0.0, 0.01), "along": (1, 0, 0),
        "plane": ((0.28, 0.0, 0.0), (1, 0, 0), (0, 1, 0)), "width": 0.3, "height": 0.2,
        "deform": 60.0,
    },
}


def read_mesh(case):
    """The nodes, as (x, y, z), and the cells: (part, [nodes]) for a shell mesh, [four nodes] for tetrahedra."""
    lines = System.IO.File.ReadAllText(System.IO.Path.Combine(HERE, case["data"])).split("\n")
    count = int(lines[0].split()[1])
    nodes = [tuple(float(v) for v in line.split()) for line in lines[1:1 + count]]
    kind, many = lines[1 + count].split()
    rows = [[int(v) for v in line.split()] for line in lines[2 + count:2 + count + int(many)]]
    if kind == "faces":
        return nodes, [(r[0], r[1:]) for r in rows]
    return nodes, rows


def plate_edge_shares(nodes):
    """The plate with a hole's pulled edge, X = 0.4: each node's share of a uniform pull, its tributary length."""
    right = sorted([i for i, p in enumerate(nodes) if abs(p[0] - 0.4) < E], key=lambda i: nodes[i][1])
    ys = [nodes[i][1] for i in right]
    shares = []
    for k in range(len(right)):
        lo = (ys[k] - ys[k - 1]) / 2.0 if k > 0 else 0.0
        hi = (ys[k + 1] - ys[k]) / 2.0 if k < len(right) - 1 else 0.0
        shares.append((lo + hi) / (ys[-1] - ys[0]))
    return [nodes[i] for i in right], shares


def bore_forces(nodes, tets, a=0.1):
    """The pipe's bore: each face of a tetrahedron on it puts a third of its area on each of its edge
    nodes, outwards, and nothing on its corners, a six node triangle's share of a uniform pressure.
    The edge nodes as Ten Node Tetrahedron puts them, at the middle of each edge; the force per kN/m2."""
    forces = {}
    for t in tets:
        for f in ((0, 1, 2), (0, 1, 3), (1, 2, 3), (0, 2, 3)):
            cs = [t[q] for q in f]
            if not all(abs(math.hypot(nodes[i][0], nodes[i][1]) - a) < E for i in cs):
                continue
            p = [nodes[i] for i in cs]
            u = [p[1][d] - p[0][d] for d in range(3)]
            v = [p[2][d] - p[0][d] for d in range(3)]
            area = 0.5 * math.sqrt((u[1] * v[2] - u[2] * v[1]) ** 2 + (u[2] * v[0] - u[0] * v[2]) ** 2 + (u[0] * v[1] - u[1] * v[0]) ** 2)
            for x, y in ((0, 1), (1, 2), (0, 2)):
                m = tuple(round(0.5 * (p[x][d] + p[y][d]), 9) for d in range(3))
                r = math.hypot(m[0], m[1])
                fx, fy = forces.get(m, (0.0, 0.0))
                forces[m] = (fx + area / 3.0 * m[0] / r, fy + area / 3.0 * m[1] / r)
    keys = sorted(forces)
    return keys, [forces[k] for k in keys]


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
    part of the model, so that each part's chain of components sits on one line. Column widths and
    row heights are the objects' own, measured once Grasshopper has laid them out."""

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
        """Places everything; returns the right edge of the last column."""
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
        return x - col_gap


# Makes an object from its GUID. Inside Rhino, Grasshopper's component server does; WriteGh.py,
# which writes the .gh without Rhino, sets its own.
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


def toggle(grid, name, value, col, row=None, gap=0):
    t = GH_BooleanToggle()
    t.CreateAttributes()
    t.NickName = name
    t.Value = value
    return grid.put(t, col, row, gap)



PANEL = Color.FromArgb(255, 255, 250, 90)


def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a material's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
    return p


def note(doc, title, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
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


def widget(obj, field):
    """One of an Alpaca4d component's menu controls, which it keeps to itself."""
    return obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj)


def show_result(view, show, component):
    """Ticks one of Model View's results alone, at a component, as clicks in its Results menu do.
    Its menus then show the controls its settings have a use for and put the rest away, and it is laid
    out again: the grid measures it at the height it opens at. Set its other widgets first."""
    flags = BindingFlags.NonPublic | BindingFlags.Instance
    view.GetType().GetMethod("ShowResult", flags).Invoke(view, System.Array[System.Object]([show, component]))
    view.Attributes.ExpireLayout()
    view.Attributes.PerformLayout()




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


def booleans(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Boolean(v))
    return param


def words(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
    return param


def vector(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def shader(param, c):
    """A colour typed into a Custom Preview's Material input, which takes it as a plain material."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Material(c))
    return param


def colours(param, stops, count, top=None):
    """A colour scale typed into an unwired input: count colours spread evenly between the stops,
    and then top, when given, as one colour more."""
    rgb = [tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) for h in stops]
    param.PersistentData.Clear()
    for k in range(count):
        t = k / float(count - 1) * (len(rgb) - 1)
        i = min(int(t), len(rgb) - 2)
        f = t - i
        c = [int(round(rgb[i][j] + (rgb[i + 1][j] - rgb[i][j]) * f)) for j in range(3)]
        param.PersistentData.Append(GH_Colour(Color.FromArgb(255, c[0], c[1], c[2])))
    if top is not None:
        param.PersistentData.Append(GH_Colour(top))
    return param


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def switch_unit(c, unit):
    """Puts a switcher component on another of its units, as its drop-down does. It sets its unit's
    parameters aside as it switches, and writes to Rhino's command line when one still has a parent -
    which outside Rhino is not there to write to - so the parents are cleared first."""
    c = getattr(c, "__implementation__", c)
    for param in list(c.Params.Input) + list(c.Params.Output):
        if param.Attributes is not None:
            param.Attributes.Parent = None
    c.SwitchUnit(unit, False, False)
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()
    return c


def menu_setting(c, field, prop, value):
    """Sets a widget of a component's fold-out menus, found by its field, as a click on it would:
    the component saves it with the file."""
    c = getattr(c, "__implementation__", c)
    flags = BindingFlags.Instance | BindingFlags.NonPublic
    setattr(c.GetType().GetField(field, flags).GetValue(c), prop, value)


def python(grid, nickname, source, function, inputs, outputs, icon, col, row=None):
    """A GhPython component running function from the file source next to this script: its inputs as
    (name, hint, access), its outputs by name after its own out, which carries what the code prints
    and its errors; shown with icon, a PNG in Assets/icon-candidates."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, (name, hint, access) in enumerate(inputs):
        p = c.CreateParameter(GH_ParameterSide.Input, i)
        p.Name = p.NickName = name
        p.TypeHint = hint
        p.Access = access
        c.Params.RegisterInputParam(p)
    for i, name in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    code = System.IO.File.ReadAllText(System.IO.Path.Combine(HERE, source))
    c.Code = code + "\n\n" + ", ".join(outputs) + " = " + function + "(" + ", ".join(i[0] for i in inputs) + ")\n"
    c.SetIconOverride(Bitmap(System.IO.Path.Combine(ICONS, icon)))
    return grid.put(c, col, row)


# ---------------------------------------------------------------- the examples

# Columns, in the order the data flows...
DATA_, CORNERS, MESHES, NAMES, MATERIAL, ELEMENTS, COORDS, TEST_, KEEP, UNIQUE, LOADS = range(11)
# ...and rows, one per part of the model.
CELLS, HELD, HELD2, HELD3, LOADED = range(5)

# The model and its analysis, in a grid of their own to the right.
MODEL, VIEW, SHOWN = range(3)
ASSEMBLED, ANALYSED, CURVE_ = range(3)

# Which case to build: make.sh and the ScriptEditor take it from the environment; a script that
# imports this module can set it.
CASE = System.Environment.GetEnvironmentVariable("CASE") or "tjoint"


def vectors(param, values):
    param.PersistentData.Clear()
    for x, y, z in values:
        param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return param


def points(param, values):
    param.PersistentData.Clear()
    for x, y, z in values:
        param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def held_by(g, nodes, test, sources, name, holds, toggles, row):
    """A support on the nodes test picks: an Expression of their coordinates, Cull Pattern, and a
    Support holding the translations holds names (X, Y, Z), the rotations free."""
    picked = expression(g, test, sources, name, TEST_, row)
    keep = component(g, CULL_PATTERN, "Cull Pattern", name, KEEP, row)
    wire(inp(keep, 0), nodes)
    wire(inp(keep, 1), out(picked))
    support = component(g, SUPPORT, "Support", name + ": " + " ".join(holds), LOADS, row)
    wire(inp(support, 0), out(keep))
    held, free = toggles
    for i, axis in enumerate("XYZ"):
        wire(inp(support, 1 + i), held if axis in holds else free)
    for i in (4, 5, 6):
        wire(inp(support, i), free)
    return [picked, keep, support], support


def build(doc):
    case = CASES[CASE]
    nodes, cells = read_mesh(case)
    shell = case["kind"] == "shell"
    g = Grid(doc)
    parts_1, parts_2, parts_3, parts_4 = [], [], [], []

    # 1 - The mesh as gmsh wrote it: the nodes, and the corners of each cell.
    node_points = component(g, POINT, "Point", "Nodes", DATA_, CELLS)
    node_points.PersistentData.Clear()
    for x, y, z in nodes:
        node_points.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    parts_1.append(node_points)

    # 2 - S355 that yields, J2Plasticity, perfectly plastic.
    steel_values = [text(g, "Steel", "S355", NAMES, CELLS),
                    text(g, "E [kN/m2]", "210000000", NAMES, CELLS),
                    text(g, "Poisson", "0.3", NAMES, CELLS),
                    text(g, "Fy [kN/m2]", "355000", NAMES, CELLS),
                    text(g, "H [kN/m2]", "%g" % case.get("hardening", 0), NAMES, CELLS),
                    text(g, "Density [kg/m3]", "7850", NAMES, CELLS)]
    steel = component(g, ND_MATERIAL, "nD", "S355, yielding", MATERIAL, CELLS)
    switch_unit(steel, J2_PLASTICITY)
    for i, source in enumerate(steel_values):
        wire(inp(steel, i), source)
    parts_2 += steel_values + [steel]

    elements = []
    if shell:
        # One mesh per part, every face an ASD Shell, on a Layered Shell of ten equal layers of the
        # steel - or, for an elastic part, a Plate Fiber Section of elastic steel.
        elastic = None
        for part, (label, thickness, local_x, yields) in sorted(case["parts"].items()):
            faces = component(g, MESH_FACE, "Mesh Face", label + " faces", DATA_, CELLS)
            faces.PersistentData.Clear()
            for p, c in cells:
                if p == part:
                    faces.PersistentData.Append(GH_MeshFace(MeshFace(*c) if len(c) == 4 else MeshFace(c[0], c[1], c[2])))
            mesh = component(g, CONSTRUCT_MESH, "Construct Mesh", label, MESHES, CELLS)
            wire(inp(mesh, 0), node_points)
            wire(inp(mesh, 1), faces)
            if yields:
                section = component(g, LAYERED_SHELL, "Layered Shell", label, MATERIAL, CELLS)
                wire(inp(section, 1), out(steel))
                number(inp(section, 2), *([thickness / 10.0] * 10))
            else:
                if elastic is None:
                    elastic = component(g, ND_MATERIAL, "nD", "Steel, elastic", MATERIAL, CELLS)
                    number(inp(elastic, 1), 210000000)
                    number(inp(elastic, 2), 80770000)
                    number(inp(elastic, 3), 0.3)
                    parts_2.append(elastic)
                section = component(g, PLATE_FIBER, "Plate Fiber Section", label, MATERIAL, CELLS)
                number(inp(section, 1), thickness)
                wire(inp(section, 2), out(elastic))
            words(inp(section, 0), label)
            element = component(g, ASD_SHELL, "ASD Shell", label, ELEMENTS, CELLS)
            wire(inp(element, 0), out(mesh))
            wire(inp(element, 1), out(section))
            vector(inp(element, 3), *local_x)
            elements.append(element)
            parts_1 += [faces, mesh]
            parts_2 += [section, element]
        model_nodes = node_points
    else:
        corner_numbers = component(g, INTEGER, "Integer", "Tetrahedra", DATA_, CELLS)
        corner_numbers.PersistentData.Clear()
        for i, tet in enumerate(cells):
            for n in tet:
                corner_numbers.PersistentData.Append(GH_Integer(n), GH_Path(i))
        faces = component(g, MESH_FACE, "Mesh Face", "Four faces", DATA_, CELLS)
        faces.PersistentData.Clear()
        for a, b, c in ((0, 1, 2), (0, 3, 1), (1, 3, 2), (2, 3, 0)):
            faces.PersistentData.Append(GH_MeshFace(MeshFace(a, b, c)))
        corners = component(g, LIST_ITEM, "List Item", "Corners", CORNERS, CELLS)
        wire(inp(corners, 0), node_points)
        wire(inp(corners, 1), corner_numbers)
        tet_meshes = component(g, CONSTRUCT_MESH, "Construct Mesh", "Tetrahedra", MESHES, CELLS)
        wire(inp(tet_meshes, 0), out(corners))
        wire(inp(tet_meshes, 1), faces)
        ten = component(g, TEN_NODE, "Ten Node Tetrahedron", None, ELEMENTS, CELLS)
        wire(inp(ten, 0), out(tet_meshes))
        wire(inp(ten, 1), out(steel))
        elements.append(ten)
        parts_1 += [corner_numbers, faces, corners, tet_meshes]
        parts_2.append(ten)
        # Every element's ten nodes, the edge nodes with the corners, once each.
        all_nodes = component(g, POINT, "Point", "Element nodes", ELEMENTS, HELD)
        flat(wire(all_nodes, out(ten, 1)))
        model_nodes = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", COORDS, HELD)
        wire(inp(model_nodes, 0), all_nodes)
        number(inp(model_nodes, 1), 0.0001)
        parts_3 += [all_nodes, model_nodes]
        model_nodes = out(model_nodes)

    # 3 - Supports: each on the nodes an Expression of their coordinates picks, no node on two.
    coords = component(g, DECONSTRUCT_POINT, "Deconstruct", "x y z", COORDS, HELD2)
    wire(inp(coords, 0), model_nodes)
    held = toggle(g, "Held", True, UNIQUE, HELD)
    free = toggle(g, "Free", False, UNIQUE, HELD)
    parts_3 += [coords, held, free]
    x, y, z = out(coords, 0), out(coords, 1), out(coords, 2)
    supports = []
    if CASE == "tjoint":
        # Both ends of the chord, every node of the ring, as end plates on pins.
        made, s = held_by(g, model_nodes, "f(x)=(x<%.9g) Or (x>%.9g)" % (E, 1.8 - E), [x], "Chord ends", "XYZ", (held, free), HELD)
        parts_3 += made
        supports.append(s)
    elif CASE == "yieldlines":
        # On its four edges, held up and down; one corner held along X and Y, the next along Y.
        edge = "((x<{0}) Or (x>{1}) Or (y<{0}) Or (y>{1}))".format(E, 1.0 - E)
        made, s = held_by(g, model_nodes, "f(x,y)=%s And ((y>%.9g) Or ((x>%.9g) And (x<%.9g)))" % (edge, E, E, 1.0 - E),
                          [x, y], "Edges", "Z", (held, free), HELD)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x,y)=(x<%.9g) And (y<%.9g)" % (E, E), [x, y], "Corner", "XYZ", (held, free), HELD2)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x,y)=(x>%.9g) And (y<%.9g)" % (1.0 - E, E), [x, y], "Next corner", "YZ", (held, free), HELD3)
        parts_3 += made
        supports.append(s)
    elif CASE == "platehole":
        # In its own plane: every node held in Z; the left edge along X; its middle along Y too.
        made, s = held_by(g, model_nodes, "f(x,y)=(x<%.9g) And (Abs(y-0.1)>%.9g)" % (E, E), [x, y], "Left edge", "XZ", (held, free), HELD)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x,y)=(x<%.9g) And (Abs(y-0.1)<%.9g)" % (E, E), [x, y], "Its middle", "XYZ", (held, free), HELD2)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x)=x>%.9g" % E, [x], "In its plane", "Z", (held, free), HELD3)
        parts_3 += made
        supports.append(s)
    else:
        # Plane strain, every node held along the pipe; the two cut faces are planes of symmetry.
        made, s = held_by(g, model_nodes, "f(x)=x<%.9g" % E, [x], "Face X = 0", "XZ", (held, free), HELD)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x,y)=(y<%.9g) And (x>%.9g)" % (E, E), [x, y], "Face Y = 0", "YZ", (held, free), HELD2)
        parts_3 += made
        supports.append(s)
        made, s = held_by(g, model_nodes, "f(x,y)=(x>%.9g) And (y>%.9g)" % (E, E), [x, y], "Along the pipe", "Z", (held, free), HELD3)
        parts_3 += made
        supports.append(s)

    # 4 - The load, growing with the steps on a Linear Time Series.
    total = text(g, case["load_name"], "%g" % case["load"], UNIQUE, LOADED)
    parts_4.append(total)
    if CASE == "tjoint":
        # On the brace's top, shared evenly among its ring of nodes.
        on_top = expression(g, "f(z)=z>%.9g" % (0.6 - E), [z], "On the brace's top", TEST_, LOADED)
        top = component(g, CULL_PATTERN, "Cull Pattern", "Brace top", KEEP, LOADED)
        wire(inp(top, 0), model_nodes)
        wire(inp(top, 1), out(on_top))
        count = component(g, LIST_LENGTH, "List Length", "How many", UNIQUE, LOADED)
        wire(inp(count, 0), out(top))
        share = expression(g, "f(p,n)=-p/n", [total, out(count)], "Each node's share", UNIQUE, LOADED)
        force = component(g, MULTIPLY, "Multiply", "Down", UNIQUE, LOADED)
        vector(inp(force, 0), 0.0, 0.0, 1.0)
        wire(inp(force, 1), out(share))
        loads = component(g, POINT_LOAD, "Point Load", "Brace top", LOADS, LOADED)
        wire(inp(loads, 0), out(top))
        wire(inp(loads, 1), out(force))
        parts_4 += [on_top, top, count, share, force, loads]
    elif CASE == "yieldlines":
        # A pressure on every shell, down.
        force = component(g, MULTIPLY, "Multiply", "Down", UNIQUE, LOADED)
        vector(inp(force, 0), 0.0, 0.0, -1.0)
        wire(inp(force, 1), total)
        loads = component(g, MESH_LOAD, "Mesh Load", "Pressure", LOADS, LOADED)
        flat(wire(inp(loads, 0), *[out(e) for e in elements]))
        wire(inp(loads, 1), out(force))
        parts_4 += [force, loads]
    elif CASE == "platehole":
        # A uniform pull on the right edge: each node its share of the edge's length.
        where, shares = plate_edge_shares(nodes)
        edge = component(g, POINT, "Point", "Pulled edge", KEEP, LOADED)
        points(edge, where)
        unit = component(g, VECTOR, "Vector", "Each node's share", KEEP, LOADED)
        vectors(unit, [(s, 0.0, 0.0) for s in shares])
        force = component(g, MULTIPLY, "Multiply", "Pull", UNIQUE, LOADED)
        wire(inp(force, 0), unit)
        wire(inp(force, 1), total)
        loads = component(g, POINT_LOAD, "Point Load", "Pulled edge", LOADS, LOADED)
        wire(inp(loads, 0), edge)
        wire(inp(loads, 1), out(force))
        parts_4 += [edge, unit, force, loads]
    else:
        # The pressure on the bore, as the forces a six node triangle shares it out as.
        where, per = bore_forces(nodes, cells)
        bore = component(g, POINT, "Point", "Bore's edge nodes", KEEP, LOADED)
        points(bore, where)
        unit = component(g, VECTOR, "Vector", "Per kN/m2", KEEP, LOADED)
        vectors(unit, [(fx, fy, 0.0) for fx, fy in per])
        force = component(g, MULTIPLY, "Multiply", "Outwards", UNIQUE, LOADED)
        wire(inp(force, 0), unit)
        wire(inp(force, 1), total)
        loads = component(g, POINT_LOAD, "Point Load", "Bore", LOADS, LOADED)
        wire(inp(loads, 0), bore)
        wire(inp(loads, 1), out(force))
        parts_4 += [bore, unit, force, loads]
    linear = component(g, LINEAR_SERIES, "Linear Time Series", "Grows with the steps", UNIQUE, LOADED)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, LOADS, LOADED)
    wire(inp(pattern, 0), out(linear))
    flat(wire(inp(pattern, 1), out(loads)))
    parts_4 += [linear, pattern]

    # At positive canvas coordinates, below the note: GenerateHiResImage cuts a negative origin.
    right = g.arrange(300, 760, stages=(NAMES, COORDS, LOADS))

    # 5 - Assemble: a tolerance well under the distance between the nodes.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), *[out(e) for e in elements]))
    flat(wire(inp(assemble, 1), *[out(s) for s in supports]))
    flat(wire(inp(assemble, 2), out(pattern)))
    number(inp(assemble, 5), 0.0005)
    view = component(m, MODEL_VIEW, "Model View", "Model at rest", VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    # 6 - Pushed in steps: Newton, 20 steps of Load Control of 5% of the load, each solved to a
    # displacement increment of 1e-8 m (1e-6 for the T-joint) in at most 50 iterations.
    test = component(m, TEST, "Test", None, MODEL, ANALYSED)
    switch_unit(test, NORM_DISP_INCR)
    number(inp(test, 0), case.get("tolerance", 1e-8))
    inp(test, 1).PersistentData.Clear()
    inp(test, 1).PersistentData.Append(GH_Integer(50))
    integrator = component(m, INTEGRATOR, "Integrator", "Load Control", MODEL, ANALYSED)
    switch_unit(integrator, LOAD_CONTROL)
    number(inp(integrator, 0), LAMBDA)
    steps = component(m, ANALYSIS_STEP, "Analysis Step", "20 steps", MODEL, ANALYSED)
    inp(steps, 0).PersistentData.Clear()
    inp(steps, 0).PersistentData.Append(GH_Integer(STEPS))
    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    wire(inp(settings, 4), out(test))
    words(inp(settings, 5), "Newton")
    wire(inp(settings, 6), out(integrator))
    wire(inp(settings, 8), out(steps))
    run = component(m, RUN_ANALYSIS, "Run Analysis", None, MODEL, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))

    # 7 - The Von Mises stress, step by step, in Alpaca4d's colours: eleven shades from light blue
    # through blue to purple for the elastic range, and orange at the top, where the steel yields.
    step = slider(m, "Step", 0, STEPS - 1, STEPS - 1, 0, VIEW, ANALYSED)
    results = component(m, MODEL_VIEW, "Model View", "Steel pushed", VIEW, ANALYSED)
    wire(inp(results, 0), out(run))
    wire(inp(results, 1), step)
    widget(results, "_ckExtruded").Active = False
    widget(results, "_ckReleases").Active = False
    menu_setting(results, "_ckBands", "Active", True)
    menu_setting(results, "_ckDeformed", "Active", True)
    menu_setting(results, "_slDeformScale", "MaxValue", max(50.0, case["deform"] * 2))
    menu_setting(results, "_slDeformScale", "Value", case["deform"])
    show_result(results, SHELL_STRESS if shell else BRICK_STRESS, VON_MISES_SHELL if shell else VON_MISES_BRICK)
    colours(inp(results, 3), ["#E4EEFE", "#4288F7", "#9E35DA"], BANDS - 1, FULL_ORANGE)
    stress_range = text(m, "Range [MPa]", "0 To %g" % FY, VIEW, ANALYSED, width=110.0)
    wire(inp(results, 4), stress_range)
    legend = component(m, LEGEND, "Legend", "Steel pushed legend", SHOWN, ANALYSED)
    legend_title = text(m, "Title", "Von Mises [MPa], orange: yielding", SHOWN, ANALYSED, width=200.0)
    wire(inp(legend, 0), legend_title)
    wire(inp(legend, 1), out(results, 3))
    wire(inp(legend, 2), out(results, 4))
    info = readout(m, "What is drawn", out(results, 2), SHOWN, ANALYSED, width=300.0, height=40.0)
    log = readout(m, "OpenSees log", out(run, 1), SHOWN, ANALYSED, width=300.0, height=140.0)

    # 8 - The load-displacement curve, from every step: how far the node the case follows moves,
    # and the largest Von Mises stress, which tells the step the steel starts to yield.
    displacements = component(m, NODAL_DISPLACEMENTS, "Nodal Displacements", None, MODEL, CURVE_)
    wire(inp(displacements, 0), out(run))
    booleans(inp(displacements, 1), True)
    stresses = component(m, SHELL_STRESSES if shell else BRICK_STRESSES_, "Shell Stresses" if shell else "Brick Stresses", None, MODEL, CURVE_)
    wire(inp(stresses, 0), out(run))
    booleans(inp(stresses, 1), True)
    hints = emit(PYTHON, "GhPython").GetType().Assembly
    def hint(name):
        return System.Activator.CreateInstance(hints.GetType("GhPython.Component." + name))
    number_hint = hint("NewFloatHint")
    tree, item = GH_ParamAccess.tree, GH_ParamAccess.item
    curve = python(m, "Load-displacement", CURVE, "load_displacement",
                   [("Positions", GH_Point3dHint(), tree), ("Displacements", GH_Vector3dHint(), tree),
                    ("VonMises", number_hint, tree), ("Load", number_hint, item), ("Lambda", number_hint, item),
                    ("Fy", number_hint, item), ("Origin", GH_Point3dHint(), item), ("XAxis", GH_Vector3dHint(), item),
                    ("YAxis", GH_Vector3dHint(), item), ("Width", number_hint, item),
                    ("Height", number_hint, item), ("Point", GH_Point3dHint(), item), ("Direction", GH_Vector3dHint(), item),
                    ("Unit", GH_StringHint_CS(), item), ("Scale", number_hint, item)],
                   ["Elastic", "Yielding", "Axes", "Summary", "Deflection", "Load", "YieldDeflection", "YieldLoad"],
                   "perforated-beam-load-deflection.png", VIEW, CURVE_)
    wire(inp(curve, 0), out(displacements, 0))
    wire(inp(curve, 1), out(displacements, 1))
    wire(inp(curve, 2), out(stresses, 5 if shell else 6))
    wire(inp(curve, 3), total)
    number(inp(curve, 4), LAMBDA)
    number(inp(curve, 5), FY * 1000.0)
    origin, x_axis, y_axis = case["plane"]
    point(inp(curve, 6), *origin)
    vector(inp(curve, 7), *x_axis)
    vector(inp(curve, 8), *y_axis)
    number(inp(curve, 9), case["width"])
    number(inp(curve, 10), case["height"])
    point(inp(curve, 11), *case["follow"])
    vector(inp(curve, 12), *case["along"])
    words(inp(curve, 13), case["unit"])
    number(inp(curve, 14), case["scale"])
    # NO_PREVIEW leaves the Custom Previews out, so that make.sh can write a definition to check -
    # Custom Preview takes Rhino's display code to be made. The definitions handed out have them.
    previews = []
    if not System.Environment.GetEnvironmentVariable("NO_PREVIEW"):
        for nickname, output, colour_ in (("Elastic", 1, LIGHT_BLUE), ("Yielding", 2, FULL_ORANGE), ("Axes", 3, DARK_TECH)):
            p = component(m, CUSTOM_PREVIEW, "Custom Preview", nickname, SHOWN, CURVE_)
            wire(inp(p, 0), out(curve, output))
            shader(inp(p, 1), colour_)
            previews.append(p)
    summary = readout(m, "Summary", out(curve, 4), SHOWN, CURVE_, width=300.0, height=80.0)

    m.arrange(right + 160, 760)

    group(doc, "1 - " + case["title"] + ": the mesh, as gmsh wrote it", parts_1, ORANGE)
    group(doc, "2 - S355 that yields - J2Plasticity", parts_2, PURPLE)
    group(doc, "3 - Supports", parts_3, GREEN)
    group(doc, "4 - The load, growing with the steps", parts_4, RED)
    group(doc, "5 - Assemble Model", [assemble, view], BLUE)
    group(doc, "6 - Pushed in 20 steps, past the load it starts to yield at", [test, integrator, steps, settings, run], GREY)
    group(doc, "7 - Von Mises, step by step: orange where the steel yields", [step, results, stress_range, legend, legend_title, info, log], ORANGE)
    group(doc, "8 - The load-displacement curve: blue while elastic, orange after",
          [displacements, stresses, curve, summary] + previews, BLUE)

    # Only the Model View after Run Analysis, its legend and the curve draw; the rest stays out of
    # the viewport. Show the first Model View's preview to see the elements, supports and loads.
    shown = set(o.InstanceGuid for o in [results, legend] + previews)
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in shown:
            obj.Hidden = True

    note(doc, case["title"], NOTES[CASE].format(cells=len(cells), nodes=len(nodes)), 300, 300, 620, 380)


NOTES = {
    "tjoint": (
        "A T-joint of two steel tubes, pushed past the load at which it starts to yield.\n\n"
        "A brace 150 x 8 mm stands on a chord 300 x 10 mm, 1.8 m long, both S355, as {cells} ASD Shells "
        "through the middle of their walls. The chord's wall runs on inside the brace's foot, as a welded "
        "joint's does.\n\n"
        "1. The shells as gmsh wrote them: the nodes, and the corners of each face, by part.\n"
        "2. J2Plasticity, in a Layered Shell of ten layers for each tube; the brace's top 60 mm stays elastic "
        "and 30 mm thick, as the end plate the load comes in through makes it. Each part its own Local X, "
        "along its tube.\n"
        "3. The chord's two ends held, every node of the ring.\n"
        "4. 640 kN down the brace, shared among the nodes of its top, on a Linear Time Series.\n"
        "6. Newton and 20 steps of Load Control.\n"
        "7. The chord's wall yields first at the brace's saddles and crowns, then round its whole foot.\n"
        "8. EN 1993-1-8 gives this joint 388 kN for chord face failure; its limit is a deformation, 3% of "
        "the chord's diameter, 9 mm."),
    "yieldlines": (
        "A steel plate under pressure, pushed past the load at which it starts to yield.\n\n"
        "A square S355 plate 1 x 1 m and 25 mm thick, simply supported on its four edges, as {cells} ASD "
        "Shells.\n\n"
        "1. The shells: 40 x 40 squares.\n"
        "2. J2Plasticity, in a Layered Shell of ten layers.\n"
        "3. Its edges held up and down, and its corners held down; one corner along X and Y, the next along Y.\n"
        "4. 1 300 kN/m2 on every shell, with Mesh Load, on a Linear Time Series.\n"
        "6. Newton and 20 steps of Load Control.\n"
        "7. It yields first at its middle, then out along its diagonals: the yield lines of yield-line "
        "theory, which gives the plate 24 mp / L2 = 1 331 kN/m2, mp = Fy t2 / 4.\n"
        "8. The curve flattens as it reaches it. Membrane action, which would carry more once the plate "
        "sags, is left out: the analysis is of small deflections."),
    "platehole": (
        "A steel plate with a hole, pulled past the load at which it starts to yield.\n\n"
        "An S355 plate 400 x 200 mm and 10 mm thick with a hole 60 mm across, pulled along its length, as "
        "{cells} ASD Shells, finer round the hole.\n\n"
        "1. The shells as gmsh wrote them, a node at the middle of each short edge.\n"
        "2. J2Plasticity, in a Layered Shell of ten layers.\n"
        "3. In its own plane: every node held in Z, the left edge along X, its middle along Y too.\n"
        "4. 500 kN on the right edge, each node its share of the edge's length, on a Linear Time Series.\n"
        "6. Newton and 20 steps of Load Control.\n"
        "7. The steel yields first at the edge of the hole, where the stress is 2.3 times the average across "
        "the net section (Heywood: Kt = 2 + (1 - d/W)3), at about 212 kN; then the yielding crosses the "
        "net section.\n"
        "8. It levels off at Fy times the net section, 497 kN, a little over."),
    "pipe": (
        "A thick-walled steel pipe under pressure, pushed past the pressure at which it starts to yield.\n\n"
        "An S355 pipe, bore 200 mm and outside 400 mm, under a pressure inside it: a quarter of a slice "
        "20 mm long, in {cells} Ten Node Tetrahedra, finer at the bore.\n\n"
        "1. The tetrahedra as gmsh wrote them.\n"
        "2. J2Plasticity.\n"
        "3. Plane strain, as in a long pipe: every node held along it. The two cut faces are planes of "
        "symmetry, held across.\n"
        "4. 280 MPa on the bore, as the forces a six node triangle shares a pressure out as, on a Linear "
        "Time Series.\n"
        "6. Newton and 20 steps of Load Control.\n"
        "7. The steel yields first at the bore, at Fy / sqrt(3) (1 - a2 / b2) = 154 MPa, then outwards, a "
        "ring at a time.\n"
        "8. It levels off at 2 Fy / sqrt(3) ln(b / a) = 284 MPa, when the whole wall has yielded."),
}


def main():
    doc = open_canvas()
    build(doc)
    path = System.Environment.GetEnvironmentVariable("SAVE")
    if path:
        Grasshopper.Kernel.GH_DocumentIO(doc).SaveQuiet(path)
        print("Saved " + path)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(CASES[CASE]["title"] + " built.")


if __name__ == "__main__":
    main()
