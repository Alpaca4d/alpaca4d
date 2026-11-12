#! python3
# The Watchtower, lesson 7 of the website's Playground: one leg of a steel watchtower, a tube 6 m
# tall carrying its share of the platform, and the load at which it buckles. Built on the
# Grasshopper canvas.
#
# make.sh, in the folder above, writes PlaygroundTower.gh straight from this script, without Rhino:
#     ./make.sh Playground/PlaygroundTower.py
#     START=1 ./make.sh Playground/PlaygroundTower.py PlaygroundTowerStart.gh    # the leg's line and its tube only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the leg's line, 6 m up Z from its foot; its
# section, a steel tube 60.3 mm across with 3.2 mm walls, S235; the beam, the line shattered in two
# so that there is a node at mid-height for the bracing to hold; the supports, a pin at the base,
# the bracing at mid-height and the platform at the top holding the leg sideways; the platform's
# 8 kN down on its head; the model assembled; and the Linear Buckling Analysis, every beam split in 8
# for it, the load factor in a panel and the leg drawn in its buckled shape. The Playground's lesson
# (assets/site/playground/tower.js in the website) draws its snippets from this definition, read by
# Tools/GhCanvas, and hands this file out at the end.
#
# With START=1 (or TOWER_START=1) it writes the lesson's start in the docs instead: the line and the
# section, for the rest to be added by hand.
#
# The hand check: braced at mid-height, each half of the leg is pinned at both ends, 3 m long:
# P_cr = pi^2 EI / L^2 = pi^2 x 210 000 000 x 2.347e-7 / 3^2 = 54.0 kN, 6.76 times the 8 kN on it.
# Linear Buckling gives 6.83: PDelta pieces come at it from above, 1.2 % high with 8 to a half wave.
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
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
CIRCLE = "AFCC30B2-7CB9-453F-B5A1-C73554BD3DC7"             # CircleCS
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
LINEAR_BUCKLING = "6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815"    # Linear Buckling Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless wired.
S_POSITION, S_TX, S_TY, S_TZ, S_RX, S_RY, S_RZ = 0, 1, 2, 3, 4, 5, 6
# Divide Curve's outputs: the points, the tangents and the parameters.
D_PARAMETERS = 2
# Linear Buckling's inputs and outputs.
LB_MODEL, LB_MODES, LB_DIVISIONS = 0, 1, 2
LB_FACTORS = 1
# Model View's results, from 1: Displacement is the first, and Magnitude the first of its components.
DISPLACEMENT, MAGNITUDE = 1, 0
# The start of the docs lesson, or the whole leg.
START = "1" in (System.Environment.GetEnvironmentVariable("START"), System.Environment.GetEnvironmentVariable("TOWER_START"))

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


# ---------------------------------------------------------------- the leg

# Columns in the order the data flows...
INPUTS, POINTS, CURVES, AMOUNTS, DIVIDED, SHATTERED, ELEMENT, PATTERN, MODEL, SETTINGS, RUN, VIEW = range(12)
# ...and rows, one per part of the model.
LINE_, SECTION_, SUPPORTS, LOAD = range(4)


