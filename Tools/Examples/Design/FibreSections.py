#! python3
# Fibre sections with the new laws: three studies of Moment Curvature and Interaction Domain, built on the
# Grasshopper canvas.
#
# One script, three definitions; the case is picked by SECTION_CASE, and make.sh, in the folder above,
# writes each straight from this script, without Rhino:
#     SECTION_CASE=laws      ./make.sh Design/FibreSections.py FibreLaws.gh
#     SECTION_CASE=hea       ./make.sh Design/FibreSections.py HeaSection.gh
#     SECTION_CASE=composite ./make.sh Design/FibreSections.py CompositeSections.gh
# It also runs in Rhino with Grasshopper and Alpaca4d installed (CASE below, when SECTION_CASE is not set).
#
# laws       A 300 x 500 reinforced concrete section six times over, each with a different law: Concrete01,
#            Concrete02 and Concrete04 for the concrete, Steel01, Steel02, ElasticPP and Hysteretic for the
#            steel, from the Concrete, Steel and Other Laws components. Their curves lie on the same
#            Moment Curvature Diagram, their domains in the same N-M Diagram.
# hea        A HEA 300 in fibres - two flanges and a web - bent about its strong axis in Steel02, Steel01 and Hysteretic,
#            and about its weak axis once; and its domain in N-M and My-Mz.
# composite  Three sections that mix materials: a HEA 240 under a concrete slab (a composite beam), a HEA 200
#            encased in concrete, and a reinforced concrete beam with a carbon fibre laminate along its top
#            and bottom, beside the same beam without them.
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

CASE = System.Environment.GetEnvironmentVariable("SECTION_CASE") or "laws"

# Grasshopper
RECTANGLE_3PT = "9BC98A1D-2ECC-407E-948A-09A09ED3E69D"      # Curve > Primitive > Rectangle 3Pt
MESH_PLANE = "8ADBF481-7589-4A40-B490-006531EA001D"         # Mesh > Primitive > Mesh Plane
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
XY_PLANE = "17b7152b-d30d-4d50-b9ef-c9fe25576fc2"           # Vector > Plane > XY Plane

# Alpaca4d
CONCRETE = "57BF0FDB-B333-491C-B9EB-50783029B8C2"           # Concrete: Concrete01, 02, 04
STEEL = "DEB86811-4429-426F-8A7B-02DF89C5A074"              # Steel: Steel01, 02, ReinforcingSteel
OTHER = "B3F7D2A5-61C8-4E94-8A0D-5E2C9F174B38"              # Other Laws: ElasticPP, Hysteretic, Elastic
PATCH = "49DDD2E0-99B5-4D14-976E-531481B07909"              # Patch
POINT_FIBER = "934021CE-8D12-4FCA-8CF1-9158EB08208A"        # Fiber Point
FIBER_SECTION = "18cd5969-e0e4-4acf-9790-0af2c22cfeb9"      # Fiber Section
MOMENT_CURVATURE = "5A456D82-1C30-4EE1-AAC7-876846328C8F"   # Moment Curvature
DIAGRAM = "B7297C55-C18D-43C1-A082-979958576A8F"            # Moment Curvature Diagram
DOMAIN = "1ACCF37E-EA71-4C37-8D0D-1D0C55179BAD"             # Interaction Domain
NM_DIAGRAM = "F0A7A4EC-435E-493E-89C2-7231CB0C2D28"         # N-M Diagram
MYMZ_DIAGRAM = "41525B7A-8024-44B4-ABFC-4680277DF95B"       # My-Mz Diagram

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


# ---------------------------------------------------------------- materials, by law

# Columns in the order the data flows...
GEOMETRY, MATERIALS, FIBRES, SECTION, ANALYSIS, DOMAIN_COL, NM_COL, READ, PLANE_COL, VIEW = range(10)

E_STEEL = 210000000.0           # kN/m2
FC = 38000.0                    # kN/m2: C30/37's mean strength, fcm
FY_REBAR, FY_STEEL = 500000.0, 355000.0


def area(diameter_mm):
    return math.pi * (diameter_mm / 1000.0) ** 2 / 4.0


