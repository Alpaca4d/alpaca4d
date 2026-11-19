#! python3
# The tetrahedron example on a bone: a femur held at the knee, with the force of the hip on its head,
# in tetrahedra Mesh Solid makes from its surface.
#
# make.sh, in the folder above, writes Femur.gh straight from this script, without Rhino. It also runs in Rhino
# with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this file and press
# Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# The bone is the Standardised Femur, the surface of a composite femur put forward by Viceconti et al.
# (J. Biomech. 1996) as the reference geometry for finite element studies of the femur, published by
# the University of Sheffield under CC BY 4.0. Its closed surface - the outer faces of the
# tetrahedra in Femur.txt next to this script, which FemurMesh.py made with gmsh - is kept in the
# definition as nodes and triangles. Mesh Solid fills it with tetrahedra there, finer along the neck,
# where the bone bends most; it runs gmsh, which each user installs.
#
# The elements are Ten Node Tetrahedra, which bend, and need an OpenSees built from the master branch
# since 16 October 2026. Every node in the bottom 2 cm is held, as a bone potted for a test is; the
# force is shared among the nodes on the top of the head. A Natural Vibration Analysis gives its
# modes.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Special import GH_Group, GH_Panel, GH_BooleanToggle, GH_NumberSlider
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Boolean, GH_Point, GH_MeshFace, GH_Colour
from Rhino.Geometry import Point3d, MeshFace
from System.Drawing import PointF, RectangleF, Color
from System.Reflection import BindingFlags

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
MESH_FACE = "E02B3DA5-543A-46AC-A867-0BA6B0A524DE"          # Params > Geometry > Mesh Face
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
LIST_LENGTH = "1817FD29-20AE-4503-B542-F0FB651E67D7"        # Sets > List > List Length
SORT_LIST = "6F93D366-919F-4DDA-A35E-BA03DD62799B"          # Sets > List > Sort List
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
DISTANCE = "93B8E93D-F932-402C-B435-84BE04D87666"           # Vector > Point > Distance
SMALLER_THAN = "AE840986-CADE-4E5A-96B0-570F007D4FC0"       # Maths > Operators > Smaller Than
DIVISION = "9C85271F-89FA-4E9F-9F4A-D75802120CCC"           # Maths > Operators > Division
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern
CULL_DUPLICATES = "6EAFFBB2-3392-441A-8556-2DC126AA8910"    # Vector > Point > Cull Duplicates
VECTOR_XYZ = "56B92EAB-D121-43F7-94D3-6CD8F0DDEAD8"         # Vector > Vector > Vector XYZ
MULTIPLY = "63FFF845-7C61-4DFB-BA12-44D481B4BF0F"           # Vector > Vector > Multiply

# Alpaca4d
MESH_SOLID = "8C2E4A71-5B9D-4F36-A1E8-2D7C6B3F9E05"         # Mesh Solid
MESH_REFINEMENT = "3F6B2C1E-8D4A-4E7B-9C2F-5A1D7E8B4C63"    # Mesh Refinement
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
TEN_NODE = "1F38CDFC-F909-4F3F-969E-9E2C63F9A321"           # Ten Node Tetrahedron
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
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

DATA = "Femur.txt"
HELD = 0.02                 # m: every node lower than this is held, as in a pot of cement
HEAD = 0.015                # m: the force is shared among the nodes this close to the top of the head
# The hip's force on the head when walking, about two and a half times the weight of an 80 kg
# person: 2 kN, leaning 13 degrees from the shaft, pushing the head down and out (+X: the head is on
# the -X side of the shaft).
HIP_FORCE = (0.45, 0.0, -1.949)
TOLERANCE = 0.0001          # m: two points nearer than this are one node
SIZE = 0.008                # m: the tetrahedra's size
# The femoral neck, from the middle of the head down to where it meets the shaft: the tetrahedra are
# NECK_SIZE along it, growing back to SIZE by NECK_REACH from it.
NECK = ((-0.045, -0.004, 0.427), (-0.005, -0.004, 0.395))
NECK_SIZE, NECK_REACH = 0.003, 0.03
MODES = 6                   # how many modes to solve for
MODE_COLOURS = ["#B9D0F6", "#4288F7", "#9E35DA", "#EB6C3F"]
# Model View's results, from 1: Displacement, Beam forces, Beam stresses, Shell forces, Shell
# stresses, Brick stresses, Reactions; and the displacements: Magnitude, Ux, Uy, Uz.
DISPLACEMENT, MAGNITUDE = 1, 0
DEFORMATION_SCALE = 20.0


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


