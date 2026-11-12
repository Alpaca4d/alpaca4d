#! python3
# Five example reports, each a definition of its own: a structure of one kind of element, analysed, and
# written up as a PDF by the Report component - a title block on every page, numbered headings, text that
# reads its numbers off the models, tables, and pictures with their largest and smallest values written on.
#
#   CASE=beams    a steel hall roofed by Pratt trusses on columns, all beams: static, natural vibration and
#                 linear buckling (ReportBeams.gh)
#   CASE=shells   a concrete barrel vault under wind, all shells: static, natural vibration and linear
#                 buckling (ReportShells.gh)
#   CASE=bricks   a concrete barrel vault built of bricks, five layers through its thickness: static and
#                 natural vibration (ReportBricks.gh)
#   CASE=mixed    a footbridge, a concrete deck of shells on a steel box girder of beams: static and natural
#                 vibration (ReportMixed.gh)
#   CASE=stair    a U-shaped steel stair, hollow section stringers and plate treads, beams and shells: static,
#                 natural vibration and footfall, after SCI P354 (ReportStair.gh)
#
# Each structure is the one its own example builds - Structures.py's hall, vault and footbridge,
# MeshLoftBricks.py's vault, FootfallStair.py's U stair - with what it lacks added beside it: the loads of a
# static analysis, the analyses, and the report, a column for each chapter, through a Merge into the Report.
#
# make.sh, in the folder above, writes each straight from this script, without Rhino:
#     CASE=beams ./make.sh Report/ReportCases.py ReportBeams.gh
#     CASE=shells ./make.sh Report/ReportCases.py ReportShells.gh
#     CASE=bricks ./make.sh Report/ReportCases.py ReportBricks.gh
#     CASE=mixed ./make.sh Report/ReportCases.py ReportMixed.gh
#     CASE=stair ./make.sh Report/ReportCases.py ReportStair.gh
# Set 'Write the report' to True to write the PDF beside the file, then click Open report under the Report.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
EXAMPLES = System.IO.Path.GetDirectoryName(HERE)
for folder in (HERE, System.IO.Path.Combine(EXAMPLES, "Structures"), System.IO.Path.Combine(EXAMPLES, "Solids")):
    if folder not in sys.path:
        sys.path.append(folder)

import Grasshopper
from Grasshopper.Kernel.Special import GH_BooleanToggle
from Grasshopper.Kernel.Types import GH_Interval
from Grasshopper.Kernel import GH_ParameterSide, IGH_VariableParameterComponent
from Rhino.Geometry import Interval

import Structures as S
import FootfallStair as FS
import MeshLoftBricks as MB
import ReportFrame as RF
from ReportFrame import (inp, out, wire, flat, integer, values, vector, at, pick, fold, switch_unit, named, output,
                         results_menu, with_tables, with_table, tick, also, set_box, menu_widget, panel_at, group,
                         MODEL_TOPIC, STATIC_TOPIC, VIBRATION_TOPIC, STABILITY_TOPIC, FOOTFALL_TOPIC,
                         R_MODELS, R_PAGE, R_CONTENT, R_RUN, R_FILE_OUT, R_LOG)

CASE = (System.Environment.GetEnvironmentVariable("CASE") or "beams").lower()

MESH_LOAD = S.MESH_LOAD

# A Report View's results, by their place in its Results menu, and their components by their place.
DISPLACEMENT, BEAM_FORCES, BEAM_STRESSES, SHELL_FORCES, SHELL_STRESSES, BRICK_STRESSES, REACTIONS = range(7)
MAGNITUDE = 0
N, VY, VZ, T, MY, MZ = range(6)
SHELL_VON_MISES, BRICK_VON_MISES = 5, 6
FORCE = 0


def find(doc, guid, nickname=None):
    """The object of guid on the canvas - the one called nickname, if there are several."""
    for o in doc.Objects:
        o = getattr(o, "__implementation__", o)
        if str(o.ComponentGuid).upper() == guid.upper() and (nickname is None or o.NickName == nickname):
            return o
    raise Exception("nothing of " + guid + (" called " + nickname if nickname else "") + " on the canvas")


