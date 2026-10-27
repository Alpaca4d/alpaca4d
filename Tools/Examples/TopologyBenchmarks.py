#! python3
# Topology Optimisation's two benchmarks, to run in Grasshopper and compare with top3d.
#
# make.sh, next to it, writes TopologyBenchmarks.gh straight from this script, without Rhino. It also
# runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# Both are top3d's problems (Liu and Tovar 2014), in its units - a unit modulus, Poisson 0.3, unit
# cubes, loads of 1 - with its settings: 30% of the volume, penalty 3, a filter of 1.5, 200 iterations
# at most. Each has its own Run toggle, and panels with what Alpaca4d.Test/TopologyOptimisation and
# top3d itself give, to compare with:
#
#   Cantilever        60 x 4 x 20 bricks, held at x = 0, -1 at each node of the bottom edge of the
#                     free end: top3d(60,20,4,0.3,3,1.5), its own example.
#   Simply supported  120 x 4 x 20 bricks, pinned along the bottom edge at x = 0, on rollers along the
#                     one at x = 120, -1 at each node of the top edge at mid-span.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Boolean
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

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
TOPOLOGY = "8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93"           # Topology Optimisation

# Topology Optimisation's inputs and outputs, by position.
T_MODEL, T_KEEP, T_VOLUME_FRACTION, T_ITERATIONS, T_RUN = range(5)
T_STEP = 12                                                 # registered last, after the Settings menu's
T_SURFACE, T_SOLID, T_DENSITY, T_ELEMENT, T_COMPLIANCE, T_VOLUME, T_CONVERGED, T_HISTORY, T_LOG = range(9)

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


# ---------------------------------------------------------------- the benchmarks

LIST_ITEM = "59daf374-bc21-4a5e-8282-5504fb7ae9ae"          # Sets > List > List Item
T_FILTER_RADIUS = 6                                         # Topology Optimisation's Settings menu

# Columns, in the order the data flows...
NUMBERS, SERIES_, POINTS, MESHES, BRICKS_, ELEMENTS, LOADS, PATTERN, MODEL, OPTIMISE, RESULTS = range(11)
# ...and rows: the material, then a band of rows per problem.
MATERIAL_ = 0
CANTILEVER, CANTILEVER_SUPPORT, CANTILEVER_LOAD = 1, 2, 3
SIMPLE, SIMPLE_SUPPORT, SIMPLE_LOAD = 4, 5, 6

DEPTH, HEIGHT = 4, 20        # bricks deep and high, for both


def domain(g, name, span, row):
    """A block span x DEPTH x HEIGHT of unit bricks: its plan meshed, copied up level by level and made
    into bricks between each pair. Returns the components, last the Mesh Series to Brick."""
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", name + " plan", POINTS, row)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), span, 0, 0)
    point(inp(plan, 2), span, DEPTH, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, row)
    wire(inp(plan_mesh, 0), out(plan))
    integer(inp(plan_mesh, 1), span)
    integer(inp(plan_mesh, 2), DEPTH)
    levels = component(g, SERIES, "Series", "Levels", SERIES_, row)
    number(inp(levels, 0), 0.0)
    number(inp(levels, 1), 1.0)
    integer(inp(levels, 2), HEIGHT + 1)
    lift = vector(g, UNIT_Z, "Up to each level", out(levels), POINTS, row, gap=10)
    level_meshes = component(g, MOVE, "Move", "Level meshes", MESHES, row, gap=10)
    wire(inp(level_meshes, 0), out(plan_mesh))
    wire(inp(level_meshes, 1), out(lift))
    bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, row)
    wire(inp(bricks, 0), out(level_meshes))
    return [plan, plan_mesh, levels, lift, level_meshes, bricks]


def line_of_nodes(g, nickname, x, z, col, row, gap=0):
    """The nodes along the depth at x and z: DEPTH + 1 points, a unit apart."""
    p = component(g, CONSTRUCT_POINT, "Construct Point", nickname, col, row, gap)
    number(inp(p, 0), x)
    number(inp(p, 1), *range(DEPTH + 1))
    number(inp(p, 2), z)
    return p


def support(g, nickname, points, held, row, gap=0):
    """A Support on points, holding the translations in held (x, y, z) and none of the rotations."""
    s = component(g, SUPPORT, "Support", nickname, ELEMENTS, row, gap)
    flat(wire(inp(s, 0), out(points)))
    for i in range(3):
        inp(s, i + 1).PersistentData.Clear()
        inp(s, i + 1).PersistentData.Append(GH_Boolean(held[i]))
    for i in range(3):
        inp(s, i + 4).PersistentData.Clear()
        inp(s, i + 4).PersistentData.Append(GH_Boolean(False))
    return s


