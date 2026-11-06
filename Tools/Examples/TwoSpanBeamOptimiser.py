#! python3
# A curved steel beam over two spans, built in at both ends and pinned between them, sized by the
# Section Optimiser: built on the Grasshopper canvas.
#
# make.sh, next to it, writes TwoSpanBeamOptimiser.gh straight from this script, without Rhino:
#     ./make.sh TwoSpanBeamOptimiser.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A curve about 30 m long rises and falls in a vertical plane, through five points. A point placed
# along it - Evaluate Curve, from 0 to 1 - shatters it into two segments, and each segment is cut
# into elements about 25 cm long: as many as its length holds, rounded, so each segment ends on a
# node. The beam is built in at both ends and pinned at the point between them. It is an HEA300 in
# S235 to begin with, all of it with the ElementId "Beam", and carries its own weight ten times over -
# Gravity factor - and two point loads, 150 kN on the first span and 200 kN on the second, each moved
# along the curve by a slider: Evaluate Curve again, then the node closest to it.
# Assembled, the model goes to a Section Optimiser with one design group over the whole beam, from a
# list of HEAs: Uniform off, as it opens, gives each element the first of the list that it can have, so
# the HEA varies along the beam; Uniform on gives the whole beam one section, the first of the list
# that every element can have. Text tags name the HEA wherever it changes, and a second Model View
# draws the sized beam's deformed shape, coloured by its displacement, with a Legend.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Boolean, GH_Colour
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
CLOSEST_POINT = "571CA323-6E55-425A-BF9E-EE103C7BA4B9"      # Vector > Point > Closest Point
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
INTERPOLATE = "2b2a4145-3dff-41d4-a8de-1ea9d29eef33"        # Curve > Spline > Interpolate
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
EVALUATE_CURVE = "FC6979E4-7E91-4508-8E05-37C680779751"     # Curve > Analysis > Evaluate Curve
CURVE_CP = "2DC44B22-B1DD-460A-A704-6462D6E91096"           # Curve > Analysis > Curve Closest Point
LENGTH = "C75B62FA-0A33-4DA7-A5BD-03FD0068FD93"             # Curve > Analysis > Length
END_POINTS = "11BBD48B-BB0A-4F1B-8167-FA297590390D"         # Curve > Analysis > End Points
SHATTER = "2AD2A4D4-3DE1-42F6-A4B8-F71835F35710"            # Curve > Division > Shatter
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
CURVE_MIDDLE = "CCC7B468-E743-4049-891F-299432545898"       # Curve > Analysis > Curve Middle
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
CREATE_SET = "98C3C63A-E78A-43EA-A111-514FCF312C95"         # Sets > Sets > Create Set
MERGE = "3CADDDEF-1E2B-4C09-9390-0E8F78F7609F"              # Sets > Tree > Merge
SHIFT_LIST = "4FDFE351-6C07-47CE-9FB9-BE027FB62186"         # Sets > List > Shift List
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern
DUPLICATE_DATA = "DD8134C0-109B-4012-92BE-51D843EDFFF7"     # Sets > Sequence > Duplicate Data
EQUALITY = "5DB0FB89-4F22-4F09-A777-FA5E55AED7EC"           # Maths > Operators > Equality
TEXT_TAG_3D = "5A41528B-12B9-40DC-A3F2-842034D267C4"        # Display > Dimensions > Text Tag 3D
CONSTRUCT_PLANE = "BC3E379E-7206-4E7B-B63A-FF61F4B38A3E"    # Vector > Plane > Construct Plane
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend
DESIGN_GROUP = "35B2FF27-7BCC-4138-9CC7-267397D6A45A"       # Design Group
OPTIMISER = "56302D3A-734F-4B03-899E-F6C273CF1B9F"          # Section Optimiser

