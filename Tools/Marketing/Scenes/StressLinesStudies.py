#! python3
# The principal stress lines studies, for the marketing pictures (run.sh, next to it): symmetric
# slabs and shells after Nervi, Candela, Isler and the like, each its own definition. Not examples of
# the docs. The shapes - meshes, supports, seeds - are in StressLinesShapes.py, next to this file.
#
# Tools/Examples/make.sh writes a study's definition straight from this script, without Rhino, the
# study named by STUDY; make-studies.sh, next to it, writes them all into Studies/:
#
#     STUDY=groin-vault ../../Examples/make.sh StressLinesStudies.py Studies/groin-vault.gh
#
# It also runs in Rhino with Grasshopper, Kangaroo2 (which Rhino 7 and 8 ship) and Alpaca4d
# installed, building the study STUDY names or the first.
#
# Three kinds:
# * a slab: a flat concrete plate on columns under a uniform load, its bending lines; for the
#   pictures, the lines extruded down into ribs, as Nervi built them, and the columns down to a floor;
# * a shell: a surface given by its formula, under its own weight, its membrane lines;
# * a Kangaroo shell: a flat net held at its supports and pushed up, as a cloth hung from them falls
#   but upwards, its edges springs as long as the flat net's; then a shell as above.
# Each is meshed as the shapes say, typed into a Point parameter and Mesh Quad and Mesh Triangle
# components, and analysed alone; a hall of copies, for some, is only for the pictures.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Boolean, GH_String, GH_Vector
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d
from System.Drawing import PointF, RectangleF, Color

# The shapes, next to this file: make.sh puts this file's folder on the path; in Rhino, it is put here.
# System.IO rather than os: make.sh's IronPython has no standard library.
try:
    sys.path.append(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__)))
except NameError:
    pass
import StressLinesShapes as shapes

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
LINE_SDL = "4C619BC9-39FD-4717-82A6-1E07EA237BBE"           # Curve > Primitive > Line SDL
FIT_CURVE = "A3F9F19E-3E6C-4AC7-97C3-946DE32C3E8E"          # Curve > Util > Fit Curve
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
MESH_QUAD = "1CB59C86-7F6B-4E52-9A0C-6441850E9520"          # Mesh > Primitive > Mesh Quad
MESH_TRIANGLE = "5A4DDEDD-5AF9-49E5-BACE-12910A8B9366"      # Mesh > Primitive > Mesh Triangle
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
EXTRUDE = "962034E9-CC27-4394-AFC4-5C16E3447CF9"            # Surface > Freeform > Extrude
PIPE = "C277F778-6FDF-4890-8F78-347EFB23C406"               # Surface > Freeform > Pipe
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move

# Kangaroo2
SHOW = "0ED5E67D-539D-480E-88CB-D81FA795D66C"               # Kangaroo2 > Main > Show
ZOMBIE = "4408343B-577E-4FD8-96FF-DF7549189186"             # Kangaroo2 > Main > Zombie Solver
EDGE_LENGTHS = "24E2B0D2-D7B8-4478-8A83-1F04CBA701A1"       # Kangaroo2 > Goals-Mesh > EdgeLengths
ANCHOR = "3C30B1A1-4473-4AD4-A700-EA9770726C03"             # Kangaroo2 > Goals-Pt > Anchor
LOAD = "2019C995-53AF-4EB2-976D-95B1FDC823FA"               # Kangaroo2 > Goals-Pt > Load

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
STRESS_LINES = "E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2"       # Principal Stress Lines

# Principal Stress Lines' inputs, by place. Bending to Test Dist. are plugs of its Advanced menu.
PSL_MODEL, PSL_SEED, PSL_SINGLE, PSL_BENDING, PSL_SEPARATION, PSL_TEST = 0, 2, 4, 6, 7, 8

