using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Whether an OpenSees run solved, read off what it printed. Shared by the components that run an
    /// analysis, so they agree on what a failure is and say it the same way.
    /// </summary>
    internal static class AnalysisOutcome
    {
        /// <summary>The line the deck prints after analyze, followed by what analyze returned.</summary>
        public const string AnalyzeResultTag = "ALPACA_ANALYZE_RESULT";

        /// <summary>Why the run failed, or null when it solved.</summary>
        /// <param name="output">What OpenSees printed to its standard output.</param>
        /// <param name="error">What it printed to its error stream - which is where Tcl's puts goes, and with it the
        /// result line: OpenSees puts its own puts in place of Tcl's, and that one writes to opserr.</param>
        public static string Failure(string output, string error, int exitCode)
        {
            // A non-zero exit code means the Tcl script itself errored out
            // (e.g. bad command, undefined material). OpenSees exits 0 then too;
            // Model.RunOpenSees reads the error off the log and hands on 1.
            if (exitCode != 0)
                return "Analysis Failed: OpenSees exited with an error. See log for details.";

            // analyze() always returns TCL_OK to Tcl even on non-convergence; the
            // actual result is a negative integer captured via the sentinel below.
            // One line per analyze in the deck, and any that failed fails the run.
            var failed = ((error ?? "") + "\n" + (output ?? ""))
                .Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None)
                .Where(l => l.Contains(AnalyzeResultTag))
                .Select(l => l.Substring(l.IndexOf(AnalyzeResultTag) + AnalyzeResultTag.Length).Trim())
                .Any(token => int.TryParse(token, out int parsed) && parsed < 0);

            return failed ? "Analysis Failed: OpenSees 'analyze' did not converge." : null;
        }
    }
}
