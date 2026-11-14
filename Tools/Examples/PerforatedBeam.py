#! python3
# The plasticity example: a steel beam with holes, pushed past the load at which it starts to yield.
#
# A simply supported S355 beam 2.4 m long, 300 mm deep and 80 mm thick, with six round holes 180 mm
# across, loaded at midspan, in 7 478 ten node tetrahedra. Its steel is J2Plasticity - von Mises
# plasticity - so where the stress reaches Fy = 355 MPa the steel yields and the load moves to the
# steel around it. Twenty steps of Load Control take it to 740 kN, close to what it can carry; the
# first yield comes at about 520 kN. Model View draws the Von Mises stress in Alpaca4d's colours, the
# yielding steel orange, step by step; a GhPython component draws the load-deflection curve, blue
# while the beam is elastic, orange after.
#
# It runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open
# this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript - and opens the
# definition on the canvas; with SAVE set to a path it also saves it there as a .gh. Unlike most of
# the examples it cannot be written by make.sh: the curve's colours come from Custom Preview, which
# takes Rhino's own display code to be made.
#
# The tetrahedra come from gmsh, in PerforatedBeam.txt: PerforatedBeamMesh.py made it. They need an
# OpenSees built from the master branch since 16 October 2026.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay, GH_ParameterSide, GH_ParamAccess
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Parameters.Hints import GH_Point3dHint, GH_Vector3dHint
from Grasshopper.Kernel.Special import GH_Group, GH_Panel, GH_BooleanToggle, GH_NumberSlider
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_MeshFace, GH_Boolean, GH_String, GH_Vector, GH_Colour, GH_Material
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d, Vector3d, MeshFace
from System.Drawing import PointF, RectangleF, Color, Bitmap
from System.Reflection import BindingFlags

# Grasshopper
POINT = "FBAC3E32-F100-4292-8692-77240A42FD1A"              # Params > Geometry > Point
INTEGER = "2E3AB970-8545-46BB-836C-1C11E5610BCE"            # Params > Primitive > Integer
MESH_FACE = "E02B3DA5-543A-46AC-A867-0BA6B0A524DE"          # Params > Geometry > Mesh Face
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
LIST_LENGTH = "1817FD29-20AE-4503-B542-F0FB651E67D7"        # Sets > List > List Length
CONSTRUCT_MESH = "E2C0F9DB-A862-4BD9-810C-EF2610E7A56F"     # Mesh > Primitive > Construct Mesh
DECONSTRUCT_POINT = "9ABAE6B7-FA1D-448C-9209-4A8155345841"  # Vector > Point > Deconstruct
CLOSEST_POINT = "571CA323-6E55-425A-BF9E-EE103C7BA4B9"      # Vector > Point > Closest Point
MULTIPLY = "63FFF845-7C61-4DFB-BA12-44D481B4BF0F"           # Vector > Vector > Multiply
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern
CULL_DUPLICATES = "6EAFFBB2-3392-441A-8556-2DC126AA8910"    # Vector > Point > Cull Duplicates
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script
CUSTOM_PREVIEW = "537B0419-BBC2-4FF4-BF08-AFE526367B2C"     # Display > Preview > Custom Preview

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD, on its J2Plasticity unit
TEN_NODE = "1F38CDFC-F909-4F3F-969E-9E2C63F9A321"           # Ten Node Tetrahedron
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LINEAR_SERIES = "05134721-D2DC-483E-BAD7-1A1AF0238A3D"      # Linear Time Series
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
TEST = "F8A3B2C1-D4E5-4F6A-B7C8-9D0E1F2A3B4C"               # Test
INTEGRATOR = "A5F2B8C3-D1E4-4F5A-B6C7-8D9E0F1A2B3C"         # Integrator, on its LoadControl unit
ANALYSIS_STEP = "825E700F-52D7-433D-92F6-8B6730F0A217"      # Analysis Step
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NODAL_DISPLACEMENTS = "25085EAB-6C66-487E-B83B-BAB5AB02B1BB"  # Nodal Displacements
BRICK_STRESSES_ = "DF03EC57-E1FA-4A3D-82BB-7F65526CF0B6"    # Brick Stresses
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend

