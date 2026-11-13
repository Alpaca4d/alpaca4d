#! python3
# Connection design: the cast steel node of a tree column, modelled in shells, to see how the stress flows
# through it and where it gathers.
#
# A trunk tube 323.9 mm across splits at a steel sphere into three branches 219.1 mm across, each leaning 35
# degrees out. A GhPython component (ConnectionDesignGeometry.py, next to this script) joins the sphere and the
# tubes into one surface, open at the tube ends; Mesh Brep meshes it in quads about 40 mm across, keeping the
# weld lines as edges; ASD Shells of steel 25 mm thick are the node. The trunk's end is held; each branch's end
# carries the roof it holds up, shared out over the nodes round it.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Design/ConnectionDesign.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run), in Rhino 7 with _RunPythonScript.
#
# It borrows the building blocks of Structures.py: the grid the components are laid out on, the groups, the
# sliders and the views.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
STRUCTURES = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(HERE), "Structures")
if STRUCTURES not in sys.path:
    sys.path.append(STRUCTURES)

import Grasshopper
from Grasshopper.Kernel import GH_ParamAccess
from Grasshopper.Kernel.Types import GH_Colour
from Grasshopper.Kernel.Parameters.Hints import GH_MeshHint, GH_PlaneHint

import Structures as S
from Structures import (component, inp, out, wire, flat, number, words, text, panel, colours, menu_setting,
                        show_result, python_component, Structure, SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, WIDTHS,
                        LOADS, MAIN, LOADED, SHOWN, VIEWS, LEGENDS, V_MODEL, V_COLOURS, R_VALUES, R_COLORS, L_TITLE,
                        L_VALUES, L_COLORS, L_SCALE, AT_REST, LIGHT_BLUE, PURPLE_, ORANGE_, PALE_BLUE)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "ConnectionDesignGeometry.py")

MESH_BREP = "E0121E43-9C8D-4FC5-A273-50FE088F5E43"          # Mesh Brep
# Model View's results, from 1: Shell stresses is the fifth, Von Mises the sixth of its components.
SHELL_STRESSES, VON_MISES = 5, 5


