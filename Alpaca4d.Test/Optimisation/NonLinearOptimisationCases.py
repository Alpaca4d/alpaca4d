#! python3
# Test cases for Alpaca4d's Non-Linear Optimisation, built on the Grasshopper canvas.
#
# Tools/Examples/make.sh writes it straight to NonLinearOptimisationCases.gh, without Rhino:
#     Tools/Examples/make.sh Alpaca4d.Test/Optimisation/NonLinearOptimisationCases.py
# It also runs in Rhino with Grasshopper and Alpaca4d installed - in Rhino 8 from the ScriptEditor
# (open this file and press Run) or with _-RunPythonScript; in Rhino 7 with _RunPythonScript.
# It builds a Grasshopper document with one group per case: the sliders, the objective and
# constraints as Expression components, an optimiser already linked to them and set to the
# algorithm the case is for, and a panel saying what to do and what to expect. Double-click an
# optimiser, press Start, and compare the end with the panel. Save the document to keep it.
#
# Every case has a known answer, so a wrong one shows. They cover each of the seven algorithms,
# minimising and maximising, two constraints and a global constrained problem, a Galapagos gene
# pool, and three set-ups the optimiser must refuse or warn about.
#
# Written for both Python 3 (Rhino 8) and IronPython 2.7 (Rhino 7): no f-strings, ASCII only.

import System
import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Special import GH_NumberSlider, GH_Group, GH_Panel
from Grasshopper.GUI.Base import GH_SliderAccuracy
from System.Drawing import PointF, RectangleF, Color

OPTIMISER = "7C1E2A5D-3B84-4F6A-9D0E-5A2B8C4F1E63"      # Alpaca4d: Non-Linear Optimisation
EXPRESSION = "9DF5E896-552D-4c8c-B9CA-4FC147FFA022"     # Maths > Script > Expression
MASS_ADDITION = "5B850221-B527-4bd6-8C62-E94168CD6EFA"  # Maths > Operators > Mass Addition
GENE_POOL = "21553C44-EA62-475e-A8BB-62B2A3EE5CA5"      # Params > Util > Gene Pool (Galapagos)

ROW = 340          # canvas height of a case
GROUP_COLOUR = Color.FromArgb(60, 254, 0, 0)    # Alpaca red, faint
WARN_COLOUR = Color.FromArgb(60, 235, 108, 63)  # Alpaca orange, faint


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
    # itself is needed for its own members, such as the optimiser's Variables and Configure.
    obj = getattr(obj, "__implementation__", obj)
    obj.CreateAttributes()
    return obj


def place(doc, obj, x, y):
    obj.Attributes.Pivot = PointF(float(x), float(y))
    obj.Attributes.ExpireLayout()
    doc.AddObject(obj, False)
    return obj


def slider(doc, case, name, lo, hi, value, decimals, x, y, integer=False):
    s = GH_NumberSlider()
    s.CreateAttributes()
    s.NickName = name
    s.Slider.Type = GH_SliderAccuracy.Integer if integer else GH_SliderAccuracy.Float
    s.Slider.DecimalPlaces = 0 if integer else decimals
    s.Slider.Minimum = System.Decimal(float(lo))
    s.Slider.Maximum = System.Decimal(float(hi))
    s.Slider.Value = System.Decimal(float(value))
    case.append(place(doc, s, x, y))
    return s


def expression(doc, case, code, sources, x, y):
    """An Expression component set up as the canvas search sets it up from f(x,y)=...; returns its result."""
    e = emit(EXPRESSION, "The Expression component")
    e.SetInitCode(code)
    case.append(place(doc, e, x, y))
    for i in range(len(sources)):
        e.Params.Input[i].AddSource(sources[i])
    return e.Params.Output[0]


def mass_addition(doc, case, source, x, y):
    m = emit(MASS_ADDITION, "The Mass Addition component")
    case.append(place(doc, m, x, y))
    m.Params.Input[0].AddSource(source)
    return m.Params.Output[0]


def gene_pool(doc, case, name, count, lo, hi, decimals, x, y):
    g = emit(GENE_POOL, "Galapagos's Gene Pool")
    g.NickName = name
    g.Count = count
    g.Decimals = decimals
    g.Minimum = System.Decimal(float(lo))
    g.Maximum = System.Decimal(float(hi))
    case.append(place(doc, g, x, y))
    return g


def optimiser(doc, case, variables, objective, constraints, x, y,
              algorithm, maximise=False, evaluations=1000, seed=1):
    """An optimiser linked as the grips would link it, and set up as the window would set it."""
    o = emit(OPTIMISER, "Alpaca4d's Non-Linear Optimisation")
    for v in variables:
        o.Variables.Add(v.InstanceGuid)
    o.Objective = objective.InstanceGuid
    for c in constraints:
        o.Constraints.Add(c.InstanceGuid)
    o.Configure(algorithm, maximise, evaluations, seed)
    case.append(place(doc, o, x, y))
    return o