class Writeup(object):
    """What the report is made of, beside the structure: the loads and analyses it lacks in a column of
    their own, the page in the next, and the content in columns, a chapter each, through a Merge into the
    Report, with a toggle to write it and panels of what it wrote."""

    COLUMN = 360.0

    def __init__(self, doc, title, subtitle, project):
        self.doc = doc
        right = max(o.Attributes.Bounds.Right for o in doc.Objects)
        top = min(o.Attributes.Bounds.Top for o in doc.Objects)
        self.x0, self.y0 = right + 200.0, top
        self.ay = self.y0
        self.added, self.parts = [], []
        self.title, self.subtitle, self.project = title, subtitle, project

        # The page: A4, its title block drawn by a script, the project's name at the top right.
        px = self.x0 + 300.0
        code = RF.TITLE_BLOCK_CODE.replace("Report component test", project)
        drawing = RF.script(doc, "Title block drawing", code, ["Curves", "Text"], px, self.y0)
        block = at(doc, RF.TITLE_BLOCK, "Report Title Block", None, px, self.y0 + 140)
        wire(inp(block, 0), out(drawing, 1))
        wire(inp(block, 1), out(drawing, 2))
        self.page = at(doc, RF.REPORT_PAGE, "Report Page", None, px, self.y0 + 340)
        wire(inp(self.page, 6), out(block))
        self.page_parts = [drawing, block, self.page]

        self.cx = self.x0 + 300.0
        self.cy = None

    # ---------------------------------------------------------------- what the structure lacks

    def add(self, guid, what, nickname=None):
        """A load or an analysis the structure lacks, in the first column."""
        c = at(self.doc, guid, what, nickname, self.x0, self.ay)
        self.ay += max(60.0, c.Attributes.Bounds.Height * 1.2) + 40.0
        self.added.append(c)
        return c

    def static(self, assembled, loads):
        """Own weight and loads, in a Load Pattern into Assemble, and a static analysis of it."""
        weight = self.add(RF.GRAVITY, "Gravity Load", "Own weight")
        pattern = self.add(RF.LOAD_PATTERN, "Load Pattern", "Own weight and loads")
        wire(flat(inp(pattern, 1)), out(weight), *[out(l) for l in loads])
        wire(flat(inp(assembled, 2)), out(pattern))
        settings = self.add(RF.ANALYSIS_SETTINGS, "Analysis Settings")
        run = self.add(RF.RUN_ANALYSIS, "Run Analysis")
        wire(inp(run, 0), out(assembled))
        wire(inp(run, 1), out(settings))
        return run

    def mesh_load(self, name, shells, pressure):
        """pressure kN/m2 down on the shells of the components given."""
        load = self.add(MESH_LOAD, "Mesh Load", name)
        wire(flat(inp(load, 0)), *[out(s) for s in shells])
        vector(inp(load, 1), 0.0, 0.0, -pressure)
        return load

    def vibration(self, assembled, modes):
        v = self.add(RF.NATURAL_VIBRATION, "Natural Vibration Analysis")
        wire(inp(v, 0), out(assembled))
        integer(inp(v, 1), modes)
        return v

    def buckling(self, assembled, modes):
        b = self.add(RF.LINEAR_BUCKLING, "Linear Buckling Analysis")
        wire(inp(b, 0), out(assembled))
        integer(inp(b, 1), modes)
        return b

    # ---------------------------------------------------------------- the content

    def chapter(self):
        """The next column, for the next chapter."""
        self.cx += self.COLUMN
        self.cy = self.y0

    def place(self, c, extra=30.0):
        """c at the foot of the column, which grows by its height."""
        RF.move(c, self.cx, self.cy)
        self.cy += max(50.0, c.Attributes.Bounds.Height * 1.2) + extra
        self.parts.append(c)
        return c

    def text(self, style, *lines):
        t = at(self.doc, RF.REPORT_TEXT, "Report Text", None, self.cx, self.cy)
        pick(t, "_ddStyle", style)
        values(inp(t, 0), *lines)
        fold(t)
        return self.place(t)

    def cover(self):
        return self.text("Title", self.title, self.subtitle)

    def heading(self, words):
        return self.text("Heading", words)

    def subheading(self, words):
        return self.text("Subheading", words)

    def subsubheading(self, words):
        return self.text("Sub-subheading", words)

    def paragraph(self, words):
        return self.text("Paragraph", words)

    def table(self, name, modes=None):
        t = at(self.doc, RF.RESULT_TABLE, "Report Result Table", name, self.cx, self.cy)
        pick(t, "_ddTable", name)
        if modes:
            values(inp(t, 0), modes)
        fold(t)
        return self.place(t)

    def view(self, name, topic, **settings):
        """A Report View on a topic, its inputs typed in by name: View, Caption, Modes, Range..."""
        v = at(self.doc, RF.REPORT_VIEW, "Report View", name, self.cx, self.cy)
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
            elif isinstance(value, int) and not isinstance(value, bool):
                integer(named(v, key), value)
            else:
                values(named(v, key), value)
        return self.place(v, 60.0)

    def static_view(self, name, results, deformed=0.0, table=True, **settings):
        """A Report View of the static analysis: results, (place, [components]) each, a picture per
        component, each with its table; on the shape deformed so many times, or not deformed."""
        v = self.view(name, STATIC_TOPIC, **settings)
        menu = results_menu(v, STATIC_TOPIC)
        tick(menu, DISPLACEMENT, False)
        for place, components in results:
            tick(menu, place, True, components[0])
            for c in components[1:]:
                also(menu, place, c)
            if table:
                with_table(menu, place)
        set_box(menu_widget(menu, "_ckDeformed"), deformed > 0.0)
        if deformed > 0.0:
            menu_widget(menu, "_slScale").Value = float(deformed)
        return v

    def page_break(self):
        b = at(self.doc, RF.PAGE_BREAK, "Report Page Break", None, self.cx, self.cy)
        return self.place(b)

    # ---------------------------------------------------------------- the report

    def finish(self, models):
        """The parts through a Merge, in order, into the Report, with the models and the page, a toggle to
        write it, and panels of the PDF written and the log."""
        x = self.cx + self.COLUMN
        merge = at(self.doc, RF.MERGE, "Merge", "Content", x, self.y0)
        while merge.Params.Input.Count < len(self.parts):
            index = merge.Params.Input.Count
            merge.Params.RegisterInputParam(IGH_VariableParameterComponent.CreateParameter(merge, GH_ParameterSide.Input, index), index)
        IGH_VariableParameterComponent.VariableParameterMaintenance(merge)
        merge.Params.OnParametersChanged()
        for k, part in enumerate(self.parts):
            wire(inp(merge, k), out(part, 0))
        RF.move(merge, x, self.y0)

        report = at(self.doc, RF.REPORT, "Report", None, x + 160.0, self.y0)
        wire(flat(inp(report, R_MODELS)), *models)
        wire(inp(report, R_PAGE), out(self.page))
        wire(inp(report, R_CONTENT), out(merge))
        go = GH_BooleanToggle()
        go.CreateAttributes()
        go.NickName = "Write the report"
        go.Value = False
        RF.put(self.doc, go, x + 160.0, self.y0 + 180)
        wire(inp(report, R_RUN), go)
        written = panel_at(self.doc, "PDF", out(report, R_FILE_OUT), 360, 50, x + 160.0, self.y0 + 240)
        log = panel_at(self.doc, "Log", out(report, R_LOG), 360, 160, x + 160.0, self.y0 + 320)

        if self.added:
            group(self.doc, "8 - For the report: loads and analyses", self.added, RF.PURPLE)
        group(self.doc, "9 - The page: A4, its title block drawn by a script", self.page_parts, RF.GREY)
        group(self.doc, "10 - The content, a chapter a column, in order", self.parts, RF.GREY)
        group(self.doc, "11 - The report: set Write the report to True, then click Open report", [merge, go, report, written, log], RF.YELLOW)


