#! python3
# Four concrete shells and their principal stress lines, for the marketing pictures (run.sh, next to
# it): a cantilever wall and a deep beam standing in a vertical plane, which carry their loads in
# their own plane; a slab on four edges lying flat, which carries its load in bending; and a barrel
# canopy cantilevering from its root, curved, which carries its weight in its surface. Not an
# example of the docs.
#
# Tools/Examples/make.sh writes StressLinesPlates.gh straight from this script, without Rhino:
#
#     ../Examples/make.sh StressLinesPlates.py
#
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# Each one is a row of its own, from its rectangle to its stress lines, and stands alone: its own
# material, supports, loads and Run Analysis. The sliders the pictures move are named after the plate:
# "B load node" walks the deep beam's point load along its top, "C point load" and "C load node" add
# one to the slab and walk it along its middle.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Boolean, GH_Vector, GH_String
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
DIVIDE_CURVE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"       # Curve > Division > Divide Curve
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
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

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
RED = Color.FromArgb(50, 254, 0, 0)
BLUE = Color.FromArgb(50, 66, 136, 247)


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


def vector_into(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
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


# ---------------------------------------------------------------- the parts every plate has

# Columns, in the order the data flows, and rows, one per part of a plate. Each plate is a grid of
# its own, and each group keeps to its own rows and columns, so that no two groups overlap: the
# shell on the first row, the supports on the second, the loads on the third, and on the last the
# analysis and, in a column of their own, the lines.
DATA, SHAPE, MESHED, ELEMENT, PART, PATTERN, MODEL, LINES = range(8)
GEOMETRY, HELD, LOADED, SOLVED = range(4)
ROWS = 4


def plate(g, row, name, corners, counts, thickness):
    """A concrete plate: its rectangle through three corners, meshed counts[0] x counts[1], with ASD
    Shells on the mesh. Returns the objects, the rectangle, the mesh and the shells."""
    outline = component(g, RECTANGLE_3PT, "Rectangle 3Pt", name, SHAPE, row + GEOMETRY)
    for i, (x, y, z) in enumerate(corners):
        points_into(inp(outline, i), [(x, y, z)])
    mesh = component(g, MESH_PLANE, "Mesh Plane", "{0} x {1} quads".format(*counts), MESHED, row + GEOMETRY)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), counts[0])
    integer(inp(mesh, 2), counts[1])
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", MESHED, row + GEOMETRY, gap=30)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    section = component(g, PLATE_FIBER, "Plate Fiber Section", "{0:.0f} mm".format(thickness * 1000), ELEMENT, row + GEOMETRY)
    number(inp(section, 1), thickness)
    wire(inp(section, 2), out(concrete))
    shell = component(g, SHELL, "ASD Shell", None, PART, row + GEOMETRY)
    wire(inp(shell, 0), out(mesh))
    wire(inp(shell, 1), out(section))
    return [outline, mesh, concrete, section, shell], outline, mesh, shell


def edge_nodes(g, name, starts, ends, count, row, gap=0):
    """Lines from starts to ends, each divided into count: the nodes along them. Returns the objects
    and Divide Curve."""
    lines = component(g, LINE, "Line", name, SHAPE, row, gap)
    points_into(inp(lines, 0), starts)
    points_into(inp(lines, 1), ends)
    nodes = component(g, DIVIDE_CURVE, "Divide Curve", None, MESHED, row, gap)
    wire(inp(nodes, 0), out(lines))
    integer(inp(nodes, 1), count)
    return [lines, nodes], nodes


def support(g, name, nodes, held, row, gap=0):
    """A Support on every node, holding the degrees of freedom held marks, Tx to Rz."""
    s = component(g, SUPPORT, "Support", name, ELEMENT, row, gap)
    flat(wire(inp(s, 0), out(nodes)))
    for i in range(6):
        boolean(inp(s, i + 1), held[i])
    return s


def down(g, size, col, row, gap=0):
    """A load straight down, its size from a slider: Unit Z times minus the slider. Returns the
    objects and Unit Z."""
    minus = emit(EXPRESSION, "The Expression component")
    minus.SetInitCode("f(x)=-x")
    minus.NickName = "Minus"
    g.put(minus, col, row, gap)
    wire(inp(minus, 0), size)
    v = component(g, UNIT_Z, "Unit Z", "Down", col + 1, row, gap)
    wire(inp(v, 0), out(minus))
    return [minus, v], v


