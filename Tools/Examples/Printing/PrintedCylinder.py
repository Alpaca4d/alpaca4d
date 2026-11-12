#! python3
# A 3D printed cylinder of fresh concrete, checked for buckling while it is printed. Built on the
# Grasshopper canvas.
#
# make.sh, in the folder above, writes PrintedCylinder.gh straight from this script, without Rhino:
#     ./make.sh Printing/PrintedCylinder.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# The cylinder TU/e printed (Wolfs, Bos and Salet, "Early age mechanical behaviour of 3D printed
# concrete", Cement and Concrete Research 106, 2018): a print path of radius 250 mm, layers 40 mm
# wide and 10 mm high, printed at 5 m/min - a lap every 0.31 minutes. Their mortar stiffens with
# age, E = 0.078 + 0.0012 t MPa, t in minutes, so the bottom of the wall is older and stiffer than
# its top. Three GhPython components do the printing, each with its code in a file next to this
# script: Printed Layers (PrintedCylinderLayers.py) cuts what is printed by Time into bricks - each
# filament an oval, flat where the layers touch and round at the sides - and gives each brick its
# age; Fresh Concrete (PrintingConcrete.py) turns the ages into materials; Surface Connection
# (PrintingJoint.py) gives a joint band its own. Then Alpaca4d: SSP Bricks, the base held on
# the print bed, the concrete's own weight as the load, and Linear Buckling for the factor on that
# weight at which the wall buckles. Move Time on and more is printed: the factor falls, and reaches
# 1 at about 47 layers.
#
# Their five lab prints fell at 25 to 31 layers, 29 on average, and their Abaqus model, plastic and
# geometrically non-linear, at 40 to 53: this is elastic buckling alone, which is where the factor
# says it is. A joint band (Joint, in the fourth group) gives each layer a thin bottom row of bricks
# whose stiffness is given per area by a Surface Connection: lower Ks to let the layers slip.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_ParameterSide, GH_ParamAccess
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS, GH_BooleanHint_CS, GH_CurveHint
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color, Bitmap

# Grasshopper
CIRCLE = "D114323A-E6EE-4164-946B-E4CA0CE15EFA"             # Curve > Primitive > Circle CNR
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script

# Alpaca4d
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
LINEAR_BUCKLING = "6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815"    # Linear Buckling Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View

# SSP Brick's inputs.
B_MESH, B_MATERIAL, B_COLOUR = 0, 1, 2

# The three GhPython components' code and icons, next to this script and in Assets/icon-candidates.
HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
ICONS = System.IO.Path.Combine(HERE, "..", "..", "..", "Assets", "icon-candidates")
# Assemble's inputs.
A_ELEMENTS, A_SUPPORTS, A_PATTERNS, A_TOLERANCE = 0, 1, 2, 5
# Linear Buckling's inputs and outputs.
LB_MODEL, LB_MODES = 0, 1
LB_FACTORS = 1

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per part of the model.
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


