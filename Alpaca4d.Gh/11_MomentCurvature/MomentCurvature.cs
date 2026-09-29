using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    public class MomentCurvature : GH_Component
    {
        public MomentCurvature()
          : base("MomentCurvature (Alpaca4d)", "MC",
            "Pushes a fibre section to a given curvature under a held axial force, and returns the moment-curvature curve together with the stress-strain history of every fibre.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("FiberSection", "FiberSection", "The section to analyse.", GH_ParamAccess.item);

            pManager.AddNumberParameter("Axial", "Axial",
                "Axial force, in kN, held constant while the curvature is applied.\npositive value - tension\nnegative value - compression",
                GH_ParamAccess.item, 0.0);

            pManager.AddTextParameter("Direction", "Direction",
                "The section's local axis to bend about - \"y\" or \"z\".",
                GH_ParamAccess.item, "y");

            pManager.AddIntegerParameter("NumIncr", "NumIncr",
                "Number of increments used to reach MaxPhi - the number of points on the curve.",
                GH_ParamAccess.item, 100);

            pManager.AddNumberParameter("MaxPhi", "MaxPhi",
                "The curvature to push the section to, in rad/m.",
                GH_ParamAccess.item, 0.02);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("log", "log", "The OpenSees input deck this component ran, followed by the solver's console output.");
            pManager.Register_DoubleParam("N", "N", "Axial force at each increment, in kN.");
            pManager.Register_DoubleParam("My", "My", "Moment about the local y axis at each increment, in kN m.");
            pManager.Register_DoubleParam("Mz", "Mz", "Moment about the local z axis at each increment, in kN m.");
            pManager.Register_DoubleParam("ε", "ε", "Axial strain at the section centroid at each increment, dimensionless.");
            pManager.Register_DoubleParam("κy", "κy", "Curvature about the local y axis at each increment, in rad/m.");
            pManager.Register_DoubleParam("κz", "κz", "Curvature about the local z axis at each increment, in rad/m.");
            pManager.Register_GenericParam("fiberStressStrain", "fiberStressStrain", "Stress and strain history of every fibre, one branch per fibre.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Alpaca4d.Section.FiberSection fiberSection = null;
            if (!DA.GetData(0, ref fiberSection) || fiberSection == null)
                return;

            // Needs a license whatever the section.
            if (!LicenseGate.Allows(this, "Moment Curvature"))
                return;

            // The registered defaults above are what these are; the fallbacks here only
            // matter if a wire is connected and carries nothing.
            double axial = 0.0;
            DA.GetData(1, ref axial);

            string dir = "y";
            DA.GetData(2, ref dir);

            int numIncr = 100;
            DA.GetData(3, ref numIncr);

            double maxPhi = 0.02;
            DA.GetData(4, ref maxPhi);

            // Everything below is relative to the current directory - the deck, the folder
            // the recorders write into, and the files read back - so the directory is
            // settled first and only then used. Prepared the other way round, the folder
            // was created beside whatever Rhino's own current directory happened to be
            // while the solver looked for it beside the Grasshopper file, and the first
            // run of a session read files that were never written.
            var document = OnPingDocument();
            if (document != null && document.IsFilePathDefined)
            {
                var currentDir = System.IO.Path.GetDirectoryName(document.FilePath);
                System.IO.Directory.SetCurrentDirectory(currentDir);
            }
            else
            {
                // Unsaved, or hosted by something with no document of its own (Hops): the
                // current directory is as good a place as any, and better than refusing to
                // run at all.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "The Grasshopper script has not been saved, so the analysis files are written to " +
                    System.IO.Directory.GetCurrentDirectory() + ".");
            }

            string text;
            try
            {
                Alpaca4d.Template.MomentCurvature.PrepareOutputFolder();

                text = Alpaca4d.Template.MomentCurvature.Define(
                    fiber: fiberSection,
                    axialForce: axial,
                    dof: dir,
                    numIncr: numIncr,
                    maxPhi: maxPhi);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var model = new Model();
            model.Tcl.Add(text);
            model.FileName = System.IO.Path.GetFullPath("MomentCurvature.tcl");
            model.Serialise();

            string output, error;
            int exitCode;
            try
            {
                (output, error, exitCode) = model.RunOpenSees();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var solverLog = string.Join(Environment.NewLine,
                new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x)));

            // OpenSees exits 0 whether or not "analyze" converged, so the exit code alone
            // says nothing; the deck prints a marker instead. The solver log goes out on
            // the log output either way, which is what a failure message can point at.
            DA.SetData(0, text + Environment.NewLine + solverLog);

            if (exitCode != 0 || output.Contains(Alpaca4d.Template.MomentCurvature.AxialFailedMarker))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The axial step did not converge, so no curvature was applied. See the log output." +
                    Environment.NewLine + Tail(solverLog));
                return;
            }

            var incomplete = output.Contains(Alpaca4d.Template.MomentCurvature.CurvatureIncompleteMarker);

            var force = ReadHistory(Alpaca4d.Template.MomentCurvature.ForceFilePath);
            var deformation = ReadHistory(Alpaca4d.Template.MomentCurvature.DeformationFilePath);

            if (force.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The analysis recorded nothing. See the log output." +
                    Environment.NewLine + Tail(solverLog));
                return;
            }

            if (incomplete)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The section stopped converging after {force.Count} of {numIncr} increments, so the curve " +
                    "stops short of MaxPhi. The part that did converge is on the outputs.");
            }

            // A FiberSectionGJ reports P, Mz, My, T - in that order - and its deformations
            // in the matching order. Reading columns 2 and 3 the other way round put every
            // curve on the wrong output.
            var N = Column(force, 0);
            var Mz = Column(force, 1);
            var My = Column(force, 2);

            var e = Column(deformation, 0);
            var kz = Column(deformation, 1);
            var ky = Column(deformation, 2);

            // Fibres pair with their history by position in the section's fibre order,
            // which is what "section fiberData" reports in - no file per fibre, and no
            // matching by coordinate.
            var fibers = fiberSection.Fibers;
            var fiberHistory = Alpaca4d.Result.Read.FiberData(
                Alpaca4d.Template.MomentCurvature.FiberDataFilePath, fibers.Count);

            var fiberResult = new Alpaca4d.Result.PointFiberResult();
            for (var i = 0; i < fibers.Count; i++)
            {
                var path = new Grasshopper.Kernel.Data.GH_Path(i);
                fiberResult.Stress.AddRange(fiberHistory[i].Stress, path);
                fiberResult.Strain.AddRange(fiberHistory[i].Strain, path);
                fiberResult.Fibers.Add(fibers[i], path);
            }

            if (fiberHistory.Count > 0 && fiberHistory[0].Stress.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The moment-curvature curve was read but no fibre stresses were: " +
                    Alpaca4d.Template.MomentCurvature.FiberDataFilePath + " is missing or empty.");
            }

            DA.SetDataList(1, N);
            DA.SetDataList(2, My);
            DA.SetDataList(3, Mz);
            DA.SetDataList(4, e);
            DA.SetDataList(5, ky);
            DA.SetDataList(6, kz);
            DA.SetData(7, fiberResult);
        }

        /// <summary>
        /// A recorder file as one row of numbers per increment. Blank fields are dropped
        /// rather than parsed, and a short or missing file gives what there is - a section
        /// that stops converging leaves a shorter file behind, which is a result and not a
        /// reason to throw.
        /// </summary>
        private static List<double[]> ReadHistory(string relativePath)
        {
            var rows = new List<double[]>();
            var filePath = System.IO.Path.GetFullPath(relativePath);

            if (!System.IO.File.Exists(filePath))
                return rows;

            foreach (var line in System.IO.File.ReadAllLines(filePath))
            {
                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (values.Length == 0)
                    continue;

                rows.Add(values.Select(TclNumber.Read).ToArray());
            }

            return rows;
        }

        private static List<double> Column(List<double[]> rows, int index)
        {
            return rows.Select(row => index < row.Length ? row[index] : 0.0).ToList();
        }

        /// <summary>The last few lines of a solver log, for a runtime message.</summary>
        private static string Tail(string log, int lines = 6)
        {
            if (string.IsNullOrWhiteSpace(log))
                return "(the solver printed nothing)";

            var all = log.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);

            return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
        }

        protected override void BeforeSolveInstance()
        {
            List<string> directions = new List<string> { "y", "z" };
            ValueListUtils.UpdateValueLists(this, 2, directions, null);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Moment_Curvature_Model__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{EA3284A5-4E95-4836-8663-60D8B2F5D6FE}");
    }
}
