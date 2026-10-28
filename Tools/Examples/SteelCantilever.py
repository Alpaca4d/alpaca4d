#! python3
# A steel cantilever checked to EN 1993-1-1: the Utilisation example of the docs, built on the
# Grasshopper canvas.
#
# make.sh, next to it, writes SteelCantilever.gh straight from this script, without Rhino:
#     ./make.sh SteelCantilever.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 4 m cantilever, built in at its root and free at its tip, carries its own weight and 15 kN at
# the tip. The member is S355 from the material library and a section from the steel section
# library, an HEA 200 to begin with. Run Analysis solves it and Utilisation checks it. The
# buckling lengths are given as twice the length, as a cantilever needs: left empty they would be
# the beam's own length, held at both ends, and the free tip is not held at all.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
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
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
UTILISATION = "4F8A2D6B-91C3-4E57-B0A9-3C6E8D1F2A74"        # Utilisation

# Utilisation's inputs and outputs, by place. Fabrication to Report are plugs of its Design menu.
U_MODEL, U_LENGTH_Y, U_REPORT, U_LENGTH_Z, U_LENGTH_LT = 0, 3, 7, 8, 9
U_BENDING_Z, U_LTB, U_MAX, U_CLASS, U_GOVERNING, U_REPORT_OUT = 5, 9, 12, 15, 16, 17

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
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


# ---------------------------------------------------------------- the cantilever

# Columns in the order the data flows...
INPUTS, GEOMETRY, NAMES, MEMBER, ELEMENT, LOADS, PATTERN = range(7)
MODEL, RUN, CHECKS, CHECK, RESULTS = range(7, 12)
# ...and rows, one per part of the model.
MAIN, BASE, TIP, WEIGHT = range(4)


