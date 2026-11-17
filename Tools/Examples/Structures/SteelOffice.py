#! python3
# A steel office building under the load combinations of EN 1990, and the report of it: its loads in
# four load patterns, G, Q, S and W, combined seven ways by one Load Combination, every combination
# solved by one Run Analysis into one model that holds them all, and a Report that lists the patterns
# and the combinations, gives the results of each and checks the steel members over the ultimate ones.
#
# Four bays of 6 m along x by two of 5 m along y, four storeys of 3.5 m: HEB 240 columns built in at
# the ground, IPE 360 floor beams and IPE 300 roof beams along x, which carry the floors spanning
# between them, and IPE 240 ties along y, all S355. The geometry comes from a GhPython component
# (SteelOfficeGeometry.py, next to this script); the rest is Alpaca4d's components.
#
# Run in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run) - it also lays out the report's chapters, as the Report's Templates bar does,
# through Grasshopper's own component server. make.sh, in the folder above, writes the rest straight
# from this script, without Rhino, and leaves the Report's Content empty: it then writes its own
# report, the same tables without the texts.
#     ./make.sh Structures/SteelOffice.py
#
# It borrows the building blocks of Structures.py, next to it: the grid the components are laid out on,
# the groups, the sliders and the views.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
if HERE not in sys.path:
    sys.path.append(HERE)

import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_Panel, GH_BooleanToggle
from System.Drawing import PointF, RectangleF, Color

import Structures as S
from Structures import (component, inp, out, wire, flat, words, panel, slider, text, Structure,
                        SECTIONS, MEMBERS, GEOMETRY_, WIDTHS, MODEL, ANALYSIS, VIEWS, LEGENDS,
                        MAIN, LOADED, SHOWN, UNIT_X, UNIT_Z)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "SteelOfficeGeometry.py")

LOAD_COMBINATION = "6F2B9C4E-3D81-4A7F-B5E2-9C04D7A1E863"   # Load Combination
REPORT = "4D6285D0-F64A-4CF1-A9A6-B62EDC615D7D"             # Report

# Load Pattern's Name input; Load Combination's inputs; Model View's Combination; Report's Models and Run.
P_NAME = 3
C_MODEL, C_NAME, C_TYPE, C_FORMULA = 0, 1, 2, 3
V_COMBINATION = 6
R_MODELS, R_RUN = 0, 4

# The columns after Assemble: the combinations' names, types and formulas, and the Load Combination;
# and the combination to draw.
NAMES, TYPES, FORMULAS, COMBINE = MODEL + 0.2, MODEL + 0.4, MODEL + 0.6, MODEL + 0.8
PICK = ANALYSIS + 0.3

YELLOW = Color.FromArgb(60, 230, 200, 40)

FLAGS = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
STATIC = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public

# The combinations, to EN 1990, Table A1.2(B) and A1.4, for an office (category B: psi0 0.7, psi2 0.3),
# snow at a site below 1000 m (psi0 0.5) and wind (psi0 0.6): each variable load leads an ultimate
# combination in turn, and ULS 4 takes the permanent load as favourable, with the wind alone. One Load
# Combination adds them all: a line each in three panels, its name, its type and its formula.
COMBINATIONS = [
    ("ULS 1", "ULS", "1.35 G + 1.5 Q + 0.75 S + 0.9 W"),
    ("ULS 2", "ULS", "1.35 G + 1.05 Q + 0.75 S + 1.5 W"),
    ("ULS 3", "ULS", "1.35 G + 1.05 Q + 1.5 S + 0.9 W"),
    ("ULS 4", "ULS", "1.0 G + 1.5 W"),
    ("SLS 1", "SLS characteristic", "G + Q + 0.5 S + 0.6 W"),
    ("SLS 2", "SLS characteristic", "G + 0.7 Q + 0.5 S + W"),
    ("SLS 3", "SLS quasi-permanent", "G + 0.3 Q"),
]

