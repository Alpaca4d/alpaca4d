#! python3
# The Market, the sixth lesson of the website's Playground: a market stall's shelf, a timber plank on
# two trestles with its end sticking out, a crate of apples on it and the alpaca standing on the
# end. Predict what the trestles carry and how the shelf bends, then read it from the model.
#
# make.sh, next to it, writes PlaygroundMarket.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundMarket.py
#     MARKET_START=1 ./make.sh PlaygroundMarket.py PlaygroundMarketStart.gh    # the stall only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the stall, solved - the River's C24 plank,
# 300 x 50 mm, 4 m long on a pin at its start and a roller 3 m along, a 1 kN crate at 1.5 m and the
# alpaca's 0.6 kN at the end; a panel with the prediction of the reactions; Reaction Forces, read
# into a panel; Beam Forces, its Mz into a panel; and Model View drawing the bending moments, with a
# panel saying the largest. The Playground's lesson (assets/site/playground/market.js in the
# website) draws its snippets from this definition, read by Tools/GhCanvas, and hands this file out
# at the end.
#
# With MARKET_START=1 it writes the lesson's start in the docs instead: the stall alone, solved, for
# the results to be read by hand.
#
# The hand check: moments about trestle A, R_B x 3 = 1 x 1.5 + 0.6 x 4, so R_B = 1.3 kN and
# R_A = 1.6 - 1.3 = 0.3 kN. Under the crate M = 0.3 x 1.5 = 0.45 kNm, sagging; over trestle B
# M = -0.6 x 1 = -0.6 kNm, hogging - the largest, though the crate is the heavier load.
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
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
RECTANGLE = "888184F3-A448-4F82-9DCA-E57EFAC8E840"          # RectangleCS
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
REACTION_FORCES = "C86DB16F-84D1-4EF6-80CD-860B0F0B5A7D"    # Reaction Forces
BEAM_FORCES = "F25101E7-06B9-41C0-8BF1-F55ED08A4630"        # Beam Forces
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_TX, S_RY, S_RZ = 0, 1, 5, 6
# Divide Curve's outputs: the points, the tangents and the parameters.
D_PARAMETERS = 2
# Reaction Forces' outputs: SupportPosition, ReactionForce, ReactionMoment, NodeTag.
R_FORCE = 1
# Beam Forces' outputs: N, Vy, Vz, Mx, My, Mz, Element.
B_MZ = 5
# Model View's results, from 1: Beam forces is the second, and Mz the last of its six components.
BEAM_FORCE, MZ = 2, 5
# The start of the docs lesson, or the whole lesson.
START = System.Environment.GetEnvironmentVariable("MARKET_START") == "1" or \
    System.Environment.GetEnvironmentVariable("START") == "1"

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per round of the lesson.
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


def typed(grid, name, value, width, height, col, row=None, gap=0):
    """A panel holding text typed into it, such as a prediction worked out by hand."""
    p = text(grid, name, value, col, row, gap)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    return p


