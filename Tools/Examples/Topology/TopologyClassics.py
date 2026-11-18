#! python3
# Two classic problems of topology optimisation, each found by the three methods of Alpaca4d's Topology
# Optimisation side by side - SIMP, BESO and Multi-Material:
#
# - cantilever: a cantilever twice as long as it is high, held along one end and loaded at the middle of the
#   other, by SIMP at penalties 1 to 5, as papers on new methods compare them (the SIMP column of the PTO
#   papers' tables), and by BESO and Multi-Material;
# - lbracket: the L-bracket, a square without its top right 0.6 of a side, held along the top of its upright
#   leg and loaded at the end of its lower one, the usual test of stress-based methods for its re-entrant corner.
#
# Both in unit values, as those papers have them: Young's modulus 1, Poisson's ratio 0.3, bricks 1 a side, one
# brick deep, and a load of 1. make.sh, in the folder above, writes the definitions straight from this script,
# without Rhino:
#
#     CLASSIC_CASE=cantilever ./make.sh Topology/TopologyClassics.py TopologyCantilever.gh
#     CLASSIC_CASE=lbracket ./make.sh Topology/TopologyClassics.py TopologyLBracket.gh
#
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript - building the case
# CLASSIC_CASE says. The same problems in mono: Alpaca4d.Test/TopologyMethods, ./run.sh bench classic
# cantilever|lbracket.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
import Grasshopper.Kernel.Parameters
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_String, GH_Vector
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
VECTOR_XYZ = "56B92EAB-D121-43F7-94D3-6CD8F0DDEAD8"         # Vector > Vector > Vector XYZ
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
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
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
TOPOLOGY = "8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93"           # Topology Optimisation, its Method one of these:
SIMP_UNIT, BESO_UNIT, MULTI_UNIT = "TopologyOptimisation", "BesoTopology", "MultiMaterialTopology"

# Topology Optimisation's inputs and outputs, by position, for each Method.
T_MODEL, T_KEEP, T_VOLUME_FRACTION, T_ITERATIONS, T_RUN, T_PENALTY, T_FILTER_RADIUS = range(7)
B_MODEL, B_KEEP, B_VOLUME_FRACTION, B_ITERATIONS, B_RUN = range(5)
M_MODEL, M_KEEP, M_MATERIALS, M_FRACTIONS, M_ITERATIONS, M_RUN = range(6)
M_FILTER_RADIUS = 8
T_SURFACE, T_COMPLIANCE, T_VOLUME = 0, 4, 5
M_COMPLIANCE = 6

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


def widget(obj, field):
    """One of an Alpaca4d component's menu controls, which it keeps to itself."""
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    return obj.GetType().GetField(field, flags).GetValue(obj)


def show_result(results, show, component):
    """Ticks one of Model View's results alone, at a component, as clicks in its Results menu do.
    Its menus then show the controls its settings have a use for and put the rest away, and it is laid
    out again: the grid measures it at the height it opens at. Set its other widgets first."""
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    results.GetType().GetMethod("ShowResult", flags).Invoke(results, System.Array[System.Object]([show, component]))
    results.Attributes.ExpireLayout()
    results.Attributes.PerformLayout()


def points(param, pts):
    """Types a list of points into an unwired input."""
    param.PersistentData.Clear()
    for p in pts:
        param.PersistentData.Append(GH_Point(Point3d(p[0], p[1], p[2])))
    return param


def vectors(param, vs):
    """Types a list of vectors into an unwired input."""
    param.PersistentData.Clear()
    for v in vs:
        param.PersistentData.Append(GH_Vector(Vector3d(v[0], v[1], v[2])))
    return param


def graft(param):
    param.DataMapping = GH_DataMapping.Graft
    return param


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


# Columns, in the order the data flows...
INPUTS, GEOMETRY, MESHES, BRICKS_, NAMES, ELEMENTS, LOADS, PATTERN = range(8)
# ...and rows, one per part of the model.
DOMAIN, MATERIALS, SUPPORTS, END_LOAD = range(4)

# The model, its optimisations and their designs, in a grid of their own to the right.
MODEL, OPTIMISE, RESULTS, PREVIEW, SHOWN_COL = range(5)


# ---------------------------------------------------------------- the two problems

CASE = System.Environment.GetEnvironmentVariable("CLASSIC_CASE") or "cantilever"

