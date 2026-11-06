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
# make.sh, next to it, writes each straight from this script, without Rhino:
#     STRUCTURE=hall ./make.sh Structures.py TrussRoofHall.gh
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
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParameterSide
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_String, GH_Boolean, GH_Vector, GH_Colour, GH_Interval
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Vector3d, Interval
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
UNIT_X = "79F9FBB3-8F1D-4D9A-88A9-F7961B1012CD"             # Vector > Vector > Unit X
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
MULTIPLY = "63FFF845-7C61-4DFB-BA12-44D481B4BF0F"           # Vector > Vector > Multiply

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

# The switchers' other units, by name: the beam's truss, a ground motion's pattern, Newmark's integrator.
TRUSS = "Truss (Alpaca4d)"
UNIFORM_EXCITATION = "UniformExcitation (Alpaca4d)"
NEWMARK = "Newmark"
# Analysis Settings' inputs, by place.
A_DAMPING, A_ALGORITHM, A_INTEGRATOR, A_TYPE, A_STEPS = 0, 5, 6, 7, 8

# ForceBeamColumn's inputs, and Truss's, by place.
B_LINE, B_SECTION, B_ZAXIS, B_ELEMENT_ID = 0, 1, 3, 5
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
# No green where line loads are drawn: theirs is green, and a green band would read as a load.
SCALES = {
    "hall": [DARK_TECH, LIGHT_BLUE, ORANGE_],
    "tower": [LIGHT_BLUE, PURPLE_, ORANGE_],
    "mode": [DARK_TECH, LIGHT_BLUE, LIGHT_GREEN],
    "canopy": [DARK_TECH, PURPLE_, ORANGE_],
    "facade": [DARK_TECH, LIGHT_BLUE, PURPLE_],
    "vault": [PURPLE_, LIGHT_BLUE, LIGHT_GREEN],
    "windtruss": [PURPLE_, LIGHT_BLUE, ORANGE_],
    "girder": [PALE_BLUE, LIGHT_BLUE, LIGHT_GREEN, ORANGE_],
    "cablestayed": [LIGHT_BLUE, PURPLE_, ORANGE_],
    "arch": [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_],
    "building": [LIGHT_BLUE, LIGHT_GREEN, ORANGE_],
    "city": [PALE_BLUE, LIGHT_BLUE, LIGHT_GREEN, ORANGE_],
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
    """A GhPython component running one function of StructuresGeometry.py: its inputs as (name, hint)
    and its outputs as names, after its own out, which carries what the code prints and its errors."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, (name, hint) in enumerate(inputs):
        p = c.CreateParameter(GH_ParameterSide.Input, i)
        p.Name = p.NickName = name
        p.TypeHint = hint
        c.Params.RegisterInputParam(p)
    for i, name in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    c.Code = (System.IO.File.ReadAllText(GEOMETRY) + "\n\n" + ", ".join(outputs) + " = " + function + "(" +
              ", ".join(name for name, hint in inputs) + ")\n")
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
        self.parts = {"1": [], "2": [], "3": [], "4": [], "5": [], "6": []}
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
        """A support at each of the geometry's points: held, Tx to Rz, True where held."""
        s = component(self.grid, SUPPORT, "Support", name, MEMBERS, LOADED, gap=30)
        wire(inp(s, S_POSITION), self.g(points))
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

    def slabs(self, name, meshes, thickness):
        """Concrete slabs, thickness thick, as ASD Shells on a mesh output."""
        concrete = component(self.grid, ND_MATERIAL, "nD", "Slab concrete", SECTIONS, MAIN, gap=30)
        words(inp(concrete, 0), "Concrete")
        number(inp(concrete, 1), 30e6)
        number(inp(concrete, 2), 30e6 / 2.4)
        number(inp(concrete, 3), 0.2)
        number(inp(concrete, 4), 2500)
        inp(concrete, 5).PersistentData.Append(GH_Colour(AT_REST))
        plate = component(self.grid, PLATE_FIBER, "Plate Fiber Section", "{0:g} mm".format(thickness * 1000), SECTIONS, MAIN, gap=30)
        number(inp(plate, 1), thickness)
        wire(inp(plate, 2), out(concrete))
        shell = component(self.grid, SHELL, "ASD Shell", name, MEMBERS, MAIN, gap=30)
        wire(flat(inp(shell, 0)), self.g(meshes))
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

    def shaking(self, name, scale, top, step, bands=False, supports=1.0):
        """A Model View of the analysed model at the Step slider's step, deformed scale times, its
        colours over 0 to top mm for every step alike, with a Legend; its supports drawn supports times
        their size, which is a metre or so."""
        stepper = slider(self.grid, "Step", 0, 1000, step, 0, VIEWS, SHOWN, gap=30)
        view = self.deformed(name, out(self.run), scale, title="Displacement [mm]", loads=False, bands=bands)
        # The elements' edges and the supports: what each piece is, and where it is held.
        menu_setting(view, "_ckWires", "Active", True)
        menu_setting(view, "_ckShowSupports", "Active", True)
        menu_setting(view, "_slSupportScale", "Value", supports)
        wire(inp(view, V_STEP), stepper)
        inp(view, V_RANGE).PersistentData.Clear()
        inp(view, V_RANGE).PersistentData.Append(GH_Interval(Interval(0.0, top)))
        self.parts["6"].append(stepper)
        return view

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
        colours = {"1": RED, "2": ORANGE, "3": PURPLE, "4": GREEN, "5": BLUE, "6": GREY}
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

def quake(f1, f2):
    """The sliders every earthquake has: how strong the record is, how much of it, and the damping,
    5% of critical at two frequencies that bracket the modes that matter."""
    return [
        ("Scale", "Ground motion scale", 0, 3, 1.0, 2),
        ("Duration", "Duration [s]", 2, 31, 12, 1),
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


BUILDERS = {"hall": hall, "tower": tower, "canopy": canopy, "facade": facade, "vault": vault, "windtruss": windtruss,
            "girder": girder, "cablestayed": cablestayed, "arch": arch, "building": building, "city": city}


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
