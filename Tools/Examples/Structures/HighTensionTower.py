#! python3
# A high-tension tower: a steel lattice pylon carrying four conductors and an earth wire, under its
# own weight, the wires' weight and the wind.
#
# A square body tapers from feet 8 m apart to a shaft 2.4 m wide, 22 m up; the shaft rises to 42 m. Two
# pairs of arms 7 m long carry the conductors, one each side, and a shorter pair at the top the earth wire.
# The geometry comes from a GhPython component (HighTensionTowerGeometry.py, next to this script); the rest
# is Alpaca4d's components.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Structures/HighTensionTower.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run), in Rhino 7 with _RunPythonScript.
#
# It borrows the building blocks of Structures.py, next to it: the grid the components are laid out on, the
# groups, the sliders and the views.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
if HERE not in sys.path:
    sys.path.append(HERE)

import Grasshopper
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS

import Structures as S
from Structures import (component, inp, out, wire, flat, number, words, text, panel, colours, menu_setting,
                        show_result, python_component, Structure, SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, LOADS,
                        LOADED, MAIN, SHOWN, VIEWS, LEGENDS, ANALYSIS, V_MODEL, V_COLOURS, R_VALUES, R_COLORS,
                        L_TITLE, L_VALUES, L_COLORS, L_SCALE, UNIT_X, PURPLE_, ORANGE_, PALE_BLUE)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "HighTensionTowerGeometry.py")

# Its displacements in a jet, dark blue for still to dark red for moving the most, as the earthquakes have them.
S.SCALES["pylon"] = S.JET
# Model View's Results menu: Beam forces is its second result, N the first of them.
BEAM_FORCES, AXIAL = 2, 0
R_INFO = 2                                                # Model View's Info output