def models(*components):
    """The models the Report reads, from the components that hand them on: Assemble, the analyses, and
    Footfall Analysis's AlpacaModel output."""
    return [output(c, "AlpacaModel") if str(c.ComponentGuid).upper() == RF.FOOTFALL.upper() else out(c) for c in components]


# ---------------------------------------------------------------- 1 - beams: the truss roof hall

def beams(doc):
    S.hall(doc)
    assembled, run = find(doc, S.ASSEMBLE), find(doc, S.RUN_ANALYSIS)
    w = Writeup(doc, "Truss roof hall", "Static, natural vibration and linear buckling analysis",
                "Alpaca4d example report - beams")
    vibration = w.vibration(assembled, 6)
    buckling = w.buckling(assembled, 4)

    w.chapter()
    w.cover()
    w.heading("The model")
    w.paragraph("A steel hall 24 m across and 30 m long: five bays of Pratt trusses 2.4 m deep on HEA300 columns 6 m "
                "tall, pinned at their feet, joined by IPE180 purlins and braced in the end bays. Every member is a "
                "beam, the webs and the bracing pinned at their ends. It has {model.nodes} nodes and {model.elements} "
                "elements, and weighs {model.mass:N0} kg.")
    w.subheading("Members")
    w.table("Model summary")
    w.table("Beam sections")
    w.subheading("Supports and loads")
    w.paragraph("Each column is pinned at its foot. The roof carries 0.5 kN/m2 of finishes and 1.0 kN/m2 of snow on its "
                "purlins: with the steel's own weight, they are factored for the ultimate limit state, 1.35 on what is "
                "permanent and 1.5 on the snow.")
    model = w.view("The hall", MODEL_TOPIC, Caption="The hall, coloured by section, with its supports and loads")
    with_tables(model, MODEL_TOPIC, "model.supports")

    w.chapter()
    w.heading("Static analysis")
    w.paragraph("Under its own weight, the finishes and the snow, the roof moves {static.maxDisplacement:F1} mm at most, "
                "at node {static.maxNode}.")
    w.subheading("Displacement")
    w.static_view("Displacement", [(DISPLACEMENT, [MAGNITUDE])], 60.0, Caption="Displacement, drawn 60 times larger")
    w.subheading("Forces")
    w.subsubheading("In the members")
    w.static_view("Member forces", [(BEAM_FORCES, [N, MY])])
    w.subsubheading("At the supports")
    w.static_view("Reactions", [(REACTIONS, [FORCE])])
    w.page_break()

    w.chapter()
    w.heading("Natural vibration")
    w.paragraph("The hall's first mode, its own weight the mass that swings, is at {vibration.frequency[1]:F2} Hz. "
                "The frequencies and the share of the mass each mode moves follow the pictures.")
    modes = w.view("Modes", VIBRATION_TOPIC, Modes="6")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")
    w.page_break()
    w.heading("Linear buckling")
    w.paragraph("The hall buckles first at {buckling.factor[1]:F2} times its factored loads.")
    buckled = w.view("Buckled shapes", STABILITY_TOPIC, Modes="4")
    with_tables(buckled, STABILITY_TOPIC, "buckling.factors")

    w.finish(models(assembled, run, vibration, buckling))