# ForceBeamColumn's inputs, by place.
B_LINE, B_SECTION, B_ELEMENT_ID = 0, 1, 5
# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_RX, S_RY, S_RZ = 0, 4, 5, 6
# Section Optimiser's inputs and outputs, by place. Fabrication to Sway, and Governing to log, are plugs of its
# Design menu; System and Algorithm, of its Analysis menu.
O_MODEL, O_GROUPS, O_MAX_UTILISATION, O_ITERATIONS, O_LENGTH_Y, O_LENGTH_Z, O_LENGTH_LT = 0, 1, 3, 4, 8, 9, 10
O_MODEL_OUT, O_SECTION, O_UTILISATION, O_MASS, O_CONVERGED, O_GOVERNING, O_HISTORY = 0, 1, 2, 4, 5, 6, 8
# Design Group's inputs.
G_NAME, G_ELEMENT_ID, G_SECTIONS, G_UNIFORM = 0, 1, 2, 3
# Text Tag 3D's inputs: where, what, how big, the colour and the justification - 4 is middle left.
T_LOCATION, T_TEXT, T_SIZE, T_COLOUR, T_JUSTIFICATION = 0, 1, 2, 3, 4
# Model View's outputs, and Legend's inputs. Its results, from 1: Displacement is the
# first and Beam forces the second. Magnitude is the first of the displacements; Vy and Mz,
# the shear and the moment of a beam bending in its vertical plane, the second and the sixth of the
# beam forces.
R_INFO, R_VALUES, R_COLORS = 2, 3, 4
DISPLACEMENT, MAGNITUDE = 1, 0
BEAM_FORCES, VY, MZ = 2, 1, 5
L_TITLE, L_VALUES, L_COLORS = 0, 1, 2

# The HEAs of the library, in the order the design group tries them: the order is the user's.
HEAS = ["HEA100", "HEA120", "HEA140", "HEA160", "HEA180", "HEA200", "HEA220", "HEA240", "HEA260",
        "HEA280", "HEA300", "HEA320", "HEA340", "HEA360", "HEA400", "HEA450", "HEA500", "HEA550",
        "HEA600", "HEA650", "HEA700", "HEA800", "HEA900", "HEA1000"]

# Grasshopper's own panel yellow: a panel made in code has no colour of its own, and Rhino draws it
# see-through, with white text.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
YELLOW = Color.FromArgb(60, 230, 200, 40)
GREY = Color.FromArgb(40, 42, 45, 49)
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
        self.lead = {}

    def put(self, obj, col, row=None, gap=0, lead=0):
        """Puts obj in a cell. lead is room to leave on the column's left: a component with menus
        lays its inputs out wider once placed than when it is first measured."""
        obj.Attributes.ExpireLayout()
        obj.Attributes.PerformLayout()
        self.doc.AddObject(obj, False)
        self.items.append((obj, col, row, gap))
        self.lead[col] = max(self.lead.get(col, 0), lead)
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
            x += self.lead.get(c, 0)
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


def graft(param):
    """Gives each item a branch of its own."""
    param.DataMapping = GH_DataMapping.Graft
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


def boolean(param, value):
    """Sets an unwired true/false input."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Boolean(bool(value)))
    return param


def colour(param, value):
    """Sets an unwired colour input."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Colour(value))
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
    p.Properties.Colour = PANEL
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 90.0, 20.0)
    return grid.put(p, col, row, gap)


