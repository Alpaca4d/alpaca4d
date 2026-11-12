#! python3
# Mesh Loft and Mesh Series to Brick: a concrete barrel vault built of bricks, from polylines.
#
# The vault is 8 m across, 2.5 m high, 6 m long and 400 mm thick. A GhPython component (MeshLoftBricksGeometry.py,
# next to this script) draws it as polylines: three surfaces of it - its inside, its middle and its outside -
# each a row of polylines across the vault, all with the same number of points. Mesh Loft lofts each surface's
# polylines into a mesh of quads; the three meshes match face for face, so Mesh Series to Brick turns each pair
# of neighbours into a layer of bricks. SSP Bricks of C30/37 then carry the vault's own weight down to its feet.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Solids/MeshLoftBricks.py
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
from Grasshopper.Kernel.Types import GH_Colour

import Structures as S
from Structures import (component, inp, out, wire, flat, number, words, text, panel, colours, menu_setting,
                        show_result, Structure, GEOMETRY_, SECTIONS, MEMBERS, LOADS, MAIN, LOADED, SHOWN, VIEWS,
                        LEGENDS, V_MODEL, V_COLOURS, R_VALUES, R_COLORS, L_TITLE, L_VALUES, L_COLORS, L_SCALE,
                        AT_REST, LIGHT_BLUE, PURPLE_, ORANGE_, PALE_BLUE)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "MeshLoftBricksGeometry.py")

MESH_LOFT = "7646895B-AF39-4BA3-91C2-3E5C00B21DCD"          # Mesh Loft
MESH_TO_BRICK = "95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9"      # Mesh Series to Brick
BRICK = "FDFC5598-7AA8-45CF-AE80-2C9996564EB2"              # SSP Brick
# Model View's results, from 1: Brick stresses is the sixth, Von Mises the seventh of its components.
BRICK_STRESSES, VON_MISES = 6, 6


def vault(doc):
    s = Structure(doc, "Vault of bricks", "layers", [
        ("Span", "Span [m]", 4, 16, 8, 1),
        ("Rise", "Rise [m]", 1, 6, 2.5, 1),
        ("Length", "Length [m]", 2, 16, 6, 1),
        ("Thickness", "Thickness [m]", 0.2, 1, 0.4, 2),
        ("Around", "Bricks round the arc", 8, 48, 24, 0),
        ("Along", "Bricks along it", 2, 32, 12, 0),
        # Five layers through the thickness at least, so the stress can vary across it (user, 2026-11-12).
        ("Layers", "Layers of bricks", 1, 10, 5, 0),
    ], [])
    s.geometry(["Layers", "Feet", "Info"])
    s.geo.Hidden = True
    info = panel(s.grid, "About the vault", s.g("Info"), 260, 40, GEOMETRY_, MAIN, gap=30)
    s.parts["1"].append(info)

    # 2 - Mesh Loft: each branch of polylines, one surface of the vault, lofted into a mesh. Mesh Series to
    # Brick: the meshes, inside to outside, a layer of bricks between each two.
    loft = component(s.grid, MESH_LOFT, "Mesh Loft", "A mesh a surface", SECTIONS, MAIN, gap=30)
    wire(inp(loft, 0), s.g("Layers"))
    loft.Hidden = True
    bricks = component(s.grid, MESH_TO_BRICK, "Mesh Series to Brick", "Bricks", SECTIONS, MAIN, gap=30)
    wire(flat(inp(bricks, 0)), out(loft))
    bricks.Hidden = True
    concrete = component(s.grid, S.ND_MATERIAL, "nD", "C30/37", SECTIONS, MAIN, gap=30)
    words(inp(concrete, 0), "C30/37")
    number(inp(concrete, 1), 33e6)
    number(inp(concrete, 2), 33e6 / 2.4)
    number(inp(concrete, 3), 0.2)
    number(inp(concrete, 4), 2500)
    inp(concrete, 5).PersistentData.Append(GH_Colour(AT_REST))
    solid = component(s.grid, BRICK, "SSP Brick", "Vault", MEMBERS, MAIN, gap=30)
    wire(inp(solid, 0), out(bricks))
    wire(inp(solid, 1), out(concrete))
    s.members.append(solid)
    s.parts["2"] += [loft, bricks, concrete, solid]

    # 3 - The feet: every node along both springings held; a brick's node has no rotations to hold.
    s.support("Springings", "Feet", (True, True, True, False, False, False))

    # 4 - Its own weight.
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Own weight", LOADS, LOADED, gap=30)
    s.parts["4"].append(weight)
    s.pattern("Own weight", [weight])

    # 5 - Analysis.
    run = s.assemble()

    # 6 - The vault, deformed, coloured by Von Mises stress in MPa.
    view = component(s.grid, S.MODEL_VIEW, "Model View", "Von Mises", VIEWS, SHOWN, gap=30)
    wire(inp(view, V_MODEL), out(run))
    colours(inp(view, V_COLOURS), [PALE_BLUE, LIGHT_BLUE, PURPLE_, ORANGE_], 9)
    menu_setting(view, "_ckDeformed", "Active", True)
    # The slider's top first: a value past it is cut back to it.
    menu_setting(view, "_slDeformScale", "MaxValue", 5000.0)
    menu_setting(view, "_slDeformScale", "Value", 2000.0)
    menu_setting(view, "_ckReleases", "Active", False)
    # Stresses read in MPa, Model View's own unit for them.
    show_result(view, BRICK_STRESSES, VON_MISES)
    caption = text(s.grid, "Legend title", "Von Mises [MPa]", LEGENDS, SHOWN, gap=30)
    legend = component(s.grid, S.LEGEND, "Legend", "Vault legend", LEGENDS, SHOWN, gap=30)
    wire(inp(legend, L_TITLE), caption)
    wire(inp(legend, L_VALUES), out(view, R_VALUES))
    wire(inp(legend, L_COLORS), out(view, R_COLORS))
    number(inp(legend, L_SCALE), 2.4)
    s.parts["6"] += [view, caption, legend]

    s.finish({"1": "Geometry: the vault as polylines, a branch a surface, inside to outside",
              "2": "Bricks: Mesh Loft a mesh a surface, Mesh Series to Brick a layer between each two, SSP Bricks of C30/37",
              "3": "Supports: every node of both springings held",
              "4": "Loads: its own weight",
              "5": "Analysis",
              "6": "Results: Von Mises stress on the vault, deformed 2000 times"},
             "Vault of bricks - Mesh Loft and Mesh Series to Brick, from polylines to a solid.\n\n"
             "A concrete barrel vault 8 m across, 2.5 m high, 6 m long and 400 mm thick. The GhPython component draws "
             "it as polylines: Layers + 1 surfaces through its thickness, inside to outside, each a branch of "
             "polylines across the vault, one every brick along it, all with the same number of points - Mesh Loft "
             "needs that.\n\n"
             "Mesh Loft lofts each branch into one mesh of quads. The meshes match face for face, so Mesh Series to "
             "Brick, taking them in order, makes a layer of bricks between each two: hexahedra, eight nodes each. "
             "SSP Brick turns them into elements of C30/37; both springings are held, every node along them.\n\n"
             "Under its own weight the vault pushes outwards at its feet, and Model View colours it by Von Mises "
             "stress, drawn 2000 times its deformation: it works in compression, the stress highest along the "
             "feet, where its weight and its thrust meet the ground.\n\n"
             "Try: Rise 4 m, Layers of bricks 8, Thickness 0.25 m, and see how the stress moves.")


def build(doc):
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    vault(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Vault of bricks built.")


if __name__ == "__main__":
    main()