# The report's chapters, as the Templates bar's For the models given lays them out for these models:
# each by its method in the plugin, with its argument.
CHAPTERS = [("TitlePage", None), ("TheModel", True), ("Loads", True), ("StaticAnalysis", True),
            ("MemberForces", None), ("DesignChecks", None), ("Conclusions", None), ("About", None)]

# The template's texts to write over, by how they start: the building's own.
TEXTS = {
    "Structural analysis report": [
        u"Steel office building",
        u"Load combinations to EN 1990 and the steel check to EN 1993-1-1"],
    "This report sets out": [
        u"This report sets out the steel frame of a four-storey office building, the loads on it and how they are "
        u"combined to EN 1990, the results of each combination, and the check of its members to EN 1993-1-1. It was "
        u"made in Grasshopper with Alpaca4d and OpenSees."],
    "Describe the structure here": [
        u"A steel frame of four bays of 6 m along x by two of 5 m along y, and four storeys of 3.5 m. HEB 240 "
        u"columns, built in at the ground; IPE 360 floor beams and IPE 300 roof beams along x, which carry the floors "
        u"and the roof spanning between them; IPE 240 ties along y. All S355.",
        u"The model has {model.nodes} nodes and {model.elements} elements, and weighs {model.mass:N0} kg."],
    "The loads are grouped": [
        u"G is the permanent load: the frame's own weight, and 4.0 kN/m² of floor slab, finishes and services on "
        u"each floor, 2.0 kN/m² on the roof. Q is the imposed load of an office, 3.0 kN/m² on each floor. S is "
        u"1.0 kN/m² of snow on the roof. W is 1.0 kN/m² of wind on the face at x = 0, blowing along x. The "
        u"floor and roof loads reach the beams along x over the width of floor each carries, and the wind the columns "
        u"of that face over the width of wall each catches.",
        u"The patterns are combined to EN 1990, Table A1.2(B), with ψ0 = 0.7 for Q, 0.5 for S and 0.6 for W, "
        u"and ψ2 = 0.3 for Q: each variable load leads an ultimate combination in turn, and ULS 4 takes the "
        u"permanent load as favourable, under the wind alone."],
    # Read off the report of the building as the definition opens.
    "Write the conclusions here": [
        u"With the loads as typed, every steel member passes the check over the ultimate combinations. The floor "
        u"beams are the most used, at 0.52 in bending under ULS 1, where the imposed load leads; the columns next, at "
        u"0.48 under ULS 1 too.",
        u"The ties are worst under ULS 2, where the wind leads: the model holds them rigidly to the columns, so they "
        u"twist with the joints as the floor beams bend and the frame sways, and torsion gives their 0.42. With the "
        u"simple connections ties usually have, they would carry none.",
        u"Under the characteristic combinations the frame moves 10.7 mm at most, under SLS 2, where the wind leads: "
        u"in the middle of a roof beam of the windward bay, its sway and its sag together. That is less than H/500 = "
        u"28 mm for the sway alone; compare each beam's sag with the limits of the project too."],
}


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


def retext(chapter, texts):
    """Writes the building's own texts over the template's, before the chapter is laid out: each panel
    is then as tall as its text."""
    parts = chapter.GetType().GetField("Parts", FLAGS).GetValue(chapter)
    for part in parts:
        field = part.GetType().GetField("Lines")
        if field is None:
            continue
        lines = list(field.GetValue(part))
        for start in texts:
            if lines and lines[0].startswith(start):
                field.SetValue(part, System.Array[System.String](texts[start]))


def template(report, name, chapters, texts):
    """The report's chapters as components, laid out left of the Report and wired into its Content, as
    the Templates bar under it does: by the plugin's own code, reached by reflection."""
    report = getattr(report, "__implementation__", report)
    t = report.GetType().Assembly.GetType("Alpaca4d.Gh.ReportTemplates")
    chapter_type = t.GetNestedType("Chapter", STATIC)
    list_type = System.Type.GetType("System.Collections.Generic.List`1").MakeGenericType(
        System.Array[System.Type]([chapter_type]))
    laid = System.Activator.CreateInstance(list_type)
    add = list_type.GetMethod("Add")
    for method, argument in chapters:
        count = 0 if argument is None else 1
        made = [m for m in t.GetMethods(STATIC) if m.Name == method and len(m.GetParameters()) == count][0]
        chapter = made.Invoke(None, None if argument is None else System.Array[System.Object]([argument]))
        retext(chapter, texts)
        add.Invoke(laid, System.Array[System.Object]([chapter]))
    t.GetMethod("Insert", STATIC).Invoke(None, System.Array[System.Object]([report, name, laid]))


