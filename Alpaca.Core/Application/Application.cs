using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;


namespace Alpaca4d
{
    public partial class Application
    {
        public static string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        public static string GhAlpacaFolder = System.IO.Path.GetDirectoryName(assemblyLocation);

        /// <summary>
        /// How long <see cref="Model.RunOpenSees"/> waits for the solver before killing it
        /// and throwing. Null - the default - waits forever, because a long analysis on
        /// the canvas is a legitimate thing to do and no timeout we could pick here would
        /// be right for every model.
        ///
        /// Unattended callers should set it. The test bench does: a solver that never
        /// returns there is a test run that never finishes, and a host process that
        /// outlives the run that started it.
        /// </summary>
        public static TimeSpan? OpenSeesTimeout { get; set; }

        /// <summary>
        /// The exit status a Gatekeeper kill leaves behind - SIGKILL, 128 + 9. macOS kills
        /// the process before a single instruction of it runs, so this arrives with no
        /// output and nothing on stderr to explain it.
        /// </summary>
        public const int GatekeeperKillExitCode = 137;

        /// <summary>
        /// Solver paths already prepared successfully in this session. <see cref="OpenSees"/>
        /// is read on every analysis and preparing spawns processes; the work is idempotent,
        /// so repeating it per run buys nothing. Failures are deliberately not recorded -
        /// the user may fix the file's ownership from a Terminal without touching the path,
        /// and the next run should pick that up.
        /// </summary>
        private static readonly HashSet<string> preparedSolvers = new HashSet<string>(StringComparer.Ordinal);

        public static string OpenSees
        {
            get
            {
                string openSeesPath = AlpacaSettings.OpenSeesPath;

                if (!string.IsNullOrEmpty(openSeesPath) && File.Exists(openSeesPath))
                {
                    bool prepared;
                    lock (preparedSolvers)
                        prepared = preparedSolvers.Contains(openSeesPath);

                    // Nothing to tell the user from inside a property getter. When this
                    // fails, RunOpenSees is the one that reports it, with the solver's own
                    // exit status to back it up.
                    if (!prepared && PrepareSolver(openSeesPath, out _))
                    {
                        lock (preparedSolvers)
                            preparedSolvers.Add(openSeesPath);
                    }
                }

                return openSeesPath;
            }
        }

        /// <summary>
        /// Clears the two things macOS puts between a downloaded solver and running it: a
        /// missing execute bit, and the com.apple.quarantine flag.
        ///
        /// The flag is the one that matters and the one people mistake for a permissions
        /// problem. We ship no Apple Developer ID, so the solver is unsigned; Gatekeeper
        /// SIGKILLs an unsigned binary while it is still flagged, and removing the flag is
        /// what makes it runnable. An ad-hoc signature is not an alternative - a
        /// quarantined ad-hoc-signed binary is still killed - and `spctl -a` is no use as
        /// a check either, because it rejects any unsigned binary whether it runs or not.
        /// Presence of the attribute is the only signal worth reading.
        /// </summary>
        /// <param name="filePath">Path to the solver executable.</param>
        /// <param name="problem">
        /// Set when something is still in the way, phrased for the user and carrying the
        /// command that fixes it. Usually means the solver belongs to another user, so we
        /// can neither chmod nor unquarantine it.
        /// </param>
        /// <returns>False if the solver is still blocked.</returns>
        public static bool PrepareSolver(string filePath, out string problem)
        {
            problem = null;

            // Windows has neither an execute bit nor quarantine.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                problem = $"There is no file at \"{filePath}\".";
                return false;
            }

            // A solver unzipped by some tools arrives with the execute bit stripped.
            if (RunTool("chmod", $"755 \"{filePath}\"") != 0)
            {
                problem =
                    $"Alpaca4d could not make \"{filePath}\" executable." + Environment.NewLine + Environment.NewLine +
                    "The file most likely belongs to another user. Run this in Terminal, then set the path again:" +
                    Environment.NewLine + Environment.NewLine +
                    $"    chmod 755 \"{filePath}\"";
                return false;
            }

            // Quarantine is a macOS concept; on Linux there is nothing left to do.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || !IsQuarantined(filePath))
                return true;

            RunTool("xattr", $"-d com.apple.quarantine \"{filePath}\"");

            // Deleting an attribute reports the same failure as deleting one that was never
            // there, so read it back rather than trusting the exit status.
            if (IsQuarantined(filePath))
            {
                problem = QuarantineHelp(filePath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether macOS still has the file flagged as downloaded. False when we cannot
        /// tell - an unreadable file or a missing xattr tool is not evidence of a block,
        /// and reporting a Gatekeeper problem that is not there sends people chasing the
        /// wrong thing.
        /// </summary>
        public static bool IsQuarantined(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) ||
                !RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                return RunTool("xattr", $"-p com.apple.quarantine \"{filePath}\"") == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// What to tell the user about a solver Gatekeeper is blocking. Shared so the
        /// settings dialog and a failed analysis say the same thing.
        /// </summary>
        public static string QuarantineHelp(string filePath)
        {
            string folder = System.IO.Path.GetDirectoryName(filePath);

            return
                $"macOS is blocking the OpenSees executable at \"{filePath}\"." +
                Environment.NewLine + Environment.NewLine +
                "OpenSees is not signed with an Apple Developer ID, so macOS refuses to run it " +
                "while the download is still flagged as quarantined." +
                Environment.NewLine + Environment.NewLine +
                "Alpaca4d could not clear that flag itself. Run this in Terminal - point it at the " +
                "folder you extracted OpenSees into - then try again:" +
                Environment.NewLine + Environment.NewLine +
                $"    xattr -dr com.apple.quarantine \"{folder}\"";
        }

        /// <summary>
        /// Runs a command-line tool to completion and returns its exit status.
        /// </summary>
        private static int RunTool(string fileName, string arguments)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                        return -1;

                    // Drained before the wait, or a tool that fills a pipe would block on
                    // it forever. Sequential reads are safe for chmod and xattr, whose
                    // output is a line at most - nowhere near a pipe buffer.
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    return process.ExitCode;
                }
            }
            catch (Exception)
            {
                // A tool we cannot even start tells us nothing about the solver.
                return -1;
            }
        }
    }
}
