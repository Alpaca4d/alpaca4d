#! python3
# A reinforced concrete column's N-My-Mz resistance domain: the Interaction Domain example of the
# docs, built on the Grasshopper canvas.
#
# make.sh, next to it, writes ColumnDomain.gh straight from this script, without Rhino:
#     ./make.sh ColumnDomain.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# A 400 x 600 column of C30/37 with 4 phi 25 at its corners and 2 phi 20 at mid-height, in design
# values: Concrete01 with its crushing strength equal to its peak is Eurocode 2's parabola-rectangle,
# Steel01 with no hardening is elastic-perfectly plastic. Interaction Domain sweeps its domain,
# Domain Check checks three combinations against it, and the three views draw it beside the
# section, the checks on them.
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
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
XY_PLANE = "17b7152b-d30d-4d50-b9ef-c9fe25576fc2"           # Vector > Plane > XY Plane
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane

# Alpaca4d
CONCRETE = "57BF0FDB-B333-491C-B9EB-50783029B8C2"           # Concrete01
STEEL = "DEB86811-4429-426F-8A7B-02DF89C5A074"              # Steel01
PATCH = "49DDD2E0-99B5-4D14-976E-531481B07909"              # Patch
POINT_FIBER = "934021CE-8D12-4FCA-8CF1-9158EB08208A"        # Fiber Point
FIBER_SECTION = "18cd5969-e0e4-4acf-9790-0af2c22cfeb9"      # Fiber Section
DOMAIN = "1ACCF37E-EA71-4C37-8D0D-1D0C55179BAD"             # Interaction Domain
CHECK = "82267846-9782-4E58-8628-383D5E0B4329"              # Domain Check
DOMAIN_3D = "084A5E4D-98ED-4D79-A15C-BEE423F4ACF4"          # Domain 3D
NM = "F0A7A4EC-435E-493E-89C2-7231CB0C2D28"                 # N-M Diagram
MYMZ = "41525B7A-8024-44B4-ABFC-4680277DF95B"               # My-Mz Diagram

# The section, in m: y along world X, z along world Y, as a fibre is written.
WIDTH, DEPTH, COVER = 0.4, 0.6, 0.05
BARS = [(-0.15, -0.25, 25), (0.15, -0.25, 25), (-0.15, 0.25, 25), (0.15, 0.25, 25),
        (-0.15, 0.0, 20), (0.15, 0.0, 20)]

# Three combinations, kN and kN m.
N_ED = [-2000.0, -1000.0, -3000.0]
MY_ED = [250.0, 400.0, 100.0]
MZ_ED = [80.0, 150.0, 60.0]

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


# ---------------------------------------------------------------- the column

# Columns in the order the data flows...
MATERIALS, GEOMETRY, MESH, FIBRES, SECTION, DOMAIN_COL, FORCES, CHECK_COL, RESULTS = range(9)
PLANE_ORIGIN, PLANE, VIEWS = range(9, 12)
# ...and rows.
CONCRETE_ROW, BARS_ROW, CHECKS_ROW, VIEW_3D, VIEW_NM, VIEW_MYMZ = range(6)


def area(diameter_mm):
    return math.pi * (diameter_mm / 1000.0) ** 2 / 4.0