def law(grid, guid, what, unit, nickname, col, row, gap=0):
    """A Concrete, Steel or Other Laws component, on the law its Law menu would pick."""
    c = component(grid, guid, what, nickname, col, row, gap)
    if unit is not None:
        switch_unit(c, unit)
    text(inp(c, 0), nickname)
    return c


def concrete_law(grid, unit, nickname, col, row, fc=FC, gap=0):
    """Concrete with strength fc (kN/m2): the parabola to 0.002 and 0.0035, a fall to 80 % of fc, in each law's
    own inputs - and the tension Concrete02 and Concrete04 have, 3 MPa."""
    c = law(grid, CONCRETE, "Concrete", unit if unit != "Concrete01" else None, nickname, col, row, gap)
    if unit == "Concrete02":
        for i, v in enumerate((-fc, -0.002, -0.8 * fc, -0.0035, 0.2, 3000.0, 2000000.0)):
            number(inp(c, 1 + i), v)
    elif unit == "Concrete04":
        for i, v in enumerate((-fc, -0.002, -0.0035, 2.0 * fc / 0.002, 3000.0, 0.0001)):
            number(inp(c, 1 + i), v)
    else:
        for i, v in enumerate((-fc, -0.8 * fc, -0.002, -0.0035)):
            number(inp(c, 1 + i), v)
    return c


def steel_law(grid, unit, nickname, col, row, fy=FY_STEEL, e=E_STEEL, gap=0):
    """Steel of yield strength fy (kN/m2) and modulus e, in the law the Law menu names: hardening 1 % in
    Steel01 and Steel02, a bar's fu = 1.08 fy in ReinforcingSteel, no hardening in ElasticPP and a rise to
    1.08 fy at 2 % and 1.15 fy at 8 % in Hysteretic."""
    if unit in ("Steel01", "Steel02", "ReinforcingSteel"):
        c = law(grid, STEEL, "Steel", unit if unit != "Steel01" else None, nickname, col, row, gap)
        if unit == "ReinforcingSteel":
            number(inp(c, 1), fy)
            number(inp(c, 2), 1.08 * fy)
            number(inp(c, 3), e)
        else:
            number(inp(c, 1), fy)
            number(inp(c, 2), e)
            number(inp(c, 3), 0.01)
        return c
    c = law(grid, OTHER, "Other Laws", unit, nickname, col, row, gap)
    if unit == "ElasticPP":
        number(inp(c, 1), e)
        number(inp(c, 2), fy / e)
    else:
        for i, v in enumerate((fy / e, fy, 0.02, 1.08 * fy, 0.08, 1.15 * fy)):
            number(inp(c, 1 + i), v)
    return c


def laminate(grid, nickname, col, row, gap=0):
    """A carbon fibre laminate: linear, E = 160 GPa, to a rupture strain of 1.7 %, in Other Laws' Elastic."""
    c = law(grid, OTHER, "Other Laws", "Elastic", nickname, col, row, gap)
    number(inp(c, 1), 160000000.0)
    number(inp(c, 3), 0.017)
    return c


# ---------------------------------------------------------------- geometry

def rectangle(grid, label, y0, z0, y1, z1, u, v, col, row, gap=0):
    """A rectangle of the section, y along world X and z along world Y, meshed u by v: a fibre a face."""
    outline = component(grid, RECTANGLE_3PT, "Rectangle 3Pt", label, col, row, gap)
    points(inp(outline, 0), (y0, z0, 0.0))
    points(inp(outline, 1), (y1, z0, 0.0))
    points(inp(outline, 2), (y1, z1, 0.0))
    mesh = component(grid, MESH_PLANE, "Mesh Plane", label + " fibres", col + 1, row, gap)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), u)
    integer(inp(mesh, 2), v)
    return outline, mesh


def cells(size, step=0.02):
    return max(1, int(round(size / step)))


