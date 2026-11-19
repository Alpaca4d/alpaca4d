#! python3
# A hall of steel and timber under the load combinations of EN 1990, each member checked to the code of
# its own material: glulam arches 50 m across, their crowns rising and falling along the hall in waves,
# glulam purlins between them, on a frame of steel - columns built in across the hall, crosses of round
# bars in the walls and the roof of the end bays, in tension only, and a tie rod under every arch.
#
# Thirteen arches 5 m apart: GL30h 240 x 1200, a parabola from column head to column head, its rise
# swinging between 5.3 and 9.7 m along a 30 m wave. GL24h 140 x 400 purlins every other node of the
# arches. HEB 300 columns 6 m tall, HEA 200 eave beams, an 80 mm round tie rod under each arch, and
# 30 mm round bars in the crosses, all S355. The geometry comes from a GhPython component
# (WaveRoofHallGeometry.py, next to this script); the rest is Alpaca4d's components.
#
# The loads in four patterns, each with the duration timber's k_mod is set by, in Load Pattern's
# Duration menu: G permanent, S short-term, Wx and Wy instantaneous. Six ULS combinations and two SLS
# ones; Utilisation checks the timber on its Timber type, to EN 1995-1-1, with each combination's k_mod,
# and the steel on its Steel type, to EN 1993-1-1. Then one Section Optimiser sizes timber and steel
# together over the six ULS combinations, from lists of sections a Design Group each, and the hall's
# utilisation is drawn as drawn and as sized, side by side.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Structures/WaveRoofHall.py
# Alpaca4d.Test/WaveRoofHall/example.sh builds and solves it inside Rhino with this build of the plugin.
#
# It borrows the building blocks of Structures.py, next to it.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
if HERE not in sys.path:
    sys.path.append(HERE)

import Grasshopper
from Grasshopper.Kernel.Special import GH_Panel
from Grasshopper.Kernel import GH_ParamAccess
from Grasshopper.Kernel.Types import GH_Colour, GH_String
from System.Drawing import PointF, RectangleF, Color

import Structures as S
from Structures import (component, inp, out, wire, flat, words, number, panel, slider, text, expression, switch_unit,
                        Structure, SECTIONS, MEMBERS, GEOMETRY_, WIDTHS, MODEL, ANALYSIS, VIEWS, LEGENDS,
                        MAIN, LOADED, SHOWN, UNIT_X, UNIT_Y, UNIT_Z, RECTANGLE, CIRCLE, B_ELEMENT_ID, M_COLOUR,
                        BY_MATERIAL)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "WaveRoofHallGeometry.py")

LOAD_COMBINATION = "6F2B9C4E-3D81-4A7F-B5E2-9C04D7A1E863"   # Load Combination
UTILISATION = "4F8A2D6B-91C3-4E57-B0A9-3C6E8D1F2A74"        # Utilisation
BOUNDS = "F44B92B0-3B5B-493A-86F4-FD7408C3DAF3"             # Maths > Domain > Bounds
DECONSTRUCT_DOMAIN = "825EA536-AEBB-41E9-AF32-8BAEB2ECB590" # Maths > Domain > Deconstruct Domain
LIST_ITEM = "59DAF374-BC21-4A5E-8282-5504FB7AE9AE"          # Sets > List > List Item
VECTOR_LENGTH = "675E31BF-1775-48D7-BB8D-76B77786DD53"      # Vector > Vector > Vector Length
SECTION_OPTIMISER = "56302D3A-734F-4B03-899E-F6C273CF1B9F"  # Section Optimiser
DESIGN_GROUP = "35B2FF27-7BCC-4138-9CC7-267397D6A45A"       # Design Group

TIMBER_UNIT = "UtilisationTimber"
# Truss's WorksIn, after Line and Section; its ElementId, after Prestress, ZAxis and Colour.
T_WORKS_IN, T_ELEMENT_ID = 2, 6
# Load Pattern's Name input; Load Combination's inputs; Model View's Combination.
P_NAME = 3
C_MODEL, C_NAME, C_TYPE, C_FORMULA = 0, 1, 2, 3
V_COMBINATION = 6
# Load Pattern's Duration menu, by place: Not set, Permanent, Long-term, Medium-term, Short-term, Instantaneous.
PERMANENT, SHORT_TERM, INSTANTANEOUS = 1, 4, 5

