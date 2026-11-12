#! python3
# Six conventional structures, each a definition of its own, built on the Grasshopper canvas: their
# geometry from sliders, in a GhPython component (StructuresGeometry.py, next to this script), and the
# rest from Alpaca4d's components - the members, the supports, the loads, the analysis and what it
# shows.
#
#   STRUCTURE=hall      a steel hall 24 m across, roofed by Pratt trusses on columns, under snow
#   STRUCTURE=tower     a steel tower with a stair inside, under wind, and its first modes
#   STRUCTURE=canopy    a canopy on T-shaped frames, under snow lying deeper on one side
#   STRUCTURE=facade    a glass wall 10.8 m tall stiffened by vertical trusses, under wind
#   STRUCTURE=vault     a concrete barrel vault under wind, pressure on its windward quarter and
#                       suction over the rest
#   STRUCTURE=windtruss a glass facade 12 m tall held by two horizontal wind trusses
#
# and five shaken by the same earthquake, El Centro 1940, in a transient analysis:
#
#   STRUCTURE=girder       a continuous concrete box girder bridge on two piers
#   STRUCTURE=cablestayed  a cable-stayed bridge, two spans from one H-shaped pylon
#   STRUCTURE=arch         a concrete deck arch bridge
#   STRUCTURE=building     an eight-storey steel office building with concrete slabs
#   STRUCTURE=city         a tiny city of sixteen buildings from two storeys to twenty
#
# a footbridge and its natural modes:
#
#   STRUCTURE=footbridge   a concrete deck on a steel box girder, and its first six modes
#
# and a row of sticks the same earthquake shakes, which together draw its response spectrum:
#
#   STRUCTURE=sticks       thirty cantilevers with a mass on top, from 1 m to 10 m tall
#
# and a shell form-found with Kangaroo2 to carry its own weight in compression:
#
#   STRUCTURE=shell        a concrete shell of four legs, 20 m across
#
# and a concrete tank's first hundred natural modes, a copy of the tank in each, on a grid:
#
#   STRUCTURE=tank         a concrete cylinder 4 m across and 5 m tall, built in at its base
#
# and, shaken by El Centro too, four buildings that are not straight:
#
#   STRUCTURE=twisting     a thirty-storey concrete tower turning a quarter turn to its roof
#   STRUCTURE=leaning      two steel towers leaning towards each other over a street
#   STRUCTURE=curving      a forty-storey round steel tower bowing out and back
#   STRUCTURE=crescent     a fourteen-storey concrete block curved in plan
#
# and five towers of a hundred storeys and more, each after a famous one's structure, every floor a
# rigid diaphragm with its mass at its nodes:
#
#   STRUCTURE=bundled      a bundled tube, after the Willis Tower
#   STRUCTURE=braced       a braced tube, after the John Hancock Center
#   STRUCTURE=buttressed   a buttressed core of concrete walls and a spire, after the Burj Khalifa
#   STRUCTURE=outrigger    a braced core, super columns, outriggers and a tuned mass damper, after Taipei 101
#   STRUCTURE=diagrid      a diagrid round a concrete core, after the Guangzhou IFC
#
# and a concrete building pushed over, its beams and columns fibre sections that crack and yield:
#
#   STRUCTURE=pushover     a small concrete building, L-shaped, a tall ground storey and a top storey
#                          set back, pushed sideways to 3% drift
#
# make.sh, in the folder above, writes each straight from this script, without Rhino:
#     STRUCTURE=hall ./make.sh Structures/Structures.py TrussRoofHall.gh
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript - and
# builds the hall unless STRUCTURE says otherwise.
#
# The loads are what a designer types in, worked out to a code of practice beforehand: a roof's
# snow and finishes in kN/m2, times the width of roof each purlin carries, as a line load on it; a
# facade's wind in kN/m2, times the width each mullion catches, the same way. The geometry gives each
# loaded member its width.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParameterSide, GH_ParamAccess
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS, GH_Point3dHint, GH_Vector3dHint
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_String, GH_Boolean, GH_Vector, GH_Colour, GH_Interval, GH_StructurePath, GH_Point
from Grasshopper.Kernel.Data import GH_Path
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Vector3d, Interval, Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MULTIPLY = "63FFF845-7C61-4DFB-BA12-44D481B4BF0F"           # Vector > Vector > Multiply
VECTOR_XYZ = "56B92EAB-D121-43F7-94D3-6CD8F0DDEAD8"         # Vector > Vector > Vector XYZ
DUPLICATE = "DD8134C0-109B-4012-92BE-51D843EDFFF7"          # Sets > Sequence > Duplicate Data
UNFLATTEN = "B8E2AA8F-8830-4EE1-BB59-613EA279C281"          # Sets > Tree > Unflatten Tree
TREE_BRANCH = "3A710C1E-1809-4E19-8C15-82ADCE31CD62"        # Sets > Tree > Tree Branch
MESH_PARAM = "1E936DF3-0EEA-4246-8549-514CB8862B7A"         # Params > Geometry > Mesh
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
MESH_EDGES = "2B9BF01D-5FE5-464C-B0B3-B469EB5F2EFB"         # Mesh > Analysis > Mesh Edges
DECONSTRUCT_MESH = "BA2D8F57-0738-42B4-B5A5-FE4D853517EB"   # Mesh > Analysis > Deconstruct Mesh
PULL_POINT = "902289DA-28DC-454B-98D4-B8F8AA234516"         # Vector > Point > Pull Point
SMALLER_THAN = "AE840986-CADE-4E5A-96B0-570F007D4FC0"       # Maths > Operators > Smaller Than
CULL_PATTERN = "008E9A6F-478A-4813-8C8A-546273BC3A6B"       # Sets > Sequence > Cull Pattern

# Kangaroo2
REFINE = "6003658A-C422-41CF-8A2F-8E106B09CC10"             # Kangaroo2 > Mesh > Refine
LENGTH_LINE = "091BAE84-8FA9-4B35-8AAD-B25B859055F6"        # Kangaroo2 > Goals-Lin > Length(Line)
ANCHOR = "3C30B1A1-4473-4AD4-A700-EA9770726C03"             # Kangaroo2 > Goals-Pt > Anchor
LOAD = "2019C995-53AF-4EB2-976D-95B1FDC823FA"               # Kangaroo2 > Goals-Pt > Load
SHOW = "0ED5E67D-539D-480E-88CB-D81FA795D66C"               # Kangaroo2 > Main > Show
ZOMBIE = "4408343B-577E-4FD8-96FF-DF7549189186"             # Kangaroo2 > Main > Zombie Solver

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
PLATE_FIBER = "D54FADEF-3E36-492C-9241-4E9B8F48EE26"        # Plate Fiber Section
SHELL = "EE45CFFD-CCDC-41D4-AA93-551FB576C2FC"              # ASD Shell
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LINEAR_LOAD = "8593AC69-D975-4566-97FC-27D7D4A4D415"        # Linear Load
MESH_LOAD = "C8514DEA-1B1B-45A6-ABC1-8004C1EFC617"          # Mesh Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NATURAL_VIBRATION = "3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3"  # Natural Vibration Analysis
MODEL_VIEW = "F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE"         # Model View
LEGEND = "A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5"             # Legend
RECTANGLE = "888184F3-A448-4F82-9DCA-E57EFAC8E840"          # RectangleCS
RECTANGLE_HOLLOW = "0DA19D50-BCDF-4AFA-9777-45AE6A5392CC"   # Rectangle Hollow CS
CIRCLE = "AFCC30B2-7CB9-453F-B5A1-C73554BD3DC7"             # CircleCS
PATH_SERIES = "AFAF03A6-6C4E-4DAB-AA46-EB60DFB95DFB"        # Path Time Series
DAMPING = "EB8C2885-7DEF-4B12-B978-FD81A1A5763E"            # Damping
INTEGRATOR = "A5F2B8C3-D1E4-4F5A-B6C7-8D9E0F1A2B3C"         # Integrator
ANALYSIS_STEP = "825E700F-52D7-433D-92F6-8B6730F0A217"      # Analysis Step
MASS_POINT = "5C7056ED-01D4-4DDE-AD9E-46A5266ECEE6"         # Mass Point
NODAL_DISPLACEMENTS = "25085EAB-6C66-487E-B83B-BAB5AB02B1BB" # Nodal Displacements
MPCO_RECORDER = "3F6C2B8E-9D41-4A7E-B5C3-1E8F0A2D7C94"      # MPCO Recorder
RIGID_LINK = "C7A1A1B5-8C17-4B51-B9E2-3E8E9F3C6475"         # Rigid Link
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
RIGID_DIAPHRAGM = "BDA8CE51-1B34-48C7-B0B1-80A25E7E5754"    # Rigid Diaphragm
LINK = "5E9C2F84-1D67-4A03-9B58-C4F2E7A06D31"               # Link
UNIAXIAL = "4F1E13F9-0B35-4F8F-A324-883CEA2F8777"           # Uniaxial, Elastic
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
TEXT_TAG = "5A41528B-12B9-40DC-A3F2-842034D267C4"           # Display > Dimensions > Text Tag 3D
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
CONCRETE01 = "57BF0FDB-B333-491C-B9EB-50783029B8C2"         # Concrete01
REBAR = "47B04CA7-0F7E-4585-8F89-D31592E61394"              # ReinforcingSteel
PATCH = "49DDD2E0-99B5-4D14-976E-531481B07909"              # Patch
POINT_FIBER = "934021CE-8D12-4FCA-8CF1-9158EB08208A"        # Fiber Point
FIBER_SECTION = "18cd5969-e0e4-4acf-9790-0af2c22cfeb9"      # Fiber Section
LINEAR_SERIES = "05134721-D2DC-483E-BAD7-1A1AF0238A3D"      # Linear Time Series
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
PUSHOVER = "3F7A2D90-5C1B-4E86-9A4D-2B8E6C0F7135"           # Pushover Analysis
CURVE_PARAM = "D5967B9F-E8EE-436B-A8AD-29FDCECF32D5"        # Params > Geometry > Curve

# The switchers' other units, by name: the beam's truss, a ground motion's pattern, Newmark's integrator.
TRUSS = "Truss (Alpaca4d)"
UNIFORM_EXCITATION = "UniformExcitation (Alpaca4d)"
NEWMARK = "Newmark"
# Analysis Settings' inputs, by place.
A_DAMPING, A_SYSTEM, A_ALGORITHM, A_INTEGRATOR, A_TYPE, A_STEPS = 0, 3, 5, 6, 7, 8

# ForceBeamColumn's inputs, and Truss's, by place.
B_LINE, B_SECTION, B_ZAXIS, B_COLOUR, B_ELEMENT_ID = 0, 1, 3, 4, 5
T_LINE, T_SECTION = 0, 1
# Support's inputs: where, then whether each of Tx, Ty, Tz, Rx, Ry, Rz is held - all held unless typed.
S_POSITION = 0
# Model View's inputs and outputs, and Legend's inputs. Its results, from 1: Displacement is the first,
# and Magnitude the first of the displacements.
V_MODEL, V_STEP, V_COLOURS, V_RANGE = 0, 1, 3, 4
# Model View's Colour by: by material, the third of its list. Material Library Elastic's Colour input.
BY_MATERIAL = 2
M_COLOUR = 2
# What the structure at rest is drawn in, behind its deformed shape: a light grey from the palette.
AT_REST = Color.FromArgb(255, 190, 194, 199)

# Alpaca4d's palette, and the colour scales the structures draw their displacement in, from no
# displacement to the most, each its own.
DARK_TECH, LIGHT_BLUE, PURPLE_, LIGHT_GREEN, ORANGE_ = "#2A2D31", "#4288F7", "#9E35DA", "#AFC930", "#EB6C3F"
# A pale tint of the light blue: where the elements' edges are drawn, black, a scale starting dark would
# draw everything that hardly moves black too.
PALE_BLUE = "#D4E3FD"
# The earthquakes' scale, every one alike: a jet, dark blue for still to dark red for moving the most, the way
# engineers read a displacement - the user's choice for the buckling modes, 2026-11-07, and for these the same day.
JET = ["#000080", "#0000FF", "#0080FF", "#00FFFF", "#80FF80", "#FFFF00", "#FF8000", "#FF0000", "#800000"]
# No green where line loads are drawn: theirs is green, and a green band would read as a load.
SCALES = {
    "hall": [DARK_TECH, LIGHT_BLUE, ORANGE_],
    "tower": [LIGHT_BLUE, PURPLE_, ORANGE_],
    "mode": [DARK_TECH, LIGHT_BLUE, LIGHT_GREEN],
    "canopy": [DARK_TECH, PURPLE_, ORANGE_],
    "facade": [DARK_TECH, LIGHT_BLUE, PURPLE_],
    "vault": [PURPLE_, LIGHT_BLUE, LIGHT_GREEN],
    "windtruss": [PURPLE_, LIGHT_BLUE, ORANGE_],
    "girder": JET,
    "cablestayed": JET,
    "arch": JET,
    "building": JET,
    "city": JET,
    "footbridge": [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_],
    "sticks": [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_],
    "shell": [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_],
    "tank": [LIGHT_BLUE, LIGHT_GREEN, ORANGE_],
    "twisting": JET,
    "leaning": JET,
    "curving": JET,
    "crescent": JET,
    "bundled": JET,
    "braced": JET,
    "buttressed": JET,
    "outrigger": JET,
    "diagrid": JET,
    "pushover": JET,
}
R_VALUES, R_COLORS = 3, 4
DISPLACEMENT, MAGNITUDE = 1, 0
L_TITLE, L_VALUES, L_COLORS, L_SCALE = 0, 1, 2, 4

STRUCTURE = (System.Environment.GetEnvironmentVariable("STRUCTURE") or "hall").lower()

# The geometry's code, next to this file. System.IO rather than os: make.sh's IronPython has no
# standard library.
GEOMETRY = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__)),
                                  "StructuresGeometry.py")

# Grasshopper's own panel yellow: a panel made in code has no colour of its own, and Rhino draws it
# see-through, with white text.
PANEL = Color.FromArgb(255, 255, 250, 90)

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


def flat(param):
    """Takes whatever tree arrives as one list."""
    param.DataMapping = GH_DataMapping.Flatten
    return param


def number(param, *values):
    """Types numbers into an unwired input, as on the canvas."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Number(float(v)))
    return param


def integer(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def words(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
    return param


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


def booleans(param, *values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Boolean(v))
    return param


def vector(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
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


def text(grid, name, value, col, row=None, gap=0):
    """A one-line panel holding a text, such as a Legend's title."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Colour = PANEL
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 120.0, 20.0)
    return grid.put(p, col, row, gap)


def panel(grid, name, source, width, height, col, row=None, gap=0):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Colour = PANEL
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    p.AddSource(source)
    return grid.put(p, col, row, gap)


def expression(grid, code, sources, nickname, col, row=None, gap=0):
    """An Expression component set up as the canvas search sets it up from f(x)=...; returns it."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    e.NickName = nickname
    grid.put(e, col, row, gap)
    for i in range(len(sources)):
        inp(e, i).AddSource(sources[i])
    return e


def note(doc, title, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = title
    p.Properties.Colour = PANEL
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
    """Ticks one of Model View's results alone, at a component, as clicks in its Results menu do.
    Its menus then show the controls its settings have a use for and put the rest away, and it is laid
    out again: the grid measures it at the height it opens at. Set its other widgets first."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    c.GetType().GetMethod("ShowResult", flags).Invoke(c, System.Array[System.Object]([show, component_]))
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()


def recorder(grid, nickname, nodes, col, row=None, gap=0):
    """An MPCO Recorder writing only the node results named, such as "displacement", and no element
    results, as unticking the rest of its menus does."""
    c = component(grid, MPCO_RECORDER, "MPCO Recorder", nickname, col, row, gap)
    impl = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    for field, keep in (("_nodeBoxes", nodes), ("_elementBoxes", ())):
        for box in impl.GetType().GetField(field, flags).GetValue(impl):
            box.Active = box.Tag in keep
    return c


def result_unit(c, show, component_, index):
    """Picks the unit Model View reads a result in, by its place in the result's Unit drop-down: for
    a length, mm, cm, m."""
    c = getattr(c, "__implementation__", c)
    flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    unit = c.GetType().GetMethod("UnitOf", flags).Invoke(c, System.Array[System.Object]([show, component_]))
    unit.Value = index


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


