#! python3
# Report test: a two-storey steel frame, analysed three ways - static, natural vibration and linear
# buckling - and written up as a PDF by the Report component, from the report components: a page with
# a title block, texts, tables of its own and of the results, and Report Views for the pictures, in
# order through a Merge. There is no Model View on the canvas: the Report draws its pictures itself.
#
# make.sh, in the folder above, writes ReportFrame.gh straight from this script, without Rhino:
#     ./make.sh Report/ReportFrame.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed, from the ScriptEditor in Rhino 8.
#
# The frame: 6 m along X by 4 m along Y on plan, two storeys of 3.5 m, fixed at its four feet.
# HEB200 columns, IPE300 beams along X and IPE240 along Y, S355. Every member is split in three, so
# that the modes and the buckled shapes bend along the members: 48 elements, inside the free limit.
# Loads: its own weight, 120 kN on each first-floor corner, 80 kN on each roof corner and 10 kN of
# wind along X on each roof corner, in one load pattern.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParameterSide, IGH_VariableParameterComponent
from Grasshopper.Kernel.Special import GH_Group, GH_Panel, GH_BooleanToggle
from Grasshopper.Kernel.Parameters import Param_Line, Param_Point
from Grasshopper.Kernel.Types import GH_Line, GH_Point, GH_String, GH_Integer, GH_Vector, GH_Number, GH_Curve, GH_Colour, GH_Boolean, GH_Interval
from Rhino.Geometry import Point3d, Line, Vector3d, Interval
from System.Drawing import PointF, RectangleF, Color

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION_LIBRARY = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"    # Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05"            # Support, Rigid
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
POINT_LOAD = "069017B3-4A2F-40FE-87BA-126100908BC8"         # Point Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NATURAL_VIBRATION = "3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3"  # Natural Vibration Analysis
LINEAR_BUCKLING = "6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815"    # Linear Buckling Analysis
FOOTFALL = "7E4C1A92-3D58-4B6F-A0E7-2C9F5B81D364"           # Footfall Analysis
REPORT = "4D6285D0-F64A-4CF1-A9A6-B62EDC615D7D"             # Report
REPORT_PAGE = "457F5896-898C-4A1B-A0DE-93D0AA0B202A"        # Report Page
TITLE_BLOCK = "4DE6A7F1-8FC7-41DF-8FDE-3B6584EF094E"        # Report Title Block
PAGE_BREAK = "9505C286-9EA1-4075-A640-EBE74A4F5C8A"         # Report Page Break
REPORT_TEXT = "8E5443D2-9B7D-417D-B2BF-C0CFB264150B"        # Report Text
REPORT_TABLE = "4945F608-9760-4287-93DC-DA48FFCD57B3"       # Report Table
RESULT_TABLE = "2EB069A1-3335-436E-92FD-0F86FAA75487"       # Report Result Table
REPORT_VIEW = "08439B75-6150-4771-ACC1-204708F25F17"        # Report View
MERGE = "3CADDDEF-1E2B-4C09-9390-0E8F78F7609F"              # Sets > Tree > Merge
PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script

# Report's inputs and outputs.
R_MODELS, R_PAGE, R_CONTENT, R_FILE, R_RUN = 0, 1, 2, 3, 4
R_FILE_OUT, R_LOG = 0, 1
# Report View's topics, by the names of its switcher's units.
MODEL_TOPIC, STATIC_TOPIC = "Report View Model", "Report View Static"
VIBRATION_TOPIC, STABILITY_TOPIC = "Report View Natural Vibration", "Report View Stability"
FOOTFALL_TOPIC = "Report View Footfall"

# Grasshopper's own panel yellow: a panel made in code has no colour of its own.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
GREEN = Color.FromArgb(60, 175, 201, 48)
BLUE = Color.FromArgb(50, 66, 136, 247)
YELLOW = Color.FromArgb(60, 230, 200, 40)
GREY = Color.FromArgb(40, 42, 45, 49)

# The frame.
SPAN_X, SPAN_Y, STOREY, LEVELS, PIECES = 6.0, 4.0, 3.5, 2, 3

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
    """Moves an object so that its bounds start at (left, top)."""
    a = obj.Attributes
    b = a.Bounds
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


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


