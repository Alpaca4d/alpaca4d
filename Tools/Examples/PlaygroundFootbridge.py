#! python3
# The Footbridge, lesson 8 of the website's Playground: a timber footbridge across the hill lake
# that bounces when the herd of alpacas crosses it. Built on the Grasshopper canvas.
#
# make.sh, next to it, writes PlaygroundFootbridge.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundFootbridge.py
#     START=1 ./make.sh PlaygroundFootbridge.py PlaygroundFootbridgeStart.gh   # the deck, span and abutments only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the deck, a C24 timber deck 1.2 m wide and
# 120 mm deep; the span, 10 m from bank to bank in ten elements, so that the mass sits on nodes
# that can move; the abutments, a pin and a roller; the herd, an alpaca of 60 kg on every node, as
# a Mass Point in kg along X, Y and Z; the model assembled, the Mass Point going straight into
# LoadPatterns beside any load pattern; the natural vibration analysis, its first three
# frequencies in a panel and the first mode drawn; and the fix, a pier at mid-span, which halves
# the span and so makes the deck four times as quick. The Playground's lesson
# (assets/site/playground/footbridge.js in the website) draws its snippets from this definition,
# read by Tools/GhCanvas, and hands this file out at the end.
#
# With START=1 it writes the lesson's start in the docs instead: the deck, the span and the
# abutments, for the rest to be added by hand.
#
# The hand check, the deck as built: f = pi / (2 L^2) x sqrt(EI / m), with
# EI = 10 500 000 x 1.2 x 0.12^3 / 12 = 1 814 kN m^2 and m = 410 x 1.2 x 0.12 + 60 = 119 kg/m:
# f = pi / 200 x sqrt(1 814 000 / 119) = 1.94 Hz, which OpenSees gives too. With the pier, each half
# is a 5 m bridge: four times as quick, 7.75 Hz.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
SHATTER = "2AD2A4D4-3DE1-42F6-A4B8-F71835F35710"            # Curve > Division > Shatter
CURVE_MIDDLE = "CCC7B468-E743-4049-891F-299432545898"       # Curve > Analysis > Curve Middle
VECTOR_XYZ = "56B92EAB-D121-43F7-94D3-6CD8F0DDEAD8"         # Vector > Vector > Vector XYZ

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
RECTANGLE = "888184F3-A448-4F82-9DCA-E57EFAC8E840"          # RectangleCS
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
MASS_POINT = "5C7056ED-01D4-4DDE-AD9E-46A5266ECEE6"         # Mass Point
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
NATURAL_VIBRATION = "3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3"  # Natural Vibration Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_TX, S_RY, S_RZ = 0, 1, 5, 6
# Divide Curve's outputs: the points, the tangents and the parameters.
D_POINTS, D_PARAMETERS = 0, 2
# Natural Vibration's outputs: the solved model, the eigenvalues, the periods and the frequencies.
NV_MODEL, NV_FREQUENCIES = 0, 3
# Model View's results, from 1: Displacement is the first, and Magnitude the first of its components.
DISPLACEMENT, MAGNITUDE = 1, 0
# The start of the docs lesson, or the whole footbridge.
START = System.Environment.GetEnvironmentVariable("START") == "1"

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per round of the lesson.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
YELLOW = Color.FromArgb(60, 230, 200, 40)
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


def text(grid, name, value, col, row=None, gap=0):
    """A small panel holding a text, such as a grade's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 50.0, 20.0)
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


def menu_setting(c, field, prop, value):
    """Sets a widget of a component's fold-out menus, found by its field, as a click on it would:
    the component saves it with the file."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    setattr(c.GetType().GetField(field, flags).GetValue(c), prop, value)


def show_result(c, show, component_):
    """Ticks one of Model View's results alone, as clicks in its Results menu do."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    c.GetType().GetMethod("ShowResult", flags).Invoke(c, System.Array[System.Object]([show, component_]))
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()

def readout(doc, name, source, width=120.0, height=64.0):
    """A panel showing what source carries, placed by hand."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Colour = PANEL
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    doc.AddObject(p, False)
    wire(p, source)
    return p


def loose(doc, obj):
    """An object placed by hand, out of the grid."""
    obj.Attributes.ExpireLayout()
    obj.Attributes.PerformLayout()
    doc.AddObject(obj, False)
    return obj


