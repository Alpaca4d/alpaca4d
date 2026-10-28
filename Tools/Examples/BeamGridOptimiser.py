#! python3
# A square grid of steel beams on four point supports, sized by the Section Optimiser: the beam grid
# example, built on the Grasshopper canvas.
#
# make.sh, next to it, writes BeamGridOptimiser.gh straight from this script, without Rhino:
#     ./make.sh BeamGridOptimiser.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 20 m square grid, 10 bays each way, of beams cut at every crossing, carries its own weight and a
# point load at every node, as a slab would hand them down. Four point supports, pinned, sit at the
# corners to begin with; the Supports slider walks them along the diagonals towards the centre, one
# bay at a time. At the corners the grid spans between them as a slab does; near the centre it
# cantilevers out to its edges. The Section Optimiser, with no design group, sizes every element on
# its own within the family of the section it starts with, so the grid shows where the material goes.
# The sections are only a start: choose your own.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_String
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
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
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
VIEW_RESULTS = "4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3"       # View Results
OPTIMISER = "56302D3A-734F-4B03-899E-F6C273CF1B9F"          # Section Optimiser
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend

# ForceBeamColumn's inputs, by place.
B_LINE, B_SECTION, B_ELEMENT_ID = 0, 1, 5
# Section Optimiser's inputs and outputs, by place.
O_MODEL, O_MAX_UTILISATION, O_ITERATIONS = 0, 3, 4
O_MODEL_OUT, O_MASS, O_CONVERGED, O_HISTORY = 0, 4, 5, 8
# View Results' outputs and Legend's inputs, by place.
R_VALUES, R_COLORS = 1, 2
L_TITLE, L_VALUES, L_COLORS, L_POSITION = 0, 1, 2, 3

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
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


# ---------------------------------------------------------------- the grid

# Columns in the order the data flows...
INPUTS, NUMBERS, SERIES_, POINTS, LINES, DIVIDED, POLYLINES, SEGMENTS, NAMES, ELEMENT, LOADS, PATTERN = range(12)
MODEL, OPTIMISE, RESULTS, KEY = range(12, 16)
# ...and rows, one per part of the model.
ALONG_X, ALONG_Y, BEARINGS, LOADING = range(4)


