#! python3
# The Snowy roof, the third lesson of the website's Playground: a timber rafter of a flat roof under
# snow, and how deep it must be. Built on the Grasshopper canvas.
#
# make.sh, next to it, writes PlaygroundRoof.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundRoof.py
#     START=1 ./make.sh PlaygroundRoof.py PlaygroundRoofStart.gh    # the rafter and its section only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# The rafter comes as the River left off: a C24 rafter 4 m across, from Wall A to Wall B, cut into
# four elements, a pin at one end and a roller at the other. Then one group per round of the
# lesson, in its order: the snow, 1.5 kN/m2 on the roof, which a rafter 0.6 m from the next one
# carries as 0.9 kN per metre of its length; its own weight, which Gravity Load works out from the
# section and the timber's density; the two in one Load Pattern; the section, 75 mm wide and 150 mm
# deep, the shallowest that keeps the sag under L/250; and the analysis, drawn deformed. The
# Playground's lesson (assets/site/playground/roof.js in the website) draws its snippets from this
# definition, read by Tools/GhCanvas, and hands this file out at the end.
#
# With START=1 it writes the lesson's start in the docs instead: the rafter, its supports and its
# section, 100 mm deep, for the rest to be added by hand.
#
# The hand check: w = 0.9 + 0.075 x 0.15 x 410 x 9.81 / 1000 = 0.945 kN/m, and
# 5wL^4 / 384EI = 5 x 0.945 x 4^4 / (384 x 10 500 000 x 0.075 x 0.15^3 / 12) = 14.2 mm, under
# L/250 = 16 mm.
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
MULTIPLICATION = "CE46B74E-00C9-43C4-805A-193B69EA4A11"     # Maths > Operators > Multiplication

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
RECTANGLE = "888184F3-A448-4F82-9DCA-E57EFAC8E840"          # RectangleCS
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
LINEAR_LOAD = "8593AC69-D975-4566-97FC-27D7D4A4D415"        # Linear Load
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_TX, S_RY, S_RZ = 0, 1, 5, 6
# Divide Curve's outputs: the points, the tangents and the parameters.
D_PARAMETERS = 2
# Linear Load's inputs: the beams it goes on (left empty, every beam), and its force per metre.
LL_TARGET, LL_FORCE = 0, 1
# Load Pattern's inputs: its time series, its loads and a factor on them all.
LP_LOADS = 1
# Model View's results, from 1: Displacement is the first, and Magnitude the first of its components.
DISPLACEMENT, MAGNITUDE = 1, 0
# How many times its true size Model View draws the deformation: the Playground draws it so too.
DEFORMATION_SCALE = 20
# The start of the docs lesson, or the whole roof.
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




# ---------------------------------------------------------------- the roof

# Columns in the order the data flows...
INPUTS, POINTS, CURVES, AMOUNTS, DIVIDED, SHATTERED, ELEMENT, PATTERN, MODEL, SETTINGS, RUN, VIEW = range(12)
# ...and rows, one per part of the model.
SECTION_, RAFTER_, SUPPORTS, SNOW, WEIGHT = range(5)


