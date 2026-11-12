#! python3
# 3D printed structures of fresh concrete, checked for buckling while they are printed, layer by layer
# along their own sections. Built on the Grasshopper canvas, one definition per shape:
#
#   SHAPE=branching (the default)  PrintedStructure.gh: the shape of the earlier voxel example, a branching
#                                  shell 0.33 x 0.43 m and 0.36 m tall - three legs at its foot, two above,
#                                  one towards the top, open at its crown - from PrintedStructure.3dm
#   SHAPE=twisted                  PrintedTwistedColumn.gh: a column of six lobes round a radius of 160 mm,
#                                  turning 60 degrees over its 0.6 m
#   SHAPE=wall                     PrintedWavyWall.gh: a free-standing wall 1 m long and 0.4 m tall, in
#                                  waves 250 mm long and 50 mm deep
#   SHAPE=dome                     PrintedDome.gh: a beehive dome 0.6 m across its foot and 0.39 m tall,
#                                  leaning in as it rises, open at the top
#   SHAPE=leaning                  PrintedLeaningColumn.gh: a tube 0.3 m across, rising 0.2 m, then bending
#                                  over to 45 degrees and on 0.3 m: printed along its axis, in layers square
#                                  to it, the nozzle tilting as it bends - the Path, which unplugged prints
#                                  it in flat layers
#
# Run it in Rhino 8 with Grasshopper and Alpaca4d installed - from the ScriptEditor, or with rhinocode -
# and it opens the definition on the canvas; with SAVE set to a path it also saves it there as a .gh.
# Unlike the other examples it cannot be written by make.sh: the shape, a surface kept in the
# definition, takes Rhino's own geometry code.
#
# The shape is cut every layer height - flat, or square to a Path - and each section is a print path. Printed Layers, a GhPython
# component (PrintedStructureLayers.py), lays a filament 40 x 10 mm along each, round at the sides, its
# bricks running from the nodes of one section to where those nodes land on the next, so that the layers
# stay bonded where the wall leans or twists; each brick gets its age. Fresh Concrete (PrintingConcrete.py) turns
# the ages into Wolfs, Bos and Salet's mortar (E = 0.078 + 0.0012 t MPa); Surface Connection
# (PrintingJoint.py) gives a joint band its own. The base rests on the print bed, held; the concrete's
# own weight is the load; Linear Buckling gives the factor on that weight at which the structure buckles
# as it is printed, and Run Analysis how much it settles under it.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import math

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_ParameterSide, GH_ParamAccess
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS, GH_BooleanHint_CS, GH_CurveHint
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color, Bitmap
from Grasshopper.Kernel.Parameters import Param_Brep, Param_Curve
from Grasshopper.Kernel.Types import GH_Brep, GH_Curve
from Grasshopper.Kernel.Parameters.Hints import GH_BrepHint

# Grasshopper
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script

# Alpaca4d
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
LINEAR_BUCKLING = "6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815"    # Linear Buckling Analysis
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
# Model View's results, from 1: Displacement is the first, and Magnitude the first of its components.
DISPLACEMENT, MAGNITUDE = 1, 0
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
INPUTS, PRINT, MATERIAL, ELEMENT, PATTERN, MODEL, RUN, VIEW = range(8)
# ...and rows, one per part of the model.
SHAPE, PRINTING, CONCRETE, JOINT, BED, WEIGHT, SETTLE = range(7)


