#! python3
# The Cliff, the second lesson of the website's Playground: the River's plank built into a cliff,
# sticking out 4 m over the drop, with the alpaca standing at its tip. Built on the Grasshopper
# canvas.
#
# make.sh, in the folder above, writes PlaygroundCliff.gh straight from this script, without Rhino:
#     ./make.sh Playground/PlaygroundCliff.py
#     START=1 ./make.sh Playground/PlaygroundCliff.py PlaygroundCliffStart.gh    # the balcony only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# One group per round of the lesson, in its order: the balcony, the River's C24 plank 300 x 50 mm as
# one element from the Root at the cliff's edge to the Tip, 4 m out along X; the support, built in
# at the Root, holding all six; the alpaca, 0.6 kN down at the Tip; the model assembled; and the
# analysis, drawn deformed true to scale, with the tip's displacement read by Nodal Displacements.
# The Playground's lesson (assets/site/playground/cliff.js in the website) draws its snippets from
# this definition, read by Tools/GhCanvas, and hands this file out at the end.
#
# With START=1 it writes the lesson's start in the docs instead: the balcony, for the rest to be
# added by hand.
#
# The hand check: PL^3 / 3EI = 0.6 x 4^3 / (3 x 10 500 000 x 0.3 x 0.05^3 / 12) = 390 mm, 16 times
# the River's 24.4 mm: the same plank, span and load, held at one end instead of both.
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
END_POINTS = "11BBD48B-BB0A-4F1B-8167-FA297590390D"         # Curve > Analysis > End Points
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
DECONSTRUCT_VECTOR = "A50FCD4A-CF42-4C3F-8616-022761E6CC93" # Vector > Vector > Deconstruct Vector

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
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
NODAL_DISPLACEMENTS = "25085EAB-6C66-487E-B83B-BAB5AB02B1BB"  # Nodal Displacements

# Support's first input: where. Its others, Tx to Rz, hold unless wired: none is, so it holds all six.
S_POSITION = 0
# End Points' outputs: the curve's start, then its end.
E_START, E_END = 0, 1
# Nodal Displacements' outputs: where each node is, then how far it moved.
N_DISPLACEMENT = 1
# Deconstruct Vector's outputs: X, Y and Z.
V_Z = 2
# Model View's results, from 1: Displacement is the first, and Magnitude the first of its components.
DISPLACEMENT, MAGNITUDE = 1, 0
# Model View draws the bend true to scale: 390 mm on 4 m shows as it is.
DEFORMATION_SCALE = 1
# The start of the docs lesson, or the whole balcony.
START = System.Environment.GetEnvironmentVariable("START") == "1"

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per round of the lesson.
RED = Color.FromArgb(50, 254, 0, 0)
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


# ---------------------------------------------------------------- the balcony

# Columns in the order the data flows...
INPUTS, POINTS, CURVES, ELEMENT, MODEL, SETTINGS, RUN, VIEW = range(8)
# ...and rows, one per part of the model.
LINE_, SECTION_, SUPPORT_, LOAD_ = range(4)