def python(grid, nickname, source, function, inputs, outputs, icon, col, row=None):
    """A GhPython component running function from the file source next to this script: its inputs as
    (name, hint, access), its outputs by name after its own out, which carries what the code prints
    and its errors; shown with icon, a PNG in Assets/icon-candidates."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, (name, hint, access) in enumerate(inputs):
        p = c.CreateParameter(GH_ParameterSide.Input, i)
        p.Name = p.NickName = name
        p.TypeHint = hint
        p.Access = access
        c.Params.RegisterInputParam(p)
    for i, name in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    code = System.IO.File.ReadAllText(System.IO.Path.Combine(HERE, source))
    c.Code = code + "\n\n" + ", ".join(outputs) + " = " + function + "(" + ", ".join(i[0] for i in inputs) + ")\n"
    c.SetIconOverride(Bitmap(System.IO.Path.Combine(ICONS, icon)))
    return grid.put(c, col, row)


def named(params, name):
    for p in params:
        if p.Name == name:
            return p
    raise Exception("no parameter " + name)


# ---------------------------------------------------------------- the cylinder

# Columns in the order the data flows...
INPUTS, PATH, PRINT, MATERIAL, ELEMENT, PATTERN, MODEL, RUN, VIEW = range(9)
# ...and rows, one per part of the model.
PRINTING, CONCRETE, JOINT, BED, WEIGHT = range(5)


def build(doc):
    grid = Grid(doc)

    # 1 - The print path: a circle of radius 250 mm, the heart line of the wall, on the print bed.
    # Its centre wired from a point at the origin: Circle CNR made in code has no default centre.
    radius = slider(grid, "Radius", 0.1, 1.0, 0.25, 3, INPUTS, PRINTING)
    centre = component(grid, CONSTRUCT_POINT, "Construct Point", "Centre", INPUTS, PRINTING, gap=10)
    circle = component(grid, CIRCLE, "Circle CNR", "Print path", PATH, PRINTING)
    wire(inp(circle, 0), out(centre))
    wire(inp(circle, 2), radius)

    # GhPython's hints: numbers as they are, Alpaca4d's materials untouched.
    hints = emit(PYTHON, "GhPython").GetType().Assembly
    def hint(name):
        return System.Activator.CreateInstance(hints.GetType("GhPython.Component." + name))
    number, keep, curve = hint("NewFloatHint"), hint("NoChangeHint"), GH_CurveHint()
    integer, boolean = GH_IntegerHint_CS(), GH_BooleanHint_CS()
    item, many = GH_ParamAccess.item, GH_ParamAccess.list

    # 2 - Printing: 60 layers of 40 x 10 mm on that path, round at the sides, at 5 m/min, cut
    # 1 across, 2 up - enough for the round sides - and 4 cm along; Time says how far the print has
    # got. 9.42 min is 30 laps. A GhPython component, Printed Layers.
    layers = slider(grid, "Layers", 1, 100, 60, 0, INPUTS, PRINTING, gap=10)
    width = slider(grid, "Width", 0.01, 0.1, 0.04, 3, INPUTS, PRINTING)
    height = slider(grid, "Height", 0.005, 0.03, 0.01, 3, INPUTS, PRINTING)
    rounding = slider(grid, "Round", 0, 1, 1, 2, INPUTS, PRINTING)
    across = slider(grid, "Across", 1, 4, 1, 0, INPUTS, PRINTING)
    up = slider(grid, "Up", 1, 4, 2, 0, INPUTS, PRINTING)
    segment = slider(grid, "Segment", 0.01, 0.1, 0.04, 3, INPUTS, PRINTING)
    speed = slider(grid, "Speed [m/min]", 1, 20, 5, 1, INPUTS, PRINTING)
    time = slider(grid, "Time [min]", 0.5, 20, 9.42, 2, INPUTS, PRINTING, gap=10)
    joint = slider(grid, "Joint", 0, 0.002, 0, 4, INPUTS, JOINT)
    printed = python(grid, "Printed Layers", "PrintedCylinderLayers.py", "printed_layers",
                     [("Path", curve, many), ("Layers", integer, item), ("Width", number, item), ("Height", number, item),
                      ("Round", number, item), ("Across", integer, item), ("Up", integer, item), ("Segment", number, item),
                      ("Joint", number, item), ("Speed", number, item), ("Time", number, item)],
                     ["Mesh", "Age", "InJoint", "Colour", "Base", "Nozzle", "Printed", "Duration"],
                     "printed-layers-stack.png", PRINT, PRINTING)
    for name, source in (("Path", out(circle)), ("Layers", layers), ("Width", width), ("Height", height), ("Round", rounding),
                         ("Across", across), ("Up", up), ("Segment", segment), ("Joint", joint), ("Speed", speed), ("Time", time)):
        wire(named(printed.Params.Input, name), source)
    count = panel(grid, "Layers printed", named(printed.Params.Output, "Printed"), 90, 24, MATERIAL, PRINTING)

    # 3 - Fresh concrete: each brick's age into its stiffness, Wolfs' mortar - the defaults, left
    # unwired. A GhPython component, Fresh Concrete.
    concrete = python(grid, "Fresh Concrete", "PrintingConcrete.py", "fresh_concrete",
                      [("Age", number, many), ("E0", number, item), ("Rate", number, item), ("Final", number, item),
                       ("Nu", number, item), ("Rho", number, item)],
                      ["Material", "E"], "fresh-concrete-clock.png", MATERIAL, CONCRETE)
    wire(named(concrete.Params.Input, "Age"), named(printed.Params.Output, "Age"))

    # 4 - The joint: with Joint above 0, a band that thick at the bottom of each layer, whose bricks
    # Surface Connection - a GhPython component - gives a stiffness per area; the other bricks keep
    # the concrete. Left at 0 the layers are bonded, as wet on wet. Then the bricks, coloured by age:
    # orange just printed, blue the oldest.
    ks = slider(grid, "Ks [kN/m3]", 1000, 50000, 10000, 0, INPUTS, JOINT)
    connection = python(grid, "Surface Connection", "PrintingJoint.py", "surface_connection",
                        [("Material", keep, many), ("Joint", boolean, many), ("Thickness", number, item),
                         ("Kn", number, item), ("Ks", number, item)],
                        ["Materials", "KnUsed", "KsUsed"], "surface-connection-springs.png", ELEMENT, JOINT)
    wire(named(connection.Params.Input, "Material"), named(concrete.Params.Output, "Material"))
    wire(named(connection.Params.Input, "Joint"), named(printed.Params.Output, "InJoint"))
    wire(named(connection.Params.Input, "Thickness"), joint)
    wire(named(connection.Params.Input, "Ks"), ks)
    bricks = component(grid, BRICK, "SSP Brick", "Bricks", PATTERN, JOINT)
    wire(inp(bricks, B_MESH), named(printed.Params.Output, "Mesh"))
    wire(inp(bricks, B_MATERIAL), named(connection.Params.Output, "Materials"))
    wire(inp(bricks, B_COLOUR), named(printed.Params.Output, "Colour"))

    # 5 - The print bed holds the first layer where it rests.
    bed = component(grid, SUPPORT, "Support", "Print bed", ELEMENT, BED)
    wire(inp(bed, 0), named(printed.Params.Output, "Base"))

    # 6 - The concrete's own weight, the only load while printing.
    gravity = component(grid, GRAVITY, "Gravity Load", None, ELEMENT, WEIGHT)
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", "Self-weight", PATTERN, WEIGHT)
    wire(inp(pattern, 1), out(gravity))

    # 7 - Assemble, with a tolerance below the joint band's 1 mm: the default 10 mm would weld a
    # layer's rows of nodes into one.
    tolerance = slider(grid, "Tolerance", 0.0001, 0.001, 0.0005, 4, PATTERN, BED)
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, CONCRETE)
    wire(inp(assemble, A_ELEMENTS), out(bricks))
    wire(inp(assemble, A_SUPPORTS), out(bed))
    wire(inp(assemble, A_PATTERNS), out(pattern))
    wire(inp(assemble, A_TOLERANCE), tolerance)

    # 8 - Buckle: the factor on the concrete's weight at which the wall buckles, and the wall
    # drawn in that shape, coloured by age, every brick's edges drawn.
    buckling = component(grid, LINEAR_BUCKLING, "Linear Buckling Analysis", "Buckling", RUN, CONCRETE)
    wire(inp(buckling, LB_MODEL), out(assemble))
    factor = panel(grid, "Load factor", out(buckling, LB_FACTORS), 90, 24, RUN, CONCRETE)
    view = component(grid, MODEL_VIEW, "Model View", "The wall", VIEW, CONCRETE)
    wire(inp(view, 0), out(buckling))
    menu_setting(view, "_ckShowSupports", "Active", False)
    menu_setting(view, "_ckDeformed", "Active", True)
    # The wall as printed, before any analysis: hidden, for pictures of it growing.
    print_view = component(grid, MODEL_VIEW, "Model View", "The print", VIEW, BED)
    wire(inp(print_view, 0), out(assemble))
    menu_setting(print_view, "_ckShowSupports", "Active", False)

    grid.arrange(0, 0, stages=(PATH, MATERIAL, MODEL, RUN), row_gap=50.0)

    group(doc, "1 - The print path", [radius, centre, circle], RED)
    group(doc, "2 - Printing", [layers, width, height, rounding, across, up, segment, speed, time, printed, count], ORANGE)
    group(doc, "3 - Fresh concrete", [concrete], PURPLE)
    group(doc, "4 - The joint and the bricks", [joint, ks, connection, bricks], BLUE)
    group(doc, "5 - The print bed", [bed], GREEN)
    group(doc, "6 - Self-weight", [gravity, pattern], GREEN)
    group(doc, "7 - Assemble", [tolerance, assemble], YELLOW)
    group(doc, "8 - Buckle", [buckling, factor, view, print_view], GREY)

    # Only the model draws in Rhino, not the bricks and points behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, "A printed cylinder",
         "A 3D printed cylinder, checked for buckling while it is printed.\n\n"
         "TU/e's cylinder (Wolfs, Bos and Salet 2018): radius 250 mm, layers 40 x 10 mm, 5 m/min, a "
         "mortar whose stiffness grows with age, E = 0.078 + 0.0012 t MPa. Move Time: more layers "
         "are printed, the bottom ones older and stiffer, and the load factor on the concrete's own "
         "weight falls. It reaches 1 - the wall buckles - at about 47 layers.\n\n"
         "Their lab cylinders fell at 25 to 31 layers, by buckling and by the concrete yielding at "
         "the base; this is the elastic buckling.\n\n"
         "Printed Layers, Fresh Concrete and Surface Connection are GhPython components: open them "
         "to see, and change, how the bricks are cut, how the concrete stiffens and what a joint is.\n\n"
         "Try: Joint 1 mm and a lower Ks lets the layers slip; Round 0 squares the filament; Speed "
         "lower gives each layer time to stiffen.",
         0, -360, 560, 300)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The printed cylinder built. Move Time and watch the load factor.")


if __name__ == "__main__":
    main()
