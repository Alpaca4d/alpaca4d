#! python3
# A reciprocal vault of plywood planks pushed until it gives way.
#
# Ninety-odd planks of birch plywood, 127 x 18 mm and about a metre long, laid over a barrel vault 4 m across
# and 2 m high in a pinwheel pattern: round every node of a grid, four planks turn about a small opening, each
# resting on the next, and one bolt holds each crossing. The geometry comes from a GhPython component
# (ReciprocalVaultGeometry.py, next to this script), which draws the pattern flat, wraps it round the vault and
# then moves the planks until every crossing is one plank deep, as they sit once built.
#
# The rest is Alpaca4d's components: each plank a chain of ForceBeamColumns on a Fiber Section of Steel01 - a
# stand-in for plywood, yielding alike in tension and compression - each bolt a Spring Link, stiff along the
# bolt, softer across it and free to turn about it, the feet held where they stand. One small GhPython component
# gives what no Grasshopper component does: Corotational transformations, for displacements far beyond what
# P-Delta follows (pressed down, P-Delta finds half the strength). Pushover Analysis then holds the vault's
# weight and adds more of it, pressing it down, until it collapses; or pushes its weight sideways.
#
# make.sh, in the folder above, writes it straight from this script, without Rhino:
#     ./make.sh Structures/ReciprocalVault.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open this file
# and press Run), in Rhino 7 with _RunPythonScript.
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
from Grasshopper.Kernel import GH_ParamAccess
from Grasshopper.Kernel.Types import GH_Colour, GH_Point
from Grasshopper.Kernel.Parameters.Hints import GH_IntegerHint_CS, GH_Point3dHint, GH_LineHint, GH_Vector3dHint, GH_StringHint_CS
from Rhino.Geometry import Point3d
from System.Drawing import Color

import Structures as S
from Structures import (component, inp, out, wire, flat, number, integer, words, booleans, slider, text, panel,
                        expression, menu_setting, show_result, python_component, Structure, colours,
                        SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, WIDTHS, VECTORS, LOADS, PATTERNS, MODEL, ANALYSIS,
                        VIEWS, LEGENDS, MAIN, LOADED, SHOWN, V_MODEL, V_STEP, V_COLOURS, R_VALUES, R_COLORS,
                        L_TITLE, L_VALUES, L_COLORS, L_SCALE, DISPLACEMENT, MAGNITUDE, B_LINE, B_SECTION, B_ZAXIS,
                        B_COLOUR)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "ReciprocalVaultGeometry.py")

STEEL01 = "DEB86811-4429-426F-8A7B-02DF89C5A074"            # Steel01
B_GEOMTRANSF = 2                                             # ForceBeamColumn's GeomTransf input
A_TOLERANCE = 5                                              # Assemble Model's Tolerance input

# Plywood, roughly - birch, 18 mm, along its face grain; check a maker's sheet before trusting a number.
E_PLYWOOD = 9.0e6        # kN/m2, 9 000 MPa
FC_PLYWOOD = 25000.0     # kN/m2: where Steel01 yields, in compression and in tension alike
B_PLYWOOD = 0.01         # Steel01's hardening: b = 0 leaves a force-based beam nothing to invert
G_PLYWOOD = 600000.0     # kN/m2, its shear modulus, for the plank's GJ

# The bolts: kN/m along and across, kN m/rad for the plank on plank turning against each other.
BOLT_AXIAL = 1.0e5
BOLT_SHEAR = 4000.0      # EN 1995's K_u = 2/3 rho^1.5 d / 23, rho 680 kg/m3, an 8 mm bolt
BOLT_TURN = 20.0

# The planks' colour: a light grey, which the plastic hinges' yellows, oranges and reds stand out on - a
# displacement's colours would run up to red as well.
PLANK = Color.FromArgb(255, 160, 168, 178)
# The hinges' colours, as Model View draws them (Alpaca4d.Result.PlasticHinges.BandColours), for their legend:
# the ductility reached, from 1, 2, 4 and 8 times the yield strain.
BANDS = [Color.FromArgb(255, 242, 201, 76), Color.FromArgb(255, 235, 108, 63), Color.FromArgb(255, 214, 40, 40),
         Color.FromArgb(255, 122, 14, 28)]
# The pushed vault is drawn as it moves: its displacements are as large as they look.
TRUE_SCALE = 1


