#! python3
# Footfall on stairs: how much a stair shakes when someone walks up or down it, after SCI P354, on three
# stairs, one definition each.
#
#   STAIR=u         a U-shaped steel stair with two quarter landings, beams and shells: three flights on
#                   hollow section stringers, steel plate treads, landings on edge beams and posts
#                   (FootfallStair.gh)
#   STAIR=helical   a helical stair, all shells: a steel ribbon climbing round an open well between two
#                   parapets, continuous curved steel walls that rise with it and carry it
#                   (FootfallHelicalStair.gh)
#   STAIR=straight  a straight stair of 36 steps in three flights with two landings, all shells, held only
#                   at its two ends: steel plate stringers, plate treads and landings (FootfallStraightStair.gh)
#
# In each, Natural Vibration finds the modes, the mass the steel's own and the finishes with a tenth of the
# imposed load, as P354 has it; Footfall Analysis, with P354's Fourier coefficients for stairs (Table 3.2),
# finds the response factor at every node; a GhPython component colours where people walk by it, green to
# orange up to the Limit and red past it, and marks the worst node; a Model View draws a mode in blues, its
# frequency in its legend.
#
# make.sh, next to it, writes each straight from this script, without Rhino:
#     ./make.sh FootfallStair.py
#     STAIR=helical ./make.sh FootfallStair.py FootfallHelicalStair.gh
#     STAIR=straight ./make.sh FootfallStair.py FootfallStraightStair.gh
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open
# this file and press Run), in Rhino 7 with _RunPythonScript.
#
# It borrows the building blocks of Structures.py, next to it: the grid the components are laid out on,
# the groups, the sliders and the views.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import sys
import System

HERE = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(__file__))
if HERE not in sys.path:
    sys.path.append(HERE)

import Grasshopper
from Grasshopper.Kernel import GH_ParamAccess
from Grasshopper.Kernel.Types import GH_Colour
from Grasshopper.Kernel.Parameters.Hints import GH_MeshHint
from System.Drawing import Bitmap, Color

import Structures as S
from Structures import (component, inp, out, wire, flat, number, integer, words, colours, booleans, slider, text,
                        panel, menu_setting, show_result, result_unit, python_component, Structure,
                        SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, WIDTHS, LOADS, MODEL, ANALYSIS, VIEWS, LEGENDS,
                        MAIN, LOADED, SHOWN, V_MODEL, V_STEP, V_COLOURS, R_VALUES, R_COLORS, L_TITLE, L_VALUES,
                        L_COLORS, L_SCALE, DISPLACEMENT, MAGNITUDE, M_COLOUR, AT_REST, BY_MATERIAL)

STAIR = (System.Environment.GetEnvironmentVariable("STAIR") or "u").lower()

# The geometry's and the map's code, and the icons of their components.
GEOMETRY_FILE = System.IO.Path.Combine(HERE, "FootfallStairGeometry.py")
ICONS = System.IO.Path.Combine(HERE, "..", "..", "Assets", "icon-candidates")

FOOTFALL = "7E4C1A92-3D58-4B6F-A0E7-2C9F5B81D364"            # Footfall Analysis
MESH_PARAM = S.MESH_PARAM
TEXT_TAG = S.TEXT_TAG
LIST_ITEM = S.LIST_ITEM

# Footfall Analysis's inputs on its Self excitation unit: the body's, then the Frequency data menu's; and
# its outputs. Walk is left empty: the stairs walk on the nodes of their shells.
F_MODEL, F_WALK, F_FOOTSTEPS, F_WALKER, F_WEIGHTING = 0, 1, 2, 3, 4
F_ACTIVITY, F_MINIMUM, F_MAXIMUM, F_STEPS, F_CUTOFF, F_DAMPING = 5, 6, 7, 8, 9, 10
F_FACTOR, F_POINT, F_CRITICAL, F_REPORT = 0, 1, 2, 3

# The U stair's stringers' depth, mm, as it opens.
STRINGER_DEPTH = 140

