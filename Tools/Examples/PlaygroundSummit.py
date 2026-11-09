#! python3
# The Summit bridge, the last lesson of the website's Playground: the lightest concrete bridge across
# the chasm, found by Topology Optimisation in a block of bricks. Built on the Grasshopper canvas.
#
# make.sh, next to it, writes PlaygroundSummit.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundSummit.py
#     START=1 ./make.sh PlaygroundSummit.py PlaygroundSummitStart.gh    # the block, the rocks and the load only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the block, 12 m across the chasm, 4 m deep and
# 1.2 m wide, its plan meshed and stacked into 30 x 10 bricks of 0.4 x 1.2 x 0.4 m; the concrete,
# the top row of bricks split off as the deck; the rocks, both ends of the bottom pinned to the
# chasm's walls; the walkway, 5 kN/m2 shared by every node of the deck's top; the model assembled;
# what the optimisation keeps, the deck and a quarter of the rest; and Topology Optimisation itself,
# with Run and Step. It finds an arch rising from the foot of each wall into the deck, with a hanger
# near each quarter: 40 iterations, about a second. 300 bricks is under the 500 Topology Optimisation
# takes without a license. The Playground's lesson (assets/site/playground/summit.js in the website)
# draws its snippets from this definition, read by Tools/GhCanvas, and hands this file out at the end.
#
# The eighth group, not a round of the lesson, analyses the design once the run has stopped and
# colours it by Von Mises stress. The design has about 90 bricks, past the 50 elements Run Analysis
# and Model View take without a license, but a design that came out of Topology Optimisation is
# analysed and drawn free up to the 500 the optimisation took.
#
# The hand checks: the bridge keeps the deck, 30 x 0.192 m3 x 2.5 t/m3 = 14.4 t, and a quarter of the
# rest, 32.4 t: 46.8 t of the block's 144 t. An arch rising f = 3.8 m from the foot of the rock to the
# middle of the deck pushes each wall out with H = wL^2 / 8f = 6 x 12^2 / (8 x 3.8) = 28.4 kN;
# OpenSees gives 26.8 kN on the design.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
SPLIT_LIST = "9AB93E1A-EBDF-4090-9296-B000CFF7B202"         # Sets > List > Split List
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
LIST_LENGTH = "1817FD29-20AE-4503-B542-F0FB651E67D7"        # Sets > List > List Length

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (Elastic Isotropic)
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
TOPOLOGY = "8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93"           # Topology Optimisation
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless
# wired. A brick's node has no rotations: Rx, Ry and Rz are freed, as TopologyBridge does.
S_POSITION, S_TX, S_RX, S_RY, S_RZ = 0, 1, 4, 5, 6
# Topology Optimisation's inputs and outputs, by position.
T_MODEL, T_KEEP, T_VOLUME_FRACTION, T_ITERATIONS, T_RUN = range(5)
T_STEP = 12                                                 # registered last, after the Settings menu's
T_ALPACA_MODEL = 10                                         # after Step, registered last too
# Model View's results, from 1: Brick stresses is the sixth, Von Mises the seventh of its components.
BRICK_STRESSES, VON_MISES = 6, 6

SPAN, WIDTH, DEPTH = 12.0, 1.2, 4.0      # the block, m
ALONG, UP = 30, 10                       # bricks along the span and up the depth: 0.4 m each way
WALKWAY = 5.0                            # kN/m2 over the deck
# The start of the docs lesson, or the whole bridge.
START = System.Environment.GetEnvironmentVariable("START") == "1"

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per round of the lesson.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
YELLOW = Color.FromArgb(60, 230, 200, 40)
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
    """Lays objects out in columns, left to right in the order the data flows, and in rows, one
    per part of the model, so that each part's chain of components sits on one line. Column widths
    and row heights are the objects' own, measured once Grasshopper has laid them out. A column
    listed in stages starts a new group, and leaves room for the group boxes either side of it."""

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


def text(grid, name, value, col, row=None, gap=0, width=60.0):
    """A small panel holding a text or a number, such as a material's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
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


def widget(obj, field):
    """One of an Alpaca4d component's menu controls, which it keeps to itself."""
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    return obj.GetType().GetField(field, flags).GetValue(obj)


