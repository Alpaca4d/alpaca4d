// The optimisation algorithms behind Non-Linear Optimisation: NLopt's derivative-free algorithms,
// ported to C#.
//
// A port is only worth its name if it does what NLopt does, so with NLOPT set to an NLopt library
// every case below is run through both and compared point by point: the same points evaluated
// in the same order, the same result, the same best. Without NLOPT only the checks that need no
// reference are run - that each algorithm finds the minimum of problems whose minimum is known,
// never steps outside the bounds, and stops when and how it is told to.
//
//   ./run.sh                 every algorithm
//   ./run.sh Cobyla          one (the names of OptimisationAlgorithm)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Alpaca4d.Optimisation;
using Alpaca4d.Optimisation.NLopt;

sealed class Problem
{
    public string Name;
    public int N;
    public double[] Lb, Ub, X0;
    public Func<double[], double> F;
    public int M;
    public Action<double[], double[]> C;
    public double[] Tol;
    public double FMin;

    public Problem With(string name, Func<double[], double> f)
    {
        var p = (Problem)MemberwiseClone();
        p.Name = name;
        p.F = f;
        return p;
    }
}

sealed class Settings
{
    public int MaxEval = 400;
    public double MaxTime;
    public double FtolRel = 1e-10, FtolAbs, XtolRel = 1e-8;
    public double[] XtolAbs;
    public double? StopValue;
    public double[] InitialStep;
    public int Population;
    public uint Seed = 7;
    public bool Maximise;
    public int StopAfter;

    public Settings With(Action<Settings> change)
    {
        var s = (Settings)MemberwiseClone();
        change(s);
        return s;
    }
}

class OptimisationTest
{
    static int fails;
    static int passes;