# The steel, dark, and the plates, light, in the view of the stair.
DARK = Color.FromArgb(255, 42, 45, 49)
PLATE = Color.FromArgb(255, 222, 225, 229)

# The colours of the modes: blues to purple, Alpaca4d's - none of the response factor's greens, yellows,
# oranges and red, so that a mode is never read as a response.
MODE_SCALE = [S.PALE_BLUE, S.LIGHT_BLUE, "#011EFE", S.PURPLE_]


def icon(c, name):
    """Shows a GhPython component with an icon of Assets/icon-candidates."""
    path = System.IO.Path.Combine(ICONS, name)
    if System.IO.File.Exists(path):
        c.SetIconOverride(Bitmap(path))
    return c


def open_menus(c):
    """Opens the fold-out menus of a component that has them, as a click on their headers would."""
    c = getattr(c, "__implementation__", c)
    unit = getattr(c, "ActiveUnit", None)
    if unit is not None:
        for menu in unit.Context.Collection.Menus:
            menu.Expand()
    c.Attributes.ExpireLayout()
    c.Attributes.PerformLayout()


def library(s, name, material):
    c = component(s.grid, S.SECTION, "Steel Section Library", name, SECTIONS, MAIN, gap=30)
    words(inp(c, 0), name)
    wire(inp(c, 1), out(material))
    s.parts["2"].append(c)
    return c


def plate_steel(s):
    """S355 for the shells, as an nD material, light grey in the view of the stair."""
    m = component(s.grid, S.ND_MATERIAL, "nD", "Plate steel", SECTIONS, MAIN, gap=30)
    words(inp(m, 0), "S355 plate")
    number(inp(m, 1), 210e6)
    number(inp(m, 2), 210e6 / 2.6)
    number(inp(m, 3), 0.3)
    number(inp(m, 4), 7850)
    inp(m, 5).PersistentData.Append(GH_Colour(PLATE))
    s.parts["2"].append(m)
    return m


def plate(s, material, name, label, lo, hi, value):
    """A Plate Fiber Section of the steel, its thickness a slider in mm: returns it and the slider."""
    thickness = slider(s.grid, label, lo, hi, value, 0, SLIDERS, MAIN, gap=30)
    s.parts["1"].append(thickness)
    metres = S.expression(s.grid, "f(t)=t/1000", [thickness], label.replace("[mm]", "[m]"), SECTIONS, MAIN, gap=30)
    section = component(s.grid, S.PLATE_FIBER, "Plate Fiber Section", name, SECTIONS, MAIN, gap=30)
    words(inp(section, 0), name)
    wire(inp(section, 1), out(metres))
    wire(inp(section, 2), out(material))
    s.parts["2"] += [metres, section]
    return section, thickness


def shells(s, name, meshes, section):
    shell = component(s.grid, S.SHELL, "ASD Shell", name, MEMBERS, MAIN, gap=30)
    wire(flat(inp(shell, 0)), *[s.g(m) for m in meshes])
    wire(inp(shell, 1), out(section))
    s.members.append(shell)
    s.parts["2"].append(shell)
    return shell


