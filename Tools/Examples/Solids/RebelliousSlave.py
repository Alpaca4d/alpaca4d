#! python3
# The tetrahedron example in marble: Michelangelo's Rebellious Slave, 2.13 m tall on its plinth, in
# twenty eight thousand tetrahedra - how its own weight comes down through it, and how it sways.
#
# make.sh, in the folder above, writes RebelliousSlave.gh straight from this script, without Rhino. It also runs
# in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this file
# and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# The Rebellious Slave is Michelangelo's, carved about 1513 for the tomb of Pope Julius II, now in the
# Louvre. The shape is the SMK's (Statens Museum for Kunst, Copenhagen) scan of its plaster cast KAS86,
# in the public domain. Grasshopper has no tetrahedral mesher, so the tetrahedra come in as a
# tetrahedral mesher hands them out, nodes and four node numbers per tetrahedron, read from
# RebelliousSlave.txt next to this script and kept in the definition. RebelliousSlaveMesh.py made
# that file with gmsh.
#
# The elements are Ten Node Tetrahedra, which bend, and need an OpenSees built from the master branch
# since 16 October 2026. The plinth is held at every node of its underside, edge nodes included.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Special import GH_Group, GH_Panel, GH_BooleanToggle, GH_NumberSlider
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_MeshFace, GH_Colour
from Rhino.Geometry import Point3d, MeshFace
from System.Drawing import PointF, RectangleF, Color
from System.Reflection import BindingFlags

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
INTEGER = "2E3AB970-8545-46BB-836C-1C11E5610BCE"            # Params > Primitive > Integer
MESH_FACE = "E02B3DA5-543A-46AC-A867-0BA6B0A524DE"          # Params > Geometry > Mesh Face
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
SMALLER_THAN = "AE840986-CADE-4E5A-96B0-570F007D4FC0"       # Maths > Operators > Smaller Than
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern
CULL_DUPLICATES = "6EAFFBB2-3392-441A-8556-2DC126AA8910"    # Vector > Point > Cull Duplicates

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
TEN_NODE = "1F38CDFC-F909-4F3F-969E-9E2C63F9A321"           # Ten Node Tetrahedron
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NATURAL_VIBRATION = "3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3"  # Natural Vibration Analysis
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)

DATA = "RebelliousSlave.txt"
GROUND = 0.001              # m: a node below this stands on the ground
# The four faces of a tetrahedron, by its corners. Ten Node Tetrahedron reads only the corners and
# puts them in order itself, so any four faces would do; these make a closed mesh.
FACES = ((0, 1, 2), (0, 3, 1), (1, 3, 2), (2, 3, 0))
# Model View's results, from 1: Displacement, Beam forces, Beam stresses, Shell forces, Shell
# stresses, Brick stresses, Reactions; and the brick stresses: s11, s22, s33, s12, s23, s13, Von Mises;
# and the displacements: Magnitude, Ux, Uy, Uz.
BRICK_STRESSES, SIGMA_33 = 6, 2
DISPLACEMENT, MAGNITUDE = 1, 0
# The range the colours are stretched over, in MPa: from the compression in the legs to none, in eight
# bands of 0.05 MPa. Fitted to what is drawn it would run from -0.7 MPa, at a few nodes where the
# plinth is held, to +0.3, and leave the rest of the statue one colour.
RANGE = "-0.4 To 0"
MODES = 6                   # how many modes to solve for
# A mode is read with its largest translation 1, and Model View draws it at scale 1 with its largest
# movement 8% of the statue's diagonal, 0.19 m.
DEFORMATION_SCALE = 1.0

# Alpaca4d's palette: the stress from the most compression, orange, to none, a tint of the light blue
# dark enough for the statue to stand out on a white page; the modes from still, the same tint, to
# where the statue moves most.
STRESS_COLOURS = ["#EB6C3F", "#9E35DA", "#4288F7", "#B9D0F6"]
MODE_COLOURS = ["#B9D0F6", "#4288F7", "#9E35DA", "#EB6C3F"]


def colours(param, stops, count):
    """A colour scale typed into an unwired input: count colours spread evenly between the stops."""
    rgb = [tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) for h in stops]
    param.PersistentData.Clear()
    for k in range(count):
        t = k / float(count - 1) * (len(rgb) - 1)
        i = min(int(t), len(rgb) - 2)
        f = t - i
        c = [int(round(rgb[i][j] + (rgb[i + 1][j] - rgb[i][j]) * f)) for j in range(3)]
        param.PersistentData.Append(GH_Colour(Color.FromArgb(255, c[0], c[1], c[2])))
    return param


