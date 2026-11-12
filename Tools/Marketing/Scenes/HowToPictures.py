#! python3
# The pictures of the docs' how-to guides, built on the Grasshopper canvas: small models side by side,
# each with a Model View of its own, which Tools/Marketing's howto scene pictures one by one.
#
#   - Supports: six short columns, each held another way - fixed, pinned, a roller, skewed, on springs,
#     compression only - with its name under it.
#   - Releases: a portal frame whose beam is pinned at both ends, Beam With Hinges, pushed sideways
#     and loaded along the beam.
#   - Wind: a box of concrete shell walls and a roof, pinned along its foot, with a wind pressure on
#     the windward wall and suction on the leeward one, each loaded by the wall's ElementId.
#   - Buckling: a steel portal frame under two column loads, and its first buckling mode.
#   - Loads: a steel frame carrying a concrete slab, with every kind of load Model View draws - its own
#     weight, point loads and a moment, line loads, a surface load, a mass - and, beside it, its own
#     weight alone, for Model View's page.
#
# ../../Examples/make.sh writes HowToPictures.gh from it, without Rhino:
#     ../../Examples/make.sh HowToPictures.py
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_String, GH_Point, GH_Vector, GH_Boolean, GH_Colour
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
CONSTRUCT_PLANE = "BC3E379E-7206-4E7B-B63A-FF61F4B38A3E"    # Vector > Plane > Construct Plane
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
TEXT_TAG_3D = "5A41528B-12B9-40DC-A3F2-842034D267C4"        # Display > Dimensions > Text Tag 3D
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
HINGE_RELEASE = "3F8A1C2D-5B4E-4F9A-8D7C-1E2F3A4B5C6D"      # Hinge Release
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LINEAR_LOAD = "8593AC69-D975-4566-97FC-27D7D4A4D415"        # Linear Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
MASS_POINT = "5C7056ED-01D4-4DDE-AD9E-46A5266ECEE6"         # Mass Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
LINEAR_BUCKLING = "6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815"    # Linear Buckling Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# The switchers' other units.
ELASTIC = "Spring Support Elastic (Alpaca4d)"
COMPRESSION_ONLY = "Spring Support Compression Only (Alpaca4d)"
WITH_HINGES = "WithHinges (Alpaca4d)"

# Model View's Results: Displacement, its magnitude.
DISPLACEMENT, MAGNITUDE = 1, 0

RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
DARK_TECH = Color.FromArgb(255, 42, 45, 49)


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


def flat(param):
    """Takes whatever tree arrives as one list."""
    param.DataMapping = GH_DataMapping.Flatten
    return param


def number(param, *values):
    """Types numbers into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Number(float(v)))
    return param


def integer(param, value):
    """Types a whole number into an unwired input."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def words(param, value):
    """Types a text into an unwired input."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
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


def text(grid, name, value, col, row=None, gap=0):
    """A one-line panel holding a text, such as an ElementId."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 90.0, 20.0)
    return grid.put(p, col, row, gap)


def lines(grid, name, values, width, height, col, row=None, gap=0):
    """A panel holding a list, one item a line: Multiline Data off."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Multiline = False
    p.SetUserText("\n".join(values))
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
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


def panel(grid, name, source, width, height, col, row=None, gap=0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    p.AddSource(source)
    return grid.put(p, col, row, gap)


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


def menu_setting(c, field, prop, value):
    """Sets a widget of a component's fold-out menus, found by its field, as a click on it would:
    the component saves it with the file."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    setattr(c.GetType().GetField(field, flags).GetValue(c), prop, value)


def show_result(c, show, component):
    """Ticks one of Model View's results alone, at a component, as clicks in its Results menu do.
    Its menus then show the controls its settings have a use for and put the rest away, and it is laid
    out again: the grid measures it at the height it opens at. Set its other widgets first."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    c.GetType().GetMethod("ShowResult", flags).Invoke(c, System.Array[System.Object]([show, component]))
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()


def points(grid, name, xyz, col, row=None, gap=0):
    """A Point parameter holding points typed in."""
    p = emit(POINT, "The Point parameter")
    p.NickName = name
    for x, y, z in xyz:
        p.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return grid.put(p, col, row, gap)


def vector(param, x, y, z):
    """Types a vector into an unwired input."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return param


def booleans(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Boolean(v))
    return param


def integers(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Integer(int(v)))
    return param


