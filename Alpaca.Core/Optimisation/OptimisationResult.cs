namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// How an optimisation ended. The numbers are NLopt's nlopt_result, so a result means the same
    /// here as in NLopt's documentation. Positive is a success, negative a failure, and ForcedStop
    /// is a stop that was asked for: the best design found so far is still returned.
    /// </summary>
    public enum OptimisationResult
    {
        Failure = -1,
        InvalidArgs = -2,
        OutOfMemory = -3,
        RoundoffLimited = -4,
        ForcedStop = -5,
        Success = 1,
        StopValueReached = 2,
        FtolReached = 3,
        XtolReached = 4,
        MaxEvalReached = 5,
        MaxTimeReached = 6,
    }

    public static class OptimisationResults
    {
        /// <summary>Why the optimisation stopped, in a sentence for the user.</summary>
        public static string Describe(OptimisationResult result)
        {
            switch (result)
            {
                case OptimisationResult.Success:
                    return "The optimisation converged.";
                case OptimisationResult.StopValueReached:
                    return "The objective reached the stop value.";
                case OptimisationResult.FtolReached:
                    return "The objective changed by less than its tolerance, so the optimisation converged.";
                case OptimisationResult.XtolReached:
                    return "The variables changed by less than their tolerance, so the optimisation converged.";
                case OptimisationResult.MaxEvalReached:
                    return "The optimisation used every evaluation it was allowed.";
                case OptimisationResult.MaxTimeReached:
                    return "The optimisation ran for all the time it was allowed.";
                case OptimisationResult.ForcedStop:
                    return "The optimisation was stopped.";
                case OptimisationResult.RoundoffLimited:
                    return "Round-off errors kept the optimisation from going further. The best design found is usually still good.";
                case OptimisationResult.OutOfMemory:
                    return "The optimisation ran out of memory.";
                case OptimisationResult.InvalidArgs:
                    return "The optimisation could not start: its settings do not fit the problem.";
                default:
                    return "The optimisation failed.";
            }
        }

        /// <summary>Whether the result is a normal end: converged, or a limit reached, or stopped on request.</summary>
        public static bool IsSuccess(OptimisationResult result) => (int)result > 0 || result == OptimisationResult.ForcedStop;
    }
}