# Concrete, in kN and m.
E, NU, DENSITY = 33000000.0, 0.2, 2500.0

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
RED = Color.FromArgb(50, 254, 0, 0)
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
    (left, top). A component's pivot is not its corner: its inputs hang to the left of it. Rhino
    draws the names wider than mono measures them, on both sides: the object is moved right by half
    of that, so that in Rhino it starts at left."""
    a = obj.Attributes
    b = a.Bounds
    left += (size(obj)[0] - b.Width) / 2.0
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


# How big Rhino 8 draws the components these definitions use, measured there. mono, which writes the
# file, measures Alpaca4d's components with menus far off - Principal Stress Lines 1090 x 104 rather
# than 125 x 248 - and the others' input names narrower; laid out on mono's sizes, they overlap in
# Rhino. Anything not listed is laid out as mono measures it.
RHINO_SIZES = {
    "Rectangle 3Pt": (56, 64), "Mesh Plane": (60, 64), "Line": (55, 44), "Divide Curve": (56, 64),
    "List Item": (58, 64), "Unit Z": (55, 28), "Point": (50, 24), "Deconstruct Mesh": (58, 84),
    "Deconstruct": (55, 64), "Construct Point": (59, 64), "Construct Mesh": (58, 64), "Cull Pattern": (55, 44),
    "nD": (129, 168), "Plate Fiber Section (Alpaca4d)": (127, 84), "ASD Shell (Alpaca4d)": (148, 124),
    "Support (Alpaca4d)": (141, 188), "Point Load (Alpaca4d)": (121, 64), "Mesh Load (Alpaca4d)": (101, 44),
    "Gravity Load (Alpaca4d)": (96, 28), "Load Pattern (Alpaca4d)": (176, 108),
    "AssembleModel (Alpaca4d)": (184, 124), "Analysis Settings (Alpaca4d)": (149, 184),
    " Run Analysis (Alpaca4d)": (167, 44), "Principal Stress Lines (Alpaca4d)": (125, 248),
}


def size(o):
    """An object's width and height as Rhino draws it."""
    b = o.Attributes.Bounds
    return RHINO_SIZES.get(o.Name, (b.Width, b.Height))


class Grid(object):
    """Lays objects out in columns, left to right in the order the data flows, and rows, one per
    part of the model, so that each part's chain of components sits on one line. Column widths and
    row heights are the objects' own, measured once Grasshopper has laid them out."""

    def __init__(self, doc):
        self.doc = doc
        self.items = []

    def put(self, obj, col, row=None, gap=0):
        obj.Attributes.ExpireLayout()
        obj.Attributes.PerformLayout()
        self.doc.AddObject(obj, False)
        self.items.append((obj, col, row, gap))
        return obj

    def arrange(self, x0, y0, stages=(), col_gap=60.0, row_gap=36.0, stack_gap=18.0, stage_gap=80.0):
        """Places everything; returns the bottom edge of the last row."""
        def width(o):
            return size(o)[0]

        def height(o):
            return size(o)[1]

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
        return y - row_gap


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


def faint(param):
    """Draws the wires into param faintly: for the long ones that cross the canvas."""
    param.WireDisplay = GH_ParamWireDisplay.faint
    return param


def number(param, *values):
    """Types numbers into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Number(float(v)))
    return param


def integer(param, value):
    """Types a whole number into an unwired input that takes only those, such as Mesh Plane's counts."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def boolean(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Boolean(value))
    return param


def points_into(param, values):
    param.PersistentData.Clear()
    for x, y, z in values:
        param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def points(grid, name, values, col, row=None, gap=0):
    """A Point parameter holding points typed in."""
    p = component(grid, POINT, "Point", name, col, row, gap)
    return points_into(p, values)


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


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
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


# ---------------------------------------------------------------- the studies

# Columns, in the order the data flows, and rows. Each group keeps to rows and columns of its own, so
# that no two overlap.
NODES, FACES, MESHED, GOALS, SOLVER, FORM, ELEMENT, PART, PATTERN, MODEL, LINES, PICTURE, DRAWN, HALL = range(14)
NET, PUSHED, HELD, LOADED = range(4)