def text(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_String(value))
    return param


def integer(param, value):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Integer(int(value)))
    return param


def vector(param, x, y, z):
    param.PersistentData.Clear()
    param.PersistentData.Append(GH_Vector(Vector3d(x, y, z)))
    return param


FLAGS = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public


def member(c, name):
    """A field of a component, its own or a base class's, found by its name."""
    c = getattr(c, "__implementation__", c)
    t = c.GetType()
    while t is not None:
        f = t.GetField(name, FLAGS)
        if f is not None:
            return f, c
        t = t.BaseType
    raise Exception("no field " + name)


def widget(c, name):
    f, obj = member(c, name)
    return f.GetValue(obj)


def set_member(c, name, value):
    f, obj = member(c, name)
    f.SetValue(obj, value)


def pick(c, name, item):
    """Picks an item of one of a component's dropdowns by its name, as a click would."""
    dropdown = widget(c, name)
    names = [e.name for e in dropdown.Items]
    dropdown.Value = names.index(item)


def show_result(c, show, component_):
    """Ticks one of Model View's results alone, as clicks in its Results menu do."""
    c = getattr(c, "__implementation__", c)
    t = c.GetType()
    while t.GetMethod("ShowResult", FLAGS) is None:
        t = t.BaseType
    t.GetMethod("ShowResult", FLAGS).Invoke(c, System.Array[System.Object]([show, component_]))


def fold(c):
    """Folds a component's menus away, as clicks on their headers would: a Report View with its
    Results open is as tall as a column of the canvas."""
    attributes = getattr(c, "__implementation__", c).Attributes
    collection = attributes.GetType().GetField("_collection", FLAGS).GetValue(attributes)
    for menu in collection.Menus:
        menu.Collapse()
    attributes.ExpireLayout()
    attributes.PerformLayout()


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


def named(c, name):
    """A component's input by its name."""
    for param in getattr(c, "__implementation__", c).Params.Input:
        if param.Name == name:
            return param
    raise Exception("no input " + name)


def output(c, name):
    """A component's output by its name."""
    for param in getattr(c, "__implementation__", c).Params.Output:
        if param.Name == name:
            return param
    raise Exception("no output " + name)


# The title block, as an office would keep it: drawn as it is on an A4 page, the page's bottom left
# corner at the world origin, in mm - its rules as lines and its texts as Rhino text objects, which
# carry their place, height and justification. Scaled to mm whatever the Rhino file's units are.
TITLE_BLOCK_CODE = """import Rhino
import Rhino.Geometry as rg
from Rhino.DocObjects import TextHorizontalAlignment as H, TextVerticalAlignment as V

doc = Rhino.RhinoDoc.ActiveDoc
k = Rhino.RhinoMath.UnitScale(Rhino.UnitSystem.Millimeters, doc.ModelUnitSystem) if doc else 1.0

def at(x, y):
    return rg.Point3d(x * k, y * k, 0.0)

Curves = [rg.LineCurve(at(20, 282), at(190, 282)), rg.LineCurve(at(20, 17), at(190, 17))]
Text = []
for words, x, y, height, align, bold in [
        ("{title}", 20, 285, 3.6, H.Left, True),
        ("Report component test", 190, 285, 3.0, H.Right, False),
        ("Alpaca4d - {date}", 20, 10, 2.8, H.Left, False),
        ("Alpaca4d", 105, 10, 2.8, H.Center, True),
        ("Page {page} of {pages}", 190, 10, 2.8, H.Right, False)]:
    text = rg.TextEntity.Create(words, rg.Plane(at(x, y), rg.Vector3d.ZAxis), doc.DimStyles.Current, False, 0.0, 0.0)
    text.TextHeight = height * k
    text.TextHorizontalAlignment = align
    text.TextVerticalAlignment = V.Bottom
    try:
        text.SetBold(bold)
    except Exception:
        pass
    Text.append(text)
"""


def script(doc, nickname, code, outputs, x, y):
    """A GhPython component with no inputs, running code that sets each of outputs."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, name in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    c.Code = code
    return put(doc, c, x, y)


def topic_of(view, topic):
    """One of a Report View's topics, by its name."""
    for t in widget(view, "_topics"):
        t = getattr(t, "__implementation__", t)
        if t.name() == topic:
            return t
    raise Exception("no topic " + topic)