    static void Check(bool ok, string what)
    {
        if (ok) passes++;
        else fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(double[] v) => "(" + string.Join(", ", v.Select(F)) + ")";

    static double Sq(double v) => v * v;

    #region Problems

    static readonly Problem Rosenbrock = new Problem
    {
        Name = "Rosenbrock",
        N = 2,
        Lb = new[] { -2.0, -1.0 },
        Ub = new[] { 2.0, 3.0 },
        X0 = new[] { -1.2, 1.0 },
        F = x => 100 * Sq(x[1] - x[0] * x[0]) + Sq(1 - x[0]),
        FMin = 0,
    };

    // A tilted bowl in four variables: every variable matters, none the same as another.
    static readonly Problem Bowl = new Problem
    {
        Name = "a tilted bowl in 4D",
        N = 4,
        Lb = new[] { -5.0, -4.0, -6.0, -3.0 },
        Ub = new[] { 5.0, 6.0, 4.0, 7.0 },
        X0 = new[] { 1.0, -2.0, 0.5, 3.0 },
        F = x => 1.0 * Sq(x[0] - 1.3) + 2.5 * Sq(x[1] + 0.7) + 0.6 * Sq(x[2] - 2.1) + 4.0 * Sq(x[3] - 0.4)
                 + 0.3 * (x[0] - 1.3) * (x[1] + 0.7) - 0.2 * (x[2] - 2.1) * (x[3] - 0.4) + 1.75,
        FMin = 1.75,
    };

    static readonly Problem Branin = new Problem
    {
        Name = "Branin",
        N = 2,
        Lb = new[] { -5.0, 0.0 },
        Ub = new[] { 10.0, 15.0 },
        X0 = new[] { 2.5, 7.5 },
        F = x =>
        {
            double b = 5.1 / (4 * Math.PI * Math.PI), c = 5 / Math.PI, t = 1 / (8 * Math.PI);
            return Sq(x[1] - b * x[0] * x[0] + c * x[0] - 6) + 10 * (1 - t) * Math.Cos(x[0]) + 10;
        },
        FMin = 0.397887357729738,
    };

    // Many valleys, the deepest at the origin: a local algorithm finds the one it starts in.
    static readonly Problem Rastrigin = new Problem
    {
        Name = "Rastrigin in 3D",
        N = 3,
        Lb = new[] { -5.12, -4.0, -5.0 },
        Ub = new[] { 5.12, 6.0, 4.5 },
        X0 = new[] { 1.7, -2.2, 0.9 },
        F = x => 30 + x.Sum(v => v * v - 10 * Math.Cos(2 * Math.PI * v)),
        FMin = 0,
    };

    // Rosenbrock with a third variable that is not free: its bounds are equal.
    static readonly Problem Pinned = new Problem
    {
        Name = "Rosenbrock with a pinned third variable",
        N = 3,
        Lb = new[] { -2.0, -1.0, 0.5 },
        Ub = new[] { 2.0, 3.0, 0.5 },
        X0 = new[] { -1.2, 1.0, 0.5 },
        F = x => 100 * Sq(x[1] - x[0] * x[0]) + Sq(1 - x[0]) + Sq(x[2] - 0.5),
        FMin = 0,
    };

    // NLopt's tutorial problem: minimise sqrt(x2) above two cubics, x2 >= (a x1 + b)^3.
    static readonly Problem Tutorial = new Problem
    {
        Name = "NLopt's tutorial, two constraints",
        N = 2,
        Lb = new[] { -2.0, 1e-6 },
        Ub = new[] { 2.0, 8.0 },
        X0 = new[] { 1.234, 5.678 },
        F = x => Math.Sqrt(x[1]),
        M = 2,
        C = (x, r) =>
        {
            r[0] = Math.Pow(2 * x[0] + 0, 3) - x[1];
            r[1] = Math.Pow(-1 * x[0] + 1, 3) - x[1];
        },
        Tol = new[] { 1e-8, 1e-8 },
        FMin = Math.Sqrt(8.0 / 27.0),
    };

    static Problem Negated(Problem p) => p.With("-" + p.Name + ", maximised", x => -p.F(x));

    #endregion

    static int NloptCode(OptimisationAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case OptimisationAlgorithm.DirectL: return 1;
            case OptimisationAlgorithm.Crs2Lm: return 19;
            case OptimisationAlgorithm.Cobyla: return 25;
            case OptimisationAlgorithm.NelderMead: return 28;
            case OptimisationAlgorithm.Sbplx: return 29;
            case OptimisationAlgorithm.Bobyqa: return 34;
            case OptimisationAlgorithm.Isres: return 35;
        }
        throw new ArgumentException();
    }

    /// <summary>Runs one case through the port, recording every point it evaluates.</summary>
    static Run Port(OptimisationAlgorithm algorithm, Problem p, Settings s, out Optimiser optimiser)
    {
        var run = new Run();
        var opt = new Optimiser(algorithm, p.N)
        {
            Maximise = s.Maximise,
            StopValue = s.StopValue,
            RelativeFunctionTolerance = s.FtolRel,
            AbsoluteFunctionTolerance = s.FtolAbs,
            RelativeVariableTolerance = s.XtolRel,
            AbsoluteVariableTolerance = s.XtolAbs,
            MaxEvaluations = s.MaxEval,
            MaxTime = s.MaxTime,
            InitialStep = s.InitialStep,
            Population = s.Population,
            Seed = s.Seed,
        };
        Array.Copy(p.Lb, opt.LowerBounds, p.N);
        Array.Copy(p.Ub, opt.UpperBounds, p.N);
        if (p.M > 0)
            opt.AddInequalityConstraints((x, r) => { p.C(x, r); run.ConstraintCalls++; }, p.Tol);

        var x0 = (double[])p.X0.Clone();
        run.Result = (int)opt.Optimise(x =>
        {
            if (x.Length != p.N) throw new Exception($"the objective was called with {x.Length} values for {p.N} variables");
            var copy = (double[])x.Clone();
            double v = p.F(copy);
            run.Points.Add(copy);
            run.Values.Add(v);
            if (s.StopAfter > 0 && run.Points.Count >= s.StopAfter) opt.ForceStop();
            return v;
        }, x0, out double best);
        run.X = x0;
        run.F = best;
        run.Evaluations = opt.Evaluations;
        optimiser = opt;
        return run;
    }