def integers(param, values):
    """Types whole numbers into an unwired input, one item each."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Integer(int(v)))
    return param


def mesh(g, s, nickname):
    """The study's mesh: its nodes typed into a Point parameter, its faces into Mesh Quad and Mesh
    Triangle by their nodes' places, and Construct Mesh. Returns the objects and Construct Mesh."""
    nodes = points(g, "Nodes", s["vertices"], NODES, NET)
    faces = []
    for name, guid, items in (("Quads", MESH_QUAD, s["quads"]), ("Triangles", MESH_TRIANGLE, s["tris"])):
        if not items:
            continue
        f = component(g, guid, name, name, FACES, NET, gap=30 if faces else 0)
        for k in range(len(items[0])):
            integers(inp(f, k), [face[k] for face in items])
        faces.append(f)
    built = component(g, CONSTRUCT_MESH, "Construct Mesh", nickname, MESHED, NET)
    wire(inp(built, 0), nodes)
    flat(wire(inp(built, 1), *[out(f) for f in faces]))
    return [nodes] + faces + [built], built


def form_found(g, s, net, held):
    """Kangaroo: the net shown, its edges springs as long as they are, held, every node pushed up by
    Lift; the Zombie Solver to the end or for Iterations. Returns the objects and the form, the first
    thing out, as the net is the first goal."""
    show = component(g, SHOW, "Show", "Net", GOALS, NET)
    wire(inp(show, 0), out(net))
    edges = component(g, EDGE_LENGTHS, "EdgeLengths", "Edges", GOALS, NET, gap=30)
    wire(inp(edges, 0), out(net))
    number(inp(edges, 1), 1.0)
    number(inp(edges, 2), 1.0)
    nodes = component(g, DECONSTRUCT_MESH, "Deconstruct Mesh", "Net nodes", GOALS, PUSHED)
    wire(inp(nodes, 0), out(net))
    lift = slider(g, "Lift", 0, 3 * s["lift"], s["lift"], 4, GOALS, PUSHED, gap=30)
    up = component(g, UNIT_Z, "Unit Z", "Up", GOALS, PUSHED, gap=30)
    wire(inp(up, 0), lift)
    push = component(g, LOAD, "Load", "Push up", GOALS, PUSHED, gap=30)
    wire(inp(push, 0), out(nodes, 0))
    wire(inp(push, 1), out(up))
    anchors = component(g, ANCHOR, "Anchor", "Held", GOALS, HELD)
    faint(wire(inp(anchors, 0), held))
    number(inp(anchors, 2), 10000.0)
    iterations = slider(g, "Iterations", 1, 20000, 20000, 0, SOLVER, NET)
    zombie = component(g, ZOMBIE, "Zombie Solver", "Zombie Solver", SOLVER, NET, gap=30)
    flat(wire(inp(zombie, 0), out(show), out(edges), out(push), out(anchors)))
    wire(inp(zombie, 3), iterations)
    form = component(g, LIST_ITEM, "List Item", "Surface", FORM, NET)
    wire(inp(form, 0), out(zombie, 2))
    integer(inp(form, 1), 0)
    used = readout(g, "Iterations used", out(zombie, 0), FORM, NET, gap=30, width=80.0)
    return [show, edges, nodes, lift, up, push, anchors, iterations, zombie, form, used], form


