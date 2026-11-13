#! python3
# A multi-storey building: a reinforced concrete frame with a slab on every floor - the whole workflow on a
# building, from the grid to its loads, deflections and natural modes.
#
# Five storeys of 3.5 m on a grid of five bays of 6 m by three of 5 m: a column at every crossing of the grid,
# a beam along every line of it at every floor and a 200 mm slab on the beams. The geometry comes from a
# GhPython component (MultiStoreyBuildingGeometry.py, next to this script); the rest is Alpaca4d's components.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Structures/MultiStoreyBuilding.py
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

import Structures as S
from Structures import (component, inp, out, wire, flat, integer, expression, panel, python_component, Structure,
                        GEOMETRY_, WIDTHS, VECTORS, LOADS, MAIN, LOADED, ANALYSIS, UNIT_X, UNIT_Z, RECTANGLE)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "MultiStoreyBuildingGeometry.py")

# Its displacements in a jet, dark blue for still to dark red for moving the most, as the earthquakes have them.
S.SCALES["office"] = S.JET
MODES = 6


def floor_load(s, name, shells, pressure):
    """A Mesh Load on the slabs: a pressure slider, in kN/m2, down."""
    amount = expression(s.grid, "f(q)=-q", [s.sliders[pressure]], name + " [kN/m2]", WIDTHS, LOADED, gap=30)
    down = component(s.grid, UNIT_Z, "Unit vector", name, VECTORS, LOADED, gap=30)
    wire(inp(down, 0), out(amount))
    load = component(s.grid, S.MESH_LOAD, "Mesh Load", name, LOADS, LOADED, gap=30)
    wire(flat(inp(load, 0)), out(shells))
    wire(flat(inp(load, 1)), out(down))
    s.parts["4"] += [amount, down, load]
    return load


def office(doc):
    s = Structure(doc, "Multi-storey building", "building", [
        ("BaysX", "Bays along x", 1, 8, 5, 0),
        ("BaysY", "Bays along y", 1, 6, 3, 0),
        ("BayX", "Bay along x [m]", 4, 9, 6, 1),
        ("BayY", "Bay along y [m]", 4, 9, 5, 1),
        ("Storeys", "Storeys", 1, 12, 5, 0),
        ("StoreyHeight", "Storey height [m]", 2.8, 4.5, 3.5, 1),
        ("Split", "Slab quads a bay", 2, 6, 4, 0),
    ], [
        ("Finishes", "Finishes [kN/m2]", 0, 3, 1.5, 2),
        ("Imposed", "Imposed load [kN/m2]", 0, 5, 3.0, 2),
        ("Wind", "Wind [kN/m2]", 0, 2, 0.8, 2),
    ])
    s.geometry(["Columns", "WindwardColumns", "WindWidths", "Beams", "Slabs", "Feet", "Info"])
    # Model View draws the building; the component's own preview would draw its lines and meshes over it.
    s.geo.Hidden = True
    info = panel(s.grid, "About the building", s.g("Info"), 260, 60, GEOMETRY_, MAIN, gap=30)
    s.parts["1"].append(info)

    # 2 - The members: C30/37 columns 400 x 400 and beams 300 x 500, and 200 mm slabs as ASD Shells.
    concrete = s.concrete()
    column = s.section(RECTANGLE, "Column 400 x 400", concrete, 0.4, 0.4)
    beam = s.section(RECTANGLE, "Beam 300 x 500", concrete, 0.3, 0.5)
    s.elements("Columns", "Columns", column)
    windward = s.elements("Windward columns", "WindwardColumns", column)
    s.elements("Beams", "Beams", beam)
    slabs = s.slabs("Slabs", "Slabs", 0.2)

    # 3 - Every column built in at the ground.
    s.support("Built in", "Feet", (True,) * 6)

    # 4 - The loads, as they are, not factored: its own weight and the finishes, people on every floor, and the
    # wind on the face at x = 0, taken by its columns over the width of wall each carries.
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    finishes = floor_load(s, "Finishes", slabs, "Finishes")
    imposed = floor_load(s, "Imposed", slabs, "Imposed")
    wind = s.line_load("Wind", [windward], UNIT_X, 1, "Wind", s.g("WindWidths"))
    s.pattern("Permanent", [weight, finishes])
    s.pattern("Imposed", [imposed])
    s.pattern("Wind", [wind])

    # 5 - Analysis: static, every pattern at once; and its natural modes, from the members' and slabs' own mass.
    run = s.assemble()
    modes = component(s.grid, S.NATURAL_VIBRATION, "Natural Vibration Analysis", None, ANALYSIS, MAIN, gap=30)
    wire(inp(modes, 0), out(s.assembled))
    integer(inp(modes, 1), MODES)
    frequencies = panel(s.grid, "Frequencies [Hz]", out(modes, 3), 160, 120, ANALYSIS, MAIN, gap=30)
    s.parts["5"] += [modes, frequencies]

    # 6 - Results.
    # Without its loads: on every slab quad they would hide the slabs' colours. The loads view has them.
    deformed = s.deformed("Building deformed", out(run), 200, loads=False, palette="office", bands=True)
    first = s.deformed("First mode", out(modes), 1, step=0, title="First mode", loads=False, palette="mode")
    first.Hidden = True
    # The slabs' element edges drawn, as on any meshed model: deformed() leaves them off for thin members.
    for view in (deformed, first):
        S.menu_setting(view, "_ckWires", "Active", True)
    s.loads_view()

    s.finish({"1": "Geometry: a grid of bays, storeys, and a slab a floor meshed onto the beams",
              "2": "Members: C30/37 columns 400 x 400, beams 300 x 500, 200 mm slabs as ASD Shells",
              "3": "Supports: every column built in at the ground",
              "4": "Loads: own weight and finishes, people on the floors, wind on one face",
              "5": "Analysis: static, and the first six natural modes",
              "6": "Results: the deformed shape, drawn 200 times, the first mode and the loads"},
             "Multi-storey building - a reinforced concrete frame with a slab on every floor.\n\n"
             "A column at every crossing of the grid, built in at the ground; a beam along every line of the grid at "
             "every floor; a 200 mm slab on the beams, as ASD Shells. The slab is meshed Slab quads a bay times a "
             "bay each way and the beams are cut at the same points, so that slab and beams share their nodes and "
             "bend together.\n\n"
             "The loads are typed in kN/m2 and are as they are, not factored, for the deflections: the finishes and "
             "the people on every slab, as Mesh Loads, and the wind on the face at x = 0, which its columns take "
             "over the width of wall each carries. Factor the Load Patterns for the ultimate limit state.\n\n"
             "Run Analysis solves all three patterns at once; Natural Vibration Analysis gives the first six modes, "
             "from the concrete's own mass alone - the finishes and people are loads, not masses, here. Model "
             "View draws the building deformed, 200 times larger, coloured by how far it moves, and the first "
             "mode - turn its preview on.\n\n"
             "Try: Storeys up to 10, and see the first frequency fall; Bays along y down to 1, and see which way "
             "the first mode sways it; Wind up to 1.5 kN/m2.")


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code from its
    # GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    office(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Multi-storey building built.")


if __name__ == "__main__":
    main()