def body(obj):
    """What Grasshopper draws of an object: for a component, the box around the ports near its
    pivot. A port a folded menu holds is left at the origin by a definition written outside Rhino,
    and would stretch the bounds across the canvas."""
    a = obj.Attributes
    params = getattr(obj, "Params", None)
    if params is None:
        return a.Bounds
    near = []
    for q in list(params.Input) + list(params.Output):
        b = q.Attributes.Bounds
        if abs(b.X + b.Width / 2 - a.Pivot.X) < 300 and abs(b.Y + b.Height / 2 - a.Pivot.Y) < 300:
            near.append(b)
    if not near:
        return a.Bounds
    x0 = min(b.Left for b in near)
    y0 = min(b.Top for b in near)
    x1 = max(b.Right for b in near)
    y1 = max(b.Bottom for b in near)
    return RectangleF(x0, y0, x1 - x0, y1 - y0)


def place(obj, left, top):
    """Moves an object so that what Grasshopper draws of it starts at (left, top)."""
    b = body(obj)
    a = obj.Attributes
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


# ---------------------------------------------------------------- the footbridge

# Columns in the order the data flows...
INPUTS, POINTS, CURVES, AMOUNTS, DIVIDED, SHATTERED, ELEMENT, MODEL = range(8)
# ...and rows, one per part of the model.
SECTION_, LINE_, SUPPORTS, HERD, PIER = range(5)


