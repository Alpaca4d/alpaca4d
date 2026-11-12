#! python3
# The Nervi umbrella: a square concrete slab cantilevering all round from one column, after the
# umbrellas of Nervi's Palazzo del Lavoro in Turin, and its principal moment lines, which run out
# from the column and round it as his ribs do. For the marketing pictures (run.sh, next to it), not
# an example of the docs.
#
# Tools/Examples/make.sh writes StressLinesNerviUmbrella.gh straight from this script, without Rhino:
#
#     ../../Examples/make.sh StressLinesNerviUmbrella.py
#
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One umbrella, 15 x 15 m and 200 mm thick, built into its column over a head 1 m square, under a
# uniform load. Principal Stress Lines draws its bending lines from one seed off the diagonals. For
# the pictures, the lines are smoothed and extruded 350 mm down into ribs, the column is a pipe 8 m
# down to a floor, and nine umbrellas stand 1 m apart, three by three, as a hall: each is analysed
# alone, as Nervi's stand apart.
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
LINE_SDL = "4C619BC9-39FD-4717-82A6-1E07EA237BBE"           # Curve > Primitive > Line SDL
FIT_CURVE = "A3F9F19E-3E6C-4AC7-97C3-946DE32C3E8E"          # Curve > Util > Fit Curve
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
EXTRUDE = "962034E9-CC27-4394-AFC4-5C16E3447CF9"            # Surface > Freeform > Extrude
PIPE = "C277F778-6FDF-4890-8F78-347EFB23C406"               # Surface > Freeform > Pipe
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
STRESS_LINES = "E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2"       # Principal Stress Lines

# Principal Stress Lines' inputs, by place. Bending to Test Dist. are plugs of its Advanced menu.
PSL_MODEL, PSL_SEED, PSL_SINGLE, PSL_BENDING, PSL_SEPARATION, PSL_TEST = 0, 2, 4, 6, 7, 8

# Concrete, in kN and m.
E, NU, DENSITY = 33000000.0, 0.2, 2500.0

# The umbrella, meshed CELLS a side; the column's head, built in, HEAD a side; the hall's umbrellas
# PITCH apart, COPIES a side.
SIDE, CELLS, HEAD, HEIGHT = 15.0, 60, 1.0, 8.0
PITCH, COPIES = 16.0, 3

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




# ---------------------------------------------------------------- the umbrella

# Columns, in the order the data flows, and rows, one per part of the model. Each group keeps to rows
# and columns of its own, so that no two overlap.
DATA, SHAPE, MESHED, ELEMENT, PART, PATTERN, MODEL, LINES, RIBS, DRAWN, HALL = range(11)
SLAB, HELD, LOADED, SOLVED = range(4)


