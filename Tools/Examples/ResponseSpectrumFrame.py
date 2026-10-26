#! python3
# A concrete frame shaken by the NTC 2018 design spectrum: the Response Spectrum Analysis example,
# built on the Grasshopper canvas.
#
# make.sh, next to it, writes ResponseSpectrumFrame.gh straight from this script, without Rhino:
#     ./make.sh ResponseSpectrumFrame.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A frame of two bays by one, two storeys high: 35 x 35 cm columns, 30 x 50 cm beams and a 20 cm slab
# in C30/37. The slab is one shell per bay, its corners on the columns, so it holds each floor
# together. Their own weight is mass already, from the density of the concrete; Mass from Loads adds
# the finishes (G2) in full and the floor load (Q) at psi2. Design Spectrum, on its NTC 2018 unit,
# draws the spectrum of the site, and Response Spectrum Analysis shakes the frame with it along X and
# along Y, combines the modes (CQC) and the directions (30%). View Results draws a result case:
# E, the two directions together, or Ex or Ey alone.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
LINE_SDL = "4C619BC9-39FD-4717-82A6-1E07EA237BBE"           # Curve > Primitive > Line SDL
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
RECTANGLE = "888184F3-A448-4F82-9DCA-E57EFAC8E840"          # Rectangle Cross Section
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
MASS_FROM_LOADS = "D47E2A90-6B15-4F83-A2C6-91E8B04F5D27"    # Mass from Loads
DESIGN_SPECTRUM = "8A3F61D2-4C7B-4E95-9B10-2D6E5C8F7A31"    # Design Spectrum
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
RESPONSE_SPECTRUM = "2F9C5B84-7D31-4A6E-B8E2-5C0D93A17F46"  # Response Spectrum Analysis
VIEW_RESULTS = "4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3"       # View Results

NTC_2018 = "Design Spectrum NTC 2018 (Alpaca4d)"            # Design Spectrum's NTC 2018 unit

# Design Spectrum's inputs on its NTC 2018 unit, and its outputs, by place.
S_AG, S_FO, S_TC, S_SOIL, S_TOPOGRAPHY, S_COMPONENT, S_Q = range(7)
S_SPECTRUM, S_PERIOD, S_ACCELERATION, S_CURVE, S_PARAMETERS = range(5)
# Response Spectrum Analysis's inputs and outputs, by place.
R_MODEL, R_X, R_Y, R_Z, R_MODES, R_MODAL, R_DIRECTIONAL = range(7)
R_OUT_MODEL, R_CASES, R_PERIOD, R_MASS_RATIO, R_BASE_SHEAR = range(5)

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


def component(grid, guid, what, nickname, col, row=None, gap=0, unit=None):
    c = emit(guid, what)
    if unit is not None:
        # A switcher component on another unit than its first, as picking it from its menu would. The
        # unit it leaves takes its parameters back, and writes to Rhino's command line when one is still
        # attached to the component - which outside Rhino is not there to write to. Detached first, the
        # parameters are attached again as they go.
        for plug in list(c.ActiveUnit.Inputs) + list(c.ActiveUnit.Outputs):
            if plug.Parameter.Attributes is not None:
                plug.Parameter.Attributes.Parent = None
        c.SwitchUnit(unit, False, False)
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


def graft(param):
    """Puts each item that arrives on a branch of its own."""
    param.DataMapping = GH_DataMapping.Graft
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


def value_list(grid, name, items, selected, col, row=None, gap=0):
    """A drop-down value list: items are (name, expression); text is an expression in quotes."""
    v = GH_ValueList()
    v.CreateAttributes()
    v.NickName = name
    v.ListMode = GH_ValueListMode.DropDown
    v.ListItems.Clear()
    for item_name, expression_ in items:
        v.ListItems.Add(GH_ValueListItem(item_name, expression_))
    v.SelectItem(selected)
    return grid.put(v, col, row, gap)


def toggle(grid, name, value, col, row=None, gap=0):
    t = GH_BooleanToggle()
    t.CreateAttributes()
    t.NickName = name
    t.Value = value
    return grid.put(t, col, row, gap)


def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a grade."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def panel(grid, name, source, width, height, col, row=None, gap=0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    p.AddSource(source)
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


