using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Result;
using Alpaca4d.Gh;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Creates principal stress lines (direction 1 and 2) on shell elements of an Alpaca model: the
    /// field comes from <see cref="PrincipalField"/>, the lines from <see cref="StressLineTracer"/>.
    /// The method follows LilyPad, by Matthew Church and Format Engineers.
    /// </summary>
    public class PrincipalStressLines : GH_SwitcherComponent
    {
        public PrincipalStressLines()
          : base("Principal Stress Lines (Alpaca4d)", "Principal Stress Lines",
            "Plot principal stress lines on shell elements using nodal displacements/rotations.\n" +
            "The method follows the open-source LilyPad principal stress line generator by Matthew Church/Format Engineers.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // All inputs are managed via the EvaluationUnit (see RegisterEvaluationUnits).
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Stress Lines Dir1", "SL1", "Principal stress lines for direction 1", GH_ParamAccess.list);
            pManager.AddCurveParameter("Stress Lines Dir2", "SL2", "Principal stress lines for direction 2", GH_ParamAccess.list);
        }

        /// <summary>
        /// Register a single evaluation unit and attach an 'Advanced' menu that
        /// hosts the four advanced plugs (v, BSL, dSep, dTest).
        /// </summary>
        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(
                "PrincipalStressLines",
                "PrincipalStressLines",
                "Plot principal stress lines on shell elements using nodal displacements/rotations");

            unit.Icon = Alpaca4d.Gh.Properties.Resources.PrincipalStressLines__Alpaca4D_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(
                new Param_GenericObject(),
                "AlpacaModel", "AlpacaModel",
                "Alpaca4d model with analysed shell elements",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = false;

            unit.RegisterInputParam(
                new Param_Integer(),
                "Step", "Step",
                "Analysis step index",
                GH_ParamAccess.item,
                new GH_Integer(0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(
                new Param_Point(),
                "Seed", "S",
                "Seed point(s) for stress lines",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = false;

            unit.RegisterInputParam(
                new Param_Number(),
                "Step Tolerance", "T",
                "Integration step size along stress lines. 0 takes a third of the shells' mean edge length.",
                GH_ParamAccess.item,
                new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(
                new Param_Boolean(),
                "Single Line", "SL",
                "If true, generate only one stress line through the seed (GH_StressLine-like). If false, generate a field of lines (GH_StressLines-like).",
                GH_ParamAccess.item,
                new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            // Advanced inputs (will be moved into the Advanced menu)
            unit.RegisterInputParam(
                new Param_Number(),
                "Poisson's ratio", "v",
                "Not used: for an isotropic elastic material the principal directions do not depend on it. Kept so that older files still open.",
                GH_ParamAccess.item,
                new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(
                new Param_Boolean(),
                "Bending Stress Lines", "BSL",
                "If true use rotations (bending), if false use in-plane displacements",
                GH_ParamAccess.item,
                new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(
                new Param_Number(),
                "Separation", "dSep",
                "Separation distance for neighbouring stress lines (multi-line mode). 0 takes twice the shells' mean edge length.",
                GH_ParamAccess.item,
                new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(
                new Param_Number(),
                "Test Dist.", "dTest",
                "Distance to existing lines below which new streamlines stop (0 disables check)",
                GH_ParamAccess.item,
                new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            // Create Advanced menu and move the last four inputs into it as menu plugs
            var menu = new GH_ExtendableMenu(0, "PrincipalStressLines_Advanced");
            menu.Name = "Advanced";
            menu.Header = "Advanced parameters (v, BSL, dSep, dTest)";


            menu.RegisterInputPlug(unit.Inputs[5]);
            menu.RegisterInputPlug(unit.Inputs[6]);
            menu.RegisterInputPlug(unit.Inputs[7]);
            menu.RegisterInputPlug(unit.Inputs[8]);


            unit.AddMenu(menu);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null)
                return;

            var model = new Alpaca4d.Model();
            if (!DA.GetData(0, ref model)) return;

            int step = 0;
            DA.GetData(1, ref step);

            var seeds = new List<Point3d>();
            if (!DA.GetDataList(2, seeds) || seeds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No seed points supplied for stress line generation.");
                return;
            }

            double stepSize = 0.0;
            DA.GetData(3, ref stepSize);

            bool singleLine = false;
            DA.GetData(4, ref singleLine);

            // Input 5, Poisson's ratio, is no longer read: see PrincipalField.

            bool bending = false;
            DA.GetData(6, ref bending);

            double separation = 0.0;
            DA.GetData(7, ref separation);

            double testDistance = 0.0;
            DA.GetData(8, ref testDistance);

            if (model.Shells == null || model.Shells.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Model has no shell elements. Nothing to draw.");
                return;
            }

            // ---------------------------------------------------------------------
            // 1. The nodal translations, or for bending the rotations - only the one needed
            // ---------------------------------------------------------------------
            var values = new Dictionary<int, Vector3d>();
            try
            {
                if (bending)
                {
                    var rotations = Alpaca4d.Result.Read.NodalOutput(model, step, ResultType.ROTATION).ToList();
                    foreach (var node in model.Nodes)
                    {
                        if (!node.Id.HasValue) continue;
                        int index = node.Id.Value - 1;
                        if (index >= 0 && index < rotations.Count)
                            values[node.Id.Value] = rotations[index];
                    }
                }
                else
                {
                    foreach (var pair in model.NodalDisplacements(step))
                        if (pair.Key.HasValue)
                            values[pair.Key.Value] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // ---------------------------------------------------------------------
            // 2. The shells as faces on the model's nodes, so that faces Assemble welded are joined
            // ---------------------------------------------------------------------
            var positions = new Dictionary<int, Point3d>();
            foreach (var node in model.Nodes)
                if (node.Id.HasValue)
                    positions[node.Id.Value] = node.Pos;

            var vertexOf = new Dictionary<int, int>();
            var points = new List<Point3d>();
            var vectors = new List<Vector3d>();
            var faces = new List<int[]>();
            int withoutResults = 0;
            foreach (var shell in model.Shells)
            {
                var ids = shell.IndexNodes;
                if (ids == null || ids.Count < 3 || ids.Count > 4 ||
                    ids.Any(id => !id.HasValue || !positions.ContainsKey(id.Value) || !values.ContainsKey(id.Value)))
                {
                    withoutResults++;
                    continue;
                }

                var face = new int[ids.Count];
                for (int k = 0; k < ids.Count; k++)
                {
                    int id = ids[k].Value;
                    if (!vertexOf.TryGetValue(id, out int vertex))
                    {
                        vertex = points.Count;
                        vertexOf[id] = vertex;
                        points.Add(positions[id]);
                        vectors.Add(values[id]);
                    }
                    face[k] = vertex;
                }
                faces.Add(face);
            }

            if (withoutResults > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{withoutResults} shell element(s) have nodes without results and are left out.");

            var field = new PrincipalField(points, faces, vectors, bending);
            if (field.Degenerate > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{field.Degenerate} shell element(s) have no area and are left out.");
            if (field.Flipped > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{field.Flipped} shell element(s) face the other way from their neighbours. Their bending is read the other way up, " +
                    "so that the lines run on across them.");
            if (field.Mesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid shell faces found for principal stress line generation.");
                return;
            }

            // ---------------------------------------------------------------------
            // 3. The lines, step and spacing scaled to the mesh when left at 0
            // ---------------------------------------------------------------------
            if (stepSize <= 0.0) stepSize = field.EdgeLength / 3.0;
            if (separation <= 0.0) separation = 2.0 * field.EdgeLength;
            if (testDistance < 0.0) testDistance = 0.0;

            var lines1 = new List<Polyline>();
            var lines2 = new List<Polyline>();
            try
            {
                foreach (int family in new[] { 1, 2 })
                {
                    var tracer = new StressLineTracer(field, family, stepSize, singleLine ? 0.0 : testDistance)
                    {
                        Cancelled = GH_Document.IsEscapeKeyDown,
                    };
                    var lines = family == 1 ? lines1 : lines2;
                    if (singleLine)
                        lines.AddRange(seeds.Select(tracer.Trace));
                    else
                        lines.AddRange(tracer.TraceField(seeds, separation));
                }
            }
            catch (OperationCanceledException)
            {
                OnPingDocument()?.RequestAbortSolution();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Stress line generation aborted (ESC pressed).");
                return;
            }

            DA.SetDataList(0, lines1);
            DA.SetDataList(1, lines2);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.PrincipalStressLines__Alpaca4D_;

        public override Guid ComponentGuid => new Guid("{E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2}");
    }
}