# ---------------------------------------------------------------- 2 - shells: the barrel vault

def shells(doc):
    S.vault(doc)
    assembled, run = find(doc, S.ASSEMBLE), find(doc, S.RUN_ANALYSIS)
    w = Writeup(doc, "Barrel vault", "Static, natural vibration and linear buckling analysis of a concrete shell",
                "Alpaca4d example report - shells")
    vibration = w.vibration(assembled, 6)
    buckling = w.buckling(assembled, 4)

    w.chapter()
    w.cover()
    w.heading("The model")
    w.paragraph("A concrete barrel vault 16 m across, 5 m high and 24 m long, 120 mm thick, pinned along both its long "
                "edges. It is all shells: ASD shells on a mesh of 16 strips round the arc and 24 divisions along it. It "
                "has {model.nodes} nodes and {model.elements} elements, and weighs {model.mass:N0} kg.")
    w.subheading("The shell")
    w.table("Model summary")
    w.view("The vault", MODEL_TOPIC, Caption="The vault, with its supports and loads")
    w.subheading("Loads")
    w.paragraph("Its own weight, and wind blowing across it: a peak velocity pressure of 0.8 kN/m2 times a pressure "
                "coefficient for each zone, 0.6 on the windward quarter, -1.0 on the middle half and -0.4 on the "
                "leeward quarter, each strip pushed or pulled along its normal.")

    w.chapter()
    w.heading("Static analysis")
    w.paragraph("Under its own weight and the wind, the vault moves {static.maxDisplacement:F2} mm at most, at node "
                "{static.maxNode}.")
    w.subheading("Displacement")
    w.static_view("Displacement", [(DISPLACEMENT, [MAGNITUDE])], 200.0, Caption="Displacement, drawn 200 times larger")
    w.subheading("Stresses")
    w.static_view("Stresses", [(SHELL_STRESSES, [SHELL_VON_MISES])], Caption="Von Mises stress at the top of the shell [{unit}]")
    w.subheading("Reactions")
    # The picture alone: fifty supports along the edges are fifty rows of table.
    w.static_view("Reactions", [(REACTIONS, [FORCE])], table=False)
    w.page_break()

    w.chapter()
    w.heading("Natural vibration")
    w.paragraph("The vault's first mode, its own weight the mass that swings, is at {vibration.frequency[1]:F2} Hz.")
    modes = w.view("Modes", VIBRATION_TOPIC, Modes="6")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")
    w.page_break()
    w.heading("Linear buckling")
    w.paragraph("Under its own weight and the wind, the vault buckles first at {buckling.factor[1]:F2} times its loads.")
    buckled = w.view("Buckled shapes", STABILITY_TOPIC, Modes="4")
    with_tables(buckled, STABILITY_TOPIC, "buckling.factors")

    w.finish(models(assembled, run, vibration, buckling))