def show_result(c, show, component_):
    """Ticks one of Model View's results alone, as clicks in its Results menu do."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    c.GetType().GetMethod("ShowResult", flags).Invoke(c, System.Array[System.Object]([show, component_]))
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()


# ---------------------------------------------------------------- the bridge

# Columns in the order the data flows...
(INPUTS, NUMBERS, POINTS, CURVES, MESHES, SERIES_, VECTORS, MOVED, BRICKS_, SPLIT, MATERIALS,
 ELEMENTS, PATTERN, MODEL, KEEP_, TOPO, SETTINGS, RUN, VIEW) = range(19)
# ...and rows, one per part of the model.
BLOCK_, ROCKS, WALKWAY_ = range(3)


def build(doc):
    grid = Grid(doc)

    # 1 - The block: the deck's plan, 12 x 1.2 m, meshed into 30 bricks along and one across, and
    # copied up level by level, 11 levels for 10 layers of bricks, 0.4 m each. Mesh Series to Brick
    # makes the bricks layer by layer from the bottom, so the deck's are the last 30.
    span = slider(grid, "Span", 4, 20, SPAN, 1, INPUTS, BLOCK_)
    width = slider(grid, "Width", 0.4, 3, WIDTH, 1, INPUTS, BLOCK_)
    depth = slider(grid, "Depth", 0.4, 8, DEPTH, 1, INPUTS, BLOCK_)
    along = slider(grid, "Along", 1, 80, ALONG, 0, INPUTS, BLOCK_, gap=10)
    up = slider(grid, "Up", 1, 30, UP, 0, INPUTS, BLOCK_)
    layer = expression(grid, "f(d,u)=d/u", [depth, up], "Layer", NUMBERS, BLOCK_)
    levels = expression(grid, "f(u)=u+1", [up], "Level count", NUMBERS, BLOCK_)
    far = component(grid, CONSTRUCT_POINT, "Construct Point", "Far end", POINTS, BLOCK_)
    wire(inp(far, 0), span)
    corner = component(grid, CONSTRUCT_POINT, "Construct Point", "Far corner", POINTS, BLOCK_)
    wire(inp(corner, 0), span)
    wire(inp(corner, 1), width)
    plan = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Plan", CURVES, BLOCK_)
    point(inp(plan, 0), 0, 0, 0)
    wire(inp(plan, 1), out(far))
    wire(inp(plan, 2), out(corner))
    plan_mesh = component(grid, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, BLOCK_)
    wire(inp(plan_mesh, 0), out(plan))
    wire(inp(plan_mesh, 1), along)
    integer(inp(plan_mesh, 2), 1)
    heights = component(grid, SERIES, "Series", "Heights", SERIES_, BLOCK_)
    number(inp(heights, 0), 0.0)
    wire(inp(heights, 1), out(layer))
    wire(inp(heights, 2), out(levels))
    lift = component(grid, UNIT_Z, "Unit Z", "Up to each level", VECTORS, BLOCK_)
    wire(inp(lift, 0), out(heights))
    stack = component(grid, MOVE, "Move", "Levels", MOVED, BLOCK_)
    wire(inp(stack, 0), out(plan_mesh))
    wire(inp(stack, 1), out(lift))
    bricks = component(grid, MESH_TO_BRICK, "Mesh Series to Brick", "Bricks", BRICKS_, BLOCK_)
    wire(inp(bricks, 0), out(stack))

    # 2 - Concrete: C30/37, and the bricks in two, the top row the deck - the last Along of them.
    deck_starts = expression(grid, "f(a,u)=a*(u-1)", [along, up], "Deck starts", SPLIT, BLOCK_)
    split = component(grid, SPLIT_LIST, "Split List", None, SPLIT, BLOCK_)
    wire(inp(split, 0), out(bricks))
    wire(inp(split, 1), out(deck_starts))
    concrete_values = [text(grid, "Concrete", "C30/37", MATERIALS, BLOCK_),
                       text(grid, "E [kN/m2]", "33000000", MATERIALS, BLOCK_),
                       text(grid, "G [kN/m2]", "13750000", MATERIALS, BLOCK_),
                       text(grid, "Poisson", "0.2", MATERIALS, BLOCK_),
                       text(grid, "Density [kg/m3]", "2500", MATERIALS, BLOCK_)]
    concrete = component(grid, ND_MATERIAL, "nD", "C30/37", ELEMENTS, BLOCK_)
    for i, source in enumerate(concrete_values):
        wire(inp(concrete, i), source)
    domain_name = text(grid, "Domain name", "Domain", ELEMENTS, BLOCK_, gap=10)
    deck_name = text(grid, "Deck name", "Deck", ELEMENTS, BLOCK_)
    domain = component(grid, BRICK, "SSP Brick", "Domain", PATTERN, BLOCK_)
    wire(inp(domain, 0), out(split, 0))
    wire(inp(domain, 1), out(concrete))
    wire(inp(domain, 3), domain_name)
    deck = component(grid, BRICK, "SSP Brick", "Deck", PATTERN, BLOCK_)
    wire(inp(deck, 0), out(split, 1))
    wire(inp(deck, 1), out(concrete))
    wire(inp(deck, 3), deck_name)

    # 3 - The rocks: both ends of the block's bottom, at each of its two edges across, pinned to the
    # chasm's walls. The rock can push back, so both hold X: an arch needs that push. This row and
    # the walkway's start under the end of the block's, so that each round's group stays compact.
    across = component(grid, SERIES, "Series", "Across", BRICKS_, ROCKS)
    number(inp(across, 0), 0.0)
    wire(inp(across, 1), width)
    integer(inp(across, 2), 2)
    foot_a = component(grid, CONSTRUCT_POINT, "Construct Point", "Foot A", SPLIT, ROCKS)
    wire(inp(foot_a, 1), out(across))
    foot_b = component(grid, CONSTRUCT_POINT, "Construct Point", "Foot B", SPLIT, ROCKS)
    wire(inp(foot_b, 0), span)
    wire(inp(foot_b, 1), out(across))
    free = toggle(grid, "Free", False, MATERIALS, ROCKS)
    rock_a = component(grid, SUPPORT, "Support", "Rock A", ELEMENTS, ROCKS)
    rock_b = component(grid, SUPPORT, "Support", "Rock B", ELEMENTS, ROCKS)
    for rock, foot in ((rock_a, foot_a), (rock_b, foot_b)):
        wire(inp(rock, S_POSITION), out(foot))
        for i in (S_RX, S_RY, S_RZ):
            wire(inp(rock, i), free)

    # 4 - The walkway: 5 kN/m2 over the 12 x 1.2 m deck, 72 kN, shared by the 62 nodes of its top.
    walkway = slider(grid, "Walkway", 0, 10, WALKWAY, 1, SERIES_, WALKWAY_)
    to_top = component(grid, UNIT_Z, "Unit Z", "Up to the deck", VECTORS, WALKWAY_)
    wire(inp(to_top, 0), depth)
    top = component(grid, MOVE, "Move", "Deck top", MOVED, WALKWAY_)
    wire(inp(top, 0), out(plan_mesh))
    wire(inp(top, 1), out(to_top))
    top_nodes = component(grid, DECONSTRUCT_MESH, "Deconstruct Mesh", "Deck nodes", BRICKS_, WALKWAY_)
    wire(inp(top_nodes, 0), out(top))
    count = component(grid, LIST_LENGTH, "List Length", "How many", SPLIT, WALKWAY_)
    wire(inp(count, 0), out(top_nodes, 0))
    share = expression(grid, "f(w,s,b,n)=-w*s*b/n", [walkway, span, width, out(count)], "Each node's share", MATERIALS, WALKWAY_)
    down = component(grid, UNIT_Z, "Unit Z", "Down", ELEMENTS, WALKWAY_)
    wire(inp(down, 0), out(share))
    load = component(grid, POINT_LOAD, "Point Load", "The walkway", PATTERN, WALKWAY_)
    wire(inp(load, 0), out(top_nodes, 0))
    wire(inp(load, 1), out(down))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, MODEL, WALKWAY_)
    wire(inp(pattern, 1), out(load))

    if START:
        grid.arrange(0, 0, stages=(SPLIT, MODEL), row_gap=60.0)
        group(doc, "1 - The block", [span, width, depth, along, up, layer, levels, far, corner, plan, plan_mesh, heights, lift, stack, bricks], RED)
        group(doc, "2 - Concrete", [deck_starts, split, concrete, domain_name, deck_name, domain, deck] + concrete_values, ORANGE)
        group(doc, "3 - The rocks", [across, foot_a, foot_b, free, rock_a, rock_b], PURPLE)
        group(doc, "4 - The walkway", [walkway, to_top, top, top_nodes, count, share, down, load, pattern], GREEN)
        hide_all_but(doc, [])
        note(doc, "The Summit bridge - start",
             "The Summit bridge - lesson 10 of the Alpaca4d docs, the start.\n\n"
             "A block of concrete 12 m across the chasm, 4 m deep and 1.2 m wide, in 30 x 10 bricks; its "
             "top row is the deck. Both ends of its bottom are pinned to the rock, and the walkway, "
             "5 kN/m2, stands on the deck. Assemble it, and add Topology Optimisation as the lesson's "
             "steps say.",
             0, -260, 520, 180)
        return

    # 5 - Assemble: both sets of bricks, both rocks and the walkway into one model.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, ROCKS)
    wire(inp(assemble, 0), out(domain), out(deck))
    wire(inp(assemble, 1), out(rock_a), out(rock_b))
    wire(inp(assemble, 2), out(pattern))

    # 6 - What it keeps: the deck whatever it finds, by its ElementId, and a quarter of the rest.
    keep = text(grid, "Keep", "Deck", KEEP_, BLOCK_, width=50.0)
    fraction = slider(grid, "Share kept", 0, 1, 0.25, 2, KEEP_, BLOCK_)

    # 7 - Topology Optimisation: Run to start it, and Step to watch it again.
    run = toggle(grid, "Run", True, KEEP_, WALKWAY_)
    step = slider(grid, "Step", 0, 100, 100, 0, KEEP_, WALKWAY_)
    topology = component(grid, TOPOLOGY, "Topology Optimisation", None, TOPO, ROCKS)
    wire(inp(topology, T_MODEL), out(assemble))
    wire(inp(topology, T_KEEP), keep)
    wire(inp(topology, T_VOLUME_FRACTION), fraction)
    wire(inp(topology, T_RUN), run)
    wire(inp(topology, T_STEP), step)

    # 8 - The design analysed, coloured by Von Mises stress, once the run has stopped and AlpacaModel
    # has it. Free, as a topology design, up to 500 elements.
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    analysis = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, ROCKS)
    wire(inp(analysis, 0), out(topology, T_ALPACA_MODEL))
    wire(inp(analysis, 1), out(settings))
    stresses = component(grid, MODEL_VIEW, "Model View", "Von Mises", VIEW)
    wire(inp(stresses, 0), out(analysis))
    widget(stresses, "_ckExtruded").Active = False
    show_result(stresses, BRICK_STRESSES, VON_MISES)

    grid.arrange(0, 0, stages=(SPLIT, MODEL, KEEP_, TOPO, SETTINGS), row_gap=60.0)
    # Analysis Settings under Run Analysis, and Model View in line with it, as in the River.
    in_line = analysis.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(analysis, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    move(stresses, analysis.Attributes.Bounds.Right + 80.0, in_line.Y)

    group(doc, "1 - The block", [span, width, depth, along, up, layer, levels, far, corner, plan, plan_mesh, heights, lift, stack, bricks], RED)
    group(doc, "2 - Concrete", [deck_starts, split, concrete, domain_name, deck_name, domain, deck] + concrete_values, ORANGE)
    group(doc, "3 - The rocks", [across, foot_a, foot_b, free, rock_a, rock_b], PURPLE)
    group(doc, "4 - The walkway", [walkway, to_top, top, top_nodes, count, share, down, load, pattern], GREEN)
    group(doc, "5 - Assemble", [assemble], BLUE)
    group(doc, "6 - What it keeps", [keep, fraction], YELLOW)
    group(doc, "7 - Topology Optimisation", [run, step, topology], GREY)
    group(doc, "8 - The design, analysed once the run has stopped",
          [settings, analysis, stresses], PURPLE)

    # Only the optimised surface, and the design's stresses once analysed, draw in Rhino.
    hide_all_but(doc, [topology, stresses])

    note(doc, "The Summit bridge",
         "The Summit bridge - lesson 10 of the Alpaca4d Playground.\n\n"
         "A block of concrete 12 m across the chasm, 4 m deep and 1.2 m wide, in 30 x 10 bricks, pinned to "
         "the rock at both ends of its bottom, with a 5 kN/m2 walkway on its top row, the deck. Topology "
         "Optimisation keeps the deck and a quarter of the rest, where the forces flow: an arch rising from "
         "the foot of each wall into the deck, with a hanger near each quarter. 40 iterations, a second or "
         "so; drag Step back to watch it again.\n\n"
         "Check it by hand: it keeps 14.4 t of deck and a quarter of the rest, 32.4 t: 46.8 t of the "
         "block's 144 t. Each wall pushes the arch in with H = wL^2 / 8f = 6 x 12^2 / (8 x 3.8) = 28.4 kN; "
         "OpenSees gives 26.8 kN.\n\n"
         "Try: free Rock B along X (wire Free into its Tx), or Keep nothing.",
         0, -300, 560, 230)


def hide_all_but(doc, shown):
    """Only the components given draw in Rhino, not the construction geometry behind them."""
    keep = set(str(o.InstanceGuid) for o in shown)
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in keep:
            obj.Hidden = True


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Summit bridge built. Topology Optimisation runs as it opens; drag Step to watch it again.")


if __name__ == "__main__":
    main()