def read_tetrahedra():
    """The nodes, as (x, y, z), and the tetrahedra, as four node numbers from 0, of DATA."""
    # System.IO rather than os, which the IronPython make.sh runs this in does not have.
    here = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
    lines = System.IO.File.ReadAllText(System.IO.Path.Combine(here, DATA)).split("\n")
    count = int(lines[0].split()[1])
    nodes = [tuple(float(v) for v in line.split()) for line in lines[1:1 + count]]
    tets = int(lines[1 + count].split()[1])
    tetrahedra = [tuple(int(v) for v in line.split()) for line in lines[2 + count:2 + count + tets]]
    return nodes, tetrahedra


def volume(nodes, tetrahedra):
    """The volume the tetrahedra fill."""
    total = 0.0
    for tet in tetrahedra:
        a, b, c, d = [nodes[n] for n in tet]
        u, v, w = [[p[i] - a[i] for i in range(3)] for p in (b, c, d)]
        total += abs(u[0] * (v[1] * w[2] - v[2] * w[1]) - u[1] * (v[0] * w[2] - v[2] * w[0]) +
                     u[2] * (v[0] * w[1] - v[1] * w[0])) / 6.0
    return total


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


def toggle(grid, name, value, col, row=None, gap=0):
    t = GH_BooleanToggle()
    t.CreateAttributes()
    t.NickName = name
    t.Value = value
    return grid.put(t, col, row, gap)



def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a material's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
    return p


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


def slider(grid, name, lo, hi, value, col, row=None, gap=0):
    """A whole number slider."""
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    s.Slider.Type = GH_SliderAccuracy.Integer
    s.Slider.DecimalPlaces = 0
    s.Slider.Minimum = System.Decimal(float(lo))
    s.Slider.Maximum = System.Decimal(float(hi))
    s.Slider.Value = System.Decimal(float(value))
    return grid.put(s, col, row, gap)


def widget(obj, field):
    """One of an Alpaca4d component's menu controls, which it keeps to itself."""
    return obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj)


def show_result(view, show, component):
    """Ticks one of Model View's results alone, at a component, as clicks in its Results menu do.
    Its menus then show the controls its settings have a use for and put the rest away, and it is laid
    out again: the grid measures it at the height it opens at. Set its other widgets first."""
    flags = BindingFlags.NonPublic | BindingFlags.Instance
    view.GetType().GetMethod("ShowResult", flags).Invoke(view, System.Array[System.Object]([show, component]))
    view.Attributes.ExpireLayout()
    view.Attributes.PerformLayout()


# ---------------------------------------------------------------- the slave

# Columns, in the order the data flows...
DATA_, CORNERS, MESHES, NAMES, MATERIAL, ELEMENTS, HEIGHT, TEST, KEEP, UNIQUE, LOADS = range(11)
# ...and rows, one per part of the model.
TETS, SOLES, WEIGHT = range(3)

# The model and its analysis, in a grid of their own to the right.
MODEL, VIEW, SHOWN = range(3)
ASSEMBLED, ANALYSED, MODAL = range(3)