def vault(doc):
    s = Structure(doc, "Reciprocal vault", "vault", [
        ("Length", "Length [m]", 2, 10, 4.8, 1),
        ("Span", "Span [m]", 2, 8, 4, 1),
        ("Rise", "Rise [m]", 1, 4, 2, 1),
        ("Cell", "Cell [m]", 0.5, 1.2, 0.8, 2),
        ("Fan", "Fan [m]", 0.05, 0.3, 0.18, 2),
        ("Thickness", "Plank thickness [mm]", 9, 40, 18, 0),
        ("Width", "Plank width [mm]", 60, 250, 127, 0),
    ], [
        ("Density", "Density [kg/m3]", 400, 800, 680, 0),
        ("Target", "Push to [mm]", 50, 2000, 600, 0),
        ("Steps", "Steps", 20, 400, 200, 0),
    ])
    as_float, as_int = s.as_float, GH_IntegerHint_CS()

    # 1 - The geometry: the vault, and which way it is pushed - Z, pressed down; Y or X, sideways.
    direction = text(s.grid, "Direction", "Z", SLIDERS, LOADED, gap=30)
    inputs = [(name, hint) for name, hint in s.inputs] + [("Density", as_float), ("Direction", GH_StringHint_CS())]
    outputs = ["Planks", "ZAxes", "Bolts", "Feet", "LoadPoints", "Loads", "Control", "Sign", "Weight", "Info"]
    geo = python_component(s.grid, "Reciprocal vault", "vault", inputs, outputs, GEOMETRY_, MAIN)
    for k, (name, hint) in enumerate(inputs[:-1]):
        wire(inp(geo, k), s.sliders[name])
    wire(inp(geo, len(inputs) - 1), direction)
    s.geo, s.outputs = geo, outputs
    # Model View draws the vault; the component's own preview would draw its lines and points over it.
    geo.Hidden = True
    info = panel(s.grid, "About the vault", s.g("Info"), 330, 70, GEOMETRY_, MAIN, gap=30)
    s.parts["1"] += [geo, info, direction]

    # 2 - The planks: Steel01 as plywood, in 6 x 8 fibres over the plank's width and
    # thickness - the drawing's x is the beam's local y, across the plank, and its y local z, out of its face -
    # on ForceBeamColumns with Corotational transformations.
    steel = component(s.grid, STEEL01, "Steel01", "Birch plywood", SECTIONS, MAIN, gap=30)
    words(inp(steel, 0), "Birch plywood")
    number(inp(steel, 1), FC_PLYWOOD)
    number(inp(steel, 2), E_PLYWOOD)
    number(inp(steel, 3), B_PLYWOOD)
    corner = expression(s.grid, "f(w)=0.0005*w", [s.sliders["Width"]], "Half width [m]", SECTIONS, MAIN, gap=30)
    corner_t = expression(s.grid, "f(t)=0.0005*t", [s.sliders["Thickness"]], "Half thickness [m]", SECTIONS, MAIN, gap=30)
    a = component(s.grid, S.CONSTRUCT_POINT, "Construct Point", "Corner", SECTIONS, MAIN, gap=30)
    neg = expression(s.grid, "f(x)=-x", [out(corner)], "-Half width", SECTIONS, MAIN, gap=30)
    neg_t = expression(s.grid, "f(x)=-x", [out(corner_t)], "-Half thickness", SECTIONS, MAIN, gap=30)
    wire(inp(a, 0), out(neg))
    wire(inp(a, 1), out(neg_t))
    b = component(s.grid, S.CONSTRUCT_POINT, "Construct Point", "Along the width", SECTIONS, MAIN, gap=30)
    wire(inp(b, 0), out(corner))
    wire(inp(b, 1), out(neg_t))
    c = component(s.grid, S.CONSTRUCT_POINT, "Construct Point", "Opposite corner", SECTIONS, MAIN, gap=30)
    wire(inp(c, 0), out(corner))
    wire(inp(c, 1), out(corner_t))
    outline = component(s.grid, S.RECTANGLE_3PT, "Rectangle 3Pt", "Plank", SECTIONS, MAIN, gap=30)
    wire(inp(outline, 0), out(a))
    wire(inp(outline, 1), out(b))
    wire(inp(outline, 2), out(c))
    mesh = component(s.grid, S.MESH_PLANE, "Mesh Plane", "Plank fibres", SECTIONS, MAIN, gap=30)
    wire(inp(mesh, 0), out(outline))
    integer(inp(mesh, 1), 6)
    integer(inp(mesh, 2), 8)
    patch = component(s.grid, S.PATCH, "Patch", "Plywood", SECTIONS, MAIN, gap=30)
    wire(inp(patch, 0), out(mesh))
    wire(inp(patch, 1), out(steel))
    # G J of a flat plank: G b t^3 / 3 (1 - 0.63 t / b).
    gj = expression(s.grid, "f(w,t)={0}*(w/1000)*(t/1000)^3/3*(1-0.63*t/w)".format(G_PLYWOOD),
                    [s.sliders["Width"], s.sliders["Thickness"]], "GJ [kN m2]", SECTIONS, MAIN, gap=30)
    section = component(s.grid, S.FIBER_SECTION, "Fiber Section", "Plank", SECTIONS, MAIN, gap=30)
    wire(inp(section, 2), out(patch))
    wire(inp(section, 3), out(gj))
    s.parts["2"] += [steel, corner, corner_t, a, neg, neg_t, b, c, outline, mesh, patch, gj, section]

    turning = python_component(s.grid, "Corotational", "corotational",
                               [("Planks", GH_LineHint(), GH_ParamAccess.list), ("ZAxes", GH_Vector3dHint(), GH_ParamAccess.list)],
                               ["GeomTransf"], MEMBERS, MAIN)
    wire(inp(turning, 0), s.g("Planks"))
    wire(inp(turning, 1), s.g("ZAxes"))
    planks = component(s.grid, S.BEAM, "ForceBeamColumn", "Planks", MEMBERS, MAIN, gap=30)
    wire(inp(planks, B_LINE), s.g("Planks"))
    wire(inp(planks, B_SECTION), out(section))
    wire(inp(planks, B_GEOMTRANSF), out(turning, 1))
    wire(inp(planks, B_ZAXIS), s.g("ZAxes"))
    inp(planks, B_COLOUR).PersistentData.Clear()
    inp(planks, B_COLOUR).PersistentData.Append(GH_Colour(PLANK))
    s.members.append(planks)
    s.parts["2"] += [turning, planks]

    # 3 - The bolts and the feet: a Spring Link a crossing, from the plank under to the plank on top - stiff
    # along the bolt (1), softer across it (2, 3), the planks pressed together turning against each other
    # stiffly (5, 6) and free to turn about the bolt (4, left out); the feet held where they stand, free to turn.
    springs = []
    for name, value in (("Along the bolt", BOLT_AXIAL), ("Across the bolt, y", BOLT_SHEAR), ("Across the bolt, z", BOLT_SHEAR),
                        ("Plank on plank, about y", BOLT_TURN), ("Plank on plank, about z", BOLT_TURN)):
        spring = component(s.grid, S.UNIAXIAL, "Uniaxial", name, SECTIONS, LOADED, gap=30)
        words(inp(spring, 0), name)
        # E and Eneg both: Eneg is the stiffness the other way, and left alone it is 210 000 000 whatever E is.
        number(inp(spring, 1), value)
        number(inp(spring, 2), value)
        springs.append(spring)
    bolts = component(s.grid, S.LINK, "Spring Link", "Bolts", MEMBERS, LOADED, gap=30)
    wire(inp(bolts, 0), s.g("Bolts"))
    wire(inp(bolts, 1), *[out(x) for x in springs])
    inp(bolts, 2).PersistentData.Clear()
    for k in (1, 2, 3, 5, 6):
        inp(bolts, 2).PersistentData.Append(Grasshopper.Kernel.Types.GH_Integer(k))
    s.members.append(bolts)
    feet = s.support("Feet", "Feet", (True, True, True, False, False, False))
    s.parts["3"] += springs + [bolts]

    # 4 - The loads: its own weight, ramped up over the pushover's first ten steps and held - Steel01 weighs
    # 7 850 kg/m3, so the Gravity Load's factor makes it plywood's Density; and the push - its weight again, a
    # share at every node, down or sideways.
    plywood = expression(s.grid, "f(d)=d/7850", [s.sliders["Density"]], "Plywood over steel", WIDTHS, LOADED, gap=30)
    weight = component(s.grid, S.GRAVITY, "Gravity Load", "Its own weight", LOADS, LOADED, gap=30)
    wire(inp(weight, 0), out(plywood))
    series = component(s.grid, S.LINEAR_SERIES, "Linear Time Series", "Ramp", PATTERNS, LOADED, gap=30)
    pattern = s.pattern("Its own weight", [weight])
    wire(inp(pattern, 0), out(series))
    push = component(s.grid, S.POINT_LOAD, "Point Load", "The push, its weight again", LOADS, LOADED, gap=30)
    wire(inp(push, 0), s.g("LoadPoints"))
    wire(inp(push, 1), s.g("Loads"))
    s.parts["4"] += [plywood, weight, series, push]

    # 5 - Assembled - bolts 18 mm long, so a tolerance well under that - and pushed: the crown to Push to mm.
    assembled = component(s.grid, S.ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(assembled, 0)), *[out(m) for m in s.members])
    wire(flat(inp(assembled, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(assembled, 2)), *[out(x) for x in s.patterns])
    number(inp(assembled, A_TOLERANCE), 0.002)
    target = expression(s.grid, "f(t,s)=s*t/1000", [s.sliders["Target"], s.g("Sign")], "Target [m]", MODEL, MAIN, gap=30)
    pushover = component(s.grid, S.PUSHOVER, "Pushover Analysis", "Pushover", ANALYSIS, MAIN)
    wire(inp(pushover, 0), out(assembled))
    wire(inp(pushover, 1), s.g("Control"))
    wire(inp(pushover, 2), direction)
    wire(inp(pushover, 3), out(target))
    wire(inp(pushover, 4), s.sliders["Steps"])
    wire(flat(inp(pushover, 5)), out(push))
    s.assembled, s.run = assembled, pushover
    s.parts["5"] += [assembled, target, pushover]

    # 6 - The vault at a point of the curve, as it moves, in grey, with its plastic hinges - a ball where the
    # plywood has yielded, coloured by the ductility it has reached and faded once it has unloaded - and the
    # capacity curve beside it.
    point = slider(s.grid, "Point on the curve", 0, 400, 200, 0, VIEWS, SHOWN, gap=30)
    origin = component(s.grid, S.CONSTRUCT_POINT, "Construct Point", "Curve origin", VIEWS, SHOWN, gap=30)
    beside = expression(s.grid, "f(l)=l+1.5", [s.sliders["Length"]], "Curve at x", VIEWS, SHOWN, gap=30)
    wire(inp(origin, 0), out(beside))
    size = expression(s.grid, "f(h)=h", [s.sliders["Rise"]], "Curve size", VIEWS, SHOWN, gap=30)
    curve = python_component(s.grid, "Capacity curve", "capacity_curve",
                             [("Displacement", as_float, GH_ParamAccess.list), ("LoadFactor", as_float, GH_ParamAccess.list),
                              ("Steps", as_int, GH_ParamAccess.list), ("Point", as_int), ("Weight", as_float),
                              ("Direction", GH_StringHint_CS()), ("Origin", GH_Point3dHint()), ("Size", as_float)],
                             ["Curve", "SoFar", "Dot", "Axes", "TagAt", "Tags", "Step"], LEGENDS, SHOWN)
    for k, source in enumerate([out(pushover, 1), out(pushover, 3), out(pushover, 4), point, s.g("Weight"),
                                direction, out(origin), out(size)]):
        wire(inp(curve, k), source)
    curve.Hidden = True
    drawn = S.emit(S.CURVE_PARAM, "The Curve parameter")
    drawn.NickName = "Capacity curve drawn"
    s.grid.put(drawn, LEGENDS, SHOWN, gap=30)
    wire(drawn, out(curve, 1), out(curve, 2), out(curve, 3), out(curve, 4))
    tags = component(s.grid, S.TEXT_TAG, "Text Tag 3D", "Curve labels", LEGENDS, SHOWN, gap=30)
    wire(inp(tags, 0), out(curve, 5))
    wire(inp(tags, 1), out(curve, 6))
    number(inp(tags, 2), 0.07)
    inp(tags, 3).PersistentData.Clear()
    inp(tags, 3).PersistentData.Append(GH_Colour(Color.FromArgb(255, 42, 45, 49)))

    view = component(s.grid, S.MODEL_VIEW, "Model View", "Vault pushed", VIEWS, SHOWN, gap=30)
    wire(inp(view, V_MODEL), out(pushover))
    wire(inp(view, V_STEP), out(curve, 7))
    menu_setting(view, "_ckDeformed", "Active", True)
    menu_setting(view, "_slDeformScale", "Value", TRUE_SCALE)
    menu_setting(view, "_ckHinges", "Active", True)
    menu_setting(view, "_ckReleases", "Active", False)
    menu_setting(view, "_ckShowSupports", "Active", True)
    # The supports' symbols are sized for a building; a plank's foot wants a small one.
    menu_setting(view, "_slSupportScale", "Value", 0.3)
    view.Attributes.ExpireLayout()
    view.Attributes.PerformLayout()
    # The hinges' legend: each colour from the ductility it starts at.
    caption = text(s.grid, "Legend title", "Hinges: ductility reached", LEGENDS, SHOWN, gap=30)
    legend = component(s.grid, S.LEGEND, "Legend", "Vault legend", LEGENDS, SHOWN, gap=30)
    wire(inp(legend, L_TITLE), caption)
    number(inp(legend, L_VALUES), 1, 2, 4, 8)
    inp(legend, L_COLORS).PersistentData.Clear()
    for colour in BANDS:
        inp(legend, L_COLORS).PersistentData.Append(GH_Colour(colour))
    hinges = panel(s.grid, "Hinges", out(pushover, 5), 520, 220, LEGENDS, SHOWN, gap=30)
    log = panel(s.grid, "What OpenSees said", out(pushover, 8), 520, 120, LEGENDS, SHOWN, gap=30)
    rest = s.at_rest(colour_by=0)
    s.parts["6"] += [point, origin, beside, size, curve, drawn, tags, view, caption, legend, hinges, log]

    s.finish({"1": "Geometry: a pinwheel of planks over a barrel vault, every crossing one plank deep; which way to push",
              "2": "Planks: birch plywood as Steel01, in fibres, on Corotational ForceBeamColumns",
              "3": "Bolts and feet: a Spring Link a crossing, free to turn about the bolt; the feet pinned",
              "4": "Loads: its own weight, held; the push, its weight again at every node",
              "5": "Pushover: the crown pushed to Push to mm, down (Z) or sideways (X, Y)",
              "6": "Results: the vault at a Point on the curve, as it moves, its hinges by the ductility they reach, and the capacity curve"},
             "Reciprocal vault - plywood planks bolted where they cross, pressed down until the vault gives way.\n\n"
             "A barrel vault 4 m across, 2 m high and 4.8 m long, of planks 127 x 18 mm in a pinwheel pattern: round "
             "each node of a grid four planks turn about a small opening, each resting on the next, one bolt at each "
             "crossing. The planks are moved until every crossing is one plank deep. Each plank is a chain of "
             "ForceBeamColumns between its bolts, on a Fiber Section of Steel01 standing in for birch plywood - 9 000 "
             "MPa, yielding at 25 MPa - with Corotational transformations, as the vault moves far before it gives way. "
             "Each bolt is a Spring Link 18 mm long: 4 000 kN/m across it, free to turn about it. The feet are pinned.\n\n"
             "Pushover Analysis holds its own weight, then adds its weight again, a share at every node, and scales "
             "it until the crown has moved Push to mm: down for Direction Z, sideways for Y or X. The curve reads "
             "in times its own weight pressed down, or in g sideways. Point on the curve picks a point of it; Model "
             "View draws the vault there, as it is, with a ball where the plywood has yielded, coloured by how far: yellow from "
             "first yield, orange from twice its yield strain, red from four times, dark red from eight; faded once it has "
             "unloaded. A ball stays once it has come.\n\n"
             "What it shows: pressed down, the vault carries its own weight and 2.4 times as much again, its crown "
             "220 mm down, then gives way, the crown dropping as the load falls off. The first plastic hinge forms at "
             "1.5 times; 104 have by the end. Pushed sideways, it does not give way: it leans further and further and stiffens, its planks "
             "pulled into a net.\n\n"
             "Not in it: plywood breaking. Steel01 yields in tension as in compression, where a plank of plywood would "
             "crack: the first Hinges, at about 1.5 times its weight, are where a real one would start to. Nor the "
             "play in the bolt holes, the planks crushing round the bolts, a bolt tearing out. Its bolts never slip "
             "and never break, so the vault is stiffer, and stronger, than one built.\n\n"
             "Try: Direction Y, and Push to 1000 mm; Plank thickness 24 mm; Fan wider.")


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code from its
    # GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    vault(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Reciprocal vault built.")


if __name__ == "__main__":
    main()
