using System.Collections.Generic;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The algorithms Alpaca4d can optimise with: NLopt's derivative-free ones, ported to C#.
    ///
    /// Only derivative-free algorithms are here. An objective computed by a Grasshopper definition
    /// has no gradient to give, so the gradient-based algorithms DoDo listed (MMA, SLSQP, the
    /// LD_ family) could never have worked with it.
    /// </summary>
    public enum OptimisationAlgorithm
    {
        DirectL,
        Crs2Lm,
        Isres,
        Cobyla,
        Bobyqa,
        NelderMead,
        Sbplx,
    }

    /// <summary>What the user is told about an algorithm, and what it can and cannot do.</summary>
    public sealed class AlgorithmInfo
    {
        public OptimisationAlgorithm Algorithm { get; }

        /// <summary>The short name, as NLopt's documentation and DoDo call it.</summary>
        public string Name { get; }

        /// <summary>NLopt's constant, for those who know NLopt.</summary>
        public string NloptName { get; }

        /// <summary>Searches the whole of the sliders' ranges rather than improving on where they are.</summary>
        public bool IsGlobal { get; }

        /// <summary>Takes inequality constraints.</summary>
        public bool TakesConstraints { get; }

        /// <summary>Draws random numbers, so the seed matters.</summary>
        public bool IsStochastic { get; }

        public string Description { get; }

        private AlgorithmInfo(OptimisationAlgorithm algorithm, string name, string nloptName, bool global, bool constraints, bool stochastic, string description)
        {
            Algorithm = algorithm;
            Name = name;
            NloptName = nloptName;
            IsGlobal = global;
            TakesConstraints = constraints;
            IsStochastic = stochastic;
            Description = description;
        }

        public string Label => $"{Name} ({(IsGlobal ? "global" : "local")})";

        public static readonly IReadOnlyList<AlgorithmInfo> All = new[]
        {
            new AlgorithmInfo(OptimisationAlgorithm.DirectL, "DIRECT-L", "GN_DIRECT_L", true, false, false,
                "Divides the sliders' ranges into boxes and keeps dividing the most promising ones. " +
                "Deterministic, and a good first choice for a few variables when the objective may have many valleys. " +
                "It ignores where the sliders are when it starts."),
            new AlgorithmInfo(OptimisationAlgorithm.Crs2Lm, "CRS2-LM", "GN_CRS2_LM", true, false, true,
                "Controlled random search: a population of random designs that shrinks towards the best. " +
                "Often quicker than DIRECT-L to find a good region; the seed changes the run."),
            new AlgorithmInfo(OptimisationAlgorithm.Isres, "ISRES", "GN_ISRES", true, true, true,
                "An evolution strategy that ranks designs by objective and constraint violation together. " +
                "The global choice when there are constraints; it needs many evaluations."),
            new AlgorithmInfo(OptimisationAlgorithm.Cobyla, "COBYLA", "LN_COBYLA", false, true, false,
                "Builds linear approximations of the objective and the constraints around the sliders and steps across them. " +
                "The local choice when there are constraints."),
            new AlgorithmInfo(OptimisationAlgorithm.Bobyqa, "BOBYQA", "LN_BOBYQA", false, false, false,
                "Builds a quadratic model of the objective around the sliders. " +
                "Usually the fastest local algorithm for a smooth objective without constraints."),
            new AlgorithmInfo(OptimisationAlgorithm.NelderMead, "Nelder-Mead", "LN_NELDERMEAD", false, false, false,
                "The simplex method: a set of designs that reflects, expands and shrinks towards the best. " +
                "Robust to a noisy objective, but slows down with many variables."),
            new AlgorithmInfo(OptimisationAlgorithm.Sbplx, "Sbplx", "LN_SBPLX", false, false, false,
                "Subplex: Nelder-Mead on a few variables at a time. " +
                "Keeps Nelder-Mead's robustness with many more variables."),
        };

        public static AlgorithmInfo Of(OptimisationAlgorithm algorithm)
        {
            foreach (var info in All)
                if (info.Algorithm == algorithm) return info;
            return All[0];
        }
    }
}