    static Run Port(OptimisationAlgorithm algorithm, Problem p, Settings s) => Port(algorithm, p, s, out _);

    static bool Same(double a, double b) => a == b || Math.Abs(a - b) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b))) || (double.IsNaN(a) && double.IsNaN(b));

    static bool Same(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, Same).All(same => same);

    static void Compare(Reference nlopt, OptimisationAlgorithm algorithm, Problem p, Settings s, string what)
    {
        string label = $"{AlgorithmInfo.Of(algorithm).Name}, {p.Name}{(what == null ? "" : ", " + what)}";
        Run port;
        try
        {
            port = Port(algorithm, p, s);
        }
        catch (NotImplementedException)
        {
            Check(false, $"{label}: not ported yet");
            return;
        }
        catch (Exception ex)
        {
            Check(false, $"{label}: the port threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return;
        }
        var reference = nlopt.Optimise(NloptCode(algorithm), p, s);

        int count = Math.Min(port.Points.Count, reference.Points.Count);
        int first = -1;
        bool exact = true;
        for (int k = 0; k < count; k++)
        {
            if (!Same(port.Points[k], reference.Points[k]) || !Same(port.Values[k], reference.Values[k]))
            {
                first = k;
                break;
            }
            if (!port.Points[k].SequenceEqual(reference.Points[k])) exact = false;
        }

        if (first < 0 && port.Points.Count == reference.Points.Count)
            Check(true, $"{label}: the same {count} points as NLopt{(exact ? ", to the last bit" : "")}");
        else if (first < 0)
            Check(false, $"{label}: the same first {count} points, then the port evaluated {port.Points.Count} and NLopt {reference.Points.Count}");
        else
            Check(false, $"{label}: point {first + 1} of {reference.Points.Count} differs\n" +
                         $"         port  {V(port.Points[first])} -> {F(port.Values[first])}\n" +
                         $"         NLopt {V(reference.Points[first])} -> {F(reference.Values[first])}");

        bool sameEnd = port.Result == reference.Result && Same(port.X, reference.X) && Same(port.F, reference.F)
                       && port.Evaluations == reference.Evaluations && port.ConstraintCalls == reference.ConstraintCalls;
        Check(sameEnd, $"{label}: ends as NLopt does, {(OptimisationResult)reference.Result} at {F(reference.F)} after {reference.Evaluations} evaluations" +
                       (sameEnd ? "" : $"\n         port  {(OptimisationResult)port.Result} at {F(port.F)} {V(port.X)}, {port.Evaluations} evaluations, {port.ConstraintCalls} constraint calls" +
                                       $"\n         NLopt {(OptimisationResult)reference.Result} at {F(reference.F)} {V(reference.X)}, {reference.Evaluations} evaluations, {reference.ConstraintCalls} constraint calls"));
    }

    static void Main(string[] args)
    {
        var only = args.Length > 0
            ? args.Select(a => (OptimisationAlgorithm)Enum.Parse(typeof(OptimisationAlgorithm), a, true)).ToList()
            : Enum.GetValues(typeof(OptimisationAlgorithm)).Cast<OptimisationAlgorithm>().ToList();

        string path = Environment.GetEnvironmentVariable("NLOPT");
        Reference nlopt = null;
        if (!string.IsNullOrEmpty(path))
        {
            nlopt = Reference.Load(path);
            Console.WriteLine($"Comparing with NLopt {nlopt.Version} at {path}");
        }
        else
            Console.WriteLine("NLOPT is not set, so the port is not compared with NLopt. See README.md.");

        if (args.Length == 0)
        {
            Infrastructure(nlopt);
            Facade();
        }

        foreach (var algorithm in only)
        {
            Section($"{AlgorithmInfo.Of(algorithm).Name}");
            if (nlopt != null) Parity(nlopt, algorithm);
            Behaviour(algorithm);
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? $"All {passes} checks passed." : $"{fails} of {passes + fails} check(s) failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    #region What the algorithms share

    static void Infrastructure(Reference nlopt)
    {
        Section("Random numbers");
        var mt = new MersenneTwister();
        Check(mt.GenrandInt32() == 3499211612U, "unseeded, the first number is MT19937's with the default seed 5489");
        string draws = string.Concat(Enumerable.Range(0, 3000).Select(k => "uin"[(k * 7 + k / 5) % 3]));
        var mine = new MersenneTwister(12345);
        var port = draws.Select(d => d == 'u' ? mine.Urand(-1, 3) : d == 'i' ? mine.Iurand(17) : mine.Nrand(2, 0.5)).ToArray();
        if (nlopt != null)
            Check(port.SequenceEqual(nlopt.Random(12345, draws)), "3000 draws of urand, iurand and nrand are NLopt's, to the last bit");
        Check(port.Where((v, k) => draws[k] == 'u').All(v => v >= -1 && v < 3), "urand stays in [a, b)");
        Check(port.Where((v, k) => draws[k] == 'i').All(v => v >= 0 && v < 17 && v == Math.Floor(v)), "iurand stays in [0, n)");

        Section("Sorting");
        var random = new Random(3);
        bool sorted = true, asNlopt = true;
        foreach (int length in new[] { 0, 1, 5, 6, 7, 8, 40, 41, 100, 1000 })
            for (int trial = 0; trial < 20; trial++)
            {
                // few distinct keys, so that most comparisons are ties
                var keys = Enumerable.Range(0, length).Select(_ => (double)random.Next(trial % 2 == 0 ? 4 : 1000)).ToArray();
                var indices = Enumerable.Range(0, length).ToArray();
                var a = (int[])indices.Clone();
                QSort.Sort(a, length, (i, j) => keys[i] < keys[j] ? -1 : keys[i] > keys[j] ? 1 : 0);
                for (int k = 1; k < length; k++)
                    if (keys[a[k - 1]] > keys[a[k]]) sorted = false;
                if (!a.OrderBy(i => i).SequenceEqual(indices)) sorted = false;
                if (nlopt != null && !a.SequenceEqual(nlopt.Sort(indices, keys))) asNlopt = false;
            }
        Check(sorted, "indices come out sorted by key, each once");
        if (nlopt != null)
            Check(asNlopt, "equal keys come out in the order NLopt's sort leaves them");

        Section("Red-black tree");
        var tree = new RedBlackTree<double[]>((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
        var nodes = new List<RedBlackTree<double[]>.Node>();
        bool valid = true;
        for (int k = 0; k < 2000; k++)
        {
            if (nodes.Count > 0 && random.NextDouble() < 0.35)
            {
                int at = random.Next(nodes.Count);
                var removed = tree.Remove(nodes[at]);
                // the node taken out may be another one holding this key; that node then stands for the other key
                int other = nodes.IndexOf(removed);
                if (other >= 0 && other != at) nodes[other] = nodes[at];
                nodes.RemoveAt(at);
            }
            else
                nodes.Add(tree.Insert(new[] { (double)random.Next(300) }));
            if (!tree.Check()) valid = false;
        }
        Check(valid, "2000 random inserts and removes keep it a red-black tree");
        var walked = new List<double>();
        for (var node = tree.Min(); node != null; node = tree.Succ(node)) walked.Add(node.K[0]);
        Check(walked.Count == tree.N && walked.SequenceEqual(walked.OrderBy(v => v)), "walking it from Min with Succ visits every key in order");
        var back = new List<double>();
        for (var node = tree.Max(); node != null; node = tree.Pred(node)) back.Add(node.K[0]);
        Check(back.AsEnumerable().Reverse().SequenceEqual(walked), "and from Max with Pred, backwards");
        var probe = new[] { 150.5 };
        var le = tree.FindLe(probe);
        var gt = tree.FindGt(probe);
        Check((le == null || le.K[0] <= 150.5) && (gt == null || gt.K[0] > 150.5) && (le == null || gt == null || tree.Succ(le) == gt),
              "FindLe and FindGt meet either side of a key");
    }

    static void Facade()
    {
        Section("What NLopt does around the algorithms");
        var s = new Settings();
        var constrained = Port(OptimisationAlgorithm.DirectL, Tutorial, s, out var direct);
        Check(constrained.Result == (int)OptimisationResult.InvalidArgs && constrained.Points.Count == 0,
              "an algorithm that takes no constraints refuses them, and evaluates nothing");
        var outside = Rosenbrock.With("Rosenbrock", Rosenbrock.F);
        outside.X0 = new[] { 2.5, 1.0 };
        Check(Port(OptimisationAlgorithm.NelderMead, outside, s).Result == (int)OptimisationResult.InvalidArgs,
              "a start outside the bounds is refused");
        var none = new Problem { Name = "nothing to vary", N = 2, Lb = new[] { 1.0, 2.0 }, Ub = new[] { 1.0, 2.0 }, X0 = new[] { 1.0, 2.0 }, F = x => x[0] + x[1] };
        var trivial = Port(OptimisationAlgorithm.Bobyqa, none, s);
        Check(trivial.Result == (int)OptimisationResult.Success && trivial.F == 3 && trivial.Points.Count == 1,
              "with every variable pinned, the objective is evaluated once at the start");

        var step = Optimiser.DefaultInitialStep(3, new[] { 0.0, -10.0, double.NegativeInfinity }, new[] { 4.0, 10.0, double.PositiveInfinity }, new[] { 1.0, 9.0, 5.0 });
        Check(step[0] == 1.0 && step[1] == 0.75 && step[2] == 5.0,
              "the default first step: a quarter of the range, less near a bound, the value itself when unbounded");
    }

    #endregion

    #region One algorithm

    static bool IsGlobal(OptimisationAlgorithm algorithm) => AlgorithmInfo.Of(algorithm).IsGlobal;

    static void Parity(Reference nlopt, OptimisationAlgorithm algorithm)
    {
        var s = new Settings();
        if (IsGlobal(algorithm))
        {
            Compare(nlopt, algorithm, Branin, s.With(x => x.MaxEval = 300), null);
            Compare(nlopt, algorithm, Rastrigin, s.With(x => x.MaxEval = 400), null);
            Compare(nlopt, algorithm, Rosenbrock, s.With(x => x.MaxEval = 300), null);
            Compare(nlopt, algorithm, Bowl, s.With(x => { x.MaxEval = 2000; x.FtolRel = 1e-4; x.XtolRel = 1e-3; }), "until it converges");
            Compare(nlopt, algorithm, Pinned, s.With(x => x.MaxEval = 200), null);
            Compare(nlopt, algorithm, Negated(Branin), s.With(x => { x.MaxEval = 200; x.Maximise = true; }), null);
            Compare(nlopt, algorithm, Branin, s.With(x => { x.MaxEval = 300; x.StopValue = 0.5; }), "stop value 0.5");
            Compare(nlopt, algorithm, Branin, s.With(x => { x.MaxEval = 300; x.StopAfter = 25; }), "stopped after 25");
            if (AlgorithmInfo.Of(algorithm).IsStochastic)
            {
                Compare(nlopt, algorithm, Branin, s.With(x => { x.MaxEval = 300; x.Seed = 2024; }), "seed 2024");
                Compare(nlopt, algorithm, Rastrigin, s.With(x => { x.MaxEval = 400; x.Population = 40; }), "population 40");
            }
        }
        else
        {
            Compare(nlopt, algorithm, Rosenbrock, s.With(x => x.MaxEval = 500), null);
            Compare(nlopt, algorithm, Bowl, s.With(x => x.MaxEval = 400), null);
            Compare(nlopt, algorithm, Bowl, s.With(x => { x.MaxEval = 2000; x.FtolRel = 1e-6; x.XtolRel = 1e-5; }), "until it converges");
            Compare(nlopt, algorithm, Bowl, s.With(x => { x.MaxEval = 2000; x.FtolRel = 0; x.FtolAbs = 1e-7; x.XtolRel = 0; }), "absolute objective tolerance");
            Compare(nlopt, algorithm, Rastrigin, s.With(x => x.MaxEval = 300), null);
            Compare(nlopt, algorithm, Pinned, s.With(x => x.MaxEval = 300), null);
            Compare(nlopt, algorithm, Negated(Rosenbrock), s.With(x => { x.MaxEval = 300; x.Maximise = true; }), null);
            Compare(nlopt, algorithm, Rosenbrock, s.With(x => { x.MaxEval = 500; x.StopValue = 10; }), "stop value 10");
            Compare(nlopt, algorithm, Rosenbrock, s.With(x => { x.MaxEval = 500; x.StopAfter = 25; }), "stopped after 25");
            Compare(nlopt, algorithm, Bowl, s.With(x => { x.MaxEval = 400; x.InitialStep = new[] { 0.5, 2.0, 0.1, 1.0 }; }), "uneven first steps");
            Compare(nlopt, algorithm, Bowl, s.With(x => { x.MaxEval = 2000; x.XtolRel = 0; x.FtolRel = 0; x.XtolAbs = new[] { 1e-4, 1e-4, 1e-3, 1e-5 }; }), "absolute variable tolerances");
        }
        if (AlgorithmInfo.Of(algorithm).TakesConstraints)
        {
            Compare(nlopt, algorithm, Tutorial, s.With(x => x.MaxEval = IsGlobal(algorithm) ? 600 : 300), "constrained");
            Compare(nlopt, algorithm, Tutorial, s.With(x => { x.MaxEval = 2000; x.FtolRel = 1e-8; x.XtolRel = 1e-8; }), "constrained, until it converges");
        }
    }

    static void Behaviour(OptimisationAlgorithm algorithm)
    {
        string name = AlgorithmInfo.Of(algorithm).Name;
        bool global = IsGlobal(algorithm);
        var s = new Settings { MaxEval = 3000, FtolRel = 1e-12, XtolRel = 1e-10 };
        var runs = new List<(Problem problem, Run run)>();

        Run Try(Problem p, Settings settings)
        {
            try
            {
                var run = Port(algorithm, p, settings);
                runs.Add((p, run));
                return run;
            }
            catch (NotImplementedException)
            {
                return null;
            }
        }

        var found = Try(global ? Branin : Rosenbrock, s.With(x => x.MaxEval = algorithm == OptimisationAlgorithm.Isres ? 6000 : 3000));
        if (found == null)
        {
            Check(false, $"{name} is not ported yet");
            return;
        }
        var problem = global ? Branin : Rosenbrock;
        // COBYLA's linear models crawl along Rosenbrock's curved valley: NLopt's own gets to 5e-4 in 3000.
        double tolerance = algorithm == OptimisationAlgorithm.Isres ? 1e-2
                         : algorithm == OptimisationAlgorithm.Cobyla ? 1e-3
                         : global ? 1e-3 : 1e-4;
        Check(found.F - problem.FMin < tolerance && found.F == problem.F(found.X),
              $"finds the minimum of {problem.Name}, {F(problem.FMin)}: {F(found.F)} at {V(found.X)} in {found.Evaluations} evaluations ({(OptimisationResult)found.Result})");
        Check(found.Evaluations == found.Points.Count, "counts its evaluations as the objective saw them");

        var max = Try(Negated(problem), s.With(x => { x.Maximise = true; x.MaxEval = algorithm == OptimisationAlgorithm.Isres ? 6000 : 3000; }));
        Check(-max.F - problem.FMin < tolerance, $"maximises the negative to {F(max.F)}");

        // BOBYQA solves the bowl in 45, so the local algorithms are limited on Rosenbrock, which none solves in 50.
        var limited = Try(global ? Bowl : Rosenbrock, s.With(x => x.MaxEval = 50));
        Check(limited.Result == (int)OptimisationResult.MaxEvalReached && limited.Points.Count == 50,
              $"with 50 evaluations allowed, stops after 50 ({(OptimisationResult)limited.Result}, {limited.Points.Count})");

        // CRS2-LM builds its first population, 10 (n + 1) points, before it looks at the stop
        // flag, as NLopt's does; the others look after each point or each simplex.
        int allowance = algorithm == OptimisationAlgorithm.Crs2Lm ? 10 * (Bowl.N + 1) + 1 : 20 + Bowl.N + 1;
        var stopped = Try(Bowl, s.With(x => x.StopAfter = 20));
        Check(stopped.Result == (int)OptimisationResult.ForcedStop && stopped.Points.Count <= allowance,
              $"stopped from the objective at 20 evaluations, stops ({(OptimisationResult)stopped.Result} after {stopped.Points.Count})");
        Check(stopped.F <= stopped.Values.Take(stopped.Points.Count).Min() + 1e-12 || global,
              "and still returns the best it found");

        var reached = Try(Bowl, s.With(x => x.StopValue = 3.0));
        Check(reached.Result == (int)OptimisationResult.StopValueReached && reached.F <= 3.0,
              $"with a stop value of 3, stops once it is reached ({(OptimisationResult)reached.Result} at {F(reached.F)})");

        // With every tolerance off, a local algorithm may also end on round-off, as NLopt's COBYLA does.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timed = Try(Bowl, s.With(x => { x.MaxEval = 0; x.FtolRel = 0; x.XtolRel = 0; x.MaxTime = 0.2; x.StopAfter = 0; }));
        if (timed != null)
            Check((timed.Result == (int)OptimisationResult.MaxTimeReached || timed.Result > 0 || timed.Result == (int)OptimisationResult.RoundoffLimited)
                  && clock.Elapsed.TotalSeconds < 2,
                  $"with only a time limit, ends ({(OptimisationResult)timed.Result} after {timed.Points.Count} evaluations, {clock.Elapsed.TotalSeconds:0.00} s)");

        var pinned = Try(Pinned, s.With(x => x.MaxEval = 500));
        Check(pinned.Points.All(x => x[2] == 0.5) && pinned.X[2] == 0.5, "never moves a variable whose bounds are equal");

        Try(Rastrigin, s.With(x => x.MaxEval = 500));
        Try(Bowl, s.With(x => { x.MaxEval = 400; x.InitialStep = new[] { 0.5, 2.0, 0.1, 1.0 }; }));

        if (AlgorithmInfo.Of(algorithm).TakesConstraints)
        {
            var constrained = Try(Tutorial, s.With(x => { x.MaxEval = global ? 20000 : 2000; x.FtolRel = 1e-8; x.XtolRel = 1e-8; }));
            var c = new double[2];
            Tutorial.C(constrained.X, c);
            double ctol = global ? 1e-6 : 1e-8;
            Check(c.All(v => v <= ctol) && Math.Abs(constrained.F - Tutorial.FMin) < (global ? 2e-2 : 1e-4),
                  $"meets both constraints of NLopt's tutorial ({F(c[0])}, {F(c[1])}) at its minimum {F(Tutorial.FMin)}: {F(constrained.F)}");
        }

        var outside = runs.SelectMany(r => r.run.Points.Select(x => (r.problem, x)))
            .FirstOrDefault(px => px.x.Where((v, i) => v < px.problem.Lb[i] || v > px.problem.Ub[i]).Any());
        Check(outside.x == null, outside.x == null
            ? $"never evaluates outside the bounds ({runs.Sum(r => r.run.Points.Count)} points)"
            : $"evaluated {V(outside.x)}, outside the bounds of {outside.problem.Name}");
    }

    #endregion
}