def pylon(doc):
    s = Structure(doc, "High-tension tower", "pylon", [
        ("Height", "Height [m]", 30, 60, 42, 1),
        ("Base", "Base width [m]", 4, 12, 8, 1),
        ("Waist", "Shaft width [m]", 1.5, 4, 2.4, 1),
        ("WaistHeight", "Waist height [m]", 10, 30, 22, 1),
        ("Panels", "Panels in the body", 3, 12, 7, 0),
        ("Spacing", "Arm spacing [m]", 3, 9, 6, 1),
        ("Arm", "Arm reach [m]", 4, 10, 7, 1),
    ], [
        ("Weight", "Weight of a conductor [kN]", 0, 50, 20, 1),
        ("Wind", "Wind on a conductor [kN]", 0, 30, 12, 1),
        ("TowerWind", "Wind on the tower [kN/m2]", 0, 2, 1.0, 2),
    ])

    # 1 - The geometry, and the wires' pull on the tips of the arms, from the loads' sliders.
    inputs = s.inputs + [("Weight", s.as_float), ("Wind", s.as_float)]
    outputs = ["Legs", "WindwardLegs", "WindWidths", "Bracing", "Struts", "Chords", "Feet", "WirePoints",
               "WireWeights", "WireWinds", "Info"]
    geo = python_component(s.grid, s.title, "pylon", inputs, outputs, GEOMETRY_, MAIN)
    for k, (name, hint) in enumerate(inputs):
        wire(inp(geo, k), s.sliders[name])
    s.geo, s.outputs = geo, outputs
    # Model View draws the tower; the component's own preview would draw its lines over it.
    geo.Hidden = True
    info = panel(s.grid, "About the tower", s.g("Info"), 280, 60, GEOMETRY_, MAIN, gap=30)
    s.parts["1"] += [geo, info]

    # 2 - The members: steel tubes - pylons are mostly built of single angles, which the section library has
    # only as back-to-back pairs. The legs and the arms' chords as beams, every other bar as a truss.
    s.steel()
    legs = s.member("Legs", "Legs", "193.7x10")
    windward = s.member("Windward legs", "WindwardLegs", "193.7x10")
    chords = s.member("Arm chords", "Chords", "114.3x6.3")
    bracing = s.member("Bracing", "Bracing", "76.1x4", truss=True)
    struts = s.member("Struts", "Struts", "60.3x4", truss=True)

    # 3 - The feet, pinned.
    s.support("Pinned feet", "Feet", (True, True, True, False, False, False))

    # 4 - The loads: the tower's own weight and the wires' weight; the wind on the wires, across the line, and
    # on the tower, on its windward legs - each takes half the face, times the share of it that is steel.
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    hanging = component(s.grid, S.POINT_LOAD, "Point Load", "Wires' weight", LOADS, LOADED, gap=30)
    wire(inp(hanging, 0), s.g("WirePoints"))
    wire(inp(hanging, 1), s.g("WireWeights"))
    blowing = component(s.grid, S.POINT_LOAD, "Point Load", "Wind on the wires", LOADS, LOADED, gap=30)
    wire(inp(blowing, 0), s.g("WirePoints"))
    wire(inp(blowing, 1), s.g("WireWinds"))
    s.parts["4"] += [weight, hanging, blowing]
    tower_wind = s.line_load("Wind on the tower", [windward], UNIT_X, 1, "TowerWind", s.g("WindWidths"))
    s.pattern("Permanent", [weight, hanging])
    s.pattern("Wind", [blowing, tower_wind])

    # 5 - Analysis: one static step, both patterns at once.
    run = s.assemble()

    # 6 - Results: how far it moves, and how hard each member is pulled or pushed.
    s.deformed("Tower deformed", out(run), 40, palette="pylon")
    # Model View draws a beam force as a diagram along each member, red where it is pulled and blue
    # where it is pushed - no colour scale, so no Legend: its Info gives the range, in kN.
    forces = component(s.grid, S.MODEL_VIEW, "Model View", "Axial forces", VIEWS, SHOWN, gap=30)
    wire(inp(forces, V_MODEL), out(run))
    menu_setting(forces, "_ckExtruded", "Active", False)
    menu_setting(forces, "_ckReleases", "Active", False)
    show_result(forces, BEAM_FORCES, AXIAL)
    info = panel(s.grid, "Axial forces", out(forces, R_INFO), 300, 40, LEGENDS, SHOWN, gap=30)
    forces.Hidden = True
    s.parts["6"] += [forces, info]
    s.at_rest()
    s.loads_view()

    s.finish({"1": "Geometry: a body tapering to a shaft, three pairs of arms",
              "2": "Members: steel tubes, the legs and the arms' chords as beams, the bracing as trusses",
              "3": "Supports: each foot pinned",
              "4": "Loads: its own weight and the wires', the wind on the wires and on the tower",
              "5": "Analysis",
              "6": "Results: the deformed shape, drawn 40 times, and the axial forces"},
             "High-tension tower - a steel lattice pylon carrying four conductors and an earth wire.\n\n"
             "A square body tapers from feet 8 m apart to a shaft 2.4 m wide at 22 m, and the shaft rises to "
             "42 m. Two pairs of arms 7 m long carry the conductors, one at each tip, and a shorter pair at the top "
             "the earth wire. The legs and the arms' chords are beams, running unbroken through every node; every "
             "face is crossed by bracing, and struts go round each level - those are trusses, which only pull and "
             "push. The feet are pinned.\n\n"
             "Each conductor hangs Weight of a conductor kN at the tip of its arm - the wire, its insulators and "
             "ice, over half the span either side - and the wind blowing across the line pushes it Wind on a "
             "conductor kN along +x. The earth wire takes half of both. The wind on the tower itself is typed "
             "in kN/m2 and falls on its windward legs, each taking half the face times a quarter of it, the share of "
             "a lattice that is steel.\n\n"
             "Two Model Views: the tower deformed, drawn 40 times larger, coloured by how far it moves; and the "
             "axial force in every member as a diagram along it, red pulled, blue pushed, its range in the panel beside "
             "it - turn its preview on to see it. The "
             "windward legs are pulled, the leeward ones pushed, as the wind tips the tower over.\n\n"
             "Try: Arm reach up to 9 m, and see the arms' chords take more; Base width down to 5 m, and see the "
             "legs work harder; Wind on a conductor at 0, and see what the wires' weight alone does.")


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code from its
    # GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    pylon(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("High-tension tower built.")


if __name__ == "__main__":
    main()
