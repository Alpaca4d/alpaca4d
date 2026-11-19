#! python3
# A flat slab with openings, on columns: Mesh Surface meshes it with a node at every column - its inclusion
# points - finer round the columns and along the openings' edges, where Mesh Refinement asks. The column
# reactions are checked against the load the slab carries.
#
# A concrete slab 12 m by 8 m and 250 mm thick stands on twelve columns, 4 m apart each way, and carries
# 10 kN/m2. It has a stair opening 2.4 m by 1.2 m in one bay and a round shaft 1 m across in another. Mesh
# Surface meshes it in quads of 400 mm, which shrink to 100 mm round the columns, where the slab bends most,
# and to 150 mm along the openings' edges.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Meshing/SlabWithOpenings.py
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

import Structures as S
from Structures import (component, inp, out, wire, flat, number, panel, expression, Structure, SECTIONS, WIDTHS,
                        VECTORS, LOADS, VIEWS, LEGENDS, MAIN, LOADED)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "SlabWithOpeningsGeometry.py")

MESH_SURFACE = "D4A97F12-3E6C-4B85-8F21-9B0E5C7A2D48"       # Mesh Surface
MESH_REFINEMENT = "3F6B2C1E-8D4A-4E7B-9C2F-5A1D7E8B4C63"    # Mesh Refinement
REACTION_FORCES = "C86DB16F-84D1-4EF6-80CD-860B0F0B5A7D"    # Reaction Forces
DECONSTRUCT_VECTOR = "A50FCD4A-CF42-4C3F-8616-022761E6CC93" # Vector > Vector > Deconstruct Vector
MASS_ADDITION = "5B850221-B527-4BD6-8C62-E94168CD6EFA"      # Maths > Operators > Mass Addition