def i_section(h, b, tw, tf):
    """A rolled I section as three rectangles (y0, z0, y1, z1, u, v, name): the flanges and the web, the root
    radii left out."""
    web = h / 2.0 - tf
    return [(-b / 2.0, h / 2.0 - tf, b / 2.0, h / 2.0, cells(b, 0.0125), 2, "top flange"),
            (-b / 2.0, -h / 2.0, b / 2.0, -h / 2.0 + tf, cells(b, 0.0125), 2, "bottom flange"),
            (-tw / 2.0, -web, tw / 2.0, web, 2, cells(2.0 * web, 0.0125), "web")]


def bar_points(ys, z):
    return [(y, z, 0.0) for y in ys]


# ---------------------------------------------------------------- one section, analysed

class Chain(object):
    """A fibre section made of patches and bars, and what it is put through. Collects what it makes for the
    groups of the definition."""

    def __init__(self):
        self.patches, self.bars = [], []
        self.section = self.curvature = self.domain = self.nm = None
        self.panels = []
        self.fibres = []


def output(c, name):
    for p in c.Params.Output:
        if p.Name == name or p.NickName == name:
            return p
    raise Exception("{0} has no output {1}".format(c.Name, name))


def chain(grid, row, patches, bars, axial, direction, eud, nm_plane, label):
    """A section from patches - (mesh, material component) pairs - and bars - (points, areas, material
    component) - bent in Direction under Axial, with the domain it has and its N-M diagram on nm_plane."""
    ch = Chain()
    for k, (mesh, material) in enumerate(patches):
        p = component(grid, PATCH, "Patch", label + " patch " + str(k + 1), FIBRES, row, gap=0)
        wire(inp(p, 0), out(mesh))
        wire(inp(p, 1), out(material))
        ch.patches.append(p)
    for k, (pts, areas, material) in enumerate(bars):
        f = component(grid, POINT_FIBER, "Fiber Point", label + " bars " + str(k + 1), FIBRES, row, gap=0)
        points(inp(f, 0), *pts)
        number(inp(f, 1), *areas)
        wire(inp(f, 2), out(material))
        ch.bars.append(f)
    ch.section = component(grid, FIBER_SECTION, "Fiber Section", label, SECTION, row)
    for f in ch.bars:
        wire(inp(ch.section, 0), out(f))
    for p in ch.patches:
        wire(inp(ch.section, 2), out(p))

    ch.curvature = component(grid, MOMENT_CURVATURE, "Moment Curvature", label, ANALYSIS, row)
    wire(inp(ch.curvature, 0), out(ch.section))
    number(inp(ch.curvature, 1), axial)
    number(inp(ch.curvature, 2), direction)
    integer(inp(ch.curvature, 3), 200)
    number(inp(ch.curvature, 5), eud)

    ch.domain = component(grid, DOMAIN, "Interaction Domain", label, DOMAIN_COL, row)
    wire(inp(ch.domain, 0), out(ch.section))
    number(inp(ch.domain, 1), eud)
    ch.nm = component(grid, NM_DIAGRAM, "N-M Diagram", label, NM_COL, row)
    wire(inp(ch.nm, 0), out(ch.domain))
    wire(inp(ch.nm, 2), out(nm_plane))
    number(inp(ch.nm, 3), 0.5)

    for name, w, h in (("UltimateMoment", 90, 20), ("Ductility", 90, 20), ("Failure", 230, 40)):
        ch.panels.append(panel(grid, label + " " + name, output(ch.curvature, name), w, h, READ, row, gap=6))
    return ch


def views(grid, chains, mc_x, nm_x, size, row=0):
    """One Moment Curvature Diagram for all the chains' curves, drawn beside the sections, and the plane the N-M
    diagrams are drawn on."""
    at = component(grid, CONSTRUCT_POINT, "Construct Point", "Diagram at", PLANE_COL, row)
    number(inp(at, 0), mc_x)
    plane = component(grid, XY_PLANE, "XY Plane", "Diagram plane", PLANE_COL, row)
    wire(inp(plane, 0), out(at))
    diagram = component(grid, DIAGRAM, "Moment Curvature Diagram", None, VIEW, row)
    for ch in chains:
        wire(inp(diagram, 0), output(ch.curvature, "Result"))
    wire(inp(diagram, 1), out(plane))
    number(inp(diagram, 2), size)
    return at, plane, diagram