J2_PLASTICITY = "J2Plasticity (Alpaca4d)"
LOAD_CONTROL = "LoadControl"
NORM_DISP_INCR = "NormDispIncr"

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
GREY = Color.FromArgb(40, 42, 45, 49)

# Alpaca4d's palette at full strength, for what the viewport draws.
LIGHT_BLUE = Color.FromArgb(255, 66, 136, 247)
FULL_ORANGE = Color.FromArgb(255, 235, 108, 63)
DARK_TECH = Color.FromArgb(255, 42, 45, 49)

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
ICONS = System.IO.Path.Combine(HERE, "..", "..", "Assets", "icon-candidates")
DATA = "PerforatedBeam.txt"
CURVE = "PerforatedBeamCurve.py"

# The beam, m, and its load.
LENGTH, THICKNESS, DEPTH = 2.4, 0.08, 0.3
LOAD = 740.0                # kN at the last step, spread over the load strip
STRIP = 0.10                # m: the load strip across the top at midspan
STEPS, LAMBDA = 20, 0.05    # Load Control: 20 steps of 5% of LOAD

# The four faces of a tetrahedron, by its corners: a closed mesh. Ten Node Tetrahedron reads only
# the corners and puts them in order itself.
FACES = ((0, 1, 2), (0, 3, 1), (1, 3, 2), (2, 3, 0))
# Model View's results, from 1: Displacement, Beam forces, Beam stresses, Shell forces, Shell
# stresses, Brick stresses, Reactions; and the brick stresses from 0: s11, s22, s33, s12, s23, s13,
# Von Mises.
BRICK_STRESSES, VON_MISES = 6, 6
FY = 355.0                  # MPa
BANDS = 12                  # iso-surfaces: eleven shades of the elastic range, and the top one


def read_tetrahedra():
    """The nodes, as (x, y, z), and the tetrahedra, as four node numbers from 0, of DATA."""
    # System.IO rather than os, which the IronPython make.sh runs this in does not have.
    lines = System.IO.File.ReadAllText(System.IO.Path.Combine(HERE, DATA)).split("\n")
    count = int(lines[0].split()[1])
    nodes = [tuple(float(v) for v in line.split()) for line in lines[1:1 + count]]
    tets = int(lines[1 + count].split()[1])
    tetrahedra = [tuple(int(v) for v in line.split()) for line in lines[2 + count:2 + count + tets]]
    return nodes, tetrahedra


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



PANEL = Color.FromArgb(255, 255, 250, 90)


def text(grid, name, value, col, row=None, gap=0, width=90.0):
    """A one-line panel holding a text or a number, such as a material's name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), 20.0)
    return grid.put(p, col, row, gap)


def readout(grid, name, source, col, row=None, gap=0, width=110.0, height=20.0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    grid.put(p, col, row, gap)
    wire(p, source)
    return p


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


def booleans(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Boolean(v))
    return param


def words(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
    return param


def vector(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
    return param


def shader(param, c):
    """A colour typed into a Custom Preview's Material input, which takes it as a plain material."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Material(c))
    return param


def colours(param, stops, count, top=None):
    """A colour scale typed into an unwired input: count colours spread evenly between the stops,
    and then top, when given, as one colour more."""
    rgb = [tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) for h in stops]
    param.PersistentData.Clear()
    for k in range(count):
        t = k / float(count - 1) * (len(rgb) - 1)
        i = min(int(t), len(rgb) - 2)
        f = t - i
        c = [int(round(rgb[i][j] + (rgb[i + 1][j] - rgb[i][j]) * f)) for j in range(3)]
        param.PersistentData.Append(GH_Colour(Color.FromArgb(255, c[0], c[1], c[2])))
    if top is not None:
        param.PersistentData.Append(GH_Colour(top))
    return param


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def switch_unit(c, unit):
    """Puts a switcher component on another of its units, as its drop-down does. It sets its unit's
    parameters aside as it switches, and writes to Rhino's command line when one still has a parent -
    which outside Rhino is not there to write to - so the parents are cleared first."""
    c = getattr(c, "__implementation__", c)
    for param in list(c.Params.Input) + list(c.Params.Output):
        if param.Attributes is not None:
            param.Attributes.Parent = None
    c.SwitchUnit(unit, False, False)
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()
    return c


