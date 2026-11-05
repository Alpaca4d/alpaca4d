#! python3
# The topology examples of a node where members meet, in space: a space-truss node and a beam-column joint,
# each found by all three of Alpaca4d's topology optimisations side by side - Topology Optimisation (SIMP),
# BESO Topology and Multi-Material Topology.
#
# make.sh, next to it, writes the two definitions straight from this script, without Rhino:
#
#     NODE_CASE=truss ./make.sh TopologyNodes.py TopologyTrussNode.gh
#     NODE_CASE=joint ./make.sh TopologyNodes.py TopologyBeamColumnJoint.gh
#
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript - building the case CASE says.
#
# The design domain is a cube of steel bricks, 1 m a side. The members arriving at it are stubs of bricks,
# 0.2 m long, kept solid whatever the optimisation finds, and loaded or held at their ends: a member's force
# shared evenly over the nodes of its end, a beam's hogging moment as its top half pulled out and its bottom
# half pushed in. Bricks per 10 cm sets how fine, the same every way: 1, 2 or 3, so that every stub's edges
# fall on the grid. The truss node is TopologyMethods.cs's TrussNode, the joint its BeamColumnJoint.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_String
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
LIST_LENGTH = "1817FD29-20AE-4503-B542-F0FB651E67D7"        # Sets > List > List Length
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
VECTOR_XYZ = "56B92EAB-D121-43F7-94D3-6CD8F0DDEAD8"         # Vector > Vector > Vector XYZ
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
TEXT_TAG = "5A41528B-12B9-40DC-A3F2-842034D267C4"           # Display > Dimensions > Text Tag 3D
XZ_PLANE = "8CC3A196-F6A0-49EA-9ED9-0CB343A3AE64"           # Vector > Plane > XZ Plane

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
TOPOLOGY = "8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93"           # Topology Optimisation
BESO = "3B7E5C21-9A4D-4F68-B2E1-7C5D0A8F6E14"               # BESO Topology
MULTI_TOPOLOGY = "6D2A9E47-1C8B-4E35-A9F0-58B3C7D16A22"     # Multi-Material Topology
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
VIEW_RESULTS = "4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3"       # View Results
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend

# The three components' inputs and outputs, by position. Topology Optimisation registers Step and
# AlpacaModel last, after the Settings menu's; the other two put Step after it.
T_MODEL, T_KEEP, T_VOLUME_FRACTION, T_ITERATIONS, T_RUN = range(5)
T_STEP, B_STEP = 12, 11
T_SURFACE, T_SOLID, T_DENSITY, T_ELEMENT, T_COMPLIANCE, T_VOLUME, T_CONVERGED, T_HISTORY, T_LOG, T_STEP_OUT, T_ALPACA_MODEL = range(11)
M_MODEL, M_KEEP, M_MATERIALS, M_FRACTIONS, M_ITERATIONS, M_RUN = range(6)
M_STEP = 14
(M_SURFACE, M_COLOUR, M_SOLID, M_MATERIAL, M_DENSITY, M_ELEMENT, M_COMPLIANCE, M_VOLUME, M_CONVERGED, M_HISTORY,
 M_LOG, M_STEP_OUT, M_ALPACA_MODEL) = range(13)
# View Results' Result menu: Displacement, Beam forces, Beam stresses, Shell forces, Shell stresses,
# Brick stresses, Reactions; and the brick stresses: s11, s22, s33, s12, s23, s13, Von Mises.
BRICK_STRESSES, VON_MISES = 5, 6

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)



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


def integer(param, value):
    """Types a whole number into an unwired input that takes only those, such as Series' Count."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


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


def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a material's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
    return p


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def vector(grid, guid, nickname, factor, col, row=None, gap=0):
    """A unit vector along an axis, times factor - a number, or a source to take it from."""
    v = component(grid, guid, "The Unit X, Y and Z components", nickname, col, row, gap)
    if isinstance(factor, (int, float)):
        number(inp(v, 0), factor)
    else:
        wire(inp(v, 0), factor)
    return v


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


def dropdown(obj, field):
    """One of an Alpaca4d component's menu dropdowns, which it keeps to itself."""
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    return obj.GetType().GetField(field, flags).GetValue(obj)


