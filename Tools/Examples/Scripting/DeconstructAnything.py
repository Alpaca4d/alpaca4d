#! python3
# Deconstruct anything: Alpaca4d's Deconstruct component opens any object of Alpaca4d - an element, its section,
# the section's material, a load, a whole model - and gives each of its fields and properties an output of its
# own, named after it.
#
# A steel cantilever 4 m long, an IPE 300 of S355 built in at one end and loaded at the other, is opened up
# piece by piece: the beam, then the section it carries, then the section's material; the point load; and the
# assembled model, then every node in it. A Deconstruct takes one kind of object at a time, a list of them too.
#
# Deconstruct makes its outputs when an object first reaches it. This script makes them as it builds the
# definition, named from the objects' types in Alpaca4d.Core, so that the wires from one Deconstruct to the
# next are already there when the file opens.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Scripting/DeconstructAnything.py
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
from Grasshopper.Kernel.Parameters import Param_GenericObject

import Structures as S
from Structures import (Grid, component, inp, out, wire, flat, number, words, vector, panel, group, note,
                        RED, ORANGE, PURPLE, GREEN, BLUE, GREY)

DECONSTRUCT = "1E68B376-F7AA-4C70-BE45-FBCD1BD9B356"   # Deconstruct
LINE = "4C4E56EB-2F04-43F9-95A3-CC46A14F495A"          # Curve > Primitive > Line

# Columns of the grid, in the order the data flows, and its rows.
INPUTS, MAKE, ELEMENTS, MODEL, FIRST, SECOND, THIRD = range(7)
BEAM_ROW, LOAD_ROW, MODEL_ROW = range(3)


def core_type(name):
    """A type of Alpaca4d.Core, by its full name: the assembly is loaded once a component of the plug-in is."""
    for assembly in System.AppDomain.CurrentDomain.GetAssemblies():
        if assembly.GetName().Name == "Alpaca4d.Core":
            t = assembly.GetType(name)
            if t is not None:
                return t
    raise Exception("Alpaca4d.Core has no " + name)


def deconstruct(grid, source, type_name, nickname, col, row):
    """A Deconstruct with source wired in and the outputs it makes for an object of type_name already made:
    Object Class, then one a field and one a property, in the order the component takes them. Returns it, its
    outputs by name, and their names in order."""
    d = component(grid, DECONSTRUCT, "Deconstruct", nickname, col, row, gap=30)
    wire(inp(d, 0), source)
    t = core_type(type_name)
    flags = System.Reflection.BindingFlags
    fields = t.GetFields(flags.Instance | flags.Public | flags.GetField)
    properties = t.GetProperties(flags.Instance | flags.Public | flags.GetProperty)
    names = ["Object Class"] + [f.Name for f in fields] + [p.Name for p in properties]
    outputs = {}
    for name in names:
        p = Param_GenericObject()
        p.Name = p.NickName = name
        d.Params.RegisterOutputParam(p)
        outputs[name] = p
    d.Params.OnParametersChanged()
    d.Attributes.ExpireLayout()
    d.Attributes.PerformLayout()
    return d, outputs, names


def show(grid, d, name, col, row):
    """A small panel on one of a Deconstruct's outputs."""
    return panel(grid, name, d[1][name], 160, 40, col, row, gap=12)


