#! python3
# The benchmarks of the documentation. Builds each one as a Grasshopper definition made of Alpaca4d
# components plus Grasshopper's own geometry, mesh and maths, saves it, solves it and reads the results
# back into results.json, which tables.py turns into the documentation's tables. See README.md.
#
# Runs inside Rhino 8, from a terminal:
#     "/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode" script benchmarks.py
# Nothing is put on the canvas. Everything is written to <temp>/alpaca4d-benchmark/.
import traceback, json, os, math
import System

OUT = os.path.join(str(System.IO.Path.GetTempPath()), "alpaca4d-benchmark") + os.sep
os.makedirs(OUT, exist_ok=True)

# Which benchmarks to run: those the documentation quotes unless run.txt lists others, one per
# line. A line can change the model too - "plate-vibration:n=32" - and that variant is saved under
# variants/ instead, so the definitions to publish are only ever the ones built with the defaults.
DOCUMENTED = ["beam-simply", "beam-cantilever", "beam-vibration", "beam-vibration:n=40",
              "plate-pressure", "plate-pressure:n=8", "plate-pressure:n=32",
              "plate-pressure:support=soft", "plate-pressure:n=32,support=soft", "plate-vibration", "plate-vibration:n=32",
              "brick-cantilever", "brick-cantilever:n=40,m=8", "brick-cantilever:nu=0",
              "solid-plate-vibration", "solid-plate-vibration:n=16,layers=4"]
RUN = None
if os.path.exists(OUT + "run.txt"):
    RUN = [x.strip() for x in open(OUT + "run.txt").read().split() if x.strip()]
os.makedirs(OUT + "variants", exist_ok=True)


def options(entry):
    key, _, rest = entry.partition(":")
    opts = {}
    for pair in filter(None, rest.split(",")):
        k, _, v = pair.partition("=")
        opts[k] = float(v) if v.replace(".", "", 1).replace("-", "", 1).isdigit() else v
    return key, opts

# Run Analysis moves Rhino's working folder to the document's; put it back afterwards.
_cwd = System.IO.Directory.GetCurrentDirectory()