def vector(grid, guid, nickname, factor, col, row=None, gap=0):
    """A unit vector along an axis, times factor - a number, or a source to take it from."""
    v = component(grid, guid, "The Unit X, Y and Z components", nickname, col, row, gap)
    if isinstance(factor, (int, float)):
        number(inp(v, 0), factor)
    else:
        wire(inp(v, 0), factor)
    return v


def series(grid, start, step, count, nickname, col, row=None, gap=0):
    """A Series; each of start, step and count a number or a source to take it from."""
    s = component(grid, SERIES, "Series", nickname, col, row, gap)
    for i, value in enumerate((start, step, count)):
        if isinstance(value, (int, float)):
            if i == 2:
                integer(inp(s, i), value)
            else:
                number(inp(s, i), value)
        else:
            wire(inp(s, i), value)
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


# ---------------------------------------------------------------- the frame

# Columns, in the order the data flows...
INPUTS, NUMBERS, POINTS, LEVELS, LINES, NAMES, SECTIONS, ELEMENTS, LOADS, MASSES = range(10)
# ...and rows, one per part of the frame.
COLUMNS, BEAMS_X, BEAMS_Y, SLAB, FEET, FLOOR_G2, FLOOR_Q = range(7)

# The spectrum, the analysis and its results, in a grid of their own to the right.
SITE, SPECTRUM, ANALYSIS, CHOICE, RESULTS = range(5)
SHAKE, READ = range(2)