def build(doc):
    g = Grid(doc)
    middle = SIDE / 2

    # 1 - The umbrella's slab.
    outline = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "15 x 15 m", SHAPE, SLAB)
    points_into(inp(outline, 0), [(0, 0, 0)])
    points_into(inp(outline, 1), [(SIDE, 0, 0)])
    points_into(inp(outline, 2), [(SIDE, SIDE, 0)])
    mesh = component(g, MESH_PLANE, "Mesh Plane", "60 x 60 quads", MESHED, SLAB)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), CELLS)
    integer(inp(mesh, 2), CELLS)
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", MESHED, SLAB, gap=30)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    section = component(g, PLATE_FIBER, "Plate Fiber Section", "200 mm", ELEMENT, SLAB)
    number(inp(section, 1), 0.2)
    wire(inp(section, 2), out(concrete))
    shell = component(g, SHELL, "ASD Shell", None, PART, SLAB)
    wire(inp(shell, 0), out(mesh))
    wire(inp(shell, 1), out(section))

    # 2 - Built into the column over its head: the nodes within half a metre of the middle.
    step = SIDE / CELLS
    count = int(round(HEAD / step)) + 1
    across = component(g, SERIES, "Series", "Across the head", SHAPE, HELD)
    number(inp(across, 0), middle - HEAD / 2)
    number(inp(across, 1), step)
    integer(inp(across, 2), count)
    head = component(g, CONSTRUCT_POINT, "Construct Point", "Column head", MESHED, HELD)
    wire(inp(head, 0), out(across))
    wire(inp(head, 1), out(across)).DataMapping = GH_DataMapping.Graft
    built_in = component(g, SUPPORT, "Support", "Built in", ELEMENT, HELD)
    flat(wire(inp(built_in, 0), out(head)))
    for i in range(6):
        boolean(inp(built_in, i + 1), True)

    # 3 - A uniform load, down.
    size = slider(g, "UDL [kN/m2]", 0, 20, 5, 1, DATA, LOADED)
    minus = emit(EXPRESSION, "The Expression component")
    minus.SetInitCode("f(x)=-x")
    minus.NickName = "Minus"
    g.put(minus, SHAPE, LOADED)
    wire(inp(minus, 0), size)
    pressure = component(g, UNIT_Z, "Unit Z", "Down", MESHED, LOADED)
    wire(inp(pressure, 0), out(minus))
    uniform = component(g, MESH_LOAD, "Mesh Load", "Uniform load", ELEMENT, LOADED)
    wire(inp(uniform, 1), out(pressure))

    # 4 - Assembled and solved.
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, SOLVED)
    wire(inp(pattern, 1), out(uniform))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, PATTERN, SOLVED, gap=30)
    assemble = component(g, ASSEMBLE, "Assemble Model", None, MODEL, SOLVED)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), out(built_in)))
    flat(wire(inp(assemble, 2), out(pattern)))
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, MODEL, SOLVED, gap=30)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))

    # 5 - The bending lines, from a seed off the diagonals, where the field is symmetric.
    seed = points(g, "Seed", [(0.69 * SIDE, 0.61 * SIDE, 0.0)], LINES, SOLVED)
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = "Nervi umbrella"
    open_menus(lines)
    g.put(lines, LINES, SOLVED, gap=30)
    faint(wire(inp(lines, PSL_MODEL), out(run)))
    wire(inp(lines, PSL_SEED), seed)
    boolean(inp(lines, PSL_SINGLE), False)
    boolean(inp(lines, PSL_BENDING), True)
    number(inp(lines, PSL_SEPARATION), 0.6)
    number(inp(lines, PSL_TEST), 0.24)

    # 6 - For the pictures: the lines, smoothed, as ribs 350 mm deep; the column a pipe down to the
    # floor; and nine umbrellas, three by three, 1 m apart.
    depth = component(g, UNIT_Z, "Unit Z", "Rib depth", RIBS, SOLVED)
    number(inp(depth, 0), -0.35)
    ribs = []
    for i, name in enumerate(("Ribs SL1", "Ribs SL2")):
        smooth = component(g, FIT_CURVE, "Fit Curve", "Smooth SL" + str(i + 1), RIBS, SOLVED, gap=30)
        wire(inp(smooth, 0), out(lines, i))
        integer(inp(smooth, 1), 3)
        number(inp(smooth, 2), 0.005)
        r = component(g, EXTRUDE, "Extrude", name, DRAWN, SOLVED, gap=30 if i else 0)
        wire(inp(r, 0), out(smooth))
        wire(inp(r, 1), out(depth))
        ribs += [smooth, r]
    down_ = component(g, UNIT_Z, "Unit Z", "Down the column", RIBS, SOLVED, gap=30)
    number(inp(down_, 0), -1.0)
    axis = component(g, LINE_SDL, "Line SDL", "Column axis", RIBS, SOLVED, gap=30)
    points_into(inp(axis, 0), [(middle, middle, 0)])
    wire(inp(axis, 1), out(down_))
    number(inp(axis, 2), HEIGHT)
    pipe = component(g, PIPE, "Pipe", "Column shaft", DRAWN, SOLVED, gap=30)
    wire(inp(pipe, 0), out(axis))
    number(inp(pipe, 1), 0.45)
    if pipe.Params.Input.Count > 2:
        integer(inp(pipe, 2), 1)
    offsets = component(g, SERIES, "Series", "Hall offsets", RIBS, SOLVED, gap=30)
    number(inp(offsets, 0), -PITCH)
    number(inp(offsets, 1), PITCH)
    integer(inp(offsets, 2), COPIES)
    places = component(g, CONSTRUCT_POINT, "Construct Point", "Places", DRAWN, SOLVED, gap=30)
    wire(inp(places, 0), out(offsets))
    wire(inp(places, 1), out(offsets)).DataMapping = GH_DataMapping.Graft
    hall = []
    for name, source in (("Hall ribs SL1", out(ribs[1])), ("Hall ribs SL2", out(ribs[3])), ("Hall slabs", out(mesh)), ("Hall columns", out(pipe))):
        m = component(g, MOVE, "Move", name, HALL, SOLVED, gap=30 if hall else 0)
        flat(faint(wire(inp(m, 0), source)))
        wire(inp(m, 1), out(places)).DataMapping = GH_DataMapping.Graft
        hall.append(m)
    floor = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Floor outline", RIBS, SOLVED, gap=30)
    reach = PITCH * (COPIES // 2) + SIDE
    points_into(inp(floor, 0), [(-reach, -reach, -HEIGHT)])
    points_into(inp(floor, 1), [(SIDE + reach, -reach, -HEIGHT)])
    points_into(inp(floor, 2), [(SIDE + reach, SIDE + reach, -HEIGHT)])
    ground = component(g, MESH_PLANE, "Mesh Plane", "Floor", DRAWN, SOLVED, gap=30)
    wire(inp(ground, 0), out(floor))
    integer(inp(ground, 1), 1)
    integer(inp(ground, 2), 1)

    g.arrange(0, 0, stages=(ELEMENT, PATTERN, LINES, RIBS), row_gap=60.0, stack_gap=30.0)
    group(doc, "1 - The Nervi umbrella: concrete, 15 x 15 m, 200 mm, in 60 x 60 quads", [outline, mesh, concrete, section, shell], ORANGE)
    group(doc, "2 - Built into one column over a head 1 m square", [across, head, built_in], GREEN)
    group(doc, "3 - A uniform load", [size, minus, pressure, uniform], RED)
    group(doc, "4 - Assembled and solved", [pattern, settings, assemble, run], BLUE)
    group(doc, "5 - Principal Stress Lines, in bending", [seed, lines], PURPLE)
    group(doc, "6 - For the pictures: the lines as ribs 350 mm deep, the column, nine umbrellas as a hall",
          [depth] + ribs + [down_, axis, pipe, offsets, places] + hall + [floor, ground], GREY)
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