def build(doc):
    grid = Grid(doc)

    # 1 - The leg: from its foot, at the origin, to its head, 6 m up Z.
    height = slider(grid, "Height", 1, 10, 6, 1, INPUTS, LINE_)
    foot = component(grid, CONSTRUCT_POINT, "Construct Point", "Foot", POINTS, LINE_)
    head = component(grid, CONSTRUCT_POINT, "Construct Point", "Head", POINTS, LINE_)
    wire(inp(head, 2), height)
    line = component(grid, LINE, "Line", "Leg line", CURVES, LINE_)
    wire(inp(line, 0), out(foot))
    wire(inp(line, 1), out(head))

    # 2 - The tube: a circular hollow section 60.3 mm across with 3.2 mm walls, in metres, S235.
    diameter = slider(grid, "Outside diameter", 0, 0.2, 0.0603, 4, INPUTS, SECTION_)
    wall = slider(grid, "Wall", 0, 0.02, 0.0032, 4, INPUTS, SECTION_)
    grade = text(grid, "Grade", "S235", INPUTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, POINTS, SECTION_)
    wire(inp(material, 0), grade)
    section = component(grid, CIRCLE, "CircleCS", None, CURVES, SECTION_)
    wire(inp(section, 1), diameter)
    wire(inp(section, 2), wall)
    wire(inp(section, 3), out(material))

    if START:
        grid.arrange(0, 0)
        group(doc, "1 - The leg", [height, foot, head, line], RED)
        group(doc, "2 - The tube", [diameter, wall, grade, material, section], ORANGE)
        note(doc, "The Watchtower - start",
             "The Watchtower - lesson 7 of the Alpaca4d docs, the start.\n\n"
             "One leg's line, 6 m up Z from its foot, and its section, a steel tube 60.3 mm across "
             "with 3.2 mm walls. Add the beam, the supports, the platform's load and the buckling "
             "analysis as the lesson's steps say.",
             0, -240, 460, 160)
        return

    # 3 - The beam: the line shattered in two, so that mid-height is a node for the bracing to
    # hold. Divide Curve's parameters are on the line's own domain, which Shatter reads as they are.
    count = slider(grid, "Elements", 1, 10, 2, 0, AMOUNTS, LINE_)
    divide = component(grid, DIVIDE, "Divide Curve", None, DIVIDED, LINE_)
    wire(inp(divide, 0), out(line))
    wire(inp(divide, 1), count)
    shatter = component(grid, SHATTER, "Shatter", None, SHATTERED, LINE_)
    wire(inp(shatter, 0), out(line))
    wire(inp(shatter, 1), out(divide, D_PARAMETERS))
    beam = component(grid, BEAM, "ForceBeamColumn", "Leg", ELEMENT, LINE_)
    wire(inp(beam, 0), out(shatter))
    wire(inp(beam, 1), out(section))

    # 4 - The supports, top to bottom as they stand: the platform holds the top sideways, the
    # bracing holds mid-height sideways, and the base is a pin. Each is free to turn about X and Y;
    # the base alone holds Rz, or nothing would stop the leg spinning about its own axis.
    free = toggle(grid, "Free", False, AMOUNTS, SUPPORTS)
    middle = component(grid, CURVE_MIDDLE, "Curve Middle", "Mid-height", DIVIDED, SUPPORTS)
    wire(inp(middle, 0), out(line))
    top_support = component(grid, SUPPORT, "Support", "Top", ELEMENT, SUPPORTS)
    wire(inp(top_support, S_POSITION), out(head))
    for i in (S_TZ, S_RX, S_RY, S_RZ):
        wire(inp(top_support, i), free)
    brace = component(grid, SUPPORT, "Support", "Brace", ELEMENT, SUPPORTS)
    wire(inp(brace, S_POSITION), out(middle))
    for i in (S_TZ, S_RX, S_RY, S_RZ):
        wire(inp(brace, i), free)
    base_support = component(grid, SUPPORT, "Support", "Base", ELEMENT, SUPPORTS)
    wire(inp(base_support, S_POSITION), out(foot))
    for i in (S_RX, S_RY):
        wire(inp(base_support, i), free)

    # 5 - The platform: its share on this leg, 8 kN down on its head.
    platform = slider(grid, "Platform", -10, 10, -8, 1, AMOUNTS, LOAD)
    down = component(grid, UNIT_Z, "Unit Z", "Weight", DIVIDED, LOAD)
    wire(inp(down, 0), platform)
    point_load = component(grid, POINT_LOAD, "Point Load", "The platform", ELEMENT, LOAD)
    wire(inp(point_load, 0), out(head))
    wire(inp(point_load, 1), out(down))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOAD)
    wire(inp(pattern, 1), out(point_load))

    # 6 - Assemble: the elements, the three supports and the load pattern into one model.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORTS)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(top_support), out(brace), out(base_support))
    wire(inp(assemble, 2), out(pattern))

    # 7 - Buckle: the lowest load factor, every beam split in 8 for it, in a panel, and the leg
    # drawn in the shape it buckles into. Assemble Model, Linear Buckling and Model View in a line;
    # Divisions under it on the left, the load factor under it on the right (placed once the rows
    # are).
    divisions = slider(grid, "Divisions", 1, 16, 8, 0, SETTINGS)
    buckling = component(grid, LINEAR_BUCKLING, "Linear Buckling Analysis", None, RUN, SUPPORTS)
    wire(inp(buckling, LB_MODEL), out(assemble))
    wire(inp(buckling, LB_DIVISIONS), divisions)
    factor = panel(grid, "Load factor", out(buckling, LB_FACTORS), 90, 24, RUN)
    view = component(grid, MODEL_VIEW, "Model View", "The leg", VIEW)
    wire(inp(view, 0), out(buckling))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_ckExtruded", "Active", True)
    show_result(view, DISPLACEMENT, MAGNITUDE)

    grid.arrange(0, 0, stages=(AMOUNTS, MODEL, SETTINGS), row_gap=60.0)
    # Wide gaps: Grasshopper draws these components wider than they are measured without Rhino.
    in_line = buckling.Attributes.Bounds
    move(divisions, divisions.Attributes.Bounds.X, in_line.Bottom + 40.0)
    move(buckling, divisions.Attributes.Bounds.Right + 100.0, in_line.Y)
    in_line = buckling.Attributes.Bounds
    move(factor, in_line.Right + 70.0, in_line.Bottom + 40.0)
    move(view, factor.Attributes.Bounds.Right + 90.0, in_line.Y)

    group(doc, "1 - The leg", [height, foot, head, line], RED)
    group(doc, "2 - The tube", [diameter, wall, grade, material, section], ORANGE)
    group(doc, "3 - The beam", [count, divide, shatter, beam], PURPLE)
    group(doc, "4 - The supports", [free, middle, top_support, brace, base_support], GREEN)
    group(doc, "5 - The platform", [platform, down, point_load, pattern], BLUE)
    group(doc, "6 - Assemble", [assemble], YELLOW)
    group(doc, "7 - Buckle", [divisions, buckling, factor, view], GREY)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Watchtower",
         "The Watchtower - lesson 7 of the Alpaca4d Playground.\n\n"
         "One leg of a steel watchtower: a tube 60.3 x 3.2 mm, S235, 6 m tall, pinned at the base, "
         "held sideways by the bracing at mid-height and by the platform at the top, which puts "
         "8 kN on it.\n\n"
         "Check it by hand: each half is 3 m, pinned at both ends. P_cr = pi^2 EI / L^2 = "
         "pi^2 x 210 000 000 x 2.347e-7 / 3^2 = 54.0 kN, 6.76 times 8 kN. Linear Buckling gives "
         "6.83: with 8 pieces to a half wave it is 1.2 % high.\n\n"
         "Try: let the bracing go (wire Free into the Brace's Tx and Ty) and the factor drops to a "
         "quarter, or set Divisions to 1 and nothing buckles at all.",
         0, -300, 560, 240)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Watchtower built. Move Platform or Divisions and watch the load factor.")


if __name__ == "__main__":
    main()