def build(doc):
    g = Grid(doc)

    # 1 - The frame: two bays along X, one along Y, and its storeys.
    bay_x = slider(g, "Bay X [m]", 3, 10, 6, 1, INPUTS, BEAMS_X)
    bay_y = slider(g, "Bay Y [m]", 3, 10, 5, 1, INPUTS, BEAMS_Y)
    storey = slider(g, "Storey [m]", 2.5, 5, 3.2, 1, INPUTS, COLUMNS)
    storeys = slider(g, "Storeys", 1, 6, 2, 0, INPUTS, COLUMNS, gap=10)

    span = expression(g, "f(b)=2*b", [bay_x], "Length", NUMBERS, SLAB)
    grid_x = series(g, 0, bay_x, 3, "Column lines X", NUMBERS, COLUMNS)
    grid_y = series(g, 0, bay_y, 2, "Column lines Y", NUMBERS, BEAMS_Y)
    starts_x = series(g, 0, bay_x, 2, "Beam starts X", NUMBERS, BEAMS_X)
    feet_levels = series(g, 0, storey, storeys, "Column feet", NUMBERS, FEET)
    floor_levels = series(g, storey, storey, storeys, "Floors", NUMBERS, SLAB, gap=10)

    # 2 - Geometry. Points on the plan, lifted to every level: a level each on its own branch.
    plan = component(g, CONSTRUCT_POINT, "Construct Point", "Plan", POINTS, COLUMNS)
    wire(inp(plan, 0), out(grid_x))
    graft(wire(inp(plan, 1), out(grid_y)))
    plan_x = component(g, CONSTRUCT_POINT, "Construct Point", "Beam starts X", POINTS, BEAMS_X)
    wire(inp(plan_x, 0), out(starts_x))
    graft(wire(inp(plan_x, 1), out(grid_y)))
    plan_y = component(g, CONSTRUCT_POINT, "Construct Point", "Beam starts Y", POINTS, BEAMS_Y)
    wire(inp(plan_y, 0), out(grid_x))

    rectangle = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Floor outline", POINTS, SLAB)
    corner_b = component(g, CONSTRUCT_POINT, "Construct Point", "Far corner", NUMBERS, SLAB, gap=10)
    wire(inp(corner_b, 0), out(span))
    corner_c = component(g, CONSTRUCT_POINT, "Construct Point", "Opposite corner", NUMBERS, SLAB, gap=10)
    wire(inp(corner_c, 0), out(span))
    wire(inp(corner_c, 1), bay_y)
    point(inp(rectangle, 0), 0, 0, 0)
    wire(inp(rectangle, 1), out(corner_b))
    wire(inp(rectangle, 2), out(corner_c))
    # One face per bay, so the slab's corners are the columns' tops.
    floor_mesh = component(g, MESH_PLANE, "Mesh Plane", "Floor mesh", LEVELS, SLAB)
    wire(inp(floor_mesh, 0), out(rectangle))
    integer(inp(floor_mesh, 1), 2)
    integer(inp(floor_mesh, 2), 1)

    to_feet = vector(g, UNIT_Z, "To the column feet", out(feet_levels), POINTS, FEET)
    to_floors = vector(g, UNIT_Z, "To the floors", out(floor_levels), POINTS, FEET, gap=10)

    feet = component(g, MOVE, "Move", "Column feet", LEVELS, COLUMNS)
    flat(wire(inp(feet, 0), out(plan)))
    graft(wire(inp(feet, 1), out(to_feet)))
    beams_x_at = component(g, MOVE, "Move", "Beam starts X", LEVELS, BEAMS_X)
    flat(wire(inp(beams_x_at, 0), out(plan_x)))
    graft(wire(inp(beams_x_at, 1), out(to_floors)))
    beams_y_at = component(g, MOVE, "Move", "Beam starts Y", LEVELS, BEAMS_Y)
    flat(wire(inp(beams_y_at, 0), out(plan_y)))
    graft(wire(inp(beams_y_at, 1), out(to_floors)))
    floors = component(g, MOVE, "Move", "Floors", LINES, SLAB)
    wire(inp(floors, 0), out(floor_mesh))
    graft(wire(inp(floors, 1), out(to_floors)))

    lines = []
    for start, axis, length, name, row in ((feet, UNIT_Z, storey, "Columns", COLUMNS),
                                           (beams_x_at, UNIT_X, bay_x, "Beams X", BEAMS_X),
                                           (beams_y_at, UNIT_Y, bay_y, "Beams Y", BEAMS_Y)):
        direction = vector(g, axis, "Along", 1.0, LEVELS, row, gap=10)
        line = component(g, LINE_SDL, "Line SDL", name, LINES, row)
        wire(inp(line, 0), out(start))
        wire(inp(line, 1), out(direction))
        wire(inp(line, 2), length)
        lines.append((direction, line))

    # 3 - Materials and sections: C30/37, 35 x 35 cm columns, 30 x 50 cm beams, a 20 cm slab.
    grade = text(g, "Concrete", "C30_37", NAMES, BEAMS_X)
    concrete = component(g, MATERIAL, "Material Library Elastic", None, NAMES, BEAMS_X, gap=10)
    wire(inp(concrete, 0), grade)
    column_section = component(g, RECTANGLE, "Rectangle Cross Section", "Columns 35x35", SECTIONS, COLUMNS)
    number(inp(column_section, 1), 0.35)
    number(inp(column_section, 2), 0.35)
    wire(inp(column_section, 3), out(concrete))
    beam_section = component(g, RECTANGLE, "Rectangle Cross Section", "Beams 30x50", SECTIONS, BEAMS_X)
    number(inp(beam_section, 1), 0.30)
    number(inp(beam_section, 2), 0.50)
    wire(inp(beam_section, 3), out(concrete))

    slab_values = [text(g, "Slab concrete", "C30_37", NAMES, SLAB),
                   text(g, "E [kN/m2]", "30000000", NAMES, SLAB),
                   text(g, "G [kN/m2]", "12500000", NAMES, SLAB),
                   text(g, "Poisson", "0.2", NAMES, SLAB),
                   text(g, "Density [kg/m3]", "2500", NAMES, SLAB)]
    slab_concrete = component(g, ND_MATERIAL, "nD", "Slab concrete", SECTIONS, SLAB)
    for i, source in enumerate(slab_values):
        wire(inp(slab_concrete, i), source)
    plate = component(g, PLATE_FIBER, "Plate Fiber Section", "Slab 20 cm", SECTIONS, SLAB, gap=10)
    number(inp(plate, 1), 0.20)
    wire(inp(plate, 2), out(slab_concrete))

    # 4 - Elements, and the feet fixed.
    columns = component(g, BEAM, "ForceBeamColumn", "Columns", ELEMENTS, COLUMNS)
    flat(wire(inp(columns, 0), out(lines[0][1])))
    wire(inp(columns, 1), out(column_section))
    beams = component(g, BEAM, "ForceBeamColumn", "Beams", ELEMENTS, BEAMS_X)
    flat(wire(inp(beams, 0), out(lines[1][1]), out(lines[2][1])))
    wire(inp(beams, 1), out(beam_section))
    slabs = component(g, SHELL, "ASD Shell", "Slabs", ELEMENTS, SLAB)
    wire(inp(slabs, 0), out(floors))
    wire(inp(slabs, 1), out(plate))

    fixed = toggle(g, "Fixed", True, SECTIONS, FEET)
    support = component(g, SUPPORT, "Support", "Fixed feet", ELEMENTS, FEET)
    flat(wire(inp(support, 0), out(plan)))
    for i in range(1, 7):
        wire(inp(support, i), fixed)

    # 5 - The seismic mass. The structure's own weight is in already, from the density; the floors'
    # finishes in full and their load at psi2 = 0.3, homes and offices (NTC 2018 Tab. 2.5.I).
    g2 = vector(g, UNIT_Z, "G2 2 kN/m2 down", -2.0, ELEMENTS, FLOOR_G2)
    g2_load = component(g, MESH_LOAD, "Mesh Load", "Finishes G2", LOADS, FLOOR_G2)
    wire(inp(g2_load, 1), out(g2))
    q = vector(g, UNIT_Z, "Q 2 kN/m2 down", -2.0, ELEMENTS, FLOOR_Q)
    q_load = component(g, MESH_LOAD, "Mesh Load", "Floor load Q", LOADS, FLOOR_Q)
    wire(inp(q_load, 1), out(q))
    psi2 = slider(g, "psi2", 0, 1, 0.3, 1, LOADS, FLOOR_Q, gap=10)

    g2_mass = component(g, MASS_FROM_LOADS, "Mass from Loads", "G2 in full", MASSES, FLOOR_G2)
    wire(inp(g2_mass, 0), out(g2_load))
    q_mass = component(g, MASS_FROM_LOADS, "Mass from Loads", "Q at psi2", MASSES, FLOOR_Q)
    wire(inp(q_mass, 0), out(q_load))
    wire(inp(q_mass, 1), psi2)

    right = g.arrange(0, 0, stages=(NUMBERS, NAMES, ELEMENTS, LOADS))

    # 6 - The site, the spectrum, the analysis and what it found, in a grid to the right.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, SITE, READ)
    flat(wire(inp(assemble, 0), out(columns), out(beams), out(slabs)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(g2_mass), out(q_mass)))
    mass = panel(m, "Mass [kg]", out(assemble, 1), 110, 20, SPECTRUM, READ)

    # The site: values for a medium seismicity site at the life-safety limit state (SLV), soil C.
    # Take ag, Fo and Tc* for a real site from the hazard grid (Spettri-NTC).
    ag = slider(m, "ag [g]", 0.05, 0.40, 0.20, 3, SITE, SHAKE)
    fo = slider(m, "Fo", 2.2, 3.0, 2.45, 2, SITE, SHAKE)
    tc = slider(m, "Tc* [s]", 0.2, 0.5, 0.30, 2, SITE, SHAKE)
    soil = value_list(m, "Soil", [(s, '"' + s + '"') for s in ("A", "B", "C", "D", "E")], 2, SITE, SHAKE)
    topography = value_list(m, "Topography", [(t, '"' + t + '"') for t in ("T1", "T2", "T3", "T4")], 0, SITE, SHAKE)
    behaviour = slider(m, "q", 1, 6, 3, 1, SITE, SHAKE)

    spectrum = component(m, DESIGN_SPECTRUM, "Design Spectrum", None, SPECTRUM, SHAKE, unit=NTC_2018)
    for i, source in ((S_AG, ag), (S_FO, fo), (S_TC, tc), (S_SOIL, soil), (S_TOPOGRAPHY, topography), (S_Q, behaviour)):
        wire(inp(spectrum, i), source)
    parameters = panel(m, "Spectrum", out(spectrum, S_PARAMETERS), 200, 260, ANALYSIS, SHAKE)

    modes = slider(m, "Vibration Modes", 1, 30, 9, 0, SPECTRUM, READ, gap=20)
    modal = value_list(m, "Modal Combination", [("CQC", '"CQC"'), ("SRSS", '"SRSS"')], 0, SPECTRUM, READ)
    directional = value_list(m, "Directional Combination", [("30%", '"30%"'), ("SRSS", '"SRSS"')], 0, SPECTRUM, READ)

    analysis = component(m, RESPONSE_SPECTRUM, "Response Spectrum Analysis", None, ANALYSIS, READ)
    wire(inp(analysis, R_MODEL), out(assemble))
    wire(inp(analysis, R_X), out(spectrum, S_SPECTRUM))
    wire(inp(analysis, R_Y), out(spectrum, S_SPECTRUM))
    wire(inp(analysis, R_MODES), modes)
    wire(inp(analysis, R_MODAL), modal)
    wire(inp(analysis, R_DIRECTIONAL), directional)

    # What it found, and a result case drawn.
    cases = panel(m, "Cases", out(analysis, R_CASES), 120, 70, RESULTS, READ)
    periods = panel(m, "Period [s]", out(analysis, R_PERIOD), 120, 180, RESULTS, READ)
    ratios = panel(m, "MassRatio X Y Z [%]", out(analysis, R_MASS_RATIO), 120, 70, RESULTS, READ)
    shear = panel(m, "BaseShear X Y [kN]", out(analysis, R_BASE_SHEAR), 120, 50, RESULTS, READ)

    step = value_list(m, "Result case", [("E: X and Y together", "0"), ("Ex: shaken along X", "1"), ("Ey: shaken along Y", "2")],
                      0, CHOICE, SHAKE)
    results = component(m, VIEW_RESULTS, "View Results", None, RESULTS, SHAKE)
    wire(inp(results, 0), out(analysis, R_OUT_MODEL))
    wire(inp(results, 1), step)

    m.arrange(right + 160, 0, stages=(SPECTRUM, RESULTS))

    group(doc, "1 - The frame: bays and storeys", [bay_x, bay_y, storey, storeys, span, grid_x, grid_y, starts_x,
                                                   feet_levels, floor_levels], RED)
    group(doc, "2 - Geometry: columns, beams and a floor at every level",
          [plan, plan_x, plan_y, rectangle, corner_b, corner_c, floor_mesh, to_feet, to_floors, feet, beams_x_at,
           beams_y_at, floors] + [o for parts in lines for o in parts], ORANGE)
    group(doc, "3 - C30/37: sections and slab", [grade, concrete, column_section, beam_section, slab_concrete, plate]
          + slab_values, PURPLE)
    group(doc, "4 - Elements and fixed feet", [columns, beams, slabs, fixed, support], GREEN)
    group(doc, "5 - Seismic mass: G2 in full, Q at psi2", [g2, g2_load, q, q_load, psi2, g2_mass, q_mass], GREEN)
    group(doc, "6 - NTC 2018 spectrum of the site", [ag, fo, tc, soil, topography, behaviour, spectrum, parameters], BLUE)
    group(doc, "7 - Response Spectrum Analysis", [assemble, mass, modes, modal, directional, analysis, cases, periods,
                                                  ratios, shear], BLUE)
    group(doc, "8 - A result case drawn", [step, results], GREY)

    # Only View Results draws in Rhino, not the construction geometry behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != results.InstanceGuid:
            obj.Hidden = True

    note(doc, "Response spectrum frame",
         "Response spectrum analysis - a concrete frame shaken by the NTC 2018 design spectrum.\n\n"
         "Two bays by one, two storeys of 3.2 m: 35 x 35 cm columns, 30 x 50 cm beams and a 20 cm slab, all "
         "C30/37, fixed at the feet. The slab is one shell per bay and holds each floor together.\n\n"
         "Mass: the concrete's own weight is mass already; Mass from Loads adds the finishes (G2, 2 kN/m2) "
         "in full and the floor load (Q, 2 kN/m2) at psi2 = 0.3.\n\n"
         "Spectrum: NTC 2018, soil C, q = 3, for a site with ag = 0.20 g, Fo = 2.45, Tc* = 0.30 s. Put in the "
         "values of a real site from the hazard grid.\n\n"
         "Response Spectrum Analysis shakes the frame along X and along Y with it, combines the modes by CQC "
         "and the directions by the 30% rule. MassRatio says how much of the mass the modes move: NTC asks "
         "for 85%. BaseShear is the total along each direction.\n\n"
         "Pick a Result case for View Results: E, both directions together, or Ex or Ey alone. Every value "
         "is a peak, positive, so the drawn shape leans one way only. Nodal Displacements, Beam Forces and "
         "Reaction Forces read the same model, with Step 0, 1 or 2.",
         0, -400, 620, 330)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Response spectrum frame built. Save the definition, then pick a Result case.")


if __name__ == "__main__":
    main()