def lines(grid, name, values, width, height, col, row=None, gap=0):
    """A panel holding a list, one item a line: Multiline Data off."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
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
    p.Properties.Colour = PANEL
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    p.AddSource(source)
    return grid.put(p, col, row, gap)


def note(doc, title, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
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


def reparameterize(param):
    """Sets Reparameterize on a curve input, as its right-click menu does, so that the curve runs
    from 0 to 1 whatever its own domain. Grasshopper keeps the flag in a private field."""
    param = getattr(param, "__implementation__", param)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    param.GetType().GetField("m_reparameterize", flags).SetValue(param, True)
    return param


# ---------------------------------------------------------------- the beam

# Columns in the order the data flows...
INPUTS, PROFILE, POINTS, CURVE, INTERPOLATED, SPLIT, ON_CURVE, SHATTERED, SEGMENT, COUNT, DIVIDED, POLYLINES, PIECES = range(13)
MEMBER, NAMED, ELEMENT, MODEL = range(13, 17)
LIST, GROUP_, SETTINGS, OPTIMISE, RESULTS, SECTIONS_, UTILISATION_, GOVERNING_, VIEW = range(17, 26)
SETS, MERGED, SHIFTED, COMPARED, CULLED, PLANES, TAGS = range(26, 33)
# ...and rows, one per part of the model.
MAIN, BEARINGS, LOADING, SIZING, DRAWN = range(5)

# The shape of the curve: where along the span each of its points is, and how high, as fractions
# of the span and the rise. Edit the panels to reshape it; 28.7 m by 4.5 m makes it about 30 m long.
ALONG = ["0", "0.25", "0.5", "0.75", "1"]
HEIGHT = ["0", "0.7", "1", "0.55", "0.25"]

# How many times its true size Model View draws the deformation.
DEFORMATION_SCALE = 20


def build(doc):
    grid = Grid(doc)

    # 1 - The beam: what to try. The middle support and the load sit a fraction of the way along the
    # curve, its parameter from 0 to 1.
    span = slider(grid, "Span [m]", 2, 60, 28.7, 1, INPUTS, MAIN)
    rise = slider(grid, "Rise [m]", 0, 10, 4.5, 1, INPUTS, MAIN, gap=10)
    middle_at = slider(grid, "Middle support at", 0.05, 0.95, 0.5, 2, INPUTS, MAIN, gap=10)
    element_length = slider(grid, "Element length [m]", 0.05, 2, 0.25, 2, INPUTS, MAIN, gap=10)
    start_name = text(grid, "Starting section", "HEA300", INPUTS, BEARINGS)
    grade = value_list(grid, "Grade", [("S235", '"S235"'), ("S275", '"S275"'), ("S355", '"S355"'), ("S450", '"S450"')],
                       0, INPUTS, BEARINGS, gap=10)
    load_at = slider(grid, "First load at", 0, 1, 0.25, 2, INPUTS, LOADING)
    load = slider(grid, "First load [kN]", 0, 500, 150, 1, INPUTS, LOADING, gap=10)
    second_at = slider(grid, "Second load at", 0, 1, 0.75, 2, INPUTS, LOADING, gap=10)
    second = slider(grid, "Second load [kN]", 0, 500, 200, 1, INPUTS, LOADING, gap=10)
    gravity_factor = slider(grid, "Gravity factor", 0, 20, 10, 1, INPUTS, LOADING, gap=10)

    # 2 - Geometry: a curve in the vertical XZ plane through five points. A point along it, from
    # Evaluate Curve with the curve reparameterized, splits it in two at the closest point the curve
    # has to it; each segment is divided into as many elements of about Element length as it holds,
    # at least one, and each element is a beam of its own.
    along = lines(grid, "Along, of the span", ALONG, 120, 110, PROFILE, MAIN)
    height = lines(grid, "Height, of the rise", HEIGHT, 120, 110, PROFILE, MAIN, gap=10)
    xs = expression(grid, "f(a,S)=a*S", [along, span], "X", POINTS, MAIN)
    zs = expression(grid, "f(h,R)=h*R", [height, rise], "Z", POINTS, MAIN, gap=30)
    points = component(grid, CONSTRUCT_POINT, "Construct Point", "Through", CURVE, MAIN)
    wire(inp(points, 0), out(xs))
    wire(inp(points, 2), out(zs))
    curve = component(grid, INTERPOLATE, "Interpolate", "Beam", INTERPOLATED, MAIN)
    wire(inp(curve, 0), out(points))
    length_panel = panel(grid, "Length [m]", out(curve, 1), 100, 24, SPLIT, MAIN)
    middle = component(grid, EVALUATE_CURVE, "Evaluate Curve", "Middle point", SPLIT, MAIN, gap=30)
    reparameterize(wire(inp(middle, 0), out(curve, 0)))
    wire(inp(middle, 1), middle_at)
    on_curve = component(grid, CURVE_CP, "Curve Closest Point", "On the curve", ON_CURVE, MAIN)
    wire(inp(on_curve, 0), out(middle, 0))
    wire(inp(on_curve, 1), out(curve, 0))
    shatter = component(grid, SHATTER, "Shatter", "Two segments", SHATTERED, MAIN)
    wire(inp(shatter, 0), out(curve, 0))
    wire(inp(shatter, 1), out(on_curve, 1))
    segment_length = component(grid, LENGTH, "Length", "Segment length", SEGMENT, MAIN)
    wire(inp(segment_length, 0), out(shatter))
    counts = expression(grid, "f(L,s)=Max(1,Round(L/s))", [out(segment_length), element_length], "Elements per segment",
                        COUNT, MAIN)
    divide = component(grid, DIVIDE, "Divide Curve", "Nodes", DIVIDED, MAIN)
    wire(inp(divide, 0), out(shatter))
    wire(inp(divide, 1), out(counts))
    polyline = component(grid, POLYLINE, "PolyLine", None, POLYLINES, MAIN)
    wire(inp(polyline, 0), out(divide, 0))
    pieces = component(grid, EXPLODE, "Explode", "Elements", PIECES, MAIN)
    wire(inp(pieces, 0), out(polyline))

    # 3 - The member. Every element has the ElementId "Beam", which the design group names. Left
    # without a ZAxis, a beam's local z is horizontal and its local y in the vertical plane: the depth
    # of the H lies in the plane the beam curves in.
    material = component(grid, MATERIAL, "Material Library Elastic", None, MEMBER, MAIN)
    wire(inp(material, 0), grade)
    section = component(grid, SECTION, "Steel Section Library", "Starting section", MEMBER, BEARINGS)
    wire(inp(section, 0), start_name)
    wire(inp(section, 1), out(material))
    name = text(grid, "ElementId", "Beam", NAMED, MAIN)
    beam = component(grid, BEAM, "ForceBeamColumn", None, ELEMENT, MAIN)
    flat(wire(inp(beam, B_LINE), out(pieces, 0)))
    wire(inp(beam, B_SECTION), out(section))
    wire(inp(beam, B_ELEMENT_ID), name)

    # 4 - Built in at both ends; pinned at the point between the segments - held in place, free to
    # turn. Each load goes to the node closest to the point its slider picks along the curve: both
    # sliders wired into one input give a list of two, and two point loads.
    ends = component(grid, END_POINTS, "End Points", "Ends", SPLIT, BEARINGS)
    wire(inp(ends, 0), out(curve, 0))
    free = toggle(grid, "Free", False, SPLIT, BEARINGS, gap=30)
    fixed = component(grid, SUPPORT, "Support", "Fixed", ON_CURVE, BEARINGS)
    wire(inp(fixed, S_POSITION), out(ends, 0), out(ends, 1))
    pinned = component(grid, SUPPORT, "Support", "Pinned", ON_CURVE, BEARINGS, gap=30)
    wire(inp(pinned, S_POSITION), out(on_curve, 0))
    for i in (S_RX, S_RY, S_RZ):
        wire(inp(pinned, i), free)

    load_point = component(grid, EVALUATE_CURVE, "Evaluate Curve", "Load along", SPLIT, LOADING)
    reparameterize(wire(inp(load_point, 0), out(curve, 0)))
    wire(inp(load_point, 1), load_at, second_at)
    down = expression(grid, "f(P)=-P", [load], "Downwards", SPLIT, LOADING, gap=30)
    inp(down, 0).AddSource(second)
    nearest = component(grid, CLOSEST_POINT, "Closest Point", "Nearest node", ON_CURVE, LOADING)
    wire(inp(nearest, 0), out(load_point, 0))
    flat(wire(inp(nearest, 1), out(divide, 0)))
    direction = component(grid, UNIT_Z, "Unit Z", "Down", ON_CURVE, LOADING, gap=30)
    wire(inp(direction, 0), out(down))
    point_load = component(grid, POINT_LOAD, "Point Load", "Moving load", SHATTERED, LOADING)
    wire(inp(point_load, 0), out(nearest, 0))
    wire(inp(point_load, 1), out(direction))
    gravity = component(grid, GRAVITY, "Gravity Load", "Self-weight", SHATTERED, LOADING, gap=30)
    wire(inp(gravity, 0), gravity_factor)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, SEGMENT, LOADING)
    wire(inp(pattern, 1), out(point_load), out(gravity))

    # 5 - Assembled once; the optimiser analyses it again in every iteration, with the sections that
    # iteration gives it: a static analysis in one step, which it sets up itself.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, MAIN)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(fixed), out(pinned))
    wire(inp(assemble, 2), out(pattern))

    # 6 - One design group over every element - ElementId "Beam" - from the HEAs in the panel, tried
    # in the order it gives them. Uniform off, as it opens: each element its own, so the HEA varies
    # along the beam; on: one section for the whole beam. Type other names into the panel - HEBs,
    # IPEs - to size from those instead. The top flange is taken as held sideways every 25 cm, wired
    # into LengthLT in the Design menu: slide it up to leave the beam free between its supports.
    section_names = lines(grid, "Sections, in the order to try them", HEAS, 150, 330, LIST, SIZING)
    candidates = component(grid, SECTION, "Steel Section Library", "Sections", GROUP_, SIZING)
    wire(inp(candidates, 0), section_names)
    wire(inp(candidates, 1), out(material))
    group_name = text(grid, "Name", "Whole beam", SETTINGS, SIZING)
    uniform = toggle(grid, "Uniform", False, SETTINGS, SIZING, gap=10)
    design_group = component(grid, DESIGN_GROUP, "Design Group", None, OPTIMISE, SIZING)
    # Each element buckles over the whole of its span, the segment it is in, not over its own 25 cm:
    # the segment's length once for each of its elements, in the order the model has them - the first
    # span's, then the second's - into LengthY and LengthZ, about both axes.
    buckling = component(grid, DUPLICATE_DATA, "Duplicate Data", "Span of each element", GROUP_, SIZING, gap=30)
    graft(wire(inp(buckling, 0), out(segment_length)))
    graft(wire(inp(buckling, 1), out(counts)))
    wire(inp(design_group, G_NAME), group_name)
    wire(inp(design_group, G_ELEMENT_ID), name)
    wire(inp(design_group, G_SECTIONS), out(candidates))
    wire(inp(design_group, G_UNIFORM), uniform)
    threshold = slider(grid, "MaxUtilisation", 0.5, 1.0, 1.0, 2, SETTINGS, SIZING, gap=30)
    restraint = slider(grid, "Lateral restraint every [m]", 0.05, 30, 0.25, 2, SETTINGS, SIZING, gap=10)
    # Twice the optimiser's own 10: sized element by element under its weight ten times over, the beam
    # can take more than 10 to settle - the weight follows the sections, and the moments the stiffness.
    iterations = slider(grid, "Iterations", 1, 50, 20, 0, SETTINGS, SIZING, gap=10)
    optimiser = emit(OPTIMISER, "Section Optimiser")
    optimiser.NickName = "Whole beam"
    open_menus(optimiser)
    grid.put(optimiser, RESULTS, SIZING, lead=80)
    wire(inp(optimiser, O_MODEL), out(assemble))
    wire(inp(optimiser, O_GROUPS), out(design_group))
    wire(inp(optimiser, O_MAX_UTILISATION), threshold)
    wire(inp(optimiser, O_LENGTH_LT), restraint)
    wire(inp(optimiser, O_ITERATIONS), iterations)
    flat(wire(inp(optimiser, O_LENGTH_Y), out(buckling)))
    flat(wire(inp(optimiser, O_LENGTH_Z), out(buckling)))

    sections = panel(grid, "Section", out(optimiser, O_SECTION), 110, 420, SECTIONS_, SIZING)
    utilisation = panel(grid, "Utilisation", out(optimiser, O_UTILISATION), 110, 420, UTILISATION_, SIZING)
    governing = panel(grid, "Governing", out(optimiser, O_GOVERNING), 110, 420, GOVERNING_, SIZING)
    converged = panel(grid, "Converged", out(optimiser, O_CONVERGED), 150, 30, VIEW, SIZING)
    mass = panel(grid, "Mass", out(optimiser, O_MASS), 150, 60, VIEW, SIZING, gap=10)
    history = panel(grid, "History", out(optimiser, O_HISTORY), 420, 90, VIEW, SIZING, gap=10)
    # Model View draws the sized beam, a colour for each section, with its supports and the loads and
    # what each is worth, the loads drawn to fit the beam.
    view = component(grid, MODEL_VIEW, "Model View", None, VIEW, SIZING, gap=30)
    wire(inp(view, 0), out(optimiser, O_MODEL_OUT))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckShowLoadValues", "Active", True)
    menu_setting(view, "_ddColourBy", "Value", 1)

    # 7 - The HEA named wherever it changes: Create Set numbers the sections, and each is compared
    # with the one before it - the list shifted by one, with -1 added at its end so that the first
    # element always differs. What differs starts a run of one section: its name is written upright
    # in the beam's plane, 0.2 m high, above the middle of its first element, so that the names of
    # runs one element long, 25 cm apart, stand side by side.
    numbered = component(grid, CREATE_SET, "Create Set", "Numbered", SETS, SIZING)
    wire(inp(numbered, 0), sections)
    middles = component(grid, CURVE_MIDDLE, "Curve Middle", "Middles", SETS, SIZING, gap=30)
    flat(wire(inp(middles, 0), out(pieces, 0)))
    up = component(grid, UNIT_Z, "Unit Z", "Up", SETS, SIZING, gap=30)
    number(inp(up, 0), 0.4)
    back = component(grid, UNIT_X, "Unit X", "Back", SETS, SIZING, gap=30)
    number(inp(back, 0), -1)
    end = text(grid, "End", "-1", MERGED, SIZING)
    with_end = component(grid, MERGE, "Merge", "With an end", MERGED, SIZING, gap=30)
    # Both flattened into one list: the optimiser's outputs and the panel's are on different paths.
    flat(wire(inp(with_end, 0), out(numbered, 1)))
    flat(wire(inp(with_end, 1), end))
    above = component(grid, MOVE, "Move", "Above", MERGED, SIZING, gap=30)
    wire(inp(above, 0), out(middles))
    wire(inp(above, 1), out(up))
    before = component(grid, SHIFT_LIST, "Shift List", "The one before", SHIFTED, SIZING)
    wire(inp(before, 0), out(with_end))
    integer(inp(before, 1), -1)
    boolean(inp(before, 2), True)
    changes = component(grid, EQUALITY, "Equality", "Changes", COMPARED, SIZING)
    wire(inp(changes, 0), out(with_end))
    wire(inp(changes, 1), out(before))
    run_names = component(grid, CULL_PATTERN, "Cull Pattern", "Run names", CULLED, SIZING)
    wire(inp(run_names, 0), sections)
    wire(inp(run_names, 1), out(changes, 1))
    run_starts = component(grid, CULL_PATTERN, "Cull Pattern", "Run starts", CULLED, SIZING, gap=30)
    wire(inp(run_starts, 0), out(above, 0))
    wire(inp(run_starts, 1), out(changes, 1))
    upright = component(grid, CONSTRUCT_PLANE, "Construct Plane", "Upright", PLANES, SIZING)
    wire(inp(upright, 0), out(run_starts))
    wire(inp(upright, 1), out(up))
    wire(inp(upright, 2), out(back))
    tags = component(grid, TEXT_TAG_3D, "Text Tag 3D", "HEA", TAGS, SIZING)
    wire(inp(tags, T_LOCATION), out(upright))
    wire(inp(tags, T_TEXT), out(run_names))
    number(inp(tags, T_SIZE), 0.2)
    colour(inp(tags, T_COLOUR), DARK_TECH)
    integer(inp(tags, T_JUSTIFICATION), 4)

    # 8 - The optimiser's last analysis. The sized beam's deformed shape, extruded with its real
    # sections and coloured by how far each node moves, in mm - Deformation scale is in Model View's
    # Display menu - and, with their previews off, its bending moment Mz and its shear Vy: turn a
    # preview on to draw that one. Each Model View's Info gives the range it draws.
    results = emit(MODEL_VIEW, "Model View")
    results.NickName = "Deformed shape"
    open_menus(results)
    grid.put(results, SETS, DRAWN, lead=80)
    wire(inp(results, 0), out(optimiser, O_MODEL_OUT))
    menu_setting(results, "_ckDeformed", "Active", True)
    menu_setting(results, "_slDeformScale", "Value", DEFORMATION_SCALE)
    menu_setting(results, "_ckExtruded", "Active", True)
    menu_setting(results, "_ckReleases", "Active", False)
    show_result(results, DISPLACEMENT, MAGNITUDE)
    deformed_info = panel(grid, "Info", out(results, R_INFO), 300, 40, SETS, DRAWN, gap=30)
    moment = emit(MODEL_VIEW, "Model View")
    moment.NickName = "Bending moment"
    open_menus(moment)
    grid.put(moment, CULLED, DRAWN, lead=80)
    wire(inp(moment, 0), out(optimiser, O_MODEL_OUT))
    menu_setting(moment, "_ckExtruded", "Active", False)
    menu_setting(moment, "_ckReleases", "Active", False)
    show_result(moment, BEAM_FORCES, MZ)
    moment_info = panel(grid, "Info", out(moment, R_INFO), 300, 40, CULLED, DRAWN, gap=30)
    shear = emit(MODEL_VIEW, "Model View")
    shear.NickName = "Shear"
    open_menus(shear)
    grid.put(shear, PLANES, DRAWN, lead=80)
    wire(inp(shear, 0), out(optimiser, O_MODEL_OUT))
    menu_setting(shear, "_ckExtruded", "Active", False)
    menu_setting(shear, "_ckReleases", "Active", False)
    show_result(shear, BEAM_FORCES, VY)
    shear_info = panel(grid, "Info", out(shear, R_INFO), 300, 40, PLANES, DRAWN, gap=30)
    title = text(grid, "Title", "Displacement [mm]", SHIFTED, DRAWN)
    legend = component(grid, LEGEND, "Legend", None, COMPARED, DRAWN)
    wire(inp(legend, L_TITLE), title)
    wire(inp(legend, L_VALUES), out(results, R_VALUES))
    wire(inp(legend, L_COLORS), out(results, R_COLORS))

    grid.arrange(300, 660, stages=(PROFILE, MEMBER, MODEL, LIST, SETS), col_gap=60.0)

    group(doc, "1 - The beam", [span, rise, middle_at, element_length, start_name, grade, load_at, load,
                                second_at, second, gravity_factor], RED)
    group(doc, "2 - Geometry", [along, height, xs, zs, points, curve, length_panel, middle, on_curve, shatter,
                                segment_length, counts, divide, polyline, pieces], ORANGE)
    group(doc, "3 - Member", [material, section, name, beam], PURPLE)
    group(doc, "4 - Supports and loads", [ends, fixed, free, pinned, load_point, nearest, down, direction,
                                         point_load, gravity, pattern], GREEN)
    group(doc, "5 - Model", [assemble], BLUE)
    group(doc, "6 - Section Optimiser, one design group over the whole beam",
          [section_names, candidates, group_name, uniform, design_group, buckling, threshold, restraint,
           iterations, optimiser, sections, utilisation, governing, converged, mass, history, view], YELLOW)
    group(doc, "7 - The HEA named where it changes", [numbered, middles, up, back, end, with_end, above, before,
                                                      changes, run_names, run_starts, upright, tags], GREY)
    group(doc, "8 - Deformed shape, bending moment and shear", [results, deformed_info, title, legend, moment,
                                                                moment_info, shear, shear_info], BLUE)

    # Only the sized beam, its tags and its deformed shape draw in Rhino, not the construction lines
    # behind them.
    shown = [o.InstanceGuid for o in (view, tags, results, legend)]
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in shown:
            obj.Hidden = True

    note(doc, "Section Optimiser",
         "Two-span curved beam - a Section Optimiser example.\n\n"
         "A curve about 30 m long rises and falls in a vertical plane. A point along it - Middle support "
         "at, from 0 to 1 along the reparameterized curve - shatters it into two segments, and each "
         "segment is divided into elements of about 25 cm, as many as its length holds. The beam is "
         "built in at both ends and pinned at the point between the segments. It carries its own weight "
         "ten times over - Gravity factor, the Factor of Gravity Load - and two point loads, 150 kN on "
         "the first span and 200 kN on the second, each moved along the curve by its slider to the node "
         "closest to it. The weight follows the sections the optimiser gives the beam, iteration by "
         "iteration.\n\n"
         "It starts as an HEA300 in S235. One design group over the whole beam sizes it from the HEAs in "
         "the panel of 6, tried in the order the panel gives them. Uniform off, as it opens: each element "
         "takes the first that it can have, so the HEA varies along the beam - 7 names it wherever it "
         "changes. Uniform on: the whole beam takes the first that every element can have. Type other "
         "names into the panel - HEBs, IPEs - to size from those instead.\n\n"
         "8 draws the sized beam's deformed shape, extruded, 20 times its true size, coloured by "
         "displacement. Its bending moment and its shear are there too, with their previews off: turn "
         "one on to draw it. The optimiser checks resistance to EN 1993-1-1, not deflection.\n\n"
         "Each element's buckling length, about both axes - LengthY and LengthZ in the Design menu - is "
         "the length of its span, the segment it is in. The top flange is taken as held sideways every "
         "25 cm, wired into LengthLT. "
         "Slide it up to leave the beam free between its supports and lateral-torsional buckling takes "
         "over; checked element by element, that is only a rough guide.",
         300, 260, 620, 340)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Two-span beam built. Move the middle support and the load, or switch Uniform, and watch the optimiser.")


if __name__ == "__main__":
    main()
