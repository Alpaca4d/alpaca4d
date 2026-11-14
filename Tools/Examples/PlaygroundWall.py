#! python3
# The Wall with a window, lesson 9 of the website's Playground: a concrete wall standing on its
# foundation, with a window cut into it and a floor bearing on its top. How do the floor's forces get
# round the window to the ground? Built on the Grasshopper canvas.
#
# make.sh, next to it, writes PlaygroundWall.gh straight from this script, without Rhino:
#     ./make.sh PlaygroundWall.py
#     START=1 ./make.sh PlaygroundWall.py PlaygroundWallStart.gh    # the wall, meshed into shells, only
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript. Mesh
# Brep needs Rhino to mesh, so the definition solves only there.
#
# One group per round of the lesson, in its order: the wall, 5.5 m long and 4 m tall in the XZ plane,
# with a window 1.5 m square whose sill is 1 m up; the mesh, quads 0.25 m across with a node on each
# of the points 0.5 m apart along its base and its top; the shell, 200 mm of concrete; the supports,
# the points along its base held; the floor, 50 kN down on each of the 12 points along its top,
# 600 kN in all; the model assembled; and the analysis, with its vertical stress drawn and the
# principal stress lines traced through it. The Playground's lesson (assets/site/playground/wall.js
# in the website) draws its snippets from this definition, read by Tools/GhCanvas, and hands this
# file out at the end.
#
# With START=1 it writes the lesson's start in the docs instead: the wall, meshed and made of shells,
# for the rest to be added by hand.
#
# The hand check: beside the window the wall is 5.5 - 1.5 = 4 m long, so the piers carry the floor's
# 600 kN on 4 x 0.2 = 0.8 m2: 600 / 0.8 = 750 kN/m2, 0.75 MPa on average, where the wall far from the
# window carries 600 / (5.5 x 0.2) = 0.55 MPa. OpenSees gives the same mean, rising to 1.0 MPa at the
# window's edge and about 1.5 MPa at its top corners.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_String, GH_Point
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
BOUNDARY = "D51E9B65-AA4E-4FD6-976C-CEF35D421D05"           # Surface > Freeform > Boundary Surfaces
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
DIVIDE = "2162E72E-72FC-4BF8-9459-D4D82FA8AA14"             # Curve > Division > Divide Curve
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point

# Alpaca4d
MESH_BREP = "E0121E43-9C8D-4FC5-A273-50FE088F5E43"          # Mesh Brep
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
STRESS_LINES = "E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2"       # Principal Stress Lines

# The wall, in metres: 5.5 long and 4 tall, with a window 1.5 square whose sill is 1 up, in the middle:
# its corners along the sill and up its right side, as Rectangle 3Pt takes them.
LENGTH, HEIGHT, THICKNESS = 5.5, 4.0, 0.2
WINDOW = [(2.0, 0.0, 1.0), (3.5, 0.0, 1.0), (3.5, 0.0, 2.5)]
# Concrete, kN and m: E 33 000 MPa, Poisson's ratio 0.2.
E, NU, DENSITY = 33000000.0, 0.2, 2500.0
# Points 0.5 m apart along the base and the top: 11 divisions of 5.5 m, 12 points. The mesh's quads
# are 0.25 m, half that, so that each point has a node of its own to pull onto itself.
DIVISIONS, EDGE_LENGTH = 11, 0.25
# The floor: 50 kN on each of the 12 points along the top, 600 kN in all.
FLOOR = -50.0
# Principal Stress Lines' inputs, by place, and where its field of lines starts: in the left pier.
PSL_MODEL, PSL_SEED = 0, 2
SEED = (1.0, 0.0, 1.75)
# Model View's results, from 1: Shell stresses is the fifth (after Displacement, Beam forces, Beam
# stresses and Shell forces), and sigma22 the second of its components - the vertical stress, with
# the shells' Local X along the wall.
SHELL_STRESSES, SIGMA_22 = 5, 1
# The start of the docs lesson, or the whole wall.
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

    def arrange(self, x0, y0, stages=(), col_gap=50.0, row_gap=36.0, stack_gap=24.0, stage_gap=90.0):
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