def show_result(results, family, component):
    """Picks View Results' Result and Component as the menu would: the Component list follows the
    Result, so it is filled for it before a component is picked from it."""
    dropdown(results, "_ddFamily").Value = family
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    results.GetType().GetMethod("SyncComponentList", flags).Invoke(results, None)
    dropdown(results, "_ddComponent").Value = component



# Columns, in the order the data flows...
INPUTS, NUMBERS, SERIES_, POINTS, MESHES, BRICKS_, NAMES, ELEMENTS, LOADS, PATTERN = range(10)
# ...and rows, one per part of the model.
NODE, MEMBERS, MATERIALS, SUPPORTS, END_LOADS = range(5)

# The model, its optimisations and their designs, in a grid of their own to the right.
MODEL, OPTIMISE, RESULTS, PREVIEW, SHOWN_COL = range(5)
ASSEMBLED, SIMP_RUN, BESO_RUN, MULTI_RUN, ANALYSED = range(5)
SHOWN_ROW = {"SIMP": SIMP_RUN, "BESO": BESO_RUN, "Multi-material": MULTI_RUN}


# ---------------------------------------------------------------- the two nodes

CASE = System.Environment.GetEnvironmentVariable("NODE_CASE") or "truss"

# A box of bricks: (name, x0, x1, y0, y1, z0, z1), m. A face: (name, A, B, C, force), the rectangle from A to B
# and on to C, its force in kN shared evenly over its nodes. Every edge is a multiple of 0.1 m.
TRUSS = {
    "title": "Space-truss node",
    "members": [
        ("Chord +x", 1.0, 1.2, 0.4, 0.6, 0.4, 0.6), ("Chord -x", -0.2, 0.0, 0.4, 0.6, 0.4, 0.6),
        ("Chord +y", 0.4, 0.6, 1.0, 1.2, 0.4, 0.6), ("Chord -y", 0.4, 0.6, -0.2, 0.0, 0.4, 0.6),
        ("Post", 0.4, 0.6, 0.4, 0.6, 1.0, 1.2),
        ("Diagonal", 0.0, 0.2, 0.0, 0.2, -0.2, 0.0), ("Diagonal", 0.8, 1.0, 0.0, 0.2, -0.2, 0.0),
        ("Diagonal", 0.0, 0.2, 0.8, 1.0, -0.2, 0.0), ("Diagonal", 0.8, 1.0, 0.8, 1.0, -0.2, 0.0),
    ],
    "loads": [
        ("Chord +x, 450 kN in tension", (1.2, 0.4, 0.4), (1.2, 0.6, 0.4), (1.2, 0.6, 0.6), (450, 0, 0)),
        ("Chord -x, 250 kN in tension", (-0.2, 0.4, 0.4), (-0.2, 0.6, 0.4), (-0.2, 0.6, 0.6), (-250, 0, 0)),
        ("Chord +y, 300 kN in compression", (0.4, 1.2, 0.4), (0.6, 1.2, 0.4), (0.6, 1.2, 0.6), (0, -300, 0)),
        ("Chord -y, 150 kN in compression", (0.4, -0.2, 0.4), (0.6, -0.2, 0.4), (0.6, -0.2, 0.6), (0, 150, 0)),
        ("Post, 600 kN down", (0.4, 0.4, 1.2), (0.6, 0.4, 1.2), (0.6, 0.6, 1.2), (0, 0, -600)),
    ],
    "supports": [
        ("Diagonal", (0.0, 0.0, -0.2), (0.2, 0.0, -0.2), (0.2, 0.2, -0.2)),
        ("Diagonal", (0.8, 0.0, -0.2), (1.0, 0.0, -0.2), (1.0, 0.2, -0.2)),
        ("Diagonal", (0.0, 0.8, -0.2), (0.2, 0.8, -0.2), (0.2, 1.0, -0.2)),
        ("Diagonal", (0.8, 0.8, -0.2), (1.0, 0.8, -0.2), (1.0, 1.0, -0.2)),
    ],
    "fraction": 0.12,
    "second": ("Aluminium", "70000000", "26300000", "0.33", "2700"),
    "shares": (0.04, 0.08),
    "loads_group": "4 - Diagonals held at their feet; chords pulled (x) and pushed (y), unequally; the post loaded",
}