def texts(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_String(v))
    return param


def colour(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Colour(value))
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


def widget(c, field):
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    return c.GetType().GetField(field, flags).GetValue(c)


def shows(view, *fields):
    """Ticks Model View's checkboxes, by their fields."""
    for field in fields:
        menu_setting(view, field, "Active", True)


def steel(grid, name, col, row):
    """S355 and a section of the steel library."""
    material = component(grid, MATERIAL, "Material Library Elastic", None, col, row)
    words(inp(material, 0), "S355")
    section = component(grid, SECTION, "Steel Section Library", None, col, row, gap=30)
    words(inp(section, 0), name)
    wire(inp(section, 1), out(material))
    return material, section


# ---------------------------------------------------------------- the models

# Columns in the order the data flows...
INPUTS, GEOMETRY, MEMBER, ELEMENT, LOADS, PATTERN, MODEL, VIEW = range(8)
# ...and a row for each model.
SUPPORTS, RELEASES, WIND, BUCKLING, FRAME = range(5)


def supports(grid):
    """Six columns 1.5 m tall, 3 m apart, each held another way at its foot, with its name under it."""
    names = ["Fixed", "Pinned", "Roller", "Skewed", "Springs", "Compression only"]
    feet = [(3.0 * i, 0.0, 0.0) for i in range(6)]
    bases = points(grid, "Feet", feet, INPUTS, SUPPORTS)
    tops = points(grid, "Tops", [(x, y, 1.5) for x, y, z in feet], INPUTS, SUPPORTS, gap=30)
    line = component(grid, LINE, "Line", "Columns", GEOMETRY, SUPPORTS)
    wire(inp(line, 0), bases)
    wire(inp(line, 1), tops)
    material, section = steel(grid, "HEA200", MEMBER, SUPPORTS)
    beam = component(grid, BEAM, "ForceBeamColumn", None, ELEMENT, SUPPORTS)
    wire(inp(beam, 0), out(line))
    wire(inp(beam, 1), out(section))

    held = []
    rigid = [(True,) * 6, (True, True, True, False, False, False), (False, True, True, False, False, True)]
    for i in range(3):
        foot = points(grid, names[i], [feet[i]], GEOMETRY, SUPPORTS, gap=30)
        s = component(grid, SUPPORT, "Support", names[i], ELEMENT, SUPPORTS, gap=30)
        wire(inp(s, 0), foot)
        for k in range(6):
            booleans(inp(s, k + 1), rigid[i][k])
        held += [foot, s]
    # Skewed: a pin on a plane turned 30 degrees about Y.
    foot = points(grid, names[3], [feet[3]], GEOMETRY, SUPPORTS, gap=30)
    plane = component(grid, CONSTRUCT_PLANE, "Construct Plane", "Turned 30 deg", MEMBER, SUPPORTS, gap=30)
    wire(inp(plane, 0), foot)
    vector(inp(plane, 1), 0.866, 0.0, 0.5)
    vector(inp(plane, 2), 0.0, 1.0, 0.0)
    skewed = component(grid, SUPPORT, "Support", names[3], ELEMENT, SUPPORTS, gap=30)
    wire(inp(skewed, 0), out(plane))
    booleans(inp(skewed, 4), False)
    booleans(inp(skewed, 5), False)
    booleans(inp(skewed, 6), False)
    # Springs along the three axes, 500 kN/m each.
    foot_e = points(grid, names[4], [feet[4]], GEOMETRY, SUPPORTS, gap=30)
    elastic = component(grid, SUPPORT, "Support", names[4], ELEMENT, SUPPORTS, gap=30)
    switch_unit(elastic, ELASTIC)
    wire(inp(elastic, 0), foot_e)
    for k in (1, 2, 3):
        number(inp(elastic, k), 500)
    # Compression only, rigid, with no gap.
    foot_c = points(grid, names[5], [feet[5]], GEOMETRY, SUPPORTS, gap=30)
    bearing = component(grid, SUPPORT, "Support", names[5], ELEMENT, SUPPORTS, gap=30)
    switch_unit(bearing, COMPRESSION_ONLY)
    wire(inp(bearing, 0), foot_c)

    weight = component(grid, GRAVITY, "Gravity Load", None, LOADS, SUPPORTS)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, SUPPORTS)
    wire(inp(pattern, 1), out(weight))
    assemble = component(grid, ASSEMBLE, "Assemble Model", "Supports", MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(held[1]), out(held[3]), out(held[5]), out(skewed), out(elastic), out(bearing))
    wire(inp(assemble, 2), out(pattern))
    view = component(grid, MODEL_VIEW, "Model View", "Supports view", VIEW, SUPPORTS)
    wire(inp(view, 0), out(assemble))
    shows(view, "_ckShowSupports")

    # Each name under its support, upright and facing the front.
    where = points(grid, "Name at", [(x, y, -0.9) for x, y, z in feet], VIEW, SUPPORTS, gap=30)
    upright = component(grid, CONSTRUCT_PLANE, "Construct Plane", "Upright", VIEW, SUPPORTS, gap=30)
    wire(inp(upright, 0), where)
    vector(inp(upright, 1), 1, 0, 0)
    vector(inp(upright, 2), 0, 0, 1)
    tags = component(grid, TEXT_TAG_3D, "Text Tag 3D", "Support names", VIEW, SUPPORTS, gap=30)
    wire(inp(tags, 0), out(upright))
    texts(inp(tags, 1), *names)
    number(inp(tags, 2), 0.3)
    colour(inp(tags, 3), DARK_TECH)
    integer(inp(tags, 4), 4)
    return [bases, tops, line, material, section, beam] + held + [foot, plane, skewed, foot_e, elastic, foot_c, bearing,
                                                                   weight, pattern, assemble, view, where, upright, tags]