def fed(grid, name, source, width, height, col, row=None, gap=0):
    """A panel showing what a wire brings into it."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    p.AddSource(source)
    return p


# ---------------------------------------------------------------- the stall

# Columns in the order the data flows...
(INPUTS, POINTS, CURVES, AMOUNTS, DIVIDED, SHATTERED, ELEMENT, PATTERN, MODEL, SETTINGS, RUN,
 READ, SHOW, NOTE) = range(14)
# ...and rows, one per part of the model.
LINE_, SECTION_, SUPPORTS, CRATE, ALPACA = range(5)


def build(doc):
    grid = Grid(doc)

    # 1 - The stall: the shelf, 4 m along X from the start, cut into 8 elements of 0.5 m so that
    # there is a node under the crate, on each trestle and at the end; the River's C24 plank.
    length = slider(grid, "Length", 1, 8, 4, 1, INPUTS, LINE_)
    start = component(grid, CONSTRUCT_POINT, "Construct Point", "Start", POINTS, LINE_)
    end = component(grid, CONSTRUCT_POINT, "Construct Point", "End", POINTS, LINE_)
    wire(inp(end, 0), length)
    line = component(grid, LINE, "Line", "Shelf line", CURVES, LINE_)
    wire(inp(line, 0), out(start))
    wire(inp(line, 1), out(end))
    count = slider(grid, "Elements", 1, 16, 8, 0, AMOUNTS, LINE_)
    divide = component(grid, DIVIDE, "Divide Curve", None, DIVIDED, LINE_)
    wire(inp(divide, 0), out(line))
    wire(inp(divide, 1), count)
    shatter = component(grid, SHATTER, "Shatter", None, SHATTERED, LINE_)
    wire(inp(shatter, 0), out(line))
    wire(inp(shatter, 1), out(divide, D_PARAMETERS))

    width = slider(grid, "Plank width", 0, 1, 0.3, 3, INPUTS, SECTION_)
    thickness = slider(grid, "Plank thickness", 0, 1, 0.05, 3, INPUTS, SECTION_)
    grade = text(grid, "Grade", "C24", INPUTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, POINTS, SECTION_)
    wire(inp(material, 0), grade)
    section = component(grid, RECTANGLE, "RectangleCS", None, CURVES, SECTION_)
    wire(inp(section, 1), width)
    wire(inp(section, 2), thickness)
    wire(inp(section, 3), out(material))
    beam = component(grid, BEAM, "ForceBeamColumn", "Shelf", ELEMENT, LINE_)
    wire(inp(beam, 0), out(shatter))
    wire(inp(beam, 1), out(section))

    # The trestles: a pin at the start and a roller 3 m along, so that the last metre sticks out.
    # Both are free to turn about Y and Z, the roller to slide along X too; both hold Rx.
    trestle_at = slider(grid, "Trestle B at", 0, 4, 3, 1, INPUTS, SUPPORTS)
    trestle_b = component(grid, CONSTRUCT_POINT, "Construct Point", "Trestle B", POINTS, SUPPORTS)
    wire(inp(trestle_b, 0), trestle_at)
    free = toggle(grid, "Free", False, AMOUNTS, SUPPORTS)
    pin = component(grid, SUPPORT, "Support", "Pin", ELEMENT, SUPPORTS)
    wire(inp(pin, S_POSITION), out(start))
    wire(inp(pin, S_RY), free)
    wire(inp(pin, S_RZ), free)
    roller = component(grid, SUPPORT, "Support", "Roller", ELEMENT, SUPPORTS)
    wire(inp(roller, S_POSITION), out(trestle_b))
    wire(inp(roller, S_TX), free)
    wire(inp(roller, S_RY), free)
    wire(inp(roller, S_RZ), free)

    # The crate of apples, 1 kN, half way between the trestles; the alpaca, 0.6 kN, at the end.
    crate_at = slider(grid, "Crate at", 0, 4, 1.5, 1, INPUTS, CRATE)
    crate_point = component(grid, CONSTRUCT_POINT, "Construct Point", "Crate point", POINTS, CRATE)
    wire(inp(crate_point, 0), crate_at)
    crate = slider(grid, "Crate", -2, 2, -1, 2, AMOUNTS, CRATE)
    crate_down = component(grid, UNIT_Z, "Unit Z", "Crate weight", DIVIDED, CRATE)
    wire(inp(crate_down, 0), crate)
    crate_load = component(grid, POINT_LOAD, "Point Load", "The crate", ELEMENT, CRATE)
    wire(inp(crate_load, 0), out(crate_point))
    wire(inp(crate_load, 1), out(crate_down))
    alpaca = slider(grid, "Alpaca", -1, 1, -0.6, 2, AMOUNTS, ALPACA)
    alpaca_down = component(grid, UNIT_Z, "Unit Z", "Alpaca weight", DIVIDED, ALPACA)
    wire(inp(alpaca_down, 0), alpaca)
    alpaca_load = component(grid, POINT_LOAD, "Point Load", "The alpaca", ELEMENT, ALPACA)
    wire(inp(alpaca_load, 0), out(end))
    wire(inp(alpaca_load, 1), out(alpaca_down))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, ALPACA)
    wire(inp(pattern, 1), out(crate_load), out(alpaca_load))

    # Assembled and solved: one static step, the default.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(pin), out(roller))
    wire(inp(assemble, 2), out(pattern))
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, SUPPORTS)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))

    stall = [length, start, end, line, count, divide, shatter, width, thickness, grade, material,
             section, beam, trestle_at, trestle_b, free, pin, roller, crate_at, crate_point, crate,
             crate_down, crate_load, alpaca, alpaca_down, alpaca_load, pattern, assemble, settings, run]

    if START:
        arrange(grid, settings, run)
        group(doc, "1 - The stall", stall, RED)
        note(doc, "The Market - start",
             "The Market - lesson 6 of the Alpaca4d docs, the start.\n\n"
             "A C24 plank, 300 x 50 mm and 4 m long, on a pin at its start and a roller 3 m along; "
             "a 1 kN crate at 1.5 m and the alpaca's 0.6 kN at the end. Solved. Work out the "
             "reactions and the bending moments by hand, then read them as the lesson's steps say.",
             0, -240, 480, 160)
        return

    # 2 - Predict: the reactions worked out by hand, before reading them, in a panel beside them.
    prediction = typed(grid, "Prediction", "Trestle A: 0.3 kN up\nTrestle B: 1.3 kN up", 150, 44, NOTE, SUPPORTS)

    # 3 - The reactions: what each trestle carries, read from the solved model.
    reactions = component(grid, REACTION_FORCES, "Reaction Forces", None, READ, SUPPORTS)
    wire(inp(reactions, 0), out(run))
    shown = fed(grid, "Reactions", out(reactions, R_FORCE), 150, 60, SHOW, SUPPORTS)

    # 4 - The bending: Mz, the moment about local z, which bends a horizontal beam in its upright
    # plane, sagging positive.
    forces = component(grid, BEAM_FORCES, "Beam Forces", None, READ, CRATE)
    wire(inp(forces, 0), out(run))
    bending = fed(grid, "Bending", out(forces, B_MZ), 150, 100, SHOW, CRATE)

    # 5 - The largest moment: Model View draws Mz along the shelf, on the side it stretches, and a
    # panel says which is the largest.
    view = component(grid, MODEL_VIEW, "Model View", "The moments", READ)
    wire(inp(view, 0), out(run))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckExtruded", "Active", True)
    show_result(view, BEAM_FORCE, MZ)
    largest = typed(grid, "Largest moment", "0.6 kNm, hogging,\nover trestle B", 150, 44, SHOW)

    # The results to the right of Run Analysis: Model View on top, its menus hanging down to the
    # reactions; the prediction beside the reactions it predicts; the bending under them.
    arrange(grid, settings, run, [prediction, reactions, shown, forces, bending, view, largest])

    group(doc, "1 - The stall", stall, RED)
    group(doc, "2 - Predict", [prediction], ORANGE)
    group(doc, "3 - The reactions", [reactions, shown], GREEN)
    group(doc, "4 - The bending", [forces, bending], PURPLE)
    group(doc, "5 - The largest moment", [view, largest], BLUE)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Market",
         "The Market - lesson 6 of the Alpaca4d Playground.\n\n"
         "A C24 plank, 300 x 50 mm and 4 m long, on two trestles 3 m apart: a 1 kN crate half way "
         "between them, and the alpaca, 0.6 kN, on the metre that sticks out.\n\n"
         "Check it by hand: moments about trestle A, R_B x 3 = 1 x 1.5 + 0.6 x 4, so R_B = 1.3 kN "
         "and R_A = 0.3 kN. Under the crate M = 0.3 x 1.5 = 0.45 kNm, sagging; over trestle B "
         "M = 0.6 x 1 = 0.6 kNm, hogging - the largest.\n\n"
         "Try: move Trestle B at to 2 and watch Trestle A pull down; or make the plank twice as "
         "thick, and see that the reactions and moments stay the same.",
         0, -320, 540, 240)


def arrange(grid, settings, run, later=()):
    grid.arrange(0, 0, stages=(AMOUNTS, MODEL, SETTINGS, READ), row_gap=60.0)
    # Wide gaps: Grasshopper draws these components wider than they are measured without Rhino.
    in_line = run.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(run, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    # What comes after Run Analysis moves along with it.
    if later:
        dx = run.Attributes.Bounds.Right + 120.0 - min(o.Attributes.Bounds.X for o in later)
        for o in later:
            move(o, o.Attributes.Bounds.X + dx, o.Attributes.Bounds.Y)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Market built. Move Trestle B at, and watch the reactions change.")


if __name__ == "__main__":
    main()
