#! python3
# The Valley, the fifth lesson of the website's Playground: a steel truss bridge over the gorge,
# with the alpaca standing on its deck in the middle. Built on the Grasshopper canvas.
#
# make.sh, next to it, writes PlaygroundValley.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundValley.py
#     START=1 ./make.sh PlaygroundValley.py PlaygroundValleyStart.gh    # the deck and the triangles only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the deck, 12 m from bank to bank in six panels
# of 2 m; the triangles, a top joint over the middle of each panel, 2 m up, with a diagonal up to it
# and one down from it, and the top chord between the top joints - a Warren truss; the bars, Truss
# elements of a steel tube 100 x 5 mm, which carry axial force only; the supports, a pin at Bank A,
# a roller at Bank B, and every other joint held sideways, as the deck and the bracing between the
# bridge's two trusses hold this one; the alpaca, 0.6 kN down on the deck joint at mid-span; the
# model assembled; and the analysis, drawn with each bar's axial force N. The Playground's lesson
# (assets/site/playground/valley.js in the website) draws its snippets from this definition, read
# by Tools/GhCanvas, and hands this file out at the end.
#
# With START=1 it writes the lesson's start in the docs instead: the deck and the triangles, for
# the rest to be added by hand.
#
# The hand check, cutting through the middle panel: the top chord there carries the moment under
# the alpaca over the depth, R x 6 / h = 0.3 x 6 / 2 = 0.9 kN, pushed; the bottom chord below the top
# joint at 5 m, 0.3 x 5 / 2 = 0.75 kN, pulled; each diagonal the shear over the sine of its slope,
# 0.3 / (2 / sqrt(5)) = 0.335 kN. OpenSees gives -0.900, 0.750 and 0.335 kN.
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
END_POINTS = "11BBD48B-BB0A-4F1B-8167-FA297590390D"         # Curve > Analysis > End Points
CURVE_MIDDLE = "CCC7B468-E743-4049-891F-299432545898"       # Curve > Analysis > Curve Middle
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
SHIFT_LIST = "4FDFE351-6C07-47CE-9FB9-BE027FB62186"         # Sets > List > Shift List

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
CIRCLE = "AFCC30B2-7CB9-453F-B5A1-C73554BD3DC7"             # CircleCS
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # the beam switcher, put on Truss
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# The beam switcher's truss, by its unit's name.
TRUSS = "Truss (Alpaca4d)"
# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_TX, S_TZ = 0, 1, 3
# Divide Curve's outputs: the points, the tangents and the parameters. End Points': start and end.
D_PARAMETERS = 2
E_START, E_END = 0, 1
# Model View's results, from 1: Beam forces is the second, and N the first of its components.
BEAM_FORCES, N = 2, 0
# The start of the docs lesson, or the whole bridge.
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


def close_up(objs, to, gap):
    """Moves objects right, together, until the rightmost is gap short of the left of to."""
    right = max(o.Attributes.Bounds.Right for o in objs)
    dx = to.Attributes.Bounds.X - gap - right
    for o in objs:
        b = o.Attributes.Bounds
        move(o, b.X + dx, b.Y)


# ---------------------------------------------------------------- the bridge

# Columns in the order the data flows...
INPUTS, POINTS, CURVES, DIVIDED, SHATTERED, PANEL_PARTS, LIFTED, LINES, BARS, ELEMENT, PATTERN, MODEL, SETTINGS, RUN, VIEW = range(15)
# ...and rows, one per part of the model.
DECK_, TOP_, SECTION_, SUPPORTS, LOAD = range(5)