def releases(grid):
    """A portal frame 6 m wide and 4 m tall, its columns built in, its beam pinned at both ends."""
    x0 = 20.0
    feet = points(grid, "Column feet", [(x0, 0, 0), (x0 + 6, 0, 0)], INPUTS, RELEASES)
    heads = points(grid, "Column heads", [(x0, 0, 4), (x0 + 6, 0, 4)], INPUTS, RELEASES, gap=30)
    columns = component(grid, LINE, "Line", "Columns", GEOMETRY, RELEASES)
    wire(inp(columns, 0), feet)
    wire(inp(columns, 1), heads)
    left = points(grid, "Beam start", [(x0, 0, 4)], INPUTS, RELEASES, gap=30)
    right = points(grid, "Beam end", [(x0 + 6, 0, 4)], INPUTS, RELEASES, gap=30)
    span = component(grid, LINE, "Line", "Beam", GEOMETRY, RELEASES, gap=30)
    wire(inp(span, 0), left)
    wire(inp(span, 1), right)
    material, section = steel(grid, "HEA200", MEMBER, RELEASES)
    column = component(grid, BEAM, "ForceBeamColumn", "Columns", ELEMENT, RELEASES)
    wire(inp(column, 0), out(columns))
    wire(inp(column, 1), out(section))
    # The beam, released about both its bending axes at each end: a pin.
    pin = component(grid, HINGE_RELEASE, "Hinge Release", "Pinned", MEMBER, RELEASES, gap=30)
    booleans(inp(pin, 4), False)
    booleans(inp(pin, 5), False)
    beam = component(grid, BEAM, "ForceBeamColumn", "Pinned beam", ELEMENT, RELEASES, gap=30)
    switch_unit(beam, WITH_HINGES)
    wire(inp(beam, 0), out(span))
    wire(inp(beam, 1), out(section))
    wire(inp(beam, 4), out(pin))
    wire(inp(beam, 6), out(pin))
    words(inp(beam, 9), "Beam")
    built_in = component(grid, SUPPORT, "Support", "Built in", ELEMENT, RELEASES, gap=30)
    wire(inp(built_in, 0), feet)
    push = component(grid, POINT_LOAD, "Point Load", "Wind", LOADS, RELEASES)
    wire(inp(push, 0), left)
    vector(inp(push, 1), 10, 0, 0)
    along = component(grid, LINEAR_LOAD, "Linear Load", "On the beam", LOADS, RELEASES, gap=30)
    words(inp(along, 0), "Beam")
    vector(inp(along, 1), 0, 0, -5)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, RELEASES)
    wire(inp(pattern, 1), out(push), out(along))
    assemble = component(grid, ASSEMBLE, "Assemble Model", "Frame", MODEL, RELEASES)
    wire(inp(assemble, 0), out(column), out(beam))
    wire(inp(assemble, 1), out(built_in))
    wire(inp(assemble, 2), out(pattern))
    view = component(grid, MODEL_VIEW, "Model View", "Releases view", VIEW, RELEASES)
    wire(inp(view, 0), out(assemble))
    shows(view, "_ckShowSupports", "_ckShowLoads", "_ckShowLoadValues")
    return [feet, heads, columns, left, right, span, material, section, column, pin, beam, built_in, push, along,
            pattern, assemble, view]


