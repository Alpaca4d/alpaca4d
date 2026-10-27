#! python3
# The Topology Optimisation example: where a bridge needs its steel, found in a block of bricks.
#
# make.sh, next to it, writes TopologyBridge.gh straight from this script, without Rhino. It also runs
# in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this file
# and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# The design domain is a block 16 m long, 2 m wide and 4 m high, in steel, of SSP Bricks: Bricks per
# metre sets how fine. Its top layer is the deck, which carries a load all over and is kept solid
# whatever the optimisation finds - Topology Optimisation's Keep names it by its ElementId. The block
# is pinned along both ends of its bottom. Run finds the fifth of the block below the deck that
# carries the deck best. At 3 bricks per metre, 3 456 bricks, that is an arch from support to
# support rising into the deck at mid-span, with a column up to the deck near each end: 77
# iterations, 14 s on an M2 Pro. At 2 the deck is a 0.5 m slab of solid steel and stiff enough to
# span most of the way itself, and the steel goes into two legs that shorten its span instead.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point
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

SPAN, WIDTH, HEIGHT = 16.0, 2.0, 4.0    # the design domain, m
DECK_LOAD = 10.0                         # kN/m2 over the whole deck


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


# ---------------------------------------------------------------- the bridge

# Columns, in the order the data flows...
INPUTS, NUMBERS, SERIES_, POINTS, MESHES, BRICKS_, NAMES, ELEMENTS, LOADS, PATTERN = range(10)
# ...and rows, one per part of the model.
DOMAIN, DECK, BEARINGS, DECK_LOADS = range(4)

# The model and its optimisation, in a grid of their own to the right.
MODEL, OPTIMISE, RESULTS = range(3)
ASSEMBLED, RUN, HISTORY = range(3)


