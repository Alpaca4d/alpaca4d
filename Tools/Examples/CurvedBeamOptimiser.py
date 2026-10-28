#! python3
# A curved steel beam on three supports with a cantilever, sized by the Section Optimiser: the curved
# beam example, built on the Grasshopper canvas.
#
# make.sh, next to it, writes CurvedBeamOptimiser.gh straight from this script, without Rhino:
#     ./make.sh CurvedBeamOptimiser.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 12 m beam curves up and down in a vertical plane, through five points. It is built in at its
# start, held up at two more points along its first third, and free beyond them, where a point load
# hangs near the far end: a continuous beam that ends in a long cantilever. Its own weight is on it
# too. It is cut into 30 elements, IPE300 in S355 to begin with, all with the ElementId "Beam".
# Two Section Optimisers size it from the one assembled model:
#   - with no design group, each element on its own, within the family of the section it starts
#     with - IPE300 among the IPEs - smallest area first: the beam grows over the supports and
#     thins out towards both ends;
#   - with one design group, the whole beam on one section, the first of a list of IPEs that every
#     element can have.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Types import GH_Number, GH_Integer
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
INTERPOLATE = "2b2a4145-3dff-41d4-a8de-1ea9d29eef33"        # Curve > Spline > Interpolate
LIST_ITEM = "59daf374-bc21-4a5e-8282-5504fb7ae9ae"          # Sets > List > List Item
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
DESIGN_GROUP = "35B2FF27-7BCC-4138-9CC7-267397D6A45A"       # Design Group
OPTIMISER = "56302D3A-734F-4B03-899E-F6C273CF1B9F"          # Section Optimiser

# ForceBeamColumn's inputs, by place.
B_LINE, B_SECTION, B_ELEMENT_ID = 0, 1, 5
# Section Optimiser's inputs and outputs, by place. Fabrication to Sway, and Governing to log, are plugs of its
# Design menu; System and Algorithm, of its Analysis menu.
O_MODEL, O_GROUPS, O_MAX_UTILISATION, O_LENGTH_LT = 0, 1, 3, 10
O_MODEL_OUT, O_SECTION, O_UTILISATION, O_MASS, O_CONVERGED, O_GOVERNING, O_HISTORY = 0, 1, 2, 4, 5, 6, 8
# Design Group's inputs.
G_NAME, G_ELEMENT_ID, G_SECTIONS = 0, 1, 2

# The IPEs of the library, in the order the design group tries them: the order is the user's.
IPES = ["IPE80", "IPE100", "IPE120", "IPE140", "IPE160", "IPE180", "IPE200", "IPE220", "IPE240",
        "IPE270", "IPE300", "IPE330", "IPE360", "IPE400", "IPE450", "IPE500", "IPE550", "IPE600"]

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)
YELLOW = Color.FromArgb(60, 230, 200, 40)


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


# ---------------------------------------------------------------- the beam

# Columns in the order the data flows...
INPUTS, PROFILE, POINTS, CURVE, DIVIDED, POLYLINES, SEGMENTS, NAMES, ELEMENT, LOADS, PATTERN = range(11)
MODEL, LIST, GROUP_, OPTIMISE, RESULTS, VIEW = range(11, 17)
# ...and rows, one per part of the model.
MAIN, BEARINGS, LOADING, EACH, ONE = range(5)

# The shape of the curve, from the picture it follows: where along the beam each of its points is,
# and how high, as fractions of its length and its rise. Edit the panels to reshape it.
ALONG = ["0", "0.33", "0.6", "0.88", "1"]
HEIGHT = ["0", "0.65", "0.45", "0.45", "1"]