try:
    from System.Drawing import PointF, RectangleF
    import Grasshopper
    from Grasshopper.Kernel import GH_Document, GH_DocumentIO, GH_RuntimeMessageLevel, GH_DataMapping
    from Grasshopper.Kernel.Parameters import Param_Number, Param_Integer, Param_Vector, Param_Rectangle
    from Grasshopper.Kernel.Special import GH_Panel, GH_Scribble
    from Grasshopper.Kernel.Types import GH_Number, GH_Integer, GH_Vector, GH_Boolean, GH_Point, GH_String, GH_Rectangle, GH_Plane
    import Rhino.Geometry as rg

    server = Grasshopper.Instances.ComponentServer

    # ------------------------------------------------------------------------------------ building

    def proxy(name, category=None, guid=None):
        for p in server.ObjectProxies:
            if guid and str(p.Guid) == guid:
                return p
            if not guid and p.Desc.Name == name and (category is None or p.Desc.Category == category) and not p.Obsolete:
                return p
        raise Exception("no component " + str(name or guid))

    # By GUID where a name is shared with an obsolete component, or with every material.
    UNIAXIAL = "4f1e13f9-0b35-4f8f-a324-883cea2f8777"
    ND = "b40e1124-e203-4e1a-8962-2a6fb8d751a5"
    POINT_LOAD = "069017b3-4a2f-40fe-87ba-126100908bc8"
    MESH_LOAD = "c8514dea-1b1b-45a6-abc1-8004c1efc617"

    class Definition:
        def __init__(self, title, note):
            self.doc = GH_Document()
            s = GH_Scribble()
            s.Text = title + "\n" + note
            s.Font = System.Drawing.Font("Arial", 16)
            s.CreateAttributes(); s.Attributes.Pivot = PointF(0, -160)
            self.doc.AddObject(s, False)

        def add(self, obj, x, y, nick=None):
            if nick:
                obj.NickName = nick
            obj.CreateAttributes()
            obj.Attributes.Pivot = PointF(x, y)
            self.doc.AddObject(obj, False)
            return obj

        def comp(self, name, x, y, category=None, guid=None):
            return self.add(proxy(name, category, guid).CreateInstance(), x, y)

        def number(self, value, x, y, nick):
            p = self.add(Param_Number(), x, y, nick); set_(p, GH_Number(value)); return p

        def integer(self, value, x, y, nick):
            p = self.add(Param_Integer(), x, y, nick); set_(p, GH_Integer(value)); return p

        def vector(self, v, x, y, nick):
            p = self.add(Param_Vector(), x, y, nick); set_(p, *[GH_Vector(rg.Vector3d(*vv)) for vv in v]); return p

        def panel(self, source, x, y, label, w=180, h=80):
            p = GH_Panel()
            p.NickName = label
            p.CreateAttributes()
            p.Attributes.Pivot = PointF(x, y)
            p.Attributes.Bounds = RectangleF(x, y, w, h)
            self.doc.AddObject(p, False)
            p.AddSource(source)
            return p

        def solve(self, name, opts):
            path = OUT + name if not opts else \
                OUT + "variants" + os.sep + name.replace(".gh", "_" + "_".join("%s%s" % kv for kv in sorted(opts.items())) + ".gh")
            io = GH_DocumentIO(self.doc)
            io.SaveQuiet(path)
            self.doc.FilePath = path
            self.doc.Enabled = True
            self.doc.NewSolution(True)
            msgs = []
            for obj in self.doc.Objects:
                if hasattr(obj, "RuntimeMessages"):
                    for level in [GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning]:
                        for m in obj.RuntimeMessages(level):
                            msgs.append("%s %s: %s" % (level, obj.NickName, m))
            io.SaveQuiet(path)
            return msgs

    def set_(param, *goos):
        param.PersistentData.Clear()
        for g in goos:
            param.PersistentData.Append(g)

    def link(target, *sources):
        for s in sources:
            target.AddSource(s)

    def numbers(param):
        return [float(g.Value) for g in param.VolatileData.AllData(True)]

    def uniaxial(d, E, nu, rho, x, y, name):
        m = d.comp(None, x, y, guid=UNIAXIAL)
        set_(m.Params.Input[0], GH_String(name))
        set_(m.Params.Input[1], GH_Number(E)); set_(m.Params.Input[2], GH_Number(E))
        set_(m.Params.Input[4], GH_Number(E / (2 * (1 + nu)))); set_(m.Params.Input[5], GH_Number(nu))
        set_(m.Params.Input[6], GH_Number(rho))
        return m

    def nd(d, E, nu, rho, x, y, name):
        m = d.comp(None, x, y, guid=ND)
        set_(m.Params.Input[0], GH_String(name))
        set_(m.Params.Input[1], GH_Number(E)); set_(m.Params.Input[2], GH_Number(E / (2 * (1 + nu))))
        set_(m.Params.Input[3], GH_Number(nu)); set_(m.Params.Input[4], GH_Number(rho))
        return m

    def support(d, source, flags, x, y):
        s = d.comp("Support (Alpaca4d)", x, y)
        link(s.Params.Input[0], source)
        for i, v in enumerate(flags):
            set_(s.Params.Input[1 + i], GH_Boolean(v))
        return s

    def assemble(d, elements, supports, patterns, x, y):
        a = d.comp("AssembleModel (Alpaca4d)", x, y)
        link(a.Params.Input[0], *elements)
        link(a.Params.Input[1], *supports)
        if patterns:
            link(a.Params.Input[2], *patterns)
        # Inputs from different branch depths would otherwise assemble one model per branch.
        for i in range(3):
            a.Params.Input[i].DataMapping = GH_DataMapping.Flatten
        return a

    def static(d, elements, supports, patterns, x, y):
        a = assemble(d, elements, supports, patterns, x, y)
        settings = d.comp("Analysis Settings (Alpaca4d)", x, y + 220)
        run = d.comp(" Run Analysis (Alpaca4d)", x + 200, y)
        link(run.Params.Input[0], a.Params.Output[0])
        link(run.Params.Input[1], settings.Params.Output[0])
        return run

    def modal(d, elements, supports, modes, x, y, solver=None):
        a = assemble(d, elements, supports, None, x, y)
        nv = d.comp("Natural Vibration Analysis (Alpaca4d)", x + 200, y)
        link(nv.Params.Input[0], a.Params.Output[0])
        set_(nv.Params.Input[1], GH_Integer(modes))
        if solver:
            set_(nv.Params.Input[2], GH_String(solver))
        d.panel(nv.Params.Output[4], x + 420, y, "Frequencies [Hz]", 180, 160)
        return nv

    def displacements(d, run, x, y):
        nd_ = d.comp("Nodal Displacements (Alpaca4d)", x, y)
        link(nd_.Params.Input[0], run.Params.Output[1])
        dz = d.comp("Deconstruct Vector", x + 220, y + 40, "Vector")
        link(dz.Params.Input[0], nd_.Params.Output[1])
        b = d.comp("Bounds", x + 400, y + 40, "Maths")
        link(b.Params.Input[0], dz.Params.Output[2])
        d.panel(b.Params.Output[0], x + 560, y + 20, "Uz range [m]")
        return nd_

    def node_values(nd_):
        pos = [g.Value for g in nd_.Params.Output[0].VolatileData.AllData(True)]
        dsp = [g.Value for g in nd_.Params.Output[1].VolatileData.AllData(True)]
        return pos, dsp

    def at(pos, dsp, point, tol=1e-6):
        for p, v in zip(pos, dsp):
            if p.DistanceTo(point) < tol:
                return v
        raise Exception("no node at %s" % point)

    # ------------------------------------------------------------------------------------ beams

    def beam_static(kind, o):
        L, q, E, nu = 5.0, 6.0, 2.07e8, 0.3
        n = int(o.get("n", 10))
        title = "Alpaca4d benchmark - %s beam" % ("simply supported" if kind == "simply" else "cantilever")
        d = Definition(title, "q = 6 kN/m, L = 5 m, E = 2.07e5 N/mm2, 300 x 300 section (I = 6.75e8 mm4), %d elements" % n)
        pL = d.number(L, 0, 0, "L [m]"); pn = d.integer(n, 0, 60, "Elements")
        pq = d.vector([(0, 0, -q)], 0, 120, "q [kN/m]")
        line = d.comp("Line SDL", 200, 0, "Curve")
        set_(line.Params.Input[0], GH_Point(rg.Point3d(0, 0, 0))); set_(line.Params.Input[1], GH_Vector(rg.Vector3d(1, 0, 0)))
        link(line.Params.Input[2], pL)
        div = d.comp("Divide Curve", 380, 0, "Curve"); link(div.Params.Input[0], line.Params.Output[0]); link(div.Params.Input[1], pn)
        sh = d.comp("Shatter", 560, 0, "Curve"); link(sh.Params.Input[0], line.Params.Output[0]); link(sh.Params.Input[1], div.Params.Output[2])
        ends = d.comp("End Points", 380, 140, "Curve"); link(ends.Params.Input[0], line.Params.Output[0])
        mat = uniaxial(d, E, nu, 7850, 200, 260, "E 2.07e5")
        sec = d.comp("RectangleCS (Alpaca4d)", 380, 260)
        set_(sec.Params.Input[0], GH_String("300 x 300")); set_(sec.Params.Input[1], GH_Number(0.3)); set_(sec.Params.Input[2], GH_Number(0.3))
        link(sec.Params.Input[3], mat.Params.Output[0])
        beam = d.comp("ForceBeamColumn (Alpaca4d)", 760, 60)
        link(beam.Params.Input[0], sh.Params.Output[0]); link(beam.Params.Input[1], sec.Params.Output[0])
        if kind == "simply":
            # Pinned at the start - torsion held there too, or the beam could spin about its axis -
            # and on a roller at the end.
            sups = [support(d, ends.Params.Output[0], [True, True, True, True, False, False], 760, 220),
                    support(d, ends.Params.Output[1], [False, True, True, False, False, False], 760, 420)]
        else:
            sups = [support(d, ends.Params.Output[0], [True] * 6, 760, 220)]
        load = d.comp("Linear Load (Alpaca4d)", 760, -100)
        link(load.Params.Input[0], beam.Params.Output[0]); link(load.Params.Input[1], pq)
        pat = d.comp("Load Pattern (Alpaca4d)", 960, -100); link(pat.Params.Input[1], load.Params.Output[0])
        run = static(d, [beam.Params.Output[0]], [s.Params.Output[0] for s in sups], [pat.Params.Output[0]], 1160, 60)
        forces = d.comp("Beam Forces (Alpaca4d)", 1560, -140); link(forces.Params.Input[0], output(run, "AlpacaModel"))
        # The beam lies along X with no ZAxis, so local y points up and gravity bends it about z.
        for i, (k, label) in enumerate([(1, "Vy range [kN]"), (5, "Mz range [kNm]")]):
            b = d.comp("Bounds", 1780, -180 + 100 * i, "Maths")
            link(b.Params.Input[0], forces.Params.Output[k]); b.Params.Input[0].DataMapping = GH_DataMapping.Flatten
            d.panel(b.Params.Output[0], 1960, -200 + 100 * i, label)
        disp = displacements(d, run, 1560, 160)

        name = "Alpaca4d_Benchmark_SimplySupported.gh" if kind == "simply" else "Alpaca4d_Benchmark_Cantilever.gh"
        msgs = d.solve(name, o)
        pos, dsp = node_values(disp)
        x = L / 2 if kind == "simply" else L
        EI = E * 0.3 ** 4 / 12
        GkA = E / (2 * (1 + nu)) * 0.09 * 5.0 / 6.0
        th = dict(V=q * L / 2, M=q * L * L / 8, db=5 * q * L ** 4 / (384 * EI), ds=q * L * L / (8 * GkA)) if kind == "simply" \
            else dict(V=q * L, M=q * L * L / 2, db=q * L ** 4 / (8 * EI), ds=q * L * L / (2 * GkA))
        return dict(name="beam-" + kind, file=name, messages=msgs,
                    V=max(abs(v) for v in numbers(forces.Params.Output[2])),
                    M=max(abs(v) for v in numbers(forces.Params.Output[4])),
                    d=abs(at(pos, dsp, rg.Point3d(x, 0, 0)).Z), theory=th)

    def beam_vibration(o):
        # A slender steel cantilever, L/h = 50, so that Euler-Bernoulli is the right theory.
        L, b, E, nu, rho = 5.0, 0.1, 2.1e8, 0.3, 7850.0
        n = int(o.get("n", 20))
        d = Definition("Alpaca4d benchmark - cantilever beam, natural vibration",
                       "L = 5 m, 100 x 100 mm steel bar, E = 2.1e5 N/mm2, rho = 7850 kg/m3, %d elements" % n)
        pL = d.number(L, 0, 0, "L [m]"); pn = d.integer(n, 0, 60, "Elements")
        line = d.comp("Line SDL", 200, 0, "Curve")
        set_(line.Params.Input[0], GH_Point(rg.Point3d(0, 0, 0))); set_(line.Params.Input[1], GH_Vector(rg.Vector3d(1, 0, 0)))
        link(line.Params.Input[2], pL)
        div = d.comp("Divide Curve", 380, 0, "Curve"); link(div.Params.Input[0], line.Params.Output[0]); link(div.Params.Input[1], pn)
        sh = d.comp("Shatter", 560, 0, "Curve"); link(sh.Params.Input[0], line.Params.Output[0]); link(sh.Params.Input[1], div.Params.Output[2])
        ends = d.comp("End Points", 380, 140, "Curve"); link(ends.Params.Input[0], line.Params.Output[0])
        mat = uniaxial(d, E, nu, rho, 200, 260, "S275")
        sec = d.comp("RectangleCS (Alpaca4d)", 380, 260)
        set_(sec.Params.Input[0], GH_String("100 x 100")); set_(sec.Params.Input[1], GH_Number(b)); set_(sec.Params.Input[2], GH_Number(b))
        link(sec.Params.Input[3], mat.Params.Output[0])
        beam = d.comp("ForceBeamColumn (Alpaca4d)", 760, 60)
        link(beam.Params.Input[0], sh.Params.Output[0]); link(beam.Params.Input[1], sec.Params.Output[0])
        sup = support(d, ends.Params.Output[0], [True] * 6, 760, 220)
        nv = modal(d, [beam.Params.Output[0]], [sup.Params.Output[0]], 6, 1000, 60)
        name = "Alpaca4d_Benchmark_CantileverVibration.gh"
        msgs = d.solve(name, o)
        EI = E * 1e3 * b ** 4 / 12                     # N m2
        m = rho * b * b                                # kg/m
        lam = [1.875104069, 4.694091133, 7.854757438]
        th = [l * l / (2 * math.pi * L * L) * math.sqrt(EI / m) for l in lam]
        return dict(name="beam-vibration", file=name, messages=msgs, f=numbers(nv.Params.Output[4]), theory=th)

    # ------------------------------------------------------------------------------------ shells

    def plate_mesh(d, a, b, nx, ny, x, y, z=0.0):
        r = d.add(Param_Rectangle(), x, y, "Plate")
        set_(r, GH_Rectangle(rg.Rectangle3d(rg.Plane(rg.Point3d(0, 0, z), rg.Vector3d.ZAxis), rg.Interval(0, a), rg.Interval(0, b))))
        mp = d.comp("Mesh Plane", x + 180, y, "Mesh")
        link(mp.Params.Input[0], r); set_(mp.Params.Input[1], GH_Integer(nx)); set_(mp.Params.Input[2], GH_Integer(ny))
        return r, mp

    def plate_pressure(o):
        # ASDEA ST6 / Timoshenko & Woinowsky-Krieger: simply supported square plate, uniform pressure.
        a, t, E, nu, p = 0.8, 0.008, 2.1e8, 0.3, 1.0
        n = int(o.get("n", 16))
        d = Definition("Alpaca4d benchmark - simply supported square plate under uniform pressure",
                       "0.8 x 0.8 m, t = 8 mm, E = 2.1e5 N/mm2, nu = 0.3, p = 1 kN/m2, %d x %d ASDShellQ4" % (n, n))
        r, mp = plate_mesh(d, a, a, n, n, 0, 0)
        mat = nd(d, E, nu, 7850, 0, 200, "steel")
        sec = d.comp("Plate Fiber Section (Alpaca4d)", 200, 200)
        set_(sec.Params.Input[0], GH_String("t 8 mm")); set_(sec.Params.Input[1], GH_Number(t)); link(sec.Params.Input[2], mat.Params.Output[0])
        shell = d.comp("ASD Shell (Alpaca4d)", 420, 60)
        link(shell.Params.Input[0], mp.Params.Output[0]); link(shell.Params.Input[1], sec.Params.Output[0])
        # The "hard" simple support of plate theory: no transverse displacement along the edges, and no
        # rotation about the edge's normal either, since w = 0 all along an edge means it cannot tilt
        # along it. Left free, that rotation gives a Mindlin shell the "soft" support, whose boundary
        # layer a fine mesh resolves and Kirchhoff's solution does not have. The in-plane displacements
        # are held too, which a flat plate in linear bending never notices and which keeps it from sliding.
        # Divide Curve numbers the perimeter from the corner at the origin, anticlockwise: corners at
        # 0, n, 2n and 3n, the edges along x before n and between 2n and 3n, the edges along y between.
        edge = d.comp("Divide Curve", 200, 400, "Curve")
        link(edge.Params.Input[0], r); set_(edge.Params.Input[1], GH_Integer(4 * n))
        groups = [("Edges along x", [i for k in (0, 2) for i in range(k * n + 1, (k + 1) * n)], [True, True, True, False, True, False]),
                  ("Edges along y", [i for k in (1, 3) for i in range(k * n + 1, (k + 1) * n)], [True, True, True, True, False, False]),
                  ("Corners", [0, n, 2 * n, 3 * n], [True, True, True, True, True, False])]
        if o.get("support") == "soft":
            groups = [(label, idx, [True, True, True, False, False, False]) for label, idx, _ in groups]
        sups = []
        for j, (label, idx, flags) in enumerate(groups):
            item = d.comp("List Item", 200, 520 + 120 * j, "Sets")
            item.NickName = label
            link(item.Params.Input[0], edge.Params.Output[0]); set_(item.Params.Input[1], *[GH_Integer(i) for i in idx])
            sups.append(support(d, item.Params.Output[0], flags, 420, 400 + 160 * j))
        pq = d.vector([(0, 0, -p)], 420, -100, "p [kN/m2]")
        load = d.comp(None, 620, -100, guid=MESH_LOAD)
        link(load.Params.Input[0], shell.Params.Output[0]); link(load.Params.Input[1], pq)
        pat = d.comp("Load Pattern (Alpaca4d)", 820, -100); link(pat.Params.Input[1], load.Params.Output[0])
        run = static(d, [shell.Params.Output[0]], [x.Params.Output[0] for x in sups], [pat.Params.Output[0]], 820, 60)
        disp = displacements(d, run, 1240, 60)
        name = "Alpaca4d_Benchmark_PlatePressure.gh"
        msgs = d.solve(name, o)
        pos, dsp = node_values(disp)
        D = E * t ** 3 / (12 * (1 - nu ** 2))
        # Navier's double series for the centre of a simply supported square plate.
        alpha = sum(16 / math.pi ** 6 * (-1) ** ((i + j) // 2 - 1) / (i * j * (i * i + j * j) ** 2)
                    for i in range(1, 200, 2) for j in range(1, 200, 2))
        return dict(name="plate-pressure", file=name, messages=msgs, d=abs(at(pos, dsp, rg.Point3d(a / 2, a / 2, 0)).Z),
                    theory=dict(alpha=alpha, d=alpha * p * a ** 4 / D), elements=n * n)

    def plate_vibration(o):
        # NAFEMS FV16: cantilevered thin square plate.
        a, t, E, nu, rho = 10.0, 0.05, 2.0e8, 0.3, 8000.0
        n = int(o.get("n", 16))
        d = Definition("Alpaca4d benchmark - NAFEMS FV16, cantilevered thin square plate",
                       "10 x 10 m, t = 0.05 m, E = 2.0e5 N/mm2, nu = 0.3, rho = 8000 kg/m3, clamped along x = 0, %d x %d ASDShellQ4" % (n, n))
        r, mp = plate_mesh(d, a, a, n, n, 0, 0)
        mat = nd(d, E, nu, rho, 0, 200, "steel")
        sec = d.comp("Plate Fiber Section (Alpaca4d)", 200, 200)
        set_(sec.Params.Input[0], GH_String("t 50 mm")); set_(sec.Params.Input[1], GH_Number(t)); link(sec.Params.Input[2], mat.Params.Output[0])
        shell = d.comp("ASD Shell (Alpaca4d)", 420, 60)
        link(shell.Params.Input[0], mp.Params.Output[0]); link(shell.Params.Input[1], sec.Params.Output[0])
        root = d.comp("Line", 0, 400, "Curve")
        set_(root.Params.Input[0], GH_Point(rg.Point3d(0, 0, 0))); set_(root.Params.Input[1], GH_Point(rg.Point3d(0, a, 0)))
        rd = d.comp("Divide Curve", 200, 400, "Curve"); link(rd.Params.Input[0], root.Params.Output[0]); set_(rd.Params.Input[1], GH_Integer(n))
        sup = support(d, rd.Params.Output[0], [True] * 6, 420, 300)
        nv = modal(d, [shell.Params.Output[0]], [sup.Params.Output[0]], 6, 640, 60)
        name = "Alpaca4d_Benchmark_PlateVibration_FV16.gh"
        msgs = d.solve(name, o)
        return dict(name="plate-vibration", file=name, messages=msgs, f=numbers(nv.Params.Output[4]),
                    theory=[0.421, 1.029, 2.582, 3.306, 3.753, 6.555], elements=n * n)

    # ------------------------------------------------------------------------------------ bricks

    def brick_stack(d, base_rect, nu_, nv_, count, step, direction, x, y):
        """A planar mesh repeated count + 1 times along direction, and the bricks between them."""
        r = d.add(Param_Rectangle(), x, y, "Section"); set_(r, GH_Rectangle(base_rect))
        mp = d.comp("Mesh Plane", x + 180, y, "Mesh")
        link(mp.Params.Input[0], r); set_(mp.Params.Input[1], GH_Integer(nu_)); set_(mp.Params.Input[2], GH_Integer(nv_))
        series = d.comp("Series", x + 180, y + 160, "Sets")
        set_(series.Params.Input[0], GH_Number(0.0)); set_(series.Params.Input[1], GH_Number(step)); set_(series.Params.Input[2], GH_Integer(count + 1))
        unit = d.comp(direction, x + 360, y + 160, "Vector")
        link(unit.Params.Input[0], series.Params.Output[0])
        move = d.comp("Move", x + 540, y, "Transform")
        link(move.Params.Input[0], mp.Params.Output[0]); link(move.Params.Input[1], unit.Params.Output[0])
        stack = d.comp("MeshSeriesToBrick (Alpaca4d)", x + 720, y)
        link(stack.Params.Input[0], move.Params.Output[0])
        return r, move, stack

    def layer(d, move, index, x, y):
        item = d.comp("List Item", x, y, "Sets")
        link(item.Params.Input[0], move.Params.Output[0]); set_(item.Params.Input[1], GH_Integer(index))
        dm = d.comp("Deconstruct Mesh", x + 180, y, "Mesh")
        link(dm.Params.Input[0], item.Params.Output[0])
        return dm

    def brick_cantilever(o):
        # A steel bar of SSP bricks, end-loaded, against Timoshenko beam theory.
        L, h, E, P = 2.0, 0.2, 2.1e8, 10.0
        n, m, nu = int(o.get("n", 20)), int(o.get("m", 4)), o.get("nu", 0.3)
        d = Definition("Alpaca4d benchmark - cantilever of SSP bricks under an end load",
                       "L = 2 m, 200 x 200 mm steel bar, E = 2.1e5 N/mm2, nu = %g, P = 10 kN, %d x %d x %d SSP bricks" % (nu, n, m, m))
        rect = rg.Rectangle3d(rg.Plane.WorldYZ, rg.Interval(-h / 2, h / 2), rg.Interval(-h / 2, h / 2))
        _, move, stack = brick_stack(d, rect, m, m, n, L / n, "Unit X", 0, 0)
        mat = nd(d, E, nu, 7850, 540, 220, "steel")
        brick = d.comp("SSP Brick (Alpaca4d)", 1100, 0)
        link(brick.Params.Input[0], stack.Params.Output[0]); link(brick.Params.Input[1], mat.Params.Output[0])
        root = layer(d, move, 0, 900, 260)
        sup = support(d, root.Params.Output[0], [True, True, True, False, False, False], 1300, 260)
        tip = layer(d, move, n, 900, 460)
        # The end load as the nodal forces of a uniform shear traction on the end face: bilinear
        # weights 1, 2, ..., 2, 1 each way, so the corners carry a quarter of an interior node's share.
        w = [1.0] + [2.0] * (m - 1) + [1.0]
        total = sum(w) ** 2
        forces = [(0, 0, -P * wi * wj / total) for wj in w for wi in w]
        pf = d.vector(forces, 1100, 560, "P per node [kN]")
        pl = d.comp(None, 1300, 460, guid=POINT_LOAD)
        link(pl.Params.Input[0], tip.Params.Output[0]); link(pl.Params.Input[1], pf)
        pat = d.comp("Load Pattern (Alpaca4d)", 1500, 460); link(pat.Params.Input[1], pl.Params.Output[0])
        run = static(d, [brick.Params.Output[0]], [sup.Params.Output[0]], [pat.Params.Output[0]], 1700, 0)
        disp = displacements(d, run, 2100, 0)
        name = "Alpaca4d_Benchmark_BrickCantilever.gh"
        msgs = d.solve(name, o)
        pos, dsp = node_values(disp)
        tipz = [v.Z for p, v in zip(pos, dsp) if abs(p.X - L) < 1e-6]
        I = h ** 4 / 12
        Gk = E / (2 * (1 + nu)) * 5.0 / 6.0
        return dict(name="brick-cantilever", file=name, messages=msgs,
                    d_centre=abs(at(pos, dsp, rg.Point3d(L, 0, 0)).Z), d_mean=abs(sum(tipz) / len(tipz)),
                    theory=dict(db=P * L ** 3 / (3 * E * I), ds=P * L / (Gk * h * h)), elements=n * m * m)

    def solid_plate_vibration(o):
        # NAFEMS FV52: simply supported "solid" square plate.
        a, t, E, nu, rho = 10.0, 1.0, 2.0e8, 0.3, 8000.0
        n, layers = int(o.get("n", 8)), int(o.get("layers", 3))
        d = Definition("Alpaca4d benchmark - NAFEMS FV52, simply supported solid square plate",
                       "10 x 10 x 1 m, E = 2.0e5 N/mm2, nu = 0.3, rho = 8000 kg/m3, uz = 0 along the edges of the bottom face, "
                       "%d x %d x %d SSP bricks" % (n, n, layers))
        rect = rg.Rectangle3d(rg.Plane(rg.Point3d(0, 0, -t / 2), rg.Vector3d.ZAxis), rg.Interval(0, a), rg.Interval(0, a))
        r, move, stack = brick_stack(d, rect, n, n, layers, t / layers, "Unit Z", 0, 0)
        mat = nd(d, E, nu, rho, 540, 220, "steel")
        brick = d.comp("SSP Brick (Alpaca4d)", 1100, 0)
        link(brick.Params.Input[0], stack.Params.Output[0]); link(brick.Params.Input[1], mat.Params.Output[0])
        # uz = 0 along the edges of the bottom face and nothing else, as NAFEMS has it: the plate is
        # free to slide and spin in its own plane, and those three rigid-body motions are modes 1 to 3.
        # support=mid holds the mid-plane edges instead, as STKO's verification does (layers even).
        if o.get("support") == "mid":
            r = d.add(Param_Rectangle(), 0, 400, "Mid-plane")
            set_(r, GH_Rectangle(rg.Rectangle3d(rg.Plane.WorldXY, rg.Interval(0, a), rg.Interval(0, a))))
        edge = d.comp("Divide Curve", 200, 400, "Curve"); link(edge.Params.Input[0], r); set_(edge.Params.Input[1], GH_Integer(4 * n))
        sup = support(d, edge.Params.Output[0], [False, False, True, False, False, False], 420, 400)
        nv = modal(d, [brick.Params.Output[0]], [sup.Params.Output[0]], 10, 1300, 0, o.get("solver"))
        name = "Alpaca4d_Benchmark_SolidPlateVibration_FV52.gh"
        msgs = d.solve(name, o)
        return dict(name="solid-plate-vibration", file=name, messages=msgs, f=numbers(nv.Params.Output[4]),
                    eigen=numbers(nv.Params.Output[2]),
                    theory=[0, 0, 0, 44.092, 106.66, 106.66, 156.23, 193.58, 200.13, 200.13], elements=n * n * layers)

    # ------------------------------------------------------------------------------------ run

    builders = {"beam-simply": lambda o: beam_static("simply", o), "beam-cantilever": lambda o: beam_static("cantilever", o),
                "beam-vibration": beam_vibration, "plate-pressure": plate_pressure, "plate-vibration": plate_vibration,
                "brick-cantilever": brick_cantilever, "solid-plate-vibration": solid_plate_vibration}
    results = {}
    if os.path.exists(OUT + "results.json"):
        results = json.load(open(OUT + "results.json"))
    for entry in RUN or DOCUMENTED:
        key, opts = options(entry)
        try:
            results[entry] = builders[key](opts)
        except Exception:
            results[entry] = dict(name=key, error=traceback.format_exc())
        open(OUT + "results.json", "w").write(json.dumps(results, indent=1, default=str))
except Exception:
    open(OUT + "error.txt", "w").write(traceback.format_exc())
finally:
    System.IO.Directory.SetCurrentDirectory(_cwd)
