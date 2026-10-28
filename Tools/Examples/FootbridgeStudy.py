#! python3
# The footbridge design study: the Explorer example of the docs, built on the Grasshopper canvas.
#
# make.sh, next to it, writes FootbridgeStudy.gh straight from this script, without Rhino. It also
# runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
# It builds a Grasshopper document holding a steel truss footbridge, analysed with Alpaca4d,
# and the Explorer components set up to study it: 4 spans x 3 depths x 3 panel counts x 3 chord
# sections, 108 designs, each recorded with its mass, largest displacement, highest utilisation
# and first natural frequency. A panel on the canvas says how to run the study. Save the
# document to keep it.
#
# The bridge is two Warren trusses side by side, joined by cross beams at every node: native
# Grasshopper components draw the lines, so every step of the geometry can be followed on the
# canvas. Members are steel S355 from the section library; the bridge is pinned at one end and
# free to slide along its length at the other, and carries its own weight and a deck load.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_DataMapping, GH_ParameterSide, GH_ParamWireDisplay
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode, GH_BooleanToggle)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

# Grasshopper
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"         # Maths > Script > Expression
RANGE = "9445CA40-CC73-4861-A455-146308676855"              # Sets > Sequence > Range
SERIES = "E64C5FB1-845C-4ab1-8911-5F338516BA67"             # Sets > Sequence > Series
WEAVE = "50FACCBD-9C92-4175-A5FA-D65E36013DB6"              # Sets > List > Weave
CONSTRUCT_POINT = "3581F42A-9592-4549-BD6B-1C0FC39D067B"    # Vector > Point > Construct Point
POLYLINE = "71B5B089-500A-4EA6-81C5-2F960441A0E8"           # Curve > Spline > PolyLine
EXPLODE = "AFB96615-C59A-45C9-9CAC-E27ACB1C7CA0"            # Curve > Util > Explode
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"               # Curve > Primitive > Line
END_POINTS = "11BBD48B-BB0A-4F1B-8167-FA297590390D"         # Curve > Analysis > End Points
MOVE = "E9EB1DCF-92F6-4D4D-84AE-96222D60F56B"               # Transform > Euclidean > Move
UNIT_Y = "D3D195EA-2D59-4FFA-90B1-8B7FF3369F69"             # Vector > Vector > Unit Y
UNIT_Z = "9103C240-A6A9-4223-9B42-DBD19BF38E2B"             # Vector > Vector > Unit Z
VECTOR_LENGTH = "675E31BF-1775-48D7-BB8D-76B77786DD53"      # Vector > Vector > Vector Length
BOUNDS = "F44B92B0-3B5B-493A-86F4-FD7408C3DAF3"             # Maths > Domain > Bounds
DECONSTRUCT_DOMAIN = "825EA536-AEBB-41E9-AF32-8BAEB2ECB590" # Maths > Domain > Deconstruct Domain

# Alpaca4d
MATERIAL = "F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6"           # Material Library Elastic
SECTION = "0F82C248-4B58-4C25-9F85-6B7F7C4696C0"            # Steel Section Library
BEAM = "A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"               # ForceBeamColumn
SUPPORT = "08A676AD-7332-4D86-8206-DABD66A17357"            # Support
GRAVITY = "4B6FA7FD-9388-4699-B374-B94F94583A53"            # Gravity Load
LINEAR_LOAD = "8593AC69-D975-4566-97FC-27D7D4A4D415"        # Linear Load
LOAD_PATTERN = "C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B"       # Load Pattern
ASSEMBLE = "B6978C42-7B88-4F1A-A5DF-85EF89F154F9"           # Assemble Model
ANALYSIS_SETTINGS = "9AE62757-69CC-4023-92EB-E8E1AA164AD0"  # Analysis Settings
RUN_ANALYSIS = "8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"       # Run Analysis
NATURAL_VIBRATION = "3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3"  # Natural Vibration Analysis
NODAL_DISPLACEMENTS = "25085EAB-6C66-487E-B83B-BAB5AB02B1BB" # Nodal Displacements
UTILISATION = "4F8A2D6B-91C3-4E57-B0A9-3C6E8D1F2A74"        # Utilisation
MODEL_VIEW = "5EDB6364-D458-43D6-81E9-324DECA1FEA6"         # Model View
EXPLORER_INPUTS = "1BB04F93-CCA6-44C5-9076-1E3B20918EC8"    # Explorer Inputs
EXPLORER_OUTPUTS = "E48E97B1-621B-44C9-8685-4C0D23EB96DF"   # Explorer Outputs
EXPLORER_RECORD = "CAC9F5C3-18E1-4C99-91EE-94640F700825"    # Explorer Record

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
    """Lays objects out in columns, left to right in the order the data flows.

    An object either goes in a row, or, given no row, is stacked from the top of its column.
    Rows run across every column, one per part of the bridge, so that a part's chain of
    components sits on one line and its wires run straight. Column widths and row heights are
    the objects' own, measured once Grasshopper has laid them out, so long input and output
    names never run into the next column. A column listed in stages starts a new group, and
    leaves room for the group boxes either side of it."""

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


