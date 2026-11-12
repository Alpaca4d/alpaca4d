#! python3
# A shell form-found with Kangaroo, analysed with Alpaca4d, and its principal stress lines: for the
# marketing pictures (run.sh, next to it), not an example of the docs.
#
# Tools/Examples/make.sh writes StressLinesShell.gh straight from this script, without Rhino:
#
#     ../../Examples/make.sh StressLinesShell.py
#
# It also runs in Rhino with Grasshopper, Kangaroo2 (which Rhino 7 and 8 ship) and Alpaca4d
# installed - in Rhino 8 from the ScriptEditor (open this file and press Run) or with
# _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A flat net 10 x 10 m, held at its four corners, is pushed up by a load on every node, as a cloth
# hung from four points falls, but upwards: its edges are springs as long as the flat net's, which
# stretch until they carry the load. Kangaroo's Zombie Solver finds the form in one go, and Lift sets
# how high it rises. The form is a concrete shell 80 mm thick, pinned where the net was held, under
# its own weight: it carries it in compression, and its stress lines run into the corners.
# Iterations stops Kangaroo early, on a form on its way up: the pictures move it to show the form
# being found.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Boolean, GH_String
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh

# Kangaroo2
SHOW = "0ED5E67D-539D-480E-88CB-D81FA795D66C"               # Kangaroo2 > Main > Show
ZOMBIE = "4408343B-577E-4FD8-96FF-DF7549189186"             # Kangaroo2 > Main > Zombie Solver
EDGE_LENGTHS = "24E2B0D2-D7B8-4478-8A83-1F04CBA701A1"       # Kangaroo2 > Goals-Mesh > EdgeLengths
ANCHOR = "3C30B1A1-4473-4AD4-A700-EA9770726C03"             # Kangaroo2 > Goals-Pt > Anchor
LOAD = "2019C995-53AF-4EB2-976D-95B1FDC823FA"               # Kangaroo2 > Goals-Pt > Load

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
STRESS_LINES = "E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2"       # Principal Stress Lines

# Principal Stress Lines' inputs, by place. Bending to Test Dist. are plugs of its Advanced menu.
PSL_MODEL, PSL_SEED, PSL_SINGLE, PSL_BENDING, PSL_SEPARATION, PSL_TEST = 0, 2, 4, 6, 7, 8

# Concrete, in kN and m.
E, NU, DENSITY = 33000000.0, 0.2, 2500.0

