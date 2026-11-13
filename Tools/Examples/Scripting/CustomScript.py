#! python3
# A custom script: an OpenSees Tcl script of your own, run from Grasshopper with Custom Code and Run Analysis.
#
# The script - a steel portal frame 6 m wide and 4 m tall, under a load on its beam and a push at its top - is
# typed into a panel, a line per item. Custom Code takes it with no model wired in, so the script is the whole
# model; Run Analysis, with "Do not use settings" ticked in its right-click menu, runs it as it stands, adding
# nothing. What the script prints with puts comes back in Run Analysis' log.
#
# Use it as a start whenever you need something of OpenSees that Alpaca4d has no component for: paste your
# own script over this one. To add a few lines to a model built with Alpaca4d's components instead, wire the
# model into Custom Code: its lines go after the model, before Run Analysis adds the recorders and the analysis.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Scripting/CustomScript.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run), in Rhino 7 with _RunPythonScript.
#
# It borrows the building blocks of Structures.py: the grid the components are laid out on, the groups and the
# panels.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
STRUCTURES = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(HERE), "Structures")
if STRUCTURES not in sys.path:
    sys.path.append(STRUCTURES)

import Grasshopper
from Grasshopper.Kernel.Special import GH_Panel
from System.Drawing import RectangleF

import Structures as S
from Structures import Grid, component, inp, out, wire, panel, group, note, RED, BLUE, GREY, PANEL

CUSTOM_CODE = "4D46782D-0ABE-4097-8040-D9607C645165"    # Custom Code
RUN_ANALYSIS = S.RUN_ANALYSIS

# The script: OpenSees' own Tcl, in kN and m. Two HEB 300 columns built in at their feet, an IPE 400 beam
# across their tops, 20 kN/m on the beam and 30 kN pushing the top along x, in ten steps.
TCL = """wipe
model BasicBuilder -ndm 2 -ndf 3

# A portal frame 6 m wide and 4 m tall, in kN and m.
node 1 0.0 0.0
node 2 6.0 0.0
node 3 0.0 4.0
node 4 6.0 4.0
fix 1 1 1 1
fix 2 1 1 1

# HEB 300 columns: A 149.1 cm2, I 25 170 cm4. IPE 400 beam: A 84.46 cm2, I 23 130 cm4. Steel, E 210 GPa.
geomTransf PDelta 1
geomTransf Linear 2
element elasticBeamColumn 1 1 3 0.01491 2.1e8 2.517e-4 1
element elasticBeamColumn 2 2 4 0.01491 2.1e8 2.517e-4 1
element elasticBeamColumn 3 3 4 0.008446 2.1e8 2.313e-4 2

# 20 kN/m down on the beam, 30 kN along x at its left end.
timeSeries Linear 1
pattern Plain 1 1 {
    eleLoad -ele 3 -type -beamUniform -20.0
    load 3 30.0 0.0 0.0
}

constraints Plain
numberer RCM
system BandGeneral
test NormDispIncr 1.0e-8 10
algorithm Newton
integrator LoadControl 0.1
analysis Static
analyze 10

# What came out: puts prints to the log of Run Analysis.
reactions
puts "Sway at the top: [format %.2f [expr 1000.0 * [nodeDisp 3 1]]] mm"
puts "Base shear: [format %.1f [expr -([nodeReaction 1 1] + [nodeReaction 2 1])]] kN"
puts "Vertical reactions: [format %.1f [nodeReaction 1 2]] and [format %.1f [nodeReaction 2 2]] kN"
puts "Moments at the feet: [format %.1f [nodeReaction 1 3]] and [format %.1f [nodeReaction 2 3]] kNm"
wipe"""


def script_panel(grid, name, value, width, height, col, row=None):
    """A panel holding a script, each of its lines an item of its own, as Custom Code takes it."""
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = name
    p.Properties.Colour = PANEL
    p.Properties.Multiline = False
    p.SetUserText(value)
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    return grid.put(p, col, row)


def custom_script(doc):
    grid = Grid(doc)
    script = script_panel(grid, "OpenSees script", TCL, 520, 640, 0)
    code = component(grid, CUSTOM_CODE, "Custom Code", "Your own script", 1)
    wire(inp(code, 1), script)
    run = component(grid, RUN_ANALYSIS, "Run Analysis", "Run it as it stands", 2)
    wire(inp(run, 0), out(code))
    # "Do not use settings", ticked: the script has its own analysis, and Run Analysis adds none.
    impl = getattr(run, "__implementation__", run)
    impl.GetType().GetProperty("_settings").SetValue(impl, False)
    log = panel(grid, "What OpenSees printed", out(run, 1), 420, 260, 3)
    tcl = panel(grid, "The script OpenSees ran", out(code, 1), 300, 260, 3, gap=30)
    grid.arrange(300, 300, stages=(1, 3))
    group(doc, "1 - The script, a line per item", [script], RED)
    group(doc, "2 - Custom Code: no model wired in, so the script is the whole model; Run Analysis runs it as it stands",
          [code, run], BLUE)
    group(doc, "3 - What came back", [log, tcl], GREY)
    note(doc, "Custom script",
         "Custom script - an OpenSees Tcl script of your own, run from Grasshopper.\n\n"
         "The script is typed into the panel, a line per item: a steel portal frame 6 m wide and 4 m tall, HEB 300 "
         "columns built in at their feet, an IPE 400 beam, 20 kN/m on the beam and 30 kN pushing its top sideways.\n\n"
         "Custom Code takes it with no model wired in, so the script is the whole model. Run Analysis has "
         "\"Do not use settings\" ticked in its right-click menu: it adds no recorders and no analysis, and runs the "
         "script as it stands. What the script prints with puts comes back in its log.\n\n"
         "Paste your own script over this one to reach anything of OpenSees that Alpaca4d has no component for. To "
         "add a few lines to a model built with Alpaca4d's components instead, wire the model into Custom Code, and "
         "untick \"Do not use settings\": the lines go after the model, before the recorders and the analysis.",
         300, 60, 900, 200)


def build(doc):
    S.make_object = globals().get("make_object")
    custom_script(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Custom script built.")


if __name__ == "__main__":
    main()
