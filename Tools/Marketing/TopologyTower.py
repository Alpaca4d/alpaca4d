#! python3
# A cube of 16 x 16 x 16 bricks held at its four bottom corners and loaded at the middle of its top,
# optimised to 8% of its volume: four legs that grow from the corners and meet under the load. It is
# the tower scene of the marketing pictures (run.sh, next to it), not an example of the docs.
#
# Tools/Examples/make.sh writes TopologyTower.gh straight from this script, without Rhino:
#
#     ../Examples/make.sh TopologyTower.py
#
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# top3d's units (Liu and Tovar 2014): a unit modulus, Poisson 0.3, unit cubes, loads of 1, a penalty
# of 3 and a filter of 1.5. 100 iterations at most; it settles after 44, in 12 s on an M2 Pro.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_Boolean
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move

# Alpaca4d
ND_MATERIAL = "B40E1124-E203-4E1A-8962-2A6FB8D751A5"        # nD (ElasticIsotropic)
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
TOPOLOGY = "8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93"           # Topology Optimisation

# Topology Optimisation's inputs and outputs, by position.
T_MODEL, T_KEEP, T_VOLUME_FRACTION, T_ITERATIONS, T_RUN = range(5)
T_STEP = 12                                                 # registered last, after the Settings menu's
T_SURFACE, T_SOLID, T_DENSITY, T_ELEMENT, T_COMPLIANCE, T_VOLUME, T_CONVERGED, T_HISTORY, T_LOG = range(9)

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)


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


def integer(param, value):
    """Types a whole number into an unwired input that takes only those, such as Series' Count."""
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def point(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Point(Point3d(x, y, z)))
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


def vector(grid, guid, nickname, factor, col, row=None, gap=0):
    """A unit vector along an axis, times factor - a number, or a source to take it from."""
    v = component(grid, guid, "The Unit X, Y and Z components", nickname, col, row, gap)
    if isinstance(factor, (int, float)):
        number(inp(v, 0), factor)
    else:
        wire(inp(v, 0), factor)
    return v


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


# ---------------------------------------------------------------- the tower

T_FILTER_RADIUS = 6                                         # Topology Optimisation's Settings menu

# Columns, in the order the data flows, and rows, one per part of the model.
NUMBERS, SERIES_, POINTS, MESHES, BRICKS_, ELEMENTS, LOADS, PATTERN, MODEL, OPTIMISE, RESULTS = range(11)
MATERIAL_ = 0
TOWER, TOWER_SUPPORT, TOWER_LOAD = 1, 2, 3
N = 16                       # bricks along each side
C = N // 2


def support(g, nickname, points, held, row, gap=0):
    """A Support on points, holding the translations in held (x, y, z) and none of the rotations."""
    s = component(g, SUPPORT, "Support", nickname, ELEMENTS, row, gap)
    flat(wire(inp(s, 0), out(points)))
    for i in range(3):
        inp(s, i + 1).PersistentData.Clear()
        inp(s, i + 1).PersistentData.Append(GH_Boolean(held[i]))
    for i in range(3):
        inp(s, i + 4).PersistentData.Clear()
        inp(s, i + 4).PersistentData.Append(GH_Boolean(False))
    return s