# ---------------------------------------------------------------- 3 - bricks: the vault of bricks

def bricks(doc):
    S.GEOMETRY = MB.GEOMETRY_FILE   # its geometry's GhPython code, as its own build points it
    MB.vault(doc)
    assembled, run = find(doc, S.ASSEMBLE), find(doc, S.RUN_ANALYSIS)
    w = Writeup(doc, "Vault of bricks", "Static and natural vibration analysis of a solid concrete vault",
                "Alpaca4d example report - bricks")
    vibration = w.vibration(assembled, 6)

    w.chapter()
    w.cover()
    w.heading("The model")
    w.paragraph("A concrete barrel vault 8 m across, 2.5 m high, 6 m long and 400 mm thick, held at its feet. It is all "
                "solids: SSP bricks of C30/37, five layers through its thickness, lofted from polylines and turned into "
                "bricks by Mesh Series to Brick. It has {model.nodes} nodes and {model.elements} elements, and weighs "
                "{model.mass:N0} kg.")
    w.subheading("The solid")
    w.table("Model summary")
    w.view("The vault", MODEL_TOPIC, Caption="The vault of bricks, with its supports")
    w.subheading("Loads")
    w.paragraph("Its own weight alone.")

    w.chapter()
    w.heading("Static analysis")
    w.paragraph("Under its own weight, the vault moves {static.maxDisplacement:F3} mm at most, at node {static.maxNode}.")
    w.subheading("Displacement")
    w.static_view("Displacement", [(DISPLACEMENT, [MAGNITUDE])], 200.0, Caption="Displacement, drawn 200 times larger")
    w.subheading("Stresses")
    w.static_view("Stresses", [(BRICK_STRESSES, [BRICK_VON_MISES])], Caption="Von Mises stress [{unit}]")
    w.subheading("Reactions")
    # The picture alone: every node of the springings is a support, a row of table each.
    w.static_view("Reactions", [(REACTIONS, [FORCE])], table=False)
    w.page_break()

    w.chapter()
    w.heading("Natural vibration")
    w.paragraph("The vault's first mode, its own weight the mass that swings, is at {vibration.frequency[1]:F2} Hz.")
    modes = w.view("Modes", VIBRATION_TOPIC, Modes="6")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")

    w.finish(models(assembled, run, vibration))