def optimise(m, name, assemble, row, expected, reference):
    """Topology Optimisation with top3d's settings, its own Run toggle, and what to compare it with."""
    run = toggle(m, "Run " + name, False, MODEL, row)
    # Up against its end, Step shows the latest design; drag it back to watch the run again.
    step = slider(m, "Step " + name, 0, 200, 200, 0, MODEL, row, gap=10)
    topology = emit(TOPOLOGY, "Topology Optimisation")
    topology.NickName = name
    open_menus(topology)
    m.put(topology, OPTIMISE, row)
    faint(wire(inp(topology, T_MODEL), out(assemble)))
    number(inp(topology, T_VOLUME_FRACTION), 0.3)
    inp(topology, T_ITERATIONS).PersistentData.Clear()
    inp(topology, T_ITERATIONS).PersistentData.Append(GH_Integer(200))
    wire(inp(topology, T_RUN), run)
    wire(inp(topology, T_STEP), step)
    number(inp(topology, T_FILTER_RADIUS), 1.5)

    last = component(m, LIST_ITEM, "List Item", "Last compliance", RESULTS, row)
    wire(inp(last, 0), out(topology, T_COMPLIANCE))
    integer(inp(last, 1), -1)
    inp(last, 2).PersistentData.Clear()
    inp(last, 2).PersistentData.Append(GH_Boolean(True))
    got = readout(m, "Compliance, last iteration", out(last), RESULTS, row, width=180.0)
    count = component(m, LIST_LENGTH, "List Length", "Iterations run", RESULTS, row, gap=10)
    wire(inp(count, 0), out(topology, T_COMPLIANCE))
    ran = readout(m, "Iterations run", out(count), RESULTS, row, width=180.0)
    want = text(m, "Expected after 200 iterations", expected, RESULTS, row, gap=10, width=180.0)
    top3d = text(m, "top3d (reference.py)", reference, RESULTS, row, width=180.0)
    history = readout(m, "History", out(topology, T_HISTORY), RESULTS, row, gap=10, width=560.0, height=100.0)
    return [run, topology, step, last, got, count, ran, want, top3d, history]