def results_menu(view, topic):
    """The Results menu of one of a Report View's topics: Model View's results, ticked as in Model View."""
    t = topic_of(view, topic)
    return t.GetType().GetField("_results", FLAGS).GetValue(t)


def with_tables(view, topic, *names):
    """Ticks tables in the Results panel of one of a Report View's topics - the model, the modes,
    footfall - by their names in the catalogue, as clicks do. They are written after the picture."""
    t = topic_of(view, topic)
    for name in names:
        for entry in widget(t, "_tables"):
            if entry.Item1 == name:
                tick = entry.Item2
                tick.GetType().GetProperty("Item").SetValue(tick, True, System.Array[System.Object]([0]))
                break
        else:
            raise Exception("no table " + name)
    t.GetType().GetMethod("UpdateTables", FLAGS).Invoke(t, None)


def set_box(group, on, place=0):
    """Ticks or unticks a box of one of the menus' groups of boxes, by its place in the group from 0."""
    group.GetType().GetProperty("Item").SetValue(group, on, System.Array[System.Object]([place]))


def with_table(menu, place):
    """Ticks the Table under a result of a Results menu, by its place in Model View's menu from 0:
    the table of what the picture draws of it, written after the picture."""
    set_box(menu_widget(menu, "Tables")[place], True)


def menu_widget(menu, name):
    return menu.GetType().GetField(name, FLAGS).GetValue(menu)


def tick(menu, place, on, component=None):
    """Ticks a result of a Results menu on or off, by its place in Model View's menu from 0, and picks
    its component - through reflection, as the menu's classes are the plugin's own."""
    result = menu_widget(menu, "Results")[place]
    set_box(result.GetType().GetField("Tick", FLAGS).GetValue(result), on)
    if component is not None:
        result.GetType().GetMethod("Pick", FLAGS).Invoke(result, System.Array[System.Object]([component]))


def also(menu, place, component):
    """Ticks one more component of a result of a Results menu, by their places from 0: a picture more."""
    result = menu_widget(menu, "Results")[place]
    set_box(result.GetType().GetField("Components", FLAGS).GetValue(result), True, component)


def untick(table, name, option):
    """Leaves one of what a Report Result Table can show out of it, as a click on its checkbox does."""
    for item in widget(table, "_columns")[name]:
        if item.Item1 == option:
            item.Item2.Active = False


def values(param, *items):
    """Types values into an unwired input: text, numbers, points, curves or colours."""
    param.PersistentData.Clear()
    for v in items:
        if isinstance(v, str):
            param.PersistentData.Append(GH_String(v))
        elif isinstance(v, bool):
            param.PersistentData.Append(GH_Boolean(v))
        elif isinstance(v, (int, float)):
            param.PersistentData.Append(GH_Number(float(v)))
        elif isinstance(v, Point3d):
            param.PersistentData.Append(GH_Point(v))
        elif isinstance(v, Color):
            param.PersistentData.Append(GH_Colour(v))
        else:
            param.PersistentData.Append(GH_Curve(v))
    return param


def group(doc, name, objects, colour):
    g = GH_Group()
    g.CreateAttributes()
    g.NickName = name
    g.Colour = colour
    for o in objects:
        g.AddObject(o.InstanceGuid)
    doc.AddObject(g, False)
    return g


# ---------------------------------------------------------------- the frame

def split(a, b, n=PIECES):
    """The member from a to b, in n pieces."""
    pts = [tuple(a[k] + (b[k] - a[k]) * i / float(n) for k in range(3)) for i in range(n + 1)]
    return [(pts[i], pts[i + 1]) for i in range(n)]


def geometry():
    corners = [(0.0, 0.0), (SPAN_X, 0.0), (SPAN_X, SPAN_Y), (0.0, SPAN_Y)]
    columns, along_x, along_y = [], [], []
    for x, y in corners:
        for level in range(LEVELS):
            columns += split((x, y, level * STOREY), (x, y, (level + 1) * STOREY))
    for level in range(1, LEVELS + 1):
        z = level * STOREY
        for y in (0.0, SPAN_Y):
            along_x += split((0.0, y, z), (SPAN_X, y, z))
        for x in (0.0, SPAN_X):
            along_y += split((x, 0.0, z), (x, SPAN_Y, z))
    feet = [(x, y, 0.0) for x, y in corners]
    floor = [(x, y, STOREY) for x, y in corners]
    roof = [(x, y, LEVELS * STOREY) for x, y in corners]
    return columns, along_x, along_y, feet, floor, roof