def build(doc):
    grid = Grid(doc)

    # 1 - Design materials: C30/37 and B500, gamma_c 1.5 with alpha_cc 0.85, gamma_s 1.15, kN/m2.
    concrete = component(grid, CONCRETE, "Concrete01", "C30/37 design", MATERIALS, CONCRETE_ROW)
    text(inp(concrete, 0), "C30/37 design")
    number(inp(concrete, 1), -17000.0)
    number(inp(concrete, 2), -17000.0)
    number(inp(concrete, 3), -0.002)
    number(inp(concrete, 4), -0.0035)
    boolean(inp(concrete, 5), False)
    steel = component(grid, STEEL, "Steel01", "B500 design", MATERIALS, BARS_ROW)
    text(inp(steel, 0), "B500 design")
    number(inp(steel, 1), 500000.0 / 1.15)
    number(inp(steel, 2), 200000000.0)
    number(inp(steel, 3), 0.0)

    # 2 - The concrete: the outline, meshed into 16 x 24 faces - one fibre a face - as a patch.
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", "Outline", GEOMETRY, CONCRETE_ROW)
    points(inp(outline, 0), (-WIDTH / 2, -DEPTH / 2, 0.0))
    points(inp(outline, 1), (WIDTH / 2, -DEPTH / 2, 0.0))
    points(inp(outline, 2), (WIDTH / 2, DEPTH / 2, 0.0))
    mesh = component(grid, MESH_PLANE, "Mesh Plane", "Fibres", MESH, CONCRETE_ROW)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), 16)
    integer(inp(mesh, 2), 24)
    patch = component(grid, PATCH, "Patch", "Concrete", FIBRES, CONCRETE_ROW)
    wire(inp(patch, 0), out(mesh))
    wire(inp(patch, 1), out(concrete))

    # 3 - The bars, one fibre each.
    bars = component(grid, POINT_FIBER, "Fiber Point", "Bars", FIBRES, BARS_ROW)
    points(inp(bars, 0), *[(y, z, 0.0) for y, z, d in BARS])
    number(inp(bars, 1), *[area(d) for y, z, d in BARS])
    wire(inp(bars, 2), out(steel))

    section = component(grid, FIBER_SECTION, "Fiber Section", None, SECTION, CONCRETE_ROW)
    wire(inp(section, 0), out(bars))
    wire(inp(section, 2), out(patch))

    # 4 - The domain, and three combinations checked against it.
    domain = component(grid, DOMAIN, "Interaction Domain", None, DOMAIN_COL, CONCRETE_ROW)
    wire(inp(domain, 0), out(section))

    method = value_list(grid, "Method", [("Constant N", '"Constant N"'), ("Proportional", '"Proportional"')], 0,
                        FORCES, CHECKS_ROW)
    check = component(grid, CHECK, "Domain Check", None, CHECK_COL, CHECKS_ROW)
    wire(inp(check, 0), out(domain))
    number(inp(check, 1), *N_ED)
    number(inp(check, 2), *MY_ED)
    number(inp(check, 3), *MZ_ED)
    wire(inp(check, 4), method)
    sf = panel(grid, "SF", out(check, 0), 120, 80, RESULTS, CHECKS_ROW)
    ok = panel(grid, "OK", out(check, 5), 120, 80, RESULTS, CHECKS_ROW)
    notes = panel(grid, "Materials", out(domain, 7), 300, 60, RESULTS, CONCRETE_ROW)

    # 5 - The views, side by side to the right of the section in Rhino, the checks on each.
    o3, p3 = plane_at(grid, 2.0, "Domain 3D at", PLANE_ORIGIN, VIEW_3D)
    view3d = component(grid, DOMAIN_3D, "Domain 3D", None, VIEWS, VIEW_3D)
    wire(inp(view3d, 0), out(domain))
    wire(inp(view3d, 1), out(p3))
    number(inp(view3d, 2), 4.0)
    wire(inp(view3d, 3), out(check, 6))

    onm, pnm = plane_at(grid, 8.0, "N-M at", PLANE_ORIGIN, VIEW_NM)
    angle = slider(grid, "Angle [deg]", 0, 90, 0, 0, PLANE, VIEW_NM, gap=10)
    viewnm = component(grid, NM, "N-M Diagram", None, VIEWS, VIEW_NM)
    wire(inp(viewnm, 0), out(domain))
    wire(inp(viewnm, 1), angle)
    wire(inp(viewnm, 2), out(pnm))
    number(inp(viewnm, 3), 4.0)
    wire(inp(viewnm, 4), out(check, 6))

    omz, pmz = plane_at(grid, 14.0, "My-Mz at", PLANE_ORIGIN, VIEW_MYMZ)
    viewmz = component(grid, MYMZ, "My-Mz Diagram", None, VIEWS, VIEW_MYMZ)
    wire(inp(viewmz, 0), out(domain))
    number(inp(viewmz, 1), *N_ED)
    wire(inp(viewmz, 2), out(pmz))
    number(inp(viewmz, 3), 4.0)
    wire(inp(viewmz, 4), out(check, 6))

    grid.arrange(0, 0, stages=(GEOMETRY, SECTION, FORCES, PLANE_ORIGIN))

    group(doc, "1 - Design materials", [concrete, steel], RED)
    group(doc, "2 - Concrete and bars", [outline, mesh, patch, bars], ORANGE)
    group(doc, "3 - Section and domain", [section, domain, notes], PURPLE)
    group(doc, "4 - Checks", [method, check, sf, ok], GREEN)
    group(doc, "5 - Views", [o3, p3, view3d, onm, pnm, angle, viewnm, omz, pmz, viewmz], BLUE)

    # Only the section and the views draw in Rhino, not the construction behind them.
    shown = set(str(o.InstanceGuid) for o in (section, view3d, viewnm, viewmz))
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in shown:
            obj.Hidden = True

    note(doc, "Column domain",
         "Column domain - the Alpaca4d Interaction Domain example.\n\n"
         "A 400 x 600 column of C30/37, 4 phi 25 at the corners and 2 phi 20 at mid-height, in "
         "design values: Concrete01 with fcu = fco is Eurocode 2's parabola-rectangle, Steel01 with "
         "b = 0 is elastic-perfectly plastic. Interaction Domain holds the concrete to 3.5 per mille "
         "(2 per mille at point C in full compression) and the bars to eud = 1 %.\n\n"
         "Axial resistance: -5117 kN in compression (fcd Ac + 400 MPa As), 1127 kN in tension.\n\n"
         "Domain Check, N held:\n"
         "  N -2000, My 250, Mz 80: SF 1.59\n"
         "  N -1000, My 400, Mz 150: SF 0.89 - it fails\n"
         "  N -3000, My 100, Mz 60: SF 2.74\n"
         "Switch Method to Proportional: 1.42, 0.88, 1.44 - the third is axial-dominated, and N "
         "grows with the moments.\n\n"
         "In Rhino: the section at the origin, then Domain 3D, the N-M diagram (turn Angle) and the "
         "My-Mz diagrams at the checks' N, with each check in black and where it meets the "
         "boundary in red.",
         0, -360, 560, 300)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Column domain built. Change the forces or the Method and watch Domain Check.")


if __name__ == "__main__":
    main()