def build(doc):
    g = Grid(doc)

    # 1 - top3d's material: a unit modulus and Poisson 0.3, so G = 1 / 2.6, and no weight.
    values = [text(g, "Material", "top3d", ELEMENTS, MATERIAL_),
              text(g, "E", "1", ELEMENTS, MATERIAL_),
              text(g, "G", "0.384615384615385", ELEMENTS, MATERIAL_),
              text(g, "Poisson", "0.3", ELEMENTS, MATERIAL_),
              text(g, "Density", "0", ELEMENTS, MATERIAL_)]
    material = component(g, ND_MATERIAL, "nD", "top3d material", LOADS, MATERIAL_)
    for i, source in enumerate(values):
        wire(inp(material, i), source)

    # 2 - The cantilever: held at every node of the end at x = 0, loaded at the bottom of the other.
    cantilever_domain = domain(g, "Cantilever", 60, CANTILEVER)
    cantilever_bricks = component(g, BRICK, "SSP Brick", "Cantilever", LOADS, CANTILEVER)
    wire(inp(cantilever_bricks, 0), out(cantilever_domain[-1]))
    faint(wire(inp(cantilever_bricks, 1), out(material)))

    end = component(g, CONSTRUCT_POINT, "Construct Point", "End at x = 0", POINTS, CANTILEVER_SUPPORT)
    number(inp(end, 0), 0.0)
    number(inp(end, 1), *range(DEPTH + 1)).DataMapping = GH_DataMapping.Graft
    number(inp(end, 2), *range(HEIGHT + 1))
    held = support(g, "Held end", end, (True, True, True), CANTILEVER_SUPPORT)

    tip = line_of_nodes(g, "Bottom of the free end", 60, 0, POINTS, CANTILEVER_LOAD)
    down = vector(g, UNIT_Z, "-1", -1.0, ELEMENTS, CANTILEVER_LOAD)
    tip_load = component(g, POINT_LOAD, "Point Load", "Tip load", LOADS, CANTILEVER_LOAD)
    wire(inp(tip_load, 0), out(tip))
    wire(inp(tip_load, 1), out(down))
    cantilever_pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, CANTILEVER_LOAD)
    flat(wire(inp(cantilever_pattern, 1), out(tip_load)))

    # 3 - The simply supported beam: pinned along one bottom edge, on rollers along the other, loaded
    # along the top at mid-span.
    simple_domain = domain(g, "Simply supported", 120, SIMPLE)
    simple_bricks = component(g, BRICK, "SSP Brick", "Simply supported", LOADS, SIMPLE)
    wire(inp(simple_bricks, 0), out(simple_domain[-1]))
    faint(wire(inp(simple_bricks, 1), out(material)))

    pinned_nodes = line_of_nodes(g, "Bottom edge at x = 0", 0, 0, POINTS, SIMPLE_SUPPORT)
    pinned = support(g, "Pinned", pinned_nodes, (True, True, True), SIMPLE_SUPPORT)
    roller_nodes = line_of_nodes(g, "Bottom edge at x = 120", 120, 0, POINTS, SIMPLE_SUPPORT, gap=10)
    rollers = support(g, "Rollers, free along x", roller_nodes, (False, True, True), SIMPLE_SUPPORT, gap=10)

    middle = line_of_nodes(g, "Top edge at mid-span", 60, HEIGHT, POINTS, SIMPLE_LOAD)
    simple_down = vector(g, UNIT_Z, "-1", -1.0, ELEMENTS, SIMPLE_LOAD)
    middle_load = component(g, POINT_LOAD, "Point Load", "Mid-span load", LOADS, SIMPLE_LOAD)
    wire(inp(middle_load, 0), out(middle))
    wire(inp(middle_load, 1), out(simple_down))
    simple_pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, SIMPLE_LOAD)
    flat(wire(inp(simple_pattern, 1), out(middle_load)))

    right = g.arrange(0, 0, stages=(POINTS, ELEMENTS))

    # 4 - Assembled, and optimised: each problem with its own Run.
    m = Grid(doc)
    cantilever_model = component(m, ASSEMBLE, "Assemble Model", "Cantilever", MODEL, CANTILEVER)
    flat(wire(inp(cantilever_model, 0), out(cantilever_bricks)))
    flat(wire(inp(cantilever_model, 1), out(held)))
    flat(wire(inp(cantilever_model, 2), out(cantilever_pattern)))
    simple_model = component(m, ASSEMBLE, "Assemble Model", "Simply supported", MODEL, SIMPLE)
    flat(wire(inp(simple_model, 0), out(simple_bricks)))
    flat(wire(inp(simple_model, 1), out(pinned), out(rollers)))
    flat(wire(inp(simple_model, 2), out(simple_pattern)))

    cantilever_run = optimise(m, "Cantilever", cantilever_model, CANTILEVER_SUPPORT,
                              "2415.86, 200 iterations", "2415.82")
    simple_run = optimise(m, "Simply supported", simple_model, SIMPLE_SUPPORT,
                          "1203.026, 200 iterations", "1203.026")

    m.arrange(right + 160, 0)

    group(doc, "1 - top3d's material: E = 1, Poisson 0.3", [material] + values, PURPLE)
    group(doc, "2 - Cantilever, 60 x 4 x 20 bricks: top3d(60,20,4,0.3,3,1.5)",
          cantilever_domain + [cantilever_bricks, end, held, tip, down, tip_load, cantilever_pattern], ORANGE)
    group(doc, "3 - Simply supported beam, 120 x 4 x 20 bricks",
          simple_domain + [simple_bricks, pinned_nodes, pinned, roller_nodes, rollers, middle, simple_down,
                           middle_load, simple_pattern], GREEN)
    group(doc, "4 - Cantilever: set Run Cantilever to true (about 45 s)", [cantilever_model] + cantilever_run, BLUE)
    group(doc, "5 - Simply supported: set Run Simply supported to true (about 90 s)", [simple_model] + simple_run, GREY)

    # Only the optimised surfaces draw in Rhino, the simply supported beam behind the cantilever.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in (cantilever_run[1].InstanceGuid, simple_run[1].InstanceGuid):
            obj.Hidden = True

    note(doc, "Topology Optimisation benchmarks",
         "Topology Optimisation: top3d's benchmarks.\n\n"
         "Two problems of top3d (Liu and Tovar 2014, 'An efficient 3D topology optimization code written in "
         "Matlab'), in its units and with its settings: 30% of the volume, penalty 3, a filter radius of 1.5, "
         "at most 200 iterations. Alpaca4d solves them with OpenSees; the panels give what its test bench and "
         "a numpy port of top3d give for the same problems.\n\n"
         "1. Set Run Cantilever to true. Its Surface draws as it goes; the run ends after 200 iterations, "
         "about 45 s on an Apple M2 Pro. Neither problem reaches top3d's tolerance of 0.01 before then; nor "
         "does top3d.\n"
         "2. Compare 'Compliance, last iteration' with the panels under it. The first iterations agree with "
         "top3d to round-off; the cantilever's runs part by up to 0.2% while its design rearranges itself "
         "around iteration 70, and come back together.\n"
         "3. The same for the simply supported beam, about 90 s.\n"
         "4. Drag a Step slider back to watch the run again, design by design: every iteration is kept. Each step "
         "plays the next note of In the Hall of the Mountain King; right-click Topology Optimisation > Sound for "
         "Ode to Joy, a tone from the compliance, or none.\n\n"
         "Expected: the cantilever as top flange, bottom flange and diagonal webs; the simply supported beam "
         "as a truss, its tie thickest at mid-span. Topology Optimisation needs a license.",
         0, -470, 640, 390)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Topology benchmarks built. Set Run Cantilever or Run Simply supported to true.")


if __name__ == "__main__":
    main()
