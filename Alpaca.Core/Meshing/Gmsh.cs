using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// Runs gmsh (https://gmsh.info), the mesher by Christophe Geuzaine and Jean-François Remacle,
    /// as a program of its own: a .geo script goes in, an .msh file comes out.
    ///
    /// gmsh is GPL, so it is never loaded into Alpaca4d nor shipped with it. Each user installs it,
    /// as they do OpenSees, and Alpaca4d finds it: at the path the user set, or else where the
    /// installers put it.
    ///
    /// Nothing in this folder uses Alpaca4d's other types, only RhinoCommon's, so that it could
    /// stand on its own.
    /// </summary>
    public static class Gmsh
    {
        /// <summary>
        /// The oldest gmsh the scripts are written for: 4.11 named the Distance field's lists
        /// PointsList, CurvesList and SurfacesList.
        /// </summary>
        public static readonly Version MinimumVersion = new Version(4, 11);

        public const string DownloadPage = "https://gmsh.info/#Download";

        /// <summary>The exit status a Gatekeeper kill leaves behind on a Mac: SIGKILL, 128 + 9.</summary>
        private const int GatekeeperKill = 137;

        private static readonly Dictionary<string, Version> versions = new Dictionary<string, Version>(StringComparer.Ordinal);

        /// <summary>
        /// The gmsh to run: <paramref name="configured"/> when it is set and there, else the first of
        /// <see cref="Candidates"/> that is. Null, with <paramref name="problem"/> saying why and what
        /// to do, when there is none, or when the one found is older than <see cref="MinimumVersion"/>.
        /// </summary>
        public static string Find(string configured, out string problem)
        {
            problem = null;
            string found = null;

            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!File.Exists(configured))
                {
                    problem = $"There is no gmsh at \"{configured}\", the path set in Alpaca4d ▸ Settings ▸ Set gmsh Executable. " +
                              "Set it again, or clear it to let Alpaca4d look for gmsh itself.";
                    return null;
                }
                found = configured;
            }
            else
            {
                found = Candidates().FirstOrDefault(File.Exists);
                if (found == null)
                {
                    problem = "Alpaca4d meshes with gmsh, a free mesher you install yourself. It found none. " +
                              (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                                  ? "Install it with Homebrew (brew install gmsh), or download it from gmsh.info into Applications"
                                  : "Download it from gmsh.info and unzip it") +
                              ", then point Alpaca4d at it: Alpaca4d ▸ Settings ▸ Set gmsh Executable.";
                    return null;
                }
            }

            var version = VersionOf(found);
            if (version == null)
            {
                problem = $"\"{found}\" did not say which gmsh it is when asked (gmsh --version). " +
                          "Is it gmsh? " + (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                              ? $"If macOS blocks it, run this in Terminal: xattr -dr com.apple.quarantine \"{Path.GetDirectoryName(found)}\""
                              : "Set its path again in Alpaca4d ▸ Settings ▸ Set gmsh Executable.");
                return null;
            }

            if (version < MinimumVersion)
            {
                problem = $"The gmsh at \"{found}\" is version {version}; Alpaca4d needs {MinimumVersion} or later. " +
                          "Install a newer one from gmsh.info.";
                return null;
            }

            return found;
        }

        /// <summary>Where gmsh's installers and package managers put it, most likely first.</summary>
        public static IEnumerable<string> Candidates()
        {
            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "gmsh.exe" : "gmsh";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Rhino is not started from a shell, so it does not see Homebrew's folders on PATH.
                yield return "/opt/homebrew/bin/gmsh";
                yield return "/usr/local/bin/gmsh";
                yield return "/Applications/Gmsh.app/Contents/MacOS/gmsh";
                yield return Path.Combine(home, "Applications", "Gmsh.app", "Contents", "MacOS", "gmsh");
            }

            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;
                string path;
                try { path = Path.Combine(folder.Trim().Trim('"'), exe); }
                catch (ArgumentException) { continue; }
                yield return path;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // The download is a zip with gmsh.exe in a folder named after its version, such as
                // gmsh-4.13.1-Windows64: wherever it was unzipped, newest first.
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                    home,
                    Path.Combine(home, "Downloads"),
                    Path.Combine(home, "Desktop"),
                    Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
                };
                foreach (var root in roots)
                {
                    string[] folders;
                    try { folders = Directory.Exists(root) ? Directory.GetDirectories(root, "gmsh*") : new string[0]; }
                    catch (Exception) { continue; }
                    foreach (var folder in folders.OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        yield return Path.Combine(folder, exe);
                        yield return Path.Combine(folder, "bin", exe);
                    }
                }
            }
        }

        /// <summary>The version <paramref name="executable"/> reports, or null when it reports none.</summary>
        public static Version VersionOf(string executable)
        {
            lock (versions)
                if (versions.TryGetValue(executable, out var known))
                    return known;

            Version version = null;
            try
            {
                var info = new ProcessStartInfo(executable, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (process.WaitForExit(20000))
                    {
                        // "4.15.2-git" or "4.13.1": gmsh writes it to stdout or stderr depending on the build.
                        var match = Regex.Match(output.Result + "\n" + error.Result, @"(\d+)\.(\d+)(?:\.(\d+))?");
                        if (match.Success)
                            version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                                                  match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
                    }
                    else
                    {
                        try { process.Kill(); } catch (Exception) { }
                    }
                }
            }
            catch (Exception)
            {
                // Not runnable: no version.
            }

            if (version != null)
                lock (versions)
                    versions[executable] = version;
            return version;
        }

        /// <summary>
        /// Writes <paramref name="script"/> into <paramref name="folder"/> as mesh.geo and has gmsh
        /// mesh it to <paramref name="dimension"/> (2 for surfaces, 3 for volumes) into mesh.msh.
        /// It waits for gmsh however long it takes: there is no timeout, by the user's choice. It
        /// asks <paramref name="stop"/> ten times a second, and kills gmsh when it says so - the
        /// Escape key, on the canvas.
        /// </summary>
        public static GmshRun Run(string executable, string folder, string script, int dimension, Func<bool> stop = null)
        {
            Directory.CreateDirectory(folder);
            string geo = Path.Combine(folder, "mesh.geo");
            string msh = Path.Combine(folder, "mesh.msh");
            File.WriteAllText(geo, script, new UTF8Encoding(false));
            if (File.Exists(msh))
                File.Delete(msh);

            var log = new GmshLog();
            var info = new ProcessStartInfo(executable,
                $"mesh.geo -{dimension} -format msh41 -o mesh.msh -v 4 -nopopup")
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var run = new GmshRun { Folder = folder, ScriptFile = geo };
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) log.Add(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) log.Add(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(100))
                {
                    bool asked = false;
                    try { asked = stop != null && stop(); }
                    catch (Exception) { }
                    if (asked)
                    {
                        try { process.Kill(); } catch (Exception) { }
                        run.Stopped = true;
                        break;
                    }
                }
                process.WaitForExit();
                run.ExitCode = process.ExitCode;
            }

            run.Log = log;
            if (!run.Stopped && run.ExitCode == 0 && File.Exists(msh))
                run.MeshFile = msh;
            else if (run.ExitCode == GatekeeperKill && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && log.Last(1).Count == 0)
                run.Gatekeeper = true;   // killed before it said a word
            else if (run.ExitCode == GatekeeperKill)
                run.Killed = true;
            return run;
        }
    }

    /// <summary>What a run of gmsh left: its mesh, when it made one, and its log.</summary>
    public sealed class GmshRun
    {
        public string Folder { get; set; }
        public string ScriptFile { get; set; }
        /// <summary>The .msh file, or null when gmsh made none.</summary>
        public string MeshFile { get; set; }
        public int ExitCode { get; set; }
        /// <summary>Stopped by the user before it was done.</summary>
        public bool Stopped { get; set; }
        /// <summary>Killed by macOS before it ran: it is quarantined.</summary>
        public bool Gatekeeper { get; set; }
        /// <summary>Killed while it ran, by the system: most likely out of memory.</summary>
        public bool Killed { get; set; }
        public GmshLog Log { get; set; }

        public bool Succeeded => MeshFile != null;

        /// <summary>What went wrong, for the user: gmsh's errors, or the end of its log.</summary>
        public string Failure(string executable)
        {
            if (Stopped)
                return "Stopped: gmsh was halted with Escape before it finished.";
            if (Gatekeeper)
                return $"macOS stopped gmsh before it ran. Run this in Terminal, then try again: " +
                       $"xattr -dr com.apple.quarantine \"{Path.GetDirectoryName(executable)}\"";

            if (Killed)
                return "gmsh was killed while it meshed, most likely for running out of memory: a Size or a refinement " +
                       $"asks for more elements than this computer holds. It got as far as: {string.Join(" | ", Log.Last(2))}. " +
                       $"Its script is {ScriptFile}.";

            var said = Log.Errors.Count > 0 ? Log.Errors : Log.Last(6);
            return $"gmsh made no mesh (exit {ExitCode}). It said: " + string.Join(" | ", said) +
                   $" Its script is {ScriptFile}: open it in gmsh to see where it stops.";
        }
    }

    /// <summary>
    /// gmsh's log: every warning and error, and the last lines of the rest. Its progress lines run
    /// to tens of thousands on a large mesh, and only the end of them says anything.
    /// </summary>
    public sealed class GmshLog
    {
        private const int Kept = 40;
        private readonly Queue<string> last = new Queue<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public void Add(string line)
        {
            // Progress is drawn over itself with carriage returns; the last of a run of them is the one shown.
            int cr = line.LastIndexOf('\r');
            if (cr >= 0)
                line = line.Substring(cr + 1);
            line = line.Trim();
            if (line.Length == 0)
                return;
            lock (last)
            {
                if (line.StartsWith("Error", StringComparison.Ordinal))
                    Errors.Add(Tidy(line));
                else if (line.StartsWith("Warning", StringComparison.Ordinal))
                    Warnings.Add(Tidy(line));
                last.Enqueue(line);
                while (last.Count > Kept)
                    last.Dequeue();
            }
        }

        public List<string> Last(int count)
        {
            lock (last)
                return last.Skip(Math.Max(0, last.Count - count)).ToList();
        }

        /// <summary>"Warning : 6 elements remain invalid in surface 4" as "6 elements remain invalid in surface 4".</summary>
        private static string Tidy(string line)
        {
            int colon = line.IndexOf(':');
            return colon >= 0 ? line.Substring(colon + 1).Trim() : line;
        }

        public override string ToString()
        {
            lock (last)
                return string.Join(Environment.NewLine, last);
        }
    }
}