# The columns after Assemble: the combinations' names, types and formulas, and the Load Combination; the
# combination to draw; and the checks, right of the views.
NAMES, TYPES, FORMULAS, COMBINE = MODEL + 0.2, MODEL + 0.4, MODEL + 0.6, MODEL + 0.8
PICK = ANALYSIS + 0.3
WHICH, READ, STEP1, STEP2, VALUES = [LEGENDS + k for k in (1.0, 1.3, 1.6, 1.9, 2.2)]
# Rows for the four checks, and the displacement; then the sizing's, under them.
ARCHES_ROW, PURLINS_ROW, COLUMNS_ROW, BRACING_ROW, MOVED_ROW = 3, 4, 5, 6, 7
CANDIDATES_ROW, GROUPS_ROW, DRAWN_ROW, SIZED_ROW = 8, 9, 10, 11

# The sections each member may take, lightest first, as the Section Optimiser tries them: glulam in steps of a
# 45 mm lamella; steel from the library; round bars.
ARCH_DEPTHS = [0.585, 0.675, 0.765, 0.855, 0.945, 1.035, 1.125, 1.215]
PURLIN_DEPTHS = [0.225, 0.27, 0.315, 0.36, 0.405, 0.45]
COLUMN_SIZES = ["HEB200", "HEB220", "HEB240", "HEB260", "HEB280", "HEB300", "HEB320", "HEB340"]
EAVE_SIZES = ["HEA140", "HEA160", "HEA180", "HEA200", "HEA220", "HEA240"]
TIE_DIAMETERS = [0.04, 0.045, 0.05, 0.056, 0.063, 0.07, 0.08, 0.09]
BAR_DIAMETERS = [0.012, 0.016, 0.02, 0.024, 0.03, 0.036]

YELLOW = Color.FromArgb(60, 230, 200, 40)
WOOD = Color.FromArgb(255, 214, 160, 92)

# EN 1990, Table A1.2(B): snow at a site below 1000 m (psi0 0.5), wind (psi0 0.6). The permanent load alone
# comes first: timber's k_mod is lowest under it, 0.6, and it can govern a member that the snow, at 0.9, and
# the wind, at 1.1, do not. ULS 6 takes the permanent load as favourable, under the wind's uplift.
COMBINATIONS = [
    ("ULS 1", "ULS", "1.35 G"),
    ("ULS 2", "ULS", "1.35 G + 1.5 S"),
    ("ULS 3", "ULS", "1.35 G + 1.5 S + 0.9 Wx"),
    ("ULS 4", "ULS", "1.35 G + 0.75 S + 1.5 Wx"),
    ("ULS 5", "ULS", "1.35 G + 0.75 S + 1.5 Wy"),
    ("ULS 6", "ULS", "G + 1.5 Wx"),
    ("SLS 1", "SLS characteristic", "G + S"),
    ("SLS 2", "SLS quasi-permanent", "G"),
]
ULTIMATE = [n for n, t, f in COMBINATIONS if t == "ULS"]

def texts(param, values):
    param.PersistentData.Clear()
    for v in values:
        param.PersistentData.Append(GH_String(v))
    return param


def candidates(s, guid, name, material, names, *dimensions):
    """A section component making a list of sections, one per name: a dimension given as a list varies with
    them, one given as a number is the same for all."""
    c = component(s.grid, guid, name, name, CANDIDATES_COLUMN, CANDIDATES_ROW, gap=10)
    texts(inp(c, 0), names)
    for k, value in enumerate(dimensions):
        if isinstance(value, list):
            number(inp(c, k + 1), *value)
        else:
            number(inp(c, k + 1), value)
    wire(inp(c, len(dimensions) + 1), out(material))
    return c


FLAGS = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public


def lines_panel(grid, name, lines, width, col, row=None, gap=0):
    """A panel of typed lines, each line an item of a list: what a list input takes one by one."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Colour = S.PANEL
    p.Properties.Multiline = False
    p.SetUserText("\n".join(lines))
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(max(24, 10 + 14 * len(lines))))
    return grid.put(p, col, row, gap)


def named(c, name, outputs=False):
    """An input or output by its name: Utilisation's types have inputs of their own."""
    c = getattr(c, "__implementation__", c)
    params = c.Params.Output if outputs else c.Params.Input
    return [p for p in params if p.Name == name][0]