def faint(param):
    """Draws the wires into param faintly: for the long ones that cross the canvas."""
    param.WireDisplay = GH_ParamWireDisplay.faint
    return param


def slider(grid, name, lo, hi, value, decimals, col, row=None, gap=0, accuracy=None):
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    if accuracy is None:
        accuracy = GH_SliderAccuracy.Integer if decimals == 0 else GH_SliderAccuracy.Float
    s.Slider.Type = accuracy
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


def toggle(grid, name, value, col, row=None, gap=0):
    t = GH_BooleanToggle()
    t.CreateAttributes()
    t.NickName = name
    t.Value = value
    return grid.put(t, col, row, gap)


def text(grid, name, value, col, row=None, gap=0):
    """A one-line panel holding a text, such as a section name."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, 90.0, 20.0)
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


def wire_next(c, source, name=None):
    """Wires into the free input a growing component keeps last - Explorer Inputs and Outputs -
    and gives it a name. The component adds the next free input itself when one is wired; when
    the script's wiring does not tell it, the input is added as the canvas would add it."""
    last = c.Params.Input[c.Params.Input.Count - 1]
    if last.SourceCount > 0:
        index = c.Params.Input.Count
        c.Params.RegisterInputParam(c.CreateParameter(GH_ParameterSide.Input, index), index)
        c.VariableParameterMaintenance()
        c.Params.OnParametersChanged()
        last = c.Params.Input[index]
    last.AddSource(source)
    if name:
        last.NickName = name
    return last