def build(doc):
    grid = Grid(doc)

    # 1 - The deck: C24 timber, 1.2 m wide and 120 mm deep, in metres. On a horizontal beam a
    # rectangle's Height points up, so the deck lies flat.
    width = slider(grid, "Deck width", 0, 3, 1.2, 2, INPUTS, SECTION_)
    depth = slider(grid, "Deck depth", 0, 1, 0.12, 3, INPUTS, SECTION_)
    grade = text(grid, "Grade", "C24", INPUTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, POINTS, SECTION_)
    wire(inp(material, 0), grade)
    section = component(grid, RECTANGLE, "RectangleCS", None, CURVES, SECTION_)
    wire(inp(section, 1), width)
    wire(inp(section, 2), depth)
    wire(inp(section, 3), out(material))

    # 2 - The span: 10 m from bank to bank along X, in ten elements, so that the deck's mass sits
    # on nine nodes free to move as well as on the two over the abutments.
    span = slider(grid, "Span", 1, 20, 10, 1, INPUTS, LINE_)
    bank_a = component(grid, CONSTRUCT_POINT, "Construct Point", "Bank A", POINTS, LINE_)
    bank_b = component(grid, CONSTRUCT_POINT, "Construct Point", "Bank B", POINTS, LINE_)
    wire(inp(bank_b, 0), span)
    line = component(grid, LINE, "Line", "Deck line", CURVES, LINE_)
    wire(inp(line, 0), out(bank_a))
    wire(inp(line, 1), out(bank_b))
    count = slider(grid, "Elements", 1, 20, 10, 0, AMOUNTS, LINE_)
    divide = component(grid, DIVIDE, "Divide Curve", None, DIVIDED, LINE_)
    wire(inp(divide, 0), out(line))
    wire(inp(divide, 1), count)
    shatter = component(grid, SHATTER, "Shatter", None, SHATTERED, LINE_)
    wire(inp(shatter, 0), out(line))
    wire(inp(shatter, 1), out(divide, D_PARAMETERS))
    beam = component(grid, BEAM, "ForceBeamColumn", "Deck", ELEMENT, LINE_)
    wire(inp(beam, 0), out(shatter))
    wire(inp(beam, 1), out(section))

    # 3 - The abutments: a pin at Bank A and a roller at Bank B, free to turn about Y and Z, the
    # roller free to slide along X too. Both hold Rx, against the deck twisting about its axis.
    free = toggle(grid, "Free", False, AMOUNTS, SUPPORTS)
    pin = component(grid, SUPPORT, "Support", "Pin", ELEMENT, SUPPORTS)
    wire(inp(pin, S_POSITION), out(bank_a))
    wire(inp(pin, S_RY), free)
    wire(inp(pin, S_RZ), free)
    roller = component(grid, SUPPORT, "Support", "Roller", ELEMENT, SUPPORTS)
    wire(inp(roller, S_POSITION), out(bank_b))
    wire(inp(roller, S_TX), free)
    wire(inp(roller, S_RY), free)
    wire(inp(roller, S_RZ), free)

    if START:
        grid.arrange(0, 0, stages=(AMOUNTS,), row_gap=60.0)
        group(doc, "1 - The deck", [width, depth, grade, material, section], RED)
        group(doc, "2 - The span", [span, bank_a, bank_b, line, count, divide, shatter, beam], ORANGE)
        group(doc, "3 - The abutments", [free, pin, roller], PURPLE)
        note(doc, "The Footbridge - start",
             "The Footbridge - lesson 8 of the Alpaca4d docs, the start.\n\n"
             "A C24 timber deck, 1.2 m wide and 120 mm deep, across 10 m in ten elements, on a pin "
             "and a roller. Add the herd's mass, the natural vibration analysis and the pier as the "
             "lesson's steps say.",
             0, -240, 460, 160)
        return

    # 4 - The herd: an alpaca of 60 kg on every node, nose to tail, as a Mass Point. A mass is in
    # kg, and moves along all three axes: the same 60 along X, Y and Z.
    each = slider(grid, "Alpaca mass", 0, 200, 60, 0, AMOUNTS, HERD)
    vector = component(grid, VECTOR_XYZ, "Vector XYZ", "Each alpaca", DIVIDED, HERD)
    wire(inp(vector, 0), each)
    wire(inp(vector, 1), each)
    wire(inp(vector, 2), each)
    herd = component(grid, MASS_POINT, "Mass Point", "Herd", ELEMENT, HERD)
    wire(inp(herd, 0), out(divide, D_POINTS))
    wire(inp(herd, 1), out(vector))

    # 5 - Assemble: the elements, both abutments and the herd. A Mass Point goes straight into
    # LoadPatterns, beside the load patterns: here there are none, as the modes need no load.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 2), out(herd))

    # 6 - Natural vibration: the first three modes, their frequencies in a panel and the first one
    # drawn. Assemble Model, Natural Vibration and Model View in a line; the modes' count under it,
    # and the panel between, below the wire that runs on to Model View (placed once the rows are).
    modes = slider(Grid(doc), "Modes", 1, 10, 3, 0, None)
    vibration = loose(doc, emit(NATURAL_VIBRATION, "Natural Vibration Analysis"))
    wire(inp(vibration, 0), out(assemble))
    wire(inp(vibration, 1), modes)
    frequencies = readout(doc, "Frequencies", out(vibration, NV_FREQUENCIES))
    view = loose(doc, emit(MODEL_VIEW, "Model View"))
    view.NickName = "The deck"
    wire(inp(view, 0), out(vibration, NV_MODEL))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_ckExtruded", "Active", True)
    show_result(view, DISPLACEMENT, MAGNITUDE)

    # 7 - The pier: a support at mid-span, holding the deck up there and letting it turn and slide.
    # Each half is then a 5 m bridge, four times as quick.
    middle = component(grid, CURVE_MIDDLE, "Curve Middle", "Mid-span", DIVIDED, PIER)
    wire(inp(middle, 0), out(line))
    pier = component(grid, SUPPORT, "Support", "Pier", ELEMENT, PIER)
    wire(inp(pier, S_POSITION), out(middle))
    wire(inp(pier, S_TX), free)
    wire(inp(pier, S_RY), free)
    wire(inp(pier, S_RZ), free)
    wire(inp(assemble, 1), out(pin), out(roller), out(pier))

    grid.arrange(0, 0, stages=(AMOUNTS, MODEL), row_gap=60.0)
    # In a line from Assemble Model, with wide gaps: Grasshopper draws these components wider
    # than they are measured without Rhino.
    model = body(assemble)
    place(modes, model.Right + 80.0, model.Bottom + 20.0)
    place(vibration, body(modes).Right + 90.0, model.Y + 2.0)
    in_line = body(vibration)
    place(frequencies, in_line.Right + 90.0, in_line.Y + 50.0)
    place(view, body(frequencies).Right + 90.0, in_line.Y)

    group(doc, "1 - The deck", [width, depth, grade, material, section], RED)
    group(doc, "2 - The span", [span, bank_a, bank_b, line, count, divide, shatter, beam], ORANGE)
    group(doc, "3 - The abutments", [free, pin, roller], PURPLE)
    group(doc, "4 - The herd", [each, vector, herd], GREEN)
    group(doc, "5 - Assemble", [assemble], YELLOW)
    group(doc, "6 - Natural vibration", [modes, vibration, frequencies, view], GREY)
    group(doc, "7 - The pier", [middle, pier], BLUE)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Footbridge",
         "The Footbridge - lesson 8 of the Alpaca4d Playground.\n\n"
         "A C24 timber deck, 1.2 m wide and 120 mm deep, across 10 m, on a pin and a roller, with "
         "a herd of alpacas on it, 60 kg every metre. As built it bounces at 1.94 Hz, the pace of "
         "walking; the pier at mid-span makes it 7.75 Hz.\n\n"
         "Check it by hand: f = pi / (2 L^2) x sqrt(EI / m), with EI = 1 814 kN m2 and "
         "m = 410 x 1.2 x 0.12 + 60 = 119 kg/m: 1.94 Hz for L = 10 m, four times that for 5 m.\n\n"
         "Try: unwire the pier from Assemble Model, make the deck deeper, or move Alpaca mass.",
         0, -300, 520, 220)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Footbridge built. Move Deck depth or Alpaca mass and watch the frequencies.")


if __name__ == "__main__":
    main()