def wind(grid):
    """A box 8 x 6 m and 5 m tall of 200 mm concrete walls and a roof, pinned along its foot, wind along +X."""
    x0 = 40.0
    names = ["Wall_West", "Wall_East", "Wall_South", "Wall_North", "Roof"]
    a = [(x0, 0, 0), (x0 + 8, 0, 0), (x0, 0, 0), (x0, 6, 0), (x0, 0, 5)]
    b = [(x0, 6, 0), (x0 + 8, 6, 0), (x0 + 8, 0, 0), (x0 + 8, 6, 0), (x0 + 8, 0, 5)]
    c = [(x0, 6, 5), (x0 + 8, 6, 5), (x0 + 8, 0, 5), (x0 + 8, 6, 5), (x0 + 8, 6, 5)]
    corner_a = points(grid, "A", a, INPUTS, WIND)
    corner_b = points(grid, "B", b, INPUTS, WIND, gap=30)
    corner_c = points(grid, "C", c, INPUTS, WIND, gap=30)
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Faces", GEOMETRY, WIND)
    wire(inp(outline, 0), corner_a)
    wire(inp(outline, 1), corner_b)
    wire(inp(outline, 2), corner_c)
    mesh = component(grid, MESH_PLANE, "Mesh Plane", "Faces", MEMBER, WIND)
    wire(inp(mesh, 0), out(outline))
    integers(inp(mesh, 1), 6, 6, 8, 8, 8)
    integers(inp(mesh, 2), 5, 5, 5, 5, 6)
    concrete = component(grid, ND_MATERIAL, "nD", "Concrete", GEOMETRY, WIND, gap=30)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), 30e6)
    number(inp(concrete, 2), 30e6 / 2.4)
    number(inp(concrete, 3), 0.2)
    number(inp(concrete, 4), 2500)
    plate = component(grid, PLATE_FIBER, "Plate Fiber Section", "200 mm", MEMBER, WIND, gap=30)
    number(inp(plate, 1), 0.2)
    wire(inp(plate, 2), out(concrete))
    shell = component(grid, SHELL, "ASD Shell", None, ELEMENT, WIND)
    wire(inp(shell, 0), out(mesh))
    wire(inp(shell, 1), out(plate))
    texts(inp(shell, 5), *names)
    # Pinned at every node of the foot.
    foot_start = points(grid, "Foot from", [(x0, 0, 0), (x0 + 8, 0, 0), (x0, 0, 0), (x0, 6, 0)], INPUTS, WIND, gap=30)
    foot_end = points(grid, "Foot to", [(x0, 6, 0), (x0 + 8, 6, 0), (x0 + 8, 0, 0), (x0 + 8, 6, 0)], INPUTS, WIND, gap=30)
    foot = component(grid, LINE, "Line", "Foot", GEOMETRY, WIND, gap=30)
    wire(inp(foot, 0), foot_start)
    wire(inp(foot, 1), foot_end)
    nodes = component(grid, DIVIDE, "Divide Curve", "Foot nodes", MEMBER, WIND, gap=30)
    wire(inp(nodes, 0), out(foot))
    integers(inp(nodes, 1), 6, 6, 8, 8)
    pinned = component(grid, SUPPORT, "Support", "Pinned", ELEMENT, WIND, gap=30)
    wire(flat(inp(pinned, 0)), out(nodes))
    for k in (4, 5, 6):
        booleans(inp(pinned, k), False)
    # 0.8 kN/m2 pushing on the windward wall, 0.5 kN/m2 of suction pulling the leeward one: both along +X.
    windward = component(grid, MESH_LOAD, "Mesh Load", "Windward", LOADS, WIND)
    words(inp(windward, 0), "Wall_West")
    vector(inp(windward, 1), 0.8, 0, 0)
    leeward = component(grid, MESH_LOAD, "Mesh Load", "Leeward", LOADS, WIND, gap=30)
    words(inp(leeward, 0), "Wall_East")
    vector(inp(leeward, 1), 0.5, 0, 0)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", "Wind", PATTERN, WIND)
    wire(inp(pattern, 1), out(windward), out(leeward))
    assemble = component(grid, ASSEMBLE, "Assemble Model", "Box", MODEL, WIND)
    wire(flat(inp(assemble, 0)), out(shell))
    wire(inp(assemble, 1), out(pinned))
    wire(inp(assemble, 2), out(pattern))
    view = component(grid, MODEL_VIEW, "Model View", "Wind view", VIEW, WIND)
    wire(inp(view, 0), out(assemble))
    shows(view, "_ckShowLoads", "_ckShowLoadValues")
    return [corner_a, corner_b, corner_c, outline, mesh, concrete, plate, shell, foot_start, foot_end, foot, nodes,
            pinned, windward, leeward, pattern, assemble, view]


