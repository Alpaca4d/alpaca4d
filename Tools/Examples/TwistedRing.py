#! python3
# A twisted ring truss: the geometry only, in one Python component, for an exercise on the Section
# Optimiser and on multi-objective design. Built on the Grasshopper canvas.
#
# make.sh, next to it, writes TwistedRing.gh straight from this script, without Rhino:
#     ./make.sh TwistedRing.py
# It also runs in Rhino with Grasshopper installed - in Rhino 8 from the ScriptEditor (open this file
# and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
#
# Fourteen sliders and a list - the ring, its profile and twist, its slices and bracing - go into one
# GhPython component, whose code is TwistedRingComponent.py, next to this file. It gives the chords,
# the frames and the bracing, each joined into polylines, and the nodes, how many members and nodes
# there are and how long the members are in all. The definition stops there: the supports, loads and
# the Alpaca4d model are the students' to add.
#
# GhPython, the IronPython component, opens in Rhino 7 and 8 alike, on Mac and Windows; its code runs
# unchanged in Rhino 8's Python 3 component too.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_ParameterSide
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS
from Grasshopper.Kernel.Special import (GH_NumberSlider, GH_Group, GH_Panel, GH_ValueList,
                                        GH_ValueListItem, GH_ValueListMode)
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

PYTHON = "410755B1-224A-4C1E-A407-BF32FB45EA7E"             # Maths > Script > GhPython Script

# The code inside the component, next to this file. System.IO rather than os: make.sh's IronPython has
# no standard library.
CODE = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__)),
                              "TwistedRingComponent.py")

# Grasshopper's own panel yellow: a panel made in code has no colour of its own, and Rhino draws it
# see-through, with white text.
PANEL = Color.FromArgb(255, 255, 250, 90)

# Faint group colours from Alpaca4d's palette, one per stage of the definition.
RED = Color.FromArgb(50, 254, 0, 0)
ORANGE = Color.FromArgb(50, 235, 108, 63)
PURPLE = Color.FromArgb(50, 158, 53, 218)
BLUE = Color.FromArgb(50, 66, 136, 247)

# The drop-down list of bracing: what each kind is called, and the number the code knows it by.
BRACING = [("None", "0"), ("Diagonal", "1"), ("Zigzag", "2"), ("Checkerboard", "3"), ("Cross", "4"), ("K", "5")]

# The component's inputs, in order, in three groups: its name, the slider's name, the slider's range,
# value and decimals - or the list and the item it opens on - and what the input is.
RING = [
    ("RadiusX", "Radius X [m]", (18, 22, 20, 1), "The ring's radius along x, in plan [m]."),
    ("RadiusY", "Radius Y [m]", (18, 22, 20, 1), "The ring's radius along y, in plan [m]: an ellipse when it differs from Radius X."),
    ("WaveHeight", "Wave height [m]", (0, 2, 0, 1), "How far the ring rises and falls [m]."),
    ("Waves", "Waves", (0, 3, 2, 0), "How many times the ring rises and falls on the way round."),
    ("Swell", "Swell [%]", (-50, 50, 0, 0), "How much bigger and smaller the profile grows round the ring [%]."),
    ("Swells", "Swells", (0, 8, 2, 0), "How many times the profile grows and shrinks on the way round."),
]
PROFILE = [
    ("Sides", "Sides", (3, 8, 4, 0), "How many corners the profile has."),
    ("Width", "Width [m]", (1, 3, 1.5, 2), "The width of the ellipse the profile's corners sit on [m]."),
    ("Depth", "Depth [m]", (1, 3, 3, 2), "The depth of the ellipse the profile's corners sit on [m]."),
    ("CornerStart", "Corner start [deg]", (0, 180, 0, 0), "Where round the ellipse the first corner sits [deg]."),
    ("TwistSteps", "Twist steps", (-12, 12, 2, 0), "How far the profile twists round the ring, in steps that bring it back "
                                                    "onto itself: 360/Sides degrees when Width = Depth, otherwise 180 with "
                                                    "an even number of sides and 360 with an odd one."),
    ("TwistStart", "Twist start [deg]", (0, 360, 0, 0), "How far the first slice's profile is turned [deg]."),
]
SLICES = [
    ("Slices", "Slices", (40, 60, 48, 0), "How many slices round the ring."),
    ("Bracing", "Bracing", (BRACING, 2), "The bracing in the faces between two slices: None, Diagonal, Zigzag, "
                                         "Checkerboard, Cross or K - by name or by its number from 0."),
]