def solved(g, row, shell, supports, loads):
    """Load Pattern, Assemble Model, Analysis Settings and Run Analysis."""
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, row + SOLVED)
    flat(wire(inp(pattern, 1), *[out(l) for l in loads]))
    assemble = component(g, ASSEMBLE, "Assemble Model", None, MODEL, row + SOLVED)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), *[out(s) for s in supports]))
    flat(wire(inp(assemble, 2), out(pattern)))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, PATTERN, row + SOLVED, gap=30)
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, MODEL, row + SOLVED, gap=30)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    return [pattern, assemble, settings, run], run


def traced(g, row, name, run, seed, bending, separation):
    """Principal Stress Lines: a field of lines grown from one seed, separation apart, each stopping
    0.4 times that from the lines already drawn."""
    seeds = points(g, "Seed", [seed], LINES, row + SOLVED)
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = name
    open_menus(lines)
    g.put(lines, LINES, row + SOLVED, gap=30)
    faint(wire(inp(lines, PSL_MODEL), out(run)))
    wire(inp(lines, PSL_SEED), seeds)
    boolean(inp(lines, PSL_SINGLE), False)
    boolean(inp(lines, PSL_BENDING), bending)
    number(inp(lines, PSL_SEPARATION), separation)
    number(inp(lines, PSL_TEST), separation * 0.4)
    return [seeds, lines], lines


HELD_ALL = (True,) * 6


# ---------------------------------------------------------------- the four plates

def cantilever_wall(doc, g, row):
    """A: a wall 8 m long and 2.5 m deep in the XZ plane, built in along its left edge, carrying
    50 kN/m along its top: in-plane stress lines."""
    parts, outline, mesh, shell = plate(g, row, "Cantilever wall, 8 x 2.5 m",
                                        [(0, 0, 0), (8, 0, 0), (8, 0, 2.5)], (48, 15), 0.25)
    edge, nodes = edge_nodes(g, "Left edge", [(0, 0, 0)], [(0, 0, 2.5)], 15, row + HELD)
    held = support(g, "Built in", nodes, HELD_ALL, row + HELD)
    top, top_nodes = edge_nodes(g, "Top edge", [(0, 0, 2.5)], [(8, 0, 2.5)], 48, row + LOADED)
    load = component(g, POINT_LOAD, "Point Load", "50 kN/m", PART, row + LOADED)
    flat(wire(inp(load, 0), out(top_nodes)))
    vector_into(inp(load, 1), 0, 0, -50.0 * 8.0 / 49.0)
    model, run = solved(g, row, shell, [held], [load])
    lines_parts, lines = traced(g, row, "Cantilever wall", run, (4.0, 0.0, 1.25), False, 0.2)
    groups = [("A1 - Cantilever wall: 8 x 2.5 m, 48 x 15 quads, concrete 250 mm", parts, ORANGE),
              ("A2 - Built in along its left edge", edge + [held], GREEN),
              ("A3 - 50 kN/m along its top, on each node of the edge", top + [load], RED),
              ("A4 - One load pattern, assembled and solved", model, BLUE),
              ("A5 - Principal Stress Lines, in its plane", lines_parts, PURPLE)]
    return groups


def deep_beam(doc, g, row):
    """B: a wall 10 m long and 4 m deep in the XZ plane, 11 m right of A, borne on the bottom
    0.4 m of each end, pinned at one and sliding along at the other, with 500 kN on a node of its
    top that a slider picks: in-plane stress lines."""
    x0 = 11.0
    parts, outline, mesh, shell = plate(g, row, "Deep beam, 10 x 4 m",
                                        [(x0, 0, 0), (x0 + 10, 0, 0), (x0 + 10, 0, 4)], (50, 20), 0.3)
    # Held out of its plane too: borne up the end rather than along the bottom, its bearings also
    # keep it from turning about its length.
    left, left_nodes = edge_nodes(g, "Left bearing", [(x0, 0, 0)], [(x0, 0, 0.4)], 2, row + HELD)
    pinned = support(g, "Pinned", left_nodes, (True, True, True, False, False, False), row + HELD)
    right, right_nodes = edge_nodes(g, "Right bearing", [(x0 + 10, 0, 0)], [(x0 + 10, 0, 0.4)], 2, row + HELD, gap=30)
    rollers = support(g, "Sliding", right_nodes, (False, True, True, False, False, True), row + HELD, gap=30)
    top, top_nodes = edge_nodes(g, "Top edge", [(x0, 0, 4)], [(x0 + 10, 0, 4)], 50, row + LOADED)
    where = slider(g, "B load node", 0, 50, 25, 0, DATA, row + LOADED)
    at = component(g, LIST_ITEM, "List Item", "Loaded node", ELEMENT, row + LOADED)
    wire(inp(at, 0), out(top_nodes))
    wire(inp(at, 1), where)
    load = component(g, POINT_LOAD, "Point Load", "500 kN", PART, row + LOADED)
    wire(inp(load, 0), out(at))
    vector_into(inp(load, 1), 0, 0, -500.0)
    model, run = solved(g, row, shell, [pinned, rollers], [load])
    lines_parts, lines = traced(g, row, "Deep beam", run, (x0 + 5.0, 0.0, 2.0), False, 0.3)
    groups = [("B1 - Deep beam: 10 x 4 m, 50 x 20 quads, concrete 300 mm", parts, ORANGE),
              ("B2 - Borne on the bottom 0.4 m of each end: pinned, and sliding along", left + right + [pinned, rollers], GREEN),
              ("B3 - 500 kN on a node of its top: the slider walks it along", top + [where, at, load], RED),
              ("B4 - One load pattern, assembled and solved", model, BLUE),
              ("B5 - Principal Stress Lines, in its plane", lines_parts, PURPLE)]
    return groups