def build(doc, spec):
    grid = Grid(doc)

    # 1 - The shape: a surface, in metres, its foot on the print bed; kept in the definition.
    shape = Param_Brep()
    shape.CreateAttributes()
    shape.NickName = "Shape"
    shape.PersistentData.Append(GH_Brep(spec["make"]()))
    grid.put(shape, INPUTS, SHAPE)
    # A Path to print along, square to it, for a shape that bends over: the layers lean with it.
    path = None
    if spec.get("path"):
        path = Param_Curve()
        path.CreateAttributes()
        path.NickName = "Path"
        path.PersistentData.Append(GH_Curve(spec["path"]()))
        grid.put(path, INPUTS, SHAPE, gap=10)

    # GhPython's hints: numbers as they are, Alpaca4d's materials untouched.
    hints = emit(PYTHON, "GhPython").GetType().Assembly
    def hint(name):
        return System.Activator.CreateInstance(hints.GetType("GhPython.Component." + name))
    number, keep = hint("NewFloatHint"), hint("NoChangeHint")
    integer, boolean, brep, curve = GH_IntegerHint_CS(), GH_BooleanHint_CS(), GH_BrepHint(), GH_CurveHint()
    item, many = GH_ParamAccess.item, GH_ParamAccess.list

    # 2 - Printing: the shape cut every 10 mm, a filament of 40 x 10 mm along each section, round at
    # the sides, at 5 m/min, cut 1 across, 2 up and about 4 cm along; Time says how far the print has
    # got. A GhPython component, Printed Layers.
    height = slider(grid, "Height", 0.005, 0.03, 0.01, 3, INPUTS, PRINTING, gap=10)
    width = slider(grid, "Width", 0.01, 0.1, 0.04, 3, INPUTS, PRINTING)
    rounding = slider(grid, "Round", 0, 1, 1, 2, INPUTS, PRINTING)
    across = slider(grid, "Across", 1, 4, 1, 0, INPUTS, PRINTING)
    up = slider(grid, "Up", 1, 4, 2, 0, INPUTS, PRINTING)
    segment = slider(grid, "Segment", 0.01, 0.1, 0.04, 3, INPUTS, PRINTING)
    speed = slider(grid, "Speed [m/min]", 1, 20, 5, 1, INPUTS, PRINTING)
    time = slider(grid, "Time [min]", 0.05, 20, spec["time"], 2, INPUTS, PRINTING, gap=10)
    joint = slider(grid, "Joint", 0, 0.002, 0, 4, INPUTS, JOINT)
    printed = python(grid, "Printed Layers", "PrintedStructureLayers.py", "printed_layers",
                     [("Shape", brep, item), ("Path", curve, item), ("Height", number, item), ("Width", number, item),
                      ("Round", number, item), ("Across", integer, item), ("Up", integer, item), ("Segment", number, item),
                      ("Joint", number, item), ("Speed", number, item), ("Time", number, item)],
                     ["Mesh", "Age", "InJoint", "Colour", "Base", "Nozzle", "Axis", "Printed", "Duration"],
                     "printed-layers-stack.png", PRINT, PRINTING)
    for name, source in (("Shape", shape), ("Height", height), ("Width", width), ("Round", rounding), ("Across", across),
                         ("Up", up), ("Segment", segment), ("Joint", joint), ("Speed", speed), ("Time", time)):
        wire(named(printed.Params.Input, name), source)
    if path is not None:
        wire(named(printed.Params.Input, "Path"), path)
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

    # 9 - Settle: one static step under its own weight, and how far it moves, drawn larger.
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, RUN, SETTLE)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, SETTLE)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    settle = component(grid, MODEL_VIEW, "Model View", "The settling", VIEW, SETTLE)
    wire(inp(settle, 0), out(run))
    menu_setting(settle, "_ckShowSupports", "Active", False)
    menu_setting(settle, "_ckDeformed", "Active", True)
    show_result(settle, DISPLACEMENT, MAGNITUDE)

    grid.arrange(0, 0, stages=(MATERIAL, MODEL, RUN), row_gap=50.0)

    group(doc, "1 - The shape", [shape] + ([path] if path is not None else []), RED)
    group(doc, "2 - Printing", [height, width, rounding, across, up, segment, speed, time, printed, count], ORANGE)
    group(doc, "3 - Fresh concrete", [concrete], PURPLE)
    group(doc, "4 - The joint and the bricks", [joint, ks, connection, bricks], BLUE)
    group(doc, "5 - The print bed", [bed], GREEN)
    group(doc, "6 - Self-weight", [gravity, pattern], GREEN)
    group(doc, "7 - Assemble", [tolerance, assemble], YELLOW)
    group(doc, "8 - Buckle", [buckling, factor, view, print_view], GREY)
    group(doc, "9 - Settle", [settings, run, settle], BLUE)

    # Only the model draws in Rhino, not the bricks and points behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc, spec["title"],
         spec["about"] + "\n\n"
         "Printed Layers, Fresh Concrete and Surface Connection are GhPython components: open them to see, "
         "and change, how the bricks are cut, how the concrete stiffens and what a joint is.\n\n" + spec["try"],
         0, -360, 560, 300)


def read_shape():
    """The branching shell, from PrintedStructure.3dm next to this script: a surface in metres, its foot at
    z = 0."""
    import Rhino.FileIO
    model = Rhino.FileIO.File3dm.Read(System.IO.Path.Combine(HERE, "PrintedStructure.3dm"))
    for obj in model.Objects:
        if isinstance(obj.Geometry, Rhino.Geometry.Brep):
            return obj.Geometry.Duplicate()
    raise Exception("PrintedStructure.3dm holds no Brep.")