def cantilever(doc):
    grid = Grid(doc)

    # 1 - A cantilever 4 m long along x, an IPE 300 of S355, built in at the origin, 10 kN down at its tip.
    root = component(grid, S.CONSTRUCT_POINT, "Construct Point", "Root", INPUTS, BEAM_ROW, gap=30)
    tip = component(grid, S.CONSTRUCT_POINT, "Construct Point", "Tip", INPUTS, BEAM_ROW, gap=30)
    number(inp(tip, 0), 4.0)
    material = component(grid, S.MATERIAL, "Material Library Elastic", "S355", INPUTS, BEAM_ROW, gap=30)
    words(inp(material, 0), "S355")
    line = component(grid, LINE, "Line", "Cantilever", MAKE, BEAM_ROW, gap=30)
    wire(inp(line, 0), out(root))
    wire(inp(line, 1), out(tip))
    section = component(grid, S.SECTION, "Steel Section Library", "IPE300", MAKE, BEAM_ROW, gap=30)
    words(inp(section, 0), "IPE300")
    wire(inp(section, 1), out(material))
    beam = component(grid, S.BEAM, "ForceBeamColumn", "Cantilever", ELEMENTS, BEAM_ROW, gap=30)
    wire(inp(beam, 0), out(line))
    wire(inp(beam, 1), out(section))
    support = component(grid, S.SUPPORT, "Support", "Built in", ELEMENTS, BEAM_ROW, gap=30)
    wire(inp(support, 0), out(root))
    load = component(grid, S.POINT_LOAD, "Point Load", "10 kN at the tip", ELEMENTS, LOAD_ROW, gap=30)
    wire(inp(load, 0), out(tip))
    vector(inp(load, 1), 0.0, 0.0, -10.0)
    pattern = component(grid, S.LOAD_PATTERN, "Load Pattern", "Tip load", MODEL, LOAD_ROW, gap=30)
    wire(flat(inp(pattern, 1)), out(load))
    model = component(grid, S.ASSEMBLE, "Assemble Model", "Cantilever", MODEL, MODEL_ROW, gap=30)
    wire(flat(inp(model, 0)), out(beam))
    wire(flat(inp(model, 1)), out(support))
    wire(flat(inp(model, 2)), out(pattern))

    # 2 - The beam opened, then the section it carries, then the section's material.
    d_beam = deconstruct(grid, out(beam), "Alpaca4d.Element.ForceBeamColumn", "The beam", FIRST, BEAM_ROW)
    d_section = deconstruct(grid, d_beam[1]["Section"], "Alpaca4d.Section.ISection", "Its section", SECOND, BEAM_ROW)
    d_material = deconstruct(grid, d_section[1]["Material"], "Alpaca4d.Material.UniaxialMaterialElastic",
                             "Its material", THIRD, BEAM_ROW)
    beam_panels = [show(grid, d_section, name, SECOND, BEAM_ROW) for name in ("Area", "Iyy", "Izz") if name in d_section[1]]
    material_panels = [show(grid, d_material, name, THIRD, BEAM_ROW) for name in ("E", "Rho") if name in d_material[1]]

    # 3 - The load opened.
    d_load = deconstruct(grid, out(load), "Alpaca4d.Loads.PointLoad", "The load", FIRST, LOAD_ROW)

    # 4 - The model opened, then every node in it: a Deconstruct takes a list of one kind of object as well.
    d_model = deconstruct(grid, out(model), "Alpaca4d.Model", "The model", FIRST, MODEL_ROW)
    d_nodes = deconstruct(grid, d_model[1]["Nodes"], "Alpaca4d.Element.Node", "Its nodes", SECOND, MODEL_ROW)
    node_panels = [show(grid, d_nodes, name, THIRD, MODEL_ROW) for name in d_nodes[2][1:4]]

    grid.arrange(300, 300, stages=(MAKE, FIRST))
    group(doc, "1 - A cantilever: IPE 300 of S355, 4 m, built in, 10 kN at its tip",
          [root, tip, material, line, section, beam, support], RED)
    group(doc, "2 - The load and the model", [load, pattern, model], GREEN)
    group(doc, "3 - The beam opened, then its section, then the section's material",
          [d_beam[0], d_section[0], d_material[0]] + beam_panels + material_panels, ORANGE)
    group(doc, "4 - The load opened", [d_load[0]], PURPLE)
    group(doc, "5 - The model opened, then every node in it", [d_model[0], d_nodes[0]] + node_panels, BLUE)
    note(doc, "Deconstruct anything",
         "Deconstruct anything - look inside any object of Alpaca4d.\n\n"
         "Deconstruct takes an object of Alpaca4d - an element, a section, a material, a load, a model, a result - "
         "and gives each of its fields and properties an output of its own, named after it. The first, Object "
         "Class, says what the object is.\n\n"
         "Here a cantilever, an IPE 300 of S355 4 m long, built in and loaded at its tip, is opened up piece by "
         "piece: the beam, then the section it carries, then the section's material, each Deconstruct fed from the "
         "one before; the point load; and the assembled model, then every node in it - one Deconstruct takes a "
         "list of the same kind of object too.\n\n"
         "Use it to check what reached a component, to read a number Alpaca4d keeps but shows nowhere else, or to "
         "pick objects out for filters and views of your own. Plug something else into any of them: its outputs "
         "change to fit, once it is solved.",
         300, 60, 900, 170)


def build(doc):
    S.make_object = globals().get("make_object")
    cantilever(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Deconstruct anything built.")


if __name__ == "__main__":
    main()