def office(doc):
    s = Structure(doc, "Steel office", "office", [
        ("BaysX", "Bays along x", 1, 8, 4, 0),
        ("BaysY", "Bays along y", 1, 6, 2, 0),
        ("BayX", "Bay along x [m]", 4, 9, 6, 1),
        ("BayY", "Bay along y [m]", 4, 9, 5, 1),
        ("Storeys", "Storeys", 1, 10, 4, 0),
        ("StoreyHeight", "Storey height [m]", 2.8, 4.5, 3.5, 1),
    ], [
        ("FloorDead", "Floor slab and finishes [kN/m2]", 0, 8, 4.0, 2),
        ("RoofDead", "Roof and finishes [kN/m2]", 0, 5, 2.0, 2),
        ("Imposed", "Imposed load [kN/m2]", 0, 7.5, 3.0, 2),
        ("Snow", "Snow [kN/m2]", 0, 3, 1.0, 2),
        ("Wind", "Wind [kN/m2]", 0, 2, 1.0, 2),
    ])
    s.geometry(["Columns", "WindwardColumns", "WindWidths", "FloorBeams", "FloorWidths", "RoofBeams", "RoofWidths",
                "Ties", "Feet", "Info"])
    info = panel(s.grid, "About the building", s.g("Info"), 260, 60, GEOMETRY_, MAIN, gap=30)
    s.parts["1"].append(info)

    # 2 - The members, S355: HEB 240 columns, those of the windward face too; IPE 360 floor beams and
    # IPE 300 roof beams along x; IPE 240 ties along y.
    s.steel()
    columns = component(s.grid, S.SECTION, "Steel Section Library", "HEB240", SECTIONS, MAIN, gap=30)
    words(inp(columns, 0), "HEB240")
    wire(inp(columns, 1), out(s.material))
    s.parts["2"].append(columns)
    s.elements("Columns", "Columns", columns)
    windward = s.elements("Windward columns", "WindwardColumns", columns)
    floors = s.member("Floor beams", "FloorBeams", "IPE360")
    roof = s.member("Roof beams", "RoofBeams", "IPE300")
    s.member("Ties", "Ties", "IPE240")

    # 3 - Every column built in at the ground.
    s.support("Built in", "Feet", (True,) * 6)

    # 4 - The loads, as they are, not factored, a load pattern each, named for the combinations: G, the
    # own weight and the floors' and roof's dead load; Q, people on the floors; S, snow on the roof; W, the
    # wind on the face at x = 0.
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Own weight", S.LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    floor_dead = s.line_load("Floor slab and finishes", [floors], UNIT_Z, -1, "FloorDead", s.g("FloorWidths"))
    roof_dead = s.line_load("Roof and finishes", [roof], UNIT_Z, -1, "RoofDead", s.g("RoofWidths"))
    imposed = s.line_load("Imposed", [floors], UNIT_Z, -1, "Imposed", s.g("FloorWidths"))
    snow = s.line_load("Snow", [roof], UNIT_Z, -1, "Snow", s.g("RoofWidths"))
    wind = s.line_load("Wind", [windward], UNIT_X, 1, "Wind", s.g("WindWidths"))
    for nickname, name, loads in [("G - permanent", "G", [weight, floor_dead, roof_dead]),
                                  ("Q - imposed", "Q", [imposed]),
                                  ("S - snow", "S", [snow]),
                                  ("W - wind along x", "W", [wind])]:
        words(inp(s.pattern(nickname, loads), P_NAME), name)

    # 5 - The model, with every pattern on it.
    assembled = component(s.grid, S.ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(assembled, 0)), *[out(m) for m in s.members])
    wire(flat(inp(assembled, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(assembled, 2)), *[out(p) for p in s.patterns])
    settings = component(s.grid, S.ANALYSIS_SETTINGS, "Analysis Settings", None, MODEL, MAIN, gap=30)
    s.assembled = assembled
    model_part = [assembled, settings]

    # 6 - The combinations: one Load Combination adds them all to the model, a line each in its three
    # panels - name, type, formula.
    names = lines_panel(s.grid, "Name", [n for n, t, f in COMBINATIONS], 60, NAMES, MAIN, gap=30)
    types = lines_panel(s.grid, "Type", [t for n, t, f in COMBINATIONS], 130, TYPES, MAIN, gap=30)
    formulas = lines_panel(s.grid, "Combination", [f for n, t, f in COMBINATIONS], 220, FORMULAS, MAIN, gap=30)
    combine = component(s.grid, LOAD_COMBINATION, "Load Combination", "EN 1990", COMBINE, MAIN, gap=30)
    wire(inp(combine, C_MODEL), out(assembled))
    wire(inp(combine, C_NAME), names)
    wire(inp(combine, C_TYPE), types)
    wire(inp(combine, C_FORMULA), formulas)
    combine_part = [names, types, formulas, combine]

    # 7 - One Run Analysis solves every combination, each with its own results, and hands on one model
    # that holds them all.
    run = component(s.grid, S.RUN_ANALYSIS, "Run Analysis", "Every combination", ANALYSIS, MAIN)
    wire(inp(run, 0), out(combine))
    wire(inp(run, 1), out(settings))
    s.run = run

    # 8 - Results: one combination at a time, picked by its place - or type its name in a panel instead -
    # deformed and with its loads; and the model with all its loads.
    pick = slider(s.grid, "Combination", 0, len(COMBINATIONS) - 1, 0, 0, PICK, SHOWN, gap=30)
    order = text(s.grid, "Combinations in order", ", ".join(
        "{0} {1}".format(i, n) for i, (n, t, f) in enumerate(COMBINATIONS)), PICK, SHOWN, gap=10)
    order.Attributes.Bounds = RectangleF(0.0, 0.0, 200.0, 48.0)
    deformed = s.deformed("Combination deformed", out(run), 60, palette="steeloffice")
    wire(inp(deformed, V_COMBINATION), pick)
    # Its preview off: its arrows, every pattern's at full size, would hide the combination's shape.
    s.loads_view().Hidden = True
    results_part = [pick, order] + s.parts["6"]

    right = s.grid.arrange(300, 300, stages=(GEOMETRY_, SECTIONS, WIDTHS, MODEL, NAMES, ANALYSIS, PICK))
    bottom = max(o.Attributes.Bounds.Bottom for o, col, row, gap in s.grid.items)
    for key, name, objects, colour in [
            ("1", "Geometry: a grid of bays and storeys", s.parts["1"], S.RED),
            ("2", "Members: HEB 240 columns, IPE 360 floor beams, IPE 300 roof beams, IPE 240 ties, S355", s.parts["2"], S.ORANGE),
            ("3", "Supports: every column built in", s.parts["3"], S.PURPLE),
            ("4", "Loads: four load patterns, G, Q, S and W, not factored", s.parts["4"], S.GREEN),
            ("5", "Model: every pattern on it", model_part, S.BLUE),
            ("6", "Load combinations to EN 1990: a line each", combine_part, YELLOW),
            ("7", "Analysis: every combination, one model out", [run], S.BLUE),
            ("8", "Results: a combination deformed, picked by its place, and the loads", results_part, S.GREY)]:
        S.group(doc, key + " - " + name, objects, colour)
    S.note(doc, s.title, ABOUT, 300, 60, 900, 200)

    # 9 - The report, under the rest: the model Run Analysis hands on, and every combination in it. Its chapters are
    # laid out left of it, as the Templates bar lays them, then the whole band is moved to the left edge.
    report = S.emit(REPORT, "Report")
    report.NickName = "Steel office report"
    doc.AddObject(report, False)
    S.move(report, right + 2000, bottom + 260)
    wire(flat(inp(report, R_MODELS)), out(run))
    before = set(str(o.InstanceGuid) for o in doc.Objects)
    if isinstance(doc, GH_Document):
        template(report, "Steel office", CHAPTERS, TEXTS)
    band = [report] + [o for o in doc.Objects if str(o.InstanceGuid) not in before]
    boxes = [o.Attributes.Bounds for o in band if o.GetType().Name != "GH_Group"]
    dx, dy = 300 - min(b.Left for b in boxes), bottom + 260 - min(b.Top for b in boxes)
    for o in band:
        if o.GetType().Name != "GH_Group":
            shift(o, dx, dy)

    # Write the report sets Run, above the Report.
    toggle = GH_BooleanToggle()
    toggle.CreateAttributes()
    toggle.NickName = "Write the report"
    toggle.Value = False
    doc.AddObject(toggle, False)
    wire(inp(report, R_RUN), toggle)
    at = report.Attributes.Bounds
    S.move(toggle, at.Left, at.Top - 60)
    S.group(doc, "9 - Report: set Write the report to True", [toggle, report], S.GREY)
    return report


def shift(obj, dx, dy):
    """Moves an object by dx, dy: a panel's box too, which does not follow its pivot."""
    obj = getattr(obj, "__implementation__", obj)
    a = obj.Attributes
    b = a.Bounds
    a.Pivot = PointF(float(a.Pivot.X + dx), float(a.Pivot.Y + dy))
    if obj.GetType().Name == "GH_Panel":
        a.Bounds = RectangleF(float(b.X + dx), float(b.Y + dy), b.Width, b.Height)
    a.ExpireLayout()
    a.PerformLayout()


ABOUT = (
    "Steel office - load combinations, and the report of them.\n\n"
    "Four bays by two, four storeys: a column at every crossing of the grid, built in at the ground; beams along x, "
    "which carry the floors spanning along y between them; ties along y.\n\n"
    "The loads are typed in kN/m2, as they are, and grouped in four load patterns, each named: G the permanent load, "
    "Q the imposed load on the floors, S the snow on the roof and W the wind on the face at x = 0. Load Combination "
    "adds seven combinations to the model, a line each in its three panels - four ULS, two SLS characteristic and one "
    "SLS quasi-permanent. Run Analysis solves each, with only the patterns it names, each times its factor, and hands on "
    "one model that holds them all.\n\n"
    "Combination picks one to draw, deformed 60 times, with its own loads: its place, or type its name instead. The "
    "view of every load has its preview off. The Report lists the patterns and the combinations, gives "
    "the results of each and checks the steel members to EN 1993-1-1 over the ULS ones: set Write the report to "
    "True. Its texts describe the building as it opens; change them with the sliders.\n\n"
    "Try: a fifth ULS combination, a line in each panel; Storeys up to 6, and see which member the steel check "
    "finds worst; Wind up to 1.5 kN/m2.")


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code from its
    # GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    S.SCALES["steeloffice"] = [S.DARK_TECH, S.LIGHT_BLUE, S.ORANGE_]
    office(doc)


def fit(canvas, doc):
    """The canvas zoomed to show the whole definition."""
    boxes = [o.Attributes.Bounds for o in doc.Objects]
    left, top = min(b.Left for b in boxes), min(b.Top for b in boxes)
    width, height = max(b.Right for b in boxes) - left, max(b.Bottom for b in boxes) - top
    view = canvas.Viewport
    view.Zoom = float(min(view.Width / (width + 80.0), view.Height / (height + 80.0)))
    view.MidPoint = PointF(float(left + width / 2.0), float(top + height / 2.0))
    view.ComputeProjection()
    canvas.Refresh()


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    fit(Grasshopper.Instances.ActiveCanvas, doc)
    print("Steel office built.")


if __name__ == "__main__":
    main()