def build(doc):
    nodes, tetrahedra = read_tetrahedra()
    g = Grid(doc)

    # 1 - The slave in tetrahedra, as a tetrahedral mesher hands them out: the nodes, and four node
    # numbers per tetrahedron, one branch each. Each tetrahedron's corners made into a mesh.
    node_points = component(g, POINT, "Point", "Nodes", DATA_, TETS)
    node_points.PersistentData.Clear()
    for x, y, z in nodes:
        node_points.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    corner_numbers = component(g, INTEGER, "Integer", "Tetrahedra", DATA_, TETS)
    corner_numbers.PersistentData.Clear()
    for i, tet in enumerate(tetrahedra):
        path = GH_Path(i)
        for n in tet:
            corner_numbers.PersistentData.Append(GH_Integer(n), path)
    faces = component(g, MESH_FACE, "Mesh Face", "Four faces", DATA_, TETS)
    faces.PersistentData.Clear()
    for a, b, c in FACES:
        faces.PersistentData.Append(GH_MeshFace(MeshFace(a, b, c)))

    corners = component(g, LIST_ITEM, "List Item", "Corners", CORNERS, TETS)
    wire(inp(corners, 0), node_points)
    wire(inp(corners, 1), corner_numbers)
    tet_meshes = component(g, CONSTRUCT_MESH, "Construct Mesh", "Tetrahedra", MESHES, TETS)
    wire(inp(tet_meshes, 0), out(corners))
    wire(inp(tet_meshes, 1), faces)

    # 2 - Carrara marble, and the ten node tetrahedra.
    concrete_values = [text(g, "Marble", "Carrara marble", NAMES, TETS),
                       text(g, "E [kN/m2]", "50000000", NAMES, TETS),
                       text(g, "G [kN/m2]", "20000000", NAMES, TETS),
                       text(g, "Poisson", "0.25", NAMES, TETS),
                       text(g, "Density [kg/m3]", "2700", NAMES, TETS)]
    concrete = component(g, ND_MATERIAL, "nD", "Marble", MATERIAL, TETS)
    for i, source in enumerate(concrete_values):
        wire(inp(concrete, i), source)
    ten = component(g, TEN_NODE, "Ten Node Tetrahedron", None, ELEMENTS, TETS)
    wire(inp(ten, 0), out(tet_meshes))
    wire(inp(ten, 1), out(concrete))

    # 3 - Held at every node on the underside of the plinth: every element's ten from its Points output, the edge
    # nodes with the corners, in one list; those below GROUND, once each.
    model_nodes = component(g, POINT, "Point", "Element nodes", ELEMENTS, SOLES)
    flat(wire(model_nodes, out(ten, 1)))
    height = component(g, DECONSTRUCT_POINT, "Deconstruct", "Height", HEIGHT, SOLES)
    wire(inp(height, 0), model_nodes)
    on_ground = component(g, SMALLER_THAN, "Smaller Than", "On the ground", TEST, SOLES)
    wire(inp(on_ground, 0), out(height, 2))
    number(inp(on_ground, 1), GROUND)
    soles = component(g, CULL_PATTERN, "Cull Pattern", "Sole nodes", KEEP, SOLES)
    wire(inp(soles, 0), model_nodes)
    wire(inp(soles, 1), out(on_ground))
    sole_nodes = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", UNIQUE, SOLES)
    wire(inp(sole_nodes, 0), out(soles))
    number(inp(sole_nodes, 1), GROUND)
    held = toggle(g, "Held", True, UNIQUE, SOLES)
    free = toggle(g, "Free", False, UNIQUE, SOLES)
    support = component(g, SUPPORT, "Support", "Held plinth", LOADS, SOLES)
    wire(inp(support, 0), out(sole_nodes))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    # 4 - Its own weight.
    gravity = component(g, GRAVITY, "Gravity Load", None, UNIQUE, WEIGHT)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, LOADS, WEIGHT)
    flat(wire(inp(pattern, 1), out(gravity)))

    right = g.arrange(0, 0, stages=(NAMES, HEIGHT, LOADS))

    # 5 - Assemble, solve, and the vertical stress drawn over the statue.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(ten)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    # Nodes closer than this are welded into one: the 1 cm default is half an element here, and would
    # weld an edge's middle node to its corners.
    number(inp(assemble, 5), 0.0001)
    view = component(m, MODEL_VIEW, "Model View", None, VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    run = component(m, RUN_ANALYSIS, "Run Analysis", None, MODEL, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    results = component(m, MODEL_VIEW, "Model View", "Vertical stress", VIEW, ANALYSED)
    wire(inp(results, 0), out(run))
    colours(inp(results, 3), STRESS_COLOURS, 8)
    widget(results, "_ckExtruded").Active = False
    widget(results, "_ckReleases").Active = False
    widget(results, "_ckBands").Active = True
    # With the elements' edges, so that the tetrahedra show: without them the statue reads as a scan,
    # not as a model. Display settings' Element edges colour softens them on 28 000 tetrahedra.
    widget(results, "_ckWires").Active = True
    show_result(results, BRICK_STRESSES, SIGMA_33)
    stress_range = text(m, "Range [MPa]", RANGE, MODEL, ANALYSED, width=110.0)
    if RANGE:
        wire(inp(results, 4), stress_range)
    legend = component(m, LEGEND, "Legend", "Vertical stress legend", SHOWN, ANALYSED)
    legend_title = text(m, "Title", "Vertical stress [MPa]", SHOWN, ANALYSED, width=140.0)
    wire(inp(legend, 0), legend_title)
    wire(inp(legend, 1), out(results, 3))
    wire(inp(legend, 2), out(results, 4))
    info = readout(m, "What is drawn", out(results, 2), SHOWN, ANALYSED, width=300.0, height=40.0)
    log = readout(m, "OpenSees log", out(run, 1), SHOWN, ANALYSED, width=300.0, height=140.0)

    # 6 - The first MODES modes, and the one Mode picks drawn over the statue.
    vibration = component(m, NATURAL_VIBRATION, "Natural Vibration Analysis", None, MODEL, MODAL)
    wire(inp(vibration, 0), out(assemble))
    inp(vibration, 1).PersistentData.Clear()
    inp(vibration, 1).PersistentData.Append(GH_Integer(MODES))
    mode = slider(m, "Mode, from 0", 0, MODES - 1, 0, MODEL, MODAL)
    modal = component(m, MODEL_VIEW, "Model View", "Mode shape", VIEW, MODAL)
    wire(inp(modal, 0), out(vibration))
    wire(inp(modal, 1), mode)
    colours(inp(modal, 3), MODE_COLOURS, 9)
    widget(modal, "_ckDeformed").Active = True
    widget(modal, "_slDeformScale").Value = DEFORMATION_SCALE
    widget(modal, "_ckExtruded").Active = False
    widget(modal, "_ckReleases").Active = False
    widget(modal, "_ckWires").Active = True
    show_result(modal, DISPLACEMENT, MAGNITUDE)
    # A mode's size is its own: read with its largest translation 1, in m, its legend runs 0 to 1.
    unit = modal.GetType().GetMethod("UnitOf", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(
        modal, System.Array[System.Object]([DISPLACEMENT, MAGNITUDE]))
    unit.Value = 2
    mode_legend = component(m, LEGEND, "Legend", "Mode shape legend", SHOWN, MODAL)
    mode_title = text(m, "Mode title", "Mode shape, largest movement 1", SHOWN, MODAL, width=180.0)
    wire(inp(mode_legend, 0), mode_title)
    wire(inp(mode_legend, 1), out(modal, 3))
    wire(inp(mode_legend, 2), out(modal, 4))
    frequencies = readout(m, "Frequencies [Hz]", out(vibration, 3), SHOWN, MODAL, width=160.0, height=130.0)

    m.arrange(right + 160, 0)

    group(doc, "1 - The Rebellious Slave in tetrahedra: nodes, and four nodes per tetrahedron",
          [node_points, corner_numbers, faces, corners, tet_meshes], ORANGE)
    group(doc, "2 - Marble, and Ten Node Tetrahedra: they need an OpenSees from master",
          [concrete, ten] + concrete_values, PURPLE)
    group(doc, "3 - Held at every node under the plinth, edge nodes included",
          [model_nodes, height, on_ground, soles, sole_nodes, held, free, support], GREEN)
    group(doc, "4 - Its own weight", [gravity, pattern], RED)
    group(doc, "5 - Assemble Model, solve, and the vertical stress over the statue",
          [assemble, view, settings, run, stress_range, results, legend, legend_title, info, log], BLUE)
    group(doc, "6 - Its first six modes, and the one picked drawn over the statue",
          [vibration, mode, modal, mode_legend, mode_title, frequencies], GREY)

    # Only the stress view draws the statue; the other views and the construction geometry stay out
    # of the viewport. Show the mode's preview, and hide the stress's, to see the statue sway.
    shown = (results.InstanceGuid, legend.InstanceGuid)
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in shown:
            obj.Hidden = True

    note(doc, "The Rebellious Slave",
         "Michelangelo's Rebellious Slave, in tetrahedra.\n\n"
         "A marble figure 2.13 m tall on its plinth, under its own weight: {0} tetrahedra on {1} corners, "
         "{2:.2f} m3 or {3:.0f} kg. Michelangelo carved it about 1513 for the tomb of Pope Julius II; it is in the "
         "Louvre. The shape is the SMK's (Statens Museum for Kunst) scan of its plaster cast KAS86, in the "
         "public domain.\n\n"
         "1. Grasshopper has no tetrahedral mesher, so the tetrahedra come as a mesher writes them: "
         "the nodes, and four node numbers per tetrahedron. List Item and Construct Mesh make each "
         "into a four vertex mesh. They were made with gmsh; TetGen, gmsh or any other mesher's "
         "output goes in the same way.\n"
         "2. Ten Node Tetrahedron: four corners and a node in the middle of each edge, so it bends. "
         "It needs an OpenSees built from the master branch since 16 October 2026 (Alpaca4d > "
         "Settings > Set OpenSees Executable).\n"
         "3. A support holds only the node at its point, so the plinth is held at every node of its "
         "underside, the edge nodes too: Points gives every element's ten.\n"
         "4. Its own weight, from the marble's density.\n"
         "5. The vertical stress, in iso-surfaces of 0.05 MPa, over -0.4 to 0: its weight comes down through the "
         "legs and the rough stone behind them into the plinth, in compression. Clear Range to see all of it, and "
         "pick another result from the Results menu.\n"
         "6. Its first six modes: Mode picks one, 0 being the first. Show that Model View's preview, and "
         "hide the stress's, to see it.".format(len(tetrahedra), len(nodes), volume(nodes, tetrahedra),
                                                 2700 * volume(nodes, tetrahedra)),
         0, -420, 640, 350)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The Rebellious Slave built.")


if __name__ == "__main__":
    main()