def build(doc):
    grid = Grid(doc)

    # 1 - The cantilever: what to try.
    length = slider(grid, "Length [m]", 1, 8, 4, 1, INPUTS, MAIN)
    section_name = value_list(grid, "Section", [("IPE300", '"IPE300"'), ("HEA200", '"HEA200"'), ("HEA240", '"HEA240"')], 1, INPUTS, BASE)
    grade = value_list(grid, "Grade", [("S235", '"S235"'), ("S275", '"S275"'), ("S355", '"S355"')], 2, INPUTS, BASE, gap=20)
    tip_load = slider(grid, "Tip load [kN]", 0, 40, 15, 1, INPUTS, TIP)

    # 2 - Geometry: a line along X, from the root at the origin to the tip.
    root = component(grid, CONSTRUCT_POINT, "Construct Point", "Root", GEOMETRY, BASE)
    tip = component(grid, CONSTRUCT_POINT, "Construct Point", "Tip", GEOMETRY, MAIN)
    wire(inp(tip, 0), length)
    line = component(grid, LINE, "Line", "Cantilever", NAMES, MAIN)
    wire(inp(line, 0), out(root))
    wire(inp(line, 1), out(tip))

    # 3 - The member. Material Library Elastic stamps the grade on the material, which is how
    # Utilisation knows the beam is steel and what its fy is. Left without a ZAxis, the beam's local
    # y points up: the depth of the I is vertical and the tip load bends it about its major axis.
    material = component(grid, MATERIAL, "Material Library Elastic", None, NAMES, BASE)
    wire(inp(material, 0), grade)
    section = component(grid, SECTION, "Steel Section Library", None, MEMBER, BASE)
    wire(inp(section, 0), section_name)
    wire(inp(section, 1), out(material))
    beam = component(grid, BEAM, "ForceBeamColumn", None, ELEMENT, MAIN)
    wire(inp(beam, 0), out(line))
    wire(inp(beam, 1), out(section))

    # 4 - Built in at the root; its own weight and a load down at the tip.
    fixed = toggle(grid, "Restrained", True, MEMBER, TIP)
    support = component(grid, SUPPORT, "Support", "Built in", ELEMENT, BASE)
    wire(inp(support, 0), out(root))
    for i in range(1, 7):
        wire(inp(support, i), fixed)
    down = expression(grid, "f(p)=-p", [tip_load], "Downwards", GEOMETRY, TIP)
    direction = component(grid, UNIT_Z, "Unit Z", "Tip force", NAMES, TIP)
    wire(inp(direction, 0), out(down))
    point_load = component(grid, POINT_LOAD, "Point Load", "Tip load", ELEMENT, TIP)
    wire(inp(point_load, 0), out(tip))
    wire(inp(point_load, 1), out(direction))
    gravity = component(grid, GRAVITY, "Gravity Load", None, ELEMENT, WEIGHT)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, TIP)
    wire(inp(pattern, 1), out(point_load), out(gravity))

    # 5 - Analysis: one static step, the default.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, MAIN)
    wire(inp(assemble, 0), out(beam))
    wire(inp(assemble, 1), out(support))
    wire(inp(assemble, 2), out(pattern))
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, TIP)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, MAIN)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(grid, MODEL_VIEW, "Model View", None, RUN, TIP)
    wire(inp(view, 0), out(run))

    # 6 - The check. A cantilever's tip is free, so the buckling lengths are given: twice the
    # length about both axes, and 2 L for lateral-torsional buckling too, with the root moment
    # taken as uniform over it - on the safe side for a root built in sideways and against twist.
    # The Design menu is open, for the Report and the governing check.
    twice = expression(grid, "f(L)=2*L", [length], "Cantilever lengths", CHECKS, MAIN)
    report = toggle(grid, "Report", True, CHECKS, TIP)
    utilisation = emit(UTILISATION, "Utilisation")
    open_menus(utilisation)
    grid.put(utilisation, CHECK, MAIN)
    wire(inp(utilisation, U_MODEL), out(run))
    for i in (U_LENGTH_Y, U_LENGTH_Z, U_LENGTH_LT):
        wire(inp(utilisation, i), out(twice))
    wire(inp(utilisation, U_REPORT), report)

    max_ = panel(grid, "Max", out(utilisation, U_MAX), 120, 40, RESULTS, MAIN)
    governing = panel(grid, "Governing", out(utilisation, U_GOVERNING), 120, 40, RESULTS, MAIN)
    ltb = panel(grid, "LTB", out(utilisation, U_LTB), 120, 40, RESULTS, MAIN)
    bending = panel(grid, "BendingZ", out(utilisation, U_BENDING_Z), 120, 40, RESULTS, MAIN)
    klass = panel(grid, "Class", out(utilisation, U_CLASS), 120, 40, RESULTS, MAIN)
    report_text = panel(grid, "Report", out(utilisation, U_REPORT_OUT), 620, 420, RESULTS, TIP)

    grid.arrange(0, 0, stages=(GEOMETRY, NAMES, MODEL, CHECKS))

    group(doc, "1 - The cantilever", [length, section_name, grade, tip_load], RED)
    group(doc, "2 - Geometry", [root, tip, down], ORANGE)
    group(doc, "3 - Member", [line, material, section, direction, beam], PURPLE)
    group(doc, "4 - Support and loads", [fixed, support, point_load, gravity, pattern], GREEN)
    group(doc, "5 - Analysis", [assemble, settings, run, view], BLUE)
    group(doc, "6 - Utilisation", [twice, report, utilisation, max_, governing, ltb, bending, klass, report_text], GREY)

    # Only the model draws in Rhino, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "Steel cantilever",
         "Steel cantilever - the Alpaca4d Utilisation example.\n\n"
         "A 4 m cantilever, built in at the root, carries its own weight and 15 kN at the tip. "
         "Utilisation checks it to EN 1993-1-1: 1 is exactly at the limit.\n\n"
         "The buckling lengths are the point of it. Left empty, Utilisation takes each beam's own "
         "length, held at both ends - but a cantilever's tip is free. Here LengthY, LengthZ and "
         "LengthLT are twice the length, and the root moment is taken as uniform over it: on the "
         "safe side for a root built in sideways and against twist.\n\n"
         "HEA 200: bending 0.48, but lateral-torsional buckling 0.89 - LTB governs.\n"
         "Switch Section to IPE300, the same weight: bending only 0.30, LTB 1.11 - it fails. With "
         "the lengths left empty it would have read 0.38.\n\n"
         "Open the Report panel to follow the check by hand.",
         0, -330, 560, 260)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Steel cantilever built. Change Section, Length or Tip load and watch Utilisation.")


if __name__ == "__main__":
    main()