def slab(doc, g, row):
    """C: a slab 12 x 8 m lying flat, in front of A, simply supported on all four edges, under 10 kN/m2
    and a point load a slider sets, 0 to start, on a node of its middle line that another picks:
    bending stress lines."""
    y0 = -14.0
    parts, outline, mesh, shell = plate(g, row, "Slab, 12 x 8 m",
                                        [(0, y0, 0), (12, y0, 0), (12, y0 + 8, 0)], (30, 20), 0.25)
    # Pinned along every edge: held up, free to turn; in plane too, which bending alone does not need.
    along_x, x_nodes = edge_nodes(g, "Edges along X", [(0, y0, 0), (0, y0 + 8, 0)], [(12, y0, 0), (12, y0 + 8, 0)], 30, row + HELD)
    on_x = support(g, "Pinned", x_nodes, (True, True, True, False, False, False), row + HELD)
    along_y, y_nodes = edge_nodes(g, "Edges along Y", [(0, y0, 0), (12, y0, 0)], [(0, y0 + 8, 0), (12, y0 + 8, 0)], 20, row + HELD, gap=30)
    on_y = support(g, "Pinned", y_nodes, (True, True, True, False, False, False), row + HELD, gap=30)
    size = slider(g, "C UDL [kN/m2]", 0, 20, 10, 1, DATA, row + LOADED)
    pressure_parts, pressure = down(g, size, SHAPE, row + LOADED)
    uniform = component(g, MESH_LOAD, "Mesh Load", "Uniform load", ELEMENT, row + LOADED)
    wire(inp(uniform, 1), out(pressure))
    force = slider(g, "C point load [kN]", 0, 1000, 0, 0, DATA, row + LOADED, gap=30)
    pull_parts, pull = down(g, force, SHAPE, row + LOADED, gap=30)
    middle, middle_nodes = edge_nodes(g, "Middle line", [(0, y0 + 4, 0)], [(12, y0 + 4, 0)], 30, row + LOADED, gap=30)
    where = slider(g, "C load node", 0, 30, 15, 0, DATA, row + LOADED, gap=30)
    at = component(g, LIST_ITEM, "List Item", "Loaded node", ELEMENT, row + LOADED, gap=30)
    wire(inp(at, 0), out(middle_nodes))
    wire(inp(at, 1), where)
    point = component(g, POINT_LOAD, "Point Load", "Point load", PART, row + LOADED)
    wire(inp(point, 0), out(at))
    wire(inp(point, 1), out(pull))
    model, run = solved(g, row, shell, [on_x, on_y], [uniform, point])
    lines_parts, lines = traced(g, row, "Slab", run, (6.0, y0 + 4.0, 0.0), True, 0.35)
    groups = [("C1 - Slab: 12 x 8 m, 30 x 20 quads, concrete 250 mm", parts, ORANGE),
              ("C2 - Simply supported: pinned along all four edges", along_x + along_y + [on_x, on_y], GREEN),
              ("C3 - A uniform load, and a point load the sliders set and walk along its middle",
               [size, uniform, force, where, at, point] + pressure_parts + pull_parts + middle, RED),
              ("C4 - One load pattern, assembled and solved", model, BLUE),
              ("C5 - Principal Stress Lines, in bending", lines_parts, PURPLE)]
    return groups


