using System;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Where an analysis component writes its deck and its results: a set of files per run.
    ///
    /// The result components read the recorder file when they compute, not when the analysis runs.
    /// A component given a tree of models - one per load case, say - solves once per branch, and
    /// with one recorder.mpco for all of them every branch showed the results of whichever was solved
    /// last. So each run gets files of its own, numbered by the run: the same names from one solution
    /// to the next, so they are overwritten rather than piling up. The paths are absolute, so reading
    /// them back no longer depends on the current directory.
    ///
    /// The names do not say which component wrote them, so two Run Analysis components in one
    /// document write the same files, and the one solved last overwrites the other's results.
    /// </summary>
    internal sealed class AnalysisFiles
    {
        /// <summary>The folder, next to the .gh file, that the files go in.</summary>
        public const string FolderName = "AlpacaResults";

        /// <summary>Where this component's own files go.</summary>
        public string Folder { get; }

        /// <summary>
        /// Where a relative file name the user chose is resolved: the .gh file's folder, which is
        /// where it went before, as the current directory.
        /// </summary>
        public string UserFolder { get; }

        /// <summary>How many times the component solves in this solution.</summary>
        public int Runs { get; }

        /// <summary>Which of those runs this is, from 0: the branch, for a tree of one model per branch.</summary>
        public int Run { get; }

        private AnalysisFiles(string folder, string userFolder, int runs, int run)
        {
            Folder = folder;
            UserFolder = userFolder;
            Runs = runs;
            Run = run;
        }

        /// <summary>The files for the run <paramref name="DA"/> is on.</summary>
        public static AnalysisFiles For(GH_Component component, IGH_DataAccess DA)
        {
            var document = component.OnPingDocument();
            string ghFolder = document != null && document.IsFilePathDefined
                ? Path.GetDirectoryName(document.FilePath)
                : null;

            // A document not saved yet has no folder of its own; the current directory may well be
            // one Rhino cannot write to, so the system's temporary folder takes its place.
            string folder = ghFolder != null
                ? Path.Combine(ghFolder, FolderName)
                : Path.Combine(Path.GetTempPath(), "Alpaca4d", FolderName);
            Directory.CreateDirectory(folder);

            // Grasshopper runs an item-access component once per item of its longest input.
            int runs = Math.Max(1, component.Params.Input.Select(param => param.VolatileDataCount).DefaultIfEmpty(1).Max());

            return new AnalysisFiles(folder, ghFolder ?? folder, runs, DA.Iteration);
        }

        /// <summary>One of this run's own files: Folder/stem_run.extension.</summary>
        public string OwnFile(string stem, string extension) =>
            Path.Combine(Folder, $"{stem}_{Run}{extension}");

        /// <summary>
        /// A file the user named, as a full path: a relative name is taken from the .gh file's folder,
        /// and when the component solves more than once, "_" and the run go before the extension, so
        /// that each run keeps its own results. A single run keeps exactly the name given.
        /// </summary>
        public string UserFile(string fileName)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(fileName) ? fileName : Path.Combine(UserFolder, fileName));
            if (Runs <= 1)
                return path;

            return Path.Combine(Path.GetDirectoryName(path),
                $"{Path.GetFileNameWithoutExtension(path)}_{Run}{Path.GetExtension(path)}");
        }
    }
}