def nm_plane(grid, x, col, row):
    at = component(grid, CONSTRUCT_POINT, "Construct Point", "N-M at", col, row)
    number(inp(at, 0), x)
    plane = component(grid, XY_PLANE, "XY Plane", "N-M plane", col, row, gap=0)
    wire(inp(plane, 0), out(at))
    return at, plane


def finish(grid, doc, chains, extras, stages, groups, notes):
    grid.arrange(300, 440, stages=stages)
    for name, objects, colour in groups:
        group(doc, name, objects, colour)
    # Sections that share their coordinates draw on top of one another: only the first draws in Rhino.
    shown = set([str(chains[0].section.InstanceGuid)])
    for ch in chains:
        shown.add(str(ch.nm.InstanceGuid))
    for o in extras:
        shown.add(str(o.InstanceGuid))
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and str(obj.InstanceGuid) not in shown:
            obj.Hidden = True
    for title, text_, x, y, w, h in notes:
        note(doc, title, text_, x, y, w, h)


def chain_objects(chains):
    objs = []
    for ch in chains:
        objs += ch.patches + ch.bars + [ch.section]
    return objs


# ---------------------------------------------------------------- case 1: the laws

# Concrete law, steel law, in the order of the diagram's curves.
LAWS = [("Concrete01", "Steel01"), ("Concrete02", "Steel01"), ("Concrete04", "Steel01"),
        ("Concrete01", "Steel02"), ("Concrete01", "ElasticPP"), ("Concrete01", "Hysteretic")]


def build_laws(doc):
    grid = Grid(doc)
    W, D, C = 0.3, 0.5, 0.05
    outline, mesh = rectangle(grid, "300 x 500", -W / 2, -D / 2, W / 2, D / 2, 12, 20, GEOMETRY, None)
    bottom = (bar_points((-0.1, 0.0, 0.1), -D / 2 + C), [area(20)] * 3)
    top = (bar_points((-0.1, 0.1), D / 2 - C), [area(16)] * 2)
    at, plane = nm_plane(grid, 2.0, PLANE_COL, 0)

    chains, materials = [], []
    for row, (c_unit, s_unit) in enumerate(LAWS):
        label = c_unit if c_unit != "Concrete01" or s_unit == "Steel01" else "Concrete01"
        label = c_unit + " + " + s_unit
        concrete = concrete_law(grid, c_unit, c_unit, MATERIALS, row)
        steel = steel_law(grid, s_unit, s_unit, MATERIALS, row, fy=FY_REBAR, e=200000000.0, gap=0)
        materials += [concrete, steel]
        # Bottom bars 3 phi 20, top bars 2 phi 16; bent so that the bottom ones are in tension.
        chains.append(chain(grid, row, [(mesh, concrete)],
                            [(bottom[0], bottom[1], steel), (top[0], top[1], steel)],
                            -1000.0, 180.0, 0.05, plane, label))
    mc_at, mc_plane, diagram = views(grid, chains, 0.4, 2.0, 0.5)

    finish(grid, doc, chains, [mc_at, mc_plane, at, plane, diagram], (GEOMETRY, MATERIALS, SECTION, READ),
           [("1 - Laws: concrete and steel, one law each", materials, RED),
            ("2 - The section, six times: 300 x 500, 3 phi 20 below, 2 phi 16 above", [outline, mesh] + chain_objects(chains), ORANGE),
            ("3 - Moment Curvature under 1000 kN of compression, and the domain of each", [c.curvature for c in chains] + [c.domain for c in chains], PURPLE),
            ("4 - Curves on one diagram, domains in the N-M diagram, ultimate moments", [c.nm for c in chains] + sum([c.panels for c in chains], []) + [mc_at, mc_plane, at, plane, diagram], BLUE)],
           [("Fibre laws",
             "Fibre laws - one reinforced concrete section, six sets of laws.\n\n"
             "A 300 x 500 section of 240 concrete fibres with 3 phi 20 bars below and 2 phi 16 above, bent about y under "
             "1000 kN of compression, Direction 180 so that the three bars are in tension. Each row takes another concrete "
             "or steel law from the Concrete, Steel and Other Laws components: Concrete01, Concrete02 and Concrete04 "
             "with Steel01; then Steel02, ElasticPP and a three point Hysteretic backbone with Concrete01. Right click a "
             "law component, Law, to change it.\n\n"
             "Concrete decides the ultimate moment, steel the yield and the tension: the six curves lie close, "
             "ElasticPP, with no hardening, lowest. Each N-M diagram is drawn to fit its own size.\n\n"
             "Try: Axial from -1000 to 0; Concrete02's ft to 0; the bars' strength in Steel.",
             300, 40, 620, 290)])


