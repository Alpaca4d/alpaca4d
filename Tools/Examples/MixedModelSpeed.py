#! python3
# A speed test of Assemble Model: a building of beams, columns, a shell slab and a brick raft, one
# slider away from a few hundred elements or tens of thousands.
#
# make.sh, next to it, writes MixedModelSpeed.gh straight from this script, without Rhino. It also
# runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 12 x 8 m concrete slab on a grid of steel beams 4 m apart, on steel columns at every crossing,
# standing on a concrete raft of bricks. Everything meets everything else at shared nodes: the beams
# and columns are cut at every node of the slab's mesh, and the raft's top is meshed on the same
# spacing, so the column feet land on its nodes. Divisions sets the spacing, 4 m / Divisions, for
# all of it. One load pattern holds a point load in every bay, a line load on the beams along X, a
# load on the slab and the self weight.
#
# Turn on Grasshopper's profiler (Display > Canvas Widgets > Profiler) and drag Divisions: Assemble
# Model shows how long it took. Run Analysis and View Results are disabled, so that dragging the
# slider does not start OpenSees; enable them to solve.
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
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
LINE_SDL = "4C619BC9-39FD-4717-82A6-1E07EA237BBE"           # Curve > Primitive > Line SDL
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LINEAR_LOAD = "8593AC69-D975-4566-97FC-27D7D4A4D415"        # Linear Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
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