def build(doc):
    grid = Grid(doc)

    # 1 - The balcony: the River's plank, from the Root at the cliff's edge to the Tip, 4 m out
    # along X, over the drop. One element: a beam's ends are its nodes, and the alpaca will stand
    # on the Tip's.
    span = slider(grid, "Span", -8, 8, 4, 1, INPUTS, LINE_)
    root = component(grid, CONSTRUCT_POINT, "Construct Point", "Root", POINTS, LINE_)
    tip = component(grid, CONSTRUCT_POINT, "Construct Point", "Tip", POINTS, LINE_)
    wire(inp(tip, 0), span)
    line = component(grid, LINE, "Line", "Balcony line", CURVES, LINE_)
    wire(inp(line, 0), out(root))
    wire(inp(line, 1), out(tip))
    width = slider(grid, "Plank width", 0, 1, 0.3, 3, INPUTS, SECTION_)
    thickness = slider(grid, "Plank thickness", 0, 1, 0.05, 3, INPUTS, SECTION_)
    grade = text(grid, "Grade", "C24", INPUTS, SECTION_, gap=10)
    material = component(grid, MATERIAL, "Material Library Elastic", None, POINTS, SECTION_)
    wire(inp(material, 0), grade)
    section = component(grid, RECTANGLE, "RectangleCS", None, CURVES, SECTION_)
    wire(inp(section, 1), width)
    wire(inp(section, 2), thickness)
    wire(inp(section, 3), out(material))
    beam = component(grid, BEAM, "ForceBeamColumn", "Balcony", ELEMENT, LINE_)
    wire(inp(beam, 0), out(line))
    wire(inp(beam, 1), out(section))

    if START:
        grid.arrange(0, 0, row_gap=60.0)
        group(doc, "1 - The balcony", [span, root, tip, line, width, thickness, grade, material, section, beam], RED)
        note(doc, "The Cliff - start",
             "The Cliff - lesson 2 of the Alpaca4d docs, the start.\n\n"
             "The balcony: the River's C24 plank, 300 x 50 mm lying flat, as one element from the "
             "Root at the cliff's edge to the Tip, 4 m out along X. Add the support, the alpaca, "
             "the model and the analysis as the lesson's steps say.",
             0, -240, 460, 160)
        return

    # 2 - The support: at the Root, with nothing wired into Tx to Rz, so it holds all six - built
    # in, as a cantilever has to be.
    support = component(grid, SUPPORT, "Support", None, ELEMENT, SUPPORT_)
    wire(inp(support, S_POSITION), out(root))

    # 3 - The alpaca: 0.6 kN down at the end of the line, the Tip.
    alpaca = slider(grid, "Alpaca", -1, 1, -0.6, 2, INPUTS, LOAD_)
    ends = component(grid, END_POINTS, "End Points", "Ends", POINTS, LOAD_)
    wire(inp(ends, 0), out(line))
    down = component(grid, UNIT_Z, "Unit Z", "Weight", POINTS, LOAD_)
    wire(inp(down, 0), alpaca)
    point_load = component(grid, POINT_LOAD, "Point Load", "The alpaca", CURVES, LOAD_)
    wire(inp(point_load, 0), out(ends, E_END))
    wire(inp(point_load, 1), out(down))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, ELEMENT, LOAD_)
    wire(inp(pattern, 1), out(point_load))

    # 4 - Assemble: the element, the support and the load pattern into one model, in the support's
    # row.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, SUPPORT_)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(support))
    wire(inp(assemble, 2), out(pattern))

    # 5 - Run: one static step, the balcony drawn bent true to scale, and the tip's displacement
    # read as a number: Nodal Displacements lists the nodes in the order Assemble numbered them,
    # the Root then the Tip, and Deconstruct Vector takes their Z.
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, SUPPORT_)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(grid, MODEL_VIEW, "Model View", "The balcony", VIEW)
    wire(inp(view, 0), out(run))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_slDeformScale", "Value", DEFORMATION_SCALE)
    menu_setting(view, "_ckExtruded", "Active", True)
    show_result(view, DISPLACEMENT, MAGNITUDE)
    nodal = component(grid, NODAL_DISPLACEMENTS, "Nodal Displacements", None, VIEW)
    wire(inp(nodal, 0), out(run))
    split = component(grid, DECONSTRUCT_VECTOR, "Deconstruct Vector", None, VIEW)
    wire(inp(split, 0), out(nodal, N_DISPLACEMENT))
    reading = text(grid, "Z in m", "", VIEW)
    reading.Attributes.Bounds = RectangleF(0.0, 0.0, 90.0, 40.0)
    wire(reading, out(split, V_Z))

    grid.arrange(0, 0, stages=(MODEL, SETTINGS), row_gap=60.0)
    # Assemble Model, Run Analysis and Model View in a line; Analysis Settings under it, and the
    # reading of the tip above Model View, from Run Analysis to the panel. Wide gaps: Grasshopper
    # draws these components wider than they are measured without Rhino.
    in_line = run.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(run, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    move(view, run.Attributes.Bounds.Right + 80.0, in_line.Y)
    above = in_line.Y - 40.0 - nodal.Attributes.Bounds.Height
    move(nodal, run.Attributes.Bounds.Right + 80.0, above)
    move(split, nodal.Attributes.Bounds.Right + 70.0, above + 20.0)
    move(reading, split.Attributes.Bounds.Right + 50.0, above + 20.0 + split.Attributes.Bounds.Height - 30.0)

    group(doc, "1 - The balcony", [span, root, tip, line, width, thickness, grade, material, section, beam], RED)
    group(doc, "2 - The support", [support], GREEN)
    group(doc, "3 - The alpaca", [alpaca, ends, down, point_load, pattern], BLUE)
    group(doc, "4 - Assemble", [assemble], YELLOW)
    group(doc, "5 - Run", [settings, run, view, nodal, split, reading], GREY)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "The Cliff",
         "The Cliff - lesson 2 of the Alpaca4d Playground.\n\n"
         "The River's plank, C24 300 x 50 mm, built into the cliff and sticking out 4 m over the "
         "drop, with the alpaca, 0.6 kN, standing at its tip.\n\n"
         "Check it by hand: PL^3 / 3EI = 0.6 x 4^3 / (3 x 10 500 000 x 0.3 x 0.05^3 / 12) = 390 mm, "
         "16 times the River's 24.4 mm. The panel Z in m reads it; Model View draws it true "
         "to scale.\n\n"
         "Try: wire a toggle set to False into the Support's Ry, and see Run Analysis fail; or move "
         "the load to End Points' S.",
         0, -300, 520, 220)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Cliff built. Move Alpaca or Plank thickness and watch the tip go down.")


if __name__ == "__main__":
    main()