# ---------------------------------------------------------------- case 2: a HEA section

def build_hea(doc):
    grid = Grid(doc)
    parts = i_section(0.290, 0.300, 0.0085, 0.014)           # HEA 300
    outlines, meshes = [], []
    for k, (y0, z0, y1, z1, u, v, name) in enumerate(parts):
        o, m = rectangle(grid, "HEA 300 " + name, y0, z0, y1, z1, u, v, GEOMETRY, k)
        outlines.append(o)
        meshes.append(m)
    at, plane = nm_plane(grid, 1.2, PLANE_COL, 0)

    chains, materials = [], []
    for row, unit in enumerate(("Steel02", "Steel01", "Hysteretic")):
        steel = steel_law(grid, unit, unit, MATERIALS, row)
        materials.append(steel)
        chains.append(chain(grid, row, [(m, steel) for m in meshes], [], 0.0, 0.0, 0.05, plane, "HEA 300, " + unit))
    # The first law about the weak axis too: Direction 90.
    weak = chain(grid, 3, [(m, materials[0]) for m in meshes], [], 0.0, 90.0, 0.05, plane, "HEA 300, Steel02, weak axis")
    chains.append(weak)
    mc_at, mc_plane, diagram = views(grid, chains, 0.35, 1.2, 0.5)
    # The domain of the first law, as My against Mz at no axial force and at 1000 kN of compression.
    mymz = component(grid, MYMZ_DIAGRAM, "My-Mz Diagram", "My-Mz", NM_COL, 4)
    wire(inp(mymz, 0), out(chains[0].domain))
    number(inp(mymz, 1), 0.0, -1000.0)
    wire(inp(mymz, 2), out(plane))
    number(inp(mymz, 4), 0.5)

    finish(grid, doc, chains, [mc_at, mc_plane, at, plane, diagram, mymz], (GEOMETRY, MATERIALS, SECTION, READ),
           [("1 - Steel: Steel02, Steel01 and Hysteretic", materials, RED),
            ("2 - HEA 300 in fibres: two flanges and a web", outlines + meshes + chain_objects(chains), ORANGE),
            ("3 - Moment Curvature about both axes, and the domain", [c.curvature for c in chains] + [c.domain for c in chains], PURPLE),
            ("4 - Curves, N-M and My-Mz diagrams, ultimate moments", [c.nm for c in chains] + sum([c.panels for c in chains], []) + [mc_at, mc_plane, at, plane, diagram, mymz], BLUE)],
           [("HEA 300",
             "HEA 300 in fibres - a rolled steel section under three laws.\n\n"
             "Two flanges (300 x 14) and a web (8.5 x 262), the root radii left out, meshed in 600 fibres each way, "
             "S355. Moment Curvature bends it about the strong axis, Direction 0, without axial force, in Steel02 "
             "(smooth knee, 1 % hardening), Steel01 (sharp knee, 1 %) and a Hysteretic backbone rising to 1.15 fy; the last "
             "row bends Steel02 about the weak axis, Direction 90. The curves show the plastic reserve: the ultimate moment against the "
             "moment at first yield. Every law here hardens: a perfectly plastic one (ElasticPP) leaves a bare section with no "
             "stiffness once it is fully yielded, and the analysis cannot go on.\n\n"
             "The N-M diagrams are cut along My; the My-Mz diagram is the first law's domain at N = 0 and at 1000 kN of compression.\n\n"
             "Try: Direction between 0 and 90; Axial from 0 to -2000; the steel strength in the Steel components.",
             300, 40, 620, 290)])