def outer_surface(nodes, tetrahedra):
    """The faces of the tetrahedra that only one of them has - the bone's closed surface - each turned
    out, and the nodes they use, numbered afresh from 0."""
    faces = {}
    for a, b, c, d in tetrahedra:
        p, q, r, s_ = [nodes[n] for n in (a, b, c, d)]
        u, v, w = [[x[i] - p[i] for i in range(3)] for x in (q, r, s_)]
        if u[0] * (v[1] * w[2] - v[2] * w[1]) - u[1] * (v[0] * w[2] - v[2] * w[0]) + u[2] * (v[0] * w[1] - v[1] * w[0]) < 0:
            b, c = c, b
        for face in ((a, c, b), (a, d, c), (a, b, d), (b, c, d)):
            key = tuple(sorted(face))
            faces[key] = None if key in faces else face
    outer = [f for f in faces.values() if f is not None]
    used = {}
    for f in outer:
        for n in f:
            if n not in used:
                used[n] = len(used)
    points = [None] * len(used)
    for n, i in used.items():
        points[i] = nodes[n]
    return points, [tuple(used[n] for n in f) for f in outer]


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


# ---------------------------------------------------------------- the femur

# Columns, in the order the data flows...
DATA_, SURFACE, MESHES, NAMES, MATERIAL, ELEMENTS, HEIGHT, TEST, KEEP, UNIQUE, SHARE, LOADS, PATTERN = range(13)
# ...and rows, one per part of the model.
TETS, KNEE, HIP = range(3)

# The model and its analysis, in a grid of their own to the right.
MODEL, VIEW, SHOWN = range(3)
ASSEMBLED, ANALYSED, MODAL = range(3)