def build(doc):
    grid = Grid(doc)

    # 1 - The grid: what to try. The Supports slider goes from the corners (0) to the bays next to
    # the centre (1), a bay at a time.
    size = slider(grid, "Size [m]", 4, 40, 20, 1, INPUTS, ALONG_X)
    bays = slider(grid, "Bays", 2, 20, 10, 0, INPUTS, ALONG_X, gap=10)
    supports = slider(grid, "Supports, corner to centre", 0, 1, 0, 2, INPUTS, BEARINGS)
    start_name = value_list(grid, "Starting section",
                            [("IPE300", '"IPE300"'), ("HEA300", '"HEA300"'), ("HEB300", '"HEB300"')], 1, INPUTS, ALONG_Y)
    grade = value_list(grid, "Grade", [("S235", '"S235"'), ("S275", '"S275"'), ("S355", '"S355"')], 2, INPUTS, ALONG_Y, gap=10)
    load = slider(grid, "Node load [kN]", 0, 100, 20, 1, INPUTS, LOADING)

    # 2 - Geometry: a line along X and one along Y at every grid line, each cut at every crossing, so
    # that the beams meet at the nodes.
    step = expression(grid, "f(L,n)=L/n", [size, bays], "Bay", NUMBERS, ALONG_X)
    count = expression(grid, "f(n)=n+1", [bays], "Grid lines", NUMBERS, ALONG_Y)
    lines_at = component(grid, SERIES, "Series", "Grid line positions", SERIES_, ALONG_X)
    number(inp(lines_at, 0), 0.0)
    wire(inp(lines_at, 1), out(step))
    wire(inp(lines_at, 2), out(count))

    x_start = component(grid, CONSTRUCT_POINT, "Construct Point", "West ends", POINTS, ALONG_X)
    wire(inp(x_start, 1), out(lines_at))
    x_end = component(grid, CONSTRUCT_POINT, "Construct Point", "East ends", POINTS, ALONG_X, gap=10)
    wire(inp(x_end, 0), size)
    wire(inp(x_end, 1), out(lines_at))
    y_start = component(grid, CONSTRUCT_POINT, "Construct Point", "South ends", POINTS, ALONG_Y)
    wire(inp(y_start, 0), out(lines_at))
    y_end = component(grid, CONSTRUCT_POINT, "Construct Point", "North ends", POINTS, ALONG_Y, gap=10)
    wire(inp(y_end, 0), out(lines_at))
    wire(inp(y_end, 1), size)

    x_lines = component(grid, LINE, "Line", "Along X", LINES, ALONG_X)
    wire(inp(x_lines, 0), out(x_start))
    wire(inp(x_lines, 1), out(x_end))
    y_lines = component(grid, LINE, "Line", "Along Y", LINES, ALONG_Y)
    wire(inp(y_lines, 0), out(y_start))
    wire(inp(y_lines, 1), out(y_end))

    x_cut = component(grid, DIVIDE, "Divide Curve", "Nodes", DIVIDED, ALONG_X)
    wire(inp(x_cut, 0), out(x_lines))
    wire(inp(x_cut, 1), bays)
    y_cut = component(grid, DIVIDE, "Divide Curve", "Cut Y", DIVIDED, ALONG_Y)
    wire(inp(y_cut, 0), out(y_lines))
    wire(inp(y_cut, 1), bays)
    x_poly = component(grid, POLYLINE, "PolyLine", None, POLYLINES, ALONG_X)
    wire(inp(x_poly, 0), out(x_cut, 0))
    y_poly = component(grid, POLYLINE, "PolyLine", None, POLYLINES, ALONG_Y)
    wire(inp(y_poly, 0), out(y_cut, 0))
    x_pieces = component(grid, EXPLODE, "Explode", "Beams X", SEGMENTS, ALONG_X)
    wire(inp(x_pieces, 0), out(x_poly))
    y_pieces = component(grid, EXPLODE, "Explode", "Beams Y", SEGMENTS, ALONG_Y)
    wire(inp(y_pieces, 0), out(y_poly))

    # 3 - The beams, every element with the ElementId "Grid". Left without a ZAxis, a beam's local y
    # points up: the depth of the I is vertical.
    material = component(grid, MATERIAL, "Material Library Elastic", None, NAMES, ALONG_Y)
    wire(inp(material, 0), grade)
    section = component(grid, SECTION, "Steel Section Library", "Starting section", NAMES, ALONG_X)
    wire(inp(section, 0), start_name)
    wire(inp(section, 1), out(material))
    name = text(grid, "ElementId", "Grid", NAMES, ALONG_X, gap=10)
    beam = component(grid, BEAM, "ForceBeamColumn", None, ELEMENT, ALONG_X)
    flat(wire(inp(beam, B_LINE), out(x_pieces, 0), out(y_pieces, 0)))
    wire(inp(beam, B_SECTION), out(section))
    wire(inp(beam, B_ELEMENT_ID), name)

    # 4 - Four supports on the diagonals, k bays in from the corners: k = 0 at the corners, and at
    # most one bay short of the centre, so that the four stay apart. Pinned: the grid is held at four
    # points and free to turn on them.
    offset = expression(grid, "f(t,n)=Round(t*(Floor(n/2)-1))", [supports, bays], "Bays in", NUMBERS, BEARINGS)
    near = expression(grid, "f(k,L,n)=k*L/n", [out(offset), size, bays], "Near", SERIES_, BEARINGS)
    far = expression(grid, "f(k,L,n)=L-k*L/n", [out(offset), size, bays], "Far", SERIES_, BEARINGS, gap=10)
    corners = []
    for nickname, x, y in (("South-west", near, near), ("South-east", far, near), ("North-west", near, far), ("North-east", far, far)):
        p = component(grid, CONSTRUCT_POINT, "Construct Point", nickname, POINTS, BEARINGS, gap=10 if corners else 0)
        wire(inp(p, 0), out(x))
        wire(inp(p, 1), out(y))
        corners.append(p)
    held = toggle(grid, "Held", True, LINES, BEARINGS)
    free = toggle(grid, "Free", False, LINES, BEARINGS, gap=10)
    support = component(grid, SUPPORT, "Support", "Pinned", ELEMENT, BEARINGS)
    wire(inp(support, 0), *[out(p) for p in corners])
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    # 5 - Loads: its own weight, and a load down at every node, as a slab on the grid would bring.
    down = expression(grid, "f(P)=-P", [load], "Downwards", NUMBERS, LOADING)
    direction = component(grid, UNIT_Z, "Unit Z", "Down", SERIES_, LOADING)
    wire(inp(direction, 0), out(down))
    node_load = component(grid, POINT_LOAD, "Point Load", "Node loads", LOADS, LOADING)
    flat(wire(inp(node_load, 0), out(x_cut, 0)))
    wire(inp(node_load, 1), out(direction))
    gravity = component(grid, GRAVITY, "Gravity Load", "Self-weight", LOADS, LOADING, gap=10)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOADING)
    flat(wire(inp(pattern, 1), out(node_load), out(gravity)))

    # 6 - The optimiser: no design group, so every element goes to the first section of its family -
    # the HEAs, for an HEA300 - that holds it, smallest area first. Model View draws the sections it
    # chose; View Results, hidden, how far the grid moves, with a Legend on the right of the view,
    # hidden with it. Switch Model View off and View Results and Legend on to see them.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, ALONG_X)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(support))
    wire(inp(assemble, 2), out(pattern))
    max_utilisation = slider(grid, "MaxUtilisation", 0.5, 1.0, 1.0, 2, MODEL, BEARINGS)
    optimiser = emit(OPTIMISER, "Section Optimiser")
    optimiser.NickName = "Every element"
    open_menus(optimiser)
    grid.put(optimiser, OPTIMISE, ALONG_X)
    wire(inp(optimiser, O_MODEL), out(assemble))
    wire(inp(optimiser, O_MAX_UTILISATION), max_utilisation)

    mass = panel(grid, "Mass", out(optimiser, O_MASS), 150, 60, RESULTS, ALONG_X)
    converged = panel(grid, "Converged", out(optimiser, O_CONVERGED), 150, 30, RESULTS, ALONG_X, gap=10)
    history = panel(grid, "History", out(optimiser, O_HISTORY), 460, 110, RESULTS, ALONG_X, gap=10)
    view = component(grid, MODEL_VIEW, "Model View", None, RESULTS, ALONG_Y)
    wire(inp(view, 0), out(optimiser, O_MODEL_OUT))
    results = component(grid, VIEW_RESULTS, "View Results", None, RESULTS, ALONG_Y, gap=10)
    wire(inp(results, 0), out(optimiser, O_MODEL_OUT))
    legend = component(grid, LEGEND, "Legend", None, KEY, ALONG_Y)
    words(inp(legend, L_TITLE), "Displacement [mm]")
    wire(inp(legend, L_VALUES), out(results, R_VALUES))
    wire(inp(legend, L_COLORS), out(results, R_COLORS))
    integer(inp(legend, L_POSITION), 1)

    grid.arrange(0, 0, stages=(NUMBERS, NAMES, MODEL))

    group(doc, "1 - The grid", [size, bays, supports, start_name, grade, load], RED)
    group(doc, "2 - Geometry", [step, count, lines_at, x_start, x_end, y_start, y_end, x_lines, y_lines,
                                x_cut, y_cut, x_poly, y_poly, x_pieces, y_pieces], ORANGE)
    group(doc, "3 - Beams", [material, section, name, beam], PURPLE)
    group(doc, "4 - Supports", [offset, near, far] + corners + [held, free, support], GREEN)
    group(doc, "5 - Loads", [down, direction, node_load, gravity, pattern], BLUE)
    group(doc, "6 - Every element on its own, within its family",
          [assemble, max_utilisation, optimiser, mass, converged, history, view, results, legend], YELLOW)

    # Only the sized grid draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "Beam grid",
         "Beam grid - a Section Optimiser example.\n\n"
         "A 20 m square grid of steel beams, 10 bays each way, carries its own weight and a load at "
         "every node, as a slab on it would bring. Four pinned supports start at the corners.\n\n"
         "Slide 'Supports, corner to centre' to walk them in along the diagonals, a bay at a time. "
         "At the corners the grid spans between them as a slab does, and the beams along the edges and "
         "through the middle work hardest. Near the centre it cantilevers out to its edges: the "
         "material gathers over the supports and the edges go light.\n\n"
         "The Section Optimiser has no design group, so every element is sized on its own, within the "
         "family of the section it starts with - the HEAs, for an HEA300 - smallest area first. Model "
         "View draws the sections it chose; switch it off, and View Results and Legend on, for how far "
         "the grid moves.\n\n"
         "The sections are only a start: choose your own, or give lines of beams a Design Group each "
         "so that every line ends up with one section.",
         0, -380, 620, 320)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Beam grid built. Move the supports in and watch the sections change.")


if __name__ == "__main__":
    main()