def build(doc):
    grid = Grid(doc)

    # 1 - The deck: from one bank to the other, 12 m along X, in six panels of 2 m. Divide Curve
    # gives the deck's joints, and Shatter cuts the deck line at them into the bottom chord's bars.
    span = slider(grid, "Span", 4, 24, 12, 1, INPUTS, DECK_)
    panels = slider(grid, "Panels", 2, 12, 6, 0, INPUTS, DECK_)
    bank_a = component(grid, CONSTRUCT_POINT, "Construct Point", "Bank A", POINTS, DECK_)
    bank_b = component(grid, CONSTRUCT_POINT, "Construct Point", "Bank B", POINTS, DECK_)
    wire(inp(bank_b, 0), span)
    deck = component(grid, LINE, "Line", "Deck line", CURVES, DECK_)
    wire(inp(deck, 0), out(bank_a))
    wire(inp(deck, 1), out(bank_b))
    divide = component(grid, DIVIDE, "Divide Curve", None, DIVIDED, DECK_)
    wire(inp(divide, 0), out(deck))
    wire(inp(divide, 1), panels)
    shatter = component(grid, SHATTER, "Shatter", "Bottom chord", SHATTERED, DECK_)
    wire(inp(shatter, 0), out(deck))
    wire(inp(shatter, 1), out(divide, D_PARAMETERS))

    # 2 - The triangles: a top joint over the middle of each panel, Depth up, a diagonal from the
    # panel's start up to it and one down from it to the panel's end, and the top chord through the
    # top joints, exploded into its bars.
    depth = slider(grid, "Depth", 0.5, 4, 2, 1, SHATTERED, TOP_)
    ends = component(grid, END_POINTS, "End Points", "Panel ends", PANEL_PARTS, TOP_)
    wire(inp(ends, 0), out(shatter))
    middles = component(grid, CURVE_MIDDLE, "Curve Middle", "Panel middles", PANEL_PARTS, TOP_)
    wire(inp(middles, 0), out(shatter))
    lift = component(grid, UNIT_Z, "Unit Z", "Lift", PANEL_PARTS, TOP_)
    wire(inp(lift, 0), depth)
    top = component(grid, MOVE, "Move", "Top joints", LIFTED, TOP_)
    wire(inp(top, 0), out(middles))
    wire(inp(top, 1), out(lift))
    up = component(grid, LINE, "Line", "Up diagonals", LINES, TOP_)
    wire(inp(up, 0), out(ends, E_START))
    wire(inp(up, 1), out(top))
    down = component(grid, LINE, "Line", "Down diagonals", LINES, TOP_)
    wire(inp(down, 0), out(top))
    wire(inp(down, 1), out(ends, E_END))
    chord = component(grid, POLYLINE, "PolyLine", "Top chord", LINES, TOP_)
    wire(inp(chord, 0), out(top))
    top_bars = component(grid, EXPLODE, "Explode", "Top bars", BARS, TOP_)
    wire(inp(top_bars, 0), out(chord))

    if START:
        grid.arrange(0, 0, row_gap=60.0)
        group(doc, "1 - The deck", [span, panels, bank_a, bank_b, deck, divide, shatter], RED)
        group(doc, "2 - The triangles", [depth, ends, middles, lift, top, up, down, chord, top_bars], ORANGE)
        note(doc, "The Valley - start",
             "The Valley - lesson 5 of the Alpaca4d docs, the start.\n\n"
             "The truss's lines: the deck, 12 m along X in six panels of 2 m, and the triangles over "
             "it, 2 m deep - the bottom chord's bars, the diagonals and the top chord's bars. Make "
             "them Truss bars, then add the supports, the alpaca and the analysis as the lesson's "
             "steps say.",
             0, -240, 460, 170)
        return

    # 3 - The bars: every line a Truss bar - the beam component switched to Truss - of a steel
    # tube 100 x 5 mm in S355. Line takes the four lists, one wire each.
    diameter = slider(grid, "Tube diameter", 0, 0.5, 0.1, 3, PANEL_PARTS, SECTION_)
    wall = slider(grid, "Tube wall", 0, 0.05, 0.005, 3, PANEL_PARTS, SECTION_)
    grade = text(grid, "Grade", "S355", PANEL_PARTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, LIFTED, SECTION_)
    wire(inp(material, 0), grade)
    tube = component(grid, CIRCLE, "CircleCS", "Tube", LINES, SECTION_)
    wire(inp(tube, 1), diameter)
    wire(inp(tube, 2), wall)
    wire(inp(tube, 3), out(material))
    truss = component(grid, BEAM, "Truss", "Truss", ELEMENT, SECTION_)
    switch_unit(truss, TRUSS)
    wire(inp(truss, 0), out(shatter), out(up), out(down), out(top_bars))
    wire(inp(truss, 1), out(tube))

    # 4 - The supports: a pin at Bank A, a roller at Bank B free along X, and every other joint held
    # sideways only, along Y - the deck and the bracing between the bridge's two trusses hold this
    # one so. The deck's joints between the banks are the panels' starts but the first: Shift List,
    # not wrapping round, drops it. A support's rotations mean nothing at a joint only bars reach.
    free = toggle(grid, "Free", False, PANEL_PARTS, SUPPORTS)
    wrap = toggle(grid, "Wrap", False, PANEL_PARTS, SUPPORTS)
    inner = component(grid, SHIFT_LIST, "Shift List", "Deck joints", LIFTED, SUPPORTS)
    wire(inp(inner, 0), out(ends, E_START))
    wire(inp(inner, 2), wrap)
    pin = component(grid, SUPPORT, "Support", "Pin", ELEMENT, SUPPORTS)
    wire(inp(pin, S_POSITION), out(bank_a))
    roller = component(grid, SUPPORT, "Support", "Roller", ELEMENT, SUPPORTS)
    wire(inp(roller, S_POSITION), out(bank_b))
    wire(inp(roller, S_TX), free)
    sideways = component(grid, SUPPORT, "Support", "Side support", ELEMENT, SUPPORTS)
    wire(inp(sideways, S_POSITION), out(inner), out(top))
    wire(inp(sideways, S_TX), free)
    wire(inp(sideways, S_TZ), free)

    # 5 - The alpaca: 0.6 kN down, at the middle of the deck - the joint under the middle top bar.
    alpaca = slider(grid, "Alpaca", -1, 1, -0.6, 2, PANEL_PARTS, LOAD)
    middle = component(grid, CURVE_MIDDLE, "Curve Middle", "Mid-span", LIFTED, LOAD)
    wire(inp(middle, 0), out(deck))
    down_force = component(grid, UNIT_Z, "Unit Z", "Weight", LIFTED, LOAD)
    wire(inp(down_force, 0), alpaca)
    point_load = component(grid, POINT_LOAD, "Point Load", "The alpaca", ELEMENT, LOAD)
    wire(inp(point_load, 0), out(middle))
    wire(inp(point_load, 1), out(down_force))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOAD)
    wire(inp(pattern, 1), out(point_load))

    # 6 - Assemble: the bars, the three supports and the load pattern into one model.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(truss))
    wire(inp(assemble, 1), out(pin), out(roller), out(sideways))
    wire(inp(assemble, 2), out(pattern))

    # 7 - Run: one static step, the default, and every bar drawn with its axial force N and its
    # value: red pulled, blue pushed. Assemble Model, Run Analysis and Model View in a line, in the
    # row of Assemble Model; Analysis Settings under the line, and Model View's menus hanging below
    # it, out of the rows (placed once the rows are).
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, SUPPORTS)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(grid, MODEL_VIEW, "Model View", "The bridge", VIEW)
    wire(inp(view, 0), out(run))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckExtruded", "Active", True)
    menu_setting(view, "_ckValues", "Active", True)
    show_result(view, BEAM_FORCES, N)

    grid.arrange(0, 0, stages=(PANEL_PARTS, ELEMENT, MODEL, SETTINGS), row_gap=60.0)
    # The bars', the supports' and the alpaca's inputs close up to the component they feed, rather
    # than a column away: each round's snippet stays narrow enough to read.
    close_up([diameter, wall, grade, material, tube], truss, 90.0)
    close_up([free, wrap, inner], pin, 90.0)
    close_up([alpaca, middle, down_force], point_load, 90.0)
    # Wide gaps: Grasshopper draws these components wider than they are measured without Rhino.
    in_line = run.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(run, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    move(view, run.Attributes.Bounds.Right + 80.0, in_line.Y)

    group(doc, "1 - The deck", [span, panels, bank_a, bank_b, deck, divide, shatter], RED)
    group(doc, "2 - The triangles", [depth, ends, middles, lift, top, up, down, chord, top_bars], ORANGE)
    group(doc, "3 - The bars", [diameter, wall, grade, material, tube, truss], PURPLE)
    group(doc, "4 - The supports", [free, wrap, inner, pin, roller, sideways], GREEN)
    group(doc, "5 - The alpaca", [alpaca, middle, down_force, point_load, pattern], BLUE)
    group(doc, "6 - Assemble", [assemble], YELLOW)
    group(doc, "7 - Run", [settings, run, view], GREY)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Valley",
         "The Valley - lesson 5 of the Alpaca4d Playground.\n\n"
         "A Warren truss bridge across a 12 m gorge: six panels of 2 m, 2 m deep, steel tubes "
         "100 x 5 mm as Truss bars, a pin at Bank A, a roller at Bank B, every other joint held "
         "sideways, and the alpaca, 0.6 kN, on the deck in the middle.\n\n"
         "Check it by hand, cutting through the middle panel: the top chord carries "
         "0.3 x 6 / 2 = 0.9 kN, pushed (blue); the bottom chord 0.3 x 5 / 2 = 0.75 kN, pulled "
         "(red); each diagonal 0.3 / (2 / sqrt 5) = 0.335 kN.\n\n"
         "Try: halve Depth and watch the chords' forces double, or set Alpaca to +0.6 and watch "
         "every bar change colour.",
         0, -330, 540, 250)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Valley built. Move Depth or Alpaca and watch the forces in the bars.")


if __name__ == "__main__":
    main()
