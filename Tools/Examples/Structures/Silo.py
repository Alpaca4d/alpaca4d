#! python3
# A feed silo of glass fibre on four steel legs, as farms have, and two sudden loads it can meet, each a
# transient analysis: a dust explosion inside it, and a tractor reversing into one of its legs.
#
# A dust explosion: filling a silo blows a cloud of fine feed dust up inside it, and a spark can light
# it. The pressure rises in a few tens of milliseconds and pushes on the whole inside of the wall, until
# the lid of the manhole on the roof blows open and lets it out. Here the pressure rises to its peak in
# Rise ms and is gone at Duration ms, pushing on every face of the wall along its normal: a Mesh Load a
# face, and a Path Time Series of the pulse.
#
# A tractor reversing into a leg: a hard impact, the tractor's mass at its speed against a contact that
# gives like a spring. The force rises and falls as half a sine, its peak v sqrt(k m) over pi sqrt(m / k):
# a Point Load on the leg and a Path Time Series of the pulse.
#
# Load combinations are solved as static analyses, so the two events are not combinations here: Event
# picks one, and turns its load pattern on and the other's off, by their factors, and gives the
# analysis that event's time step. The feed inside is mass, from the Feed level slider; the silo's own
# weight is mass too. Neither weighs on it here: the results are what the event adds to the silo
# standing full, and in an elastic model they add to its weight's.
#
# The geometry comes from a GhPython component (SiloGeometry.py, next to this script), and so do the
# two pulses; the rest is Alpaca4d's components. make.sh, in the folder above, writes it straight from
# this script, without Rhino:
#     ./make.sh Structures/Silo.py SiloExplosionImpact.gh
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor (open
# this file and press Run).
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
from Grasshopper.Kernel.Special import GH_ValueList, GH_ValueListItem, GH_ValueListMode
from Grasshopper.Kernel.Types import GH_Colour, GH_Interval
from Rhino.Geometry import Interval
from System.Drawing import Color

import Structures as S
from Structures import (component, inp, out, wire, flat, words, number, panel, slider, expression, menu_setting,
                        python_component, switch_unit, Structure,
                        SLIDERS, GEOMETRY_, SECTIONS, MEMBERS, WIDTHS, VECTORS, LOADS, PATTERNS, MODEL, ANALYSIS,
                        VIEWS, LEGENDS, MAIN, SHOWN, V_STEP, V_RANGE)

GEOMETRY_FILE = System.IO.Path.Combine(HERE, "SiloGeometry.py")

STREAM_GATE = "71fcc052-6add-4d70-8d97-cfb37ea9d169"        # Sets > Tree > Stream Gate
QUICK_GRAPH = "2b69bf71-4e69-43aa-b7be-4f6ce7e45bef"        # Params > Input > Quick Graph

# The event's group, the yellow of Steel Office's combinations.
YELLOW = Color.FromArgb(60, 230, 200, 40)

# Load Pattern's inputs, by place.
P_SERIES, P_LOADS, P_FACTOR, P_NAME = 0, 1, 2, 3

# The rows, from the top: the silo and the analysis; the event; the feed; the explosion; the impact;
# the results.
EVENT, FEED, BLAST, KNOCK = 0.4, 0.8, 1.2, 1.6

# The two events, each with its time step and how many steps: the explosion is over in a tenth of a
# second, and is followed for 0.4 s, a millisecond a step; the silo swings for seconds after the
# tractor, and is followed for 4 s, 5 ms a step.
EXPLOSION_STEP, EXPLOSION_STEPS = 0.001, 400
IMPACT_STEP, IMPACT_STEPS = 0.005, 800

# Each view's deformation scale, the top of its colours in mm, the same for every step - eight bands of
# a round number each - and the step it opens at: the explosion at its peak, 50 ms in, when the edge of
# the roof has moved 0.74 mm; the impact at the silo's largest sway, 0.27 s in, 24 mm at the outlet.
EXPLOSION_VIEW = (600, 0.8, 50)
IMPACT_VIEW = (20, 24, 54)


def value_list(grid, name, items, selected, col, row=None, gap=0):
    """A drop-down value list: items are (name, value)."""
    v = GH_ValueList()
    v.CreateAttributes()
    v.NickName = name
    v.ListMode = GH_ValueListMode.DropDown
    v.ListItems.Clear()
    for item_name, value in items:
        v.ListItems.Add(GH_ValueListItem(item_name, value))
    v.SelectItem(selected)
    return grid.put(v, col, row, gap)


def named(params, name):
    """An input or output by its name."""
    return [p for p in params if p.Name == name][0]


def sliders(s, row, specs, col=SLIDERS):
    """Sliders on a row of their own, in the first column unless col says, each kept by its name."""
    made = []
    for name, label, lo, hi, value, decimals in specs:
        s.sliders[name] = slider(s.grid, label, lo, hi, value, decimals, col, row, gap=30)
        made.append(s.sliders[name])
    return made


def view(s, name, model, settings, steps, title):
    """A Model View of model at its own Step slider, deformed scale times, its colours over 0 to top mm
    for every step alike, the elements' edges and the supports drawn, with a Legend."""
    scale, top, step = settings
    stepper = slider(s.grid, name + " step", 0, steps, step, 0, VIEWS, SHOWN, gap=30)
    v = s.deformed(name, model, scale, title=title, loads=False, palette="silo", bands=True)
    menu_setting(v, "_ckWires", "Active", True)
    menu_setting(v, "_ckShowSupports", "Active", True)
    menu_setting(v, "_slSupportScale", "Value", 0.4)
    wire(inp(v, V_STEP), stepper)
    inp(v, V_RANGE).PersistentData.Clear()
    inp(v, V_RANGE).PersistentData.Append(GH_Interval(Interval(0.0, top)))
    return [stepper] + s.parts["6"][-3:]


def silo(doc):
    s = Structure(doc, "Silo", "silo", [
        ("Radius", "Radius [m]", 0.6, 2.0, 1.1, 2),
        ("CylinderHeight", "Cylinder height [m]", 1.0, 6.0, 2.4, 2),
        ("HopperAngle", "Hopper angle [deg]", 40, 70, 60, 0),
        ("OutletHeight", "Outlet height [m]", 0.5, 2.0, 1.0, 2),
        ("Around", "Elements around", 16, 64, 32, 0),
        ("Size", "Element size [m]", 0.15, 0.6, 0.3, 2),
    ], [])
    as_float = s.as_float
    feed = sliders(s, FEED, [("Filled", "Feed level [%]", 0, 100, 75, 0),
                             ("Density", "Feed density [kg/m3]", 300, 900, 650, 0)])
    blast = sliders(s, BLAST, [("Pressure", "Peak pressure [kPa]", 0, 100, 20, 1),
                               ("Rise", "Rise [ms]", 1, 200, 50, 0),
                               ("Duration", "Duration [ms]", 10, 500, 150, 0)])
    knock = sliders(s, KNOCK, [("TractorMass", "Tractor mass [t]", 1, 15, 5, 1),
                               ("Speed", "Speed [km/h]", 0, 10, 4, 1),
                               ("Stiffness", "Contact stiffness [kN/m]", 50, 2000, 300, 0),
                               ("ImpactHeight", "Impact height [m]", 0.3, 2.0, 1.25, 2)])
    # Beside the analysis they feed: 2% of critical at the silo's sway on its legs, 2.4 Hz, and at 100 Hz,
    # where the wall's own modes start; less between the two.
    damping = sliders(s, MAIN, [("Damping", "Damping [%]", 0, 10, 2, 1),
                                ("F1", "Damping at [Hz]", 0.1, 10, 2.4, 2),
                                ("F2", "and at [Hz]", 1, 1000, 100, 0)], col=MODEL - 0.5)

    # 1 - The silo: its wall, the legs, brackets, steel rings and ties, the feet, the feed as masses, and the point
    # the tractor hits - which the legs need a node at, and the feed's masses where they sit.
    s.inputs += [("Filled", as_float), ("Density", as_float), ("ImpactHeight", as_float)]
    s.geometry(["Wall", "Normals", "Legs", "Brackets", "Rings", "Ties", "Feet", "MassPoints", "Masses", "Impact", "Info"])
    info = panel(s.grid, "About the silo", s.g("Info"), 300, 60, GEOMETRY_, MAIN, gap=30)
    s.parts["1"].append(info)

    # 2 - The wall, glass fibre Wall [mm] thick, as ASD Shells; legs and brackets of 114.3 x 5 tube, the
    # steel rings flat bars 100 x 10, lying flat round the wall, and ties of 60.3 x 4, S355.
    wall = slider(s.grid, "Wall [mm]", 3, 20, 8, 0, SLIDERS, MAIN, gap=30)
    s.parts["1"].append(wall)
    # Glass fibre as an elastic, isotropic nD material: a laminate's E of 12 GPa, nu 0.3, 1 850 kg/m3.
    glass = component(s.grid, S.ND_MATERIAL, "nD", "Glass fibre", SECTIONS, MAIN, gap=30)
    words(inp(glass, 0), "Glass fibre")
    number(inp(glass, 1), 12e6)
    number(inp(glass, 2), 12e6 / 2.6)
    number(inp(glass, 3), 0.3)
    number(inp(glass, 4), 1850)
    inp(glass, 5).PersistentData.Append(GH_Colour(S.AT_REST))
    metres = expression(s.grid, "f(t)=t/1000", [wall], "Wall [m]", SECTIONS, MAIN, gap=30)
    plate = component(s.grid, S.PLATE_FIBER, "Plate Fiber Section", "Wall", SECTIONS, MAIN, gap=30)
    wire(inp(plate, 1), out(metres))
    wire(inp(plate, 2), out(glass))
    shells = component(s.grid, S.SHELL, "ASD Shell", "Wall", MEMBERS, MAIN, gap=30)
    wire(flat(inp(shells, 0)), s.g("Wall"))
    wire(inp(shells, 1), out(plate))
    s.members.append(shells)
    s.parts["2"] += [glass, metres, plate, shells]
    s.steel()
    tube = component(s.grid, S.SECTION, "Steel Section Library", "114.3x5", SECTIONS, MAIN, gap=30)
    words(inp(tube, 0), "114.3x5")
    wire(inp(tube, 1), out(s.material))
    s.parts["2"].append(tube)
    s.elements("Legs", "Legs", tube)
    s.elements("Brackets", "Brackets", tube)
    flat_bar = s.section(S.RECTANGLE, "Ring 100 x 10", s.material, 0.1, 0.01)
    s.elements("Steel rings", "Rings", flat_bar)
    s.member("Ties", "Ties", "60.3x4")

    # 3 - The legs' feet, bolted down to the slab: built in.
    s.support("Feet built in", "Feet", (True,) * 6)

    # The event: which load pattern acts, the other's factor 0, and the time step of the analysis.
    event = value_list(s.grid, "Event", [("Dust explosion", "0"), ("Tractor impact", "1")], 0, SLIDERS, EVENT, gap=30)
    explosion_on = expression(s.grid, "f(e)=1-e", [event], "Explosion on", WIDTHS, EVENT, gap=30)
    impact_on = expression(s.grid, "f(e)=e", [event], "Impact on", VECTORS, EVENT, gap=30)
    step = expression(s.grid, "f(e)={0}+{1}*e".format(EXPLOSION_STEP, IMPACT_STEP - EXPLOSION_STEP), [event],
                      "Time step [s]", LOADS, EVENT, gap=30)
    steps = expression(s.grid, "f(e)={0}+{1}*e".format(EXPLOSION_STEPS, IMPACT_STEPS - EXPLOSION_STEPS), [event],
                       "Steps", PATTERNS, EVENT, gap=30)
    event_part = [event, explosion_on, impact_on, step, steps]

    # 4 - The feed, as mass at the wall's nodes: Mass Points go into Assemble with the load patterns.
    masses = component(s.grid, S.MASS_POINT, "Mass Point", "Feed", LOADS, FEED, gap=30)
    wire(inp(masses, 0), s.g("MassPoints"))
    wire(inp(masses, 1), s.g("Masses"))
    s.patterns.append(masses)
    feed_part = feed + [masses]

    # 5 - The dust explosion: the pulse, and the peak pressure on every face, outwards along its normal.
    pulse = python_component(s.grid, "Explosion pulse", "explosion", [("Rise", as_float), ("Duration", as_float)],
                             ["Times", "Values"], WIDTHS, BLAST)
    wire(inp(pulse, 0), s.sliders["Rise"])
    wire(inp(pulse, 1), s.sliders["Duration"])
    series = component(s.grid, S.PATH_SERIES, "Path Time Series", "Pressure over time", VECTORS, BLAST, gap=30)
    wire(inp(series, 0), out(pulse, 1))
    wire(inp(series, 1), out(pulse, 2))
    graph = component(s.grid, QUICK_GRAPH, "Quick Graph", "Explosion pulse", LOADS, BLAST, gap=30)
    wire(graph, out(series, 1))
    push = component(s.grid, S.MULTIPLY, "Multiply", "Pressure on each face", VECTORS, BLAST, gap=30)
    wire(inp(push, 0), s.g("Normals"))
    wire(inp(push, 1), s.sliders["Pressure"])
    pressure = component(s.grid, S.MESH_LOAD, "Mesh Load", "Pressure on the wall", LOADS, BLAST, gap=30)
    wire(flat(inp(pressure, 0)), out(shells))
    wire(flat(inp(pressure, 1)), out(push))
    explosion = component(s.grid, S.LOAD_PATTERN, "Load Pattern", "Dust explosion", PATTERNS, BLAST, gap=30)
    wire(inp(explosion, P_SERIES), out(series))
    wire(flat(inp(explosion, P_LOADS)), out(pressure))
    wire(inp(explosion, P_FACTOR), out(explosion_on))
    words(inp(explosion, P_NAME), "Dust explosion")
    s.patterns.append(explosion)
    blast_part = blast + [pulse, series, graph, push, pressure, explosion]

    # 6 - The tractor: the pulse, and its peak force on the leg, along -X.
    hit = python_component(s.grid, "Tractor impact", "impact",
                           [("Mass", as_float), ("Speed", as_float), ("Stiffness", as_float)],
                           ["Force", "Contact", "Times", "Values"], WIDTHS, KNOCK)
    wire(inp(hit, 0), s.sliders["TractorMass"])
    wire(inp(hit, 1), s.sliders["Speed"])
    wire(inp(hit, 2), s.sliders["Stiffness"])
    shown = panel(s.grid, "Peak force [kN], contact [s]", out(hit, 1), 170, 40, WIDTHS, KNOCK, gap=30)
    wire(shown, out(hit, 2))
    series2 = component(s.grid, S.PATH_SERIES, "Path Time Series", "Force over time", VECTORS, KNOCK, gap=30)
    wire(inp(series2, 0), out(hit, 3))
    wire(inp(series2, 1), out(hit, 4))
    graph2 = component(s.grid, QUICK_GRAPH, "Quick Graph", "Impact pulse", LOADS, KNOCK, gap=30)
    wire(graph2, out(series2, 1))
    backwards = expression(s.grid, "f(F)=-F", [out(hit, 1)], "Force [kN]", WIDTHS, KNOCK, gap=30)
    along = component(s.grid, S.UNIT_X, "Unit X", "Along -X", VECTORS, KNOCK, gap=30)
    wire(inp(along, 0), out(backwards))
    force = component(s.grid, S.POINT_LOAD, "Point Load", "Tractor", LOADS, KNOCK, gap=30)
    wire(inp(force, 0), s.g("Impact"))
    wire(inp(force, 1), out(along))
    impact = component(s.grid, S.LOAD_PATTERN, "Load Pattern", "Tractor impact", PATTERNS, KNOCK, gap=30)
    wire(inp(impact, P_SERIES), out(series2))
    wire(flat(inp(impact, P_LOADS)), out(force))
    wire(inp(impact, P_FACTOR), out(impact_on))
    words(inp(impact, P_NAME), "Tractor impact")
    s.patterns.append(impact)
    knock_part = knock + [hit, shown, series2, graph2, backwards, along, force, impact]

    # 7 - Assembled, and followed step by step: Newmark's average acceleration, the event's time step,
    # Rayleigh damping of Damping % of critical at two frequencies - the silo's sway and its wall's
    # breathing - and the Linear algorithm, one solve a step, as an elastic model needs.
    a = component(s.grid, S.ASSEMBLE, "Assemble Model", s.title, MODEL, MAIN)
    wire(flat(inp(a, 0)), *[out(m) for m in s.members])
    wire(flat(inp(a, 1)), *[out(x) for x in s.supports])
    wire(flat(inp(a, 2)), *[out(x) for x in s.patterns])
    # Only what the views and a check of the totals read, every step: the displacements and the reactions,
    # forces and moments, which Reaction Forces reads together. Everything, every step, would be close to a
    # gigabyte for the impact's 800 steps.
    record = S.recorder(s.grid, "Displacements and reactions", ["displacement", "reactionForce", "reactionMoment"],
                        MODEL, MAIN, gap=30)
    wire(inp(a, 4), out(record))
    zeta, f1, f2 = s.sliders["Damping"], s.sliders["F1"], s.sliders["F2"]
    # alphaM = 2 zeta w1 w2 / (w1 + w2) and betaK = 2 zeta / (w1 + w2), w = 2 pi f, zeta in %.
    alpha = expression(s.grid, "f(z,a,b)=0.1256637*z*a*b/(a+b)", [zeta, f1, f2], "alphaM", MODEL, MAIN, gap=30)
    beta = expression(s.grid, "f(z,a,b)=0.0031831*z/(a+b)", [zeta, f1, f2], "betaK", MODEL, MAIN, gap=30)
    rayleigh = component(s.grid, S.DAMPING, "Damping", "Rayleigh", MODEL, MAIN, gap=30)
    wire(inp(rayleigh, 0), out(alpha))
    wire(inp(rayleigh, 3), out(beta))
    integrator = component(s.grid, S.INTEGRATOR, "Integrator", "Newmark", MODEL, MAIN, gap=30)
    switch_unit(integrator, S.NEWMARK)
    increments = component(s.grid, S.ANALYSIS_STEP, "Analysis Step", "The event's steps", MODEL, MAIN, gap=30)
    wire(inp(increments, 0), out(steps))
    wire(inp(increments, 1), out(step))
    settings = component(s.grid, S.ANALYSIS_SETTINGS, "Analysis Settings", "Transient", ANALYSIS, MAIN)
    wire(inp(settings, S.A_DAMPING), out(rayleigh))
    words(inp(settings, S.A_ALGORITHM), "Linear")
    wire(inp(settings, S.A_INTEGRATOR), out(integrator))
    words(inp(settings, S.A_TYPE), "Transient")
    wire(inp(settings, S.A_STEPS), out(increments))
    run = component(s.grid, S.RUN_ANALYSIS, "Run Analysis", None, ANALYSIS, MAIN, gap=30)
    wire(inp(run, 0), out(a))
    wire(inp(run, 1), out(settings))
    s.assembled, s.run = a, run
    analysis_part = damping + [a, record, alpha, beta, rayleigh, integrator, increments, settings, run]

    # 8 - Results: the analysed model goes to the view of its event, each with its own scale - the
    # explosion moves the wall a millimetre or so, the tractor sways the whole silo by centimetres.
    gate = component(s.grid, STREAM_GATE, "Stream Gate", "To the event's view", ANALYSIS + 0.5, SHOWN, gap=30)
    wire(named(gate.Params.Input, "Stream"), out(run))
    wire(named(gate.Params.Input, "Gate"), event)
    results_part = [gate]
    results_part += view(s, "Explosion", gate.Params.Output[0], EXPLOSION_VIEW, EXPLOSION_STEPS,
                         "Dust explosion: displacement [mm]")
    results_part += view(s, "Impact", gate.Params.Output[1], IMPACT_VIEW, IMPACT_STEPS,
                         "Tractor impact: displacement [mm]")

    s.grid.arrange(300, 300, stages=(GEOMETRY_, SECTIONS, WIDTHS, MODEL, VIEWS))
    for key, name, objects, colour in [
            ("1", "Geometry: a silo of glass fibre on four steel legs", s.parts["1"], S.RED),
            ("2", "Members: the wall as ASD Shells; steel legs, brackets, rings and ties", s.parts["2"], S.ORANGE),
            ("3", "Supports: the legs built in at their feet", s.parts["3"], S.PURPLE),
            ("4", "Event: which load acts, and its time step", event_part, YELLOW),
            ("5", "Feed: mass at the wall's nodes, up to Feed level", feed_part, S.GREEN),
            ("6", "Dust explosion: a pressure pulse on every face, outwards", blast_part, S.GREEN),
            ("7", "Tractor impact: a half-sine force on a leg, along -X", knock_part, S.GREEN),
            ("8", "Transient analysis: Newmark, Rayleigh damping", analysis_part, S.BLUE),
            ("9", "Results: each event in its own view, at its Step", results_part, S.GREY)]:
        S.group(doc, key + " - " + name, objects, colour)
    S.note(doc, s.title, ABOUT, 300, 60, 900, 200)


ABOUT = (
    "Silo - a feed silo of glass fibre on four steel legs, and two sudden loads: a dust explosion inside it, and a "
    "tractor reversing into a leg.\n\n"
    "The silo: a cylinder 2.2 m across and 2.4 m tall, a 60 degree hopper down to the outlet, 1 m above the ground, and "
    "a 30 degree roof up to the manhole, left open. Its wall is 8 mm of glass fibre, as ASD Shells. Four legs of "
    "114.3 x 5 tube stand round it, built in at their feet, bolted by brackets to steel rings round the bottom and the "
    "top of the cylinder, and joined by ties low down. The feed fills it to Feed level, as mass at the wall's nodes.\n\n"
    "Dust explosion: a pressure pulse, up to Peak pressure in Rise ms and gone at Duration ms, as the manhole's lid "
    "blows open, on every face of the wall along its normal. Tractor impact: a tractor of Tractor mass at Speed "
    "reversing into the leg on +X, at Impact height; its force rises and falls as half a sine, v sqrt(k m) at its "
    "peak, over pi sqrt(m / k).\n\n"
    "Event picks which acts and the time step: 1 ms for the explosion, 5 ms for the impact. A transient analysis "
    "follows the silo step by step; neither the silo's weight nor the feed's is applied - the results are what the "
    "event adds. Each event has its own view, which Stream Gate sends the results to: drag its Step to watch it.\n\n"
    "What it shows. The explosion, 0.2 bar: the wall follows the pressure as if it were held still - its own modes are "
    "far quicker than a rise of 50 ms - and the edge of the roof lifts most, 0.74 mm. The wall's stress is 4.5 MPa at "
    "most, a small share of what a laminate carries. With the manhole open, the pressure on the hopper is not balanced "
    "over the opening and pushes the silo down onto its legs: 4.2 kN held still, 7.3 kN as it bounces. The tractor, "
    "5 t at 4 km/h: 43 kN over 0.41 s, close to the period of the silo's sway on its legs, 0.42 s. The silo sways "
    "22 mm, 1.7 times what the same force held still would do, and rings on for seconds; the foot of the leg it hits "
    "bends to 15.4 kNm, just under the 16.0 kNm at which 114.3 x 5 S355 starts to yield.\n\n"
    "Try: Speed 6 km/h, and the foot passes its plastic moment, 21 kNm - the leg would kink, which this elastic model "
    "cannot show: put a bollard in front of it. Feed level 0, and the empty silo sways faster. Peak pressure 100 kPa, "
    "an explosion the lid lets out late.")


def build(doc):
    # The building blocks make their objects through Structures' emit, and read their GhPython code from its
    # GEOMETRY: both pointed here.
    S.make_object = globals().get("make_object")
    S.GEOMETRY = GEOMETRY_FILE
    S.SCALES["silo"] = S.JET
    silo(doc)


def main():
    doc = S.open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Silo built.")


if __name__ == "__main__":
    main()
