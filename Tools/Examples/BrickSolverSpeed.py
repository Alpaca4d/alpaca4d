#! python3
# A speed test of the solver: a concrete block of bricks, one slider away from fifteen hundred unknowns
# or a hundred and fifty thousand, and a System list to solve it with Mumps on several cores - or with
# BandSPD or UmfPack, on one, to compare.
#
# make.sh, next to it, writes BrickSolverSpeed.gh straight from this script, without Rhino. It also
# runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# An 8 x 8 m concrete block 6 m deep - a pile cap, the foot of a bridge pier - pinned all along its
# bottom, with four columns bearing on its top and its own weight. Bricks per metre sets the mesh:
# 8n x 8n x 6n SSP bricks, and 3 (8n+1)^2 6n unknowns above the pinned bottom. A block this compact
# is where a band solver suffers most and Mumps gains most: its band is a whole cross-section wide.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode)
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
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
VIEW_RESULTS = "4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3"       # View Results

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)

SIDE, DEPTH = 8.0, 6.0        # the block: 8 x 8 m in plan, 6 m deep
COLUMNS_AT = (2.0, 6.0)       # the four columns stand at these x and y, on the mesh for any n
COLUMN_LOAD = -5000.0         # kN each


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


def value_list(grid, name, items, selected, col, row=None, gap=0):
    """A drop-down value list of texts; selected is the index of the one picked."""
    v = GH_ValueList()
    v.CreateAttributes()
    v.NickName = name
    v.ListMode = GH_ValueListMode.DropDown
    v.ListItems.Clear()
    for item in items:
        v.ListItems.Add(GH_ValueListItem(item, '"' + item + '"'))
    v.SelectItem(selected)
    return grid.put(v, col, row, gap)


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


# ---------------------------------------------------------------- the block

# Columns, in the order the data flows...
INPUTS, NUMBERS, SERIES_, POINTS, MESHES, BRICKS_, NAMES, ELEMENTS, LOADS, PATTERN = range(10)
# ...and rows, one per part of the model.
BLOCK, BEARINGS, COLUMN_LOADS, WEIGHT = range(4)

# The model, its size and its analysis, in a grid of their own to the right.
MODEL, VIEW, COUNTS = range(3)
ASSEMBLED, SETTINGS, ANALYSED = range(3)