WHOLE_NUMBERS = ("Waves", "Swells", "Sides", "TwistSteps", "Slices")

OUTPUTS = [
    ("Chords", "Each chord round the ring, as a closed polyline. Explode it before making elements."),
    ("Frames", "Each slice's polygon, as a closed polyline. Explode it before making elements."),
    ("Bracing", "The diagonals, as polylines running on from one face to the next. Explode them before making "
                "elements."),
    ("Nodes", "The points the members end at, each once."),
    ("MemberCount", "How many straight members there are."),
    ("NodeCount", "How many nodes there are: the joints to make."),
    ("TotalLength", "How long the members are in all [m]."),
    ("Info", "The twist and the members, group by group, in words."),
]


# ---------------------------------------------------------------- building blocks

def open_canvas():
    """Grasshopper's editor, open, with a new empty document on its canvas."""
    gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    if gh is None:
        raise Exception("Grasshopper is not available in this Rhino.")
    if not gh.IsEditorLoaded():
        gh.LoadEditor()
    gh.ShowEditor()
    canvas = Grasshopper.Instances.ActiveCanvas
    if canvas is None:
        raise Exception("Open the Grasshopper editor once, then run this script again.")
    doc = GH_Document()
    Grasshopper.Instances.DocumentServer.AddDocument(doc)
    canvas.Document = doc
    return doc


def move(obj, left, top):
    """Moves an object so that its bounds - its input and output names included - start at
    (left, top). A component's pivot is not its corner: its inputs hang to the left of it."""
    a = obj.Attributes
    a.ExpireLayout()
    a.PerformLayout()
    b = a.Bounds
    a.Pivot = PointF(float(a.Pivot.X + left - b.X), float(a.Pivot.Y + top - b.Y))
    a.ExpireLayout()
    a.PerformLayout()


# Makes an object from its GUID. Inside Rhino, Grasshopper's component server does; WriteGh.py,
# which writes the .gh without Rhino, sets its own.
make_object = None


def emit(guid, what):
    if make_object is not None:
        obj = make_object(guid)
    else:
        obj = Grasshopper.Instances.ComponentServer.EmitObject(System.Guid(guid))
    if obj is None:
        raise Exception("{0} is not installed in this Grasshopper.".format(what))
    # Python 3 in Rhino 8 hands back an object by the interface a method returns; the component
    # itself is needed for its own members.
    obj = getattr(obj, "__implementation__", obj)
    obj.CreateAttributes()
    return obj


def slider(name, lo, hi, value, decimals):
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    s.Slider.Type = GH_SliderAccuracy.Integer if decimals == 0 else GH_SliderAccuracy.Float
    s.Slider.DecimalPlaces = decimals
    s.Slider.Minimum = System.Decimal(float(lo))
    s.Slider.Maximum = System.Decimal(float(hi))
    s.Slider.Value = System.Decimal(float(value))
    return s


def value_list(name, items, selected):
    """A drop-down value list: items are (name, expression)."""
    v = GH_ValueList()
    v.CreateAttributes()
    v.NickName = name
    v.ListMode = GH_ValueListMode.DropDown
    v.ListItems.Clear()
    for item_name, expression_ in items:
        v.ListItems.Add(GH_ValueListItem(item_name, expression_))
    v.SelectItem(selected)
    return v


def panel(name, source, width, height):
    """A panel showing what source carries."""
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = name
    p.Attributes.Bounds = RectangleF(0.0, 0.0, float(width), float(height))
    p.AddSource(source)
    return p


def note(doc, title, text_, x, y, width, height):
    p = GH_Panel()
    p.CreateAttributes()
    p.Properties.Colour = PANEL
    p.NickName = title
    p.SetUserText(text_)
    p.Attributes.Pivot = PointF(float(x), float(y))
    p.Attributes.Bounds = RectangleF(float(x), float(y), float(width), float(height))
    doc.AddObject(p, False)
    return p


def group(doc, name, objects, colour):
    g = GH_Group()
    g.CreateAttributes()
    g.NickName = name
    g.Colour = colour
    for o in objects:
        g.AddObject(o.InstanceGuid)
    doc.AddObject(g, False)
    return g