def cantilever_shell(doc, g, row):
    """D: a barrel canopy right of C, 7 m out from its root and 6 m wide, curving 1.5 m up across
    its width - a flat mesh, its nodes lifted onto a parabola - built in along its root, under its
    own weight: stress lines in its curved surface. Laid out on columns of its own, which the
    lifting takes more of than a flat plate needs: the shell along the first row, the supports on
    the second, and the load, the analysis and the lines along the last, left to right."""
    x0, y0, out_, width, rise = 15.0, -14.0, 7.0, 6.0, 1.5
    flat_ = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Plan, 7 x 6 m", 1, row)
    for i, (x, y) in enumerate([(x0, y0), (x0 + out_, y0), (x0 + out_, y0 + width)]):
        points_into(inp(flat_, i), [(x, y, 0)])
    plan = component(g, MESH_PLANE, "Mesh Plane", "35 x 30 quads", 2, row)
    wire(inp(plan, 0), out(flat_))
    integer(inp(plan, 1), 35)
    integer(inp(plan, 2), 30)
    parts_of = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Nodes and faces", 3, row)
    wire(inp(parts_of, 0), out(plan))
    xyz = component(g, DECONSTRUCT_POINT, "Deconstruct", "Node x, y", 4, row)
    wire(inp(xyz, 0), out(parts_of, 0))
    lift = expression(g, "f(y)={0}*(1-((y-({1}))/{2})^2)".format(rise, y0 + width / 2, width / 2), [out(xyz, 1)], "Parabola", 5, row)
    lifted = component(g, CONSTRUCT_POINT, "Construct Point", "Lifted", 6, row)
    wire(inp(lifted, 0), out(xyz, 0))
    wire(inp(lifted, 1), out(xyz, 1))
    wire(inp(lifted, 2), out(lift))
    canopy = component(g, CONSTRUCT_MESH, "Construct Mesh", "Canopy", 7, row)
    wire(inp(canopy, 0), out(lifted))
    faint(wire(inp(canopy, 1), out(parts_of, 1)))
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", 7, row, gap=30)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    section = component(g, PLATE_FIBER, "Plate Fiber Section", "100 mm", 8, row)
    number(inp(section, 1), 0.1)
    wire(inp(section, 2), out(concrete))
    shell = component(g, SHELL, "ASD Shell", None, 9, row)
    wire(inp(shell, 0), out(canopy))
    wire(inp(shell, 1), out(section))

    # Built in along its root: the lifted nodes at x0.
    at_root = expression(g, "f(x)=x<{0}".format(x0 + 0.001), [out(xyz, 0)], "At the root", 5, row + HELD)
    root = component(g, CULL_PATTERN, "Cull Pattern", "Root nodes", 6, row + HELD)
    wire(inp(root, 0), out(lifted))
    wire(inp(root, 1), out(at_root))
    held = component(g, SUPPORT, "Support", "Built in", 7, row + HELD)
    flat(wire(inp(held, 0), out(root)))
    for i in range(6):
        boolean(inp(held, i + 1), True)
    weight = component(g, GRAVITY, "Gravity Load", "Own weight", 8, row + SOLVED)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, 9, row + SOLVED)
    wire(inp(pattern, 1), out(weight))
    assemble = component(g, ASSEMBLE, "Assemble Model", None, 11, row + SOLVED)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), out(held)))
    flat(wire(inp(assemble, 2), out(pattern)))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, 10, row + SOLVED)
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, 11, row + SOLVED, gap=30)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    seeds = points(g, "Seed", [(x0 + out_ / 2, y0 + width / 2, rise)], 12, row + SOLVED)
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = "Cantilever shell"
    open_menus(lines)
    g.put(lines, 12, row + SOLVED, gap=30)
    faint(wire(inp(lines, PSL_MODEL), out(run)))
    wire(inp(lines, PSL_SEED), seeds)
    boolean(inp(lines, PSL_SINGLE), False)
    boolean(inp(lines, PSL_BENDING), False)
    number(inp(lines, PSL_SEPARATION), 0.25)
    number(inp(lines, PSL_TEST), 0.1)
    groups = [("D1 - Cantilever shell: a barrel 7 m out and 6 m wide, rising 1.5 m, 35 x 30 quads, concrete 100 mm",
               [flat_, plan, parts_of, xyz, lift, lifted, canopy, concrete, section, shell], ORANGE),
              ("D2 - Built in along its root", [at_root, root, held], GREEN),
              ("D3 - Its own weight", [weight, pattern], RED),
              ("D4 - Assembled and solved", [assemble, settings, run], BLUE),
              ("D5 - Principal Stress Lines, in its surface", [seeds, lines], PURPLE)]
    return groups


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def build(doc):
    groups = []
    y = 0.0
    for i, make in enumerate((cantilever_wall, deep_beam, slab, cantilever_shell)):
        g = Grid(doc)
        groups += make(doc, g, ROWS * i)
        # 12 is the canopy's column of lines, laid out on columns of its own.
        y = g.arrange(0, y, stages=(ELEMENT, PATTERN, LINES, 12), row_gap=60.0, stack_gap=30.0) + 400.0
    for name, objects, colour in groups:
        group(doc, name, objects, colour)
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
