#! python3
# A reinforced concrete section's moment-curvature response: the Moment Curvature example, built on the
# Grasshopper canvas.
#
# make.sh, next to it, writes MomentCurvature.gh straight from this script, without Rhino:
#     ./make.sh MomentCurvature.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 300 x 500 section of concrete, Concrete01, with phi 24 bars of ReinforcingSteel 30 mm in from
# its short faces, eight along the bottom and three along the top, under 400 kN of compression.
# Moment Curvature bends it about its local y axis until the first fibre reaches its ultimate
# strain, and Moment Curvature Diagram draws the curve beside the section, first yield and the
# ultimate state marked.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import math
import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList, GH_ValueListItem, GH_ValueListMode
from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Point, GH_String, GH_Boolean
from Grasshopper.GUI.Base import GH_SliderAccuracy
from Rhino.Geometry import Point3d
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
XY_PLANE = "17b7152b-d30d-4d50-b9ef-c9fe25576fc2"           # Vector > Plane > XY Plane

# Alpaca4d
CONCRETE = "57BF0FDB-B333-491C-B9EB-50783029B8C2"           # Concrete01
REBAR = "47B04CA7-0F7E-4585-8F89-D31592E61394"              # ReinforcingSteel
PATCH = "49DDD2E0-99B5-4D14-976E-531481B07909"              # Patch
POINT_FIBER = "934021CE-8D12-4FCA-8CF1-9158EB08208A"        # Fiber Point
FIBER_SECTION = "18cd5969-e0e4-4acf-9790-0af2c22cfeb9"      # Fiber Section
MOMENT_CURVATURE = "5A456D82-1C30-4EE1-AAC7-876846328C8F"   # Moment Curvature
DIAGRAM = "B7297C55-C18D-43C1-A082-979958576A8F"            # Moment Curvature Diagram

# The section, in m: y along world X, z along world Y, as a fibre is written. Bent about y, its depth
# is along world Y; the bars sit COVER in from the faces it bends towards, eight at the bottom, three
# at the top.
WIDTH, DEPTH, COVER, BAR = 0.3, 0.5, 0.03, 24
BOTTOM, TOP = 8, 3
AXIAL = -400.0              # kN, compression
# Where the diagram is drawn, beside the section, and how large: m.
DIAGRAM_AT, DIAGRAM_SIZE = (0.4, -0.25, 0.0), 0.5

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
    per part of the definition. Column widths and row heights are the objects' own, measured once
    Grasshopper has laid them out. A column listed in stages starts a new group."""

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


def number(param, *values):
    """Sets what an input holds when nothing is wired to it."""
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_Number(float(v)))
    return param


def integer(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def text(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
    return param


def boolean(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Boolean(value))
    return param


def points(param, *xyz):
    param.PersistentData.Clear()
    for x, y, z in xyz:
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


def value_list(grid, name, items, selected, col, row=None, gap=0):
    """A drop-down value list: items are (name, expression); text is an expression in quotes."""
    v = GH_ValueList()
    v.CreateAttributes()
    v.NickName = name
    v.ListMode = GH_ValueListMode.DropDown
    v.ListItems.Clear()
    for item_name, expression_ in items:
        v.ListItems.Add(GH_ValueListItem(item_name, expression_))
    v.SelectItem(selected)
    return grid.put(v, col, row, gap)


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


def plane_at(grid, x, nickname, col, row):
    """An XY plane with its origin at (x, 0, 0): where a view is drawn, clear of the others."""
    origin = component(grid, CONSTRUCT_POINT, "Construct Point", nickname + " origin", col, row)
    number(inp(origin, 0), x)
    plane = component(grid, XY_PLANE, "XY Plane", nickname, col + 1, row)
    wire(inp(plane, 0), out(origin))
    return origin, plane


# ---------------------------------------------------------------- the section

# Columns in the order the data flows...
MATERIALS, GEOMETRY, MESH, FIBRES, SECTION, ANALYSIS, RESULTS, PLANE_COL, VIEW = range(9)
# ...and rows.
CONCRETE_ROW, BARS_ROW, RESULTS_ROW = range(3)


def area(diameter_mm):
    return math.pi * (diameter_mm / 1000.0) ** 2 / 4.0


def bars():
    """The bars' centres: BOTTOM along the bottom face, TOP along the top, COVER in from the faces."""
    y0, y1 = -WIDTH / 2 + COVER, WIDTH / 2 - COVER
    row = lambda n, z: [(y0 + (y1 - y0) * i / (n - 1), z, 0.0) for i in range(n)]
    return row(BOTTOM, -DEPTH / 2 + COVER) + row(TOP, DEPTH / 2 - COVER)