# ---------------------------------------------------------------- 4 - beams and shells: the footbridge

def mixed(doc):
    S.footbridge(doc)
    assembled, vibration = find(doc, S.ASSEMBLE), find(doc, S.NATURAL_VIBRATION)
    deck = find(doc, S.SHELL, "Deck")
    w = Writeup(doc, "Footbridge", "Static and natural vibration analysis of a deck on a box girder",
                "Alpaca4d example report - beams and shells")
    crowd = w.mesh_load("Crowd, 5 kN/m2", [deck], 5.0)
    run = w.static(assembled, [crowd])

    w.chapter()
    w.cover()
    w.heading("The model")
    w.paragraph("A footbridge of 40 m: a concrete deck 3.5 m wide and 200 mm thick, ASD shells, on a steel box girder "
                "1.6 m wide and 1.2 m deep down its middle, beams, the deck tied to the box by rigid links so that they "
                "bend together. It spans between a pinned bearing and a sliding one, both holding it against twisting. "
                "It has {model.nodes} nodes and {model.elements} elements, and weighs {model.mass:N0} kg.")
    w.subheading("Members")
    w.table("Model summary")
    w.table("Beam sections")
    w.view("The footbridge", MODEL_TOPIC, Caption="The footbridge, coloured by section, with its supports and loads")
    w.subheading("Loads")
    w.paragraph("Its own weight, and a crowd of 5 kN/m2 over the whole deck.")

    w.chapter()
    w.heading("Static analysis")
    w.paragraph("Under its own weight and the crowd, the bridge sags {static.maxDisplacement:F1} mm at most, at node "
                "{static.maxNode}.")
    w.subheading("Displacement")
    w.static_view("Displacement", [(DISPLACEMENT, [MAGNITUDE])], 50.0, Caption="Displacement, drawn 50 times larger")
    w.subheading("Forces and stresses")
    w.subsubheading("In the box girder")
    # Its vertical shear and bending, about its strong axis, as its local axes have them.
    w.static_view("Girder forces", [(BEAM_FORCES, [VY, MZ])], View="Front")
    w.subsubheading("In the deck")
    w.static_view("Deck stresses", [(SHELL_STRESSES, [SHELL_VON_MISES])], Caption="Von Mises stress at the top of the deck [{unit}]")
    # No reactions: the bearings are on the box's nodes that the deck's rigid links hang from, and the
    # reactions OpenSees reads there leave out what the links carry - a sliding bearing showing a force
    # along the bridge, the vertical ones short of the load by what the deck brings to its ends.
    w.page_break()

    w.chapter()
    w.heading("Natural vibration")
    w.paragraph("The bridge's first mode, its own weight the mass that swings, is at {vibration.frequency[1]:F2} Hz. "
                "People walk at 1.6 to 2.4 steps a second: a vertical mode near there is the one to watch.")
    modes = w.view("Modes", VIBRATION_TOPIC, Modes="6")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")

    w.finish(models(assembled, run, vibration))


# ---------------------------------------------------------------- 5 - beams and shells: the U stair