# Where each part goes on the canvas, in canvas units. Placed by hand rather than measured: a
# definition written outside Rhino measures its components smaller than Grasshopper draws them, and
# the switchers' menus taller still.
COL = {"inputs": 20, "materials": 130, "elements": 360, "patterns": 560, "model": 790,
       "analyses": 1030, "template": 1400, "report": 1880}
ROW = {"columns": 20, "beams x": 220, "beams y": 420, "material": 620, "supports": 850, "loads": 1110}


def put(doc, obj, x, y):
    obj.Attributes.ExpireLayout()
    obj.Attributes.PerformLayout()
    doc.AddObject(obj, False)
    move(obj, x, y)
    return obj


def lines_at(doc, name, segments, x, y):
    p = Param_Line()
    p.CreateAttributes()
    p.NickName = name
    for a, b in segments:
        p.PersistentData.Append(GH_Line(Line(Point3d(*a), Point3d(*b))))
    return put(doc, p, x, y)


def points_at(doc, name, where, x, y):
    p = Param_Point()
    p.CreateAttributes()
    p.NickName = name
    for xyz in where:
        p.PersistentData.Append(GH_Point(Point3d(*xyz)))
    return put(doc, p, x, y)


def at(doc, guid, what, nickname, x, y):
    c = emit(guid, what)
    if nickname:
        c.NickName = nickname
    return put(doc, c, x, y)


def panel_at(doc, name, source, width, height, x, y, value=None):
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    if value is not None:
        p.Properties.Colour = PANEL
        p.Properties.Multiline = True
        p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    if source is not None:
        p.AddSource(source)
    return put(doc, p, x, y)