# A block of the domain: (name, x0, x1, z0, z1), in the xz plane, one brick deep along y. Its supports and load
# are nodes on both of its faces, y = 0 and y = 1; the load of 1 is shared between them.
CANTILEVER = {
    "title": "Cantilever, five penalties",
    "blocks": [("Domain", 0, 80, 0, 40)],
    "held": [(0, y, z) for z in range(41) for y in (0, 1)],
    "held_text": "Held along its left end, x = 0",
    "loaded": [(80, y, 20) for y in (0, 1)],
    "loaded_text": "Loaded down by 1 at the middle of its free end",
    "fraction": 0.4,
    "penalties": [1, 2, 3, 4, 5],
    # Where each design is drawn: SIMP's column on the left, p = 1 at the top; Multi-Material where the model is.
    "simp_at": lambda p: (-100.0, 0.0, (3 - p) * 55.0),
    "beso_at": (100.0, 0.0, 0.0),
    "label": (40.0, 0.0, 44.0),
    "size": (80.0, 40.0),
}
LBRACKET = {
    "title": "L-bracket",
    "blocks": [("Upright leg", 0, 24, 0, 60), ("Lower leg", 24, 60, 0, 24)],
    "held": [(x, y, 60) for x in range(25) for y in (0, 1)],
    "held_text": "Held along the top of its upright leg",
    "loaded": [(60, y, 12) for y in (0, 1)],
    "loaded_text": "Loaded down by 1 at the middle of the end of its lower leg",
    "fraction": 0.4,
    "penalties": [3],
    "simp_at": lambda p: (-80.0, 0.0, 0.0),
    "beso_at": (80.0, 0.0, 0.0),
    "label": (30.0, 0.0, 64.0),
    "size": (60.0, 60.0),
}

# Unit values: the materials' Young's moduli, Poisson's ratio and shear moduli, G = E / 2(1 + nu).
STIFF = ("Stiff", "1", "0.384615384615385", "0.3", "0")
SOFT = ("Soft", "0.5", "0.192307692307692", "0.3", "0")
FILTER_RADIUS = 1.5


def material(g, values, row):
    texts = [text(g, values[0], values[0], NAMES, row),
             text(g, "E", values[1], NAMES, row),
             text(g, "G", values[2], NAMES, row),
             text(g, "Poisson", values[3], NAMES, row),
             text(g, "Density", values[4], NAMES, row)]
    m = component(g, ND_MATERIAL, "nD", values[0], ELEMENTS, row)
    for i, source in enumerate(texts):
        wire(inp(m, i), source)
    return m, texts


def tag(m, label, at, col, row):
    """A name drawn upright, facing -y, at a point."""
    place = component(m, XZ_PLANE, "XZ Plane", label + " name", col, row)
    point(inp(place, 0), at[0], at[1], at[2])
    t = component(m, TEXT_TAG, "Text Tag 3D", label + " name", col + 1, row)
    wire(inp(t, 0), out(place))
    inp(t, 1).PersistentData.Clear()
    inp(t, 1).PersistentData.Append(GH_String(label))
    number(inp(t, 2), 3.0)
    return [place, t]