def build(doc):
    g = Grid(doc)
    values = [text(g, "Material", "top3d", ELEMENTS, MATERIAL_),
              text(g, "E", "1", ELEMENTS, MATERIAL_),
              text(g, "G", "0.384615384615385", ELEMENTS, MATERIAL_),
              text(g, "Poisson", "0.3", ELEMENTS, MATERIAL_),
              text(g, "Density", "0", ELEMENTS, MATERIAL_)]
    material = component(g, ND_MATERIAL, "nD", "top3d material", LOADS, MATERIAL_)
    for i, source in enumerate(values):
        wire(inp(material, i), source)

    # The cube: its plan meshed, copied up level by level, bricks between each pair.
    plan = component(g, RECTANGLE_3PT, "Rectangle 3Pt", "Cube plan", POINTS, TOWER)
    point(inp(plan, 0), 0, 0, 0)
    point(inp(plan, 1), N, 0, 0)
    point(inp(plan, 2), N, N, 0)
    plan_mesh = component(g, MESH_PLANE, "Mesh Plane", "Plan mesh", MESHES, TOWER)
    wire(inp(plan_mesh, 0), out(plan))
    integer(inp(plan_mesh, 1), N)
    integer(inp(plan_mesh, 2), N)
    levels = component(g, SERIES, "Series", "Levels", SERIES_, TOWER)
    number(inp(levels, 0), 0.0)
    number(inp(levels, 1), 1.0)
    integer(inp(levels, 2), N + 1)
    lift = vector(g, UNIT_Z, "Up to each level", out(levels), POINTS, TOWER, gap=10)
    level_meshes = component(g, MOVE, "Move", "Level meshes", MESHES, TOWER, gap=10)
    wire(inp(level_meshes, 0), out(plan_mesh))
    wire(inp(level_meshes, 1), out(lift))
    bricks = component(g, MESH_TO_BRICK, "Mesh Series to Brick", None, BRICKS_, TOWER)
    wire(inp(bricks, 0), out(level_meshes))
    cube = component(g, BRICK, "SSP Brick", "Cube", LOADS, TOWER)
    wire(inp(cube, 0), out(bricks))
    faint(wire(inp(cube, 1), out(material)))

    # Held at its four bottom corners, two by two nodes each.
    corners = component(g, CONSTRUCT_POINT, "Construct Point", "Corner nodes", POINTS, TOWER_SUPPORT)
    number(inp(corners, 0), 0, 1, N - 1, N)
    number(inp(corners, 1), 0, 1, N - 1, N).DataMapping = GH_DataMapping.Graft
    number(inp(corners, 2), 0.0)
    held = support(g, "Held corners", corners, (True, True, True), TOWER_SUPPORT)

    # -1 at each of the three by three nodes in the middle of the top.
    top = component(g, CONSTRUCT_POINT, "Construct Point", "Middle of the top", POINTS, TOWER_LOAD)
    number(inp(top, 0), C - 1, C, C + 1)
    number(inp(top, 1), C - 1, C, C + 1).DataMapping = GH_DataMapping.Graft
    number(inp(top, 2), N)
    down = vector(g, UNIT_Z, "-1", -1.0, ELEMENTS, TOWER_LOAD)
    load = component(g, POINT_LOAD, "Point Load", "Top load", LOADS, TOWER_LOAD)
    flat(wire(inp(load, 0), out(top)))
    wire(inp(load, 1), out(down))
    pattern = component(g, LOAD_PATTERN, "Load Pattern", None, PATTERN, TOWER_LOAD)
    flat(wire(inp(pattern, 1), out(load)))

    right = g.arrange(0, 0, stages=(POINTS, ELEMENTS))

    m = Grid(doc)
    model = component(m, ASSEMBLE, "Assemble Model", "Tower", MODEL, TOWER)
    flat(wire(inp(model, 0), out(cube)))
    flat(wire(inp(model, 1), out(held)))
    flat(wire(inp(model, 2), out(pattern)))
    run = toggle(m, "Run", False, MODEL, TOWER_SUPPORT)
    step = slider(m, "Step", 0, 100, 100, 0, MODEL, TOWER_SUPPORT, gap=10)
    topology = emit(TOPOLOGY, "Topology Optimisation")
    topology.NickName = "Tower"
    open_menus(topology)
    m.put(topology, OPTIMISE, TOWER)
    faint(wire(inp(topology, T_MODEL), out(model)))
    number(inp(topology, T_VOLUME_FRACTION), 0.08)
    inp(topology, T_ITERATIONS).PersistentData.Clear()
    inp(topology, T_ITERATIONS).PersistentData.Append(GH_Integer(100))
    wire(inp(topology, T_RUN), run)
    wire(inp(topology, T_STEP), step)
    number(inp(topology, T_FILTER_RADIUS), 1.5)
    history = readout(m, "History", out(topology, T_HISTORY), RESULTS, TOWER, width=420.0, height=100.0)
    m.arrange(right + 160, 0)

    group(doc, "1 - Material: E = 1, Poisson 0.3", [material] + values, PURPLE)
    group(doc, "2 - A cube of 16 x 16 x 16 bricks", [plan, plan_mesh, levels, lift, level_meshes, bricks, cube], ORANGE)
    group(doc, "3 - Held at the four bottom corners, loaded at the middle of the top",
          [corners, held, top, down, load, pattern], GREEN)
    group(doc, "4 - Topology Optimisation: 8% of the volume, set Run to true", [model, run, step, topology, history], BLUE)
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != topology.InstanceGuid:
            obj.Hidden = True


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()


if __name__ == "__main__":
    main()