# Each shape's surface ends a hair above its top layer, so that the section at the top of that layer is
# cut too: Printed Layers cuts from 0.1 mm above the foot, every Height, up to 0.1 mm below the top.

def twisted_column():
    """Six lobes round a radius of 160 mm, 18% deep, turning 60 degrees - one lobe - over 0.6 m: a loft
    through sections every 50 mm, each a closed curve through 96 points."""
    rg = Rhino.Geometry
    height, top = 0.6, 0.605
    curves = []
    for k in range(13):
        z = top * k / 12.0
        points = []
        for i in range(96):
            t = 2 * math.pi * i / 96.0
            r = 0.16 * (1 + 0.18 * math.cos(6 * (t - math.pi / 3 * z / height)))
            points.append(rg.Point3d(r * math.cos(t), r * math.sin(t), z))
        # A periodic curve is closed only through a last point that repeats the first.
        curves.append(rg.Curve.CreateInterpolatedCurve(points + [points[0]], 3, rg.CurveKnotStyle.ChordPeriodic))
    return rg.Brep.CreateFromLoft(curves, rg.Point3d.Unset, rg.Point3d.Unset, rg.LoftType.Normal, False)[0]


def wavy_wall():
    """A wall 1 m long in waves 250 mm long and 50 mm deep, 0.4 m tall."""
    rg = Rhino.Geometry
    points = [rg.Point3d(x, 0.05 * math.sin(2 * math.pi * x / 0.25), 0) for x in (i / 400.0 for i in range(401))]
    curve = rg.Curve.CreateInterpolatedCurve(points, 3)
    return rg.Surface.CreateExtrusion(curve, rg.Vector3d(0, 0, 0.405)).ToBrep()


def dome():
    """A beehive dome: its wall an arc of radius 0.6 m, 0.3 m from the centre at its foot and leaning in as it
    rises, to an opening 0.31 m across at 0.39 m, where it leans 40 degrees."""
    rg = Rhino.Geometry
    rho, foot, top = 0.6, 0.3, 0.39

    def radius(z):
        return foot - rho + math.sqrt(rho * rho - z * z)

    arc = rg.Arc(rg.Point3d(radius(0), 0, 0), rg.Point3d(radius(top / 2), 0, top / 2), rg.Point3d(radius(top), 0, top))
    return rg.RevSurface.Create(rg.ArcCurve(arc), rg.Line(rg.Point3d(0, 0, 0), rg.Point3d(0, 0, 1))).ToBrep()


def leaning_axis():
    """The leaning column's axis: up 0.2 m, a bend of radius 0.4 m over to 45 degrees, then on 0.3 m."""
    rg = Rhino.Geometry
    up, bend, angle, on = 0.2, 0.4, math.pi / 4, 0.3
    a, b = rg.Point3d(0, 0, 0), rg.Point3d(0, 0, up)
    c = rg.Point3d(bend * (1 - math.cos(angle)), 0, up + bend * math.sin(angle))
    d = c + rg.Vector3d(math.sin(angle), 0, math.cos(angle)) * on
    axis = rg.PolyCurve()
    axis.Append(rg.Line(a, b))
    axis.Append(rg.Arc(b, rg.Vector3d.ZAxis, c))
    axis.Append(rg.Line(c, d))
    return axis


def leaning_column():
    """A tube of radius 150 mm round the leaning column's axis, open at both ends."""
    rg = Rhino.Geometry
    # PipeCapMode.None, which IronPython names and Python 3 does not: there, the enum of its value, 0.
    cap = getattr(rg.PipeCapMode, "None", None)
    if cap is None:
        cap = rg.PipeCapMode(0)
    return rg.Brep.CreatePipe(leaning_axis(), 0.15, False, cap, True, 1e-6, 0.01)[0]


TRY = ("Try: Height 5 mm for a finer print; Joint 1 mm and a lower Ks lets the layers slip; Speed lower gives "
       "each layer longer to stiffen, though this mortar gains little in a few minutes.")
FILAMENT = ("cut every 10 mm: each section is a print path, a filament 40 x 10 mm laid along it at 5 m/min in a "
            "mortar whose stiffness grows with age, E = 0.078 + 0.0012 t MPa (Wolfs, Bos and Salet 2018). ")