SIDE, CELLS = 10.0, 24

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
RED = Color.FromArgb(50, 254, 0, 0)
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
    (left, top). A component's pivot is not its corner: its inputs hang to the left of it. Rhino
    draws the names wider than mono measures them, on both sides: the object is moved right by half
    of that, so that in Rhino it starts at left."""
    a = obj.Attributes
    b = a.Bounds
    left += (size(obj)[0] - b.Width) / 2.0
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


# How big Rhino 8 draws the components these definitions use, measured there. mono, which writes the
# file, measures Alpaca4d's components with menus far off - Principal Stress Lines 1090 x 104 rather
# than 125 x 248 - and the others' input names narrower; laid out on mono's sizes, they overlap in
# Rhino. Anything not listed is laid out as mono measures it.
RHINO_SIZES = {
    "Rectangle 3Pt": (56, 64), "Mesh Plane": (60, 64), "Line": (55, 44), "Divide Curve": (56, 64),
    "List Item": (58, 64), "Unit Z": (55, 28), "Point": (50, 24), "Deconstruct Mesh": (58, 84),
    "Deconstruct": (55, 64), "Construct Point": (59, 64), "Construct Mesh": (58, 64), "Cull Pattern": (55, 44),
    "nD": (129, 168), "Plate Fiber Section (Alpaca4d)": (127, 84), "ASD Shell (Alpaca4d)": (148, 124),
    "Support (Alpaca4d)": (141, 188), "Point Load (Alpaca4d)": (121, 64), "Mesh Load (Alpaca4d)": (101, 44),
    "Gravity Load (Alpaca4d)": (96, 28), "Load Pattern (Alpaca4d)": (176, 108),
    "AssembleModel (Alpaca4d)": (184, 124), "Analysis Settings (Alpaca4d)": (149, 184),
    " Run Analysis (Alpaca4d)": (167, 44), "Principal Stress Lines (Alpaca4d)": (125, 248),
}


def size(o):
    """An object's width and height as Rhino draws it."""
    b = o.Attributes.Bounds
    return RHINO_SIZES.get(o.Name, (b.Width, b.Height))


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

    def arrange(self, x0, y0, stages=(), col_gap=60.0, row_gap=36.0, stack_gap=18.0, stage_gap=80.0):
        """Places everything; returns the bottom edge of the last row."""
        def width(o):
            return size(o)[0]

        def height(o):
            return size(o)[1]

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
    """Types a whole number into an unwired input that takes only those, such as Mesh Plane's counts."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def boolean(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Boolean(value))
    return param


def points_into(param, values):
    param.PersistentData.Clear()
    for x, y, z in values:
        param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def points(grid, name, values, col, row=None, gap=0):
    """A Point parameter holding points typed in."""
    p = component(grid, POINT, "Point", name, col, row, gap)
    return points_into(p, values)


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


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
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


# ---------------------------------------------------------------- the shell

# Columns, in the order the data flows, and rows, one per part of the model. Each group keeps to
# columns of its own, so that no two overlap.
SHAPE, MESHED, PUSH, GOALS, SOLVER, FORM, ELEMENT, PART, PATTERN, MODEL, LINES = range(11)
NET, PUSHED, HELD, WEIGHT = range(4)


def feet():
    """The nodes the net is held at: each corner and its two neighbours along the edges."""
    h = SIDE / CELLS
    nodes = []
    for x, y, dx, dy in ((0, 0, 1, 1), (SIDE, 0, -1, 1), (SIDE, SIDE, -1, -1), (0, SIDE, 1, -1)):
        nodes += [(x, y, 0.0), (x + dx * h, y, 0.0), (x, y + dy * h, 0.0)]
    return nodes


def build(doc):
    g = Grid(doc)

    # 1 - The flat net, 10 x 10 m in 24 x 24 quads, and its nodes.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "10 x 10 m", SHAPE, NET)
    points_into(inp(plan, 0), [(0, 0, 0)])
    points_into(inp(plan, 1), [(SIDE, 0, 0)])
    points_into(inp(plan, 2), [(SIDE, SIDE, 0)])
    net = component(g, MESH_PLANE, "Mesh Plane", "24 x 24 quads", MESHED, NET)
    wire(inp(net, 0), out(plan))
    integer(inp(net, 1), CELLS)
    integer(inp(net, 2), CELLS)
    nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Nodes", MESHED, PUSHED)
    wire(inp(nodes, 0), out(net))
    held_at = points(g, "Corners", feet(), MESHED, HELD)

    # 2 - Kangaroo's goals: the net shown, its edges as springs as long as they are, held at the
    # corners, and every node pushed up.
    show = component(g, SHOW, "Show", "Net", GOALS, NET)
    wire(inp(show, 0), out(net))
    edges = component(g, EDGE_LENGTHS, "EdgeLengths", "Edges", GOALS, NET, gap=30)
    wire(inp(edges, 0), out(net))
    number(inp(edges, 1), 1.0)
    number(inp(edges, 2), 1.0)
    # Next to nothing: the edges stretch only as far as they must, and the net rises about 3.6 m.
    lift = slider(g, "Lift", 0, 0.002, 0.0004, 4, PUSH, PUSHED)
    up = component(g, UNIT_Z, "Unit Z", "Up", PUSH, PUSHED, gap=30)
    wire(inp(up, 0), lift)
    push = component(g, LOAD, "Load", "Push up", GOALS, PUSHED)
    wire(inp(push, 0), out(nodes, 0))
    wire(inp(push, 1), out(up))
    anchors = component(g, ANCHOR, "Anchor", "Held", GOALS, HELD)
    wire(inp(anchors, 0), held_at)
    number(inp(anchors, 2), 10000.0)

    # 3 - The Zombie Solver, to the end or for Iterations; the net is the first goal, so the form
    # is the first thing out.
    iterations = slider(g, "Iterations", 1, 10000, 10000, 0, SOLVER, NET)
    zombie = component(g, ZOMBIE, "Zombie Solver", "Zombie Solver", SOLVER, NET, gap=30)
    flat(wire(inp(zombie, 0), out(show), out(edges), out(push), out(anchors)))
    wire(inp(zombie, 3), iterations)
    form = component(g, LIST_ITEM, "List Item", "Form", FORM, NET)
    wire(inp(form, 0), out(zombie, 2))
    integer(inp(form, 1), 0)
    used = readout(g, "Iterations used", out(zombie, 0), FORM, NET, gap=30, width=80.0)

    # 4 - A concrete shell on the form, 80 mm thick, pinned at the corners, under its own weight.
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", ELEMENT, PUSHED)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    section = component(g, PLATE_FIBER, "Plate Fiber Section", "80 mm", PART, PUSHED)
    number(inp(section, 1), 0.08)
    wire(inp(section, 2), out(concrete))
    shell = component(g, SHELL, "ASD Shell", None, PATTERN, NET)
    wire(inp(shell, 0), out(form))
    wire(inp(shell, 1), out(section))
    pinned = component(g, SUPPORT, "Support", "Pinned", ELEMENT, HELD)
    faint(flat(wire(inp(pinned, 0), held_at)))
    for i, held in enumerate((True, True, True, False, False, False)):
        boolean(inp(pinned, i + 1), held)
    weight = component(g, GRAVITY, "Gravity Load", "Own weight", ELEMENT, WEIGHT)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, WEIGHT)
    wire(inp(pattern, 1), out(weight))
    assemble = component(g, ASSEMBLE, "Assemble Model", None, MODEL, NET)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), out(pinned)))
    flat(wire(inp(assemble, 2), out(pattern)))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, PATTERN, HELD)
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, MODEL, HELD)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))

    # 5 - The stress lines in the shell's own surface: a field grown from a seed above it, off the
    # crown, where the two principal stresses are the same and there is no direction to follow.
    seed = points(g, "Seed", [(0.3 * SIDE, 0.4 * SIDE, SIDE)], LINES, NET)
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = "Shell"
    open_menus(lines)
    g.put(lines, LINES, NET, gap=30)
    wire(inp(lines, PSL_MODEL), out(run))
    wire(inp(lines, PSL_SEED), seed)
    boolean(inp(lines, PSL_SINGLE), False)
    boolean(inp(lines, PSL_BENDING), False)
    number(inp(lines, PSL_SEPARATION), 0.35)
    number(inp(lines, PSL_TEST), 0.175)

    g.arrange(0, 0, stages=(PUSH, SOLVER, ELEMENT, LINES), row_gap=60.0, stack_gap=30.0)
    group(doc, "1 - A flat net, 10 x 10 m in 24 x 24 quads", [plan, net, nodes, held_at], GREY)
    group(doc, "2 - Kangaroo: edges that keep their length, held at the corners, every node pushed up",
          [show, edges, lift, up, push, anchors], GREEN)
    group(doc, "3 - Kangaroo's Zombie Solver finds the form", [iterations, zombie, form, used], RED)
    group(doc, "4 - A concrete shell on the form, 80 mm, pinned at the corners, under its own weight",
          [concrete, section, shell, pinned, weight, pattern, assemble, settings, run], BLUE)
    group(doc, "5 - Principal Stress Lines, in the shell's surface", [seed, lines], PURPLE)
    # Only the lines draw: the scenes add their own previews.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden"):
            obj.Hidden = obj.ComponentGuid != System.Guid(STRESS_LINES)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()


if __name__ == "__main__":
    main()
