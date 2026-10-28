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
            "Pushes a fibre section's curvature in any moment direction under a held axial force, and returns the moment-curvature " +
            "curve, the stress-strain history of every fibre, and the curve's first yield and ultimate state.\n" +
            "The ultimate state is the first fibre at its ultimate strain - Interaction Domain's limits: concrete at εcu, or εc2 at " +
            "point C in full compression, everything else at εud - and the analysis stops there unless told not to.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // The first five are the obsolete component's, in its order: MomentCurvatureUpgrader
            // carries them over by place.
            pManager.AddGenericParameter("FiberSection", "FiberSection", "The section to analyse.", GH_ParamAccess.item);

            pManager.AddNumberParameter("Axial", "Axial",
                "Axial force, in kN, held constant while the curvature is applied.\npositive value - tension\nnegative value - compression",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter("Direction", "Direction",
                "The moment direction, in degrees from the section's local y axis towards z, -180 to 180: 0 bends it about y, " +
                "90 about z, and 180 and -90 the other way about each - the other side of a section that is not symmetric, " +
                "hogging where 0 is sagging. The moment is held to that direction and the curvature across it left free, so a " +
                "section bent off its axes turns its neutral axis as it needs to.",
                GH_ParamAccess.item, 0.0);

            pManager.AddIntegerParameter("NumIncr", "NumIncr",
                "Number of increments used to reach MaxPhi - the number of points on the curve, when it does not stop sooner.",
                GH_ParamAccess.item, 100);

            pManager.AddNumberParameter("MaxPhi", "MaxPhi",
                "The curvature along Direction to push the section to, in rad/m.\nLeave it empty to take it from the section's " +
                "ultimate state: a fifth past it when the analysis stops there, twice it when it does not.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddNumberParameter("εud", "εud",
                "Ultimate strain, either way, of every fibre that is not Concrete01, as in Interaction Domain. A MinMax bound on " +
                "the material holds instead where it is tighter.",
                GH_ParamAccess.item, 0.01);

            pManager.AddNumberParameter("εcu", "εcu",
                "Ultimate compressive strain of the concrete, positive.\nLeave it empty to take each Concrete01's own EpsilonCu.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddBooleanParameter("StopAtUltimate", "StopAtUltimate",
                "Stop at the first increment where a fibre reaches its ultimate strain. Turn it off to follow the section past it " +
                "to MaxPhi - its softening, or a confined core still carrying load; the ultimate state is reported either way.",
                GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // The first eight are the obsolete component's, by name: MomentCurvatureUpgrader moves
            // their wires to the output of the same name.
            pManager.Register_GenericParam("log", "log", "The OpenSees decks this component ran - the materials' curves and the analysis - followed by the solver's console output.");
            pManager.Register_DoubleParam("N", "N", "Axial force at each increment, in kN.");
            pManager.Register_DoubleParam("My", "My", "Moment about the local y axis at each increment, in kN m.");
            pManager.Register_DoubleParam("Mz", "Mz", "Moment about the local z axis at each increment, in kN m.");
            pManager.Register_DoubleParam("ε", "ε", "Axial strain at the section centroid at each increment, dimensionless.");
            pManager.Register_DoubleParam("κy", "κy", "Curvature about the local y axis at each increment, in rad/m.");
            pManager.Register_DoubleParam("κz", "κz", "Curvature about the local z axis at each increment, in rad/m.");
            pManager.Register_GenericParam("fiberStressStrain", "fiberStressStrain", "Stress and strain history of every fibre, one branch per fibre.");

            pManager.Register_DoubleParam("M", "M", "Moment along Direction at each increment, in kN m: My cos a + Mz sin a. Plot it against κ for the curve.");
            pManager.Register_DoubleParam("κ", "κ", "Curvature along Direction at each increment, in rad/m: κy cos a + κz sin a.");
            pManager.Register_DoubleParam("YieldCurvature", "κYield", "Curvature along Direction at first yield: the first fibre of Steel01 or ReinforcingSteel at fy/E. Empty when none yields.");
            pManager.Register_DoubleParam("YieldMoment", "MYield", "Moment along Direction at first yield, in kN m.");
            pManager.Register_DoubleParam("UltimateCurvature", "κUltimate", "Curvature along Direction where the first fibre reaches its ultimate strain, in rad/m. Empty when none does.");
            pManager.Register_DoubleParam("UltimateMoment", "MUltimate", "Moment along Direction there, in kN m.");
            pManager.Register_DoubleParam("Ductility", "μ", "Curvature ductility: UltimateCurvature / YieldCurvature.");
            pManager.Register_DoubleParam("YieldStiffness", "EI", "Secant stiffness to first yield, MYield / κYield, in kN m2: an effective cracked stiffness.");
            pManager.Register_StringParam("Failure", "Failure", "Which material reaches its ultimate strain first, and where.");
            pManager.Register_GenericParam("Result", "Result", "The whole result, for Moment Curvature Diagram to draw.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Alpaca4d.Section.FiberSection fiberSection = null;
            if (!DA.GetData(0, ref fiberSection) || fiberSection == null)
                return;

            // Needs a license whatever the section.
            if (!LicenseGate.Allows(this, "Moment Curvature"))
                return;

            double axial = 0.0;
            DA.GetData(1, ref axial);

            double degrees = 0.0;
            DA.GetData(2, ref degrees);

            int numIncr = 100;
            DA.GetData(3, ref numIncr);

            double maxPhiValue = 0.0;
            double? maxPhi = DA.GetData(4, ref maxPhiValue) ? maxPhiValue : (double?)null;

            double steel = 0.01;
            DA.GetData(5, ref steel);

            double concrete = 0.0;
            bool hasConcrete = DA.GetData(6, ref concrete);

            bool stop = true;
            DA.GetData(7, ref stop);

            if (!(degrees >= -180.0 && degrees <= 180.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Direction has to be between -180 and 180 degrees, not {degrees}: 0 bends the section about y, 90 about z.");
                return;
            }
            double angle = degrees * Math.PI / 180.0;

            // Everything the deck writes is relative to the current directory, which is where
            // OpenSees runs: settled first, and only then used. Prepared the other way round, the
            // folder was created beside whatever Rhino's own current directory happened to be while
            // the solver looked for it beside the Grasshopper file.
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
            string folder = System.IO.Directory.GetCurrentDirectory();

            var files = AnalysisFiles.For(this, DA);
            Func<string, (string output, string error, int exitCode)> run =
                deckPath => new Model { FileName = deckPath }.RunOpenSees();

            var limits = new Alpaca4d.Section.DomainSettings
            {
                SteelUltimateStrain = steel,
                ConcreteUltimateStrain = hasConcrete ? concrete : (double?)null,
            };

            Alpaca4d.Section.MomentCurvatureResult result;
            try
            {
                var layout = Alpaca4d.Section.SectionLayout.Of(fiberSection);
                result = Alpaca4d.Section.MomentCurvatureAnalysis.Run(
                    fiberSection, layout, axial, angle, numIncr, maxPhi, limits, stop,
                    folder, files.Folder, $"mc_materials_{files.Run}", run);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            DA.SetData(0, result.Deck + Environment.NewLine + result.Log);

            if (result.Incomplete)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The section stopped converging after {result.N.Count} of {numIncr} increments, so the curve stops short. " +
                    "The part that did converge is on the outputs." + Environment.NewLine + Tail(result.Log));

            foreach (var warning in result.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            if (result.MaxPhiFromDomain)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"MaxPhi was taken from the section's ultimate state: {result.MaxPhi:0.######} rad/m.");

            // Fibres pair with their history by position in the section's fibre order,
            // which is what "section fiberData" reports in - no file per fibre, and no
            // matching by coordinate.
            var fibers = fiberSection.Fibers;
            var fiberHistory = Alpaca4d.Result.Read.FiberData(
                System.IO.Path.Combine(folder, Alpaca4d.Template.MomentCurvature.FiberDataFilePath), fibers.Count);

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

            DA.SetDataList(1, result.N);
            DA.SetDataList(2, result.My);
            DA.SetDataList(3, result.Mz);
            DA.SetDataList(4, result.Axial);
            DA.SetDataList(5, result.CurvatureY);
            DA.SetDataList(6, result.CurvatureZ);
            DA.SetData(7, fiberResult);
            DA.SetDataList(8, result.Moment);
            DA.SetDataList(9, result.Curvature);

            // A point the curve never reached stays empty rather than going out as NaN.
            SetIfReal(DA, 10, result.YieldCurvature);
            SetIfReal(DA, 11, result.YieldMoment);
            SetIfReal(DA, 12, result.UltimateCurvature);
            SetIfReal(DA, 13, result.UltimateMoment);
            SetIfReal(DA, 14, result.Ductility);
            SetIfReal(DA, 15, result.YieldStiffness);
            if (result.Failure != null)
                DA.SetData(16, result.Failure);
            DA.SetData(17, result);
        }

        private static void SetIfReal(IGH_DataAccess DA, int index, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
                DA.SetData(index, value);
        }

        /// <summary>The last few lines of a solver log, for a runtime message.</summary>
        private static string Tail(string log, int lines = 6)
        {
            if (string.IsNullOrWhiteSpace(log))
                return "(the solver printed nothing)";

            var all = log.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);

            return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Moment_Curvature_Model__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{5A456D82-1C30-4EE1-AAC7-876846328C8F}");
    }
}