def connection(doc):
    s = Structure(doc, "Tree column node", "node", [
        ("TrunkDiameter", "Trunk diameter [m]", 0.2, 0.6, 0.3239, 4),
        ("TrunkLength", "Trunk length [m]", 0.4, 2, 0.8, 2),
        ("BranchDiameter", "Branch diameter [m]", 0.1, 0.5, 0.2191, 4),
        ("BranchLength", "Branch length [m]", 0.4, 2, 0.8, 2),
        ("Lean", "Branch lean [deg]", 10, 60, 35, 0),
        ("Branches", "Branches", 2, 5, 3, 0),
        ("Sphere", "Sphere diameter [m]", 0.3, 1, 0.5, 2),
    ], [
        ("Load", "Load on a branch [kN]", 0, 1000, 250, 0),
        ("Sideways", "Sideways on a branch [kN]", 0, 200, 25, 0),
        ("Thickness", "Wall thickness [mm]", 6, 40, 25, 0),
        ("EdgeLength", "Mesh size [m]", 0.02, 0.1, 0.04, 3),
    ])
    s.geometry(["Surface", "Ends"])
    s.geo.Hidden = True

    # 2 - The steel: the surface meshed in quads, ASD Shells of S355, Wall thickness thick.
    mesher = component(s.grid, MESH_BREP, "Mesh Brep", "Quads", SECTIONS, MAIN, gap=30)
    wire(inp(mesher, 0), s.g("Surface"))
    wire(inp(mesher, 1), s.sliders["EdgeLength"])
    mesher.Hidden = True
    steel = component(s.grid, S.ND_MATERIAL, "nD", "S355", SECTIONS, MAIN, gap=30)
    words(inp(steel, 0), "S355")
    number(inp(steel, 1), 210e6)
    number(inp(steel, 2), 81e6)
    number(inp(steel, 3), 0.3)
    number(inp(steel, 4), 7850)
    inp(steel, 5).PersistentData.Append(GH_Colour(AT_REST))
    metres = S.expression(s.grid, "f(t)=t/1000", [s.sliders["Thickness"]], "Thickness [m]", SECTIONS, MAIN, gap=30)
    plate = component(s.grid, S.PLATE_FIBER, "Plate Fiber Section", "Wall", SECTIONS, MAIN, gap=30)
    wire(inp(plate, 1), out(metres))
    wire(inp(plate, 2), out(steel))
    shell = component(s.grid, S.SHELL, "ASD Shell", "Node", MEMBERS, MAIN, gap=30)
    wire(inp(shell, 0), out(mesher))
    wire(inp(shell, 1), out(plate))
    s.members.append(shell)
    s.parts["2"] += [mesher, steel, metres, plate, shell]

    # 3 and 4 - The nodes at each tube's end: the trunk's held, built in; the branches' loaded.
    tolerance = S.expression(s.grid, "f(e)=e/4", [s.sliders["EdgeLength"]], "Within [m]", WIDTHS, LOADED, gap=30)
    as_float = s.as_float
    found = python_component(s.grid, "Tube ends", "ends",
                             [("Mesh", GH_MeshHint()), ("Ends", GH_PlaneHint(), GH_ParamAccess.list),
                              ("Load", as_float), ("Sideways", as_float), ("Tolerance", as_float)],
                             ["Held", "Loaded", "Forces", "Info"], WIDTHS, LOADED)
    wire(inp(found, 0), out(mesher))
    wire(inp(found, 1), s.g("Ends"))
    wire(inp(found, 2), s.sliders["Load"])
    wire(inp(found, 3), s.sliders["Sideways"])
    wire(inp(found, 4), out(tolerance))
    found.Hidden = True
    info = panel(s.grid, "About the ends", out(found, 4), 260, 40, WIDTHS, LOADED, gap=30)
    support = component(s.grid, S.SUPPORT, "Support", "Trunk built in", MEMBERS, LOADED, gap=30)
    wire(inp(support, 0), out(found, 1))
    s.supports.append(support)
    s.parts["3"].append(support)
    load = component(s.grid, S.POINT_LOAD, "Point Load", "The roof on the branches", LOADS, LOADED, gap=30)
    wire(inp(load, 0), out(found, 2))
    wire(inp(load, 1), out(found, 3))
    s.parts["4"] += [tolerance, found, info, load]
    s.pattern("Roof", [load])

    # 5 - Analysis.
    run = s.assemble()

    # 6 - The node deformed, coloured by Von Mises stress in MPa, on the shells' middle surface.
    view = component(s.grid, S.MODEL_VIEW, "Model View", "Von Mises", VIEWS, SHOWN, gap=30)
    wire(inp(view, V_MODEL), out(run))
    colours(inp(view, V_COLOURS), [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_], 9)
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_slDeformScale", "Value", 50.0)
    menu_setting(view, "_ckReleases", "Active", False)
    menu_setting(view, "_ckShowSupports", "Active", False)
    show_result(view, SHELL_STRESSES, VON_MISES)
    caption = text(s.grid, "Legend title", "Von Mises [MPa]", LEGENDS, SHOWN, gap=30)
    legend = component(s.grid, S.LEGEND, "Legend", "Node legend", LEGENDS, SHOWN, gap=30)
    wire(inp(legend, L_TITLE), caption)
    wire(inp(legend, L_VALUES), out(view, R_VALUES))
    wire(inp(legend, L_COLORS), out(view, R_COLORS))
    number(inp(legend, L_SCALE), 2.4)
    s.parts["6"] += [view, caption, legend]
    s.loads_view()

    s.finish({"1": "Geometry: a sphere and the tubes welded to it, joined, open at the tube ends",
              "2": "Steel: meshed in quads by Mesh Brep, ASD Shells of S355",
              "3": "Supports: every node round the trunk's end built in",
              "4": "Loads: each branch's load shared over the nodes round its end",
              "5": "Analysis",
              "6": "Results: Von Mises stress, deformed 50 times, and the loads"},
             "Tree column node - a cast steel node in shells, to see how the stress flows through a connection.\n\n"
             "A trunk tube 323.9 mm across splits at a steel sphere 500 mm across into three branches 219.1 mm "
             "across, each leaning 35 degrees out. The GhPython component joins the sphere and the tubes into one "
             "solid and cuts away the tubes' flat ends: what is left is the steel's surface, its welds kept as edges. "
             "Mesh Brep meshes it in quads of about Mesh size, and ASD Shells of S355, Wall thickness thick, are the "
             "node.\n\n"
             "A second GhPython component finds the mesh's nodes round the end of each tube. The trunk's are "
             "built in. Each branch carries Load on a branch kN down and Sideways on a branch kN along +x - the "
             "roof it holds up - shared out over the nodes round its end.\n\n"
             "Model View colours the node by Von Mises stress at the shells' top face - its Layer menu picks top, "
             "middle or bottom - drawn 50 times its deformation: the stress gathers where the branches meet the "
             "sphere, on the side facing the other branches, and less where the trunk does.\n\n"
             "Try: Mesh size 0.02 m, and see how the peak at the welds rises as the mesh refines - a sign of a "
             "stress concentration, which a design check reads at a distance from the weld; Branch lean 50 degrees; "
             "Wall thickness 16 mm, and see the peak pass the steel's yield.")


def build(doc):
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    connection(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Tree column node built.")


if __name__ == "__main__":
    main()