def beam_end(name, x0, y0, x1, y1, normal, shear, moment):
    """A beam end 0.4 m deep, from 0.4 to 0.8 m up, between (x0, y0) and (x1, y1): its shear down over the whole end,
    and its hogging moment as the top half pulled out along normal and the bottom half pushed in, each by 2M/d."""
    f = 2.0 * moment / 0.4
    out = (normal[0] * f, normal[1] * f, 0.0)
    back = (-out[0], -out[1], 0.0)
    return [
        (name + ", " + str(shear) + " kN shear", (x0, y0, 0.4), (x1, y1, 0.4), (x1, y1, 0.8), (0, 0, -shear)),
        (name + ", " + str(moment) + " kNm, top", (x0, y0, 0.6), (x1, y1, 0.6), (x1, y1, 0.8), out),
        (name + ", " + str(moment) + " kNm, bottom", (x0, y0, 0.4), (x1, y1, 0.4), (x1, y1, 0.6), back),
    ]

JOINT = {
    "title": "Beam-column joint",
    "members": [
        ("Column below", 0.3, 0.7, 0.3, 0.7, -0.2, 0.0), ("Column above", 0.3, 0.7, 0.3, 0.7, 1.0, 1.2),
        ("Beam +x", 1.0, 1.2, 0.4, 0.6, 0.4, 0.8), ("Beam -x", -0.2, 0.0, 0.4, 0.6, 0.4, 0.8),
        ("Beam +y", 0.4, 0.6, 1.0, 1.2, 0.4, 0.8), ("Beam -y", 0.4, 0.6, -0.2, 0.0, 0.4, 0.8),
    ],
    "loads": [("Column, 1500 kN down", (0.3, 0.3, 1.2), (0.7, 0.3, 1.2), (0.7, 0.7, 1.2), (0, 0, -1500))]
             + beam_end("Beam +x", 1.2, 0.4, 1.2, 0.6, (1, 0), 150, 150)
             + beam_end("Beam -x", -0.2, 0.4, -0.2, 0.6, (-1, 0), 150, 80)
             + beam_end("Beam +y", 0.4, 1.2, 0.6, 1.2, (0, 1), 100, 100)
             + beam_end("Beam -y", 0.4, -0.2, 0.6, -0.2, (0, -1), 100, 100),
    "supports": [("Column foot", (0.3, 0.3, -0.2), (0.7, 0.3, -0.2), (0.7, 0.7, -0.2))],
    "fraction": 0.2,
    "second": ("Concrete C30/37", "33000000", "13750000", "0.2", "2500"),
    "shares": (0.06, 0.14),
    "loads_group": "4 - The column held at its foot and loaded at its head; each beam's shear and hogging moment",
}

# Where each method's design is drawn, side by side along x, 2 m apart: Multi-Material Topology draws its own, in
# its materials' colours, where the model is; the other two are moved to either side.
SHOWN = [("SIMP", -2.0), ("Multi-material", 0.0), ("BESO", 2.0)]


def length(a, b):
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def points(param, pts):
    """Types a list of points into an unwired input."""
    param.PersistentData.Clear()
    for p in pts:
        param.PersistentData.Append(GH_Point(Point3d(p[0], p[1], p[2])))
    return param


def graft(param):
    param.DataMapping = GH_DataMapping.Graft
    return param