def build(doc):
    case = CANTILEVER if CASE == "cantilever" else LBRACKET
    g = Grid(doc)

    # 1 - The domain: each block's side meshed in the xz plane, a copy moved 1 along y, and bricks between the two.
    blocks = case["blocks"]
    sides = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Sides", GEOMETRY, DOMAIN)
    # Up the block first and then along it, so that the mesh faces +y, the way the bricks are made.
    points(inp(sides, 0), [(b[1], 0, b[3]) for b in blocks])
    points(inp(sides, 1), [(b[1], 0, b[4]) for b in blocks])
    points(inp(sides, 2), [(b[2], 0, b[4]) for b in blocks])
    side_meshes = component(g, MESH_PLANE, "Mesh Plane", "Unit squares", MESHES, DOMAIN)
    wire(inp(side_meshes, 0), out(sides))
    for i, counts in ((1, [b[4] - b[3] for b in blocks]), (2, [b[2] - b[1] for b in blocks])):
        inp(side_meshes, i).PersistentData.Clear()
        for n in counts:
            inp(side_meshes, i).PersistentData.Append(GH_Integer(int(n)))
    faces = component(g, MOVE, "Move", "Both faces", MESHES, DOMAIN, gap=10)
    graft(wire(inp(faces, 0), out(side_meshes)))
    vectors(inp(faces, 1), [(0, 0, 0), (0, 1, 0)])
    brick_meshes = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, DOMAIN)
    wire(inp(brick_meshes, 0), out(faces))

    # 2 - The material, and the bricks; a softer one for Multi-Material.
    stiff, stiff_texts = material(g, STIFF, DOMAIN)
    soft, soft_texts = material(g, SOFT, MATERIALS)
    bricks = component(g, BRICK, "SSP Brick", "Domain", LOADS, DOMAIN)
    flat(wire(inp(bricks, 0), out(brick_meshes)))
    wire(inp(bricks, 1), out(stiff))

    # 3 - Held and loaded: nodes on both faces.
    held_note = text(g, "Held", case["held_text"], INPUTS, SUPPORTS, width=200.0)
    # The held nodes as points: Support takes them as planes there.
    held = Grasshopper.Kernel.Parameters.Param_Point()
    held.CreateAttributes()
    held.NickName = "Held nodes"
    points(held, case["held"])
    g.put(held, NAMES, SUPPORTS)
    support = component(g, SUPPORT, "Support", "Held", ELEMENTS, SUPPORTS)
    wire(inp(support, 0), held)
    loaded_note = text(g, "Loaded", case["loaded_text"], INPUTS, END_LOAD, width=200.0)
    load = component(g, POINT_LOAD, "Point Load", "A load of 1", LOADS, END_LOAD)
    points(inp(load, 0), case["loaded"])
    vectors(inp(load, 1), [(0, 0, -1.0 / len(case["loaded"]))] * len(case["loaded"]))
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, END_LOAD)
    flat(wire(inp(pattern, 1), out(load)))

    right = g.arrange(300, 300, stages=(NAMES, ELEMENTS))

    # 4 - Assembled, and 5 - the optimisations: SIMP at each penalty, BESO and Multi-Material.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, 0)
    flat(wire(inp(assemble, 0), out(bricks)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    view = component(m, MODEL_VIEW, "Model View", None, OPTIMISE, 0)
    wire(inp(view, 0), out(assemble))
    view.Locked = True

    fraction = slider(m, "VolumeFraction", 0.1, 0.8, case["fraction"], 2, MODEL, 1)
    iterations = slider(m, "Iterations", 10, 500, 300, 0, MODEL, 1, gap=10)
    simp_run = toggle(m, "Run SIMP", False, MODEL, 1, gap=10)
    optimisers, readouts, shown = [fraction, iterations, simp_run], [], []
    row = 1
    for p in case["penalties"]:
        simp = emit(TOPOLOGY, "Topology Optimisation")
        simp.NickName = "SIMP p = %d" % p
        open_menus(simp)
        m.put(simp, OPTIMISE, row)
        faint(wire(inp(simp, T_MODEL), out(assemble)))
        faint(wire(inp(simp, T_VOLUME_FRACTION), fraction))
        faint(wire(inp(simp, T_ITERATIONS), iterations))
        faint(wire(inp(simp, T_RUN), simp_run))
        number(inp(simp, T_PENALTY), p)
        number(inp(simp, T_FILTER_RADIUS), FILTER_RADIUS)
        readouts.append(readout(m, "Compliance, SIMP p = %d" % p, out(simp, T_COMPLIANCE), RESULTS, row, width=170.0, height=60.0))
        optimisers.append(simp)
        shown.append((simp, "SIMP" if len(case["penalties"]) == 1 else "SIMP, p = %d" % p, case["simp_at"](p), row))
        row += 1

    beso_run = toggle(m, "Run BESO", False, MODEL, row)
    beso = switch_unit(emit(TOPOLOGY, "Topology Optimisation"), BESO_UNIT)
    beso.NickName = "BESO"
    open_menus(beso)
    m.put(beso, OPTIMISE, row)
    faint(wire(inp(beso, B_MODEL), out(assemble)))
    faint(wire(inp(beso, B_VOLUME_FRACTION), fraction))
    faint(wire(inp(beso, B_ITERATIONS), iterations))
    wire(inp(beso, B_RUN), beso_run)
    readouts.append(readout(m, "Compliance, BESO", out(beso, T_COMPLIANCE), RESULTS, row, width=170.0, height=60.0))
    optimisers += [beso_run, beso]
    shown.append((beso, "BESO", case["beso_at"], row))
    row += 1

    stiff_share = expression(m, "f(v)=0.6*v", [fraction], "Stiff share", MODEL, row)
    soft_share = expression(m, "f(v)=0.4*v", [fraction], "Soft share", MODEL, row, gap=10)
    multi_run = toggle(m, "Run Multi-Material", False, MODEL, row, gap=10)
    multi = switch_unit(emit(TOPOLOGY, "Topology Optimisation"), MULTI_UNIT)
    multi.NickName = "Multi-Material"
    open_menus(multi)
    m.put(multi, OPTIMISE, row)
    faint(wire(inp(multi, M_MODEL), out(assemble)))
    faint(wire(inp(multi, M_MATERIALS), out(stiff), out(soft)))
    wire(inp(multi, M_FRACTIONS), out(stiff_share), out(soft_share))
    faint(wire(inp(multi, M_ITERATIONS), iterations))
    wire(inp(multi, M_RUN), multi_run)
    number(inp(multi, M_FILTER_RADIUS), FILTER_RADIUS)
    readouts.append(readout(m, "Compliance, Multi-Material", out(multi, M_COMPLIANCE), RESULTS, row, width=170.0, height=60.0))
    optimisers += [stiff_share, soft_share, multi_run, multi]

    # 6 - The designs side by side, each named; Multi-Material draws its own, in its materials' colours, where the model is.
    drawn = []
    w, h = case["size"]
    lx, ly, lz = case["label"]
    for source, label, at, r in shown:
        source.Hidden = True
        moved = component(m, MOVE, "Move", label, PREVIEW, r)
        wire(inp(moved, 0), out(source, T_SURFACE))
        vectors(inp(moved, 1), [at])
        drawn += [moved] + tag(m, label, (at[0] + lx, at[1] + ly, at[2] + lz), PREVIEW + 1, r)
    drawn += tag(m, "Multi-Material", (lx, ly, lz), PREVIEW + 1, row)

    m.arrange(right + 160, 300)

    group(doc, "1 - The domain: unit bricks, one deep, in the xz plane", [sides, side_meshes, faces, brick_meshes, bricks], ORANGE)
    group(doc, "2 - Unit values: E = 1, Poisson's ratio 0.3; Soft, E = 0.5, for Multi-Material",
          [stiff, soft] + stiff_texts + soft_texts, PURPLE)
    group(doc, "3 - " + case["held_text"] + "; " + case["loaded_text"].lower(),
          [held_note, held, support, loaded_note, load, pattern], GREEN)
    group(doc, "4 - Assemble Model (enable Model View to see the supports and the load)", [assemble, view], BLUE)
    group(doc, "5 - Three ways: set a Run to true; each run goes on its own", optimisers + readouts, GREY)
    group(doc, "6 - The designs side by side: SIMP on the left, Multi-Material where the model is, BESO on the right", drawn, BLUE)

    # Only the designs and their names draw in Rhino.
    keep = set(str(o.InstanceGuid) for o in drawn + [view, multi])
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in keep:
            obj.Hidden = True

    if CASE == "cantilever":
        story = ("A cantilever 80 x 40 unit bricks, one deep, held along its left end and loaded down by 1 at the middle of "
                 "its free end, with 40% of its volume. SIMP runs at penalties 1 to 5, as papers on new methods compare them: "
                 "at 1 the densities stay grey, the stiffest spread of material but not a design; from 2 on, members form, "
                 "and each step up costs a little stiffness for a cleaner result. BESO and Multi-Material run beside them.")
    else:
        story = ("The L-bracket: a square of 60 unit bricks without its top right 36 x 36, one deep, held along the top of its "
                 "upright leg and loaded down by 1 at the middle of the end of its lower leg, with 40% of its volume. A test "
                 "of stress-based methods for its re-entrant corner: minimum compliance, as here, leaves the corner sharp.")
    note(doc, case["title"],
         case["title"] + " - topology optimisation three ways.\n\n" + story + "\n\n"
         "1. Set Run SIMP, Run BESO or Run Multi-Material to true. Each component runs on its own thread, and its design "
         "draws in group 6 as it goes.\n"
         "2. Compliance, the work of the load, is in unit values, as such papers give it.\n"
         "3. Multi-Material uses Stiff (E = 1) for 60% of the volume and Soft (E = 0.5) for the rest.\n\n"
         "Above 500 elements Topology Optimisation needs a license.",
         300, -150, 640, 260)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(CASE + " built. Set a Run to true to start.")


if __name__ == "__main__":
    main()