def build(doc):
    grid = Grid(doc)

    # The rafter, as the River left off: 4 m from Wall A to Wall B along X, cut into four elements
    # so that there are nodes along it and one at mid-span, a pin at Wall A and a roller at Wall B.
    span = slider(grid, "Span", 1, 8, 4, 1, INPUTS, RAFTER_)
    wall_a = component(grid, CONSTRUCT_POINT, "Construct Point", "Wall A", POINTS, RAFTER_)
    wall_b = component(grid, CONSTRUCT_POINT, "Construct Point", "Wall B", POINTS, RAFTER_)
    wire(inp(wall_b, 0), span)
    line = component(grid, LINE, "Line", "Rafter line", CURVES, RAFTER_)
    wire(inp(line, 0), out(wall_a))
    wire(inp(line, 1), out(wall_b))
    count = slider(grid, "Elements", 1, 10, 4, 0, AMOUNTS, RAFTER_)
    divide = component(grid, DIVIDE, "Divide Curve", None, DIVIDED, RAFTER_)
    wire(inp(divide, 0), out(line))
    wire(inp(divide, 1), count)
    shatter = component(grid, SHATTER, "Shatter", None, SHATTERED, RAFTER_)
    wire(inp(shatter, 0), out(line))
    wire(inp(shatter, 1), out(divide, D_PARAMETERS))
    rafter = component(grid, BEAM, "ForceBeamColumn", "Rafter", ELEMENT, RAFTER_)
    wire(inp(rafter, 0), out(shatter))

    free = toggle(grid, "Free", False, AMOUNTS, SUPPORTS)
    pin = component(grid, SUPPORT, "Support", "Pin", ELEMENT, SUPPORTS)
    wire(inp(pin, S_POSITION), out(wall_a))
    wire(inp(pin, S_RY), free)
    wire(inp(pin, S_RZ), free)
    roller = component(grid, SUPPORT, "Support", "Roller", ELEMENT, SUPPORTS)
    wire(inp(roller, S_POSITION), out(wall_b))
    wire(inp(roller, S_TX), free)
    wire(inp(roller, S_RY), free)
    wire(inp(roller, S_RZ), free)

    # 4 - How deep: C24, 75 mm wide and 150 mm deep, in metres. On a horizontal beam a rectangle's
    # Height points up, so Depth goes into Height and the rafter stands on its edge. The start has
    # it 100 mm deep, which sags 47 mm, three times L/250.
    width = slider(grid, "Width", 0, 0.3, 0.075, 3, INPUTS, SECTION_)
    depth = slider(grid, "Depth", 0, 0.3, 0.1 if START else 0.15, 3, INPUTS, SECTION_)
    grade = text(grid, "Grade", "C24", INPUTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, POINTS, SECTION_)
    wire(inp(material, 0), grade)
    section = component(grid, RECTANGLE, "RectangleCS", None, CURVES, SECTION_)
    wire(inp(section, 1), width)
    wire(inp(section, 2), depth)
    wire(inp(section, 3), out(material))
    wire(inp(rafter, 1), out(section))

    rafter_parts = [span, wall_a, wall_b, line, count, divide, shatter, rafter, free, pin, roller]
    section_parts = [width, depth, grade, material, section]
    if START:
        grid.arrange(0, 0, row_gap=60.0)
        group(doc, "The rafter", rafter_parts, RED)
        group(doc, "The section", section_parts, ORANGE)
        note(doc, "The Snowy roof - start",
             "The Snowy roof - lesson 3 of the Alpaca4d docs, the start.\n\n"
             "A C24 rafter of a flat roof, 75 x 100 mm, 4 m from Wall A to Wall B on a pin and a "
             "roller. Add the snow, its own weight and the analysis as the lesson's steps say, then "
             "find how deep it must be.",
             0, -240, 460, 160)
        return

    # 1 - The snow: 1.5 kN on every square metre of roof, down. The rafters are 0.6 m apart, so
    # each carries a strip of roof 0.6 m wide: 1.5 x 0.6 = 0.9 kN on every metre of its length.
    # Linear Load's Beam|Id left empty puts it on every beam: the rafter's four elements.
    snow = slider(grid, "Snow", -3, 0, -1.5, 2, AMOUNTS, SNOW)
    spacing = slider(grid, "Spacing", 0, 1.2, 0.6, 2, AMOUNTS, SNOW)
    per_metre = component(grid, MULTIPLICATION, "Multiplication", "Per metre", DIVIDED, SNOW)
    wire(inp(per_metre, 0), snow)
    wire(inp(per_metre, 1), spacing)
    down = component(grid, UNIT_Z, "Unit Z", "Down", SHATTERED, SNOW)
    wire(inp(down, 0), out(per_metre))
    snow_load = component(grid, LINEAR_LOAD, "Linear Load", "Snow load", ELEMENT, SNOW)
    wire(inp(snow_load, LL_FORCE), out(down))

    # 2 - Its own weight: Gravity Load works it out from each beam's section and material, at
    # 410 kg/m3 for C24, and pulls it down. Its Factor scales it: 1 is its true weight.
    factor = slider(grid, "Weight factor", -2, 10, 1, 2, SHATTERED, WEIGHT)
    gravity = component(grid, GRAVITY, "Gravity Load", None, ELEMENT, WEIGHT)
    wire(inp(gravity, 0), factor)

    # 3 - The load pattern: the snow and the rafter's weight, acting together.
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, SNOW)
    wire(inp(pattern, LP_LOADS), out(snow_load), out(gravity))

    # 5 - Run: assembled, one static step, the default, and the rafter drawn bent, twenty times its
    # true bend, coloured by how far it moves. Assemble Model, Run Analysis and Model View in a
    # line; Analysis Settings under it (placed once the rows are).
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(rafter))
    wire(inp(assemble, 1), out(pin), out(roller))
    wire(inp(assemble, 2), out(pattern))
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, SUPPORTS)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(grid, MODEL_VIEW, "Model View", "The roof", VIEW)
    wire(inp(view, 0), out(run))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_slDeformScale", "Value", DEFORMATION_SCALE)
    menu_setting(view, "_ckExtruded", "Active", True)
    show_result(view, DISPLACEMENT, MAGNITUDE)

    grid.arrange(0, 0, stages=(AMOUNTS, MODEL, SETTINGS), row_gap=60.0)
    # Wide gaps: Grasshopper draws these components wider than they are measured without Rhino.
    in_line = run.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(run, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    move(view, run.Attributes.Bounds.Right + 80.0, in_line.Y)

    group(doc, "The rafter", rafter_parts, RED)
    group(doc, "1 - The snow", [snow, spacing, per_metre, down, snow_load], BLUE)
    group(doc, "2 - Its own weight", [factor, gravity], PURPLE)
    group(doc, "3 - The load pattern", [pattern], GREEN)
    group(doc, "4 - How deep", section_parts, ORANGE)
    group(doc, "5 - Run", [assemble, settings, run, view], GREY)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Snowy roof",
         "The Snowy roof - lesson 3 of the Alpaca4d Playground.\n\n"
         "A C24 rafter of a flat roof, 75 mm wide, 4 m from wall to wall on a pin and a roller, "
         "under 1.5 kN/m2 of snow. The rafters are 0.6 m apart, so each carries 0.9 kN/m, and its "
         "own weight.\n\n"
         "Check it by hand: 5wL^4 / 384EI = 5 x 0.945 x 4^4 / (384 x 10 500 000 x 0.075 x 0.15^3 / 12) "
         "= 14.2 mm, under L/250 = 16 mm. Model View draws it twenty times bigger.\n\n"
         "Try: Depth 0.1 sags 47 mm; 0.2, twice as deep, sags 6 mm - eight times as stiff.",
         0, -300, 560, 230)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Snowy roof built. Move Depth and watch the rafter sag.")


if __name__ == "__main__":
    main()