def menu_setting(c, field, prop, value):
    """Sets a widget of a component's fold-out menus, found by its field, as a click on it would:
    the component saves it with the file."""
    c = getattr(c, "__implementation__", c)
    flags = BindingFlags.Instance | BindingFlags.NonPublic
    setattr(c.GetType().GetField(field, flags).GetValue(c), prop, value)


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


# ---------------------------------------------------------------- the beam

# Columns, in the order the data flows...
DATA_, CORNERS, MESHES, NAMES, MATERIAL, ELEMENTS, COORDS, TEST_, KEEP, UNIQUE, LOADS = range(11)
# ...and rows, one per part of the model.
TETS, ENDS, MIDDLE, STRIP_ = range(4)

# The model and its analysis, in a grid of their own to the right.
MODEL, VIEW, SHOWN = range(3)
ASSEMBLED, ANALYSED, CURVE_ = range(3)


def build(doc):
    nodes, tetrahedra = read_tetrahedra()
    g = Grid(doc)

    # 1 - The beam in tetrahedra, as gmsh hands them out: the nodes, and four node numbers per
    # tetrahedron, one branch each. Each tetrahedron's corners made into a mesh.
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

    # 2 - S355 that yields: J2Plasticity, perfectly plastic past Fy. Ten node tetrahedra carry it
    # best: they match beam theory before yield and level off after it.
    steel_values = [text(g, "Steel", "S355", NAMES, TETS),
                    text(g, "E [kN/m2]", "210000000", NAMES, TETS),
                    text(g, "Poisson", "0.3", NAMES, TETS),
                    text(g, "Fy [kN/m2]", "355000", NAMES, TETS),
                    text(g, "H [kN/m2]", "0", NAMES, TETS),
                    text(g, "Density [kg/m3]", "7850", NAMES, TETS)]
    steel = component(g, ND_MATERIAL, "nD", "S355, yielding", MATERIAL, TETS)
    switch_unit(steel, J2_PLASTICITY)
    for i, source in enumerate(steel_values):
        wire(inp(steel, i), source)
    ten = component(g, TEN_NODE, "Ten Node Tetrahedron", None, ELEMENTS, TETS)
    wire(inp(ten, 0), out(tet_meshes))
    wire(inp(ten, 1), out(steel))

    # 3 - Simply supported over its end faces, as on end plates: every node of both faces holds Z,
    # so the faces carry the shear and still turn, and Y. One node at the middle of the underside
    # holds X: by symmetry it carries nothing, and only stops the beam sliding along.
    model_nodes = component(g, POINT, "Point", "Element nodes", ELEMENTS, ENDS)
    flat(wire(model_nodes, out(ten, 1)))
    coords = component(g, DECONSTRUCT_POINT, "Deconstruct", "x y z", COORDS, ENDS)
    wire(inp(coords, 0), model_nodes)
    at_ends = expression(g, "f(x)=Abs(x-%g)>%g" % (LENGTH / 2.0, LENGTH / 2.0 - 0.0005), [out(coords, 0)],
                         "At either end", TEST_, ENDS)
    end_nodes = component(g, CULL_PATTERN, "Cull Pattern", "End faces", KEEP, ENDS)
    wire(inp(end_nodes, 0), model_nodes)
    wire(inp(end_nodes, 1), out(at_ends))
    end_once = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", UNIQUE, ENDS)
    wire(inp(end_once, 0), out(end_nodes))
    number(inp(end_once, 1), 0.0001)
    held = toggle(g, "Held", True, UNIQUE, ENDS)
    free = toggle(g, "Free", False, UNIQUE, ENDS)
    ends = component(g, SUPPORT, "Support", "End faces: Y and Z", LOADS, ENDS)
    wire(inp(ends, 0), out(end_once))
    wire(inp(ends, 1), free)
    wire(inp(ends, 2), held)
    wire(inp(ends, 3), held)
    for i in (4, 5, 6):
        wire(inp(ends, i), free)

    midspan = component(g, POINT, "Point", "Midspan underside", COORDS, MIDDLE)
    point(midspan, LENGTH / 2.0, THICKNESS / 2.0, 0.0)
    nearest = component(g, CLOSEST_POINT, "Closest Point", "Its node", KEEP, MIDDLE)
    wire(inp(nearest, 0), midspan)
    wire(inp(nearest, 1), model_nodes)
    middle = component(g, SUPPORT, "Support", "Midspan: X", LOADS, MIDDLE)
    wire(inp(middle, 0), out(nearest, 0))
    wire(inp(middle, 1), held)
    for i in (2, 3, 4, 5, 6):
        wire(inp(middle, i), free)

    # 4 - 740 kN on a strip 100 mm long across the top at midspan, shared evenly among its nodes.
    in_strip = expression(g, "f(x,z)=(Abs(x-%g)<%g) And (z>%g)" % (LENGTH / 2.0, STRIP / 2.0 + 0.0001, DEPTH - 0.0001),
                          [out(coords, 0), out(coords, 2)], "In the load strip", TEST_, STRIP_)
    strip_nodes = component(g, CULL_PATTERN, "Cull Pattern", "Strip", KEEP, STRIP_)
    wire(inp(strip_nodes, 0), model_nodes)
    wire(inp(strip_nodes, 1), out(in_strip))
    strip_once = component(g, CULL_DUPLICATES, "Cull Duplicates", "Once each", UNIQUE, STRIP_)
    wire(inp(strip_once, 0), out(strip_nodes))
    number(inp(strip_once, 1), 0.0001)
    count = component(g, LIST_LENGTH, "List Length", "How many", UNIQUE, STRIP_)
    wire(inp(count, 0), out(strip_once))
    total = text(g, "Load [kN]", "%g" % LOAD, UNIQUE, STRIP_)
    share = expression(g, "f(p,n)=-p/n", [total, out(count)], "Each node's share", UNIQUE, STRIP_)
    force = component(g, MULTIPLY, "Multiply", "Down", UNIQUE, STRIP_)
    vector(inp(force, 0), 0.0, 0.0, 1.0)
    wire(inp(force, 1), out(share))
    loads = component(g, POINT_LOAD, "Point Load", "Load strip", LOADS, STRIP_)
    wire(inp(loads, 0), out(strip_once))
    wire(inp(loads, 1), out(force))
    # A Linear Time Series, so that the load grows with Load Control's steps: Load Pattern's own
    # default is constant, which would put all of it on at the first step.
    linear = component(g, LINEAR_SERIES, "Linear Time Series", "Grows with the steps", UNIQUE, STRIP_)
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, LOADS, STRIP_)
    wire(inp(pattern, 0), out(linear))
    flat(wire(inp(pattern, 1), out(loads)))

    # At positive canvas coordinates, below the note: GenerateHiResImage cuts a negative origin.
    right = g.arrange(300, 760, stages=(NAMES, COORDS, LOADS))

    # 5 - Assemble: a tolerance well under the 12 mm between the tetrahedra's nodes.
    m = Grid(doc)
    assemble = component(m, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    flat(wire(inp(assemble, 0), out(ten)))
    flat(wire(inp(assemble, 1), out(ends), out(middle)))
    flat(wire(inp(assemble, 2), out(pattern)))
    number(inp(assemble, 5), 0.001)
    view = component(m, MODEL_VIEW, "Model View", "Beam at rest", VIEW, ASSEMBLED)
    wire(inp(view, 0), out(assemble))

    # 6 - Pushed in steps: Newton, 20 steps of Load Control of 5% of the load, each solved to a
    # displacement increment of 1e-8 m in at most 30 iterations.
    test = component(m, TEST, "Test", None, MODEL, ANALYSED)
    switch_unit(test, NORM_DISP_INCR)
    number(inp(test, 0), 1e-8)
    inp(test, 1).PersistentData.Clear()
    inp(test, 1).PersistentData.Append(GH_Integer(30))
    integrator = component(m, INTEGRATOR, "Integrator", "Load Control", MODEL, ANALYSED)
    switch_unit(integrator, LOAD_CONTROL)
    number(inp(integrator, 0), LAMBDA)
    steps = component(m, ANALYSIS_STEP, "Analysis Step", "20 steps", MODEL, ANALYSED)
    inp(steps, 0).PersistentData.Clear()
    inp(steps, 0).PersistentData.Append(GH_Integer(STEPS))
    settings = component(m, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, ANALYSED)
    wire(inp(settings, 4), out(test))
    words(inp(settings, 5), "Newton")
    wire(inp(settings, 6), out(integrator))
    wire(inp(settings, 8), out(steps))
    run = component(m, RUN_ANALYSIS, "Run Analysis", None, MODEL, ANALYSED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))

    # 7 - The Von Mises stress, step by step, in Alpaca4d's colours: eleven shades from light blue
    # through blue to purple for the elastic range, and orange at the top, where the steel yields.
    step = slider(m, "Step", 0, STEPS - 1, STEPS - 1, 0, VIEW, ANALYSED)
    results = component(m, MODEL_VIEW, "Model View", "Beam pushed", VIEW, ANALYSED)
    wire(inp(results, 0), out(run))
    wire(inp(results, 1), step)
    widget(results, "_ckExtruded").Active = False
    widget(results, "_ckReleases").Active = False
    menu_setting(results, "_ckBands", "Active", True)
    menu_setting(results, "_ckDeformed", "Active", True)
    menu_setting(results, "_slDeformScale", "MaxValue", 50.0)
    menu_setting(results, "_slDeformScale", "Value", 15.0)
    show_result(results, BRICK_STRESSES, VON_MISES)
    colours(inp(results, 3), ["#E4EEFE", "#4288F7", "#9E35DA"], BANDS - 1, FULL_ORANGE)
    stress_range = text(m, "Range [MPa]", "0 To %g" % FY, VIEW, ANALYSED, width=110.0)
    wire(inp(results, 4), stress_range)
    legend = component(m, LEGEND, "Legend", "Beam pushed legend", SHOWN, ANALYSED)
    legend_title = text(m, "Title", "Von Mises [MPa], orange: yielding", SHOWN, ANALYSED, width=200.0)
    wire(inp(legend, 0), legend_title)
    wire(inp(legend, 1), out(results, 3))
    wire(inp(legend, 2), out(results, 4))
    info = readout(m, "What is drawn", out(results, 2), SHOWN, ANALYSED, width=300.0, height=40.0)
    log = readout(m, "OpenSees log", out(run, 1), SHOWN, ANALYSED, width=300.0, height=140.0)

    # 8 - The load-deflection curve, from every step: the deflection of the middle of the underside,
    # and the largest Von Mises stress, which tells the step the steel starts to yield.
    displacements = component(m, NODAL_DISPLACEMENTS, "Nodal Displacements", None, MODEL, CURVE_)
    wire(inp(displacements, 0), out(run))
    booleans(inp(displacements, 1), True)
    stresses = component(m, BRICK_STRESSES_, "Brick Stresses", None, MODEL, CURVE_)
    wire(inp(stresses, 0), out(run))
    booleans(inp(stresses, 1), True)

    hints = emit(PYTHON, "GhPython").GetType().Assembly
    def hint(name):
        return System.Activator.CreateInstance(hints.GetType("GhPython.Component." + name))
    number_hint = hint("NewFloatHint")
    tree, item = GH_ParamAccess.tree, GH_ParamAccess.item
    curve = python(m, "Load-deflection", CURVE, "load_deflection",
                   [("Positions", GH_Point3dHint(), tree), ("Displacements", GH_Vector3dHint(), tree),
                    ("VonMises", number_hint, tree), ("Load", number_hint, item), ("Lambda", number_hint, item),
                    ("Fy", number_hint, item), ("Origin", GH_Point3dHint(), item), ("Width", number_hint, item),
                    ("Height", number_hint, item)],
                   ["Elastic", "Yielding", "Axes", "Summary", "Deflection", "Load", "YieldDeflection", "YieldLoad"],
                   "perforated-beam-load-deflection.png", VIEW, CURVE_)
    wire(inp(curve, 0), out(displacements, 0))
    wire(inp(curve, 1), out(displacements, 1))
    wire(inp(curve, 2), out(stresses, 6))
    wire(inp(curve, 3), total)
    number(inp(curve, 4), LAMBDA)
    number(inp(curve, 5), FY * 1000.0)
    point(inp(curve, 6), 0.0, 0.0, -1.0)
    number(inp(curve, 7), LENGTH)
    number(inp(curve, 8), 0.7)
    elastic_view = component(m, CUSTOM_PREVIEW, "Custom Preview", "Elastic", SHOWN, CURVE_)
    wire(inp(elastic_view, 0), out(curve, 1))
    shader(inp(elastic_view, 1), LIGHT_BLUE)
    yielding_view = component(m, CUSTOM_PREVIEW, "Custom Preview", "Yielding", SHOWN, CURVE_)
    wire(inp(yielding_view, 0), out(curve, 2))
    shader(inp(yielding_view, 1), FULL_ORANGE)
    axes_view = component(m, CUSTOM_PREVIEW, "Custom Preview", "Axes", SHOWN, CURVE_)
    wire(inp(axes_view, 0), out(curve, 3))
    shader(inp(axes_view, 1), DARK_TECH)
    summary = readout(m, "Summary", out(curve, 4), SHOWN, CURVE_, width=300.0, height=80.0)

    m.arrange(right + 160, 760)

    group(doc, "1 - The beam in tetrahedra: nodes, and four nodes per tetrahedron",
          [node_points, corner_numbers, faces, corners, tet_meshes], ORANGE)
    group(doc, "2 - S355 that yields - J2Plasticity - in Ten Node Tetrahedra",
          [steel, ten] + steel_values, PURPLE)
    group(doc, "3 - Simply supported on its end faces; one node holds it along",
          [model_nodes, coords, at_ends, end_nodes, end_once, held, free, ends, midspan, nearest, middle], GREEN)
    group(doc, "4 - 740 kN on a strip across the top at midspan, growing with the steps",
          [in_strip, strip_nodes, strip_once, count, total, share, force, loads, linear, pattern], RED)
    group(doc, "5 - Assemble Model", [assemble, view], BLUE)
    group(doc, "6 - Pushed in 20 steps of 37 kN, past the load it starts to yield at",
          [test, integrator, steps, settings, run], GREY)
    group(doc, "7 - Von Mises, step by step: orange where the steel yields",
          [step, results, stress_range, legend, legend_title, info, log], ORANGE)
    group(doc, "8 - The load-deflection curve: blue while elastic, orange after",
          [displacements, stresses, curve, elastic_view, yielding_view, axes_view, summary], BLUE)

    # Only the Model View after Run Analysis, its legend and the curve draw; the rest stays out of
    # the viewport. Show the first Model View's preview to see the elements, supports and loads.
    shown = set(o.InstanceGuid for o in (results, legend, elastic_view, yielding_view, axes_view))
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid not in shown:
            obj.Hidden = True

    note(doc, "Beam with holes",
         "A steel beam with holes, pushed past the load at which it starts to yield.\n\n"
         "A simply supported S355 beam 2.4 m long, 300 mm deep and 80 mm thick, with six round holes 180 mm "
         "across, loaded at midspan: {0} ten node tetrahedra on {1} corners.\n\n"
         "1. The tetrahedra come as gmsh wrote them: the nodes, and four node numbers per tetrahedron.\n"
         "2. The steel is the nD component's J2Plasticity unit: elastic until the Von Mises stress reaches "
         "Fy = 355 MPa, then it yields and the load moves to the steel around it.\n"
         "3. It rests on its end faces, which carry the shear and turn; one node holds it along.\n"
         "4. 740 kN on a strip across the top at midspan, in a Load Pattern with a Linear Time Series, so "
         "that it grows step by step.\n"
         "6. Newton and 20 steps of Load Control: a material that yields needs the load in steps.\n"
         "7. Model View draws the Von Mises stress in Alpaca4d's colours, with the steel at or near yield "
         "orange. Move Step to watch the yielding start at the holes next to midspan and spread.\n"
         "8. The load-deflection curve leaves the straight line where the steel starts to yield and "
         "flattens as the yielding spreads: the beam is pushed well past first yield.".format(len(tetrahedra), len(nodes)),
         300, 300, 620, 380)


def main():
    doc = open_canvas()
    build(doc)
    path = System.Environment.GetEnvironmentVariable("SAVE")
    if path:
        Grasshopper.Kernel.GH_DocumentIO(doc).SaveQuiet(path)
        print("Saved " + path)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The beam with holes built.")


if __name__ == "__main__":
    main()
