// NLopt itself, called through its C API, to hold the C# port up against.
//
// Loaded from the path in NLOPT with dlopen (macOS, Linux) or LoadLibrary (Windows), and bound
// function by function, so that nothing here is needed unless the comparison is asked for.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

sealed class Reference
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate double NloptFunc(uint n, IntPtr x, IntPtr grad, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void NloptMFunc(uint m, IntPtr result, uint n, IntPtr x, IntPtr grad, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateFn(int algorithm, uint n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyFn(IntPtr opt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetArrayFn(IntPtr opt, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetDoubleFn(IntPtr opt, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetIntFn(IntPtr opt, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetUIntFn(IntPtr opt, uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetObjectiveFn(IntPtr opt, NloptFunc f, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AddMConstraintFn(IntPtr opt, uint m, NloptMFunc fc, IntPtr data, double[] tol);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OptimizeFn(IntPtr opt, [In, Out] double[] x, out double minf);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetIntFn(IntPtr opt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Srand64Fn(ulong seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Srand32Fn(uint seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VersionFn(out int major, out int minor, out int bugfix);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")] static extern IntPtr MacDlopen(string path, int mode);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym")] static extern IntPtr MacDlsym(IntPtr handle, string symbol);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr LinuxDlopen(string path, int mode);
    [DllImport("libdl.so.2", EntryPoint = "dlsym")] static extern IntPtr LinuxDlsym(IntPtr handle, string symbol);
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)] static extern IntPtr WinLoadLibrary(string path);
    [DllImport("kernel32", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi)] static extern IntPtr WinGetProcAddress(IntPtr handle, string symbol);

    readonly IntPtr _lib;
    readonly CreateFn _create;
    readonly DestroyFn _destroy;
    readonly SetArrayFn _lowerBounds, _upperBounds, _initialStep, _xtolAbs;
    readonly SetDoubleFn _stopval, _ftolRel, _ftolAbs, _xtolRel, _maxtime;
    readonly SetIntFn _maxeval;
    readonly SetUIntFn _population;
    readonly SetObjectiveFn _minObjective, _maxObjective;
    readonly AddMConstraintFn _inequality;
    readonly OptimizeFn _optimize;
    readonly GetIntFn _numevals;
    readonly Srand64Fn _srand64;
    readonly Srand32Fn _srand32;

    public string Version { get; }

    static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
    static bool IsMac => Environment.OSVersion.Platform == PlatformID.Unix && System.IO.Directory.Exists("/System/Library/CoreServices");

    Reference(string path)
    {
        const int RTLD_NOW = 2;
        _lib = IsWindows ? WinLoadLibrary(path) : IsMac ? MacDlopen(path, RTLD_NOW) : LinuxDlopen(path, RTLD_NOW);
        if (_lib == IntPtr.Zero) throw new Exception("could not load " + path);

        _create = Bind<CreateFn>("nlopt_create");
        _destroy = Bind<DestroyFn>("nlopt_destroy");
        _lowerBounds = Bind<SetArrayFn>("nlopt_set_lower_bounds");
        _upperBounds = Bind<SetArrayFn>("nlopt_set_upper_bounds");
        _initialStep = Bind<SetArrayFn>("nlopt_set_initial_step");
        _xtolAbs = Bind<SetArrayFn>("nlopt_set_xtol_abs");
        _stopval = Bind<SetDoubleFn>("nlopt_set_stopval");
        _ftolRel = Bind<SetDoubleFn>("nlopt_set_ftol_rel");
        _ftolAbs = Bind<SetDoubleFn>("nlopt_set_ftol_abs");
        _xtolRel = Bind<SetDoubleFn>("nlopt_set_xtol_rel");
        _maxtime = Bind<SetDoubleFn>("nlopt_set_maxtime");
        _maxeval = Bind<SetIntFn>("nlopt_set_maxeval");
        _population = Bind<SetUIntFn>("nlopt_set_population");
        _minObjective = Bind<SetObjectiveFn>("nlopt_set_min_objective");
        _maxObjective = Bind<SetObjectiveFn>("nlopt_set_max_objective");
        _inequality = Bind<AddMConstraintFn>("nlopt_add_inequality_mconstraint");
        _optimize = Bind<OptimizeFn>("nlopt_optimize");
        _numevals = Bind<GetIntFn>("nlopt_get_numevals");
        // unsigned long: 64 bits on macOS and Linux, 32 on Windows
        if (IsWindows) _srand32 = Bind<Srand32Fn>("nlopt_srand");
        else _srand64 = Bind<Srand64Fn>("nlopt_srand");

        Bind<VersionFn>("nlopt_version")(out int major, out int minor, out int bugfix);
        Version = $"{major}.{minor}.{bugfix}";
    }

    public static Reference Load(string path) => new Reference(path);

    T Bind<T>(string name) where T : class
    {
        IntPtr fn = IsWindows ? WinGetProcAddress(_lib, name) : IsMac ? MacDlsym(_lib, name) : LinuxDlsym(_lib, name);
        if (fn == IntPtr.Zero) throw new Exception("NLopt has no " + name);
        return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
    }

    /// <summary>Runs one case through NLopt, recording every point it evaluates.</summary>
    public Run Optimise(int nloptAlgorithm, Problem problem, Settings settings)
    {
        var run = new Run();
        IntPtr opt = _create(nloptAlgorithm, (uint)problem.N);
        try
        {
            _lowerBounds(opt, problem.Lb);
            _upperBounds(opt, problem.Ub);
            if (settings.StopValue.HasValue) _stopval(opt, settings.StopValue.Value);
            _ftolRel(opt, settings.FtolRel);
            _ftolAbs(opt, settings.FtolAbs);
            _xtolRel(opt, settings.XtolRel);
            if (settings.XtolAbs != null) _xtolAbs(opt, settings.XtolAbs);
            _maxeval(opt, settings.MaxEval);
            _maxtime(opt, settings.MaxTime);
            if (settings.InitialStep != null) _initialStep(opt, settings.InitialStep);
            if (settings.Population > 0) _population(opt, (uint)settings.Population);

            NloptFunc objective = (n, xp, grad, data) =>
            {
                var x = new double[n];
                Marshal.Copy(xp, x, 0, (int)n);
                double f = problem.F(x);
                run.Points.Add(x);
                run.Values.Add(f);
                if (settings.StopAfter > 0 && run.Points.Count >= settings.StopAfter)
                    ForceStop(opt);
                return f;
            };
            NloptMFunc constraints = (m, resultp, n, xp, grad, data) =>
            {
                var x = new double[n];
                Marshal.Copy(xp, x, 0, (int)n);
                var result = new double[m];
                problem.C(x, result);
                Marshal.Copy(result, 0, resultp, (int)m);
                run.ConstraintCalls++;
            };

            if (settings.Maximise) _maxObjective(opt, objective, IntPtr.Zero);
            else _minObjective(opt, objective, IntPtr.Zero);
            if (problem.M > 0) _inequality(opt, (uint)problem.M, constraints, IntPtr.Zero, problem.Tol);

            if (IsWindows) _srand32(settings.Seed);
            else _srand64(settings.Seed);

            var x0 = (double[])problem.X0.Clone();
            run.Result = _optimize(opt, x0, out double minf);
            run.X = x0;
            run.F = minf;
            run.Evaluations = _numevals(opt);
            GC.KeepAlive(objective);
            GC.KeepAlive(constraints);
        }
        finally
        {
            _destroy(opt);
        }
        return run;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ForceStopFn(IntPtr opt);
    ForceStopFn _forceStop;

    void ForceStop(IntPtr opt)
    {
        if (_forceStop == null) _forceStop = Bind<ForceStopFn>("nlopt_force_stop");
        _forceStop(opt);
    }

    // NLopt's internals, which a shared build exports: the random numbers and the sort.

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InitGenrandFn(UIntPtr seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate double UrandFn(double a, double b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int IurandFn(int n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate double NrandFn(double mean, double stddev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CompareFn(IntPtr thunk, IntPtr a, IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void QsortFn([In, Out] int[] a, UIntPtr n, UIntPtr size, IntPtr thunk, CompareFn compare);

    /// <summary>
    /// What NLopt's generator gives after nlopt_init_genrand(seed): each draw is "u" (urand on
    /// [-1, 3)), "i" (iurand of 17) or "n" (nrand, mean 2, deviation 0.5), in the order asked.
    /// </summary>
    public double[] Random(uint seed, string draws)
    {
        // unsigned long: pointer-sized on macOS and Linux; on Windows only the low 32 bits matter
        Bind<InitGenrandFn>("nlopt_init_genrand")(new UIntPtr(seed));
        var urand = Bind<UrandFn>("nlopt_urand");
        var iurand = Bind<IurandFn>("nlopt_iurand");
        var nrand = Bind<NrandFn>("nlopt_nrand");
        var values = new double[draws.Length];
        for (int k = 0; k < draws.Length; k++)
            values[k] = draws[k] == 'u' ? urand(-1, 3) : draws[k] == 'i' ? iurand(17) : nrand(2, 0.5);
        return values;
    }

    /// <summary>NLopt's nlopt_qsort_r on indices into keys, comparing keys[a] with keys[b].</summary>
    public int[] Sort(int[] indices, double[] keys)
    {
        var a = (int[])indices.Clone();
        CompareFn compare = (thunk, pa, pb) =>
        {
            double ka = keys[Marshal.ReadInt32(pa)], kb = keys[Marshal.ReadInt32(pb)];
            return ka < kb ? -1 : ka > kb ? 1 : 0;
        };
        Bind<QsortFn>("nlopt_qsort_r")(a, new UIntPtr((uint)a.Length), new UIntPtr(sizeof(int)), IntPtr.Zero, compare);
        GC.KeepAlive(compare);
        return a;
    }
}

/// <summary>One run of an optimisation: its result, and every point it evaluated, in order.</summary>
sealed class Run
{
    public int Result;
    public double[] X;
    public double F;
    public int Evaluations;
    public int ConstraintCalls;
    public readonly List<double[]> Points = new List<double[]>();
    public readonly List<double> Values = new List<double>();
}