def python_component(grid, nickname, function, inputs, outputs, col, row=None):
    """A GhPython component running one function of StructuresGeometry.py: its inputs as (name, hint),
    or (name, hint, access) for a list or a tree, and its outputs as names, after its own out, which
    carries what the code prints and its errors."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, spec in enumerate(inputs):
        p = c.CreateParameter(GH_ParameterSide.Input, i)
        p.Name = p.NickName = spec[0]
        p.TypeHint = spec[1]
        if len(spec) > 2:
            p.Access = spec[2]
        c.Params.RegisterInputParam(p)
    for i, name in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    c.Code = (System.IO.File.ReadAllText(GEOMETRY) + "\n\n" + ", ".join(outputs) + " = " + function + "(" +
              ", ".join(spec[0] for spec in inputs) + ")\n")
    return grid.put(c, col, row)


# ---------------------------------------------------------------- the parts every structure has

# Columns in the order the data flows...
SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, WIDTHS, VECTORS, LOADS, PATTERNS, MODEL, ANALYSIS, VIEWS, LEGENDS = range(12)
# ...and the rows.
MAIN, LOADED, SHOWN = range(3)


class Structure(object):
    """What a structure's definition is made of, as it is built: its geometry component, its
    material, what goes into Assemble, and the groups of the canvas."""

    def __init__(self, doc, title, function, shape, loads):
        """shape: the geometry's sliders, as (input, slider name, min, max, value, decimals), in the
        order of the function's arguments; loads: the loads' sliders, the same way."""
        self.doc = doc
        self.grid = Grid(doc)
        self.title = title
        self.members, self.supports, self.patterns = [], [], []
        self.constraints, self.recorders = [], []
        self.parts = {"1": [], "2": [], "3": [], "4": [], "5": [], "6": [], "7": []}
        hints = emit(PYTHON, "GhPython").GetType().Assembly
        as_float = self.as_float = System.Activator.CreateInstance(hints.GetType("GhPython.Component.NewFloatHint"))
        as_int = GH_IntegerHint_CS()
        self.sliders, self.inputs = {}, []
        for name, label, lo, hi, value, decimals in shape:
            self.sliders[name] = slider(self.grid, label, lo, hi, value, decimals, SLIDERS, MAIN, gap=30)
            self.inputs.append((name, as_int if decimals == 0 else as_float))
            self.parts["1"].append(self.sliders[name])
        for name, label, lo, hi, value, decimals in loads:
            self.sliders[name] = slider(self.grid, label, lo, hi, value, decimals, SLIDERS, LOADED, gap=30)
            self.parts["4"].append(self.sliders[name])
        self.function = function

    def geometry(self, outputs):
        self.geo = python_component(self.grid, self.title, self.function, self.inputs, outputs, GEOMETRY_, MAIN)
        for i, (name, hint) in enumerate(self.inputs):
            wire(inp(self.geo, i), self.sliders[name])
        self.outputs = outputs
        self.parts["1"].append(self.geo)
        return self.geo

    def g(self, name):
        """One of the geometry's outputs, by name."""
        return out(self.geo, self.outputs.index(name) + 1)

    def steel(self):
        self.material = component(self.grid, MATERIAL, "Material Library Elastic", "S355", SECTIONS, MAIN)
        words(inp(self.material, 0), "S355")
        # Display only: the view of the structure at rest draws it by material, light grey.
        inp(self.material, M_COLOUR).PersistentData.Clear()
        inp(self.material, M_COLOUR).PersistentData.Append(GH_Colour(AT_REST))
        self.parts["2"].append(self.material)
        return self.material

    def member(self, name, lines, section, truss=False, zaxis=None):
        """Elements on one of the geometry's outputs, with a section of the library."""
        library = component(self.grid, SECTION, "Steel Section Library", section, SECTIONS, MAIN, gap=30)
        words(inp(library, 0), section)
        wire(inp(library, 1), out(self.material))
        beam = component(self.grid, BEAM, "ForceBeamColumn", name, MEMBERS, MAIN, gap=30)
        if truss:
            switch_unit(beam, TRUSS)
        wire(inp(beam, T_LINE if truss else B_LINE), self.g(lines))
        wire(inp(beam, T_SECTION if truss else B_SECTION), out(library))
        if zaxis is not None:
            vector(inp(beam, B_ZAXIS), *zaxis)
        self.members.append(beam)
        self.parts["2"] += [library, beam]
        return beam

    def support(self, name, points, held):
        """A support at each of the geometry's points, named, or of a parameter: held, Tx to Rz, True
        where held."""
        s = component(self.grid, SUPPORT, "Support", name, MEMBERS, LOADED, gap=30)
        wire(inp(s, S_POSITION), self.g(points) if isinstance(points, str) else points)
        for k in range(6):
            if not held[k]:
                booleans(inp(s, k + 1), False)
        self.supports.append(s)
        self.parts["3"].append(s)
        return s

    def line_load(self, name, beams, unit, sign, pressure, width=None, share=1.0):
        """A Linear Load on beams, in kN/m along a unit vector (UNIT_X, Y or Z), sign + or -: a
        pressure slider times a width - each beam's, from the geometry, or a slider - and share of it."""
        sources = [self.sliders[pressure]] + ([width] if width is not None else [])
        amount_ = ("q*w" if width is not None else "q") + ("" if share == 1.0 else "*{0}".format(share))
        code = ("f(q,w)=" if width is not None else "f(q)=") + ("-" if sign < 0 else "") + amount_
        amount = expression(self.grid, code, sources, name + " [kN/m]", WIDTHS, LOADED, gap=30)
        direction = component(self.grid, unit, "Unit vector", name, VECTORS, LOADED, gap=30)
        wire(inp(direction, 0), out(amount))
        load = component(self.grid, LINEAR_LOAD, "Linear Load", name, LOADS, LOADED, gap=30)
        wire(flat(inp(load, 0)), *[out(b) for b in beams])
        wire(flat(inp(load, 1)), out(direction))
        self.parts["4"] += [amount, direction, load]
        return load

    def pattern(self, name, loads, factor=1.0):
        p = component(self.grid, LOAD_PATTERN, "Load Pattern", name, PATTERNS, LOADED, gap=30)
        # Flattened, here and into Assemble: the elements' switchers give their data a path of their own,
        # and loads from them on another branch than the Gravity Load's made a second pattern, and
        # Assemble a second model.
        wire(flat(inp(p, 1)), *[out(l) for l in loads])
        if factor != 1.0:
            number(inp(p, 2), factor)
        self.patterns.append(p)
        self.parts["4"].append(p)
        return p

    def assemble(self):
        a = component(self.grid, ASSEMBLE, "Assemble Model", self.title, MODEL, MAIN)
        wire(flat(inp(a, 0)), *[out(m) for m in self.members])
        wire(flat(inp(a, 1)), *[out(s) for s in self.supports])
        wire(flat(inp(a, 2)), *[out(p) for p in self.patterns])
        settings = component(self.grid, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, MAIN, gap=30)
        run = component(self.grid, RUN_ANALYSIS, "Run Analysis", None, ANALYSIS, MAIN)
        wire(inp(run, 0), out(a))
        wire(inp(run, 1), out(settings))
        self.assembled, self.run = a, run
        self.parts["5"] += [a, settings, run]
        return run

    def deformed(self, name, model, scale, step=None, title="Displacement [mm]", loads=True, palette=None, bands=False):
        """A Model View of model, deformed scale times and coloured by its displacement in a scale of
        its own, with a Legend, and the loads riding on the deformed shape, without their values (a
        mode has none). bands: iso-surfaces, eight bands of values, for shells."""
        view = component(self.grid, MODEL_VIEW, "Model View", name, VIEWS, SHOWN, gap=30)
        wire(inp(view, V_MODEL), model)
        colours(inp(view, V_COLOURS), SCALES[palette or STRUCTURE], 8 if bands else 9)
        if bands:
            menu_setting(view, "_ckBands", "Active", True)
        if step is not None:
            integer(inp(view, V_STEP), step)
        menu_setting(view, "_ckDeformed", "Active", True)
        # The slider stops at 200: a shell that hardly moves is drawn larger than that.
        if scale > 200:
            menu_setting(view, "_slDeformScale", "MaxValue", float(scale))
        menu_setting(view, "_slDeformScale", "Value", scale)
        # The edges and the release symbols would hide the colours on members this thin.
        menu_setting(view, "_ckWires", "Active", False)
        menu_setting(view, "_ckReleases", "Active", False)
        if loads:
            # Short arrows, so that the shape and its colours still read under them.
            menu_setting(view, "_ckShowLoads", "Active", True)
            menu_setting(view, "_slLoadScale", "Value", 0.4)
        show_result(view, DISPLACEMENT, MAGNITUDE)
        caption = text(self.grid, name + " title", title, LEGENDS, SHOWN, gap=30)
        legend = component(self.grid, LEGEND, "Legend", name + " legend", LEGENDS, SHOWN, gap=30)
        wire(inp(legend, L_TITLE), caption)
        wire(inp(legend, L_VALUES), out(view, R_VALUES))
        wire(inp(legend, L_COLORS), out(view, R_COLORS))
        # Larger than it opens at, so that it still reads in a still captured larger than the screen.
        number(inp(legend, L_SCALE), 2.4)
        self.parts["6"] += [view, caption, legend]
        return view

    def concrete(self):
        """C30/37 from the library, light grey for the view at rest."""
        c = component(self.grid, MATERIAL, "Material Library Elastic", "C30/37", SECTIONS, MAIN, gap=30)
        words(inp(c, 0), "C30_37")   # the library's name for it
        inp(c, M_COLOUR).PersistentData.Append(GH_Colour(AT_REST))
        self.parts["2"].append(c)
        return c

    def section(self, guid, name, material, *dimensions):
        """A section of the parametric ones - RectangleCS, Rectangle Hollow CS, CircleCS - its
        dimensions in the order of its inputs after the name, then the material."""
        c = component(self.grid, guid, name, name, SECTIONS, MAIN, gap=30)
        words(inp(c, 0), name)
        for k, value in enumerate(dimensions):
            number(inp(c, k + 1), value)
        wire(inp(c, len(dimensions) + 1), out(material))
        self.parts["2"].append(c)
        return c

    def elements(self, name, lines, section, truss=False, zaxis=None):
        """Elements on one of the geometry's outputs, with a section already made."""
        beam = component(self.grid, BEAM, "ForceBeamColumn", name, MEMBERS, MAIN, gap=30)
        if truss:
            switch_unit(beam, TRUSS)
        wire(inp(beam, T_LINE if truss else B_LINE), self.g(lines))
        wire(inp(beam, T_SECTION if truss else B_SECTION), out(section))
        if zaxis is not None:
            vector(inp(beam, B_ZAXIS), *zaxis)
        self.members.append(beam)
        self.parts["2"].append(beam)
        return beam

    def slabs(self, name, meshes, thickness, concrete_name="Slab concrete"):
        """Concrete slabs, thickness thick - a number, or a slider - as ASD Shells on a mesh output,
        named, or a parameter."""
        concrete = component(self.grid, ND_MATERIAL, "nD", concrete_name, SECTIONS, MAIN, gap=30)
        words(inp(concrete, 0), "Concrete")
        number(inp(concrete, 1), 30e6)
        number(inp(concrete, 2), 30e6 / 2.4)
        number(inp(concrete, 3), 0.2)
        number(inp(concrete, 4), 2500)
        inp(concrete, 5).PersistentData.Append(GH_Colour(AT_REST))
        typed = isinstance(thickness, float)
        plate = component(self.grid, PLATE_FIBER, "Plate Fiber Section",
                          "{0:g} mm".format(thickness * 1000) if typed else "Thickness", SECTIONS, MAIN, gap=30)
        if typed:
            number(inp(plate, 1), thickness)
        else:
            wire(inp(plate, 1), thickness)
        wire(inp(plate, 2), out(concrete))
        shell = component(self.grid, SHELL, "ASD Shell", name, MEMBERS, MAIN, gap=30)
        wire(flat(inp(shell, 0)), self.g(meshes) if isinstance(meshes, str) else meshes)
        wire(inp(shell, 1), out(plate))
        self.members.append(shell)
        self.parts["2"] += [concrete, plate, shell]
        return shell

    def earthquake(self, direction):
        """El Centro along direction: the record from the Duration and Scale sliders, a Path Time
        Series of it and a Uniform Excitation pattern."""
        record = python_component(self.grid, "El Centro 1940", "elcentro",
                                  [("Duration", self.as_float), ("Scale", self.as_float)],
                                  ["Times", "Accelerations", "Steps"], WIDTHS, LOADED)
        wire(inp(record, 0), self.sliders["Duration"])
        wire(inp(record, 1), self.sliders["Scale"])
        series = component(self.grid, PATH_SERIES, "Path Time Series", "Ground acceleration", VECTORS, LOADED, gap=30)
        wire(inp(series, 0), out(record, 1))
        wire(inp(series, 1), out(record, 2))
        pattern = component(self.grid, LOAD_PATTERN, "Load Pattern", "El Centro along " + direction, PATTERNS, LOADED, gap=30)
        switch_unit(pattern, UNIFORM_EXCITATION)
        words(inp(pattern, 0), direction)
        wire(inp(pattern, 1), out(series))
        self.patterns.append(pattern)
        self.record = record
        self.parts["4"] += [record, series, pattern]
        return pattern

    def transient(self):
        """Assemble, and a transient analysis through the record: Newmark's average acceleration, a
        step of 0.02 s as the record has, Rayleigh damping of the Damping slider's share of critical
        at two frequencies, and the Linear algorithm, one solve a step, as an elastic model needs."""
        a = component(self.grid, ASSEMBLE, "Assemble Model", self.title, MODEL, MAIN)
        wire(flat(inp(a, 0)), *[out(m) for m in self.members])
        wire(flat(inp(a, 1)), *[out(x) for x in self.supports])
        wire(flat(inp(a, 2)), *[out(x) for x in self.patterns])
        if self.constraints:
            wire(flat(inp(a, 3)), *[out(x) for x in self.constraints])
        if self.recorders:
            wire(inp(a, 4), *[out(x) for x in self.recorders])
        zeta, f1, f2 = self.sliders["Damping"], self.sliders["F1"], self.sliders["F2"]
        # alphaM = 2 zeta w1 w2 / (w1 + w2) and betaK = 2 zeta / (w1 + w2), w = 2 pi f, zeta in %.
        alpha = expression(self.grid, "f(z,a,b)=0.1256637*z*a*b/(a+b)", [zeta, f1, f2], "alphaM", MODEL, MAIN, gap=30)
        beta = expression(self.grid, "f(z,a,b)=0.0031831*z/(a+b)", [zeta, f1, f2], "betaK", MODEL, MAIN, gap=30)
        damping = component(self.grid, DAMPING, "Damping", "Rayleigh", MODEL, MAIN, gap=30)
        wire(inp(damping, 0), out(alpha))
        wire(inp(damping, 3), out(beta))
        integrator = component(self.grid, INTEGRATOR, "Integrator", "Newmark", MODEL, MAIN, gap=30)
        switch_unit(integrator, NEWMARK)
        steps = component(self.grid, ANALYSIS_STEP, "Analysis Step", "0.02 s steps", MODEL, MAIN, gap=30)
        wire(inp(steps, 0), out(self.record, 3))
        number(inp(steps, 1), 0.02)
        settings = component(self.grid, ANALYSIS_SETTINGS, "Analysis Settings", "Transient", ANALYSIS, MAIN)
        wire(inp(settings, A_DAMPING), out(damping))
        words(inp(settings, A_ALGORITHM), "Linear")
        wire(inp(settings, A_INTEGRATOR), out(integrator))
        words(inp(settings, A_TYPE), "Transient")
        wire(inp(settings, A_STEPS), out(steps))
        run = component(self.grid, RUN_ANALYSIS, "Run Analysis", None, ANALYSIS, MAIN, gap=30)
        wire(inp(run, 0), out(a))
        wire(inp(run, 1), out(settings))
        self.assembled, self.run = a, run
        self.parts["5"] += [a, alpha, beta, damping, integrator, steps, settings, run]
        return run

    def shaking(self, name, scale, top, step, bands=False, supports=1.0, steps=1000, wires=True):
        """A Model View of the analysed model at the Step slider's step, deformed scale times, its
        colours over 0 to top mm for every step alike, with a Legend; its supports drawn supports times
        their size, which is a metre or so."""
        stepper = slider(self.grid, "Step", 0, steps, step, 0, VIEWS, SHOWN, gap=30)
        view = self.deformed(name, out(self.run), scale, title="Displacement [mm]", loads=False, bands=bands)
        # The elements' edges and the supports: what each piece is, and where it is held. A tower of
        # thousands of members is drawn without the edges, which would turn it black.
        menu_setting(view, "_ckWires", "Active", wires)
        menu_setting(view, "_ckShowSupports", "Active", True)
        menu_setting(view, "_slSupportScale", "Value", supports)
        wire(inp(view, V_STEP), stepper)
        inp(view, V_RANGE).PersistentData.Clear()
        inp(view, V_RANGE).PersistentData.Append(GH_Interval(Interval(0.0, top)))
        self.parts["6"].append(stepper)
        return view

    def floors(self):
        """Every floor a rigid diaphragm, driven from one of its nodes, and its mass shared between its
        nodes, from the geometry's Floors, Masters, MassPoints and Masses; and a recorder of the
        displacements alone, which is all a view of the shaking reads, for a tower of thousands of
        members through the whole record."""
        d = component(self.grid, RIGID_DIAPHRAGM, "Rigid Diaphragm", "A diaphragm a floor", MEMBERS, LOADED, gap=30)
        wire(inp(d, 0), self.g("Floors"))
        wire(inp(d, 1), self.g("Masters"))
        m = component(self.grid, MASS_POINT, "Mass Point", "Floor masses", LOADS, LOADED, gap=30)
        wire(inp(m, 0), self.g("MassPoints"))
        wire(inp(m, 1), self.g("Masses"))
        self.constraints.append(d)
        # Assemble takes masses with the load patterns.
        self.patterns.append(m)
        self.parts["3"].append(d)
        self.parts["4"].append(m)
        self.displacements_only()
        return d, m

    def displacements_only(self):
        record = recorder(self.grid, "Displacements only", ["displacement"], MODEL, MAIN, gap=30)
        self.recorders.append(record)
        self.parts["5"].append(record)
        return record

    def box(self, name, material, h, b, t):
        """A Rectangle Hollow CS h deep, b wide, its walls t thick."""
        return self.section(RECTANGLE_HOLLOW, name, material, h, b, t, t, t)

    def at_rest(self, colour_by=BY_MATERIAL):
        """A Model View of the structure at rest, as lines, light grey by its material."""
        view = component(self.grid, MODEL_VIEW, "Model View", self.title + " at rest", VIEWS, SHOWN, gap=30)
        wire(inp(view, V_MODEL), out(self.assembled))
        menu_setting(view, "_ckExtruded", "Active", False)
        menu_setting(view, "_ckReleases", "Active", False)
        menu_setting(view, "_ddColourBy", "Value", colour_by)
        self.parts["6"].append(view)
        return view

    def loads_view(self):
        """A Model View of the assembled model with its supports and loads. Show Values, in its Loads
        menu, writes what each is worth: on a structure this size they crowd the picture, so it opens off."""
        view = component(self.grid, MODEL_VIEW, "Model View", self.title + " loads", VIEWS, SHOWN, gap=30)
        wire(inp(view, V_MODEL), out(self.assembled))
        for field in ("_ckShowSupports", "_ckShowLoads"):
            menu_setting(view, field, "Active", True)
        menu_setting(view, "_ckReleases", "Active", False)
        self.parts["6"].append(view)
        return view

    def finish(self, names, about):
        self.grid.arrange(300, 300, stages=(GEOMETRY_, SECTIONS, WIDTHS, MODEL, VIEWS))
        colours = {"1": RED, "2": ORANGE, "3": PURPLE, "4": GREEN, "5": BLUE, "6": GREY, "7": GREY}
        for key in sorted(self.parts):
            if self.parts[key]:
                group(self.doc, key + " - " + names[key], self.parts[key], colours[key])
        note(self.doc, self.title, about, 300, 60, 900, 200)