def number(param, value):
    """Types a number into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Number(float(value)))
    return param


def integer(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
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


def point_into(param, xyz):
    """Types a point into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(*xyz)))
    return param


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


# ---------------------------------------------------------------- the wall

# Columns in the order the data flows...
SIZES, CORNERS, OUTLINES, SURFACE, EDGES, PICKED, POINTS, MESHED, STUFF, SECTION, ELEMENT, PATTERN, \
    MODEL, SETTINGS, RUN, VIEW = range(16)
# ...and rows, one per part of the model.
WALL_, HELD, LOADED = range(3)


def clear_of_the_mesh_wire(mesh, shell, below):
    """The shell's Mesh input level with Mesh Brep's output, and the material, the thickness, the
    axis and the section below that line, so that the mesh's wire runs clear of them."""
    b = mesh.Attributes.Bounds
    line = b.Y + b.Height / 2.0
    drop = line + 24.0 - min(o.Attributes.Bounds.Y for o in below)
    for o in below:
        o_b = o.Attributes.Bounds
        move(o, o_b.X, o_b.Y + drop)
    s = shell.Attributes.Bounds
    move(shell, s.X, line - 12.0)
    return drop


def build(doc):
    grid = Grid(doc)

    # 1 - The wall: its outline through three corners, standing up along X and Z, and the window's,
    # 1.5 m square with its sill 1 m up, typed into Rectangle 3Pt; Boundary Surfaces cuts the window
    # out of the outline.
    width = slider(grid, "Length", 1, 10, LENGTH, 2, SIZES, WALL_)
    height = slider(grid, "Height", 1, 10, HEIGHT, 2, SIZES, WALL_)
    corner = component(grid, CONSTRUCT_POINT, "Construct Point", "Corner", CORNERS, WALL_)
    along = component(grid, CONSTRUCT_POINT, "Construct Point", "Along", CORNERS, WALL_)
    wire(inp(along, 0), width)
    up = component(grid, CONSTRUCT_POINT, "Construct Point", "Up", CORNERS, WALL_)
    wire(inp(up, 0), width)
    wire(inp(up, 2), height)
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Wall outline", OUTLINES, WALL_)
    wire(inp(outline, 0), out(corner))
    wire(inp(outline, 1), out(along))
    wire(inp(outline, 2), out(up))
    window = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Window", OUTLINES, WALL_)
    for i, xyz in enumerate(WINDOW):
        point_into(inp(window, i), xyz)
    surface = component(grid, BOUNDARY, "Boundary Surfaces", "Wall surface", SURFACE, WALL_)
    flat(wire(inp(surface, 0), out(outline), out(window)))

    # 2 - The mesh: quads 0.25 m across, and a node on each point 0.5 m apart along the base and the
    # top, where the supports and the floor will go. The outline's first side is its base, its third
    # its top.
    sides = component(grid, EXPLODE, "Explode", None, EDGES, WALL_)
    wire(inp(sides, 0), out(outline))
    base = component(grid, LIST_ITEM, "List Item", "Base edge", PICKED, WALL_)
    wire(inp(base, 0), out(sides))
    top = component(grid, LIST_ITEM, "List Item", "Top edge", PICKED, WALL_)
    wire(inp(top, 0), out(sides))
    integer(inp(top, 1), 2)
    count = slider(grid, "Divisions", 1, 22, DIVISIONS, 0, PICKED, WALL_)
    base_points = component(grid, DIVIDE, "Divide Curve", "Base points", POINTS, WALL_)
    wire(inp(base_points, 0), out(base))
    wire(inp(base_points, 1), count)
    top_points = component(grid, DIVIDE, "Divide Curve", "Top points", POINTS, WALL_)
    wire(inp(top_points, 0), out(top))
    wire(inp(top_points, 1), count)
    edge = slider(grid, "Edge length", 0, 2, EDGE_LENGTH, 2, POINTS, WALL_)
    mesh = component(grid, MESH_BREP, "Mesh Brep", None, MESHED, WALL_)
    wire(inp(mesh, 0), out(surface))
    wire(inp(mesh, 1), edge)
    flat(wire(inp(mesh, 2), out(base_points), out(top_points)))

    # 3 - The shell: 200 mm of concrete, its Local X along the wall, so that sigma22 is the
    # vertical stress.
    concrete = component(grid, ND_MATERIAL, "nD", "Concrete", STUFF, WALL_)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    thickness = slider(grid, "Thickness", 0, 1, THICKNESS, 2, STUFF, WALL_)
    lengthwise = component(grid, UNIT_X, "Unit X", "Along the wall", STUFF, WALL_)
    section = component(grid, PLATE_FIBER, "Plate Fiber Section", "200 mm", SECTION, WALL_)
    wire(inp(section, 1), thickness)
    wire(inp(section, 2), out(concrete))
    shell = component(grid, SHELL, "ASD Shell", "Wall", ELEMENT, WALL_)
    wire(inp(shell, 0), out(mesh))
    wire(inp(shell, 1), out(section))
    wire(inp(shell, 3), out(lengthwise))

    if START:
        grid.arrange(0, 0, stages=(EDGES, STUFF))
        clear_of_the_mesh_wire(mesh, shell, [concrete, thickness, lengthwise, section])
        group(doc, "1 - The wall", [width, height, corner, along, up, outline, window, surface], RED)
        group(doc, "2 - The mesh", [sides, base, top, count, base_points, top_points, edge, mesh], ORANGE)
        group(doc, "3 - The shell", [concrete, thickness, lengthwise, section, shell], PURPLE)
        note(doc, "The Wall with a window - start",
             "The Wall with a window - lesson 9 of the Alpaca4d docs, the start.\n\n"
             "A concrete wall 5.5 m long, 4 m tall and 200 mm thick, with a window 1.5 m square: "
             "meshed into quads 0.25 m across, with a node on each point 0.5 m apart along its base "
             "and its top, and made of shells. Add the supports, the floor, the analysis and the "
             "stress lines as the lesson's steps say.",
             0, -240, 520, 160)
        return

    # 4 - The supports: the wall stands on its foundation, held every way at the points along its
    # base.
    foundation = component(grid, SUPPORT, "Support", "Foundation", ELEMENT, HELD)
    flat(wire(inp(foundation, 0), out(base_points)))

    # 5 - The floor: 50 kN down on each of the 12 points along the top, 600 kN in all.
    floor = slider(grid, "Floor", -100, 0, FLOOR, 1, STUFF, LOADED)
    down = component(grid, UNIT_Z, "Unit Z", "Down", SECTION, LOADED)
    wire(inp(down, 0), floor)
    point_load = component(grid, POINT_LOAD, "Point Load", "The floor", ELEMENT, LOADED)
    flat(wire(inp(point_load, 0), out(top_points)))
    wire(inp(point_load, 1), out(down))
    pattern = component(grid, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOADED)
    wire(inp(pattern, 1), out(point_load))

    # 6 - Assemble: the shells, the supports and the load pattern into one model, in the row between.
    assemble = component(grid, ASSEMBLE, "Assemble Model", None, MODEL, HELD)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), out(foundation)))
    wire(inp(assemble, 2), out(pattern))

    # 7 - Run: one static step, the vertical stress drawn on the wall, and the principal stress lines
    # traced through it from a seed in the left pier. Assemble Model, Run Analysis and Model View in a
    # line, Analysis Settings under it, and the stress lines under Model View with their seed.
    settings = component(grid, ANALYSIS_SETTINGS, "Analysis Settings", None, SETTINGS)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", None, RUN, HELD)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(grid, MODEL_VIEW, "Model View", "Stresses", VIEW)
    wire(inp(view, 0), out(run))
    menu_setting(view, "_ckShowSupports", "Active", True)
    menu_setting(view, "_ckShowLoads", "Active", True)
    show_result(view, SHELL_STRESSES, SIGMA_22)
    seed = component(grid, POINT, "Point", "Seed", VIEW)
    seed.PersistentData.Clear()
    seed.PersistentData.Append(GH_Point(Point3d(*SEED)))
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = "Flow"
    grid.put(lines, VIEW)
    wire(inp(lines, PSL_MODEL), out(run))
    wire(inp(lines, PSL_SEED), seed)

    grid.arrange(0, 0, stages=(EDGES, STUFF, MODEL, SETTINGS), row_gap=60.0)
    drop = clear_of_the_mesh_wire(mesh, shell, [concrete, thickness, lengthwise, section])
    # The rows under the shell down as far, so that its group and theirs keep apart.
    for o in (foundation, floor, down, point_load, pattern, assemble, run):
        b = o.Attributes.Bounds
        move(o, b.X, b.Y + drop)
    # Wide gaps: Grasshopper draws these components wider than they are measured without Rhino.
    in_line = run.Attributes.Bounds
    move(settings, settings.Attributes.Bounds.X, in_line.Bottom + 30.0)
    move(run, settings.Attributes.Bounds.Right + 100.0, in_line.Y)
    move(view, run.Attributes.Bounds.Right + 80.0, in_line.Y)
    # The stress lines on the right of Model View, clear of its menus, their first input level with
    # Run Analysis's output and their seed on their left. Placed by their pivot: mono measures their
    # menus thousands wide, so their bounds would put them anywhere.
    a = lines.Attributes
    a.Pivot = PointF(float(view.Attributes.Bounds.Right + 230.0), float(in_line.Y + 60.0))
    a.ExpireLayout()
    a.PerformLayout()
    move(seed, a.Pivot.X - 160.0, a.Pivot.Y - 10.0)

    group(doc, "1 - The wall", [width, height, corner, along, up, outline, window, surface], RED)
    group(doc, "2 - The mesh", [sides, base, top, count, base_points, top_points, edge, mesh], ORANGE)
    group(doc, "3 - The shell", [concrete, thickness, lengthwise, section, shell], PURPLE)
    group(doc, "4 - The supports", [foundation], GREEN)
    group(doc, "5 - The floor", [floor, down, point_load, pattern], BLUE)
    group(doc, "6 - Assemble", [assemble], YELLOW)
    group(doc, "7 - Run", [settings, run, view, seed, lines], GREY)

    # Only the model and its stress lines draw in Rhino, not the construction behind them.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in (view.InstanceGuid, lines.InstanceGuid):
            obj.Hidden = True

    note(doc, "The Wall with a window",
         "The Wall with a window - lesson 9 of the Alpaca4d Playground.\n\n"
         "A concrete wall 5.5 m long, 4 m tall and 200 mm thick, standing on its foundation, with a "
         "window 1.5 m square and a floor bearing on its top: 50 kN on each of the 12 points along "
         "it, 600 kN in all.\n\n"
         "Check it by hand: beside the window the piers are 5.5 - 1.5 = 4 m long, so they carry "
         "600 / (4 x 0.2) = 750 kN/m2 = 0.75 MPa on average, where the wall far from the window "
         "carries 600 / (5.5 x 0.2) = 0.55 MPa.\n\n"
         "Try: type other corners into the Window, make it wider, or push the floor along the wall "
         "with a Unit X in place of Unit Z, as wind or an earthquake would.",
         0, -320, 560, 240)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Wall with a window built. Move Floor or Thickness and watch the stresses.")


if __name__ == "__main__":
    main()