def buckling(grid):
    """A steel portal frame 6 m wide and 8 m tall, its columns pinned at their feet and braced at their
    heads against moving out of its plane, two loads of 200 kN down its columns: and its first buckling
    mode, the frame swaying sideways in its plane."""
    x0 = 62.0
    feet = points(grid, "Column feet", [(x0, 0, 0), (x0 + 6, 0, 0)], INPUTS, BUCKLING)
    heads = points(grid, "Column heads", [(x0, 0, 8), (x0 + 6, 0, 8)], INPUTS, BUCKLING, gap=30)
    columns = component(grid, LINE, "Line", "Columns", GEOMETRY, BUCKLING)
    wire(inp(columns, 0), feet)
    wire(inp(columns, 1), heads)
    left = points(grid, "Beam start", [(x0, 0, 8)], INPUTS, BUCKLING, gap=30)
    right = points(grid, "Beam end", [(x0 + 6, 0, 8)], INPUTS, BUCKLING, gap=30)
    span = component(grid, LINE, "Line", "Beam", GEOMETRY, BUCKLING, gap=30)
    wire(inp(span, 0), left)
    wire(inp(span, 1), right)
    material, section = steel(grid, "HEA200", MEMBER, BUCKLING)
    frame = component(grid, BEAM, "ForceBeamColumn", "Frame", ELEMENT, BUCKLING)
    wire(inp(frame, 0), out(columns), out(span))
    wire(inp(frame, 1), out(section))
    pinned = component(grid, SUPPORT, "Support", "Pinned", ELEMENT, BUCKLING, gap=30)
    wire(inp(pinned, 0), feet)
    for k in (4, 5, 6):
        booleans(inp(pinned, k), False)
    # Bracing at the heads: held along Y only, out of the frame's plane.
    braced = component(grid, SUPPORT, "Support", "Braced out of plane", ELEMENT, BUCKLING, gap=30)
    wire(inp(braced, 0), heads)
    booleans(inp(braced, 1), False)
    booleans(inp(braced, 3), False)
    for k in (4, 5, 6):
        booleans(inp(braced, k), False)
    down = component(grid, POINT_LOAD, "Point Load", "Column loads", LOADS, BUCKLING)
    wire(inp(down, 0), heads)
    vector(inp(down, 1), 0, 0, -200)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, BUCKLING)
    wire(inp(pattern, 1), out(down))
    assemble = component(grid, ASSEMBLE, "Assemble Model", "Frame", MODEL, BUCKLING)
    wire(inp(assemble, 0), out(frame))
    wire(inp(assemble, 1), out(pinned), out(braced))
    wire(inp(assemble, 2), out(pattern))
    analysis = component(grid, LINEAR_BUCKLING, "Linear Buckling Analysis", None, MODEL, BUCKLING, gap=30)
    wire(inp(analysis, 0), out(assemble))
    integer(inp(analysis, 1), 3)
    view = component(grid, MODEL_VIEW, "Model View", "Buckling view", VIEW, BUCKLING)
    wire(inp(view, 0), out(analysis))
    widget(view, "_ckDeformed").Active = True
    show_result(view, DISPLACEMENT, MAGNITUDE)
    # The frame at rest, as lines, behind its buckled shape.
    rest = component(grid, MODEL_VIEW, "Model View", "Buckling at rest", VIEW, BUCKLING, gap=30)
    wire(inp(rest, 0), out(assemble))
    menu_setting(rest, "_ckExtruded", "Active", False)
    factors = panel(grid, "Load Factors", out(analysis, 1), 160, 80, VIEW, BUCKLING, gap=30)
    return [feet, heads, columns, left, right, span, material, section, frame, pinned, braced, down, pattern,
            assemble, analysis, view, rest, factors]


