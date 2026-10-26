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
        public static string Failure(string output, int exitCode)
        {
            // A non-zero exit code means the Tcl script itself errored out
            // (e.g. bad command, undefined material). OpenSees exits 0 then too;
            // Model.RunOpenSees reads the error off the log and hands on 1.
            if (exitCode != 0)
                return "Analysis Failed: OpenSees exited with an error. See log for details.";

            // analyze() always returns TCL_OK to Tcl even on non-convergence; the
            // actual result is a negative integer captured via the sentinel below.
            var resultLine = (output ?? "")
                .Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None)
                .LastOrDefault(l => l.Contains(AnalyzeResultTag));

            if (resultLine != null)
            {
                var token = resultLine.Substring(resultLine.IndexOf(AnalyzeResultTag) + AnalyzeResultTag.Length).Trim();
                if (int.TryParse(token, out int parsed) && parsed < 0)
                    return "Analysis Failed: OpenSees 'analyze' did not converge.";
            }

            return null;
        }
    }
}