def stair(doc):
    S.GEOMETRY = FS.GEOMETRY_FILE   # its geometry's GhPython code, as its own build points it
    FS.u_stair(doc)
    assembled = find(doc, S.ASSEMBLE)
    vibration, footfall = find(doc, S.NATURAL_VIBRATION), find(doc, RF.FOOTFALL)
    treads, landings = find(doc, S.SHELL, "Treads"), find(doc, S.SHELL, "Landings")
    w = Writeup(doc, "U-shaped stair", "Static, natural vibration and footfall analysis, after SCI P354",
                "Alpaca4d example report - stair")
    imposed = w.mesh_load("Finishes and imposed, 4.3 kN/m2", [treads, landings], 4.3)
    run = w.static(assembled, [imposed])

    w.chapter()
    w.cover()
    w.heading("The model")
    w.paragraph("A U-shaped steel stair between floors 4.2 m apart: a long flight, a quarter landing, a short flight, a "
                "second quarter landing and a long flight back up, 1.2 m wide. Each flight hangs between two hollow "
                "section stringers, beams, 100 mm wide and 140 mm deep; its treads and the landings are 20 mm steel "
                "plates, shells; a landing rests on edge beams and a post under its free corner. It has {model.nodes} "
                "nodes and {model.elements} elements, and weighs {model.mass:N0} kg.")
    w.subheading("Members")
    w.table("Model summary")
    w.table("Beam sections")
    w.view("The stair", MODEL_TOPIC, Caption="The stair, coloured by section, with its supports")
    w.subheading("Loads and mass")
    w.paragraph("For the static analysis, its own weight and 4.3 kN/m2 on the treads and landings: 0.3 of finishes and "
                "4.0 imposed. For its vibration, as P354 has it, its own mass with the finishes and a tenth of the "
                "imposed load.")

    w.chapter()
    w.heading("Static analysis")
    w.paragraph("Under its own weight, the finishes and the imposed load, the stair moves {static.maxDisplacement:F1} mm "
                "at most, at node {static.maxNode}.")
    w.subheading("Displacement")
    w.static_view("Displacement", [(DISPLACEMENT, [MAGNITUDE])], 100.0, Caption="Displacement, drawn 100 times larger")
    w.subheading("Forces and stresses")
    w.subsubheading("In the stringers")
    w.static_view("Stringer forces", [(BEAM_FORCES, [MY, MZ])])
    w.subsubheading("In the plates")
    w.static_view("Plate stresses", [(SHELL_STRESSES, [SHELL_VON_MISES])], Caption="Von Mises stress at the top of the plates [{unit}]")
    w.subheading("Reactions")
    w.static_view("Reactions", [(REACTIONS, [FORCE])])
    w.page_break()

    w.chapter()
    w.heading("Natural vibration")
    w.paragraph("The stair's first mode, with the finishes and a tenth of the imposed load, is at "
                "{vibration.frequency[1]:F2} Hz.")
    modes = w.view("Modes", VIBRATION_TOPIC, Modes="6")
    with_tables(modes, VIBRATION_TOPIC, "vibration.modes", "vibration.massRatios")
    w.page_break()
    w.heading("Footfall")
    w.paragraph("Someone walking up and down the stair at 1.2 to 4.5 steps a second, after SCI P354 with its "
                "coefficients for stairs, 0.5% damping and the 12 Hz cut-off, makes it respond up to R = "
                "{footfall.maxFactor:F1}, at node {footfall.maxNode}. P354 accepts 32 on a stair in light use: the "
                "picture's top colour.")
    walked = w.view("Footfall", FOOTFALL_TOPIC, Range=("interval", 0.0, 32.0))
    with_tables(walked, FOOTFALL_TOPIC, "footfall.response")

    w.finish(models(assembled, run, vibration, footfall))


CASES = {"beams": beams, "shells": shells, "bricks": bricks, "mixed": mixed, "stair": stair}


def build(doc):
    maker = globals().get("make_object")
    for module in (S, RF):
        module.make_object = maker
    if CASE not in CASES:
        raise Exception("CASE is one of " + ", ".join(sorted(CASES)))
    CASES[CASE](doc)


def main():
    doc = RF.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(CASE + " report built. Set 'Write the report' to True, then click Open report under the Report.")


if __name__ == "__main__":
    main()