def build(doc):
    grid = Grid(doc)

    # 1 - The materials, with their presets: Concrete01, C30/37-like, and ReinforcingSteel, B500-like.
    concrete = component(grid, CONCRETE, "Concrete01", "Concrete", MATERIALS, CONCRETE_ROW)
    rebar = component(grid, REBAR, "ReinforcingSteel", "Bars", MATERIALS, BARS_ROW)

    # 2 - The concrete: the outline, meshed into 12 x 20 faces - one fibre a face - as a patch; and the
    # bars, a fibre each.
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "300 x 500", GEOMETRY, CONCRETE_ROW)
    points(inp(outline, 0), (-WIDTH / 2, -DEPTH / 2, 0.0))
    points(inp(outline, 1), (WIDTH / 2, -DEPTH / 2, 0.0))
    points(inp(outline, 2), (WIDTH / 2, DEPTH / 2, 0.0))
    mesh = component(grid, MESH_PLANE, "Mesh Plane", "Fibres", MESH, CONCRETE_ROW)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), 12)
    integer(inp(mesh, 2), 20)
    patch = component(grid, PATCH, "Patch", "Concrete", FIBRES, CONCRETE_ROW)
    wire(inp(patch, 0), out(mesh))
    wire(inp(patch, 1), out(concrete))
    steel = component(grid, POINT_FIBER, "Fiber Point", "phi 24 bars", FIBRES, BARS_ROW)
    points(inp(steel, 0), *bars())
    number(inp(steel, 1), *[area(BAR)] * (BOTTOM + TOP))
    wire(inp(steel, 2), out(rebar))
    section = component(grid, FIBER_SECTION, "Fiber Section", None, SECTION, CONCRETE_ROW)
    wire(inp(section, 0), out(steel))
    wire(inp(section, 2), out(patch))

    # 3 - Bent about y under 400 kN of compression, until the first fibre reaches its ultimate strain:
    # Direction 180, so that the eight bars at the bottom are the ones in tension. 0 bends it the other
    # way, and puts the three at the top in tension.
    curvature = component(grid, MOMENT_CURVATURE, "Moment Curvature", None, ANALYSIS, CONCRETE_ROW)
    wire(inp(curvature, 0), out(section))
    number(inp(curvature, 1), AXIAL)
    number(inp(curvature, 2), 180.0)
    integer(inp(curvature, 3), 200)

    # 4 - What it gives, and the curve drawn beside the section.
    readouts = []
    for p in curvature.Params.Output:
        if p.NickName in ("MYield", "MUltimate", "EI", "Failure") or p.Name in ("UltimateCurvature", "Ductility"):
            readouts.append(panel(grid, p.Name, p, 220 if p.Name == "Failure" else 120, 40 if p.Name == "Failure" else 20,
                                  RESULTS, RESULTS_ROW, gap=10))
    at = component(grid, CONSTRUCT_POINT, "Construct Point", "Diagram at", RESULTS, CONCRETE_ROW)
    number(inp(at, 0), DIAGRAM_AT[0])
    number(inp(at, 1), DIAGRAM_AT[1])
    plane = component(grid, XY_PLANE, "XY Plane", "Diagram plane", PLANE_COL, CONCRETE_ROW)
    wire(inp(plane, 0), out(at))
    diagram = component(grid, DIAGRAM, "Moment Curvature Diagram", None, VIEW, CONCRETE_ROW)
    wire(inp(diagram, 0), curvature.Params.Output[[p.Name for p in curvature.Params.Output].index("Result")])
    wire(inp(diagram, 1), out(plane))
    number(inp(diagram, 2), DIAGRAM_SIZE)

    grid.arrange(0, 0, stages=(GEOMETRY, SECTION, ANALYSIS, RESULTS))

    group(doc, "1 - Materials", [concrete, rebar], RED)
    group(doc, "2 - The section: concrete in fibres, a fibre a bar", [outline, mesh, patch, steel, section], ORANGE)
    group(doc, "3 - Moment Curvature, under 400 kN of compression", [curvature], PURPLE)
    group(doc, "4 - Yield, ultimate state, and the curve beside the section", readouts + [at, plane, diagram], BLUE)

    # Only the section and the diagram draw in Rhino, not the construction behind them.
    shown = set(str(o.InstanceGuid) for o in (section, diagram))
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in shown:
            obj.Hidden = True

    note(doc, "Moment curvature",
         "Moment curvature - a reinforced concrete section bent until it fails.\n\n"
         "A 300 x 500 section, Concrete01 in 240 fibres, with phi 24 bars of ReinforcingSteel 30 mm in "
         "from its short faces: eight along the bottom, three along the top. 400 kN of compression is held "
         "while the curvature about y grows, in 200 increments, until the first fibre reaches its "
         "ultimate strain, bent with Direction 180 so that the eight bars are in tension.\n\n"
         "Moment Curvature Diagram draws the curve beside the section: first yield and the ultimate "
         "state marked. The panels give the moment at first yield, the ultimate moment and curvature, "
         "the curvature ductility, the cracked stiffness to first yield, and which fibre failed.\n\n"
         "Try: Axial from -400 to -1500 and watch the ductility fall; Direction 0 to bend it the other "
         "way, the three bars in tension; the bar counts at the top of the script.",
         0, -340, 560, 270)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Moment curvature built.")


if __name__ == "__main__":
    main()