def build(doc):
    name = System.Environment.GetEnvironmentVariable("STUDY") or shapes.STUDIES[0]
    s = shapes.study(name)
    g = Grid(doc)
    slab, kangaroo = s["kind"] == "slab", s["kind"] == "kangaroo"
    faces = len(s["quads"]) + len(s["tris"])

    # 1 - The mesh, and the nodes it is held at.
    mesh_parts, built = mesh(g, s, "Flat net" if kangaroo else "Surface")
    held = points(g, "Held", [s["vertices"][i] for i in s["held"]], NODES, HELD)
    groups = [("1 - {0}: {1}, in {2} faces".format(s["title"], s["plan"], faces), mesh_parts + [held], GREY)]

    # 2 - Kangaroo, for a form-found shell.
    surface = built
    if kangaroo:
        parts, surface = form_found(g, s, built, held)
        groups.append(("2 - Kangaroo: edges that keep their length, held, every node pushed up; the Zombie Solver", parts, GREEN))

    # 3 - A concrete shell on the surface, held, loaded, solved.
    concrete = component(g, ND_MATERIAL, "nD", "Concrete", ELEMENT, NET)
    inp(concrete, 0).PersistentData.Clear()
    inp(concrete, 0).PersistentData.Append(GH_String("Concrete"))
    number(inp(concrete, 1), E)
    number(inp(concrete, 2), E / (2.0 * (1.0 + NU)))
    number(inp(concrete, 3), NU)
    number(inp(concrete, 4), DENSITY)
    thickness = "{0:.0f} mm".format(s["thickness"] * 1000)
    section = component(g, PLATE_FIBER, "Plate Fiber Section", thickness, ELEMENT, NET, gap=30)
    number(inp(section, 1), s["thickness"])
    wire(inp(section, 2), out(concrete))
    shell = component(g, SHELL, "ASD Shell", None, PART, NET)
    wire(inp(shell, 0), out(surface))
    wire(inp(shell, 1), out(section))
    support = component(g, SUPPORT, "Support", "Built in" if s["fixed"] else "Pinned", ELEMENT, HELD)
    faint(flat(wire(inp(support, 0), held)))
    for i in range(6):
        boolean(inp(support, i + 1), s["fixed"] or i < 3)
    if s["load"] == "udl":
        load = component(g, MESH_LOAD, "Mesh Load", "{0:.0f} kN/m2".format(s["udl"]), ELEMENT, LOADED)
        inp(load, 1).PersistentData.Clear()
        inp(load, 1).PersistentData.Append(GH_Vector(Vector3d(0, 0, -s["udl"])))
    else:
        load = component(g, GRAVITY, "Gravity Load", "Own weight", ELEMENT, LOADED)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PART, LOADED)
    wire(inp(pattern, 1), out(load))
    assemble = component(g, ASSEMBLE, "Assemble Model", None, PATTERN, NET)
    flat(wire(inp(assemble, 0), out(shell)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    settings = component(g, ANALYSIS_SETTINGS, "Analysis Settings", None, PATTERN, HELD)
    run = component(g, RUN_ANALYSIS, "Run Analysis", None, MODEL, NET)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    groups += [("3 - A concrete shell, " + thickness, [concrete, section, shell], ORANGE),
               ("4 - " + s["supported"][0].upper() + s["supported"][1:], [support], GREEN),
               ("5 - " + ("{0:.0f} kN/m2".format(s["udl"]) if s["load"] == "udl" else "Its own weight"), [load, pattern], RED),
               ("6 - Assembled and solved", [assemble, settings, run], BLUE)]

    # 7 - The lines: in bending for a slab, in the membrane for a shell; from a seed off the axes,
    # turned round the middle once for each of the shape's symmetries.
    seed = points(g, "Seeds", s["seeds"], LINES, NET)
    lines = emit(STRESS_LINES, "Principal Stress Lines")
    lines.NickName = s["title"]
    open_menus(lines)
    g.put(lines, LINES, NET, gap=30)
    faint(wire(inp(lines, PSL_MODEL), out(run)))
    wire(inp(lines, PSL_SEED), seed)
    boolean(inp(lines, PSL_SINGLE), False)
    boolean(inp(lines, PSL_BENDING), slab)
    number(inp(lines, PSL_SEPARATION), s["separation"])
    number(inp(lines, PSL_TEST), 0.4 * s["separation"])
    groups.append(("7 - Principal Stress Lines, " + ("in bending" if slab else "in the membrane"), [seed, lines], PURPLE))

    # 8 - For the pictures: a slab's lines as ribs, the columns down to the floor, and a hall of copies.
    drawn = []
    lines_out = [out(lines, 0), out(lines, 1)]
    if slab:
        depth = component(g, UNIT_Z, "Unit Z", "Rib depth", PICTURE, NET)
        number(inp(depth, 0), -s["rib"])
        drawn.append(depth)
        for i in range(2):
            smooth = component(g, FIT_CURVE, "Fit Curve", "Smooth SL" + str(i + 1), PICTURE, NET, gap=30)
            wire(inp(smooth, 0), lines_out[i])
            integer(inp(smooth, 1), 3)
            number(inp(smooth, 2), 0.005)
            ribs = component(g, EXTRUDE, "Extrude", "Ribs SL" + str(i + 1), DRAWN, NET, gap=30 if i else 0)
            wire(inp(ribs, 0), out(smooth))
            wire(inp(ribs, 1), out(depth))
            drawn += [smooth, ribs]
            lines_out[i] = out(ribs)
    shafts = None
    if s["columns"]:
        tops = points(g, "Column tops", [(x, y, 0.0) for x, y in s["columns"]], PICTURE, NET, gap=30)
        down = component(g, UNIT_Z, "Unit Z", "Down the columns", PICTURE, NET, gap=30)
        number(inp(down, 0), -1.0)
        axes = component(g, LINE_SDL, "Line SDL", "Column axes", PICTURE, NET, gap=30)
        wire(inp(axes, 0), tops)
        wire(inp(axes, 1), out(down))
        number(inp(axes, 2), s["height"])
        shafts = component(g, PIPE, "Pipe", "Column shafts", DRAWN, NET, gap=30)
        wire(inp(shafts, 0), out(axes))
        number(inp(shafts, 1), s["column_radius"])
        if shafts.Params.Input.Count > 2:
            integer(inp(shafts, 2), 1)
        drawn += [tops, down, axes, shafts]
    xs = [v[0] + c[0] for v in s["vertices"] for c in (s["copies"][0], s["copies"][-1])]
    ys = [v[1] + c[1] for v in s["vertices"] for c in (s["copies"][0], s["copies"][-1])]
    reach = 0.5 * max(max(xs) - min(xs), max(ys) - min(ys))
    level = -s["height"] if s["height"] else min(v[2] for v in s["vertices"])
    outline = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Floor outline", PICTURE, NET, gap=30)
    points_into(inp(outline, 0), [(min(xs) - reach, min(ys) - reach, level)])
    points_into(inp(outline, 1), [(max(xs) + reach, min(ys) - reach, level)])
    points_into(inp(outline, 2), [(max(xs) + reach, max(ys) + reach, level)])
    floor = component(g, MESH_PLANE, "Mesh Plane", "Floor", DRAWN, NET, gap=30)
    wire(inp(floor, 0), out(outline))
    integer(inp(floor, 1), 1)
    integer(inp(floor, 2), 1)
    drawn += [outline, floor]
    if len(s["copies"]) > 1:
        places = points(g, "Hall places", [(x, y, 0.0) for x, y in s["copies"]], PICTURE, NET, gap=30)
        drawn.append(places)
        hall = [("Hall SL1", lines_out[0]), ("Hall SL2", lines_out[1]), ("Hall surface", out(surface))]
        if shafts is not None:
            hall.append(("Hall columns", out(shafts)))
        for k, (nick, source) in enumerate(hall):
            m = component(g, MOVE, "Move", nick, HALL, NET, gap=30 if k else 0)
            flat(faint(wire(inp(m, 0), source)))
            wire(inp(m, 1), places).DataMapping = GH_DataMapping.Graft
            drawn.append(m)
    what = ["the lines as ribs {0:.0f} mm deep".format(s["rib"] * 1000)] if slab else []
    what += (["the columns"] if s["columns"] else []) + ["the floor"] + (["a hall of copies"] if len(s["copies"]) > 1 else [])
    groups.append(("8 - For the pictures: " + ", ".join(what), drawn, GREY))

    g.arrange(0, 0, stages=(GOALS, ELEMENT, PATTERN, LINES, PICTURE), row_gap=60.0, stack_gap=30.0)
    for title, objects, colour in groups:
        group(doc, title, objects, colour)
    # Only the lines draw: the scenes add their own previews.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden"):
            obj.Hidden = obj.ComponentGuid != System.Guid(STRESS_LINES)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()


if __name__ == "__main__":
    main()