def build(doc):
    nodes, tetrahedra = read_tetrahedra()
    surface_nodes, surface_triangles = outer_surface(nodes, tetrahedra)
    g = Grid(doc)

    # 1 - The femur's closed surface, as nodes and triangles, made into a mesh; Mesh Solid fills it with
    # tetrahedra of SIZE, NECK_SIZE along the neck.
    node_points = component(g, POINT, "Point", "Surface nodes", DATA_, TETS)
    node_points.PersistentData.Clear()
    for x, y, z in surface_nodes:
        node_points.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    faces = component(g, MESH_FACE, "Mesh Face", "Surface triangles", DATA_, TETS)
    faces.PersistentData.Clear()
    for a, b, c in surface_triangles:
        faces.PersistentData.Append(GH_MeshFace(MeshFace(a, b, c)))
    surface = component(g, CONSTRUCT_MESH, "Construct Mesh", "The bone's surface", SURFACE, TETS)
    wire(inp(surface, 0), node_points)
    wire(inp(surface, 1), faces)

    neck_ends = component(g, POINT, "Point", "Neck", DATA_, TETS)
    neck_ends.PersistentData.Clear()
    for x, y, z in NECK:
        neck_ends.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    start = component(g, LIST_ITEM, "List Item", "Head", SURFACE, TETS)
    wire(inp(start, 0), neck_ends)
    end = component(g, LIST_ITEM, "List Item", "Shaft", SURFACE, TETS)
    wire(inp(end, 0), neck_ends)
    inp(end, 1).PersistentData.Clear()
    inp(end, 1).PersistentData.Append(GH_Integer(1))
    neck = component(g, LINE, "Line", "Neck", SURFACE, TETS)
    wire(inp(neck, 0), out(start))
    wire(inp(neck, 1), out(end))
    refinement = component(g, MESH_REFINEMENT, "Mesh Refinement", "Finer along the neck", MESHES, TETS)
    wire(inp(refinement, 0), out(neck))
    number(inp(refinement, 1), NECK_SIZE)
    number(inp(refinement, 2), NECK_REACH)
    tet_meshes = component(g, MESH_SOLID, "Mesh Solid", "Tetrahedra", MESHES, TETS)
    wire(inp(tet_meshes, 0), out(surface))
    number(inp(tet_meshes, 1), SIZE)
    wire(inp(tet_meshes, 2), out(refinement))

    # 2 - Bone, and the ten node tetrahedra.
    bone_values = [text(g, "Bone", "Cortical bone", NAMES, TETS),
                   text(g, "E [kN/m2]", "17000000", NAMES, TETS),
                   text(g, "G [kN/m2]", "6540000", NAMES, TETS),
                   text(g, "Poisson", "0.3", NAMES, TETS),
                   text(g, "Density [kg/m3]", "1900", NAMES, TETS)]
    bone = component(g, ND_MATERIAL, "nD", "Bone", MATERIAL, TETS)
    for i, source in enumerate(bone_values):
        wire(inp(bone, i), source)
    ten = component(g, TEN_NODE, "Ten Node Tetrahedron", None, ELEMENTS, TETS)
    wire(inp(ten, 0), out(tet_meshes))
    wire(inp(ten, 1), out(bone))

    # 3 - Held at the knee: every node in the bottom HELD - every element's ten from its Points
    # output, the edge nodes with the corners - once each.
    model_nodes = component(g, POINT, "Point", "Element nodes", ELEMENTS, KNEE)
    flat(wire(model_nodes, out(ten, 1)))
    height = component(g, DECONSTRUCT_POINT, "Deconstruct", "Height", HEIGHT, KNEE)
    wire(inp(height, 0), model_nodes)
    low = component(g, SMALLER_THAN, "Smaller Than", "Bottom 2 cm", TEST, KNEE)
    wire(inp(low, 0), out(height, 2))
    number(inp(low, 1), HELD)
    knee = component(g, CULL_PATTERN, "Cull Pattern", "Knee nodes", KEEP, KNEE)
    wire(inp(knee, 0), model_nodes)
    wire(inp(knee, 1), out(low))
    knee_nodes = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", UNIQUE, KNEE)
    wire(inp(knee_nodes, 0), out(knee))
    number(inp(knee_nodes, 1), TOLERANCE)
    held = toggle(g, "Held", True, SHARE, KNEE)
    free = toggle(g, "Free", False, SHARE, KNEE)
    support = component(g, SUPPORT, "Support", "Held at the knee", LOADS, KNEE)
    wire(inp(support, 0), out(knee_nodes))
    for i in (1, 2, 3):
        wire(inp(support, i), held)
    for i in (4, 5, 6):
        wire(inp(support, i), free)

    # 4 - The hip's force, shared among the nodes within HEAD of the top of the head.
    by_height = component(g, SORT_LIST, "Sort List", "By height", HEIGHT, HIP)
    faint(wire(inp(by_height, 0), out(height, 2)))
    faint(wire(inp(by_height, 1), model_nodes))
    top = component(g, LIST_ITEM, "List Item", "Top of the head", TEST, HIP)
    wire(inp(top, 0), out(by_height, 1))
    # The last, by wrapping round from -1.
    inp(top, 1).PersistentData.Clear()
    inp(top, 1).PersistentData.Append(GH_Integer(-1))
    inp(top, 2).PersistentData.Clear()
    inp(top, 2).PersistentData.Append(GH_Boolean(True))
    away = component(g, DISTANCE, "Distance", "From the top", KEEP, HIP)
    faint(wire(inp(away, 0), model_nodes))
    wire(inp(away, 1), out(top))
    close = component(g, SMALLER_THAN, "Smaller Than", "Within 15 mm", UNIQUE, HIP)
    wire(inp(close, 0), out(away))
    number(inp(close, 1), HEAD)
    head = component(g, CULL_PATTERN, "Cull Pattern", "Head nodes", SHARE, HIP)
    faint(wire(inp(head, 0), model_nodes))
    wire(inp(head, 1), out(close))
    head_nodes = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", LOADS, HIP)
    wire(inp(head_nodes, 0), out(head))
    number(inp(head_nodes, 1), TOLERANCE)
    count = component(g, LIST_LENGTH, "List Length", "How many", PATTERN, HIP)
    wire(inp(count, 0), out(head_nodes))
    share = component(g, DIVISION, "Division", "Share", PATTERN, HIP)
    number(inp(share, 0), 1)
    wire(inp(share, 1), out(count))
    hip = component(g, VECTOR_XYZ, "Vector XYZ", "Hip force, 2 kN", PATTERN, HIP)
    for i, value in enumerate(HIP_FORCE):
        number(inp(hip, i), value)
    each = component(g, MULTIPLY, "Multiply", "On each node", PATTERN, HIP)
    wire(inp(each, 0), out(hip))
    wire(inp(each, 1), out(share))
    load = component(g, POINT_LOAD, "Point Load", "Hip", PATTERN + 1, HIP)
    wire(inp(load, 0), out(head_nodes))
    wire(inp(load, 1), out(each))
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN + 2, HIP)
    flat(wire(inp(pattern, 1), out(load)))

    right = g.arrange(0, 0, stages=(NAMES, HEIGHT, LOADS))

    # 5 - Assemble, solve, and the displacement drawn over the bone, deformed.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(ten)))
    flat(wire(inp(assemble, 1), out(support)))
    flat(wire(inp(assemble, 2), out(pattern)))
    # Smaller than the tetrahedra: the default, 1 cm, would weld their nodes together.
    number(inp(assemble, 5), TOLERANCE)
    view = component(m, MODEL_VIEW, "Model View", None, VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    run = component(m, RUN_ANALYSIS, "Run Analysis", None, MODEL, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    results = component(m, MODEL_VIEW, "Model View", None, VIEW, ANALYSED)
    wire(inp(results, 0), out(run))
    widget(results, "_ckDeformed").Active = True
    widget(results, "_slDeformScale").Value = DEFORMATION_SCALE
    widget(results, "_ckExtruded").Active = False
    widget(results, "_ckReleases").Active = False
    show_result(results, DISPLACEMENT, MAGNITUDE)
    legend = component(m, LEGEND, "Legend", None, SHOWN, ANALYSED)
    legend_title = text(m, "Title", "Displacement [mm]", SHOWN, ANALYSED, width=140.0)
    wire(inp(legend, 0), legend_title)
    wire(inp(legend, 1), out(results, 3))
    wire(inp(legend, 2), out(results, 4))
    info = readout(m, "What is drawn", out(results, 2), SHOWN, ANALYSED, width=300.0, height=40.0)
    log = readout(m, "OpenSees log", out(run, 1), SHOWN, ANALYSED, width=300.0, height=140.0)

    # 7 - The first MODES modes, and the one Step picks drawn over the bone.
    vibration = component(m, NATURAL_VIBRATION, "Natural Vibration Analysis", None, MODEL, MODAL)
    wire(inp(vibration, 0), out(assemble))
    inp(vibration, 1).PersistentData.Clear()
    inp(vibration, 1).PersistentData.Append(GH_Integer(MODES))
    mode = slider(m, "Step, the mode from 0", 0, MODES - 1, 0, MODEL, MODAL)
    modal = component(m, MODEL_VIEW, "Model View", "Mode shape", VIEW, MODAL)
    wire(inp(modal, 0), out(vibration))
    wire(inp(modal, 1), mode)
    colours(inp(modal, 3), MODE_COLOURS, 9)
    widget(modal, "_ckDeformed").Active = True
    widget(modal, "_ckExtruded").Active = False
    widget(modal, "_ckReleases").Active = False
    show_result(modal, DISPLACEMENT, MAGNITUDE)
    frequencies = readout(m, "Frequencies [Hz]", out(vibration, 3), SHOWN, MODAL, width=160.0, height=130.0)

    m.arrange(right + 160, 0)

    group(doc, "1 - The femur's surface, filled with tetrahedra by Mesh Solid, finer along the neck",
          [node_points, faces, surface, neck_ends, start, end, neck, refinement, tet_meshes], ORANGE)
    group(doc, "2 - Bone, and Ten Node Tetrahedra: they need an OpenSees from master",
          [bone, ten] + bone_values, PURPLE)
    group(doc, "3 - Held at the knee: every node in the bottom 2 cm, edge nodes included",
          [model_nodes, height, low, knee, knee_nodes, held, free, support], GREEN)
    group(doc, "4 - The hip's force, 2 kN, shared among the nodes on the top of the head",
          [by_height, top, away, close, head, head_nodes, count, share, hip, each, load, pattern], RED)
    group(doc, "5 - Assemble Model, its Tolerance 0.1 mm, below the tetrahedra's size", [assemble, view], BLUE)
    group(doc, "6 - Solve, and the displacement over the bone, 20 times",
          [settings, run, results, legend, legend_title, info, log], GREY)
    group(doc, "7 - Its modes: Natural Vibration, and Step picks the mode drawn",
          [vibration, mode, modal, frequencies], PURPLE)

    # Only the Model View after Run Analysis draws the bone; the one after Assemble Model and the
    # construction geometry stay out of the viewport. Show that one's preview to see the elements, the
    # supports and the loads.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in (results.InstanceGuid, legend.InstanceGuid):
            obj.Hidden = True

    note(doc, "Femur",
         "A femur, in tetrahedra.\n\n"
         "The Standardised Femur, 449 mm long: the reference geometry for finite element studies of the "
         "femur (Viceconti et al., J. Biomech. 1996), from the University of Sheffield, CC BY 4.0. "
         "{0:.0f} cm3, all of it cortical bone.\n\n"
         "1. The bone's closed surface, {1} triangles, is kept in the definition as nodes and triangles, "
         "and Construct Mesh makes it a mesh. Mesh Solid fills it with tetrahedra about 8 mm across, "
         "remeshing the surface as it goes, and 3 mm along the neck, where Mesh Refinement asks: the neck "
         "is where the bone bends most. Mesh Solid runs gmsh, which you install yourself (Alpaca4d > "
         "Settings > Download gmsh).\n"
         "2. Ten Node Tetrahedron: four corners and a node in the middle of each edge, so it bends. "
         "It needs an OpenSees built from the master branch since 16 October 2026 (Alpaca4d > "
         "Settings > Set OpenSees Executable).\n"
         "3. Held at the knee: every node in the bottom 2 cm, as a bone potted in cement for a test. "
         "A support holds only the node at its point, so the edge nodes are held too: Points gives "
         "every element's ten.\n"
         "4. The hip pushes on the head with about 2.5 times the body's weight when walking: 2 kN, "
         "leaning 13 degrees from the shaft, shared among the nodes within 15 mm of the top of the head.\n"
         "5. Assemble Model's Tolerance is 0.1 mm: its default, 1 cm, is larger than the tetrahedra and "
         "would weld their nodes together.\n"
         "6. The Model View after Run Analysis draws the displacement, 20 times: the head moves most, "
         "and the bone bends over the knee. Pick another result from its Results menu.\n"
         "7. Natural Vibration gives the first six modes; Step picks the one drawn.".format(
             1e6 * volume(nodes, tetrahedra), len(surface_triangles)),
         0, -420, 640, 350)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Femur built.")


if __name__ == "__main__":
    main()