def frame_loads(grid):
    """A steel frame 6 x 4 m and 3 m tall, HEA200 throughout, its columns built in, carrying a 200 mm
    concrete slab on its edge beams, every member in pieces of 1 m so that the slab's nodes are the
    beams'. Every kind of load Model View draws: the frame's own weight in a pattern of its own, and in
    another 50 kN down on the front beam's middle, 20 kN and a moment of 15 kNm on a back corner,
    10 kN/m along the front beams, 5 kN/m of wind on a column, 3 kN/m2 on the slab - and 2000 kg on a
    front corner. Assembled twice: with every load, and with its own weight alone."""
    x0, lx, ly, h = 84.0, 6, 4, 3.0

    def pieces(a, b, n):
        """The line from a to b in n pieces: their starts and their ends."""
        starts = [tuple(a[k] + (b[k] - a[k]) * i / float(n) for k in range(3)) for i in range(n)]
        ends = [tuple(a[k] + (b[k] - a[k]) * (i + 1) / float(n) for k in range(3)) for i in range(n)]
        return starts, ends

    front_s, front_e = pieces((x0, 0, h), (x0 + lx, 0, h), lx)
    edge_s, edge_e = [], []
    for a, b, n in [((x0, ly, h), (x0 + lx, ly, h), lx), ((x0, 0, h), (x0, ly, h), ly), ((x0 + lx, 0, h), (x0 + lx, ly, h), ly)]:
        s_, e_ = pieces(a, b, n)
        edge_s += s_
        edge_e += e_
    windward_s, windward_e = pieces((x0, 0, 0), (x0, 0, h), 3)
    column_s, column_e = [], []
    for x, y in [(x0 + lx, 0), (x0, ly), (x0 + lx, ly)]:
        s_, e_ = pieces((x, y, 0), (x, y, h), 3)
        column_s += s_
        column_e += e_

    material, section = steel(grid, "HEA200", MEMBER, FRAME)
    parts = [material, section]
    members = []
    for name, starts, ends in [("Front beams", front_s, front_e), ("Edge beams", edge_s, edge_e),
                               ("Windward column", windward_s, windward_e), ("Columns", column_s, column_e)]:
        a = points(grid, name + " from", starts, INPUTS, FRAME, gap=30)
        b = points(grid, name + " to", ends, INPUTS, FRAME, gap=30)
        line = component(grid, LINE, "Line", name, GEOMETRY, FRAME, gap=30)
        wire(inp(line, 0), a)
        wire(inp(line, 1), b)
        beam = component(grid, BEAM, "ForceBeamColumn", name, ELEMENT, FRAME, gap=30)
        wire(inp(beam, 0), out(line))
        wire(inp(beam, 1), out(section))
        parts += [a, b, line, beam]
        members.append(beam)
    front, edges, windward, columns = members

    corner = [points(grid, "Slab " + n, [(x, y, h)], INPUTS, FRAME, gap=30)
              for n, (x, y) in zip("ABC", [(x0, 0), (x0 + lx, 0), (x0 + lx, ly)])]
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Slab", GEOMETRY, FRAME, gap=30)
    for k in range(3):
        wire(inp(outline, k), corner[k])
    mesh = component(grid, MESH_PLANE, "Mesh Plane", "Slab", MEMBER, FRAME, gap=30)
    wire(inp(mesh, 0), out(outline))
    integers(inp(mesh, 1), lx)
    integers(inp(mesh, 2), ly)
    concrete = component(grid, ND_MATERIAL, "nD", "Concrete", GEOMETRY, FRAME, gap=30)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), 30e6)
    number(inp(concrete, 2), 30e6 / 2.4)
    number(inp(concrete, 3), 0.2)
    number(inp(concrete, 4), 2500)
    plate = component(grid, PLATE_FIBER, "Plate Fiber Section", "200 mm", MEMBER, FRAME, gap=30)
    number(inp(plate, 1), 0.2)
    wire(inp(plate, 2), out(concrete))
    slab = component(grid, SHELL, "ASD Shell", "Slab", ELEMENT, FRAME, gap=30)
    wire(inp(slab, 0), out(mesh))
    wire(inp(slab, 1), out(plate))
    feet = points(grid, "Column feet", [(x0, 0, 0), (x0 + lx, 0, 0), (x0, ly, 0), (x0 + lx, ly, 0)], INPUTS, FRAME, gap=30)
    built_in = component(grid, SUPPORT, "Support", "Built in", ELEMENT, FRAME, gap=30)
    wire(inp(built_in, 0), feet)
    parts += corner + [outline, mesh, concrete, plate, slab, feet, built_in]

    weight = component(grid, GRAVITY, "Gravity Load", "Own weight", LOADS, FRAME)
    middle = points(grid, "Front middle", [(x0 + 3, 0, h)], INPUTS, FRAME, gap=30)
    down = component(grid, POINT_LOAD, "Point Load", "50 kN", LOADS, FRAME, gap=30)
    wire(inp(down, 0), middle)
    vector(inp(down, 1), 0, 0, -50)
    back = points(grid, "Back corner", [(x0 + lx, ly, h)], INPUTS, FRAME, gap=30)
    turned = component(grid, POINT_LOAD, "Point Load", "20 kN and 15 kNm", LOADS, FRAME, gap=30)
    wire(inp(turned, 0), back)
    vector(inp(turned, 1), 0, 0, -20)
    vector(inp(turned, 2), 0, 15, 0)
    along = component(grid, LINEAR_LOAD, "Linear Load", "10 kN/m", LOADS, FRAME, gap=30)
    wire(inp(along, 0), out(front))
    vector(inp(along, 1), 0, 0, -10)
    wind_ = component(grid, LINEAR_LOAD, "Linear Load", "Wind 5 kN/m", LOADS, FRAME, gap=30)
    wire(inp(wind_, 0), out(windward))
    vector(inp(wind_, 1), 5, 0, 0)
    floor = component(grid, MESH_LOAD, "Mesh Load", "3 kN/m2", LOADS, FRAME, gap=30)
    wire(inp(floor, 0), out(slab))
    vector(inp(floor, 1), 0, 0, -3)
    front_corner = points(grid, "Mass at", [(x0 + lx, 0, h)], INPUTS, FRAME, gap=30)
    mass = component(grid, MASS_POINT, "Mass Point Load", "2000 kg", LOADS, FRAME, gap=30)
    wire(inp(mass, 0), front_corner)
    vector(inp(mass, 1), 2000, 2000, 2000)
    own = component(grid, LOAD_PATTERN, "Load Pattern", "Own weight", PATTERN, FRAME)
    wire(inp(own, 1), out(weight))
    imposed = component(grid, LOAD_PATTERN, "Load Pattern", "Imposed", PATTERN, FRAME, gap=30)
    wire(inp(imposed, 1), out(down), out(turned), out(along), out(wind_), out(floor))

    views = []
    for name, patterns in [("Every load", [own, imposed, mass]), ("Own weight", [own])]:
        assemble = component(grid, ASSEMBLE, "Assemble Model", name, MODEL, FRAME, gap=30)
        wire(inp(assemble, 0), *[out(m) for m in members + [slab]])
        wire(inp(assemble, 1), out(built_in))
        wire(inp(assemble, 2), *[out(x) for x in patterns])
        view = component(grid, MODEL_VIEW, "Model View", name + " view", VIEW, FRAME, gap=30)
        wire(inp(view, 0), out(assemble))
        shows(view, "_ckShowSupports", "_ckShowLoads", "_ckShowLoadValues")
        # Text a fifth of a metre tall, on a frame 6 m wide: 0.5, the default, is for larger models.
        menu_setting(view, "_slTextSize", "Value", 0.2)
        views += [assemble, view]
    return parts + [weight, middle, down, back, turned, along, wind_, floor, front_corner, mass, own, imposed] + views


def build(doc):
    grid = Grid(doc)
    parts = [("Supports", supports(grid), GREEN), ("Releases", releases(grid), PURPLE),
             ("Wind", wind(grid), BLUE), ("Buckling", buckling(grid), ORANGE),
             ("Loads", frame_loads(grid), RED)]
    grid.arrange(300, 300, stages=(GEOMETRY, MODEL))
    for name, objects, c in parts:
        group(doc, name, objects, c)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The how-to pictures' models are built.")


if __name__ == "__main__":
    main()