def note(doc, case, text, x, y, width=330, height=140):
    p = GH_Panel()
    p.CreateAttributes()
    p.NickName = "What to expect"
    p.SetUserText(text)
    p.Attributes.Pivot = PointF(float(x), float(y))
    p.Attributes.Bounds = RectangleF(float(x), float(y), float(width), float(height))
    doc.AddObject(p, False)
    case.append(p)
    return p


def group(doc, name, objects, colour=GROUP_COLOUR):
    g = GH_Group()
    g.CreateAttributes()
    g.NickName = name
    g.Colour = colour
    for o in objects:
        g.AddObject(o.InstanceGuid)
    doc.AddObject(g, False)
    return g


def two_sliders(doc, case, x0, y0, lo, hi, start, decimals, names=("x", "y"), lo2=None, hi2=None):
    sx = slider(doc, case, names[0], lo, hi, start[0], decimals, x0, y0 + 90)
    sy = slider(doc, case, names[1], lo if lo2 is None else lo2, hi if hi2 is None else hi2,
                start[1], decimals, x0, y0 + 120)
    return sx, sy


# ---------------------------------------------------------------- the cases

def rosenbrock(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -2, 2, (-1.2, 1.0), 4)
    f = expression(doc, case, "f(x,y)=(1-x)^2+100*(y-x^2)^2", [sx, sy], x0 + 260, y0 + 95)
    optimiser(doc, case, [sx, sy], f, [], x0 + 290, y0 + 10, "Bobyqa", evaluations=1000)
    note(doc, case,
         "BOBYQA, local, minimising Rosenbrock's banana valley from (-1.2, 1).\n"
         "Expect 0 at x = 1, y = 1, in about 200 evaluations.\n"
         "Then try Nelder-Mead, Sbplx and COBYLA from the same start: all reach (1, 1), "
         "COBYLA slowest. Ctrl+Z after a run puts the sliders back at the start.",
         x0 + 520, y0 + 20)
    group(doc, "1 - Rosenbrock - BOBYQA", case)


def branin(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -5, 10, (2.5, 7.5), 3, lo2=0, hi2=15)
    f = expression(doc, case,
                   "f(x,y)=(y-5.1/(4*Pi^2)*x^2+5/Pi*x-6)^2+10*(1-1/(8*Pi))*Cos(x)+10",
                   [sx, sy], x0 + 330, y0 + 95)   # its formula makes it wide: clear of the sliders
    optimiser(doc, case, [sx, sy], f, [], x0 + 290, y0 + 10, "DirectL", evaluations=500)
    note(doc, case,
         "DIRECT-L, global, on Branin: three valleys of the same depth.\n"
         "Expect 0.3979 at one of (-3.142, 12.275), (3.142, 2.275) or (9.425, 2.475). "
         "DIRECT-L is deterministic: running it again gives the same run. "
         "The chart shows it sampling the whole range before closing in.",
         x0 + 520, y0 + 20)
    group(doc, "2 - Branin - DIRECT-L", case)


def hill(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -5, 5, (-3.0, 4.0), 4)
    f = expression(doc, case, "f(x,y)=5-(x-2)^2-2*(y+1)^2", [sx, sy], x0 + 260, y0 + 95)
    optimiser(doc, case, [sx, sy], f, [], x0 + 290, y0 + 10, "NelderMead", maximise=True, evaluations=500)
    note(doc, case,
         "Nelder-Mead, local, MAXIMISING a hill.\n"
         "Expect 5 at x = 2, y = -1.\n"
         "Switch Goal to Minimise and run again: it ends in a corner of the sliders' range, "
         "where the hill is lowest.",
         x0 + 520, y0 + 20)
    group(doc, "3 - A hill - Nelder-Mead, maximising", case)


def tutorial(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -1, 2, (1.234, 5.678), 5, lo2=0.001, hi2=8)
    f = expression(doc, case, "f(x,y)=Sqrt(y)", [sx, sy], x0 + 260, y0 + 95)
    c1 = expression(doc, case, "f(x,y)=(2*x)^3-y", [sx, sy], x0 + 260, y0 + 165)
    c2 = expression(doc, case, "f(x,y)=(1-x)^3-y", [sx, sy], x0 + 260, y0 + 235)
    optimiser(doc, case, [sx, sy], f, [c1, c2], x0 + 290, y0 + 10, "Cobyla", evaluations=500)
    note(doc, case,
         "COBYLA, local, with TWO CONSTRAINTS: NLopt's tutorial. Minimise sqrt(y) with y above "
         "two cubics, written as (2x)^3 - y <= 0 and (1-x)^3 - y <= 0.\n"
         "Expect 0.5443 at x = 0.3333, y = 0.2963, where both constraints are 0.\n"
         "Try DIRECT-L: Start refuses, as it takes no constraints.",
         x0 + 520, y0 + 20, height=160)
    group(doc, "4 - NLopt's tutorial - COBYLA, two constraints", case)