def build(doc):
    grid = Grid(doc)

    # 1 - The beam: what to try. Where the supports and the load sit is a fraction of the way along,
    # rounded to the nearest node.
    length = slider(grid, "Length [m]", 4, 30, 12, 1, INPUTS, MAIN)
    rise = slider(grid, "Rise [m]", 0, 5, 2, 1, INPUTS, MAIN, gap=10)
    count = slider(grid, "Number of elements", 4, 80, 30, 0, INPUTS, MAIN, gap=10)
    start_name = value_list(grid, "Starting section",
                            [("IPE300", '"IPE300"'), ("HEA300", '"HEA300"'), ("HEB300", '"HEB300"')], 0, INPUTS, BEARINGS)
    grade = value_list(grid, "Grade", [("S235", '"S235"'), ("S275", '"S275"'), ("S355", '"S355"')], 2, INPUTS, BEARINGS, gap=10)
    second = slider(grid, "Second support at", 0.05, 0.95, 0.15, 2, INPUTS, BEARINGS, gap=10)
    third = slider(grid, "Third support at", 0.05, 0.95, 0.33, 2, INPUTS, BEARINGS, gap=10)
    load_at = slider(grid, "Load at", 0.05, 1.0, 0.88, 2, INPUTS, LOADING)
    load = slider(grid, "Load [kN]", 0, 200, 50, 1, INPUTS, LOADING, gap=10)

    # 2 - Geometry: a curve in the vertical XZ plane through five points, cut into elements, each a
    # beam of its own.
    along = lines(grid, "Along, of the length", ALONG, 120, 110, PROFILE, MAIN)
    height = lines(grid, "Height, of the rise", HEIGHT, 120, 110, PROFILE, MAIN, gap=10)
    xs = expression(grid, "f(a,L)=a*L", [along, length], "X", POINTS, MAIN)
    zs = expression(grid, "f(h,R)=h*R", [height, rise], "Z", POINTS, MAIN, gap=10)
    points = component(grid, CONSTRUCT_POINT, "Construct Point", "Through", CURVE, MAIN)
    wire(inp(points, 0), out(xs))
    wire(inp(points, 2), out(zs))
    curve = component(grid, INTERPOLATE, "Interpolate", "Beam", DIVIDED, MAIN)
    wire(inp(curve, 0), out(points))
    divide = component(grid, DIVIDE, "Divide Curve", "Nodes", POLYLINES, MAIN)
    wire(inp(divide, 0), out(curve, 0))
    wire(inp(divide, 1), count)
    polyline = component(grid, POLYLINE, "PolyLine", None, SEGMENTS, MAIN)
    wire(inp(polyline, 0), out(divide, 0))
    pieces = component(grid, EXPLODE, "Explode", "Elements", NAMES, MAIN)
    wire(inp(pieces, 0), out(polyline))

    # 3 - The member. Every element has the ElementId "Beam", which the design group names. Left
    # without a ZAxis, a beam's local z is horizontal and its local y in the vertical plane: the depth
    # of the I lies in the plane the beam curves in.
    material = component(grid, MATERIAL, "Material Library Elastic", None, NAMES, BEARINGS)
    wire(inp(material, 0), grade)
    section = component(grid, SECTION, "Steel Section Library", "Starting section", ELEMENT, BEARINGS)
    wire(inp(section, 0), start_name)
    wire(inp(section, 1), out(material))
    name = text(grid, "ElementId", "Beam", ELEMENT, BEARINGS, gap=10)
    beam = component(grid, BEAM, "ForceBeamColumn", None, ELEMENT, MAIN)
    flat(wire(inp(beam, B_LINE), out(pieces, 0)))
    wire(inp(beam, B_SECTION), out(section))
    wire(inp(beam, B_ELEMENT_ID), name)

    # 4 - Built in at its start; held up, and free to slide and turn, at the two other supports, each
    # on the node nearest the fraction asked for. The load hangs on the node nearest its own.
    held = toggle(grid, "Held", True, PROFILE, BEARINGS)
    free = toggle(grid, "Free", False, PROFILE, BEARINGS, gap=10)
    first_node = component(grid, LIST_ITEM, "List Item", "Start", POINTS, BEARINGS)
    wire(inp(first_node, 0), out(divide, 0))
    integer(inp(first_node, 1), 0)
    support_nodes = expression(grid, "f(t,n)=Round(t*n)", [second, count], "Support nodes", POINTS, BEARINGS, gap=10)
    inp(support_nodes, 0).AddSource(third)
    supported = component(grid, LIST_ITEM, "List Item", "Supported", CURVE, BEARINGS)
    wire(inp(supported, 0), out(divide, 0))
    wire(inp(supported, 1), out(support_nodes))
    fixed = component(grid, SUPPORT, "Support", "Built in", DIVIDED, BEARINGS)
    wire(inp(fixed, 0), out(first_node))
    for i in range(1, 7):
        wire(inp(fixed, i), held)
    propped = component(grid, SUPPORT, "Support", "Held up", DIVIDED, BEARINGS, gap=10)
    wire(inp(propped, 0), out(supported))
    for i in (1, 2, 4, 5, 6):
        wire(inp(propped, i), free)
    wire(inp(propped, 3), held)

    load_node = expression(grid, "f(t,n)=Round(t*n)", [load_at, count], "Load node", POINTS, LOADING)
    loaded = component(grid, LIST_ITEM, "List Item", "Loaded", CURVE, LOADING)
    wire(inp(loaded, 0), out(divide, 0))
    wire(inp(loaded, 1), out(load_node))
    down = expression(grid, "f(P)=-P", [load], "Downwards", CURVE, LOADING, gap=10)
    direction = component(grid, UNIT_Z, "Unit Z", "Down", DIVIDED, LOADING)
    wire(inp(direction, 0), out(down))
    point_load = component(grid, POINT_LOAD, "Point Load", "Load", LOADS, LOADING)
    wire(inp(point_load, 0), out(loaded))
    wire(inp(point_load, 1), out(direction))
    gravity = component(grid, GRAVITY, "Gravity Load", "Self-weight", LOADS, LOADING, gap=10)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOADING)
    wire(inp(pattern, 1), out(point_load), out(gravity))

    # 5 - Assembled once, for both optimisers. Each analyses it again in every iteration, with the
    # sections that iteration gives it: a static analysis in one step, which the optimiser sets up
    # itself - its Analysis menu has the solver, should one be wanted.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, MAIN)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(fixed), out(propped))
    wire(inp(assemble, 2), out(pattern))

    # 6 - Each element on its own: no design group, so each goes to the first section of its
    # family - the IPEs, for an IPE300 - that holds it, smallest area first. The top flange is taken
    # as held sideways every 0.5 m, wired into LengthLT in the Design menu: slide it up to leave the
    # beam free between its supports, and lateral-torsional buckling takes over.
    threshold = slider(grid, "MaxUtilisation", 0.5, 1.0, 1.0, 2, LIST, EACH)
    restraint = slider(grid, "Lateral restraint every [m]", 0.1, 30, 0.5, 1, LIST, EACH, gap=10)
    each = emit(OPTIMISER, "Section Optimiser")
    each.NickName = "Each element"
    open_menus(each)
    grid.put(each, OPTIMISE, EACH)
    wire(inp(each, O_MODEL), out(assemble))
    wire(inp(each, O_MAX_UTILISATION), threshold)
    wire(inp(each, O_LENGTH_LT), restraint)

    each_sections = panel(grid, "Section", out(each, O_SECTION), 110, 420, RESULTS, EACH)
    each_utilisation = panel(grid, "Utilisation", out(each, O_UTILISATION), 110, 420, RESULTS, EACH)
    each_governing = panel(grid, "Governing", out(each, O_GOVERNING), 110, 420, RESULTS, EACH)
    each_mass = panel(grid, "Mass", out(each, O_MASS), 150, 60, VIEW, EACH)
    each_history = panel(grid, "History", out(each, O_HISTORY), 420, 90, VIEW, EACH, gap=10)
    view = component(grid, MODEL_VIEW, "Model View", None, VIEW, EACH, gap=10)
    wire(inp(view, 0), out(each, O_MODEL_OUT))

    # 7 - One section for the whole beam: a design group over every element - ElementId "Beam" -
    # from a list of IPEs, tried in the order the list gives them.
    ipe_names = lines(grid, "IPEs, in the order to try them", IPES, 150, 330, LIST, ONE)
    ipes = component(grid, SECTION, "Steel Section Library", "IPEs", GROUP_, ONE)
    wire(inp(ipes, 0), ipe_names)
    wire(inp(ipes, 1), out(material))
    group_name = text(grid, "Name", "Whole beam", GROUP_, ONE, gap=10)
    design_group = component(grid, DESIGN_GROUP, "Design Group", None, OPTIMISE, ONE)
    wire(inp(design_group, G_NAME), group_name)
    wire(inp(design_group, G_ELEMENT_ID), name)
    wire(inp(design_group, G_SECTIONS), out(ipes))
    one = emit(OPTIMISER, "Section Optimiser")
    one.NickName = "Whole beam"
    open_menus(one)
    grid.put(one, OPTIMISE, ONE, gap=20)
    wire(inp(one, O_MODEL), out(assemble))
    wire(inp(one, O_GROUPS), out(design_group))
    wire(inp(one, O_MAX_UTILISATION), threshold)
    wire(inp(one, O_LENGTH_LT), restraint)

    one_sections = panel(grid, "Section", out(one, O_SECTION), 110, 420, RESULTS, ONE)
    one_utilisation = panel(grid, "Utilisation", out(one, O_UTILISATION), 110, 420, RESULTS, ONE)
    one_converged = panel(grid, "Converged", out(one, O_CONVERGED), 110, 30, RESULTS, ONE)
    one_mass = panel(grid, "Mass", out(one, O_MASS), 150, 60, VIEW, ONE)
    one_history = panel(grid, "History", out(one, O_HISTORY), 420, 90, VIEW, ONE, gap=10)

    grid.arrange(0, 0, stages=(PROFILE, NAMES, MODEL, LIST))

    group(doc, "1 - The beam", [length, rise, count, start_name, grade, second, third, load_at, load], RED)
    group(doc, "2 - Geometry", [along, height, xs, zs, points, curve, divide, polyline, pieces], ORANGE)
    group(doc, "3 - Member", [material, section, name, beam], PURPLE)
    group(doc, "4 - Supports and loads", [held, free, first_node, support_nodes, supported, fixed, propped,
                                         load_node, loaded, down, direction, point_load, gravity, pattern], GREEN)
    group(doc, "5 - Model", [assemble], BLUE)
    group(doc, "6 - Each element on its own, within its family",
          [threshold, restraint, each, each_sections, each_utilisation, each_governing, each_mass, each_history, view], YELLOW)
    group(doc, "7 - One section for the whole beam",
          [ipe_names, ipes, group_name, design_group, one, one_sections, one_utilisation, one_converged,
           one_mass, one_history], GREY)

    # Only the sized beam draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "Section Optimiser",
         "Curved beam - a Section Optimiser example.\n\n"
         "A 12 m beam curves up and down in a vertical plane. It is built in at its start, held up at "
         "15% and 33% of the way along, and free beyond: a continuous beam ending in a long cantilever, "
         "with 50 kN hanging near its far end and its own weight. It is cut into 30 elements, IPE300 "
         "in S355 to begin with.\n\n"
         "6 - Each element on its own. No design group: each element goes to the first section of "
         "the family it starts in - the IPEs for an IPE300 - that keeps it at or below MaxUtilisation, "
         "smallest area first. The beam grows over the third support, where the cantilever's moment "
         "is largest, and thins out towards the load and the built-in start. Switch the starting "
         "section to HEA300 and it stays among the HEAs.\n\n"
         "7 - One section for the whole beam: a design group over every element, from the list of "
         "IPEs in the panel, tried in the order it gives them.\n\n"
         "Reshape the curve in the two panels of 2, and move the supports and the load with their "
         "sliders: each lands on the node nearest the fraction given. The top flange is held sideways "
         "every 0.5 m, wired into LengthLT in the Design menu; sized element by element, an "
         "unrestrained beam is only a rough guide, as lateral-torsional buckling belongs to the whole "
         "member.",
         0, -400, 620, 340)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Curved beam built. Move the supports and the load, or reshape the curve, and watch the optimisers.")


if __name__ == "__main__":
    main()