def analyse(s, modes_count, solver, limit_default):
    """The masses, Assemble, Natural Vibration and Footfall Analysis with P354's stair coefficients:
    paces 1.2 to 4.5 Hz, the 12 Hz cut-off for stairs, 0.5% damping for welded steel, footsteps all the way
    up. Returns Natural Vibration, Footfall Analysis and the Limit slider."""
    masses = component(s.grid, S.MASS_POINT, "Mass Point", "Finishes and 10% imposed", LOADS, LOADED, gap=30)
    wire(inp(masses, 0), s.g("MassPoints"))
    wire(inp(masses, 1), s.g("Masses"))
    s.parts["4"].append(masses)

    a = component(s.grid, S.ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(a, 0)), *[out(m) for m in s.members])
    wire(flat(inp(a, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(a, 2)), out(masses))
    if s.constraints:
        wire(flat(inp(a, 3)), *[out(c) for c in s.constraints])
    s.assembled = a

    modes = component(s.grid, S.NATURAL_VIBRATION, "Natural Vibration Analysis", None, ANALYSIS, MAIN)
    wire(inp(modes, 0), out(a))
    integer(inp(modes, 1), modes_count)
    words(inp(modes, 2), solver)
    frequencies = panel(s.grid, "Frequencies [Hz]", out(modes, 3), 160, 130, ANALYSIS, MAIN, gap=30)

    footfall = component(s.grid, FOOTFALL, "Footfall Analysis", None, ANALYSIS, MAIN, gap=30)
    wire(inp(footfall, F_MODEL), out(modes))
    wire(inp(footfall, F_FOOTSTEPS), s.g("Steps"))
    words(inp(footfall, F_ACTIVITY), "Stairs")
    number(inp(footfall, F_MINIMUM), 1.2)
    number(inp(footfall, F_MAXIMUM), 4.5)
    integer(inp(footfall, F_STEPS), 331)
    number(inp(footfall, F_CUTOFF), 12)
    number(inp(footfall, F_DAMPING), 0.005)
    open_menus(footfall)
    report = panel(s.grid, "Footfall report", out(footfall, F_REPORT), 420, 170, ANALYSIS, MAIN, gap=160)
    limit = slider(s.grid, "Limit R", 1, 64, limit_default, 0, ANALYSIS, MAIN, gap=30)
    s.parts["5"] += [a, modes, frequencies, footfall, report, limit]
    return modes, footfall, limit


def results(s, modes, footfall, limit, surfaces, shown, lift, modes_count, mode_default):
    """Where people walk coloured by the response factor, with its Legend, the largest R and a marker on
    it; the stair at rest; and the mode Mode picks, in blues, its frequency in its legend's title."""
    bands = slider(s.grid, "Bands", 3, 12, 8, 0, VIEWS, MAIN, gap=30)
    response = python_component(s.grid, "Response map", "response_map",
                                [("Surfaces", GH_MeshHint(), GH_ParamAccess.list),
                                 ("Shown", GH_MeshHint(), GH_ParamAccess.list),
                                 ("Points", S.GH_Point3dHint(), GH_ParamAccess.list),
                                 ("Factors", s.as_float, GH_ParamAccess.list),
                                 ("Limit", s.as_float),
                                 ("Bands", S.GH_IntegerHint_CS()),
                                 ("Lift", s.as_float)],
                                ["Map", "Values", "Colours", "Peak", "Label", "Marker", "Title"], VIEWS, MAIN)
    icon(response, "footfall-response-map.png")
    wire(flat(inp(response, 0)), *[s.g(m) for m in surfaces])
    if shown:
        wire(flat(inp(response, 1)), s.g(shown))
    wire(inp(response, 2), out(footfall, F_POINT))
    wire(inp(response, 3), out(footfall, F_FACTOR))
    wire(inp(response, 4), limit)
    wire(inp(response, 5), bands)
    if isinstance(lift, float):
        number(inp(response, 6), lift)
    else:
        wire(inp(response, 6), lift)
    mapped = S.emit(MESH_PARAM, "Mesh")
    mapped.NickName = "Response factor map"
    s.grid.put(mapped, LEGENDS, MAIN, 30)
    wire(mapped, out(response, 1))
    marker = S.emit(MESH_PARAM, "Mesh")
    marker.NickName = "Peak marker"
    s.grid.put(marker, LEGENDS, MAIN, 30)
    wire(marker, out(response, 6))
    legend = component(s.grid, S.LEGEND, "Legend", "Response factor", LEGENDS, MAIN, gap=30)
    wire(inp(legend, L_TITLE), out(response, 7))
    wire(inp(legend, L_VALUES), out(response, 2))
    wire(inp(legend, L_COLORS), out(response, 3))
    number(inp(legend, L_SCALE), 2.4)
    peak = component(s.grid, TEXT_TAG, "Text Tag 3D", "Largest R", LEGENDS, MAIN, gap=30)
    wire(inp(peak, 0), out(response, 4))
    wire(inp(peak, 1), out(response, 5))
    number(inp(peak, 2), 0.25)
    inp(peak, 3).PersistentData.Clear()
    inp(peak, 3).PersistentData.Append(GH_Colour(DARK))
    s.parts["6"] += [bands, response, mapped, marker, legend, peak]

    # The stair itself, steel dark and plates light, with its sections drawn.
    view = component(s.grid, S.MODEL_VIEW, "Model View", "Stair at rest", VIEWS, MAIN, gap=30)
    wire(inp(view, V_MODEL), out(s.assembled))
    menu_setting(view, "_ckReleases", "Active", False)
    menu_setting(view, "_ddColourBy", "Value", BY_MATERIAL)
    s.parts["6"].append(view)

    # A mode, picked by Mode, in blues; its legend titled with its number and frequency.
    mode = slider(s.grid, "Mode", 0, modes_count - 1, mode_default, 0, VIEWS, SHOWN, gap=30)
    shape = component(s.grid, S.MODEL_VIEW, "Model View", "Stair mode", VIEWS, SHOWN, gap=30)
    wire(inp(shape, V_MODEL), out(modes))
    wire(inp(shape, V_STEP), mode)
    colours(inp(shape, V_COLOURS), MODE_SCALE, 8)
    menu_setting(shape, "_ckBands", "Active", True)
    menu_setting(shape, "_ckDeformed", "Active", True)
    menu_setting(shape, "_slDeformScale", "Value", 1.0)
    menu_setting(shape, "_ckWires", "Active", True)
    menu_setting(shape, "_ckReleases", "Active", False)
    show_result(shape, DISPLACEMENT, MAGNITUDE)
    result_unit(shape, DISPLACEMENT, MAGNITUDE, 2)
    title = python_component(s.grid, "Mode title", "mode_title",
                             [("Frequencies", s.as_float, GH_ParamAccess.list), ("Mode", S.GH_IntegerHint_CS())],
                             ["Title"], LEGENDS, SHOWN)
    wire(inp(title, 0), out(modes, 3))
    wire(inp(title, 1), mode)
    mode_legend = component(s.grid, S.LEGEND, "Legend", "Stair mode legend", LEGENDS, SHOWN, gap=30)
    wire(inp(mode_legend, L_TITLE), out(title, 1))
    wire(inp(mode_legend, L_VALUES), out(shape, R_VALUES))
    wire(inp(mode_legend, L_COLORS), out(shape, R_COLORS))
    number(inp(mode_legend, L_SCALE), 2.4)
    s.parts["6"] += [mode, shape, title, mode_legend]


# ---------------------------------------------------------------- a U-shaped stair, beams and shells

def u_stair(doc):
    s = Structure(doc, "Footfall stair", "stair", [
        ("Width", "Width [m]", 0.9, 1.8, 1.2, 2),
        ("LongRisers", "Risers, long flights", 6, 14, 10, 0),
        ("MiddleRisers", "Risers, middle flight", 2, 8, 4, 0),
        ("Rise", "Rise [mm]", 150, 190, 175, 0),
        ("Going", "Going [mm]", 250, 320, 275, 0),
    ], [
        ("Finishes", "Finishes [kN/m2]", 0, 2, 0.3, 2),
        ("Imposed", "Imposed [kN/m2]", 0, 5, 4.0, 1),
    ])
    s.inputs.append(("Finishes", s.as_float))
    s.inputs.append(("Imposed", s.as_float))
    s.geometry(["Stringers", "Treads", "Landings", "LandingBeams", "Posts", "Feet", "Tops", "MassPoints",
                "Masses", "Steps", "Height"])
    icon(s.geo, "footfall-stair-geometry.png")

    # The steel: the members dark, the plates light, so that the stair reads as it would be built.
    steel = s.steel()
    inp(steel, M_COLOUR).PersistentData.Clear()
    inp(steel, M_COLOUR).PersistentData.Append(GH_Colour(DARK))
    # The stringers' depth a slider: the stair's stiffness, and so where its modes are, turns on it.
    depth = slider(s.grid, "Stringer depth [mm]", 120, 400, STRINGER_DEPTH, 0, SLIDERS, MAIN, gap=30)
    s.parts["1"].append(depth)
    depth_m = S.expression(s.grid, "f(d)=d/1000", [depth], "Depth [m]", SECTIONS, MAIN, gap=30)
    stringer_section = component(s.grid, S.RECTANGLE_HOLLOW, "Rectangle Hollow CS", "Stringer", SECTIONS, MAIN, gap=30)
    words(inp(stringer_section, 0), "Stringer, 100 wide, 8 mm thick")
    number(inp(stringer_section, 1), 0.1)
    wire(inp(stringer_section, 2), out(depth_m))
    for k in (3, 4, 5):
        number(inp(stringer_section, k), 0.008)
    wire(inp(stringer_section, 6), out(steel))
    s.parts["2"] += [depth_m, stringer_section]
    s.elements("Stringers", "Stringers", stringer_section)
    s.elements("Landing beams", "LandingBeams", library(s, "200x100x8", steel))
    s.elements("Posts", "Posts", library(s, "139.7x8", steel))
    section, thickness = plate(s, plate_steel(s), "Tread plate", "Plate [mm]", 6, 40, 20)
    shells(s, "Treads", ["Treads"], section)
    shells(s, "Landings", ["Landings"], section)

    # Pinned at the foot of the stringers and the posts, and where the top flight meets the floor.
    s.support("Pinned feet", "Feet", (True, True, True, False, False, False))
    s.support("Floor above", "Tops", (True, True, True, False, False, False))

    # Twelve modes, by ARPACK on Mumps: past 2 Hz over the 12 Hz cut-off and twice the fundamental, and short
    # of the treads' own modes, two dozen near 64 Hz, which ARPACK stalls on.
    modes, footfall, limit = analyse(s, 12, "-genBandArpack", 32)
    # The map a little above the plates, so that it covers them.
    lift = S.expression(s.grid, "f(t)=t/2000+0.002", [thickness], "Above the plate [m]", VIEWS, MAIN, gap=30)
    s.parts["6"].append(lift)
    results(s, modes, footfall, limit, ["Treads", "Landings"], None, out(lift), 12, 1)

    s.finish({"1": "Geometry: three flights and two quarter landings between floors 4.2 m apart",
              "2": "Members: hollow section stringers of Stringer depth, RHS edge beams and CHS posts in S355, steel plate treads and landings as ASD Shells",
              "3": "Supports: pinned at the feet of the stringers and posts, and where the top flight meets the floor",
              "4": "Mass: the finishes and a tenth of the imposed load, at the nodes of the plates",
              "5": "Analysis: the first 12 modes, then Footfall Analysis with P354's stair coefficients",
              "6": "Results: the response factor on the treads and landings against Limit R, the stair, and the mode Mode picks"},
             "Footfall stair - how much a steel stair shakes when people walk up and down it, after SCI P354.\n\n"
             "A U-shaped stair between two floors: a long flight up, a quarter landing, a short flight across, a "
             "second quarter landing and a long flight back up. Each flight hangs between two stringers, "
             "hollow sections 100 mm wide, their depth a slider; its open treads, steel plates, are fixed to them "
             "at the middle of their ends. A landing is a plate on four 200 x 100 x 8 edge beams, a 139.7 x 8 post "
             "under its free corner.\n\n"
             "Natural Vibration finds its first 12 modes. Footfall Analysis then walks a 76 kg person up and down "
             "it at 1.2 to 4.5 steps a second, with P354's Fourier coefficients for stairs (Table 3.2), 0.5% "
             "damping for welded steel and the 12 Hz cut-off for stairs, the walker at each node in turn (self "
             "excitation), and the response building up over every step of the stair. The treads and landings "
             "are coloured by the response factor R, green to orange up to Limit R and red past it: P354 accepts "
             "32 on a stair in light use, 24 in heavy use.\n\n"
             "Try: Stringer depth down until the flights turn red; Plate thicker, or Width narrower; Finishes up, "
             "which adds mass; and Mode through the modes, their frequency in the legend.")


# ---------------------------------------------------------------- a helical stair, all shells

def helical_stair(doc):
    s = Structure(doc, "Helical stair", "helical", [
        ("Height", "Height [m]", 2.5, 6.0, 3.6, 2),
        ("Risers", "Risers", 12, 32, 20, 0),
        ("Turn", "Turn [deg]", 180, 450, 300, 0),
        ("InnerRadius", "Inner radius [m]", 0.3, 2.0, 0.8, 2),
        ("Width", "Width [m]", 0.8, 1.6, 1.1, 2),
        ("Parapet", "Parapet height [m]", 0.3, 1.2, 1.0, 2),
    ], [
        ("Finishes", "Steps and finishes [kN/m2]", 0, 3, 1.0, 2),
        ("Imposed", "Imposed [kN/m2]", 0, 5, 4.0, 1),
    ])
    s.inputs.append(("Finishes", s.as_float))
    s.inputs.append(("Imposed", s.as_float))
    s.geometry(["Ribbon", "InnerParapet", "OuterParapet", "StepMeshes", "Supports", "MassPoints", "Masses",
                "Steps", "Top"])
    icon(s.geo, "footfall-stair-geometry.png")

    steel = plate_steel(s)
    ribbon, ribbon_t = plate(s, steel, "Ribbon plate", "Ribbon [mm]", 8, 40, 15)
    parapet, parapet_t = plate(s, steel, "Parapet plate", "Parapet [mm]", 6, 30, 12)
    shells(s, "Ribbon", ["Ribbon"], ribbon)
    shells(s, "Parapets", ["InnerParapet", "OuterParapet"], parapet)
    s.support("Built in at both ends", "Supports", (True, True, True, True, True, True))

    # Twelve modes, by ARPACK on Mumps: to past twice the fundamental, which the steady state of a stair above
    # the cut-off takes.
    modes, footfall, limit = analyse(s, 12, "-genBandArpack", 32)
    results(s, modes, footfall, limit, ["Ribbon"], "StepMeshes", 0.0, 12, 0)

    s.finish({"1": "Geometry: a ribbon climbing round an open well between two curved parapets, and its steps",
              "2": "Members: S355 plates as ASD Shells, the ribbon Ribbon thick and the parapets Parapet thick",
              "3": "Supports: built in at both ends, the ribbon and both parapets",
              "4": "Mass: the steps, the finishes and a tenth of the imposed load, at the nodes of the ribbon",
              "5": "Analysis: the first 12 modes, then Footfall Analysis with P354's stair coefficients",
              "6": "Results: the response factor on the steps against Limit R, the stair, and the mode Mode picks"},
             "Helical stair - a stair of shells round an open well, and how much it shakes when people walk on it, "
             "after SCI P354.\n\n"
             "A steel ribbon, the helicoid it climbs on, turns Turn degrees as it rises Height between two "
             "parapets: continuous curved steel walls Parapet height tall that rise with it, round the well and "
             "round the outside, and carry it like deep stringers. No column in the middle, no stringers: the "
             "plates are the structure. It is built in at both ends, on the floor and into the floor it reaches. "
             "The steps sit on the ribbon: drawn, and their weight, with the finishes and a tenth of the imposed "
             "load, at its nodes.\n\n"
             "Footfall Analysis walks a person up and down it with P354's coefficients for stairs, and the steps "
             "are coloured by the response factor, green to orange up to Limit R and red past it.\n\n"
             "Try: Parapet height down, and watch the stair come alive; Turn up; Ribbon thicker; and Mode through "
             "the modes, their frequency in the legend.")


# ---------------------------------------------------------------- a straight stair, 36 steps, all shells

def straight_stair(doc):
    s = Structure(doc, "Straight stair", "straight", [
        ("Width", "Width [m]", 0.9, 1.8, 1.2, 2),
        ("Risers", "Risers a flight", 6, 16, 12, 0),
        ("Flights", "Flights", 1, 4, 3, 0),
        ("Rise", "Rise [mm]", 150, 190, 175, 0),
        ("Going", "Going [mm]", 250, 320, 275, 0),
        ("Landing", "Landing [m]", 0.9, 2.4, 1.2, 2),
        ("Depth", "Stringer depth [mm]", 200, 1200, 400, 0),
    ], [
        ("Finishes", "Finishes [kN/m2]", 0, 2, 0.3, 2),
        ("Imposed", "Imposed [kN/m2]", 0, 5, 4.0, 1),
    ])
    s.inputs.append(("Finishes", s.as_float))
    s.inputs.append(("Imposed", s.as_float))
    s.geometry(["Stringers", "Treads", "Landings", "Supports", "LinkRetained", "LinkConstrained", "MassPoints",
                "Masses", "Steps", "Height"])
    icon(s.geo, "footfall-stair-geometry.png")

    steel = plate_steel(s)
    stringer, stringer_t = plate(s, steel, "Stringer plate", "Stringer plate [mm]", 8, 40, 15)
    tread, tread_t = plate(s, steel, "Tread plate", "Tread plate [mm]", 6, 40, 20)
    shells(s, "Stringers", ["Stringers"], stringer)
    shells(s, "Treads", ["Treads"], tread)
    shells(s, "Landings", ["Landings"], tread)
    s.support("Held at both ends", "Supports", (True, True, True, False, False, False))
    # The treads welded along their ends: their corners follow the stringers as rigid bodies.
    links = component(s.grid, S.RIGID_LINK, "Rigid Link", "Treads welded to stringers", MEMBERS, LOADED, gap=30)
    wire(inp(links, 0), s.g("LinkRetained"))
    wire(inp(links, 1), s.g("LinkConstrained"))
    s.constraints.append(links)
    s.parts["3"].append(links)

    modes, footfall, limit = analyse(s, 15, "-genBandArpack", 32)
    lift = S.expression(s.grid, "f(t)=t/2000+0.002", [tread_t], "Above the plate [m]", VIEWS, MAIN, gap=30)
    s.parts["6"].append(lift)
    results(s, modes, footfall, limit, ["Treads", "Landings"], None, out(lift), 15, 1)

    s.finish({"1": "Geometry: 36 steps in three flights and two landings, in a line, held only at its ends",
              "2": "Members: S355 plates as ASD Shells, two stringers Stringer depth deep, open treads and landings",
              "3": "Supports: the stringers' end edges, on the floor and at the floor above, and nowhere between; the treads' corners tied to the stringers by rigid links",
              "4": "Mass: the finishes and a tenth of the imposed load, at the nodes of the treads and landings",
              "5": "Analysis: the first 15 modes, then Footfall Analysis with P354's stair coefficients",
              "6": "Results: the response factor on the treads and landings against Limit R, the stair, and the mode Mode picks"},
             "Straight stair - 36 steps in one line, held only at its two ends, and how much it shakes when people "
             "walk on it, after SCI P354.\n\n"
             "Three flights of 12 risers with a landing between each two, all in a line: about 11.5 m along and "
             "6.3 m up, with no post or hanger between the floors. Two steel plate stringers run its whole "
             "length, Stringer depth deep below the line through the middle of the treads; the open treads and "
             "the landings are steel plates between them. Every plate is an ASD Shell.\n\n"
             "Footfall Analysis walks a person up and down it with P354's coefficients for stairs, and the treads "
             "and landings are coloured by the response factor, green to orange up to Limit R and red past it.\n\n"
             "Try: Stringer depth up until no tread is red; Flights down to two; Landing longer; and Mode through "
             "the modes, their frequency in the legend.")


BUILDERS = {"u": u_stair, "helical": helical_stair, "straight": straight_stair}


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code
    # from its GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    if STAIR not in BUILDERS:
        raise Exception("STAIR is one of " + ", ".join(sorted(BUILDERS)))
    BUILDERS[STAIR](doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print(STAIR + " stair built.")


if __name__ == "__main__":
    main()