LENGTH, WIDTH = 12.0, 8.0     # the plan, in bays of 4 m: three along X, two along Y
BAY = 4.0


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
    part of the building, so that each part's chain of components sits on one line. Column widths
    and row heights are the objects' own, measured once Grasshopper has laid them out."""

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
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def slider(grid, name, lo, hi, value, decimals, col, row=None, gap=0, accuracy=None):
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    if accuracy is None:
        accuracy = GH_SliderAccuracy.Integer if decimals == 0 else GH_SliderAccuracy.Float
    s.Slider.Type = accuracy
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
    """A one-line panel holding a text or a number, such as a section name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def readout(grid, name, source, col, row=None, gap=0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 110.0, 20.0)
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


def series(grid, start, step, count, nickname, col, row=None, gap=0):
    s = component(grid, SERIES, "Series", nickname, col, row, gap)
    number(inp(s, 0), start)
    number(inp(s, 1), step)
    integer(inp(s, 2), count)
    return s


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


# ---------------------------------------------------------------- the building

# Columns, in the order the data flows...
INPUTS, NUMBERS, SERIES_, POINTS, LINES, DIVIDED, POLYLINES, SEGMENTS, NAMES, SECTIONS, ELEMENTS, LOADS, PATTERN = range(13)
# ...and rows, one per part of the building.
PLAN, SLAB, BEAMS_X, BEAMS_Y, COLUMNS, RAFT, BEARINGS, BAYS, WEIGHT = range(9)

# The model, its counts and its analysis, in a grid of their own to the right.
MODEL, VIEW, COUNTS = range(3)
ASSEMBLED, ANALYSED = range(2)


def build(doc):
    g = Grid(doc)

    # 1 - What scales the model: one spacing for the slab's mesh, the cuts in the beams and
    # columns and the raft's bricks, so that everything meets at shared nodes.
    divisions = slider(g, "Divisions per 4 m", 2, 48, 10, 0, INPUTS, accuracy=GH_SliderAccuracy.Even)
    height = slider(g, "Storey height [m]", 3, 6, 4, 1, INPUTS)
    thickness = slider(g, "Raft thickness [m]", 0.5, 2, 1, 2, INPUTS)
    layers = slider(g, "Raft layers", 1, 4, 2, 0, INPUTS)

    along_x = expression(g, "f(n)=3*n", [divisions], "Elements along X", NUMBERS, BEAMS_X)
    along_y = expression(g, "f(n)=2*n", [divisions], "Elements along Y", NUMBERS, BEAMS_Y)
    step = expression(g, "f(t,k)=-t/k", [thickness, layers], "Layer depth", NUMBERS, RAFT)
    levels = expression(g, "f(k)=k+1", [layers], "Layer faces", NUMBERS, RAFT)
    depth = expression(g, "f(t)=-t", [thickness], "Raft bottom", NUMBERS, BEARINGS)

    # 2 - The plan, meshed once at ground level: lifted, it is the slab; lowered layer by layer,
    # the raft.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Plan", POINTS, PLAN)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), LENGTH, 0, 0)
    point(inp(plan, 2), LENGTH, WIDTH, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", LINES, PLAN)
    wire(inp(plan_mesh, 0), out(plan))
    faint(wire(inp(plan_mesh, 1), out(along_x)))
    faint(wire(inp(plan_mesh, 2), out(along_y)))

    up = vector(g, UNIT_Z, "Up a storey", height, POINTS, SLAB)
    slab = component(g, MOVE, "Move", "Slab mesh", LINES, SLAB)
    wire(inp(slab, 0), out(plan_mesh))
    wire(inp(slab, 1), out(up))

    offsets = series(g, 0, 0, 2, "Layer offsets", SERIES_, RAFT)
    wire(inp(offsets, 1), out(step))
    wire(inp(offsets, 2), out(levels))
    down = vector(g, UNIT_Z, "Down by layer", out(offsets), POINTS, RAFT)
    faces = component(g, MOVE, "Move", "Raft faces", LINES, RAFT)
    wire(inp(faces, 0), out(plan_mesh))
    wire(inp(faces, 1), out(down))
    bricks_mesh = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, DIVIDED, RAFT)
    wire(inp(bricks_mesh, 0), out(faces))

    lowest = vector(g, UNIT_Z, "Down to the bottom", out(depth), POINTS, BEARINGS)
    bottom = component(g, MOVE, "Move", "Raft bottom", LINES, BEARINGS)
    wire(inp(bottom, 0), out(plan_mesh))
    wire(inp(bottom, 1), out(lowest))
    bottom_nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Raft bottom nodes", DIVIDED, BEARINGS)
    wire(inp(bottom_nodes, 0), out(bottom))

    # 3 - The steel: beams along X and along Y every 4 m at the slab, columns at every crossing,
    # each line cut where the slab's mesh has a node.
    grid_x = series(g, 0, BAY, 4, "Grid X", SERIES_, BEAMS_Y)
    grid_y = series(g, 0, BAY, 3, "Grid Y", SERIES_, BEAMS_X)

    starts_x = component(g, CONSTRUCT_POINT, "Construct Point", "Beam starts X", POINTS, BEAMS_X)
    wire(inp(starts_x, 1), out(grid_y))
    wire(inp(starts_x, 2), height)
    starts_y = component(g, CONSTRUCT_POINT, "Construct Point", "Beam starts Y", POINTS, BEAMS_Y)
    wire(inp(starts_y, 0), out(grid_x))
    wire(inp(starts_y, 2), height)
    feet = component(g, CONSTRUCT_POINT, "Construct Point", "Column feet", POINTS, COLUMNS)
    faint(wire(inp(feet, 0), out(grid_x)))
    # Grafted: every Y with every X, twelve column feet.
    faint(wire(inp(feet, 1), out(grid_y))).DataMapping = GH_DataMapping.Graft

    lines = []
    for start, axis, length, count, name, row in ((starts_x, UNIT_X, LENGTH, out(along_x), "Beams X", BEAMS_X),
                                                  (starts_y, UNIT_Y, WIDTH, out(along_y), "Beams Y", BEAMS_Y),
                                                  (feet, UNIT_Z, None, divisions, "Columns", COLUMNS)):
        direction = vector(g, axis, "Along", 1.0, POINTS, row)
        line = component(g, LINE_SDL, "Line SDL", name + " lines", LINES, row)
        wire(inp(line, 0), out(start))
        wire(inp(line, 1), out(direction))
        if length is None:
            faint(wire(inp(line, 2), height))
        else:
            number(inp(line, 2), length)
        divide = component(g, DIVIDE, "Divide Curve", "Cut " + name, DIVIDED, row)
        wire(inp(divide, 0), out(line))
        faint(wire(inp(divide, 1), count))
        polyline = component(g, POLYLINE, "PolyLine", name + " polylines", POLYLINES, row)
        wire(inp(polyline, 0), out(divide))
        pieces = component(g, EXPLODE, "Explode", name + " pieces", SEGMENTS, row)
        wire(inp(pieces, 0), out(polyline))
        lines.append((direction, line, divide, polyline, pieces))

    # 4 - Materials and sections: S355 steel, IPE 400 beams, HEB 300 columns; C30/37 concrete,
    # a 250 mm slab and the raft.
    grade = text(g, "Steel grade", "S355", NAMES, BEAMS_X)
    steel = component(g, MATERIAL, "Material Library Elastic", None, NAMES, BEAMS_X)
    wire(inp(steel, 0), grade)
    beam_name = text(g, "Beams", "IPE400", NAMES, BEAMS_Y)
    column_name = text(g, "Columns", "HEB300", NAMES, COLUMNS)
    beam_section = component(g, SECTION, "Steel Section Library", "Beam section", SECTIONS, BEAMS_X)
    wire(inp(beam_section, 0), beam_name)
    wire(inp(beam_section, 1), out(steel))
    column_section = component(g, SECTION, "Steel Section Library", "Column section", SECTIONS, COLUMNS)
    wire(inp(column_section, 0), column_name)
    faint(wire(inp(column_section, 1), out(steel)))

    concrete_values = [text(g, "Concrete", "C30/37", NAMES, SLAB),
                       text(g, "E [kN/m2]", "33000000", NAMES, SLAB),
                       text(g, "G [kN/m2]", "13750000", NAMES, SLAB),
                       text(g, "Poisson", "0.2", NAMES, SLAB),
                       text(g, "Density [kg/m3]", "2500", NAMES, SLAB)]
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", SECTIONS, SLAB)
    for i, source in enumerate(concrete_values):
        wire(inp(concrete, i), source)
    slab_name = text(g, "Slab name", "Slab 250", SECTIONS, SLAB)
    slab_thickness = text(g, "Slab [m]", "0.25", SECTIONS, SLAB)
    plate = component(g, PLATE_FIBER, "Plate Fiber Section", "Slab section", SECTIONS, SLAB)
    wire(inp(plate, 0), slab_name)
    wire(inp(plate, 1), slab_thickness)
    wire(inp(plate, 2), out(concrete))

    # 5 - The elements.
    beams_x = component(g, BEAM, "ForceBeamColumn", "Beams X", ELEMENTS, BEAMS_X)
    flat(wire(inp(beams_x, 0), out(lines[0][4])))
    wire(inp(beams_x, 1), out(beam_section))
    beams_y = component(g, BEAM, "ForceBeamColumn", "Beams Y", ELEMENTS, BEAMS_Y)
    flat(wire(inp(beams_y, 0), out(lines[1][4])))
    faint(wire(inp(beams_y, 1), out(beam_section)))
    columns = component(g, BEAM, "ForceBeamColumn", "Columns", ELEMENTS, COLUMNS)
    flat(wire(inp(columns, 0), out(lines[2][4])))
    wire(inp(columns, 1), out(column_section))
    shells = component(g, SHELL, "ASD Shell", "Slab", ELEMENTS, SLAB)
    faint(wire(inp(shells, 0), out(slab)))
    wire(inp(shells, 1), out(plate))
    bricks = component(g, BRICK, "SSP Brick", "Raft", ELEMENTS, RAFT)
    wire(inp(bricks, 0), out(bricks_mesh))
    faint(wire(inp(bricks, 1), out(concrete)))

    # 6 - Bearings and loads: the raft pinned along its whole bottom; a point load in the middle
    # of every bay, a line load on the beams along X, a load over the slab and the self weight,
    # all in one load pattern.
    held = toggle(g, "Held", True, SECTIONS, BEARINGS)
    free = toggle(g, "Free", False, SECTIONS, BEARINGS)
    support = component(g, SUPPORT, "Support", "Pinned raft", ELEMENTS, BEARINGS)
    wire(inp(support, 0), out(bottom_nodes, 0))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    bay_x = series(g, BAY / 2, BAY, 3, "Bay middles X", SERIES_, BAYS)
    bay_y = series(g, BAY / 2, BAY, 2, "Bay middles Y", SERIES_, BAYS)
    middles = component(g, CONSTRUCT_POINT, "Construct Point", "Bay middles", POINTS, BAYS)
    wire(inp(middles, 0), out(bay_x))
    wire(inp(middles, 1), out(bay_y)).DataMapping = GH_DataMapping.Graft
    faint(wire(inp(middles, 2), height))
    point_force = vector(g, UNIT_Z, "50 kN down", -50.0, ELEMENTS, BAYS)
    point_load = component(g, POINT_LOAD, "Point Load", "Bay loads", LOADS, BAYS)
    faint(wire(inp(point_load, 0), out(middles)))
    wire(inp(point_load, 1), out(point_force))

    line_force = vector(g, UNIT_Z, "10 kN/m down", -10.0, ELEMENTS, BEAMS_X, gap=20)
    line_load = component(g, LINEAR_LOAD, "Linear Load", "Beam loads", LOADS, BEAMS_X)
    wire(inp(line_load, 0), out(beams_x))
    wire(inp(line_load, 1), out(line_force))

    area_force = vector(g, UNIT_Z, "3 kN/m2 down", -3.0, ELEMENTS, SLAB, gap=20)
    mesh_load = component(g, MESH_LOAD, "Mesh Load", "Slab load", LOADS, SLAB)
    wire(inp(mesh_load, 1), out(area_force))

    gravity = component(g, GRAVITY, "Gravity Load", None, LOADS, WEIGHT)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, WEIGHT)
    flat(wire(inp(pattern, 1), out(point_load), out(line_load), out(mesh_load), out(gravity)))

    right = g.arrange(0, 0, stages=(NUMBERS, NAMES, ELEMENTS))

    # 7 - Assemble, look, count; the analysis, switched off.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(beams_x), out(beams_y), out(columns), out(shells), out(bricks)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    view = component(m, MODEL_VIEW, "Model View", None, VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    counts = []
    for name, sources in (("Beam elements", [out(beams_x), out(beams_y), out(columns)]),
                          ("Shells", [out(shells)]),
                          ("Bricks", [out(bricks)])):
        length = component(m, LIST_LENGTH, "List Length", name, COUNTS, ASSEMBLED)
        flat(faint(wire(inp(length, 0), *sources)))
        counts.append(length)
    mass = readout(m, "Mass [kg]", out(assemble, 1), COUNTS, ASSEMBLED)

    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    run = component(m, RUN_ANALYSIS, "Run Analysis", None, VIEW, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    results = component(m, VIEW_RESULTS, "View Results", None, COUNTS, ANALYSED)
    wire(inp(results, 0), out(run))
    run.Locked = True
    results.Locked = True

    m.arrange(right + 160, 0)

    group(doc, "1 - Size: drag Divisions", [divisions, height, thickness, layers, along_x, along_y, step, levels, depth], RED)
    group(doc, "2 - Geometry: slab and raft meshed on one spacing, steel cut on it",
          [plan, plan_mesh, up, slab, offsets, down, faces, bricks_mesh, lowest, bottom, bottom_nodes,
           grid_x, grid_y, starts_x, starts_y, feet] + [o for parts in lines for o in parts], ORANGE)
    group(doc, "3 - Materials and sections", [grade, steel, beam_name, column_name, beam_section, column_section,
                                              concrete, slab_name, slab_thickness, plate] + concrete_values, PURPLE)
    group(doc, "4 - Elements, bearings and one load pattern",
          [beams_x, beams_y, columns, shells, bricks, held, free, support, bay_x, bay_y, middles, point_force,
           point_load, line_force, line_load, area_force, mesh_load, gravity, pattern], GREEN)
    group(doc, "5 - Assemble Model: the time to watch", [assemble, view] + counts + [mass], BLUE)
    group(doc, "6 - Analysis: disabled, right-click > Enable to solve in OpenSees", [settings, run, results], GREY)

    # Only Model View draws the building; the construction geometry stays out of the viewport.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid and obj.InstanceGuid != results.InstanceGuid:
            obj.Hidden = True

    note(doc, "How to watch the speed",
         "Mixed model speed test.\n\n"
         "A 12 x 8 m concrete slab of shells on a 4 m grid of IPE 400 beams and HEB 300 columns, standing "
         "on a concrete raft of bricks. The beams and columns are cut at every node of the slab's mesh and "
         "the raft is meshed on the same spacing, so slab, steel and raft are one connected model. One load "
         "pattern: a point load in every bay, a line load on the beams along X, a slab load and gravity.\n\n"
         "1. Display > Canvas Widgets > Profiler: each component shows how long it took.\n"
         "2. Drag Divisions per 4 m. At 10 the model has a few thousand elements; at 48, tens of thousands. "
         "The counts and the mass are next to Assemble Model.\n"
         "3. Run Analysis and View Results are disabled, so the slider never starts OpenSees. Right-click > "
         "Enable both to solve the model you have.",
         0, -330, 620, 260)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Mixed model speed test built. Turn on the profiler and drag Divisions.")


if __name__ == "__main__":
    main()