def python_component(nickname, code, inputs, outputs):
    """A GhPython component with the code, its inputs as (name, hint, description) and its outputs as
    (name, description), after its own out, which carries what the code prints and its errors."""
    c = emit(PYTHON, "GhPython")
    c.NickName = nickname
    while c.Params.Input.Count:
        c.Params.UnregisterInputParameter(c.Params.Input[0])
    while c.Params.Output.Count > 1:
        c.Params.UnregisterOutputParameter(c.Params.Output[1])
    for i, (name, hint, description) in enumerate(inputs):
        p = c.CreateParameter(GH_ParameterSide.Input, i)
        p.Name = p.NickName = name
        p.Description = description
        p.TypeHint = hint
        c.Params.RegisterInputParam(p)
    for i, (name, description) in enumerate(outputs):
        p = c.CreateParameter(GH_ParameterSide.Output, i + 1)
        p.Name = p.NickName = name
        p.Description = description
        c.Params.RegisterOutputParam(p)
    c.Params.OnParametersChanged()
    c.VariableParameterMaintenance()
    c.Code = code
    return c


# ---------------------------------------------------------------- the ring

def build(doc):
    # The type hints: whole numbers as int, the rest as float, and the bracing list as it comes - a
    # number from the list, or a name typed into a panel.
    python_hints = emit(PYTHON, "GhPython").GetType().Assembly
    as_float = System.Activator.CreateInstance(python_hints.GetType("GhPython.Component.NewFloatHint"))
    as_is = System.Activator.CreateInstance(python_hints.GetType("GhPython.Component.NoChangeHint"))
    as_int = GH_IntegerHint_CS()

    # 1-3 - The sliders and lists, one column of them, group after group, in the order of the inputs.
    stages, inputs = [], []
    for title, colour, rows in (("1 - Ring", RED, RING), ("2 - Profile and twist", ORANGE, PROFILE),
                                ("3 - Slices and bracing", PURPLE, SLICES)):
        made = []
        for name, label, setting, description in rows:
            if isinstance(setting[0], list):
                made.append(value_list(label, setting[0], setting[1]))
                hint = as_is
            else:
                made.append(slider(label, *setting))
                hint = as_int if name in WHOLE_NUMBERS else as_float
            inputs.append((name, hint, description))
        stages.append((title, colour, made))

    # 4 - The geometry: one Python component, and what it gives in words.
    ring = python_component("Twisted ring", System.IO.File.ReadAllText(CODE), inputs, OUTPUTS)
    info = panel("Info", ring.Params.Output[len(OUTPUTS)], 260, 210)

    # Laid out: the sliders in a column, each group 40 apart; the component beside them, level with
    # their middle; the panel beside it.
    x0, y = 300.0, 640.0
    slider_items = []
    for title, colour, made in stages:
        for obj in made:
            doc.AddObject(obj, False)
            move(obj, x0, y)
            y += obj.Attributes.Bounds.Height + 8
            slider_items.append(obj)
        y += 40
    top, bottom = 640.0, y - 48
    right = max(o.Attributes.Bounds.Right for o in slider_items)
    doc.AddObject(ring, False)
    move(ring, right + 160, 0)
    move(ring, right + 160, (top + bottom - ring.Attributes.Bounds.Height) / 2.0)
    doc.AddObject(info, False)
    move(info, ring.Attributes.Bounds.Right + 80, (top + bottom - info.Attributes.Bounds.Height) / 2.0)

    for i, obj in enumerate(slider_items):
        ring.Params.Input[i].AddSource(obj)

    for title, colour, made in stages:
        group(doc, title, made, colour)
    group(doc, "4 - Geometry: chords, frames and bracing as polylines", [ring, info], BLUE)

    note(doc, "Twisted ring",
         "Twisted ring - the geometry of a ring truss.\n\n"
         "Inputs: the ring (radius, wave, swell), the profile (sides, width, depth, twist), the slices and the "
         "bracing.\n\n"
         "Outputs: Chords, Frames and Bracing as polylines. An Alpaca4d element is a straight line between two "
         "nodes: explode the polylines before making elements. Nodes: every node, once. MemberCount, NodeCount "
         "and TotalLength: to compare designs.\n\n"
         "Twist steps: one step is 360/Sides degrees when Width = Depth, otherwise 180 with an even number of "
         "sides and 360 with an odd one, so that the ring always closes.\n\n"
         "Bracing: Diagonal; Zigzag, turning every bay; Checkerboard, turning every bay and face; Cross, both "
         "diagonals; K, to the middle of the next frame member, which it splits.",
         x0, 300, 700, 300)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Twisted ring built. Move the sliders, then build the Alpaca4d model from its outputs.")


if __name__ == "__main__":
    main()
