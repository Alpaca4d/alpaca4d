#! python3
# A free-form surface with holes, meshed by Mesh Surface: a doubly curved NURBS surface with a round hole and a
# rounded slot cut through it. The mesh has a node exactly on each of two points, a line of nodes along two
# curves drawn on the surface, and quads that shrink round the holes, where Mesh Refinement asks.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Meshing/FreeFormWithHoles.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this
# file and press Run), in Rhino 7 with _RunPythonScript. Mesh Surface runs gmsh, which each user installs.
#
# It borrows the building blocks of Structures.py: the grid the components are laid out on, the groups and the
# sliders.
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
from Structures import component, inp, out, wire, panel, Structure, SECTIONS, MEMBERS, MAIN

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "FreeFormWithHolesGeometry.py")

MESH_SURFACE = "D4A97F12-3E6C-4B85-8F21-9B0E5C7A2D48"       # Mesh Surface
MESH_REFINEMENT = "3F6B2C1E-8D4A-4E7B-9C2F-5A1D7E8B4C63"    # Mesh Refinement


def freeform(doc):
    s = Structure(doc, "Free-form surface with holes", "freeform", [
        ("Length", "Length [m]", 3, 12, 6, 1),
        ("Width", "Width [m]", 2, 8, 4, 1),
        ("Rise", "Rise [m]", 0, 2, 0.8, 2),
        ("Hole", "Round hole across [m]", 0.2, 1.6, 1.0, 2),
        ("Slot", "Slot length [m]", 0.3, 2.0, 1.2, 2),
    ], [
        ("MeshSize", "Mesh size [m]", 0.05, 0.5, 0.2, 2),
        ("HoleSize", "Mesh size at the holes [m]", 0.01, 0.2, 0.05, 2),
    ])
    s.geometry(["Surface", "Points", "Curves", "Edges"])

    # 2 - Mesh Surface: quads of Mesh size, a node on each point, a line of nodes along each curve, and the
    # quads shrinking to Mesh size at the holes along the holes' edges.
    refinement = component(s.grid, MESH_REFINEMENT, "Mesh Refinement", "Along the holes' edges", SECTIONS, MAIN, gap=30)
    wire(inp(refinement, 0), s.g("Edges"))
    wire(inp(refinement, 1), s.sliders["HoleSize"])
    mesher = component(s.grid, MESH_SURFACE, "Mesh Surface", "Quads", MEMBERS, MAIN, gap=30)
    wire(inp(mesher, 0), s.g("Surface"))
    wire(inp(mesher, 1), s.sliders["MeshSize"])
    wire(inp(mesher, 2), out(refinement))
    wire(inp(mesher, 3), s.g("Points"))
    wire(inp(mesher, 4), s.g("Curves"))
    quality = panel(s.grid, "Each element's shape, 1 at best", out(mesher, 1), 200, 120, MEMBERS, MAIN, gap=30)
    s.parts["2"] += [refinement, mesher, quality]

    s.finish({"1": "Geometry: a NURBS surface with a round hole and a slot, two points, two curves on it",
              "2": "Mesh Surface: a node on each point, nodes along each curve, finer at the holes",
              "3": "", "4": "", "5": "", "6": "", "7": ""},
             "Free-form surface with holes - Mesh Surface on a doubly curved NURBS surface.\n\n"
             "The GhPython component builds the surface through a grid of points, cuts a round hole and a rounded "
             "slot through it from above, and gives two points on it, an isocurve across it and a circle drawn on "
             "it, and the holes' edges.\n\n"
             "Mesh Surface meshes it with gmsh in quads of Mesh size: the mesh lies on the NURBS surface, has a node "
             "exactly on each point - a column, a point load - and a line of element edges along each curve - a "
             "beam, a wall. Mesh Refinement makes the quads Mesh size at the holes along the holes' edges, growing "
             "back gradually. A few triangles are left where the quads cannot all pair up.\n\n"
             "Try: Triangles from Mesh Surface's right-click menu; a larger rise; a smaller mesh size at the holes. "
             "Connect the Mesh to ASD Shell for a shell model.")


def build(doc):
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    freeform(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Free-form surface with holes built.")


if __name__ == "__main__":
    main()