SHAPES = {
    "branching": dict(
        file="PrintedStructure.gh", make=read_shape, time=4.0, title="A printed structure",
        about="A 3D printed structure, checked for buckling while it is printed.\n\n"
              "The branching shell of the earlier voxel example, 0.36 m tall, " + FILAMENT +
              "The layers follow the lean of the walls, node for node. Move Time: more is printed, and the load "
              "factor on its own weight falls. At 20 layers, 5.5 minutes in, as its last two legs are about to "
              "merge, it is down to 1.00, and it stays within 5% of 1 to the end: a print on the edge of buckling, "
              "the flat back of the long leg ready to bow out.",
        try_=TRY),
    "twisted": dict(
        file="PrintedTwistedColumn.gh", make=twisted_column, time=8.0, title="A printed twisted column",
        about="A twisted column of fresh concrete, checked for buckling while it is printed.\n\n"
              "Six lobes round a radius of 160 mm, turning 60 degrees over its 0.6 m, " + FILAMENT +
              "Move Time: more is printed, and the load factor on its own weight falls. It reaches 1 at about "
              "53 of its 60 layers. TU/e's plain cylinder, wider at 250 mm, buckles at 47: the lobes stiffen "
              "the wall.",
        try_=TRY),
    "wall": dict(
        file="PrintedWavyWall.gh", make=wavy_wall, time=5.0, title="A printed wavy wall",
        about="A wavy wall of fresh concrete, checked for buckling while it is printed.\n\n"
              "A free-standing wall 1 m long, in waves 250 mm long and 50 mm deep, 0.4 m tall, " + FILAMENT +
              "Move Time: more is printed, and the load factor on its own weight falls. It reaches 1 at about "
              "28 layers, its free end swinging out. A flat wall of the same length buckles at about 15: the "
              "waves nearly double it.",
        try_=TRY),
    "dome": dict(
        file="PrintedDome.gh", make=dome, time=6.0, title="A printed dome",
        about="A beehive dome of fresh concrete, checked for buckling while it is printed.\n\n"
              "0.6 m across its foot, leaning in as it rises to an opening 0.31 m across at 0.39 m, " + FILAMENT +
              "Every layer rests a little inside the one below, up to 40 degrees at the top. Move Time: more is "
              "printed, and the load factor on its own weight falls. It reaches 1 at about 27 of its 38 layers, its "
              "rim going oval.",
        try_=TRY),
    "leaning": dict(
        file="PrintedLeaningColumn.gh", make=leaning_column, path=leaning_axis, time=10.0,
        title="A printed leaning column",
        about="A column of fresh concrete that rises and then leans over, printed along its axis and checked for "
              "buckling while it is printed.\n\n"
              "A tube 0.3 m across, rising 0.2 m, then bending over to 45 degrees on a radius of 0.4 m and on 0.3 m. "
              "It is cut square to its axis, the Path, every 10 mm along it: a printer that tilts its nozzle as the "
              "column bends lays each layer square to the wall, thicker on the outside of the bend and thinner on its "
              "inside, a filament 40 mm wide at 5 m/min in a mortar whose stiffness grows with age, "
              "E = 0.078 + 0.0012 t MPa (Wolfs, Bos and Salet 2018). Move Time: more is printed, and the load factor "
              "on its own weight falls. It reaches 1 at about 53 of its 81 layers, as it leans over.",
        try_="Try: unplug Path, and the column is printed in flat layers, each a little over the one below - its "
             "45-degree wall then only 28 mm thick square to it - and it reaches 1 sooner, at about 47 layers. "
             "Height 5 mm for a finer print; Joint 1 mm and a lower Ks lets the layers slip."),
}
for spec in SHAPES.values():
    spec["try"] = spec.pop("try_")


def main():
    name = System.Environment.GetEnvironmentVariable("SHAPE") or "branching"
    if name not in SHAPES:
        raise Exception("SHAPE is one of " + ", ".join(sorted(SHAPES)))
    doc = open_canvas()
    build(doc, SHAPES[name])
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    path = System.Environment.GetEnvironmentVariable("SAVE")
    if path:
        Grasshopper.Kernel.GH_DocumentIO(doc).SaveQuiet(path)
        print("Saved " + path)
    print(SHAPES[name]["title"] + " built. Move Time and watch the load factor.")


if __name__ == "__main__":
    main()