def circle(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -2, 2, (1.5, 1.5), 4)
    f = expression(doc, case, "f(x,y)=x+y", [sx, sy], x0 + 260, y0 + 95)
    c = expression(doc, case, "f(x,y)=1-x^2-y^2", [sx, sy], x0 + 260, y0 + 165)
    optimiser(doc, case, [sx, sy], f, [c], x0 + 290, y0 + 10, "Isres", evaluations=3000, seed=1)
    note(doc, case,
         "ISRES, global, with a constraint: the lowest x + y outside the unit circle "
         "(1 - x^2 - y^2 <= 0).\n"
         "Expect about -1.414 at about (-0.707, -0.707). Orange dots in the chart break the "
         "constraint. The same seed gives the same run; another seed, a slightly different end.\n"
         "Set 20000 evaluations to try Stop, and Esc, part way.",
         x0 + 520, y0 + 20, height=170)
    group(doc, "5 - Outside the circle - ISRES, constrained", case)


def rastrigin(doc, x0, y0):
    case = []
    sx, sy = two_sliders(doc, case, x0, y0, -5.12, 5.12, (3.3, -2.7), 3)
    f = expression(doc, case, "f(x,y)=20+x^2-10*Cos(2*Pi*x)+y^2-10*Cos(2*Pi*y)",
                   [sx, sy], x0 + 260, y0 + 95)
    optimiser(doc, case, [sx, sy], f, [], x0 + 290, y0 + 10, "Crs2Lm", evaluations=1500, seed=1)
    note(doc, case,
         "CRS2-LM, global, on Rastrigin: a valley at every whole number.\n"
         "Expect 0 at (0, 0). CRS2-LM is random: if it ends in a neighbouring valley "
         "(1 or 2), try another seed.\n"
         "Then try Sbplx, local, from the same start: it stops in a nearer valley, "
         "around (3, -3), which is the point of a global algorithm.",
         x0 + 520, y0 + 20)
    group(doc, "6 - Rastrigin - CRS2-LM", case)


def genes(doc, x0, y0):
    case = []
    pool = gene_pool(doc, case, "Genes", 12, 0, 1, 4, x0, y0 + 90)
    each = expression(doc, case, "f(x)=(x-0.3)^2", [pool], x0 + 260, y0 + 95)
    f = mass_addition(doc, case, each, x0 + 380, y0 + 95)
    optimiser(doc, case, [pool], f, [], x0 + 290, y0 + 10, "Sbplx", evaluations=3000)
    note(doc, case,
         "Sbplx, local, on 12 variables from a Galapagos gene pool: one variable per gene.\n"
         "Expect 0 with every gene at 0.3 (the gene pool's own display shows them).\n"
         "This checks that gene pools are read and moved; the objective is the sum of the "
         "12 squares, through Mass Addition.",
         x0 + 560, y0 + 20)
    group(doc, "7 - A gene pool of 12 - Sbplx", case)
    return each   # a list of 12 values, for the case that must be refused


def refusals(doc, x0, y0, list_of_twelve):
    case = []
    n = slider(doc, case, "n", 0, 10, 8, 0, x0, y0 + 90, integer=True)
    f = expression(doc, case, "f(n)=(n-3.4)^2", [n], x0 + 260, y0 + 95)
    c = expression(doc, case, "f(n)=n-5", [n], x0 + 260, y0 + 165)
    optimiser(doc, case, [n], f, [], x0 + 290, y0 + 10, "Bobyqa", evaluations=200)
    optimiser(doc, case, [n], f, [c], x0 + 470, y0 + 10, "DirectL", evaluations=200)
    optimiser(doc, case, [n], list_of_twelve, [], x0 + 650, y0 + 10, "NelderMead", evaluations=200)
    note(doc, case,
         "Three set-ups the optimiser must not run quietly. Start each:\n"
         "- left, BOBYQA on a whole-number slider: runs, but warns that the slider is too "
         "coarse for a local algorithm; it ends near n = 3.\n"
         "- middle, DIRECT-L with a constraint: refuses, as DIRECT-L takes no constraints.\n"
         "- right, objective linked to the 12 values of case 7: refuses, as an objective "
         "must be one number.",
         x0 + 840, y0 + 20, width=360, height=190)
    group(doc, "8 - What must be refused or warned about", case, WARN_COLOUR)


def build(doc):
    rosenbrock(doc, 0, 0 * ROW)
    branin(doc, 0, 1 * ROW)
    hill(doc, 0, 2 * ROW)
    tutorial(doc, 0, 3 * ROW)
    circle(doc, 1000, 0 * ROW)
    rastrigin(doc, 1000, 1 * ROW)
    twelve = genes(doc, 1000, 2 * ROW)
    refusals(doc, 1000, 3 * ROW, twelve)


def main():
    doc = open_canvas()
    build(doc)
    doc.NewSolution(False)
    Grasshopper.Instances.ActiveCanvas.Refresh()
    print("Non-Linear Optimisation: 8 test cases built. Double-click an optimiser, press Start, "
          "and compare the end with the panel beside it. Without a license, Start refuses.")


if __name__ == "__main__":
    main()