# ---------------------------------------------------------------- 1 - a steel hall roofed by trusses

def hall(doc):
    s = Structure(doc, "Truss roof hall", "hall", [
        ("Span", "Span [m]", 12, 40, 24, 1),
        ("Bays", "Bays", 2, 10, 5, 0),
        ("Spacing", "Frame spacing [m]", 4, 8, 6, 1),
        ("Eaves", "Eaves height [m]", 4, 10, 6, 1),
        ("Depth", "Truss depth [m]", 1, 4, 2.4, 1),
        ("Panels", "Panels", 4, 16, 8, 0),
    ], [
        ("Finishes", "Roof finishes [kN/m2]", 0, 1.5, 0.5, 2),
        ("Snow", "Snow [kN/m2]", 0, 3, 1.0, 2),
    ])
    s.geometry(["Columns", "Chords", "Webs", "Purlins", "PurlinWidths", "Bracing", "Struts", "Feet"])
    s.steel()
    columns = s.member("Columns", "Columns", "HEA300")
    chords = s.member("Chords", "Chords", "HEA160")
    webs = s.member("Webs", "Webs", "88.9x5", truss=True)
    purlins = s.member("Purlins", "Purlins", "IPE180")
    bracing = s.member("Bracing", "Bracing", "60.3x4", truss=True)
    struts = s.member("Struts", "Struts", "114.3x5")
    s.support("Pinned feet", "Feet", (True, True, True, False, False, False))
    finishes = s.line_load("Finishes", [purlins], UNIT_Z, -1, "Finishes", s.g("PurlinWidths"))
    snow = s.line_load("Snow", [purlins], UNIT_Z, -1, "Snow", s.g("PurlinWidths"))
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    # The ultimate limit state, snow leading: 1.35 on what is permanent, 1.5 on the snow.
    s.pattern("Permanent x1.35", [weight, finishes], 1.35)
    s.pattern("Snow x1.5", [snow], 1.5)
    run = s.assemble()
    s.deformed("Hall deformed", out(run), 60)
    s.at_rest()
    s.loads_view()
    s.finish({"1": "Geometry: frames of trusses on columns, joined by purlins",
              "2": "Members: steel sections from the library, the webs and bracing as trusses",
              "3": "Supports: each column pinned at its foot",
              "4": "Loads: own weight, finishes and snow on the purlins, factored",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 60 times, and the loads"},
             "Truss roof hall - a steel hall roofed by Pratt trusses on columns.\n\n"
             "Each frame is a Pratt truss, its diagonals falling towards the middle, on two HEA columns that rise to "
             "the top chord: the bottom chord meets them at the eaves, so each frame stands on pinned feet. Purlins at "
             "the truss's top nodes carry the roof, and crosses in the end bays - in the roof and in the long walls - "
             "hold it lengthways.\n\n"
             "The roof finishes and the snow are typed in kN/m2 and put on the purlins, each times the width of roof "
             "it carries. With the frames' own weight, they are factored for the ultimate limit state.")


# ---------------------------------------------------------------- 2 - a steel tower with a stair

def tower(doc):
    s = Structure(doc, "Steel tower", "tower", [
        ("Width", "Width [m]", 3, 6, 4, 1),
        ("Levels", "Levels", 2, 10, 6, 0),
        ("Storey", "Storey height [m]", 2.5, 5, 3.5, 1),
    ], [
        ("Wind", "Wind [kN/m2]", 0, 2, 1.0, 2),
        ("Imposed", "Stair load [kN/m2]", 0, 5, 4.0, 1),
    ])
    s.geometry(["Legs", "WindwardLegs", "Bracing", "Landings", "Strip", "Stringers", "Feet"])
    s.steel()
    legs = s.member("Legs", "Legs", "168.3x6.3")
    windward = s.member("Windward legs", "WindwardLegs", "168.3x6.3")
    bracing = s.member("Bracing", "Bracing", "76.1x4", truss=True)
    landings = s.member("Landings", "Landings", "IPE180")
    strip = s.member("Landing strip", "Strip", "IPE180")
    stringers = s.member("Stringers", "Stringers", "IPE160")
    s.support("Pinned feet", "Feet", (True, True, True, False, False, False))
    # Each windward leg takes the wind on half the tower's width; each stringer and landing beam, the
    # people on half the stair's metre.
    wind = s.line_load("Wind", [windward], UNIT_X, 1, "Wind", s.sliders["Width"], 0.5)
    stair = s.line_load("Stair", [strip, stringers], UNIT_Z, -1, "Imposed", None, 0.5)
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    s.pattern("Own weight", [weight])
    s.pattern("Stair", [stair])
    s.pattern("Wind", [wind])
    run = s.assemble()
    s.deformed("Tower deformed", out(run), 120)
    s.at_rest()
    modes = component(s.grid, NATURAL_VIBRATION, "Natural Vibration Analysis", None, ANALYSIS, MAIN, gap=30)
    wire(inp(modes, 0), out(s.assembled))
    integer(inp(modes, 1), 6)
    frequencies = panel(s.grid, "Frequencies [Hz]", out(modes, 3), 160, 120, ANALYSIS, MAIN, gap=30)
    s.parts["5"] += [modes, frequencies]
    s.deformed("Tower first mode", out(modes), 1, step=0, title="First mode", loads=False, palette="mode")
    s.loads_view()
    s.finish({"1": "Geometry: four legs, a landing every storey, crossed faces, a stair inside",
              "2": "Members: tubes for the legs and bracing, IPEs for the landings and stair",
              "3": "Supports: each leg and the first flight pinned at the ground",
              "4": "Loads: own weight, people on the stair, wind on the windward face",
              "5": "Analysis: static, and the natural modes",
              "6": "Results: the deformed shape, drawn 120 times, and the first mode"},
             "Steel tower - a lattice tower with a stair climbing inside it.\n\n"
             "Four tube legs, crossed bracing on every face of every storey and a landing at every level. A straight "
             "flight climbs each storey along a strip a metre wide, turning back at each landing.\n\n"
             "The wind is typed in kN/m2: each windward leg takes it over half the tower's width, as a line load "
             "along +x. People on the stair are typed in kN/m2 too, over half the stair's width on each "
             "stringer and each beam of the landing strip. The Natural Vibration Analysis gives the tower's first "
             "six modes; the second Model View draws the first.")


# ---------------------------------------------------------------- 3 - a canopy on T-shaped frames

def canopy(doc):
    s = Structure(doc, "Roof canopy", "canopy", [
        ("Frames", "Frames", 2, 12, 6, 0),
        ("Spacing", "Frame spacing [m]", 3, 9, 6, 1),
        ("Height", "Height [m]", 3, 8, 4.5, 1),
        ("Cantilever", "Cantilever [m]", 2, 7, 4, 1),
        ("Rise", "Rise to the tip [m]", 0, 1.5, 0.4, 2),
        ("Purlins", "Purlins a side", 1, 6, 3, 0),
    ], [
        ("Left", "Snow on the left [kN/m2]", 0, 3, 1.2, 2),
        ("Right", "Snow on the right [kN/m2]", 0, 3, 0.4, 2),
    ])
    s.geometry(["Columns", "Arms", "Purlins", "LeftWidths", "RightWidths", "Bracing", "Feet"])
    s.steel()
    s.member("Columns", "Columns", "HEA240")
    s.member("Arms", "Arms", "IPE270")
    purlins = s.member("Purlins", "Purlins", "IPE140")
    s.member("Bracing", "Bracing", "60.3x4", truss=True)
    s.support("Built-in feet", "Feet", (True,) * 6)
    left = s.line_load("Snow left", [purlins], UNIT_Z, -1, "Left", s.g("LeftWidths"))
    right = s.line_load("Snow right", [purlins], UNIT_Z, -1, "Right", s.g("RightWidths"))
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    s.pattern("Own weight", [weight])
    s.pattern("Snow", [left, right])
    run = s.assemble()
    s.deformed("Canopy deformed", out(run), 30)
    s.at_rest()
    s.loads_view()
    s.finish({"1": "Geometry: T-shaped frames in a row, joined by purlins",
              "2": "Members: HEA columns, IPE arms and purlins, a crossed bay",
              "3": "Supports: each column built in at its foot",
              "4": "Loads: own weight, and snow lying deeper on the left",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 30 times, and the loads"},
             "Roof canopy - a row of T-shaped frames, each a column with an arm cantilevering each way.\n\n"
             "Purlins join the frames at the column heads and along the arms; a cross between two columns holds the "
             "row lengthways. The columns are built in at their feet.\n\n"
             "The snow is typed in kN/m2 for each side: more on the left than on the right, as wind drifts it, so "
             "the frames lean and twist. Each purlin takes it over the width of roof it carries, on plan.")


# ---------------------------------------------------------------- 4 - a facade stiffened by vertical trusses

def facade(doc):
    s = Structure(doc, "Vertical truss facade", "facade", [
        ("Bays", "Bays", 2, 16, 8, 0),
        ("BayWidth", "Bay width [m]", 1, 3, 1.5, 2),
        ("Height", "Height [m]", 6, 16, 10.8, 1),
        ("Levels", "Transom levels", 3, 10, 6, 0),
        ("Depth", "Truss depth [m]", 0.4, 2, 0.9, 2),
        ("TrussEvery", "A truss every ... mullions", 1, 4, 1, 0),
    ], [
        ("Wind", "Wind [kN/m2]", 0, 2.5, 1.2, 2),
        ("Glass", "Glass [kN/m2]", 0, 1.5, 0.5, 2),
    ])
    s.geometry(["Mullions", "MullionWidths", "Transoms", "Chords", "Posts", "Diagonals", "Base", "Tops",
                "ChordBase", "ChordTops", "PanelHeight"])
    s.steel()
    # The mullions' depth across the wall, along y, which the wind bends them in: local z along x.
    mullions = s.member("Mullions", "Mullions", "120x60x4", zaxis=(1, 0, 0))
    transoms = s.member("Transoms", "Transoms", "100x50x4")
    s.member("Inner chords", "Chords", "76.1x4")
    s.member("Posts", "Posts", "48.3x4", truss=True)
    s.member("Diagonals", "Diagonals", "48.3x4", truss=True)
    s.support("Feet", "Base", (True, True, True, False, False, True))
    s.support("Tops", "Tops", (False, True, False, False, False, True))
    s.support("Chord feet", "ChordBase", (True, True, True, False, False, True))
    s.support("Chord tops", "ChordTops", (True, True, False, False, False, True))
    wind = s.line_load("Wind", [mullions], UNIT_Y, 1, "Wind", s.g("MullionWidths"))
    glass = s.line_load("Glass", [transoms], UNIT_Z, -1, "Glass", s.g("PanelHeight"))
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    s.pattern("Own weight", [weight, glass])
    s.pattern("Wind", [wind])
    run = s.assemble()
    s.deformed("Facade deformed", out(run), 200)
    s.at_rest()
    s.loads_view()
    s.finish({"1": "Geometry: mullions and transoms, and a vertical truss behind each mullion",
              "2": "Members: hollow mullions and transoms, tube chords, posts and diagonals as trusses",
              "3": "Supports: the wall held at its foot and its top only",
              "4": "Loads: own weight and the glass on the transoms, wind on the mullions",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 200 times, and the loads"},
             "Vertical truss facade - a glass wall stiffened by vertical trusses, as a lobby's is.\n\n"
             "The wall is held only at its foot and its top, 10.8 m apart. Behind every mullion - or every second, "
             "third or fourth, with the slider - a vertical truss stiffens it against the wind: the mullion is its "
             "outer chord, a tube behind it the inner chord, with a post at every transom and diagonals between.\n\n"
             "The wind is typed in kN/m2 and put on the mullions, each times the width of wall it catches. The glass "
             "hangs on the transoms, its weight in kN/m2 times the height of glass each carries.")


# ---------------------------------------------------------------- 5 - a barrel vault under wind

def vault(doc):
    s = Structure(doc, "Barrel vault", "vault", [
        ("Span", "Span [m]", 8, 30, 16, 1),
        ("Rise", "Rise [m]", 2, 10, 5, 1),
        ("Length", "Length [m]", 8, 40, 24, 1),
        ("Strips", "Strips round the arc", 8, 32, 16, 0),
        ("Divisions", "Divisions along it", 4, 48, 24, 0),
        ("Windward", "cpe windward quarter", -1.5, 1, 0.6, 2),
        ("Crown", "cpe middle half", -2, 0.5, -1.0, 2),
        ("Leeward", "cpe leeward quarter", -1.5, 0.5, -0.4, 2),
    ], [
        ("Pressure", "Peak velocity pressure [kN/m2]", 0, 2, 0.8, 2),
    ])
    s.geometry(["Strip", "Names", "Coefficients", "Normals", "Feet"])
    concrete = component(s.grid, ND_MATERIAL, "nD", "Concrete", SECTIONS, MAIN)
    words(inp(concrete, 0), "Concrete")
    number(inp(concrete, 1), 33e6)
    number(inp(concrete, 2), 33e6 / 2.4)
    number(inp(concrete, 3), 0.2)
    number(inp(concrete, 4), 2500)
    inp(concrete, 5).PersistentData.Append(GH_Colour(AT_REST))
    plate = component(s.grid, PLATE_FIBER, "Plate Fiber Section", "120 mm", SECTIONS, MAIN, gap=30)
    number(inp(plate, 1), 0.12)
    wire(inp(plate, 2), out(concrete))
    shell = component(s.grid, SHELL, "ASD Shell", "Vault", MEMBERS, MAIN, gap=30)
    wire(inp(shell, 0), s.g("Strip"))
    wire(inp(shell, 1), out(plate))
    wire(inp(shell, 5), s.g("Names"))
    s.members.append(shell)
    s.parts["2"] += [concrete, plate, shell]
    s.support("Pinned feet", "Feet", (True, True, True, False, False, False))
    # Each strip's force per square metre: its coefficient times the pressure, along -Normal.
    amount = expression(s.grid, "f(c,q)=-c*q", [s.g("Coefficients"), s.sliders["Pressure"]], "Wind [kN/m2]",
                        WIDTHS, LOADED, gap=30)
    force = component(s.grid, MULTIPLY, "Multiply", "Wind on each strip", VECTORS, LOADED, gap=30)
    wire(inp(force, 0), s.g("Normals"))
    wire(inp(force, 1), out(amount))
    wind = component(s.grid, MESH_LOAD, "Mesh Load", "Wind", LOADS, LOADED, gap=30)
    wire(flat(inp(wind, 0)), s.g("Names"))
    wire(flat(inp(wind, 1)), out(force))
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"] += [amount, force, wind, weight]
    s.pattern("Own weight", [weight])
    s.pattern("Wind", [wind])
    run = s.assemble()
    # No view at rest: a shell at rest, drawn solid, would hide the deformed one.
    s.deformed("Vault deformed", out(run), 200, bands=True)
    s.loads_view()
    s.finish({"1": "Geometry: the vault's strips, each facing one way, and their wind coefficients",
              "2": "Members: 120 mm of concrete, as ASD shells, a strip an ElementId",
              "3": "Supports: pinned along both long edges",
              "4": "Loads: own weight, and the wind on each strip along its normal",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 200 times, and the loads"},
             "Barrel vault - a concrete shell vault under wind blowing across it.\n\n"
             "The vault is an arc of a circle through its feet and its crown, cut into strips along its length. A "
             "strip's faces all face the same way, so one Mesh Load a strip acts along its normal: the strips are "
             "named, Strip_0 onwards, and the load goes on each by its name.\n\n"
             "The wind is the peak velocity pressure times a pressure coefficient for each zone - the windward "
             "quarter, the middle half and the leeward quarter - typed in from the code of practice: a positive one "
             "pushes on the vault, a negative one pulls it away.")


# ---------------------------------------------------------------- 6 - a facade held by wind trusses

def windtruss(doc):
    s = Structure(doc, "Horizontal truss facade", "windtruss", [
        ("Width", "Width [m]", 12, 40, 24, 1),
        ("Height", "Height [m]", 6, 20, 12, 1),
        ("Spacing", "Mullion spacing [m]", 1.2, 3, 2, 2),
        ("Depth", "Truss depth [m]", 0.8, 3, 1.5, 1),
        ("Trusses", "Trusses", 1, 4, 2, 0),
    ], [
        ("Wind", "Wind [kN/m2]", 0, 2.5, 1.0, 2),
    ])
    s.geometry(["Mullions", "MullionWidths", "Chords", "Posts", "Diagonals", "Base", "Tops", "TrussEnds"])
    s.steel()
    mullions = s.member("Mullions", "Mullions", "150x100x5", zaxis=(1, 0, 0))
    s.member("Chords", "Chords", "114.3x5")
    # The posts are built into the mullions: they carry the inner chord, which lies level with the
    # diagonals, and as pinned bars would have left it hanging 24 m between the building's columns.
    s.member("Posts", "Posts", "76.1x4")
    s.member("Diagonals", "Diagonals", "60.3x4", truss=True)
    s.support("Feet", "Base", (True, True, True, False, False, True))
    s.support("Tops", "Tops", (False, True, False, False, False, True))
    # Held against turning about the chords as well: the posts and diagonals are pinned bars, and the
    # inner chord had nothing else to stop it rolling.
    s.support("Building columns", "TrussEnds", (True, True, True, True, False, False))
    wind = s.line_load("Wind", [mullions], UNIT_Y, 1, "Wind", s.g("MullionWidths"))
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    s.pattern("Own weight", [weight])
    s.pattern("Wind", [wind])
    run = s.assemble()
    s.deformed("Wind truss deformed", out(run), 100)
    s.at_rest()
    s.loads_view()
    s.finish({"1": "Geometry: mullions, and horizontal trusses behind them",
              "2": "Members: hollow mullions, tube chords and posts, the diagonals as trusses",
              "3": "Supports: the mullions' feet and tops, the trusses' ends on the building's columns",
              "4": "Loads: own weight, and the wind on the mullions",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 100 times, and the loads"},
             "Horizontal truss facade - a tall glass facade held up against the wind by horizontal trusses.\n\n"
             "The mullions stand on their feet and are held across the facade at the top. Between, horizontal trusses "
             "behind the facade take them: each spans the whole width between the building's columns at its ends, "
             "its outer chord running through the mullions.\n\n"
             "The wind is typed in kN/m2 and put on the mullions, each times the width of facade it catches. The "
             "mullions carry it to the trusses and the top, the trusses to the building's columns.")


# ---------------------------------------------------------------- earthquakes

def quake(f1, f2, duration=12):
    """The sliders every earthquake has: how strong the record is, how much of it, and the damping,
    5% of critical at two frequencies that bracket the modes that matter."""
    return [
        ("Scale", "Ground motion scale", 0, 3, 1.0, 2),
        ("Duration", "Duration [s]", 2, 31, duration, 1),
        ("Damping", "Damping [%]", 0, 10, 5, 1),
        ("F1", "Damping at [Hz]", 0.1, 10, f1, 2),
        ("F2", "and at [Hz]", 0.5, 50, f2, 2),
    ]


QUAKE_STORY = ("The ground motion is El Centro, 18 May 1940, its north-south component: 0.32 g at its "
               "peak, 2 s in. Its accelerations, a Path Time Series, shake the ground under the structure "
               "through a Uniform Excitation pattern, and a transient analysis follows it step by step, "
               "0.02 s at a time. The structure's own weight is its mass; the damping, Rayleigh's, is 5% "
               "of critical at the two frequencies given. Drag Step to watch it shake.")


# Each earthquake view's deformation scale, the top of its colours, in mm, the same for every step -
# eight bands, or nine colours, of a round number each - and the size of its supports.
GIRDER_VIEW = (60, 96, 3)
CABLE_VIEW = (40, 160, 3)
ARCH_VIEW = (100, 64, 3)
BUILDING_VIEW = (40, 120, 1.5)
CITY_VIEW = (20, 320, 1)


def girder(doc):
    s = Structure(doc, "Girder bridge", "girder", [
        ("SideSpan", "Side spans [m]", 20, 60, 40, 1),
        ("MainSpan", "Main span [m]", 30, 80, 50, 1),
        ("PierHeight", "Pier height [m]", 5, 30, 18, 1),
        ("Segment", "Element length [m]", 2, 10, 5, 1),
    ], quake(0.8, 8.0))
    s.geometry(["Girders", "CrossGirders", "PierCaps", "Columns", "Struts", "Slab", "Feet", "Bearings", "Watch"])
    concrete = s.concrete()
    girder_ = s.section(RECTANGLE, "Girder 1 x 2.2 m", concrete, 1.0, 2.2)
    cross = s.section(RECTANGLE, "Cross girder 0.4 x 1.6 m", concrete, 0.4, 1.6)
    cap = s.section(RECTANGLE, "Pier cap 2 x 2.2 m", concrete, 2.0, 2.2)
    column = s.section(CIRCLE, "Column 1.6 m", concrete, 1.6, 0.8)
    strut = s.section(RECTANGLE, "Strut 1 x 1.2 m", concrete, 1.0, 1.2)
    s.elements("Girders", "Girders", girder_)
    s.elements("Cross girders", "CrossGirders", cross)
    s.elements("Pier caps", "PierCaps", cap)
    s.elements("Pier columns", "Columns", column)
    s.elements("Pier struts", "Struts", strut)
    s.slabs("Deck slab", "Slab", 0.25)
    s.support("Pier feet", "Feet", (True,) * 6)
    s.support("Sliding bearings", "Bearings", (False, True, True, False, False, False))
    s.earthquake("Y")
    s.transient()
    s.shaking("Girder shaking", GIRDER_VIEW[0], GIRDER_VIEW[1], 150, bands=True, supports=GIRDER_VIEW[2])
    s.finish({"1": "Geometry: a deck over three spans on two piers",
              "2": "Members: concrete girders, cross girders and a slab, piers of two round columns, a strut and a cap",
              "3": "Supports: the columns built in, the deck sliding along its abutments, held across",
              "4": "Ground motion: El Centro 1940 across the bridge",
              "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping",
              "6": "Results: the bridge at Step, drawn {0} times, its colours over 0 to {1} mm".format(*GIRDER_VIEW[:2])},
             "Girder bridge - a concrete deck on two girders over three spans, shaken across by an earthquake.\n\n"
             "A slab 12 m wide on two girders 6 m apart, joined by cross girders; each pier is two round columns, "
             "braced halfway up and capped by a deep cross girder. The deck is built into the piers and slides "
             "along on bearings at its ends, held there across.\n\n" + QUAKE_STORY)


def cablestayed(doc):
    s = Structure(doc, "Cable-stayed bridge", "cablestayed", [
        ("Span", "Span each side [m]", 40, 120, 80, 1),
        ("DeckHeight", "Deck height [m]", 5, 30, 10, 1),
        ("PylonHeight", "Pylon above the deck [m]", 15, 60, 35, 1),
        ("Width", "Deck width [m]", 8, 20, 12, 1),
        ("Stays", "Stays a side", 3, 12, 6, 0),
    ], quake(0.4, 5.0))
    s.geometry(["Girders", "FloorBeams", "Legs", "CrossBeams", "Cables", "Feet", "Bearings", "Watch"])
    concrete = s.concrete()
    steel = s.steel()
    girder = s.section(RECTANGLE, "Girder 1.5 x 2.5 m", concrete, 1.5, 2.5)
    floor = s.section(RECTANGLE, "Floor beam 0.6 x 1.2 m", concrete, 0.6, 1.2)
    leg = s.section(RECTANGLE_HOLLOW, "Leg 3.5 x 3.5 m", concrete, 3.5, 3.5, 0.5, 0.5, 0.5)
    cross = s.section(RECTANGLE_HOLLOW, "Cross beam 2.5 x 3 m", concrete, 2.5, 3.0, 0.4, 0.4, 0.4)
    stay = s.section(CIRCLE, "Stay 110 mm", steel, 0.11, 0.055)
    s.elements("Girders", "Girders", girder)
    s.elements("Floor beams", "FloorBeams", floor)
    s.elements("Pylon legs", "Legs", leg)
    s.elements("Pylon cross beam", "CrossBeams", cross)
    s.elements("Stays", "Cables", stay, truss=True)
    s.support("Pylon feet", "Feet", (True,) * 6)
    s.support("Sliding bearings", "Bearings", (False, True, True, False, False, False))
    s.earthquake("Y")
    s.transient()
    s.shaking("Cable-stayed shaking", CABLE_VIEW[0], CABLE_VIEW[1], 150, supports=CABLE_VIEW[2])
    s.at_rest()
    s.finish({"1": "Geometry: an H-shaped pylon, a deck of two girders and floor beams, two planes of stays",
              "2": "Members: concrete pylon, girders and floor beams, steel stays as trusses",
              "3": "Supports: the pylon built in, the deck sliding along its abutments",
              "4": "Ground motion: El Centro 1940 across the bridge",
              "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping",
              "6": "Results: the bridge at Step, drawn {0} times, its colours over 0 to {1} mm".format(*CABLE_VIEW[:2])},
             "Cable-stayed bridge - two spans of a deck hung from one H-shaped pylon, shaken across by an earthquake.\n\n"
             "The deck is two concrete edge girders joined by floor beams where the stays hold them; the stays fan in "
             "two planes from the top of the pylon's legs. The girders are built into the legs and slide along their "
             "abutments.\n\n" + QUAKE_STORY)




def arch(doc):
    s = Structure(doc, "Arch bridge", "arch", [
        ("Span", "Span [m]", 40, 150, 80, 1),
        ("Rise", "Rise [m]", 8, 40, 20, 1),
        ("Columns", "Spandrel columns", 3, 15, 9, 0),
        ("DeckAbove", "Deck over the crown [m]", 1, 6, 2, 1),
    ], quake(1.0, 10.0))
    s.geometry(["Ribs", "Struts", "Girders", "FloorBeams", "SpandrelColumns", "Slab", "Springings", "Abutments", "Watch"])
    concrete = s.concrete()
    rib = s.section(RECTANGLE_HOLLOW, "Rib 3 x 1.6 m", concrete, 3.0, 1.6, 0.35, 0.3, 0.3)
    strut = s.section(RECTANGLE, "Strut 0.8 x 1 m", concrete, 0.8, 1.0)
    girder_ = s.section(RECTANGLE, "Girder 0.9 x 1.4 m", concrete, 0.9, 1.4)
    floor = s.section(RECTANGLE, "Floor beam 0.4 x 1 m", concrete, 0.4, 1.0)
    column = s.section(RECTANGLE, "Column 0.9 x 0.9 m", concrete, 0.9, 0.9)
    s.elements("Ribs", "Ribs", rib)
    s.elements("Struts", "Struts", strut)
    s.elements("Girders", "Girders", girder_)
    s.elements("Floor beams", "FloorBeams", floor)
    s.elements("Spandrel columns", "SpandrelColumns", column)
    s.slabs("Deck slab", "Slab", 0.25)
    s.support("Springings", "Springings", (True,) * 6)
    s.support("Abutments", "Abutments", (False, True, True, False, False, False))
    s.earthquake("Y")
    s.transient()
    s.shaking("Arch shaking", ARCH_VIEW[0], ARCH_VIEW[1], 150, bands=True, supports=ARCH_VIEW[2])
    s.finish({"1": "Geometry: two ribs leaning in, braced by struts, and a deck on pairs of spandrel columns",
              "2": "Members: concrete box ribs, struts, columns, girders, floor beams and a slab",
              "3": "Supports: the ribs built in at their springings, the deck sliding along its abutments, held across",
              "4": "Ground motion: El Centro 1940 across the bridge",
              "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping",
              "6": "Results: the bridge at Step, drawn {0} times, its colours over 0 to {1} mm".format(*ARCH_VIEW[:2])},
             "Arch bridge - a concrete deck arch of two ribs, shaken across by an earthquake.\n\n"
             "Two parabolic box ribs spring 12 m apart and lean in to 7 m at the crown, braced by a strut at every "
             "column. Pairs of spandrel columns carry the deck - a slab on two girders and floor beams - which runs "
             "on to the abutments, where it slides along, held across.\n\n" + QUAKE_STORY)


def building(doc):
    s = Structure(doc, "Office building", "building", [
        ("BaysX", "Bays along x", 1, 6, 3, 0),
        ("BaysY", "Bays along y", 1, 6, 3, 0),
        ("Bay", "Bay [m]", 4, 9, 6, 1),
        ("Storeys", "Storeys", 1, 20, 8, 0),
        ("StoreyHeight", "Storey height [m]", 2.8, 5, 3.5, 1),
    ], quake(1.0, 10.0))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch"])
    s.steel()
    s.member("Columns", "Columns", "HEB400")
    s.member("Beams", "Beams", "IPE450")
    s.slabs("Slabs", "Slabs", 0.2)
    s.support("Built in", "Feet", (True,) * 6)
    s.earthquake("X")
    s.transient()
    s.shaking("Building shaking", BUILDING_VIEW[0], BUILDING_VIEW[1], 150, bands=True, supports=BUILDING_VIEW[2])
    s.finish({"1": "Geometry: a steel frame and a slab at every floor",
              "2": "Members: HEB400 columns, IPE450 beams, 200 mm concrete slabs as ASD Shells",
              "3": "Supports: every column built in at the ground",
              "4": "Ground motion: El Centro 1940 along x",
              "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping",
              "6": "Results: the building at Step, drawn {0} times, in iso-surfaces over 0 to {1} mm".format(*BUILDING_VIEW[:2])},
             "Office building - a steel frame of eight storeys, shaken by an earthquake.\n\n"
             "HEB columns, built in at the ground, IPE beams and a concrete slab at every floor, meshed twice a bay "
             "so that it shares the beams' nodes.\n\n" + QUAKE_STORY)


def city(doc):
    s = Structure(doc, "Tiny city", "city", [
        ("Rows", "Rows", 1, 4, 4, 0),
        ("ColumnsPerRow", "Buildings a row", 1, 4, 4, 0),
        ("Street", "Street [m]", 4, 20, 8, 1),
        ("Bay", "Bay [m]", 3, 8, 5, 1),
        ("StoreyHeight", "Storey height [m]", 2.8, 5, 3.5, 1),
    ], quake(0.5, 5.0))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch", "Storeys"])
    s.steel()
    s.member("Columns", "Columns", "HEB300")
    s.member("Beams", "Beams", "IPE400")
    s.slabs("Slabs", "Slabs", 0.2)
    s.support("Built in", "Feet", (True,) * 6)
    s.earthquake("X")
    s.transient()
    s.shaking("City shaking", CITY_VIEW[0], CITY_VIEW[1], 150, bands=True, supports=CITY_VIEW[2])
    s.finish({"1": "Geometry: sixteen buildings, from two storeys to twenty, of five shapes",
              "2": "Members: HEB300 columns, IPE400 beams, 200 mm concrete slabs as ASD Shells",
              "3": "Supports: every column built in at the ground",
              "4": "Ground motion: El Centro 1940 along x, under every building at once",
              "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping",
              "6": "Results: the city at Step, drawn {0} times, in iso-surfaces over 0 to {1} mm".format(*CITY_VIEW[:2])},
             "Tiny city - sixteen buildings of two to twenty storeys, shaken by the same earthquake.\n\n"
             "Rectangles, Ls, a tower on a podium, blocks with a penthouse or balconies: steel frames on a grid of "
             "bays, a slab at every floor. A low one is stiff, and follows the ground; a tall one is soft and slow, "
             "and sways far behind it; an L, or a tower off its podium's middle, twists as it sways.\n\n" + QUAKE_STORY)


# ---------------------------------------------------------------- buildings that are not straight

BUILDING_STORY = ("The ground motion is El Centro, 18 May 1940, its north-south component: 0.32 g at its "
                  "peak, 2 s in. Its accelerations, a Path Time Series, shake the ground under the building "
                  "through a Uniform Excitation pattern, and a transient analysis follows it step by step, "
                  "0.02 s at a time. The building's own weight is its mass; the damping, Rayleigh's, is 5% "
                  "of critical at the two frequencies given. Only the displacements are recorded. Drag Step "
                  "to watch it shake.")

TOWER_STORY = ("The ground motion is El Centro, 18 May 1940, its north-south component: 0.32 g at its peak, "
               "2 s in, all 31 s of it, as a tower this tall swings slowly. Its accelerations, a Path Time "
               "Series, shake the ground under the tower through a Uniform Excitation pattern, and a transient "
               "analysis follows it step by step, 0.02 s at a time. Every floor is a Rigid Diaphragm, its mass "
               "- Floor mass kg/m2 of its area: slab, finishes, facade and some of what is in it - shared "
               "between its nodes as Mass Points; the members' own weight adds to it. The damping, Rayleigh's, "
               "is 2% of critical at the two frequencies given, as a tall building has. Only the "
               "displacements are recorded. Drag Step to watch it shake.")

# Each view's deformation scale, the top of its colours in mm, the same for every step, and the size of
# its supports.
TWISTING_VIEW = (15, 400, 1.5)
LEANING_VIEW = (15, 480, 1.5)
CURVING_VIEW = (25, 240, 1.5)
CRESCENT_VIEW = (25, 200, 1.5)
BUNDLED_VIEW = (30, 800, 4)
BRACED_VIEW = (40, 560, 4)
BUTTRESSED_VIEW = (20, 800, 4)
OUTRIGGER_VIEW = (20, 720, 4)
DIAGRID_VIEW = (40, 800, 4)
BUILDING_STEPS, TOWER_STEPS = 1000, 1550


def _building_groups(geometry, members, along, view, bands=True):
    return {"1": "Geometry: " + geometry,
            "2": "Members: " + members,
            "3": "Supports: every column built in at the ground",
            "4": "Ground motion: El Centro 1940 along " + along,
            "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping, the displacements recorded",
            "6": "Results: the building at Step, drawn {0} times, {1}over 0 to {2} mm".format(
                view[0], "in iso-surfaces " if bands else "its colours ", view[1])}


def _tower_groups(geometry, members, view):
    return {"1": "Geometry: " + geometry,
            "2": "Members: " + members,
            "3": "Supports and floors: built in at the ground, every floor a rigid diaphragm",
            "4": "Ground motion and masses: El Centro 1940 along x, each floor's mass at its nodes",
            "5": "Transient analysis: Newmark, 0.02 s steps, Rayleigh damping, the displacements recorded",
            "6": "Results: the tower at Step, drawn {0} times, its colours over 0 to {1} mm".format(*view[:2])}


def _shake(s, name, view, steps, along="X", bands=True, wires=True):
    s.earthquake(along)
    s.transient()
    return s.shaking(name, view[0], view[1], 150, bands=bands, supports=view[2], steps=steps, wires=wires)


def twisting(doc):
    s = Structure(doc, "Twisting tower", "twisting", [
        ("Bays", "Bays each way", 2, 6, 4, 0),
        ("Bay", "Bay [m]", 5, 10, 7, 1),
        ("Storeys", "Storeys", 5, 60, 30, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.5, 1),
        ("Twist", "Twist [deg]", 0, 180, 90, 0),
    ], quake(0.3, 3.0, 20))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch"])
    concrete = s.concrete()
    s.elements("Columns", "Columns", s.section(RECTANGLE, "Column 1 x 1 m", concrete, 1.0, 1.0))
    s.elements("Beams", "Beams", s.section(RECTANGLE, "Beam 0.45 x 0.8 m", concrete, 0.45, 0.8))
    s.slabs("Slabs", "Slabs", 0.22)
    s.support("Built in", "Feet", (True,) * 6)
    s.displacements_only()
    _shake(s, "Twisting shaking", TWISTING_VIEW, BUILDING_STEPS)
    s.finish(_building_groups("a square tower whose every floor turns a little more than the one under it",
                              "concrete columns 1 x 1 m and beams 0.45 x 0.8 m, 220 mm slabs as ASD Shells",
                              "x", TWISTING_VIEW),
             "Twisting tower - thirty storeys, each floor turned 3 degrees further than the one under it, shaken by an "
             "earthquake.\n\nA concrete frame of four bays by four, 7 m each, a slab at every floor meshed twice a bay: "
             "the columns lean round the tower from floor to floor, a quarter turn from the ground to the roof.\n\n"
             + BUILDING_STORY)


def leaning(doc):
    s = Structure(doc, "Leaning towers", "leaning", [
        ("BaysX", "Bays along x", 1, 6, 3, 0),
        ("BaysY", "Bays along y", 1, 6, 4, 0),
        ("Bay", "Bay [m]", 5, 10, 7.5, 1),
        ("Storeys", "Storeys, west tower", 5, 40, 26, 0),
        ("EastStoreys", "Storeys, east tower", 5, 40, 20, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.6, 1),
        ("Lean", "Lean [deg]", 0, 25, 15, 1),
        ("Gap", "Apart at the ground [m]", 20, 120, 60, 1),
    ], quake(0.33, 3.0, 20))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch"])
    steel = s.steel()
    s.elements("Columns", "Columns", s.box("Column box 600 x 40", steel, 0.6, 0.6, 0.04))
    s.member("Beams", "Beams", "HEB600")
    s.slabs("Slabs", "Slabs", 0.2)
    s.support("Built in", "Feet", (True,) * 6)
    s.displacements_only()
    _shake(s, "Leaning shaking", LEANING_VIEW, BUILDING_STEPS)
    s.finish(_building_groups("two towers of different heights leaning towards each other over a street",
                              "steel box columns 600 x 40 mm, HEB600 beams, 200 mm slabs as ASD Shells",
                              "x, the way they lean", LEANING_VIEW),
             "Leaning towers - two towers of twenty-six and twenty storeys, leaning 15 degrees towards each other, shaken "
             "by an earthquake along the way they lean.\n\nEach a steel frame of three bays by four, 7.5 m each, every floor "
             "moved a little further over the street than the one under it, 60 m apart at the ground. Only the "
             "shaking is analysed: the pull of their own weight, leaning, is not part of it.\n\n" + BUILDING_STORY)


def curving(doc):
    s = Structure(doc, "Curving tower", "curving", [
        ("Around", "Columns round it", 8, 32, 16, 0),
        ("Radius", "Radius [m]", 10, 30, 18, 1),
        ("Core", "Core radius [m]", 4, 12, 7, 1),
        ("Storeys", "Storeys", 5, 60, 40, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.6, 1),
        ("Bend", "Bend at half height [m]", 0, 30, 12, 1),
    ], quake(0.45, 4.0, 20))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch"])
    steel = s.steel()
    s.elements("Columns", "Columns", s.box("Column box 700 x 40", steel, 0.7, 0.7, 0.04))
    s.member("Beams", "Beams", "IPE600")
    s.slabs("Slabs", "Slabs", 0.2)
    s.support("Built in", "Feet", (True,) * 6)
    s.displacements_only()
    _shake(s, "Curving shaking", CURVING_VIEW, BUILDING_STEPS)
    s.finish(_building_groups("a round tower whose floors move out and back as it rises",
                              "steel box columns 700 x 40 mm, IPE600 ring and radial beams, 200 mm slabs as ASD Shells",
                              "x, the way it bends", CURVING_VIEW),
             "Curving tower - a round tower of forty storeys that bows out 12 m half way up and comes back over its foot, "
             "shaken by an earthquake.\n\nThree rings of sixteen steel columns round an open core, ring and radial beams "
             "and a slab at every floor.\n\n" + BUILDING_STORY)


def crescent(doc):
    s = Structure(doc, "Crescent", "crescent", [
        ("Bays", "Bays along it", 4, 20, 12, 0),
        ("Depth", "Bays deep", 1, 4, 2, 0),
        ("Bay", "Bay [m]", 5, 10, 7.5, 1),
        ("Radius", "Inner radius [m]", 15, 80, 30, 1),
        ("Sweep", "Sweep [deg]", 30, 270, 150, 0),
        ("Storeys", "Storeys", 2, 30, 14, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.5, 1),
    ], quake(0.48, 5.0, 20))
    s.geometry(["Columns", "Beams", "Slabs", "Feet", "Watch"])
    concrete = s.concrete()
    s.elements("Columns", "Columns", s.section(RECTANGLE, "Column 0.7 x 0.7 m", concrete, 0.7, 0.7))
    s.elements("Beams", "Beams", s.section(RECTANGLE, "Beam 0.4 x 0.7 m", concrete, 0.4, 0.7))
    s.slabs("Slabs", "Slabs", 0.22)
    s.support("Built in", "Feet", (True,) * 6)
    s.displacements_only()
    _shake(s, "Crescent shaking", CRESCENT_VIEW, BUILDING_STEPS)
    s.finish(_building_groups("a block curved in plan, 150 degrees of a ring",
                              "concrete columns 0.7 x 0.7 m and beams 0.4 x 0.7 m, 220 mm slabs as ASD Shells",
                              "x, across its middle", CRESCENT_VIEW),
             "Crescent - a block of fourteen storeys curved in plan, shaken by an earthquake across it.\n\nA concrete "
             "frame twelve bays along an arc of 150 degrees and two bays deep, a slab at every floor. Shaken across its "
             "middle, it twists as well as sways: its ends swing further than its middle.\n\n" + BUILDING_STORY)


# ---------------------------------------------------------------- towers of a hundred storeys and more

def _tower_quake(f1, f2, mass=800, duration=31):
    """A tall building's: all of the record, and 2% damping; and the mass of its floors."""
    return [
        ("Scale", "Ground motion scale", 0, 3, 1.0, 2),
        ("Duration", "Duration [s]", 2, 31, duration, 1),
        ("Damping", "Damping [%]", 0, 10, 2, 1),
        ("F1", "Damping at [Hz]", 0.05, 10, f1, 2),
        ("F2", "and at [Hz]", 0.5, 50, f2, 2),
        ("FloorMass", "Floor mass [kg/m2]", 300, 1500, mass, 0),
    ]


def bundled(doc):
    s = Structure(doc, "Bundled tube", "bundled", [
        ("Tube", "Tube [m]", 15, 30, 22.86, 2),
        ("Bays", "Columns a tube side", 2, 6, 5, 0),
        ("Storeys", "Storeys", 30, 120, 108, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 4.09, 2),
    ], _tower_quake(0.12, 2.0))
    s.inputs.append(("FloorMass", s.as_float))
    s.geometry(["Columns", "Spandrels", "Belts", "Feet", "Floors", "Masters", "MassPoints", "Masses", "Watch"])
    steel = s.steel()
    s.elements("Columns", "Columns", s.box("Column box 900 x 60", steel, 0.9, 0.9, 0.06))
    s.member("Spandrels", "Spandrels", "HEM1000")
    s.elements("Belt trusses", "Belts", s.box("Brace box 600 x 40", steel, 0.6, 0.6, 0.04))
    s.support("Built in", "Feet", (True,) * 6)
    s.floors()
    _shake(s, "Bundled shaking", BUNDLED_VIEW, TOWER_STEPS, bands=False, wires=False)
    s.finish(_tower_groups("nine square tubes bundled together, stopping at the 50th, 66th, 90th and 108th floors",
                           "steel box columns 900 x 60 mm 4.6 m apart, HEM1000 spandrels, belt trusses of braces at three floors",
                           BUNDLED_VIEW),
             "Bundled tube, after the Willis Tower in Chicago - nine square steel tubes of 22.9 m, 108 storeys and 442 m "
             "tall at the top, shaken by an earthquake.\n\nEach wall of each tube is a frame of columns 4.6 m apart tied "
             "by deep spandrels: together they are one tube, stiff as a box, cut into nine. Two tubes stop at the 50th "
             "floor, two at the 66th, three at the 90th, and two go on to the top. Belt trusses brace the walls over two "
             "storeys at three floors. A model of how the tower's structure works, not of the tower itself.\n\n" + TOWER_STORY)


def braced(doc):
    s = Structure(doc, "Braced tube", "braced", [
        ("Length", "Long side at the ground [m]", 40, 100, 80, 1),
        ("Width", "Short side at the ground [m]", 25, 70, 50, 1),
        ("TopLength", "Long side at the roof [m]", 20, 100, 48, 1),
        ("TopWidth", "Short side at the roof [m]", 15, 70, 30, 1),
        ("Storeys", "Storeys", 30, 120, 100, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.44, 2),
        ("XStoreys", "Storeys an X", 6, 30, 18, 0),
    ], _tower_quake(0.14, 2.0))
    s.inputs.append(("FloorMass", s.as_float))
    s.geometry(["Columns", "Spandrels", "Diagonals", "Feet", "Floors", "Masters", "MassPoints", "Masses", "Watch"])
    steel = s.steel()
    s.elements("Columns", "Columns", s.box("Column box 900 x 80", steel, 0.9, 0.9, 0.08))
    s.member("Spandrels", "Spandrels", "HEM1000")
    s.elements("Diagonals", "Diagonals", s.box("Diagonal box 900 x 80", steel, 0.9, 0.9, 0.08))
    s.support("Built in", "Feet", (True,) * 6)
    s.floors()
    _shake(s, "Braced shaking", BRACED_VIEW, TOWER_STEPS, bands=False, wires=False)
    s.finish(_tower_groups("a tapering box of 100 storeys, its faces crossed by diagonals, an X every 18 storeys",
                           "steel box columns and diagonals 900 x 80 mm, HEM1000 spandrels",
                           BRACED_VIEW),
             "Braced tube, after the John Hancock Center in Chicago - a tapering steel box of 100 storeys, 344 m tall, "
             "shaken by an earthquake.\n\n80 by 50 m at the ground and 48 by 30 m at the roof, its four faces crossed by "
             "diagonals from corner to corner, an X every 18 storeys and half of one at the top. The diagonals make the "
             "walls a truss: the columns, leaning in with the taper, share the load through them. A model of how the "
             "tower's structure works, not of the tower itself.\n\n" + TOWER_STORY)


def buttressed(doc):
    s = Structure(doc, "Buttressed core", "buttressed", [
        ("Core", "Core, middle to corner [m]", 8, 18, 12, 1),
        ("Corridor", "Corridor walls apart [m]", 3, 10, 6, 1),
        ("WingWidth", "Wing width [m]", 15, 40, 26, 1),
        ("Module", "Module [m]", 6, 12, 9, 1),
        ("Modules", "Modules a wing", 2, 8, 6, 0),
        ("Storeys", "Storeys", 40, 180, 160, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 3.7, 2),
        ("Spire", "Spire [m]", 0, 300, 220, 0),
    ], _tower_quake(0.14, 2.0, 900, 20))
    s.inputs.append(("FloorMass", s.as_float))
    s.geometry(["Walls", "Columns", "Beams", "Spire", "Feet", "Floors", "Masters", "MassPoints", "Masses", "Watch"])
    concrete = s.concrete()
    steel = s.steel()
    s.slabs("Walls", "Walls", 0.8, "Wall concrete")
    s.elements("Columns", "Columns", s.section(RECTANGLE, "Column 1.2 x 1.2 m", concrete, 1.2, 1.2))
    s.elements("Beams", "Beams", s.section(RECTANGLE, "Beam 0.6 x 1 m", concrete, 0.6, 1.0))
    s.elements("Spire", "Spire", s.section(CIRCLE, "Spire tube 8 m x 60 mm", steel, 8.0, 0.06))
    s.support("Built in", "Feet", (True,) * 6)
    s.floors()
    _shake(s, "Buttressed shaking", BUTTRESSED_VIEW, 1000, wires=False)
    s.finish(_tower_groups("three wings round a hexagonal core, stepping back one after another, and a spire",
                           "800 mm concrete walls as ASD Shells, concrete columns and beams, a steel spire",
                           BUTTRESSED_VIEW),
             "Buttressed core, after the Burj Khalifa in Dubai - 160 storeys of concrete walls, 592 m to the roof, and a "
             "steel spire 220 m over it, shaken by an earthquake.\n\nA hexagonal core of walls, and three wings round it, "
             "each two corridor walls with a hammerhead wall across its end: the wings buttress the core, and the core "
             "holds the wings. The wings step back a module of 9 m at a time, one after another round the tower, until "
             "only the core is left. A model of how the tower's structure works, not of the tower itself.\n\n" +
             TOWER_STORY.replace("all 31 s of it, as a tower this tall swings slowly", "its first 20 s, which hold its strongest shaking"))


def outrigger(doc):
    s = Structure(doc, "Outrigger tower", "outrigger", [
        ("StoreyHeight", "Storey height [m]", 3.5, 5, 4.2, 2),
        ("Width", "Module, half width at its foot [m]", 20, 30, 25, 1),
        ("Flare", "Module, flare to its top [m]", 0, 8, 4, 1),
        ("DamperMass", "Damper [t]", 1, 3000, 660, 0),
        ("DamperPeriod", "Damper period [s]", 2, 15, 9.7, 2),
        ("DamperDamping", "Damper damping [%]", 0, 30, 8, 1),
    ], _tower_quake(0.1, 2.0))
    s.inputs.append(("FloorMass", s.as_float))
    s.geometry(["CoreColumns", "CoreBeams", "CoreBraces", "Supers", "Perimeter", "Rings", "Outriggers", "Hangers",
                "Feet", "Floors", "Masters", "MassPoints", "Masses", "Damper", "DamperPoint", "DamperMasses",
                "Stiffness", "Dashpot", "Watch"])
    steel = s.steel()
    s.elements("Core columns", "CoreColumns", s.box("Core column box 1200 x 80", steel, 1.2, 1.2, 0.08))
    s.member("Core beams", "CoreBeams", "HEM600")
    s.elements("Core braces", "CoreBraces", s.box("Brace box 800 x 60", steel, 0.8, 0.8, 0.06))
    s.elements("Super columns", "Supers", s.box("Super column 2400 x 3000 x 200", steel, 2.4, 3.0, 0.2))
    s.member("Perimeter columns", "Perimeter", "HEM600")
    s.member("Ring beams", "Rings", "HEM600")
    s.elements("Outriggers", "Outriggers", s.box("Outrigger box 1200 x 80", steel, 1.2, 1.2, 0.08))
    s.member("Damper hangers", "Hangers", "HEM1000")
    s.support("Built in", "Feet", (True,) * 6)
    s.floors()
    # The damper: a ball hung from the 92nd floor by a rod - a truss, which holds it up - and swung by a
    # spring and a dashpot across, a Link of an elastic material with damping in its two directions
    # across the rod, its stiffness and damping from the damper's mass, period and damping.
    rod = s.elements("Damper rod", "Damper", s.section(CIRCLE, "Rod 300 mm", steel, 0.3, 0.15), truss=True)
    springs = []
    for axis in ("one way", "the other"):
        u = component(s.grid, UNIAXIAL, "Uniaxial", "Spring and dashpot, " + axis, SECTIONS, LOADED, gap=30)
        words(inp(u, 0), "Damper spring " + axis)
        wire(inp(u, 1), s.g("Stiffness"))
        wire(inp(u, 2), s.g("Stiffness"))
        wire(inp(u, 3), s.g("Dashpot"))
        springs.append(u)
    link = component(s.grid, LINK, "Link", "Damper springs", MEMBERS, LOADED, gap=30)
    wire(inp(link, 0), s.g("Damper"))
    wire(inp(link, 1), *[out(u) for u in springs])
    inp(link, 2).PersistentData.Clear()
    for d in (2, 3):
        inp(link, 2).PersistentData.Append(GH_Integer(d))
    s.members.append(link)
    ball = component(s.grid, MASS_POINT, "Mass Point", "Damper mass", LOADS, LOADED, gap=30)
    wire(inp(ball, 0), s.g("DamperPoint"))
    wire(inp(ball, 1), s.g("DamperMasses"))
    s.patterns.append(ball)
    s.parts["2"] += springs + [link]
    s.parts["4"].append(ball)
    _shake(s, "Outrigger shaking", OUTRIGGER_VIEW, TOWER_STEPS, bands=False, wires=False)
    s.finish(_tower_groups("a braced core, eight super columns and outrigger trusses, eight flaring modules, a damper",
                           "steel boxes and HEM sections; the damper a rod, a link of springs and dashpots, and a mass",
                           OUTRIGGER_VIEW),
             "Outrigger tower, after Taipei 101 - 101 storeys, 424 m to the roof, and a tuned mass damper, shaken by an "
             "earthquake.\n\nA braced steel core of sixteen columns and eight super columns, two on each face, tied "
             "together by outrigger trusses two storeys deep under the foot of every module: when the tower sways, the "
             "outriggers make the super columns stretch and squash, and the whole width of the tower bends as one. Eight "
             "modules of eight storeys flare out over a base that narrows as it rises. In the core, 660 t hang from the "
             "92nd floor on a spring and a dashpot tuned to the tower's sway: it swings against the tower and takes some "
             "of its movement. A model of how the tower's structure works, not of the tower itself.\n\n" + TOWER_STORY)


def diagrid(doc):
    s = Structure(doc, "Diagrid tower", "diagrid", [
        ("Radius", "Radius [m]", 20, 45, 32, 1),
        ("Bulge", "Bulge half way up", 0, 0.4, 0.12, 2),
        ("Around", "Diamonds round it", 6, 24, 12, 0),
        ("Storeys", "Storeys", 30, 120, 103, 0),
        ("StoreyHeight", "Storey height [m]", 3, 5, 4.27, 2),
        ("NodeStoreys", "Storeys a diamond", 4, 20, 12, 0),
        ("Core", "Core, middle to corner [m]", 8, 18, 12, 1),
    ], _tower_quake(0.11, 2.0, 700))
    s.inputs.append(("FloorMass", s.as_float))
    s.geometry(["Diagonals", "Rings", "Walls", "Feet", "Floors", "Masters", "MassPoints", "Masses", "Watch"])
    steel = s.steel()
    s.elements("Diagonals", "Diagonals", s.section(CIRCLE, "Tube 1800 x 180, filled", steel, 1.8, 0.18))
    s.member("Ring beams", "Rings", "HEM1000")
    s.slabs("Core walls", "Walls", 1.0, "Wall concrete")
    s.support("Built in", "Feet", (True,) * 6)
    s.floors()
    _shake(s, "Diagrid shaking", DIAGRID_VIEW, TOWER_STEPS, bands=False, wires=False)
    s.finish(_tower_groups("a tube of diagonals round a concrete core, a rounded triangle in plan, bulging half way up",
                           "steel tubes 1.8 m across for the diagonals, HEM1000 ring beams, 1 m concrete core walls",
                           DIAGRID_VIEW),
             "Diagrid tower, after the Guangzhou International Finance Center - 103 storeys, 440 m tall, shaken by an "
             "earthquake.\n\nA tube of steel diagonals, crossing at nodes every 12 storeys, round a concrete core: a "
             "tube in a tube. The diagonals carry both the weight and the sway, as a lattice; its plan is a triangle with "
             "rounded corners, bulging half way up. The tubes stand for concrete-filled ones, their steel thicker for the "
             "concrete inside. A model of how the tower's structure works, not of the tower itself.\n\n" + TOWER_STORY)


# ---------------------------------------------------------------- 12 - a footbridge's natural modes

# The view of the modes: how many are solved, and the Model View's deformation scale - a mode is drawn
# its largest movement 8% of the bridge's size at 1.
FOOTBRIDGE_MODES = 6


def footbridge(doc):
    s = Structure(doc, "Footbridge", "footbridge", [
        ("Span", "Span [m]", 15, 60, 40, 1),
        ("Width", "Deck width [m]", 2, 6, 3.5, 2),
        ("Segment", "Element length [m]", 1, 5, 2.5, 2),
        ("Depth", "Box depth [m]", 0.6, 2.5, 1.2, 2),
    ], [])
    s.geometry(["Box", "Deck", "Under", "Over", "Pinned", "Sliding"])
    steel = s.steel()
    box = component(s.grid, RECTANGLE_HOLLOW, "Rectangle Hollow CS", "Box girder", SECTIONS, MAIN, gap=30)
    words(inp(box, 0), "Steel box 1.6 m wide")
    number(inp(box, 1), 1.6)
    wire(inp(box, 2), s.sliders["Depth"])
    # Webs of 20 mm, flanges of 25 mm.
    number(inp(box, 3), 0.02)
    number(inp(box, 4), 0.025)
    number(inp(box, 5), 0.025)
    wire(inp(box, 6), out(steel))
    s.parts["2"].append(box)
    s.elements("Box girder", "Box", box)
    s.slabs("Deck", "Deck", 0.2)
    # The deck on the box: each node of the deck's middle line follows the box's node under it as a
    # rigid body, so that the deck works with the box, as a composite girder.
    links = component(s.grid, RIGID_LINK, "Rigid Link", "Deck on box", MEMBERS, MAIN, gap=30)
    wire(inp(links, 0), s.g("Under"))
    wire(inp(links, 1), s.g("Over"))
    s.parts["2"].append(links)
    # Both ends held against twisting, Rx; the far end free to slide along, Tx.
    s.support("Pinned bearing", "Pinned", (True, True, True, True, False, False))
    s.support("Sliding bearing", "Sliding", (False, True, True, True, False, False))
    a = component(s.grid, ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(a, 0)), *[out(m) for m in s.members])
    wire(flat(inp(a, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(a, 3)), out(links))
    modes = component(s.grid, NATURAL_VIBRATION, "Natural Vibration Analysis", None, ANALYSIS, MAIN)
    wire(inp(modes, 0), out(a))
    integer(inp(modes, 1), FOOTBRIDGE_MODES)
    frequencies = panel(s.grid, "Frequencies [Hz]", out(modes, 3), 160, 130, ANALYSIS, MAIN, gap=30)
    s.assembled = a
    s.parts["5"] += [a, modes, frequencies]
    mode = slider(s.grid, "Mode", 0, FOOTBRIDGE_MODES - 1, 0, 0, VIEWS, SHOWN, gap=30)
    view = s.deformed("Footbridge mode", out(modes), 1, title="Mode shape, largest movement 1", loads=False, bands=True)
    wire(inp(view, V_STEP), mode)
    # A mode has no size of its own: it is read with its largest movement 1, in m, from 0 to 1.
    result_unit(view, DISPLACEMENT, MAGNITUDE, 2)
    menu_setting(view, "_ckWires", "Active", True)
    s.parts["6"].append(mode)
    s.at_rest()
    s.finish({"1": "Geometry: a deck on a steel box girder down its middle, held at both ends",
              "2": "Members: the steel box, its depth a slider, a 200 mm concrete deck as ASD Shells, rigid links between them",
              "3": "Supports: a pinned bearing at the start, a sliding one at the far end, both holding it against twisting",
              "5": "Analysis: the first {0} natural modes; the mass is the bridge's own".format(FOOTBRIDGE_MODES),
              "6": "Results: the mode Mode picks, in iso-surfaces, over the bridge at rest"},
             "Footbridge - a footbridge's natural modes, and whether people walking set it swinging.\n\n"
             "A concrete deck 200 mm thick on a steel box girder down its middle, 1.6 m wide, its depth a slider, "
             "the deck tied to the box by rigid links so that they bend together. It spans between two bearings "
             "under the box, the far one sliding along, both holding it against twisting.\n\n"
             "The Natural Vibration Analysis finds its first six modes, its own weight the mass that swings. People "
             "walk at 1.6 to 2.4 steps a second, which a mode near that frequency picks up and bounces with; "
             "sideways, they sway at half that. Drag Mode to see each one; drag Box depth or Span to move them.")


# ---------------------------------------------------------------- 13 - sticks that draw a response spectrum

def sticks(doc):
    s = Structure(doc, "Response spectrum sticks", "sticks", [
        ("Sticks", "Sticks", 2, 60, 30, 0),
        ("Shortest", "Shortest [m]", 0.5, 3, 1.0, 2),
        ("Tallest", "Tallest [m]", 3, 15, 10, 1),
        ("Spacing", "Spacing [m]", 0.5, 3, 1.2, 2),
    ], [
        ("Scale", "Ground motion scale", 0, 3, 1.0, 2),
        ("Duration", "Duration [s]", 2, 31, 12, 1),
        ("Damping", "Damping [%]", 0, 10, 5, 1),
        ("Mass", "Mass [kg]", 500, 20000, 4000, 0),
    ])
    s.geometry(["Lines", "Feet", "Tops"])
    steel = s.steel()
    tube = component(s.grid, SECTION, "Steel Section Library", "219.1x8", SECTIONS, MAIN, gap=30)
    words(inp(tube, 0), "219.1x8")
    wire(inp(tube, 1), out(steel))
    stick = component(s.grid, BEAM, "ForceBeamColumn", "Sticks", MEMBERS, MAIN, gap=30)
    wire(inp(stick, B_LINE), s.g("Lines"))
    wire(inp(stick, B_SECTION), out(tube))
    s.members.append(stick)
    s.parts["2"] += [tube, stick]
    s.support("Built in", "Feet", (True,) * 6)
    # The mass on each stick's top, the same along x, y and z.
    mass = component(s.grid, VECTOR_XYZ, "Vector XYZ", "Mass [kg]", WIDTHS, LOADED, gap=30)
    for k in range(3):
        wire(inp(mass, k), s.sliders["Mass"])
    masses = component(s.grid, MASS_POINT, "Mass Point", "Mass on top", LOADS, LOADED, gap=30)
    wire(inp(masses, 0), s.g("Tops"))
    wire(inp(masses, 1), out(mass))
    s.parts["4"] += [mass, masses]
    # El Centro along x, and a copy of its pattern for every stick, on the masses' branches: each
    # stick's model gets its mass and the ground motion, and nothing of the others'.
    pattern = s.earthquake("X")
    s.patterns.remove(pattern)
    copies = component(s.grid, DUPLICATE, "Duplicate Data", "A pattern a stick", PATTERNS, LOADED, gap=30)
    wire(inp(copies, 0), out(pattern))
    wire(inp(copies, 1), s.sliders["Sticks"])
    each = component(s.grid, UNFLATTEN, "Unflatten Tree", "On the masses' branches", PATTERNS, LOADED, gap=30)
    wire(inp(each, 0), out(copies))
    wire(inp(each, 1), out(masses))
    s.parts["4"] += [copies, each]

    # A model per stick: its line, its foot, its mass and the ground motion. Only the displacements
    # are recorded: thirty sticks, six thousand steps each, read back step by step.
    record = recorder(s.grid, "Displacements only", ["displacement"], MODEL, MAIN, gap=30)
    a = component(s.grid, ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(inp(a, 0), out(stick))
    wire(inp(a, 1), out(s.supports[0]))
    wire(inp(a, 2), out(masses), out(each))
    wire(inp(a, 4), out(record))
    # Each stick's frequency, and damping of Damping % of critical at it: Rayleigh's, alphaM = zeta w
    # and betaK = zeta / w at both of its frequencies, gives exactly that at w.
    modes = component(s.grid, NATURAL_VIBRATION, "Natural Vibration Analysis", "Each stick's period", MODEL, MAIN, gap=30)
    wire(inp(modes, 0), out(a))
    integer(inp(modes, 1), 1)
    # LAPACK's dense solver: a stick has one node free, and ARPACK wants more than twice as many
    # degrees of freedom with a mass as the modes it is asked for.
    words(inp(modes, 2), "-fullGenLapack")
    zeta = s.sliders["Damping"]
    alpha = expression(s.grid, "f(z,f)=0.0628319*z*f", [zeta, out(modes, 3)], "alphaM", MODEL, MAIN, gap=30)
    beta = expression(s.grid, "f(z,f)=0.0015915*z/f", [zeta, out(modes, 3)], "betaK", MODEL, MAIN, gap=30)
    damping = component(s.grid, DAMPING, "Damping", "Rayleigh", MODEL, MAIN, gap=30)
    wire(inp(damping, 0), out(alpha))
    wire(inp(damping, 3), out(beta))
    integrator = component(s.grid, INTEGRATOR, "Integrator", "Newmark", MODEL, MAIN, gap=30)
    switch_unit(integrator, NEWMARK)
    # A step a quarter of the record's, 0.005 s: the stiffest sticks swing ten times a second.
    count = expression(s.grid, "f(n)=4*n", [out(s.record, 3)], "Steps", MODEL, MAIN, gap=30)
    steps = component(s.grid, ANALYSIS_STEP, "Analysis Step", "0.005 s steps", MODEL, MAIN, gap=30)
    wire(inp(steps, 0), out(count))
    number(inp(steps, 1), 0.005)
    settings = component(s.grid, ANALYSIS_SETTINGS, "Analysis Settings", "Transient", ANALYSIS, MAIN)
    wire(inp(settings, A_DAMPING), out(damping))
    words(inp(settings, A_ALGORITHM), "Linear")
    # A banded solver on one core: Mumps spends longer starting its threads than solving six equations.
    words(inp(settings, A_SYSTEM), "BandGen")
    wire(inp(settings, A_INTEGRATOR), out(integrator))
    words(inp(settings, A_TYPE), "Transient")
    wire(inp(settings, A_STEPS), out(steps))
    run = component(s.grid, RUN_ANALYSIS, "Run Analysis", None, ANALYSIS, MAIN, gap=30)
    wire(inp(run, 0), out(a))
    wire(inp(run, 1), out(settings))
    s.assembled, s.run = a, run
    s.parts["5"] += [record, a, modes, alpha, beta, damping, integrator, count, steps, settings, run]

    # The spectrum: how far each stick's top moved at the most, and what that is as an acceleration.
    # Node 2 of each stick's model is its top, node 1 its foot.
    history = component(s.grid, NODAL_DISPLACEMENTS, "Nodal Displacements", "Tops, every step", VIEWS, MAIN, gap=30)
    wire(inp(history, 0), out(run))
    booleans(inp(history, 1), True)
    integer(inp(history, 3), 2)
    tree = GH_ParamAccess.tree
    as_point, as_vector = GH_Point3dHint(), GH_Vector3dHint()
    chart = python_component(s.grid, "Response spectrum", "spectrum",
                             [("Periods", s.as_float, tree), ("Displacements", as_vector, tree), ("Tops", as_point, tree)],
                             ["Period", "Sd", "PSa", "Spectrum", "Grid"], VIEWS, MAIN)
    wire(inp(chart, 0), out(modes, 2))
    wire(inp(chart, 1), out(history, 1))
    wire(inp(chart, 2), s.g("Tops"))
    periods = panel(s.grid, "Period [s]", out(chart, 1), 110, 160, LEGENDS, MAIN, gap=30)
    accelerations = panel(s.grid, "PSa [g]", out(chart, 3), 110, 160, LEGENDS, MAIN, gap=30)
    s.parts["6"] += [history, chart, periods, accelerations]

    # Step goes as far as the whole record does, in 0.005 s steps: 6 200.
    view = s.shaking("Sticks shaking", STICKS_VIEW[0], STICKS_VIEW[1], 500, supports=STICKS_VIEW[2], steps=6200)
    # Model View solves every stick's model, a branch each, but its preview draws the last branch only:
    # a Mesh parameter draws them all from its Geometry output, coloured as it colours them.
    view.Hidden = True
    every = s.grid.put(emit(MESH_PARAM, "The Mesh parameter"), LEGENDS, SHOWN, gap=30)
    every.NickName = "Every stick"
    wire(every, out(view, 1))
    # One legend: every stick's colours are the same, over the same range, so the first stick's
    # branch stands for all of them.
    legend = s.parts["6"][-2]
    for k, name in ((L_VALUES, "Values"), (L_COLORS, "Colors")):
        first = component(s.grid, TREE_BRANCH, "Tree Branch", "First stick's " + name, LEGENDS, SHOWN, gap=30)
        wire(inp(first, 0), out(view, R_VALUES if k == L_VALUES else R_COLORS))
        inp(first, 1).PersistentData.Append(GH_StructurePath(GH_Path(System.Array[int]([0, 0, 0]))))
        inp(legend, k).RemoveAllSources()
        wire(inp(legend, k), out(first))
        s.parts["6"].append(first)
    s.parts["6"].append(every)
    s.finish({"1": "Geometry: a row of sticks from short to tall, their heights spread for their periods to be",
              "2": "Members: a steel tube, 219.1x8, built in at the ground",
              "3": "Supports: every stick built in at the ground",
              "4": "Ground motion: a mass on every top, and El Centro 1940 along x, copied for every stick",
              "5": "Analysis: a model per stick - its period, damping of 5% at it, a transient analysis in 0.005 s steps",
              "6": "Results: the most each top moved, as a spectrum behind the sticks; the sticks at Step, drawn by Every stick"},
             "Response spectrum sticks - a row of sticks shaken by one earthquake, which together draw its response "
             "spectrum.\n\n"
             "Each stick is a steel tube built in at the ground with a mass on top: an oscillator with one way to "
             "swing, at its own period. Short ones are stiff and quick, tall ones soft and slow; their heights are "
             "spread so that their periods are spread evenly, from about 0.1 s to 3 s.\n\n"
             "Each stick is a model of its own, a branch of the trees, so that each can have damping of exactly "
             "Damping % of critical at its own frequency, which its Natural Vibration Analysis gives. A transient "
             "analysis shakes every one with the same ground motion, El Centro 1940, 0.005 s at a time.\n\n"
             "The most its top moves from the ground, Sd, is the spectral displacement at its period; times "
             "(2 pi / T) squared, the pseudo-acceleration PSa. Plotted against the periods, they are the earthquake's "
             "response spectrum: the curve a response spectrum analysis reads its accelerations from.")


# The sticks' view: its deformation scale, the top of its colours in mm, the size of its supports.
STICKS_VIEW = (3, 280, 0.4)


# ---------------------------------------------------------------- 14 - a shell form-found to work in compression

# The shell's view: its deformation scale, and the top of its colours in mm - eight bands.
SHELL_VIEW = (2000, 0.32)


def shell(doc):
    s = Structure(doc, "Compressed shell", "compressed_shell", [
        ("Span", "Span [m]", 10, 40, 20, 1),
    ], [])
    s.geometry(["Net", "Ends"])
    # 2 - Kangaroo: the net refined, its edges pulling in like strings shortening to nothing, held at
    # the ends of its legs, every node pushed up by Lift: it rises until the strings hold it, all in
    # tension - turned over, a form that carries a load like its own in compression.
    # 0.6 raises the crown 8 m above the ends of the legs; the rise goes as Lift.
    lift = slider(s.grid, "Lift", 0, 2, 0.6, 2, SLIDERS, LOADED, gap=30)
    pull = slider(s.grid, "Edge strength", 1, 5, 3, 1, SLIDERS, LOADED, gap=30)
    refine = component(s.grid, REFINE, "Refine", "Refined twice", SECTIONS, LOADED, gap=30)
    wire(inp(refine, 0), s.g("Net"))
    integer(inp(refine, 1), 2)
    edges = component(s.grid, MESH_EDGES, "Mesh Edges", "Edges", MEMBERS, LOADED, gap=30)
    wire(inp(edges, 0), out(refine))
    nodes = component(s.grid, DECONSTRUCT_MESH, "Deconstruct Mesh", "Nodes", MEMBERS, LOADED, gap=30)
    wire(inp(nodes, 0), out(refine))
    distance = component(s.grid, PULL_POINT, "Pull Point", "To the leg ends", WIDTHS, LOADED, gap=30)
    wire(inp(distance, 0), out(nodes, 0))
    wire(inp(distance, 1), s.g("Ends"))
    near = component(s.grid, SMALLER_THAN, "Smaller Than", "On an end", VECTORS, LOADED, gap=30)
    wire(inp(near, 0), out(distance, 1))
    number(inp(near, 1), 0.01)
    feet = component(s.grid, CULL_PATTERN, "Cull Pattern", "Feet", LOADS, LOADED, gap=30)
    wire(inp(feet, 0), out(nodes, 0))
    wire(inp(feet, 1), out(near, 0))
    show = component(s.grid, SHOW, "Show", "The net", PATTERNS, LOADED, gap=30)
    wire(inp(show, 0), out(refine))
    strings = component(s.grid, LENGTH_LINE, "Length(Line)", "Edges pull in", PATTERNS, LOADED, gap=30)
    # Not flattened: the goals' outputs keep paths as deep as the net's, so that flattened into the
    # solver the net's Show stays first, and the form is the first thing out.
    wire(inp(strings, 0), out(edges, 0), out(edges, 1))
    number(inp(strings, 1), 0.0)
    wire(inp(strings, 2), pull)
    # Lift as the length of the push, through Unit Z's factor: Load's Weighting, wired, stops Rhino 8
    # behind an alert.
    up = component(s.grid, UNIT_Z, "Unit Z", "Up", LOADS, LOADED, gap=30)
    wire(inp(up, 0), lift)
    push = component(s.grid, LOAD, "Load", "Pushed up", PATTERNS, LOADED, gap=30)
    wire(inp(push, 0), out(nodes, 0))
    wire(inp(push, 1), out(up))
    held = component(s.grid, ANCHOR, "Anchor", "Held", PATTERNS, LOADED, gap=30)
    wire(inp(held, 0), out(feet))
    zombie = component(s.grid, ZOMBIE, "Zombie Solver", "Zombie Solver", MODEL, LOADED, gap=30)
    flat(wire(inp(zombie, 0), out(show), out(strings), out(push), out(held)))
    form = component(s.grid, LIST_ITEM, "List Item", "The form", ANALYSIS, LOADED, gap=30)
    wire(inp(form, 0), out(zombie, 2))
    integer(inp(form, 1), 0)
    kangaroo = [lift, pull, refine, edges, nodes, distance, near, feet, show, strings, up, push, held, zombie, form]

    # 3 - The form as a concrete shell; 4 - held where the net was; 5 - its own weight.
    thickness = slider(s.grid, "Thickness [m]", 0.05, 0.4, 0.15, 2, SECTIONS, MAIN, gap=30)
    s.slabs("Shell", out(form), thickness)
    concrete_shell = [thickness] + s.parts["2"][-3:]
    support = s.support("Built in", out(feet), (True,) * 6)
    weight = component(s.grid, GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    pattern = s.pattern("Own weight", [weight])
    run = s.assemble()
    analysis = s.parts["5"][-3:]
    view = s.deformed("Shell deformed", out(run), SHELL_VIEW[0], palette="shell", bands=True, loads=False)
    if SHELL_VIEW[1]:
        view_range = inp(view, V_RANGE)
        view_range.PersistentData.Clear()
        view_range.PersistentData.Append(GH_Interval(Interval(0.0, SHELL_VIEW[1])))
    menu_setting(view, "_ckWires", "Active", True)
    menu_setting(view, "_ckShowSupports", "Active", True)
    s.loads_view()
    s.parts = {"1": s.parts["1"], "2": kangaroo, "3": concrete_shell, "4": [support], "5": [weight, pattern],
               "6": analysis, "7": s.parts["6"]}
    s.finish({"1": "Geometry: a cross of four legs, a coarse net of 16 quads",
              "2": "Form finding: Kangaroo pushes the refined net up against its edges, held at the ends of its legs",
              "3": "Shell: the form in concrete, Thickness thick, as ASD Shells",
              "4": "Supports: built in where the net was held",
              "5": "Load: its own weight",
              "6": "Analysis: Assemble and Run Analysis",
              "7": "Results: the displacement, in iso-surfaces, drawn {0} times".format(SHELL_VIEW[0])},
             "Compressed shell - a concrete shell form-found with Kangaroo to carry its own weight in compression.\n\n"
             "A cross of four legs, 20 m from end to end. Its coarse net is refined twice; Kangaroo holds it at the "
             "ends of its legs, makes every edge a string that pulls in, shortening towards nothing, and pushes "
             "every node up. The net rises until the strings hold it: all in tension under an upward load, it "
             "is the hanging form turned over, and so works in compression under its own weight. Lift sets how "
             "high it rises.\n\n"
             "The form becomes a concrete shell, Thickness thick, built in at the ends of its legs, under its own "
             "weight. Model View draws its displacement in iso-surfaces: bands of equal movement.")


# ---------------------------------------------------------------- 15 - a tank's first hundred modes

# The tank's view: the deformation scale - a mode is drawn its largest movement 8% of the tank's size
# at 1 - and whether it is drawn in iso-surfaces; the number of colours that gives, which the Legend
# takes from the first mode; and a column for the Legend and the labels, right of the others.
TANK_VIEW = (0.6, False)
TANK_COLOURS = 8 if TANK_VIEW[1] else 9
TAGS_COLUMN = LEGENDS + 1


def tank(doc):
    s = Structure(doc, "Tank modes", "tank", [
        ("Radius", "Radius [m]", 0.5, 5, 2, 2),
        ("Height", "Height [m]", 1, 15, 5, 2),
        ("Around", "Elements around", 12, 120, 64, 0),
        ("Up", "Elements up", 4, 60, 30, 0),
        ("Modes", "Modes", 1, 200, 100, 0),
        ("Columns", "Columns", 1, 20, 10, 0),
        ("Spacing", "Spacing [m]", 2, 20, 6, 2),
    ], [])
    s.geometry(["Wall", "Base", "Grid"])
    wall = slider(s.grid, "Wall [mm]", 50, 400, 200, 0, SLIDERS, MAIN, gap=30)
    s.parts["1"].append(wall)
    # Concrete as an nD material, for the shells: E 30 GPa, nu 0.2, 2 500 kg/m3.
    concrete = component(s.grid, ND_MATERIAL, "nD", "Concrete", SECTIONS, MAIN, gap=30)
    words(inp(concrete, 0), "Concrete")
    number(inp(concrete, 1), 30e6)
    number(inp(concrete, 2), 30e6 / 2.4)
    number(inp(concrete, 3), 0.2)
    number(inp(concrete, 4), 2500)
    inp(concrete, 5).PersistentData.Append(GH_Colour(AT_REST))
    metres = expression(s.grid, "f(t)=t/1000", [wall], "Wall [m]", SECTIONS, MAIN, gap=30)
    plate = component(s.grid, PLATE_FIBER, "Plate Fiber Section", "Wall", SECTIONS, MAIN, gap=30)
    wire(inp(plate, 1), out(metres))
    wire(inp(plate, 2), out(concrete))
    shells = component(s.grid, SHELL, "ASD Shell", "Tank wall", MEMBERS, MAIN, gap=30)
    wire(flat(inp(shells, 0)), s.g("Wall"))
    wire(inp(shells, 1), out(plate))
    s.members.append(shells)
    s.parts["2"] += [concrete, metres, plate, shells]
    s.support("Built in", "Base", (True,) * 6)
    a = component(s.grid, ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(a, 0)), out(shells))
    wire(flat(inp(a, 1)), *[out(x) for x in s.supports])
    modes = component(s.grid, NATURAL_VIBRATION, "Natural Vibration Analysis", None, ANALYSIS, MAIN)
    wire(inp(modes, 0), out(a))
    wire(inp(modes, 1), s.sliders["Modes"])
    frequencies = panel(s.grid, "Frequencies [Hz]", out(modes, 3), 160, 200, ANALYSIS, MAIN, gap=30)
    s.assembled = a
    s.parts["5"] += [a, modes, frequencies]

    # Every mode at once: Step takes a list of them, 0 for the first, and Model View solves each in turn.
    every = component(s.grid, SERIES, "Series", "Every mode", VIEWS, SHOWN, gap=30)
    number(inp(every, 0), 0)
    number(inp(every, 1), 1)
    wire(inp(every, 2), s.sliders["Modes"])
    view = s.deformed("Mode shapes", out(modes), TANK_VIEW[0], title="Mode shape, largest movement 1", loads=False,
                      bands=TANK_VIEW[1])
    wire(inp(view, V_STEP), out(every))
    # A mode has no size of its own: it is read with its largest movement 1, in m, its colours over 0 to 1.
    result_unit(view, DISPLACEMENT, MAGNITUDE, 2)
    inp(view, V_RANGE).PersistentData.Clear()
    inp(view, V_RANGE).PersistentData.Append(GH_Interval(Interval(0.0, 1.0)))
    menu_setting(view, "_ckWires", "Active", True)
    # Model View draws its last mode only: its Geometry, a coloured mesh a mode, is moved onto the
    # grid, each mode to its own place, and drawn by a Mesh parameter.
    view.Hidden = True
    grid = component(s.grid, MOVE, "Move", "Onto the grid", LEGENDS, SHOWN, gap=30)
    wire(inp(grid, 0), out(view, 1))
    wire(inp(grid, 1), s.g("Grid")).DataMapping = GH_DataMapping.Graft
    grid.Hidden = True
    drawn = s.grid.put(emit(MESH_PARAM, "The Mesh parameter"), LEGENDS, SHOWN, gap=30)
    drawn.NickName = "Every mode"
    wire(drawn, out(grid, 0))
    # Over each tank, its mode and its frequency, upright, centred at the bottom.
    labels = python_component(s.grid, "Mode labels", "mode_labels",
                              [("Frequencies", s.as_float, GH_ParamAccess.list),
                               ("Grid", GH_Vector3dHint(), GH_ParamAccess.list), ("Radius", s.as_float),
                               ("Height", s.as_float)],
                              ["Places", "Texts"], LEGENDS, SHOWN)
    wire(inp(labels, 0), out(modes, 3))
    wire(inp(labels, 1), s.g("Grid"))
    wire(inp(labels, 2), s.sliders["Radius"])
    wire(inp(labels, 3), s.sliders["Height"])
    labels.Hidden = True
    tags = component(s.grid, TEXT_TAG, "Text Tag 3D", "Mode and frequency", TAGS_COLUMN, SHOWN, gap=30)
    wire(inp(tags, 0), out(labels, 1))
    wire(inp(tags, 1), out(labels, 2))
    number(inp(tags, 2), 0.7)
    inp(tags, 3).PersistentData.Clear()
    inp(tags, 3).PersistentData.Append(GH_Colour(Color.FromArgb(255, 42, 45, 49)))
    # One legend: every mode is coloured over 0 to 1, so the first mode's values and colours stand for
    # them all. Model View hands out every mode's, one after the other: the first nine are the first's.
    caption, legend = s.parts["6"][-2], s.parts["6"][-1]
    s.grid.items = [(o, TAGS_COLUMN if o in (caption, legend) else c, r, g) for o, c, r, g in s.grid.items]
    for k in (L_VALUES, L_COLORS):
        first = component(s.grid, LIST_ITEM, "List Item", "First mode's " + ("Values" if k == L_VALUES else "Colors"),
                          LEGENDS, SHOWN, gap=30)
        flat(wire(inp(first, 0), out(view, R_VALUES if k == L_VALUES else R_COLORS)))
        inp(first, 1).PersistentData.Clear()
        for i in range(TANK_COLOURS):
            inp(first, 1).PersistentData.Append(GH_Integer(i))
        inp(legend, k).RemoveAllSources()
        wire(inp(legend, k), out(first))
        s.parts["6"].append(first)
    s.parts["6"] += [every, grid, drawn, labels, tags]
    s.finish({"1": "Geometry: a cylinder of quads, built in round its base, and a place on a grid for each mode",
              "2": "Members: the concrete wall, Wall [mm] thick, as ASD Shells",
              "3": "Supports: built in round the base",
              "5": "Analysis: the first Modes natural modes; the mass is the tank's own",
              "6": "Results: every mode at once, each on a copy of the tank in its place on the grid"},
             "Tank modes - a concrete tank's first hundred natural modes, side by side.\n\n"
             "A cylinder 4 m across and 5 m tall, its concrete wall 200 mm thick, built in round its base and open at "
             "the top, as ASD Shells. The Natural Vibration Analysis finds its first Modes modes, its own weight the "
             "mass that swings.\n\n"
             "Model View takes every mode at once: a Series of steps, 0 for the first, into its Step. It draws only "
             "the last, so its preview is off; its Geometry output, a coloured mesh a mode, is moved onto a grid, "
             "Columns to a row, and drawn by the Mesh parameter Every mode, with each mode's number and frequency "
             "under it. A round tank's modes come in pairs, the same shape turned round by half a wave: they have "
             "the same frequency.")


# ---------------------------------------------------------------- a concrete frame pushed over

# The deformation scale: the frame is drawn twice its sway.
PUSHOVER_SCALE = 2
# The members' grey, under the hinges Model View draws in orange and purple.
MEMBER_GREY = Color.FromArgb(255, 160, 168, 178)
# Fibre sections, in m: a column 400 x 400 with eight phi 20 bars, and a beam 500 deep and 300 wide
# with three phi 20 at the top and three at the bottom, all 50 mm from the faces to the bars' centres.
# On a beam a fibre's y - its x in the drawing - is the beam's local y, which on a horizontal beam
# points up: so the beam is drawn with its depth along x.
BAR_20 = 3.141592653589793 * 0.02 ** 2 / 4.0
COLUMN_BARS = [(-0.15, -0.15, 0.0), (0.0, -0.15, 0.0), (0.15, -0.15, 0.0), (-0.15, 0.0, 0.0), (0.15, 0.0, 0.0),
               (-0.15, 0.15, 0.0), (0.0, 0.15, 0.0), (0.15, 0.15, 0.0)]
BEAM_BARS = [(y, z, 0.0) for y in (-0.2, 0.2) for z in (-0.1, 0.0, 0.1)]


# A fibre section's torsion is elastic, a GJ of its own: Fiber Section's 1e8 kN m2 would have the
# beams across a frame hold its columns' tops against turning. About a tenth of the uncracked G J of
# these sections, for concrete that has cracked.
CRACKED_GJ = 3000.0


def fibre_section(s, name, depth, width, along, across, bars, concrete, rebar):
    """A Fiber Section of concrete, depth along x by width along y, meshed into along by across faces -
    a fibre a face - and bars of rebar at bars, a phi 20 each; its torsion CRACKED_GJ."""
    outline = component(s.grid, RECTANGLE_3PT, "Rectangle 3Pt", name + " outline", 2.25, MAIN, gap=30)
    points_ = [(-depth / 2, -width / 2), (depth / 2, -width / 2), (depth / 2, width / 2)]
    for k, (x, y) in enumerate(points_):
        inp(outline, k).PersistentData.Clear()
        inp(outline, k).PersistentData.Append(GH_Point(Point3d(x, y, 0.0)))
    mesh = component(s.grid, MESH_PLANE, "Mesh Plane", name + " fibres", 2.5, MAIN, gap=30)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), along)
    integer(inp(mesh, 2), across)
    patch = component(s.grid, PATCH, "Patch", name + " concrete", 2.75, MAIN, gap=30)
    wire(inp(patch, 0), out(mesh))
    wire(inp(patch, 1), out(concrete))
    steel = component(s.grid, POINT_FIBER, "Fiber Point", name + " bars", 2.75, MAIN, gap=30)
    inp(steel, 0).PersistentData.Clear()
    for x, y, z in bars:
        inp(steel, 0).PersistentData.Append(GH_Point(Point3d(x, y, z)))
    number(inp(steel, 1), *[BAR_20] * len(bars))
    wire(inp(steel, 2), out(rebar))
    section = component(s.grid, FIBER_SECTION, "Fiber Section", name, 2.9, MAIN, gap=30)
    wire(inp(section, 0), out(steel))
    wire(inp(section, 2), out(patch))
    number(inp(section, 3), CRACKED_GJ)
    s.parts["2"] += [outline, mesh, patch, steel, section]
    return section


def pushover(doc):
    s = Structure(doc, "Irregular building", "pushover", [
        ("BayX", "Bay along x [m]", 4, 8, 6, 1),
        ("SecondBayX", "Second bay along x [m]", 4, 8, 5, 1),
        ("BayY", "Bays along y [m]", 4, 8, 5, 1),
        ("GroundStorey", "Ground storey [m]", 2.8, 5, 4, 1),
        ("Storey", "Storeys above [m]", 2.8, 4.5, 3.2, 1),
    ], [
        ("Floor", "Floor load [kN/m]", 0, 60, 20, 1),
        ("Drift", "Roof drift [%]", 0.5, 6, 3, 1),
        ("Steps", "Steps", 20, 400, 200, 0),
    ])
    s.geometry(["Columns", "Beams", "Feet", "Floors", "Forces", "Roof"])
    s.as_point = GH_Point3dHint()

    # 2 - The materials and the sections: C25/30 and B450C, Concrete01's and ReinforcingSteel's own.
    concrete = component(s.grid, CONCRETE01, "Concrete01", "C25/30", SECTIONS, MAIN, gap=30)
    rebar = component(s.grid, REBAR, "ReinforcingSteel", "B450C", SECTIONS, MAIN, gap=30)
    s.parts["2"] += [concrete, rebar]
    column = fibre_section(s, "Column 400 x 400", 0.4, 0.4, 10, 10, COLUMN_BARS, concrete, rebar)
    beam = fibre_section(s, "Beam 500 x 300", 0.5, 0.3, 12, 6, BEAM_BARS, concrete, rebar)
    columns = s.elements("Columns", "Columns", column)
    beams = s.elements("Beams", "Beams", beam)
    # Display only: both views draw each element in its own colour, a grey that the hinges stand out on.
    for member in (columns, beams):
        inp(member, B_COLOUR).PersistentData.Clear()
        inp(member, B_COLOUR).PersistentData.Append(GH_Colour(MEMBER_GREY))
    s.support("Built in", "Feet", (True,) * 6)

    # 4 - What the frame carries while it is pushed: its own weight and the floors, ramped up over the
    # pushover's first ten steps by a linear time series, then held.
    weight = component(s.grid, GRAVITY, "Gravity Load", "Self-weight", LOADS, LOADED, gap=30)
    floors = s.line_load("Floors", [beams], UNIT_Z, -1, "Floor")
    series = component(s.grid, LINEAR_SERIES, "Linear Time Series", "Ramp", PATTERNS, LOADED, gap=30)
    pattern = s.pattern("Weight and floors", [weight, floors])
    wire(inp(pattern, 0), out(series))
    lateral = component(s.grid, POINT_LOAD, "Point Load", "The push, floor by floor", LOADS, LOADED, gap=30)
    wire(inp(lateral, 0), s.g("Floors"))
    wire(inp(lateral, 1), s.g("Forces"))
    s.parts["4"] += [weight, series, lateral]

    # 5 - Assembled, and pushed: the roof to Drift % of the height, along X, with P-Delta.
    a = component(s.grid, ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(a, 0)), *[out(m) for m in s.members])
    wire(flat(inp(a, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(a, 2)), *[out(x) for x in s.patterns])
    height = expression(s.grid, "f(g,h)=g+2*h", [s.sliders["GroundStorey"], s.sliders["Storey"]], "Height [m]", MODEL, MAIN, gap=30)
    target = expression(s.grid, "f(d,h)=d/100*h", [s.sliders["Drift"], out(height)], "Target [m]", MODEL, MAIN, gap=30)
    push = component(s.grid, PUSHOVER, "Pushover Analysis", "Pushover", ANALYSIS, MAIN)
    wire(inp(push, 0), out(a))
    wire(inp(push, 1), s.g("Roof"))
    words(inp(push, 2), "X")
    wire(inp(push, 3), out(target))
    wire(inp(push, 4), s.sliders["Steps"])
    wire(flat(inp(push, 5)), out(lateral))
    s.assembled, s.run = a, push
    s.parts["5"] += [a, height, target, push]

    # 6 - The frame at a point of the curve, and the curve beside it, the point on it.
    point = slider(s.grid, "Point on the curve", 0, 400, 200, 0, VIEWS, SHOWN, gap=30)
    beside = expression(s.grid, "f(a,b)=a+b+4", [s.sliders["BayX"], s.sliders["SecondBayX"]], "Curve at x", VIEWS, SHOWN, gap=30)
    origin = component(s.grid, CONSTRUCT_POINT, "Construct Point", "Curve origin", VIEWS, SHOWN, gap=30)
    wire(inp(origin, 0), out(beside))
    size = expression(s.grid, "f(h)=h", [out(height)], "Curve size", VIEWS, SHOWN, gap=30)
    curve = python_component(s.grid, "Capacity curve", "capacity_curve",
                             [("Displacement", s.as_float, GH_ParamAccess.list), ("BaseShear", s.as_float, GH_ParamAccess.list),
                              ("Steps", GH_IntegerHint_CS(), GH_ParamAccess.list), ("Point", GH_IntegerHint_CS()),
                              ("Origin", s.as_point), ("Size", s.as_float)],
                             ["Curve", "SoFar", "Dot", "Axes", "TagAt", "Tags", "Step"], LEGENDS, SHOWN)
    for k, source in enumerate([out(push, 1), out(push, 2), out(push, 4), point, out(origin), out(size)]):
        wire(inp(curve, k), source)
    # Drawn by a Curve parameter, and the Python component's own preview off: it would draw where the
    # labels go as crosses.
    curve.Hidden = True
    drawn = emit(CURVE_PARAM, "The Curve parameter")
    drawn.NickName = "Capacity curve drawn"
    s.grid.put(drawn, LEGENDS, SHOWN, gap=30)
    wire(drawn, out(curve, 1), out(curve, 2), out(curve, 3), out(curve, 4))
    tags = component(s.grid, TEXT_TAG, "Text Tag 3D", "Curve labels", LEGENDS, SHOWN, gap=30)
    wire(inp(tags, 0), out(curve, 5))
    wire(inp(tags, 1), out(curve, 6))
    number(inp(tags, 2), 0.7)
    inp(tags, 3).PersistentData.Clear()
    inp(tags, 3).PersistentData.Append(GH_Colour(Color.FromArgb(255, 42, 45, 49)))

    # The frame at the point, drawn twice its sway, in its grey, with its plastic hinges: a ball where
    # the bars have yielded, orange, or the concrete crushed, purple. Tick Displacement in its Results
    # menu to colour it by how far it has moved as well.
    view = component(s.grid, MODEL_VIEW, "Model View", "Frame pushed", VIEWS, SHOWN, gap=30)
    wire(inp(view, V_MODEL), out(push))
    wire(inp(view, V_STEP), out(curve, 7))
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_slDeformScale", "Value", PUSHOVER_SCALE)
    menu_setting(view, "_ckHinges", "Active", True)
    menu_setting(view, "_ckReleases", "Active", False)
    menu_setting(view, "_ckShowSupports", "Active", True)
    view.Attributes.ExpireLayout()
    view.Attributes.PerformLayout()
    hinges = panel(s.grid, "Hinges", out(push, 5), 520, 220, LEGENDS, SHOWN, gap=30)
    rest = s.at_rest(colour_by=0)
    s.parts["6"] += [point, beside, origin, size, curve, drawn, tags, view, hinges]

    s.finish({"1": "Geometry: an L of three bays, a tall ground storey, a top storey set back; the push on every node",
              "2": "Members: fibre sections of Concrete01 and ReinforcingSteel, on ForceBeamColumns",
              "3": "Supports: every column built in at the ground",
              "4": "Loads: its own weight and the floors, held; the push, growing with the height",
              "5": "Pushover: the top storey's roof pushed to Drift % of the height along X, with P-Delta",
              "6": "Results: the frame at a Point on the curve, drawn {0} times, its plastic hinges, and the capacity curve".format(PUSHOVER_SCALE)},
             "Irregular building - a small concrete building that is not regular, pushed sideways until its ground storey gives way.\n\n"
             "An L in plan, bays of 6 and 5 m by two of 5 m; a ground storey 4 m tall under a second of 3.2 m, and a third "
             "storey set back over one corner. Every member is a ForceBeamColumn on a Fiber Section: concrete in fibres, a "
             "Concrete01 C25/30, and phi 20 bars of ReinforcingSteel B450C - eight in a 400 x 400 column, three at the top and "
             "three at the bottom of a 500 x 300 beam, drawn with its depth along x: on a horizontal beam a fibre section's "
             "y points up. Their GJ is a cracked section's, 3000 kN m2: Fiber Section's 1e8 would have the beams hold the "
             "columns' tops against turning.\n\n"
             "It carries its own weight and 20 kN/m on every beam, ramped up and then held. Pushover Analysis then pushes it "
             "along X with a load on every node that grows with its height, and controls the top storey's roof, up to Drift % "
             "of the height, with P-Delta. Point on the curve picks a point of the Capacity curve beside it, and Model View "
             "draws the building there with its plastic hinges: a ball where the bars have yielded, orange, or the concrete "
             "crushed, purple. Hinges lists them in the order they formed.\n\n"
             "What it shows: the tall ground storey takes most of the sway, its columns hinging top and bottom - a soft storey - "
             "and the L twists as it goes.\n\n"
             "Try: Ground storey down to 3.2 m, and see the sway spread more evenly up the building; P-Delta off on Pushover, "
             "and see the curve keep climbing; Floor load up.")


BUILDERS = {"hall": hall, "tower": tower, "canopy": canopy, "facade": facade, "vault": vault, "windtruss": windtruss,
            "girder": girder, "cablestayed": cablestayed, "arch": arch, "building": building, "city": city,
            "footbridge": footbridge, "sticks": sticks, "shell": shell, "tank": tank,
            "twisting": twisting, "leaning": leaning, "curving": curving, "crescent": crescent,
            "bundled": bundled, "braced": braced, "buttressed": buttressed, "outrigger": outrigger, "diagrid": diagrid,
            "pushover": pushover}


def build(doc):
    if STRUCTURE not in BUILDERS:
        raise Exception("STRUCTURE is one of " + ", ".join(sorted(BUILDERS)))
    BUILDERS[STRUCTURE](doc)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(STRUCTURE + " built.")


if __name__ == "__main__":
    main()