def note(doc, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = "How to run the study"
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


# ---------------------------------------------------------------- the footbridge

# The bridge and its members, as columns in the order the data flows...
INPUTS, NUMBERS, COORDINATES, NODES, POLYLINES, MOVED, SEGMENTS, NAMES, SECTIONS, BEAMS, LOADS, PATTERN = range(12)
# ...and as rows, one per part of the bridge, so each part's chain of components sits on a line.
BOTTOM, TOP, DIAGONALS, FLOOR, TIES, BEARINGS, LOADING = range(7)

# The analysis, the results and the record, in a grid of their own to the right.
MODEL, VIEWS, RESULTS, STEP1, STEP2, STEP3, STEP4, OUTPUTS, RECORD = range(9)
ASSEMBLED, STATIC, CHECKED = range(3)


def build(doc):
    bridge = Grid(doc)

    # 1 - What changes from design to design, and what does not.
    span = value_list(bridge, "Span [m]", [("20", "20"), ("25", "25"), ("30", "30"), ("35", "35")], 2, INPUTS)
    depth = value_list(bridge, "Depth [m]", [("1.5", "1.5"), ("2.25", "2.25"), ("3", "3")], 1, INPUTS)
    panels = slider(bridge, "Panels", 6, 10, 8, 0, INPUTS, accuracy=GH_SliderAccuracy.Even)
    chord = value_list(bridge, "Chord", [("HEA160", '"HEA160"'), ("HEA200", '"HEA200"'), ("HEA240", '"HEA240"')], 1, INPUTS)
    width = slider(bridge, "Width [m]", 2, 4, 2.5, 1, INPUTS, gap=50)
    deck = slider(bridge, "Deck load [kN/m]", -10, 0, -6.25, 2, INPUTS)

    # 2 - The trusses: bottom nodes every panel, top nodes half a panel in, the diagonals
    # zig-zagging between them. The second truss is the first moved across by the width.
    length = expression(bridge, "f(s,n)=s/n", [span, panels], "Panel length", NUMBERS, BOTTOM)
    half = expression(bridge, "f(d)=d/2", [out(length)], "Half a panel", NUMBERS, TOP)
    xb = component(bridge, RANGE, "Range", "Bottom x", COORDINATES, BOTTOM)
    wire(inp(xb, 0), span)
    wire(inp(xb, 1), panels)
    xt = component(bridge, SERIES, "Series", "Top x", COORDINATES, TOP)
    wire(inp(xt, 0), out(half))
    wire(inp(xt, 1), out(length))
    wire(inp(xt, 2), panels)

    pb = component(bridge, CONSTRUCT_POINT, "Construct Point", "Bottom nodes", NODES, BOTTOM)
    wire(inp(pb, 0), out(xb))
    pt = component(bridge, CONSTRUCT_POINT, "Construct Point", "Top nodes", NODES, TOP)
    wire(inp(pt, 0), out(xt))
    wire(inp(pt, 2), depth)
    zig = component(bridge, WEAVE, "Weave", "Zig-zag", NODES, DIAGONALS)
    wire(inp(zig, 1), out(pb))
    wire(inp(zig, 2), out(pt))
    across = component(bridge, UNIT_Y, "Unit Y", "Across", NODES, FLOOR)
    wire(inp(across, 0), width)

    pl_bottom = component(bridge, POLYLINE, "PolyLine", "Bottom chord", POLYLINES, BOTTOM)
    wire(inp(pl_bottom, 0), out(pb))
    pl_top = component(bridge, POLYLINE, "PolyLine", "Top chord", POLYLINES, TOP)
    wire(inp(pl_top, 0), out(pt))
    pl_diag = component(bridge, POLYLINE, "PolyLine", "Diagonals", POLYLINES, DIAGONALS)
    wire(inp(pl_diag, 0), out(zig))
    mv_pb = component(bridge, MOVE, "Move", "Bottom nodes B", POLYLINES, FLOOR)
    mv_pt = component(bridge, MOVE, "Move", "Top nodes B", POLYLINES, TIES)

    mv_bottom = component(bridge, MOVE, "Move", "Bottom chord B", MOVED, BOTTOM)
    mv_top = component(bridge, MOVE, "Move", "Top chord B", MOVED, TOP)
    mv_diag = component(bridge, MOVE, "Move", "Diagonals B", MOVED, DIAGONALS)
    for moved, source in ((mv_bottom, pl_bottom), (mv_top, pl_top), (mv_diag, pl_diag), (mv_pb, pb), (mv_pt, pt)):
        wire(inp(moved, 0), out(source))
        wire(inp(moved, 1), out(across))
    cross_bottom = component(bridge, LINE, "Line", "Floor beams", MOVED, FLOOR)
    wire(inp(cross_bottom, 0), out(pb))
    wire(inp(cross_bottom, 1), out(mv_pb))
    cross_top = component(bridge, LINE, "Line", "Top ties", MOVED, TIES)
    wire(inp(cross_top, 0), out(pt))
    wire(inp(cross_top, 1), out(mv_pt))

    ex_bottom = component(bridge, EXPLODE, "Explode", "Bottom chords", SEGMENTS, BOTTOM)
    ex_top = component(bridge, EXPLODE, "Explode", "Top chords", SEGMENTS, TOP)
    ex_diag = component(bridge, EXPLODE, "Explode", "Diagonals", SEGMENTS, DIAGONALS)
    for exploded, a, b in ((ex_bottom, pl_bottom, mv_bottom), (ex_top, pl_top, mv_top), (ex_diag, pl_diag, mv_diag)):
        wire(inp(exploded, 0), out(a), out(b))
    ends = component(bridge, END_POINTS, "End Points", "Bearings", SEGMENTS, BEARINGS)
    wire(inp(ends, 0), out(pl_bottom), out(mv_bottom))

    # 3 - Members: steel S355, the chords from the Chord input, the rest fixed.
    steel = text(bridge, "Grade", "S355", NAMES, BOTTOM)
    material = component(bridge, MATERIAL, "Material Library Elastic", None, NAMES, BOTTOM)
    wire(inp(material, 0), steel)
    diag_name = text(bridge, "Diagonals", "HEA120", NAMES, DIAGONALS)
    cross_name = text(bridge, "Cross beams", "IPE180", NAMES, FLOOR)

    chord_section = component(bridge, SECTION, "Steel Section Library", "Chord", SECTIONS, BOTTOM)
    faint(wire(inp(chord_section, 0), chord))
    diag_section = component(bridge, SECTION, "Steel Section Library", "Diagonal", SECTIONS, DIAGONALS)
    wire(inp(diag_section, 0), diag_name)
    cross_section = component(bridge, SECTION, "Steel Section Library", "Cross beam", SECTIONS, FLOOR)
    wire(inp(cross_section, 0), cross_name)
    for section in (chord_section, diag_section, cross_section):
        wire(inp(section, 1), out(material))

    beams = []
    for lines, section, name, row in (([out(ex_bottom)], chord_section, "Bottom chords", BOTTOM),
                                      ([out(ex_top)], chord_section, "Top chords", TOP),
                                      ([out(ex_diag)], diag_section, "Diagonals", DIAGONALS),
                                      ([out(cross_bottom), out(cross_top)], cross_section, "Cross beams", FLOOR)):
        b = component(bridge, BEAM, "ForceBeamColumn", name, BEAMS, row)
        wire(inp(b, 0), *lines)
        # The segments of both trusses come as one branch per truss: one list of members.
        inp(b, 0).DataMapping = GH_DataMapping.Flatten
        wire(inp(b, 1), out(section))
        beams.append(b)

    # 4 - Bearings and loads: pinned at x = 0, sliding along the span at the far end; self-weight
    # and a deck load on the bottom chords, 5 kN/m2 over half the width each.
    fixed = toggle(bridge, "Restrained", True, SECTIONS, BEARINGS)
    free = toggle(bridge, "Free", False, SECTIONS, BEARINGS)
    pinned = component(bridge, SUPPORT, "Support", "Pinned", BEAMS, BEARINGS)
    sliding = component(bridge, SUPPORT, "Support", "Sliding", BEAMS, BEARINGS)
    wire(inp(pinned, 0), out(ends, 0))
    wire(inp(sliding, 0), out(ends, 1))
    for i in (1, 2, 3):
        wire(inp(pinned, i), fixed)
    wire(inp(sliding, 1), free)
    for i in (2, 3):
        wire(inp(sliding, i), fixed)
    for i in (4, 5, 6):
        wire(inp(pinned, i), free)
        wire(inp(sliding, i), free)

    gravity = component(bridge, GRAVITY, "Gravity Load", None, SECTIONS, LOADING)
    down = component(bridge, UNIT_Z, "Unit Z", "Downwards", SECTIONS, LOADING)
    faint(wire(inp(down, 0), deck))
    deck_load = component(bridge, LINEAR_LOAD, "Linear Load", "Deck load", LOADS, LOADING)
    wire(inp(deck_load, 0), out(beams[0]))
    wire(inp(deck_load, 1), out(down))
    pattern = component(bridge, LOAD_PATTERN, "Load Pattern", None, PATTERN, LOADING)
    wire(inp(pattern, 1), out(gravity), out(deck_load))

    # 7 - Explorer Inputs, under the design inputs it steps through.
    explorer_inputs = component(bridge, EXPLORER_INPUTS, "Explorer Inputs", None, INPUTS, gap=80)
    for source in (span, depth, panels, chord):
        wire_next(explorer_inputs, source)

    right = bridge.arrange(0, 0, stages=(NUMBERS, NAMES))

    # 5, 6, 8 - The analysis, the results and the record, to the right of the bridge.
    study = Grid(doc)
    assemble = component(study, ASSEMBLE, "Assemble Model", None, MODEL, ASSEMBLED)
    wire(inp(assemble, 0), *[out(b) for b in beams])
    wire(inp(assemble, 1), out(pinned), out(sliding))
    wire(inp(assemble, 2), out(pattern))
    settings = component(study, ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, STATIC)
    run = component(study, RUN_ANALYSIS, "Run Analysis", None, VIEWS, ASSEMBLED)
    wire(inp(run, 0), out(assemble))
    wire(inp(run, 1), out(settings))
    view = component(study, MODEL_VIEW, "Model View", None, VIEWS, STATIC)
    wire(inp(view, 0), out(run))
    modal = component(study, NATURAL_VIBRATION, "Natural Vibration Analysis", None, VIEWS, CHECKED)
    wire(inp(modal, 0), out(assemble))

    mass = expression(study, "f(m)=m/1000", [out(assemble, 1)], "Mass in t", RESULTS, ASSEMBLED)    # from kg
    displacements = component(study, NODAL_DISPLACEMENTS, "Nodal Displacements", None, RESULTS, STATIC)
    wire(inp(displacements, 0), out(run))
    size = component(study, VECTOR_LENGTH, "Vector Length", None, STEP1, STATIC)
    wire(inp(size, 0), out(displacements, 1))
    largest = component(study, BOUNDS, "Bounds", "Largest", STEP2, STATIC)
    wire(inp(largest, 0), out(size))
    largest_end = component(study, DECONSTRUCT_DOMAIN, "Deconstruct Domain", "Largest value", STEP3, STATIC)
    wire(inp(largest_end, 0), out(largest))
    deflection = expression(study, "f(u)=u*1000", [out(largest_end, 1)], "Deflection in mm", STEP4, STATIC)  # from m
    utilisation = component(study, UTILISATION, "Utilisation", None, RESULTS, CHECKED)
    wire(inp(utilisation, 0), out(run))
    highest = component(study, BOUNDS, "Bounds", "Highest", STEP1, CHECKED)
    wire(inp(highest, 0), out(utilisation, 12))                         # Max, per member
    highest_end = component(study, DECONSTRUCT_DOMAIN, "Deconstruct Domain", "Highest value", STEP2, CHECKED)
    wire(inp(highest_end, 0), out(highest))

    explorer_outputs = component(study, EXPLORER_OUTPUTS, "Explorer Outputs", None, OUTPUTS)
    wire_next(explorer_outputs, out(mass), "Mass [t]")
    wire_next(explorer_outputs, out(deflection), "Deflection [mm]")
    wire_next(explorer_outputs, out(highest_end, 1), "Utilisation")
    wire_next(explorer_outputs, out(modal, 3), "Frequency [Hz]")
    write = toggle(study, "Write", False, OUTPUTS, gap=30)
    record = component(study, EXPLORER_RECORD, "Explorer Record", None, RECORD)
    faint(wire(inp(record, 1), out(explorer_inputs, 0)))
    wire(inp(record, 2), out(explorer_outputs))
    faint(wire(inp(record, 4), out(run)))
    wire(inp(record, 6), write)

    study.arrange(right + 160, 0, stages=(RESULTS, OUTPUTS))

    # The groups, once everything is in place. Each covers a block of the grid of its own.
    group(doc, "1 - The design: four inputs studied, two kept", [span, depth, panels, chord, width, deck], RED)
    group(doc, "2 - Two Warren trusses, joined at every node",
          [length, half, xb, xt, pb, pt, zig, across, pl_bottom, pl_top, pl_diag, mv_pb, mv_pt,
           mv_bottom, mv_top, mv_diag, cross_bottom, cross_top, ex_bottom, ex_top, ex_diag, ends], ORANGE)
    group(doc, "3 - Members", [steel, material, diag_name, cross_name, chord_section, diag_section,
                               cross_section] + beams, PURPLE)
    group(doc, "4 - Bearings and loads", [fixed, free, pinned, sliding, gravity, down, deck_load, pattern], GREEN)
    group(doc, "5 - Analysis", [assemble, settings, run, view, modal], BLUE)
    group(doc, "6 - Results", [mass, displacements, size, largest, largest_end, deflection, utilisation,
                               highest, highest_end], GREY)
    group(doc, "7 - Explorer Inputs: press Fly to run the study", [explorer_inputs], RED)
    group(doc, "8 - What each design is saved with", [explorer_outputs, write, record], RED)

    # Only Model View draws: the pictures each design is saved with show the bridge in its
    # sections, not the construction lines behind it.
    for obj in doc.Objects:
        obj = getattr(obj, "__implementation__", obj)
        if hasattr(obj, "Hidden") and obj.InstanceGuid != view.InstanceGuid:
            obj.Hidden = True

    note(doc,
         "Footbridge design study - the Alpaca4d Explorer example.\n\n"
         "Two steel Warren trusses, 2.5 m apart, studied over 4 spans x 3 depths x 3 panel "
         "counts x 3 chord sections: 108 designs. Each is analysed under its own weight and a "
         "deck load, checked to EN 1993-1-1 and solved for its first natural frequency.\n\n"
         "1. Save this definition. The study is recorded next to it, in AlpacaExplorer.\n"
         "2. Show the bridge in a perspective view in Rhino: each design's picture is taken "
         "from the active viewport.\n"
         "3. Set Write, on Explorer Record, to true.\n"
         "4. Press Fly under Explorer Inputs. Right-click it > Test fly runs three designs "
         "first, to check the pictures.\n"
         "5. Right-click Explorer Record > Open the Alpaca4d Explorer, and drop the study "
         "folder on the page.",
         0, -340, 560, 270)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Footbridge design study built: 108 designs. Save the definition, set Write to true "
          "and press Fly under Explorer Inputs.")


if __name__ == "__main__":
    main()