def duration(pattern, index):
    """Load Pattern's Duration menu, set as picking it in the drop-down does: the plain pattern's own."""
    c = getattr(pattern, "__implementation__", pattern)
    base = c.GetType()
    while base is not None and base.GetField("_subcomponents", FLAGS) is None:
        base = base.BaseType
    for sub in base.GetField("_subcomponents", FLAGS).GetValue(c):
        if sub.GetType().Name == "PatternPlain":
            sub.GetType().GetField("_duration", FLAGS).GetValue(sub).Value = index


def timber(s, name, grade):
    """A glulam class from the library, drawn in wood colour wherever the view colours by material."""
    m = component(s.grid, S.MATERIAL, "Material Library Elastic", grade, SECTIONS, MAIN, gap=30)
    words(inp(m, 0), grade)
    inp(m, M_COLOUR).PersistentData.Clear()
    inp(m, M_COLOUR).PersistentData.Append(GH_Colour(WOOD))
    s.parts["2"].append(m)
    return m


def library(s, name, material):
    c = component(s.grid, S.SECTION, "Steel Section Library", name, SECTIONS, MAIN, gap=30)
    words(inp(c, 0), name)
    wire(inp(c, 1), out(material))
    s.parts["2"].append(c)
    return c


def members(s, name, lines, section, element_id, truss=False, zaxis=None):
    """Elements on one of the geometry's outputs, with a section already made, and an ElementId the
    checks pick them by."""
    beam = s.elements(name, lines, section, truss, zaxis)
    words(inp(beam, T_ELEMENT_ID if truss else B_ELEMENT_ID), element_id)
    return beam


def check(s, nickname, steel, element_ids, row, lengths=None, load=None):
    """A Utilisation on its Steel or Timber type, of the members whose ElementId is given, over the ULS
    combinations: its largest Max over all of them in a panel; for timber, k_mod of each combination too."""
    c = component(s.grid, UTILISATION, "Utilisation", nickname, READ, row)
    if not steel:
        switch_unit(c, TIMBER_UNIT)
    wire(named(c, "AlpacaModel"), out(s.run))
    ids = named(c, "ElementId")
    ids.PersistentData.Clear()
    for element_id in element_ids:
        ids.PersistentData.Append(GH_String(element_id))
    for name, source in (lengths or {}).items():
        if isinstance(source, float):
            number(named(c, name), source)
        else:
            wire(named(c, name), source)
    if load:
        words(named(c, "Load"), load)
    wire(named(c, "Combination"), s.ultimate)
    bounds = component(s.grid, BOUNDS, "Bounds", None, STEP1, row)
    wire(inp(bounds, 0), named(c, "Max", True))
    largest = component(s.grid, DECONSTRUCT_DOMAIN, "Deconstruct Domain", "Largest", STEP2, row)
    wire(inp(largest, 0), out(bounds))
    shown = panel(s.grid, nickname + ": largest utilisation", out(largest, 1), 190, 50, VALUES, row)
    parts = [c, bounds, largest, shown]
    if not steel:
        # Each combination's k_mod, the first beam's: every arch is in service class 1.
        first = component(s.grid, LIST_ITEM, "List Item", "k_mod", STEP1, row, gap=10)
        wire(inp(first, 0), named(c, "kmod", True))
        kmods = panel(s.grid, nickname + ": k_mod, ULS 1 to 6", out(first), 190, 110, VALUES, row, gap=10)
        parts += [first, kmods]
    return parts