# ---------------------------------------------------------------- case 3: sections that mix materials

def build_composite(doc):
    grid = Grid(doc)
    chains, materials, objs = [], [], []
    extras = []
    row = 0

    # 1 - A composite beam: HEA 240 under a 1200 x 120 slab, the slab in compression, 8 phi 12 bars in it.
    parts = i_section(0.230, 0.240, 0.0075, 0.012)
    meshes = []
    for k, (y0, z0, y1, z1, u, v, name) in enumerate(parts):
        o, m = rectangle(grid, "HEA 240 " + name, y0, z0, y1, z1, u, v, GEOMETRY, row)
        objs += [o, m]
        meshes.append(m)
    o, slab = rectangle(grid, "slab 1200 x 120", -0.6, 0.115, 0.6, 0.235, 30, 6, GEOMETRY, row)
    objs += [o, slab]
    concrete = concrete_law(grid, "Concrete02", "Slab, Concrete02", MATERIALS, row)
    steel = steel_law(grid, "Steel02", "HEA, Steel02", MATERIALS, row, gap=0)
    rebar = steel_law(grid, "ReinforcingSteel", "Slab bars", MATERIALS, row, fy=FY_REBAR, e=200000000.0)
    materials += [concrete, steel, rebar]
    ys = [-0.55 + i * 1.1 / 7.0 for i in range(8)]
    at1, pl1 = nm_plane(grid, 3.0, PLANE_COL, row)
    ch = chain(grid, row, [(m, steel) for m in meshes] + [(slab, concrete)],
               [(bar_points(ys, 0.155), [area(12)] * 8, rebar)], 0.0, 180.0, 0.05, pl1, "Composite beam")
    chains.append(ch)
    extras += [at1, pl1]
    row += 1

    # 2 - A HEA 200 encased in a 400 x 400 column: the concrete in the chambers between the flanges and all
    # round them, 4 phi 20 bars in the corners, 1500 kN of compression.
    h, b, tw, tf = 0.190, 0.200, 0.0065, 0.010
    parts = i_section(h, b, tw, tf)
    meshes = []
    for k, (y0, z0, y1, z1, u, v, name) in enumerate(parts):
        o, m = rectangle(grid, "HEA 200 " + name, y0, z0, y1, z1, u, v, GEOMETRY, row)
        objs += [o, m]
        meshes.append(m)
    web = h / 2.0 - tf
    boxes = [("cover above", -b / 2, h / 2, b / 2, 0.2), ("cover below", -b / 2, -0.2, b / 2, -h / 2),
             ("left of the flanges", -0.2, -0.2, -b / 2, 0.2), ("right of the flanges", b / 2, -0.2, 0.2, 0.2),
             ("left chamber", -b / 2, -web, -tw / 2, web), ("right chamber", tw / 2, -web, b / 2, web)]
    concrete2 = concrete_law(grid, "Concrete04", "Encasing, Concrete04", MATERIALS, row)
    steel2 = steel_law(grid, "Steel02", "HEA, Steel02", MATERIALS, row, gap=0)
    rebar2 = steel_law(grid, "ReinforcingSteel", "Corner bars", MATERIALS, row, fy=FY_REBAR, e=200000000.0)
    materials += [concrete2, steel2, rebar2]
    boxmeshes = []
    for name, y0, z0, y1, z1 in boxes:
        o, m = rectangle(grid, "encasing " + name, y0, z0, y1, z1, cells(y1 - y0), cells(z1 - z0), GEOMETRY, row)
        objs += [o, m]
        boxmeshes.append(m)
    at2, pl2 = nm_plane(grid, 3.0, PLANE_COL, row)
    corners = [(-0.16, -0.16, 0.0), (0.16, -0.16, 0.0), (-0.16, 0.16, 0.0), (0.16, 0.16, 0.0)]
    ch = chain(grid, row, [(m, steel2) for m in meshes] + [(m, concrete2) for m in boxmeshes],
               [(corners, [area(20)] * 4, rebar2)], -1500.0, 0.0, 0.05, pl2, "Encased column")
    chains.append(ch)
    extras += [at2, pl2]
    row += 1

    # 3 - A reinforced concrete beam with a carbon fibre laminate along its bottom and its top, and the same beam
    # without them.
    W, D, C = 0.3, 0.5, 0.05
    tp = 0.003
    for with_laminate in (True, False):
        label = "RC beam with CFRP" if with_laminate else "RC beam"
        o, m = rectangle(grid, label + " concrete", -W / 2, -D / 2, W / 2, D / 2, 12, 20, GEOMETRY, row)
        objs += [o, m]
        concrete3 = concrete_law(grid, "Concrete02", label + " concrete", MATERIALS, row)
        rebar3 = steel_law(grid, "ReinforcingSteel", label + " bars", MATERIALS, row, fy=FY_REBAR, e=200000000.0)
        materials += [concrete3, rebar3]
        patches = [(m, concrete3)]
        if with_laminate:
            cfrp = laminate(grid, "CFRP laminate", MATERIALS, row)
            materials.append(cfrp)
            for name, z0, z1 in (("below", -D / 2 - tp, -D / 2), ("above", D / 2, D / 2 + tp)):
                o2, m2 = rectangle(grid, "laminate " + name, -W / 2, z0, W / 2, z1, 12, 1, GEOMETRY, row)
                objs += [o2, m2]
                patches.append((m2, cfrp))
        at3, pl3 = nm_plane(grid, 3.0, PLANE_COL, row)
        ch = chain(grid, row, patches,
                   [(bar_points((-0.1, 0.0, 0.1), -D / 2 + C), [area(20)] * 3, rebar3), (bar_points((-0.1, 0.1), D / 2 - C), [area(16)] * 2, rebar3)],
                   0.0, 180.0, 0.05, pl3, label)
        chains.append(ch)
        extras += [at3, pl3]
        row += 1

    mc_at, mc_plane, diagram = views(grid, chains, 1.0, 3.0, 0.6)
    extras += [mc_at, mc_plane, diagram]

    finish(grid, doc, chains, extras, (GEOMETRY, MATERIALS, SECTION, READ),
           [("1 - Laws: concrete, structural steel, bars and a carbon fibre laminate", materials, RED),
            ("2 - Four sections in fibres: composite beam, encased column, RC beam with and without CFRP", objs + chain_objects(chains), ORANGE),
            ("3 - Moment Curvature and the domain of each", [c.curvature for c in chains] + [c.domain for c in chains], PURPLE),
            ("4 - Curves on one diagram, N-M diagrams, ultimate moments and what failed", [c.nm for c in chains] + sum([c.panels for c in chains], []) + extras, BLUE)],
           [("Composite sections",
             "Composite sections - fibres of different materials in one section.\n\n"
             "1 Composite beam: a HEA 240 in S355 under a 1200 x 120 slab of Concrete02 with 8 phi 12 bars, bent so that "
             "the slab is in compression, full interaction.\n"
             "2 Encased column: a HEA 200 in a 400 x 400 section of Concrete04, 4 phi 20 corner bars, 1500 kN of compression; "
             "concrete between the flanges and all round them, the steel's own fibres left out of it.\n"
             "3 A 300 x 500 RC beam with a 3 mm carbon fibre laminate along its bottom and its top (Other Laws, Elastic: "
             "E 160 GPa, ruptures at 1.7 %), and the same beam without them in the last row.\n\n"
             "Moment Curvature draws every curve on one diagram, with what failed first in each. The laminate stiffens "
             "and strengthens the beam, and the section ends when it ruptures or the concrete crushes.\n\n"
             "Try: the laminate's thickness, its rupture strain; the slab's width; Axial in the column.",
             300, 40, 700, 330)])


def build(doc):
    {"laws": build_laws, "hea": build_hea, "composite": build_composite}[CASE](doc)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Fibre sections built: " + CASE)


if __name__ == "__main__":
    main()