def build(doc):
    g = Grid(doc)

    # 1 - How fine: bricks per metre, the same every way.
    per_metre = slider(g, "Bricks per metre", 1, 4, 3, 0, INPUTS)
    along = expression(g, "f(n)=%g*n" % SPAN, [per_metre], "Bricks along", NUMBERS, DOMAIN)
    across = expression(g, "f(n)=%g*n" % WIDTH, [per_metre], "Bricks across", NUMBERS, DOMAIN)
    layer = expression(g, "f(n)=1/n", [per_metre], "Layer", NUMBERS, DOMAIN)
    levels_count = expression(g, "f(n)=%g*n" % HEIGHT, [per_metre], "Domain levels", NUMBERS, DOMAIN)
    deck_base = expression(g, "f(n)=%g-1/n" % HEIGHT, [per_metre], "Deck underside", NUMBERS, DECK)

    # 2 - The block: its plan meshed once, copied up level by level and made into bricks between each
    # pair. The domain runs from the bottom to the deck's underside; the deck is the one layer above.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Plan", POINTS, DOMAIN)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), SPAN, 0, 0)
    point(inp(plan, 2), SPAN, WIDTH, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, DOMAIN)
    wire(inp(plan_mesh, 0), out(plan))
    faint(wire(inp(plan_mesh, 1), out(along)))
    faint(wire(inp(plan_mesh, 2), out(across)))

    domain_levels = component(g, SERIES, "Series", "Domain levels", SERIES_, DOMAIN)
    number(inp(domain_levels, 0), 0.0)
    wire(inp(domain_levels, 1), out(layer))
    wire(inp(domain_levels, 2), out(levels_count))
    domain_lift = vector(g, UNIT_Z, "Up to each level", out(domain_levels), POINTS, DOMAIN, gap=10)
    domain_meshes = component(g, MOVE, "Move", "Domain levels", MESHES, DOMAIN, gap=10)
    wire(inp(domain_meshes, 0), out(plan_mesh))
    wire(inp(domain_meshes, 1), out(domain_lift))
    domain_bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, DOMAIN)
    wire(inp(domain_bricks, 0), out(domain_meshes))

    deck_levels = component(g, SERIES, "Series", "Deck levels", SERIES_, DECK)
    wire(inp(deck_levels, 0), out(deck_base))
    wire(inp(deck_levels, 1), out(layer))
    integer(inp(deck_levels, 2), 2)
    deck_lift = vector(g, UNIT_Z, "Up to the deck", out(deck_levels), POINTS, DECK)
    deck_meshes = component(g, MOVE, "Move", "Deck levels", MESHES, DECK)
    faint(wire(inp(deck_meshes, 0), out(plan_mesh)))
    wire(inp(deck_meshes, 1), out(deck_lift))
    deck_bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, DECK)
    wire(inp(deck_bricks, 0), out(deck_meshes))

    # 3 - Steel, and the bricks: the domain's and the deck's, each with its ElementId.
    steel_values = [text(g, "Steel", "S355", NAMES, DOMAIN),
                    text(g, "E [kN/m2]", "210000000", NAMES, DOMAIN),
                    text(g, "G [kN/m2]", "80769231", NAMES, DOMAIN),
                    text(g, "Poisson", "0.3", NAMES, DOMAIN),
                    text(g, "Density [kg/m3]", "7850", NAMES, DOMAIN)]
    steel = component(g, ND_MATERIAL, "nD", "Steel", ELEMENTS, DOMAIN)
    for i, source in enumerate(steel_values):
        wire(inp(steel, i), source)
    domain_name = text(g, "ElementId", "Domain", ELEMENTS, DOMAIN, gap=10)
    domain = component(g, BRICK, "SSP Brick", "Domain", LOADS, DOMAIN)
    wire(inp(domain, 0), out(domain_bricks))
    wire(inp(domain, 1), out(steel))
    wire(inp(domain, 3), domain_name)

    deck_name = text(g, "ElementId", "Deck", ELEMENTS, DECK)
    deck = component(g, BRICK, "SSP Brick", "Deck", LOADS, DECK)
    wire(inp(deck, 0), out(deck_bricks))
    faint(wire(inp(deck, 1), out(steel)))
    wire(inp(deck, 3), deck_name)

    # 4 - Pinned along both ends of the bottom, and the deck load on every node of the deck's top.
    ends = component(g, CONSTRUCT_POINT, "Construct Point", "Bottom of both ends", POINTS, BEARINGS)
    number(inp(ends, 0), 0.0, SPAN).DataMapping = GH_DataMapping.Graft
    ys = component(g, SERIES, "Series", "Across", SERIES_, BEARINGS)
    number(inp(ys, 0), 0.0)
    faint(wire(inp(ys, 1), out(layer)))
    across_nodes = expression(g, "f(n)=%g*n+1" % WIDTH, [per_metre], "Nodes across", NUMBERS, BEARINGS)
    wire(inp(ys, 2), out(across_nodes))
    wire(inp(ends, 1), out(ys))
    number(inp(ends, 2), 0.0)
    held = toggle(g, "Held", True, NAMES, BEARINGS)
    free = toggle(g, "Free", False, NAMES, BEARINGS)
    support = component(g, SUPPORT, "Support", "Pinned ends", ELEMENTS, BEARINGS)
    flat(wire(inp(support, 0), out(ends)))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    top_lift = vector(g, UNIT_Z, "Up to the top", HEIGHT, POINTS, DECK_LOADS)
    top = component(g, MOVE, "Move", "Deck top", MESHES, DECK_LOADS)
    faint(wire(inp(top, 0), out(plan_mesh)))
    wire(inp(top, 1), out(top_lift))
    top_nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Deck nodes", BRICKS_, DECK_LOADS)
    wire(inp(top_nodes, 0), out(top))
    node_count = component(g, LIST_LENGTH, "List Length", "How many", NAMES, DECK_LOADS)
    wire(inp(node_count, 0), out(top_nodes, 0))
    share = expression(g, "f(c)=-%g/c" % (DECK_LOAD * SPAN * WIDTH), [out(node_count)], "Each node's share", ELEMENTS, DECK_LOADS)
    down = vector(g, UNIT_Z, "Down", out(share), ELEMENTS, DECK_LOADS, gap=10)
    deck_load = component(g, POINT_LOAD, "Point Load", "Deck load", LOADS, DECK_LOADS)
    wire(inp(deck_load, 0), out(top_nodes, 0))
    wire(inp(deck_load, 1), out(down))
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, DECK_LOADS)
    flat(wire(inp(pattern, 1), out(deck_load)))

    right = g.arrange(0, 0, stages=(NUMBERS, NAMES, ELEMENTS))

    # 5 - Assembled, and the optimisation: Run to start it.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(domain), out(deck)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    view = component(m, MODEL_VIEW, "Model View", None, OPTIMISE, ASSEMBLED)
    wire(inp(view, 0), out(assemble))
    view.Locked = True

    keep = text(m, "Keep", "Deck", MODEL, RUN)
    fraction = slider(m, "VolumeFraction", 0.05, 0.5, 0.2, 2, MODEL, RUN, gap=10)
    iterations = slider(m, "Iterations", 10, 300, 100, 0, MODEL, RUN, gap=10)
    run = toggle(m, "Run", False, MODEL, RUN, gap=10)
    # Up against its end, Step shows the latest design; drag it back to watch the run again.
    step = slider(m, "Step", 0, 300, 300, 0, MODEL, RUN, gap=10)
    topology = emit(TOPOLOGY, "Topology Optimisation")
    open_menus(topology)
    m.put(topology, OPTIMISE, RUN)
    faint(wire(inp(topology, T_MODEL), out(assemble)))
    wire(inp(topology, T_KEEP), keep)
    wire(inp(topology, T_VOLUME_FRACTION), fraction)
    wire(inp(topology, T_ITERATIONS), iterations)
    wire(inp(topology, T_RUN), run)
    wire(inp(topology, T_STEP), step)

    compliance = readout(m, "Compliance", out(topology, T_COMPLIANCE), RESULTS, RUN, width=160.0, height=120.0)
    converged = readout(m, "Converged", out(topology, T_CONVERGED), RESULTS, RUN, gap=10)
    history = readout(m, "History", out(topology, T_HISTORY), RESULTS, HISTORY, width=520.0, height=120.0)

    m.arrange(right + 160, 0)

    group(doc, "1 - How fine: drag Bricks per metre", [per_metre, along, across, layer, levels_count, deck_base, across_nodes], RED)
    group(doc, "2 - The design domain, 16 x 2 x 4 m, and the deck on top, in layers of bricks",
          [plan, plan_mesh, domain_levels, domain_lift, domain_meshes, domain_bricks,
           deck_levels, deck_lift, deck_meshes, deck_bricks], ORANGE)
    group(doc, "3 - Steel, and the bricks named Domain and Deck", [steel, domain_name, domain, deck_name, deck] + steel_values, PURPLE)
    group(doc, "4 - Pinned ends, 10 kN/m2 on the deck",
          [ends, ys, held, free, support, top_lift, top, top_nodes, node_count, share, down, deck_load, pattern], GREEN)
    group(doc, "5 - Assemble Model (enable Model View to see the domain)", [assemble, view], BLUE)
    group(doc, "6 - Topology Optimisation: set Run to true", [keep, fraction, iterations, run, step, topology, compliance, converged, history], GREY)

    # Only the optimised surface draws in Rhino, not the construction geometry behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != topology.InstanceGuid and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "Topology Optimisation",
         "Bridge - a Topology Optimisation example.\n\n"
         "The design domain is a steel block 16 m long, 2 m wide and 4 m high, pinned along both ends of its "
         "bottom. Its top layer is the deck: it carries 10 kN/m2 and is kept solid, by its ElementId, through "
         "Keep. The optimisation finds the fifth of the rest that carries the deck stiffest: an arch from "
         "support to support rising into the deck at mid-span, with a column up to the deck near each end.\n\n"
         "1. Set Run to true. The run goes on its own thread: the Surface preview and the outputs follow it, "
         "about once a second, and Rhino stays free.\n"
         "2. Set Run to false to pause, true to go on; raise Iterations to go further. Change anything else - "
         "VolumeFraction, Bricks per metre - and the run starts again.\n"
         "3. Drag Step back to watch the run again, design by design: every iteration is kept.\n"
         "4. Bricks per metre 3 gives 3 456 bricks, about 15 s on an M2 Pro; 4 gives 8 192, under a minute. At 2 "
         "the deck is a 0.5 m slab of solid steel, stiff enough to span most of the way, and the steel goes into "
         "two legs that shorten its span instead of an arch.\n\n"
         "Each SSP Brick is solved as OpenSees' eight point brick: the SSP Brick fixes part of its stiffness "
         "when it is built, so it cannot be softened. A void brick keeps a billionth of its stiffness; nothing "
         "is cut out of the mesh. Topology Optimisation needs a license.",
         0, -450, 620, 380)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Topology bridge built. Set Run to true on Topology Optimisation to start.")


if __name__ == "__main__":
    main()