def waves(doc):
    s = Structure(doc, "Wave roof hall", "hall", [
        ("Span", "Clear span [m]", 20, 60, 50, 1),
        ("Length", "Length [m]", 20, 100, 60, 1),
        ("Bay", "Arch spacing [m]", 3, 8, 5, 1),
        ("Height", "Column height [m]", 4, 10, 6, 1),
        ("Rise", "Mean rise of the arches [m]", 3, 12, 7.5, 1),
        ("Wave", "Wave: rise up and down [m]", 0, 4, 2.5, 1),
        ("Wavelength", "Wavelength along the hall [m]", 10, 60, 30, 1),
        ("Segments", "Elements per arch", 8, 40, 20, 0),
        ("PurlinEvery", "A purlin every n-th node", 1, 4, 2, 0),
    ], [
        ("RoofDead", "Roof build-up [kN/m2]", 0, 2, 0.5, 2),
        ("Snow", "Snow [kN/m2]", 0, 3, 1.0, 2),
        ("Wind", "Wind pressure [kN/m2]", 0, 2, 0.7, 2),
        ("Suction", "Wind suction [kN/m2]", 0, 2, 0.4, 2),
    ])
    s.geometry(["Arches", "Purlins", "PurlinWidths", "Eaves", "EaveWidths", "WindwardColumns", "LeewardColumns",
                "ColumnWidths", "Ties", "WallBraces", "RoofBraces", "WindwardGable", "WindwardHeights", "LeewardGable",
                "LeewardHeights", "Feet", "PurlinSpacing", "InPlaneLength", "LengthsY", "LengthsZ", "LengthsLT",
                "LoadHeights", "Info"])
    info = panel(s.grid, "About the hall", s.g("Info"), 300, 76, GEOMETRY_, MAIN, gap=30)
    s.parts["1"].append(info)

    # 2 - The members. Timber: GL30h arches, GL24h purlins. Steel, S355: HEB 300 columns, their strong
    # axis across the hall; HEA 200 eave beams; round tie rods; round bars in the crosses. The rods and the
    # bars are trusses, which carry their axial force alone - the bars in tension only: of each cross, the
    # diagonal the load stretches carries it, and the other goes slack, as a slender bar does.
    s.steel()
    gl30 = timber(s, "Arches", "GL30h")
    gl24 = timber(s, "Purlins", "GL24h")
    arch = s.section(RECTANGLE, "GL30h 240x1200", gl30, 0.24, 1.2)
    purlin = s.section(RECTANGLE, "GL24h 140x400", gl24, 0.14, 0.4)
    rod = s.section(CIRCLE, "Tie rod 80", s.material, 0.08, 0.0)
    column = library(s, "HEB300", s.material)
    eave = library(s, "HEA200", s.material)
    bar = s.section(CIRCLE, "Bar 30", s.material, 0.03, 0.0)
    members(s, "Arches", "Arches", arch, "Arch")
    gable_windward = members(s, "Arch at y = 0", "WindwardGable", arch, "Arch")
    gable_leeward = members(s, "Arch at y = length", "LeewardGable", arch, "Arch")
    purlins = members(s, "Purlins", "Purlins", purlin, "Purlin")
    eaves = members(s, "Eave beams", "Eaves", eave, "Eave")
    windward = members(s, "Columns at x = 0", "WindwardColumns", column, "Column", zaxis=(0, 1, 0))
    leeward = members(s, "Columns at x = span", "LeewardColumns", column, "Column", zaxis=(0, 1, 0))
    members(s, "Tie rods", "Ties", rod, "Tie", truss=True)
    for name, lines, element_id in (("Wall crosses", "WallBraces", "WallBrace"), ("Roof crosses", "RoofBraces", "RoofBrace")):
        crosses = members(s, name, lines, bar, element_id, truss=True)
        words(inp(crosses, T_WORKS_IN), "Tension")

    # 3 - The columns built in across the hall, about their strong axis, and free to turn along it, where
    # the crosses in the walls hold them.
    s.support("Feet: built in across the hall, pinned along it", "Feet", (True, True, True, False, True, True))

    # 4 - The loads, a pattern each, named for the combinations, with the duration of their loads for
    # timber's k_mod in each pattern's Duration menu: G permanent, S short-term, the winds instantaneous.
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Own weight", S.LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    roof_dead = [s.line_load("Roof build-up on the purlins", [purlins], UNIT_Z, -1, "RoofDead", s.g("PurlinWidths")),
                 s.line_load("Roof build-up on the eaves", [eaves], UNIT_Z, -1, "RoofDead", s.g("EaveWidths"))]
    snow = [s.line_load("Snow on the purlins", [purlins], UNIT_Z, -1, "Snow", s.g("PurlinWidths")),
            s.line_load("Snow on the eaves", [eaves], UNIT_Z, -1, "Snow", s.g("EaveWidths"))]
    wind_x = [s.line_load("Wind on the wall at x = 0", [windward], UNIT_X, 1, "Wind", s.g("ColumnWidths")),
              s.line_load("Suction on the wall at x = span", [leeward], UNIT_X, 1, "Suction", s.g("ColumnWidths")),
              s.line_load("Uplift on the purlins", [purlins], UNIT_Z, 1, "Suction", s.g("PurlinWidths")),
              s.line_load("Uplift on the eaves", [eaves], UNIT_Z, 1, "Suction", s.g("EaveWidths"))]
    wind_y = [s.line_load("Wind on the gable at y = 0", [gable_windward], UNIT_Y, 1, "Wind", s.g("WindwardHeights")),
              s.line_load("Suction on the gable at y = length", [gable_leeward], UNIT_Y, 1, "Suction", s.g("LeewardHeights"))]
    for nickname, name, loads, lasts in [("G - permanent", "G", [weight] + roof_dead, PERMANENT),
                                         ("S - snow, short-term", "S", snow, SHORT_TERM),
                                         ("Wx - wind across the hall", "Wx", wind_x, INSTANTANEOUS),
                                         ("Wy - wind on the gables", "Wy", wind_y, INSTANTANEOUS)]:
        p = s.pattern(nickname, loads)
        words(inp(p, P_NAME), name)
        duration(p, lasts)

    # 5 - The model, with every pattern on it, and its combinations.
    assembled = component(s.grid, S.ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(assembled, 0)), *[out(m) for m in s.members])
    wire(flat(inp(assembled, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(assembled, 2)), *[out(p) for p in s.patterns])
    settings = component(s.grid, S.ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, MAIN, gap=30)
    s.assembled = assembled
    model_part = [assembled, settings]

    names = lines_panel(s.grid, "Name", [n for n, t, f in COMBINATIONS], 60, NAMES, MAIN, gap=30)
    types = lines_panel(s.grid, "Type", [t for n, t, f in COMBINATIONS], 130, TYPES, MAIN, gap=30)
    formulas = lines_panel(s.grid, "Combination", [f for n, t, f in COMBINATIONS], 200, FORMULAS, MAIN, gap=30)
    combine = component(s.grid, LOAD_COMBINATION, "Load Combination", "EN 1990", COMBINE, MAIN, gap=30)
    wire(inp(combine, C_MODEL), out(assembled))
    wire(inp(combine, C_NAME), names)
    wire(inp(combine, C_TYPE), types)
    wire(inp(combine, C_FORMULA), formulas)
    combine_part = [names, types, formulas, combine]

    run = component(s.grid, S.RUN_ANALYSIS, "Run Analysis", "Every combination", ANALYSIS, MAIN)
    wire(inp(run, 0), out(combine))
    wire(inp(run, 1), out(settings))
    s.run = run

    # 6 - The hall in its materials, the timber in wood and the steel in grey; one combination deformed, picked by
    # its place - SLS 1 as it opens, the snow - over the hall at rest as lines.
    materials = component(s.grid, S.MODEL_VIEW, "Model View", "Steel and timber", VIEWS, SHOWN, gap=30)
    wire(inp(materials, S.V_MODEL), out(assembled))
    S.menu_setting(materials, "_ckReleases", "Active", False)
    S.menu_setting(materials, "_ddColourBy", "Value", BY_MATERIAL)
    s.parts["6"].append(materials)
    at_rest = s.at_rest(BY_MATERIAL)
    at_rest.Hidden = True
    pick = slider(s.grid, "Combination", 0, len(COMBINATIONS) - 1, 6, 0, PICK, SHOWN, gap=30)
    order = text(s.grid, "Combinations in order", ", ".join(
        "{0} {1}".format(i, n) for i, (n, t, f) in enumerate(COMBINATIONS)), PICK, SHOWN, gap=10)
    order.Attributes.Bounds = RectangleF(0.0, 0.0, 220.0, 48.0)
    # Drawn 100 times: the 57 mm the snow sags it reads as 6 m, a tenth of the hall. Without its loads: on 108
    # purlins their arrows would hide the colours.
    deformed = s.deformed("Combination deformed", out(run), 100, palette="waveroof", loads=False)
    wire(inp(deformed, V_COMBINATION), pick)
    deformed.Hidden = True
    results_part = [pick, order] + s.parts["6"]

    # 7 - The checks, each over the six ULS combinations, a branch each. Timber on Utilisation's Timber type,
    # to EN 1995-1-1: the arches held sideways at every purlin, buckling in their plane over 0.6 of their
    # length, the purlins loaded on top; k_mod from the shortest duration of each combination's patterns.
    # Steel on its Steel type, to EN 1993-1-1: the columns buckle across the hall as cantilevers, 2 x their
    # height, and along it between their foot and the eave beam.
    s.ultimate = lines_panel(s.grid, "ULS", ULTIMATE, 60, WHICH, ARCHES_ROW)
    twice = expression(s.grid, "f(h)=2*h", [s.sliders["Height"]], "Cantilever: 2 x height", WHICH, COLUMNS_ROW)
    checks = [s.ultimate, twice]
    checks += check(s, "Timber arches", False, ["Arch"], ARCHES_ROW,
                    {"LengthY": s.g("PurlinSpacing"), "LengthLT": s.g("PurlinSpacing"), "LengthZ": s.g("InPlaneLength")},
                    load="Top")
    checks += check(s, "Timber purlins", False, ["Purlin"], PURLINS_ROW, load="Top")
    checks += check(s, "Steel columns", True, ["Column"], COLUMNS_ROW,
                    {"LengthZ": out(twice), "LengthY": s.sliders["Height"], "LengthLT": s.sliders["Height"]})
    checks += check(s, "Steel ties, crosses, eaves", True, ["Tie", "WallBrace", "RoofBrace", "Eave"], BRACING_ROW)

    # The largest displacement under SLS 1, the characteristic combination: the arches' sag under the snow.
    characteristic = lines_panel(s.grid, "SLS characteristic", ["SLS 1"], 70, WHICH, MOVED_ROW)
    displacements = component(s.grid, S.NODAL_DISPLACEMENTS, "Nodal Displacements", "SLS 1", READ, MOVED_ROW)
    wire(inp(displacements, 0), out(s.run))
    wire(named(displacements, "Combination"), characteristic)
    size = component(s.grid, VECTOR_LENGTH, "Vector Length", None, STEP1, MOVED_ROW)
    wire(inp(size, 0), named(displacements, "Displacement", True))
    spread = component(s.grid, BOUNDS, "Bounds", None, STEP1, MOVED_ROW, gap=10)
    wire(inp(spread, 0), out(size))
    largest = component(s.grid, DECONSTRUCT_DOMAIN, "Deconstruct Domain", "Largest", STEP2, MOVED_ROW)
    wire(inp(largest, 0), out(spread))
    in_mm = expression(s.grid, "f(x)=1000*x", [out(largest, 1)], "In mm", STEP2, MOVED_ROW, gap=10)
    moved = panel(s.grid, "SLS 1: largest displacement [mm]", out(in_mm), 190, 50, VALUES, MOVED_ROW)
    checks += [characteristic, displacements, size, spread, largest, in_mm, moved]

    sizing = sized(s, gl30, gl24, combine)

    s.grid.arrange(300, 300, stages=(GEOMETRY_, SECTIONS, WIDTHS, MODEL, NAMES, ANALYSIS, PICK, WHICH))
    # Every switcher with a fold-out menu - Utilisation, Section Optimiser - has its menus composed and is laid
    # out again where the grid put it, as switching its unit does: its menus' plugs stay at the canvas's origin
    # otherwise, and the box saved in the file spans from there.
    for c in checks + sizing:
        c = getattr(c, "__implementation__", c)
        if c.GetType().Name in ("Utilisation", "SectionOptimiser"):
            c.Attributes.OnSwitchUnit()
            c.Attributes.ExpireLayout()
            c.Attributes.PerformLayout()
    for key, name, objects, colour in [
            ("1", "Geometry: arches rising and falling in waves along the hall", s.parts["1"], S.RED),
            ("2", "Members: glulam arches and purlins; steel columns, eaves, tie rods and crosses", s.parts["2"], S.ORANGE),
            ("3", "Supports: the columns built in across the hall", s.parts["3"], S.PURPLE),
            ("4", "Loads: G, S, Wx and Wy, each with its Duration for timber's k_mod", s.parts["4"], S.GREEN),
            ("5", "Model", model_part, S.BLUE),
            ("6", "Load combinations to EN 1990: a line each", combine_part, YELLOW),
            ("7", "Analysis: every combination, one model out", [run], S.BLUE),
            ("8", "Views: the hall in steel and timber, and a combination deformed", results_part, S.GREY),
            ("9", "Checks: timber to EN 1995-1-1, steel to EN 1993-1-1, over the ULS combinations; the sag under SLS 1", checks, S.GREY),
            ("10", "Sizing: one Section Optimiser for timber and steel, over the six ULS combinations; utilisation as drawn and sized", sizing, YELLOW)]:
        S.group(doc, key + " - " + name, objects, colour)
    S.note(doc, s.title, ABOUT, 300, 60, 900, 220)
    return s


# The sizing's columns: the candidate sections, the design groups, the optimisers, the plots and the numbers.
CANDIDATES_COLUMN, GROUP_COLUMN, OPTIMISER_COLUMN, PLOT_COLUMN, NUMBERS_COLUMN = [LEGENDS + k for k in (1.0, 1.3, 1.6, 1.9, 2.2)]


def sized(s, gl30, gl24, combine):
    """One Section Optimiser sizes the timber and the steel together, each member by its own code, over the six
    ULS combinations, a Design Group per kind of member, the whole kind on one section; and one more, stopped
    after its first iteration, gives the hall as drawn checked the same way. Each one's utilisation is drawn as
    a plot, the sized hall beside the one as drawn."""
    arch_names = ["GL30h 215x{0:g}".format(d * 1000) for d in ARCH_DEPTHS]
    purlin_names = ["GL24h 140x{0:g}".format(d * 1000) for d in PURLIN_DEPTHS]
    arches = candidates(s, RECTANGLE, "Arch candidates", gl30, arch_names, 0.215, ARCH_DEPTHS)
    purlins = candidates(s, RECTANGLE, "Purlin candidates", gl24, purlin_names, 0.14, PURLIN_DEPTHS)
    ties = candidates(s, CIRCLE, "Tie rod candidates", s.material,
                      ["Tie rod {0:g}".format(d * 1000) for d in TIE_DIAMETERS], TIE_DIAMETERS, 0.0)
    bars = candidates(s, CIRCLE, "Bar candidates", s.material,
                      ["Bar {0:g}".format(d * 1000) for d in BAR_DIAMETERS], BAR_DIAMETERS, 0.0)
    columns = component(s.grid, S.SECTION, "Steel Section Library", "Column candidates", CANDIDATES_COLUMN, CANDIDATES_ROW, gap=10)
    texts(inp(columns, 0), COLUMN_SIZES)
    wire(inp(columns, 1), out(s.material))
    eaves = component(s.grid, S.SECTION, "Steel Section Library", "Eave candidates", CANDIDATES_COLUMN, CANDIDATES_ROW, gap=10)
    texts(inp(eaves, 0), EAVE_SIZES)
    wire(inp(eaves, 1), out(s.material))
    parts = [arches, purlins, ties, bars, columns, eaves]

    groups = []
    for name, element_id, sections in (("Arches", "Arch", arches), ("Purlins", "Purlin", purlins), ("Columns", "Column", columns),
                                       ("Eave beams", "Eave", eaves), ("Tie rods", "Tie", ties),
                                       ("Wall crosses", "WallBrace", bars), ("Roof crosses", "RoofBrace", bars)):
        g = component(s.grid, DESIGN_GROUP, "Design Group", name, GROUP_COLUMN, GROUPS_ROW, gap=10)
        words(inp(g, 0), name)
        words(inp(g, 1), element_id)
        wire(flat(inp(g, 2)), out(sections))
        S.booleans(inp(g, 3), True)
        groups.append(g)
    parts += groups

    python = S.emit(S.PYTHON, "GhPython").GetType().Assembly
    keep = System.Activator.CreateInstance(python.GetType("GhPython.Component.NoChangeHint"))
    plots = []
    for nickname, iterations, row in (("As drawn", 1, DRAWN_ROW), ("Sized", 10, SIZED_ROW)):
        o = component(s.grid, SECTION_OPTIMISER, "Section Optimiser", nickname, OPTIMISER_COLUMN, row)
        wire(named(o, "AlpacaModel"), out(combine))
        wire(flat(named(o, "DesignGroups")), *[out(g) for g in groups])
        S.integer(named(o, "Iterations"), iterations)
        for name, geometry in (("LengthY", "LengthsY"), ("LengthZ", "LengthsZ"), ("LengthLT", "LengthsLT"), ("Load", "LoadHeights")):
            wire(named(o, name), s.g(geometry))
        plot = S.python_component(s.grid, "Utilisation " + nickname.lower(), "plot",
                                  [("Elements", keep, GH_ParamAccess.list), ("Utilisation", keep, GH_ParamAccess.list),
                                   ("Shift", S.GH_Vector3dHint())],
                                  ["Mesh", "Values", "Colours"], PLOT_COLUMN, row)
        wire(inp(plot, 0), named(o, "Element", True))
        wire(inp(plot, 1), named(o, "Utilisation", True))
        mass = panel(s.grid, nickname + ": mass in each iteration [kg]", named(o, "Mass", True), 200, 70, NUMBERS_COLUMN, row)
        parts += [o, plot, mass]
        plots.append(plot)

    # The sized hall beside the one as drawn, 20 m past it.
    beside = expression(s.grid, "f(s)=s+20", [s.sliders["Span"]], "Beside: span + 20 m", PLOT_COLUMN, SIZED_ROW, gap=10)
    shift = component(s.grid, S.VECTOR_XYZ, "Vector XYZ", "Beside", PLOT_COLUMN, SIZED_ROW, gap=10)
    wire(inp(shift, 0), out(beside))
    wire(inp(plots[1], 2), out(shift))
    caption = text(s.grid, "Utilisation title", "Utilisation, the worst ULS: as drawn, left; sized, right", NUMBERS_COLUMN, GROUPS_ROW)
    legend = component(s.grid, S.LEGEND, "Legend", "Utilisation legend", NUMBERS_COLUMN, GROUPS_ROW, gap=10)
    wire(inp(legend, S.L_TITLE), caption)
    wire(inp(legend, S.L_VALUES), out(plots[0], 2))
    wire(inp(legend, S.L_COLORS), out(plots[0], 3))
    number(inp(legend, S.L_SCALE), 2.4)
    return parts + [beside, shift, caption, legend]


ABOUT = (
    "Wave roof hall - timber and steel together, each checked to its own Eurocode.\n\n"
    "Glulam arches span 50 m across the hall, 5 m apart, each a parabola from column head to column head; their "
    "rise swings along the hall as a wave, so the roof rolls from one end to the other. Glulam purlins run from "
    "arch to arch. Steel holds it up and steady: columns built in across the hall, a tie rod under every arch "
    "taking its thrust, eave beams along the column heads, and crosses of round bars in the walls and the roof "
    "of the end bays, which work in tension only.\n\n"
    "The loads are typed in kN/m2 and grouped in four load patterns, each with the duration timber's k_mod is "
    "set by, in its Duration menu (open the menu under the Load Pattern): G permanent, S snow short-term, Wx "
    "and Wy wind instantaneous. Load Combination adds six ULS combinations and two SLS ones; Run Analysis solves "
    "each.\n\n"
    "Utilisation checks the arches and the purlins on its Timber type, to EN 1995-1-1, with each combination's "
    "k_mod - 0.6 under the permanent load alone, 0.9 with the snow, 1.1 with the wind - and the steel on its "
    "Steel type, to EN 1993-1-1. Each reads the ULS combinations, a branch each.\n\n"
    "Section Optimiser sizes the timber and the steel together, each section checked by its own code, over the six "
    "ULS combinations, each solved in each iteration: a Design Group per kind of member, the whole kind on one "
    "section, the lightest of its list that holds. A second one, stopped after one iteration, checks the hall as "
    "drawn the same way. Their utilisation is drawn side by side, as drawn on the left and sized on the right, in "
    "bands of 0.2, red past 1.\n\n"
    "Try: a wave of 0 for a plain barrel roof; Rise down to 4 m, and watch the tie rods and the arches; the "
    "Snow up to 2 kN/m2; set G's Duration to Not set, and see the remark.")


def build(doc):
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    S.SCALES["waveroof"] = [S.DARK_TECH, S.LIGHT_BLUE, S.ORANGE_]
    return waves(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    print("Wave roof hall built.")


if __name__ == "__main__":
    main()