def values(grid, name, nums, col, row=None, gap=0):
    """A panel of numbers, one a line and each an item of its own, to wire into an Expression."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText("\n".join("%g" % v for v in nums))
    p.Properties.Multiline = False
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 70.0, 20.0 + 15.0 * len(nums))
    return grid.put(p, col, row, gap)


def build(doc):
    case = TRUSS if CASE == "truss" else JOINT
    g = Grid(doc)

    # 1 - How fine: bricks per 10 cm, the same every way, so every stub's edges fall on the grid.
    fineness = slider(g, "Bricks per 10 cm", 1, 3, 2, 0, INPUTS)
    per_metre = expression(g, "f(s)=10*s", [fineness], "Bricks per metre", NUMBERS, NODE)
    layer = expression(g, "f(n)=1/n", [out(per_metre)], "Layer", NUMBERS, NODE, gap=10)

    # 2 - The node: a cube 1 m a side, its plan meshed once, copied up level by level, made into bricks.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Plan", POINTS, NODE)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), 1, 0, 0)
    point(inp(plan, 2), 1, 1, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, NODE)
    wire(inp(plan_mesh, 0), out(plan))
    faint(wire(inp(plan_mesh, 1), out(per_metre)))
    faint(wire(inp(plan_mesh, 2), out(per_metre)))
    levels_count = expression(g, "f(n)=n+1", [out(per_metre)], "Levels", NUMBERS, NODE, gap=10)
    levels = component(g, SERIES, "Series", "Levels", SERIES_, NODE)
    number(inp(levels, 0), 0.0)
    wire(inp(levels, 1), out(layer))
    wire(inp(levels, 2), out(levels_count))
    lift = vector(g, UNIT_Z, "Up to each level", out(levels), POINTS, NODE, gap=10)
    level_meshes = component(g, MOVE, "Move", "Levels", MESHES, NODE, gap=10)
    wire(inp(level_meshes, 0), out(plan_mesh))
    wire(inp(level_meshes, 1), out(lift))
    node_bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, NODE)
    wire(inp(node_bricks, 0), out(level_meshes))

    # 3 - The members: a stub of bricks each, all of them through one chain, each its own branch.
    members = case["members"]
    stub_plans = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Stub plans", POINTS, MEMBERS)
    points(inp(stub_plans, 0), [(m[1], m[3], m[5]) for m in members])
    points(inp(stub_plans, 1), [(m[2], m[3], m[5]) for m in members])
    points(inp(stub_plans, 2), [(m[2], m[4], m[5]) for m in members])
    sizes = [values(g, "Along x [m]", [m[2] - m[1] for m in members], INPUTS, MEMBERS),
             values(g, "Along y [m]", [m[4] - m[3] for m in members], INPUTS, MEMBERS, gap=10),
             values(g, "Along z [m]", [m[6] - m[5] for m in members], INPUTS, MEMBERS, gap=10)]
    along = expression(g, "f(l,n)=l*n", [sizes[0], out(per_metre)], "Bricks along", NUMBERS, MEMBERS)
    across = expression(g, "f(l,n)=l*n", [sizes[1], out(per_metre)], "Bricks across", NUMBERS, MEMBERS, gap=10)
    high = expression(g, "f(l,n)=l*n+1", [sizes[2], out(per_metre)], "Levels", NUMBERS, MEMBERS, gap=10)
    stub_meshes = component(g, MESH_PLANE, "Mesh Plane", "Stub plans", MESHES, MEMBERS)
    wire(inp(stub_meshes, 0), out(stub_plans))
    wire(inp(stub_meshes, 1), out(along))
    wire(inp(stub_meshes, 2), out(across))
    stub_levels = component(g, SERIES, "Series", "Stub levels", SERIES_, MEMBERS)
    number(inp(stub_levels, 0), 0.0)
    faint(wire(inp(stub_levels, 1), out(layer)))
    wire(inp(stub_levels, 2), out(high))
    stub_lift = vector(g, UNIT_Z, "Up to each level", out(stub_levels), POINTS, MEMBERS, gap=10)
    stub_moved = component(g, MOVE, "Move", "Stub levels", MESHES, MEMBERS, gap=10)
    graft(wire(inp(stub_moved, 0), out(stub_meshes)))
    wire(inp(stub_moved, 1), out(stub_lift))
    stub_bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, MEMBERS)
    wire(inp(stub_bricks, 0), out(stub_moved))

    # Steel, the bricks of the node and of the members, and the second material Multi-Material Topology may use.
    steel_values = [text(g, "Steel", "S355", NAMES, NODE),
                    text(g, "E [kN/m2]", "210000000", NAMES, NODE),
                    text(g, "G [kN/m2]", "80769231", NAMES, NODE),
                    text(g, "Poisson", "0.3", NAMES, NODE),
                    text(g, "Density [kg/m3]", "7850", NAMES, NODE)]
    steel = component(g, ND_MATERIAL, "nD", "Steel", ELEMENTS, NODE)
    for i, source in enumerate(steel_values):
        wire(inp(steel, i), source)
    second = case["second"]
    second_values = [text(g, second[0], second[0], NAMES, MATERIALS),
                     text(g, "E [kN/m2]", second[1], NAMES, MATERIALS),
                     text(g, "G [kN/m2]", second[2], NAMES, MATERIALS),
                     text(g, "Poisson", second[3], NAMES, MATERIALS),
                     text(g, "Density [kg/m3]", second[4], NAMES, MATERIALS)]
    other = component(g, ND_MATERIAL, "nD", second[0].split(" ")[0], ELEMENTS, MATERIALS)
    for i, source in enumerate(second_values):
        wire(inp(other, i), source)
    node_name = text(g, "ElementId", "Node", ELEMENTS, NODE, gap=10)
    node = component(g, BRICK, "SSP Brick", "Node", LOADS, NODE)
    wire(inp(node, 0), out(node_bricks))
    wire(inp(node, 1), out(steel))
    wire(inp(node, 3), node_name)
    member_name = text(g, "ElementId", "Member", ELEMENTS, MEMBERS)
    member = component(g, BRICK, "SSP Brick", "Members", LOADS, MEMBERS)
    flat(wire(inp(member, 0), out(stub_bricks)))
    faint(wire(inp(member, 1), out(steel)))
    wire(inp(member, 3), member_name)

    # 4 - Supports and loads at the members' ends, each end its own branch: its nodes from a Mesh Plane on it.
    def faces(row, table, name):
        rect = component(g, RECTANGLE_3PT, "Rectangle 3Pt", name, POINTS, row)
        points(inp(rect, 0), [f[1] for f in table])
        points(inp(rect, 1), [f[2] for f in table])
        points(inp(rect, 2), [f[3] for f in table])
        lu = values(g, "Width [m]", [length(f[1], f[2]) for f in table], INPUTS, row)
        lv = values(g, "Height [m]", [length(f[2], f[3]) for f in table], INPUTS, row, gap=10)
        u = expression(g, "f(l,n)=l*n", [lu, out(per_metre)], "Along", NUMBERS, row)
        v = expression(g, "f(l,n)=l*n", [lv, out(per_metre)], "Across", NUMBERS, row, gap=10)
        mesh = component(g, MESH_PLANE, "Mesh Plane", name, MESHES, row)
        wire(inp(mesh, 0), out(rect))
        wire(inp(mesh, 1), out(u))
        wire(inp(mesh, 2), out(v))
        nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", name + " nodes", BRICKS_, row)
        wire(inp(nodes, 0), out(mesh))
        return [rect, lu, lv, u, v, mesh, nodes], nodes

    held_parts, held_nodes = faces(SUPPORTS, case["supports"], "Held ends")
    held = toggle(g, "Held", True, NAMES, SUPPORTS)
    free = toggle(g, "Free", False, NAMES, SUPPORTS, gap=10)
    support = component(g, SUPPORT, "Support", "Held ends", ELEMENTS, SUPPORTS)
    flat(wire(inp(support, 0), out(held_nodes, 0)))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    loads = case["loads"]
    load_parts, load_nodes = faces(END_LOADS, loads, "Loaded ends")
    how_many = component(g, LIST_LENGTH, "List Length", "Nodes on each end", NAMES, END_LOADS)
    wire(inp(how_many, 0), out(load_nodes, 0))
    shares = []
    for axis, k in (("X", 0), ("Y", 1), ("Z", 2)):
        total = values(g, axis + " [kN]", [f[4][k] for f in loads], NAMES, END_LOADS, gap=10)
        e = expression(g, "f(F,c)=F/c", [total, out(how_many)], axis + " per node", ELEMENTS, END_LOADS, gap=10)
        graft(inp(e, 0))
        shares += [total, e]
    share = component(g, VECTOR_XYZ, "Vector XYZ", "Each node's share", LOADS, END_LOADS)
    for i in range(3):
        wire(inp(share, i), out(shares[2 * i + 1]))
    end_load = component(g, POINT_LOAD, "Point Load", "End loads", PATTERN, END_LOADS)
    wire(inp(end_load, 0), out(load_nodes, 0))
    wire(inp(end_load, 1), out(share, 0))
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, MEMBERS)
    flat(wire(inp(pattern, 1), out(end_load)))
    load_names = text(g, "Loads, in kN", "\n".join(f[0] + ": " + str(f[4]) for f in loads), INPUTS, END_LOADS, width=260.0)
    load_names.Attributes.Bounds = RectangleF(0.0, 0.0, 300.0, 20.0 + 14.0 * len(loads))

    right = g.arrange(300, 300, stages=(NUMBERS, NAMES, ELEMENTS))

    # 5 - Assembled, and 6 - the three optimisations, each with its own Run and Step.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(node), out(member)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    view = component(m, MODEL_VIEW, "Model View", None, OPTIMISE, ASSEMBLED)
    wire(inp(view, 0), out(assemble))
    view.Locked = True

    keep = text(m, "Keep", "Member", MODEL, SIMP_RUN)
    fraction = slider(m, "VolumeFraction", 0.05, 0.5, case["fraction"], 2, MODEL, SIMP_RUN, gap=10)
    runs, steps, components, readouts = [], [], [], []

    simp_iterations = slider(m, "Iterations", 10, 300, 200, 0, MODEL, SIMP_RUN, gap=10)
    simp_run = toggle(m, "Run SIMP", False, MODEL, SIMP_RUN, gap=10)
    simp_step = slider(m, "Step SIMP", 0, 300, 300, 0, MODEL, SIMP_RUN, gap=10)
    simp = emit(TOPOLOGY, "Topology Optimisation")
    simp.NickName = "SIMP"
    open_menus(simp)
    m.put(simp, OPTIMISE, SIMP_RUN)
    faint(wire(inp(simp, T_MODEL), out(assemble)))
    wire(inp(simp, T_KEEP), keep)
    wire(inp(simp, T_VOLUME_FRACTION), fraction)
    wire(inp(simp, T_ITERATIONS), simp_iterations)
    wire(inp(simp, T_RUN), simp_run)
    wire(inp(simp, T_STEP), simp_step)

    beso_iterations = slider(m, "Iterations", 10, 300, 300, 0, MODEL, BESO_RUN)
    beso_run = toggle(m, "Run BESO", False, MODEL, BESO_RUN, gap=10)
    beso_step = slider(m, "Step BESO", 0, 300, 300, 0, MODEL, BESO_RUN, gap=10)
    beso = emit(BESO, "BESO Topology")
    open_menus(beso)
    m.put(beso, OPTIMISE, BESO_RUN)
    faint(wire(inp(beso, T_MODEL), out(assemble)))
    faint(wire(inp(beso, T_KEEP), keep))
    faint(wire(inp(beso, T_VOLUME_FRACTION), fraction))
    wire(inp(beso, T_ITERATIONS), beso_iterations)
    wire(inp(beso, T_RUN), beso_run)
    wire(inp(beso, B_STEP), beso_step)

    steel_share = slider(m, "Steel share", 0.01, 0.3, case["shares"][0], 2, MODEL, MULTI_RUN)
    other_share = slider(m, second[0].split(" ")[0] + " share", 0.01, 0.5, case["shares"][1], 2, MODEL, MULTI_RUN, gap=10)
    multi_iterations = slider(m, "Iterations", 10, 500, 300, 0, MODEL, MULTI_RUN, gap=10)
    multi_run = toggle(m, "Run Multi-material", False, MODEL, MULTI_RUN, gap=10)
    multi_step = slider(m, "Step Multi-material", 0, 500, 500, 0, MODEL, MULTI_RUN, gap=10)
    multi = emit(MULTI_TOPOLOGY, "Multi-Material Topology")
    open_menus(multi)
    m.put(multi, OPTIMISE, MULTI_RUN)
    faint(wire(inp(multi, M_MODEL), out(assemble)))
    faint(wire(inp(multi, M_KEEP), keep))
    faint(wire(inp(multi, M_MATERIALS), out(steel), out(other)))
    wire(inp(multi, M_FRACTIONS), steel_share, other_share)
    wire(inp(multi, M_ITERATIONS), multi_iterations)
    wire(inp(multi, M_RUN), multi_run)
    wire(inp(multi, M_STEP), multi_step)

    readouts = [
        readout(m, "Compliance SIMP", out(simp, T_COMPLIANCE), RESULTS, SIMP_RUN, width=160.0, height=100.0),
        readout(m, "Compliance BESO", out(beso, T_COMPLIANCE), RESULTS, BESO_RUN, width=160.0, height=100.0),
        readout(m, "Compliance Multi-material", out(multi, M_COMPLIANCE), RESULTS, MULTI_RUN, width=160.0, height=100.0),
    ]

    # 7 - The three designs side by side, each named: SIMP's and BESO's moved to either side of the model.
    shown = [multi]
    for label, dx in SHOWN:
        source = {"SIMP": simp, "BESO": beso}.get(label)
        if source is not None:
            source.Hidden = True
            moved = component(m, MOVE, "Move", label + " beside", PREVIEW, SHOWN_ROW[label])
            wire(inp(moved, 0), out(source, T_SURFACE))
            shift = vector(m, UNIT_X, "Along x", dx, PREVIEW, SHOWN_ROW[label], gap=10)
            wire(inp(moved, 1), out(shift))
            shown += [moved, shift]
        # Upright, facing -y: read from the front.
        place = component(m, XZ_PLANE, "XZ Plane", label + " name", PREVIEW, SHOWN_ROW[label], gap=10)
        point(inp(place, 0), dx + 0.5, 0.5, 1.4)
        tag = component(m, TEXT_TAG, "Text Tag 3D", label + " name", SHOWN_COL, SHOWN_ROW[label])
        wire(inp(tag, 0), out(place))
        inp(tag, 1).PersistentData.Clear()
        inp(tag, 1).PersistentData.Append(GH_String(label))
        number(inp(tag, 2), 0.09)
        shown += [place, tag]

    # 8 - Each design analysed: Run Analysis and View Results, locked to start with - enable one at a time,
    # once its run has stopped; each draws where the model is.
    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    analysed = [settings]
    for label, c, model_out in (("SIMP", simp, T_ALPACA_MODEL), ("BESO", beso, T_ALPACA_MODEL), ("Multi-material", multi, M_ALPACA_MODEL)):
        run = component(m, RUN_ANALYSIS, "Run Analysis", "Analyse " + label, MODEL, ANALYSED, gap=10)
        wire(inp(run, 0), out(c, model_out))
        faint(wire(inp(run, 1), out(settings)))
        run.Locked = True
        results = component(m, VIEW_RESULTS, "View Results", "Stress " + label, OPTIMISE, ANALYSED, gap=10)
        wire(inp(results, 0), out(run))
        show_result(results, BRICK_STRESSES, VON_MISES)
        legend = component(m, LEGEND, "Legend", "Legend " + label, RESULTS, ANALYSED, gap=10)
        legend.Locked = True
        wire(inp(legend, 1), out(results, 1))
        wire(inp(legend, 2), out(results, 2))
        title = text(m, "Title", "Von Mises stress, " + label, RESULTS, ANALYSED, width=200.0)
        wire(inp(legend, 0), title)
        analysed += [run, results, legend, title]

    m.arrange(right + 160, 300)

    group(doc, "1 - How fine: Bricks per 10 cm, 1 to 3", [fineness, per_metre, layer], RED)
    group(doc, "2 - The node: a cube of steel bricks, 1 m a side",
          [plan, plan_mesh, levels_count, levels, lift, level_meshes, node_bricks, node_name, node], ORANGE)
    group(doc, "3 - The members: stubs 0.2 m long, named Member, kept whatever the optimisation finds",
          sizes + [stub_plans, along, across, high, stub_meshes, stub_levels, stub_lift, stub_moved, stub_bricks, member_name, member], ORANGE)
    group(doc, "Steel, and " + second[0].split(" ")[0].lower() + " for Multi-Material Topology",
          [steel, other] + steel_values + second_values, PURPLE)
    group(doc, case["loads_group"],
          held_parts + [held, free, support] + load_parts + [how_many, share, end_load, pattern, load_names] + shares, GREEN)
    group(doc, "5 - Assemble Model (enable Model View to see the supports and loads)", [assemble, view], BLUE)
    group(doc, "6 - Three ways: set one Run to true at a time",
          [keep, fraction, simp_iterations, simp_run, simp_step, simp, beso_iterations, beso_run, beso_step, beso,
           steel_share, other_share, multi_iterations, multi_run, multi_step, multi] + readouts, GREY)
    group(doc, "7 - The three designs side by side: SIMP, Multi-material where the model is, BESO", shown[1:], BLUE)
    group(doc, "8 - Each design analysed: enable its Run Analysis and Legend once its run has stopped, one at a time",
          analysed, PURPLE)

    # Only the designs, their names, and the stresses once analysed draw in Rhino.
    drawn = set(str(o.InstanceGuid) for o in shown + analysed + [view])
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in drawn:
            obj.Hidden = True

    note(doc, case["title"],
         case["title"] + " - topology optimisation three ways.\n\n"
         "The design domain is a cube of steel bricks, 1 m a side. The members that meet at it are stubs of bricks, "
         "0.2 m long, named Member and kept solid through Keep; they are held or loaded at their ends (group 4). "
         "The same model goes to all three of Alpaca4d's topology optimisations:\n"
         "- Topology Optimisation (SIMP): densities between 0 and 1, the quickest;\n"
         "- BESO Topology: every brick solid or void, the volume coming down from the whole cube - drag Step BESO "
         "to see the node at every volume;\n"
         "- Multi-Material Topology: steel and " + second[0].split(" ")[0].lower() + ", each up to its share.\n\n"
         "1. Set one Run to true at a time: each run goes on its own thread, and its design draws beside the others "
         "(group 7), SIMP on the left, BESO in the middle, Multi-material on the right.\n"
         "2. Drag a Step slider back to watch that run again.\n"
         "3. Once a run has stopped, enable its Run Analysis and Legend in group 8 to see its design in Von Mises "
         "stress, where the model is; disable them before analysing another.\n"
         "4. Bricks per 10 cm 2 gives 8 000 bricks: SIMP and BESO take about a minute each on an M2 Pro, "
         "Multi-Material Topology a few. 1 is quick to try; 3, 27 000 bricks, is fine and slow.\n\n"
         "Above 500 elements the three need a license.",
         300, -150, 640, 400)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(CASE + " node built. Set one Run to true to start.")


if __name__ == "__main__":
    main()