def build(doc):
    g = Grid(doc)

    # 1 - What scales the model: bricks per metre, the same in every direction.
    per_metre = slider(g, "Bricks per metre", 1, 5, 3, 0, INPUTS)
    across = expression(g, "f(n)=8*n", [per_metre], "Bricks across", NUMBERS, BLOCK)
    layer = expression(g, "f(n)=-1/n", [per_metre], "Layer depth", NUMBERS, BLOCK)
    faces = expression(g, "f(n)=6*n+1", [per_metre], "Layer faces", NUMBERS, BLOCK)

    # 2 - The block: its plan meshed once at the bottom, and copies of it from the top down, a
    # layer apart, made into bricks between each pair.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Plan", POINTS, BLOCK)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), SIDE, 0, 0)
    point(inp(plan, 2), SIDE, SIDE, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, BLOCK)
    wire(inp(plan_mesh, 0), out(plan))
    faint(wire(inp(plan_mesh, 1), out(across)))
    faint(wire(inp(plan_mesh, 2), out(across)))

    levels = component(g, SERIES, "Series", "Levels, top down", SERIES_, BLOCK)
    number(inp(levels, 0), DEPTH)
    wire(inp(levels, 1), out(layer))
    wire(inp(levels, 2), out(faces))
    lift = vector(g, UNIT_Z, "Up to each level", out(levels), POINTS, BLOCK)
    level_meshes = component(g, MOVE, "Move", "Level meshes", MESHES, BLOCK)
    wire(inp(level_meshes, 0), out(plan_mesh))
    wire(inp(level_meshes, 1), out(lift))
    brick_meshes = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, BLOCK)
    wire(inp(brick_meshes, 0), out(level_meshes))

    # 3 - Concrete.
    concrete_values = [text(g, "Concrete", "C30/37", NAMES, BLOCK),
                       text(g, "E [kN/m2]", "33000000", NAMES, BLOCK),
                       text(g, "G [kN/m2]", "13750000", NAMES, BLOCK),
                       text(g, "Poisson", "0.2", NAMES, BLOCK),
                       text(g, "Density [kg/m3]", "2500", NAMES, BLOCK)]
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", ELEMENTS, BLOCK)
    for i, source in enumerate(concrete_values):
        wire(inp(concrete, i), source)
    bricks = component(g, BRICK, "SSP Brick", "Block", LOADS, BLOCK)
    wire(inp(bricks, 0), out(brick_meshes))
    wire(inp(bricks, 1), out(concrete))

    # 4 - Bearings and loads: pinned at every node of the bottom; four columns on the top, and the
    # block's own weight, in one load pattern.
    bottom = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Bottom nodes", BRICKS_, BEARINGS)
    faint(wire(inp(bottom, 0), out(plan_mesh)))
    held = toggle(g, "Held", True, NAMES, BEARINGS)
    free = toggle(g, "Free", False, NAMES, BEARINGS)
    support = component(g, SUPPORT, "Support", "Pinned bottom", ELEMENTS, BEARINGS)
    wire(inp(support, 0), out(bottom, 0))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    feet = component(g, CONSTRUCT_POINT, "Construct Point", "Column feet", POINTS, COLUMN_LOADS)
    number(inp(feet, 0), *COLUMNS_AT)
    number(inp(feet, 1), *COLUMNS_AT).DataMapping = GH_DataMapping.Graft
    number(inp(feet, 2), DEPTH)
    column_force = vector(g, UNIT_Z, "5000 kN down", COLUMN_LOAD, ELEMENTS, COLUMN_LOADS)
    column_loads = component(g, POINT_LOAD, "Point Load", "Column loads", LOADS, COLUMN_LOADS)
    flat(wire(inp(column_loads, 0), out(feet)))
    wire(inp(column_loads, 1), out(column_force))

    gravity = component(g, GRAVITY, "Gravity Load", None, LOADS, WEIGHT)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, WEIGHT)
    flat(wire(inp(pattern, 1), out(column_loads), out(gravity)))

    right = g.arrange(0, 0, stages=(NUMBERS, NAMES, ELEMENTS))

    # 5 - Assemble and look; the size; the analysis, with the solver to pick, switched off.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(bricks)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    view = component(m, MODEL_VIEW, "Model View", None, VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    brick_count = component(m, LIST_LENGTH, "List Length", "Bricks", COUNTS, ASSEMBLED)
    flat(faint(wire(inp(brick_count, 0), out(bricks))))
    unknowns = expression(m, "f(n)=3*(8*n+1)^2*6*n", [per_metre], "Unknowns", COUNTS, ASSEMBLED)
    unknowns_shown = readout(m, "Unknowns (DOF)", out(unknowns), COUNTS, ASSEMBLED)

    system = value_list(m, "System", ["Auto", "Mumps", "BandSPD", "UmfPack"], 0, MODEL, SETTINGS)
    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, VIEW, SETTINGS)
    wire(inp(settings, 3), system)

    run = component(m, RUN_ANALYSIS, "Run Analysis", None, MODEL, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    results = component(m, VIEW_RESULTS, "View Results", None, VIEW, ANALYSED)
    wire(inp(results, 0), out(run))
    log = readout(m, "OpenSees log", out(run, 1), COUNTS, ANALYSED, width=300.0, height=160.0)
    run.Locked = True
    results.Locked = True

    m.arrange(right + 160, 0)

    group(doc, "1 - Size: drag Bricks per metre", [per_metre, across, layer, faces], RED)
    group(doc, "2 - The block, 8 x 8 m and 6 m deep, in layers of bricks",
          [plan, plan_mesh, levels, lift, level_meshes, brick_meshes], ORANGE)
    group(doc, "3 - Concrete", [concrete, bricks] + concrete_values, PURPLE)
    group(doc, "4 - Pinned bottom, four columns and the self weight",
          [bottom, held, free, support, feet, column_force, column_loads, gravity, pattern], GREEN)
    group(doc, "5 - Assemble Model, and how big the model is", [assemble, view, brick_count, unknowns, unknowns_shown], BLUE)
    group(doc, "6 - Solve: pick the System, right-click Analysis Settings for the cores, then Enable Run Analysis",
          [system, settings, run, results, log], GREY)

    # Only Model View and View Results draw the block; the construction geometry stays out of the viewport.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid and obj.InstanceGuid != results.InstanceGuid:
            obj.Hidden = True

    note(doc, "How to test the solver",
         "Solver speed test: a concrete block of bricks.\n\n"
         "An 8 x 8 m block 6 m deep, pinned all along its bottom, four 5000 kN columns on top and its own "
         "weight. Bricks per metre n sets the mesh: 8n x 8n x 6n bricks.\n\n"
         "The solve alone, in OpenSees, on an Apple M2 Pro (Run Analysis adds writing, building and recording):\n"
         "  n   bricks   unknowns   BandSPD          UmfPack   Mumps, all cores\n"
         "  2    3 072     10 404   0.4 s            0.3 s     0.1 s\n"
         "  3   10 368     33 750   3.7 s            2.2 s     0.5 s\n"
         "  4   24 576     78 408   29 s, 4 GB       fails     1.6 s, 2 GB\n"
         "  5   48 000    151 290   over 6 GB        fails     4.2 s, 5 GB\n\n"
         "1. Display > Canvas Widgets > Profiler: each component shows how long it took.\n"
         "2. Run Analysis and View Results are disabled, so that dragging the slider never starts OpenSees. "
         "Right-click > Enable both to solve.\n"
         "3. System: Auto and Mumps solve with Mumps on every core (right-click Analysis Settings > Cores for "
         "Mumps to use fewer); BandSPD and UmfPack on one. Auto needs an OpenSees with Mumps - Alpaca4d > "
         "Settings > Set OpenSees Executable - and says in the log when it has to use BandSPD instead.\n"
         "4. Compare Run Analysis's time. Mind BandSPD from n = 4, and n = 5 with any System on a machine with "
         "less than 16 GB.",
         0, -460, 660, 390)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Brick solver speed test built. Enable Run Analysis and View Results to solve.")


if __name__ == "__main__":
    main()