def build(doc):
    columns, along_x, along_y, feet, floor, roof = geometry()

    # 1 - Geometry: every member in three pieces, typed in.
    column_lines = lines_at(doc, "Columns", columns, COL["inputs"], ROW["columns"] + 60)
    x_lines = lines_at(doc, "Beams X", along_x, COL["inputs"], ROW["beams x"] + 60)
    y_lines = lines_at(doc, "Beams Y", along_y, COL["inputs"], ROW["beams y"] + 60)

    # 2 - Members: S355, HEB200 columns, IPE300 along X, IPE240 along Y.
    steel = at(doc, MATERIAL, "Material Library Elastic", "S355", COL["materials"], ROW["material"])
    text(inp(steel, 0), "S355")
    members, sections = [], []
    for name, profile, curves, row in (("Columns", "HEB200", column_lines, "columns"),
                                       ("Beams X", "IPE300", x_lines, "beams x"),
                                       ("Beams Y", "IPE240", y_lines, "beams y")):
        section = at(doc, SECTION_LIBRARY, "Section Library", profile, COL["materials"], ROW[row])
        text(inp(section, 0), profile)
        wire(inp(section, 1), out(steel))
        beam = at(doc, BEAM, "ForceBeamColumn", name, COL["elements"], ROW[row])
        wire(inp(beam, 0), curves)
        wire(inp(beam, 1), out(section))
        members.append(beam)
        sections.append(section)

    # 3 - Supports: fixed at the four feet.
    foot_points = points_at(doc, "Feet", feet, COL["inputs"], ROW["supports"] + 20)
    fixed = at(doc, SUPPORT, "Support", "Fixed feet", COL["elements"], ROW["supports"])
    wire(inp(fixed, 0), foot_points)

    # 4 - Loads: own weight, the floors on the corners, and wind along X on the roof.
    y = ROW["loads"]
    weight = at(doc, GRAVITY, "Gravity Load", "Own weight", COL["elements"], y)
    floor_points = points_at(doc, "First floor corners", floor, COL["inputs"], y + 110)
    roof_points = points_at(doc, "Roof corners", roof, COL["inputs"], y + 230)
    floor_load = at(doc, POINT_LOAD, "Point Load", "First floor", COL["elements"], y + 90)
    wire(inp(floor_load, 0), floor_points)
    vector(inp(floor_load, 1), 0.0, 0.0, -120.0)
    roof_load = at(doc, POINT_LOAD, "Point Load", "Roof", COL["elements"], y + 200)
    wire(inp(roof_load, 0), roof_points)
    vector(inp(roof_load, 1), 0.0, 0.0, -80.0)
    wind = at(doc, POINT_LOAD, "Point Load", "Wind", COL["elements"], y + 310)
    wire(inp(wind, 0), roof_points)
    vector(inp(wind, 1), 10.0, 0.0, 0.0)
    pattern = at(doc, LOAD_PATTERN, "Load Pattern", None, COL["patterns"], y + 150)
    wire(flat(inp(pattern, 1)), out(weight), out(floor_load), out(roof_load), out(wind))

    # 5 - Assemble.
    assemble = at(doc, ASSEMBLE, "Assemble Model", None, COL["model"], ROW["beams y"])
    wire(flat(inp(assemble, 0)), *[out(m) for m in members])
    wire(flat(inp(assemble, 1)), out(fixed))
    wire(flat(inp(assemble, 2)), out(pattern))

    # 6 - The three analyses.
    settings = at(doc, ANALYSIS_SETTINGS, "Analysis Settings", None, COL["model"], ROW["columns"])
    run = at(doc, RUN_ANALYSIS, "Run Analysis", None, COL["analyses"], ROW["columns"] + 60)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    vibration = at(doc, NATURAL_VIBRATION, "Natural Vibration Analysis", None, COL["analyses"], ROW["beams x"] + 60)
    wire(inp(vibration, 0), out(assemble))
    integer(inp(vibration, 1), 6)
    buckling = at(doc, LINEAR_BUCKLING, "Linear Buckling Analysis", None, COL["analyses"], ROW["beams y"] + 120)
    wire(inp(buckling, 0), out(assemble))
    integer(inp(buckling, 1), 4)
    floors = sorted(set(p_ for a, b in along_x + along_y for p_ in (a, b)))
    walk = points_at(doc, "Floor nodes", floors, COL["model"], ROW["beams y"] + 330)
    footfall = at(doc, FOOTFALL, "Footfall Analysis", None, COL["analyses"], ROW["beams y"] + 260)
    wire(inp(footfall, 0), out(vibration))
    wire(inp(footfall, 1), walk)

    # 7 - The page: A4 with a title block of its own - a rule under the title and the project at the
    # top, one over the author, the date and the page number at the foot - drawn by a script.
    x0, y0 = 1400.0, 20.0
    drawing = script(doc, "Title block drawing", TITLE_BLOCK_CODE, ["Curves", "Text"], x0, y0)
    title_block = at(doc, TITLE_BLOCK, "Report Title Block", None, x0 + 170.0, y0)
    wire(inp(title_block, 0), out(drawing, 1))
    wire(inp(title_block, 1), out(drawing, 2))
    page = at(doc, REPORT_PAGE, "Report Page", None, x0 + 420.0, y0)
    wire(inp(page, 6), out(title_block))

    # 8 - The content, a part after another: texts, tables and pictures, chapter by chapter.
    parts = []

    def text_part(style, x, y, *lines):
        t = at(doc, REPORT_TEXT, "Report Text", None, x, y)
        pick(t, "_ddStyle", style)
        values(inp(t, 0), *lines)
        fold(t)
        parts.append(t)
        return t

    def result_table(name, x, y, modes=None):
        t = at(doc, RESULT_TABLE, "Report Result Table", name, x, y)
        pick(t, "_ddTable", name)
        if modes:
            values(inp(t, 0), modes)
        fold(t)
        parts.append(t)
        return t

    def report_view(name, topic, x, y, **settings):
        """A Report View on a topic, its inputs typed in by name: View, Caption, Modes, Scale..."""
        v = at(doc, REPORT_VIEW, "Report View", name, x, y)
        # Switched even to Static analysis, the topic it starts on, through another: a switcher measured
        # outside Rhino before it has switched once is placed far off where it is put.
        if topic == STATIC_TOPIC:
            switch_unit(v, MODEL_TOPIC)
        switch_unit(v, topic)
        for key, value in settings.items():
            if isinstance(value, tuple) and value[0] == "interval":
                param = named(v, key)
                param.PersistentData.Clear()
                param.PersistentData.Append(GH_Interval(Interval(value[1], value[2])))
            elif isinstance(value, (list, tuple)):
                values(named(v, key.replace("_", " ")), *value)
            elif isinstance(value, int) and not isinstance(value, bool):
                integer(named(v, key.replace("_", " ")), value)
            else:
                values(named(v, key.replace("_", " ")), value)
        move(v, x, y)
        parts.append(v)
        return v

    c1, c2, c3, c4 = x0 + 680.0, x0 + 1000.0, x0 + 1320.0, x0 + 1640.0
    title = text_part("Title", c1, y0, "Two-storey steel frame", "Static, natural vibration and linear buckling analysis")
    # The headings are typed without numbers: the report numbers them as they come, 1, 1.1, 1.1.1.
    text_part("Heading", c1, y0 + 80, "The model")
    text_part("Paragraph", c1, y0 + 160,
              "A steel frame of two storeys, 6 m by 4 m on plan and 3.5 m a storey, fixed at its four feet. The columns are HEB200 "
              "and the beams IPE300 along X and IPE240 along Y, all S355. It has {model.nodes} nodes and {model.elements} elements, "
              "and weighs {model.mass:N0} kg.")
    text_part("Subheading", c1, y0 + 240, "Members")
    result_table("Model summary", c1, y0 + 300)
    section_table = result_table("Beam sections", c1, y0 + 400)
    untick(section_table, "model.sections", "Length")
    text_part("Subheading", c1, y0 + 500, "Loads")
    loads = at(doc, REPORT_TABLE, "Report Table", "Loads", c1, y0 + 560)
    values(inp(loads, 0), "The loads, in one load pattern")
    values(inp(loads, 1), "Load", "On", "Value")
    values(inp(loads, 2), "", "", "kN")
    integer(inp(loads, 3), 0)
    values(inp(loads, 5), "Own weight", "First floor", "Roof", "Wind")
    values(inp(loads, 6), "every member", "each corner, down", "each corner, down", "each roof corner, along X")
    values(inp(loads, 7), "-", 120, 80, 10)
    parts.append(loads)
    report_view("The frame", MODEL_TOPIC, c1, y0 + 780, Caption="The frame, coloured by section, with its supports and loads")

    text_part("Heading", c2, y0, "Static analysis")
    text_part("Paragraph", c2, y0 + 80,
              "Under its own weight, the floor loads and the wind, the frame moves {static.maxDisplacement:F1} mm at most, at node {static.maxNode}.")
    text_part("Subheading", c2, y0 + 160, "Displacement")
    # The tables of what a picture draws come with it, ticked under it in the Results menu: here after it.
    displaced = report_view("Displacement", STATIC_TOPIC, c2, y0 + 240, Caption="Displacement, drawn 50 times larger")
    menu_widget(results_menu(displaced, STATIC_TOPIC), "_slScale").Value = 50.0
    with_table(results_menu(displaced, STATIC_TOPIC), 0)   # the displacement drawn, as a table
    text_part("Subheading", c2, y0 + 720, "Forces")
    # A picture for each component ticked, each captioned with what it draws and followed by its table:
    # the axial force and the bending moment in the members...
    text_part("Sub-subheading", c2, y0 + 800, "In the members")
    moment = report_view("Member forces", STATIC_TOPIC, c2, y0 + 880, View="Front")
    menu = results_menu(moment, STATIC_TOPIC)
    tick(menu, 0, False)                     # Displacement off
    tick(menu, 1, True, 5)                   # Beam forces: Mz...
    also(menu, 1, 0)                         # ...and N, a picture each
    with_table(menu, 1)
    set_box(menu_widget(menu, "_ckDeformed"), False)
    # ...and the reactions at the supports.
    text_part("Sub-subheading", c2, y0 + 1440, "At the supports")
    held = report_view("Reactions", STATIC_TOPIC, c2, y0 + 1520, View="Front")
    menu = results_menu(held, STATIC_TOPIC)
    tick(menu, 0, False)                     # Displacement off
    tick(menu, 6, True, 0)                   # Reactions, the force
    with_table(menu, 6)
    set_box(menu_widget(menu, "_ckDeformed"), False)
    breaker = at(doc, PAGE_BREAK, "Report Page Break", None, c2, y0 + 2100)
    parts.append(breaker)

    text_part("Heading", c3, y0, "Natural vibration")
    text_part("Paragraph", c3, y0 + 80, "The first mode sways at {vibration.frequency[1]:F2} Hz. Their frequencies follow the pictures.")
    modes = report_view("Modes", VIBRATION_TOPIC, c3, y0 + 160, Modes="6",
                        Caption="Mode {mode}: f = {frequency:F2} Hz, T = {period:F3} s")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")

    text_part("Heading", c4, y0, "Linear buckling")
    text_part("Paragraph", c4, y0 + 80, "The frame buckles first at {buckling.factor[1]:F2} times its loads.")
    buckled = report_view("Buckled shapes", STABILITY_TOPIC, c4, y0 + 160, Modes="4")
    with_tables(buckled, STABILITY_TOPIC, "buckling.factors")

    c5 = x0 + 1960.0
    text_part("Heading", c5, y0, "Footfall")
    text_part("Paragraph", c5, y0 + 80,
              "A person walking on the floors, after SCI P354, makes them respond up to R = {footfall.maxFactor:F1}, at node {footfall.maxNode}. "
              "The picture's top colour is 8, the limit for a general office.")
    walked = report_view("Footfall", FOOTFALL_TOPIC, c5, y0 + 160, Range=("interval", 0.0, 8.0))
    with_tables(walked, FOOTFALL_TOPIC, "footfall.response")

    # 9 - The report: the parts in order through a Merge, the page, and a toggle to write it. Open
    # report, the bar under the Report, opens what it wrote.
    merge = at(doc, MERGE, "Merge", "Content", x0 + 2280.0, y0)
    while merge.Params.Input.Count < len(parts):
        index = merge.Params.Input.Count
        merge.Params.RegisterInputParam(IGH_VariableParameterComponent.CreateParameter(merge, GH_ParameterSide.Input, index), index)
    IGH_VariableParameterComponent.VariableParameterMaintenance(merge)
    merge.Params.OnParametersChanged()
    for k, part in enumerate(parts):
        wire(inp(merge, k), out(part, 0))
    move(merge, x0 + 2280.0, y0)
    go = GH_BooleanToggle()
    go.CreateAttributes()
    go.NickName = "Write the report"
    go.Value = False
    put(doc, go, x0 + 2440.0, y0 + 180)
    report = at(doc, REPORT, "Report", None, x0 + 2440.0, y0)
    # The models the report is of: the views and the tables pick theirs by topic.
    wire(flat(inp(report, R_MODELS)), out(assemble), out(run), out(vibration), out(buckling), output(footfall, "AlpacaModel"))
    wire(inp(report, R_PAGE), out(page))
    wire(inp(report, R_CONTENT), out(merge))
    wire(inp(report, R_RUN), go)
    written = panel_at(doc, "PDF", out(report, R_FILE_OUT), 360, 50, x0 + 2440.0, y0 + 240)
    log = panel_at(doc, "Log", out(report, R_LOG), 360, 160, x0 + 2440.0, y0 + 320)

    group(doc, "1 - Geometry", [column_lines, x_lines, y_lines], RED)
    group(doc, "2 - Members", [steel] + sections + members, ORANGE)
    group(doc, "3 - Supports", [foot_points, fixed], GREEN)
    group(doc, "4 - Loads", [floor_points, roof_points, weight, floor_load, roof_load, wind, pattern], BLUE)
    group(doc, "5 - Assemble", [assemble, settings], YELLOW)
    group(doc, "6 - Analyses", [run, vibration, buckling, walk, footfall], PURPLE)
    group(doc, "7 - Page", [drawing, title_block, page], GREY)
    group(doc, "8 - Content", parts, GREY)
    group(doc, "9 - Report", [merge, go, report, written, log], GREY)

    # Nothing draws in Rhino: the report draws its own pictures.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden"):
            obj.Hidden = True


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("The frame built. Set 'Write the report' to True to write the PDF beside the file, then click Open report under the Report.")


if __name__ == "__main__":
    main()