def slab(doc):
    s = Structure(doc, "Slab with openings", "slab", [
        ("Length", "Length [m]", 6, 24, 12, 1),
        ("Width", "Width [m]", 4, 16, 8, 1),
        ("BaysX", "Bays along X", 1, 6, 3, 0),
        ("BaysY", "Bays along Y", 1, 4, 2, 0),
        ("Stair", "Stair opening length [m]", 0.5, 3.5, 2.4, 2),
        ("Shaft", "Shaft across [m]", 0.2, 2.5, 1.0, 2),
    ], [
        ("Pressure", "Load [kN/m2]", 0, 30, 10, 1),
        ("Thickness", "Thickness [mm]", 150, 400, 250, 0),
        ("MeshSize", "Mesh size [m]", 0.1, 1.0, 0.4, 2),
        ("ColumnSize", "Mesh size at the columns [m]", 0.02, 0.4, 0.1, 2),
        ("EdgeSize", "Mesh size along the openings [m]", 0.02, 0.4, 0.15, 2),
    ])
    s.geometry(["Slab", "Columns", "Openings", "Area"])

    # 2 - Meshed by Mesh Surface: quads of Mesh size, a node at every column, refined round the columns and
    # along the openings' edges. ASD Shells of concrete, Thickness thick.
    at_columns = component(s.grid, MESH_REFINEMENT, "Mesh Refinement", "Round the columns", SECTIONS, MAIN, gap=30)
    wire(inp(at_columns, 0), s.g("Columns"))
    wire(inp(at_columns, 1), s.sliders["ColumnSize"])
    along_openings = component(s.grid, MESH_REFINEMENT, "Mesh Refinement", "Along the openings", SECTIONS, MAIN, gap=30)
    wire(inp(along_openings, 0), s.g("Openings"))
    wire(inp(along_openings, 1), s.sliders["EdgeSize"])
    mesher = component(s.grid, MESH_SURFACE, "Mesh Surface", "Quads", SECTIONS, MAIN, gap=30)
    wire(inp(mesher, 0), s.g("Slab"))
    wire(inp(mesher, 1), s.sliders["MeshSize"])
    wire(inp(mesher, 2), out(at_columns), out(along_openings))
    wire(inp(mesher, 3), s.g("Columns"))
    s.parts["2"] += [at_columns, along_openings, mesher]
    metres = expression(s.grid, "f(t)=t/1000", [s.sliders["Thickness"]], "Thickness [m]", SECTIONS, MAIN, gap=30)
    s.parts["2"].append(metres)
    shell = s.slabs("Slab", out(mesher), out(metres))

    # 3 - The columns: each holds the node at its top in X, Y and Z, and lets it turn.
    s.support("Columns", "Columns", (True, True, True, False, False, False))

    # 4 - The load, Load kN/m2 down on every shell.
    amount = expression(s.grid, "f(q)=-q", [s.sliders["Pressure"]], "Down [kN/m2]", WIDTHS, LOADED, gap=30)
    down = component(s.grid, S.UNIT_Z, "Unit Z", "Down", VECTORS, LOADED, gap=30)
    wire(inp(down, 0), out(amount))
    load = component(s.grid, S.MESH_LOAD, "Mesh Load", "On the slab", LOADS, LOADED, gap=30)
    wire(flat(inp(load, 0)), out(shell))
    wire(inp(load, 1), out(down))
    s.parts["4"] += [amount, down, load]
    s.pattern("Load", [load])

    # 5 - Analysis. Assemble Model's Tolerance 1 mm: its default, 1 cm, is near the smallest elements.
    run = s.assemble()
    number(inp(s.assembled, 5), 0.001)

    # 6 - The slab deflected, coloured by how far it moves, and the loads.
    s.deformed("Deflection", out(run), 100.0, bands=True)
    s.loads_view()

    # 7 - The check: the columns carry what the slab does, its area times the load.
    reactions = component(s.grid, REACTION_FORCES, "Reaction Forces", None, VIEWS, LOADED, gap=30)
    wire(inp(reactions, 0), out(run))
    parts = component(s.grid, DECONSTRUCT_VECTOR, "Deconstruct Vector", "Up", VIEWS, LOADED, gap=30)
    flat(wire(inp(parts, 0), out(reactions, 1)))
    total = component(s.grid, MASS_ADDITION, "Mass Addition", "All the columns", VIEWS, LOADED, gap=30)
    wire(inp(total, 0), out(parts, 2))
    carried = expression(s.grid, "f(A,q)=A*q", [s.g("Area"), s.sliders["Pressure"]], "Area times load [kN]",
                         LEGENDS, LOADED, gap=30)
    shown_columns = panel(s.grid, "The columns carry [kN]", out(total, 0), 160, 20, LEGENDS, LOADED, gap=30)
    shown_load = panel(s.grid, "Area times load [kN]", out(carried), 160, 20, LEGENDS, LOADED, gap=30)
    s.parts["7"] += [reactions, parts, total, carried, shown_columns, shown_load]

    s.finish({"1": "Geometry: the slab with its openings, the columns, the openings' edges",
              "2": "Concrete: meshed by Mesh Surface with a node at every column, finer round them and the openings",
              "3": "Supports: the columns, pinned",
              "4": "Loads: 10 kN/m2 on the slab",
              "5": "Analysis: Assemble Model's Tolerance 1 mm, below the elements' size",
              "6": "Results: the deflection, 100 times, and the loads",
              "7": "Check: the columns carry the slab's area times the load"},
             "Slab with openings - Mesh Surface with inclusion points and refinement.\n\n"
             "A concrete slab 12 m by 8 m and 250 mm thick stands on twelve columns, 4 m apart each way, and "
             "carries 10 kN/m2. It has a stair opening 2.4 m by 1.2 m in one bay and a round shaft 1 m across in "
             "another.\n\n"
             "Mesh Surface meshes it in quads of Mesh size. The columns are its inclusion points: the mesh has a "
             "node exactly on each, where Support holds it. Two Mesh Refinements make the quads smaller round the "
             "columns, where the slab bends most, and along the openings' edges; Reach is left empty, so they "
             "grow back gradually. A few triangles are left where the quads cannot all pair up; ASD Shell takes "
             "them too.\n\n"
             "The slab sags most in the middle of the bays and beside the openings. The columns together carry "
             "what the slab does, its area without the openings times the load: 92.3 m2 x 10 kN/m2 = 923 kN.\n\n"
             "Try: Mesh size at the columns 0.4 m, the same as the rest, and see the bending round the columns "
             "spread; Triangles, from Mesh Surface's menu; a larger shaft.")


def build(doc):
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    slab(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Slab with openings built.")


if __name__ == "__main__":
    main()
