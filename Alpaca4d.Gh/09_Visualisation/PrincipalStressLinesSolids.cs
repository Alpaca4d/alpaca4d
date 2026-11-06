using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Element;
using Alpaca4d.Result;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Principal Stress Lines' Solids unit: lines through the inside of SSP Bricks and tetrahedra,
    /// along all three principal stresses. The field comes from <see cref="SolidPrincipalField"/>, the
    /// lines from <see cref="SolidStressLineTracer"/>. The two outputs it shares with Shells carry
    /// the largest and the middle principal stress; SL3, the smallest, is its own.
    /// </summary>
    public partial class PrincipalStressLines
    {
        /// <summary>The Solids unit's name, which files save.</summary>
        private const string SolidsUnit = "PrincipalStressLines3D";

        /// <summary>The inputs a line through a solid needs, and an Advanced menu with the spacing of a field.</summary>
        private static void RegisterSolids(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(
                SolidsUnit,
                "Solids",
                "Principal stress lines through solid elements, along all three principal stresses");

            unit.Icon = Alpaca4d.Gh.Properties.Resources.Principal_Stress_Lines_3D__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "Alpaca4d model with analysed solid elements: SSP Bricks or tetrahedra.", GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = false;

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step", "Analysis step index.", GH_ParamAccess.item, new GH_Integer(0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Point(), "Seed", "S",
                "Points inside the solid for the lines to go through. A seed outside it draws nothing.", GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = false;

            unit.RegisterInputParam(new Param_Number(), "Step Tolerance", "T",
                "Length of one step along a line. 0 takes a third of the elements' mean edge length.", GH_ParamAccess.item, new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Single Line", "SL",
                "True draws one line of each family through each seed. False grows a field of lines from the seeds, " +
                "Separation apart: through a whole solid that is many lines, which hide one another.", GH_ParamAccess.item, new GH_Boolean(true));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            // Advanced inputs (moved into the Advanced menu below)
            unit.RegisterInputParam(new Param_Number(), "Separation", "dSep",
                "Distance between neighbouring lines in a field (Single Line false). 0 takes three times the elements' mean edge length.",
                GH_ParamAccess.item, new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Test Dist.", "dTest",
                "In a field, a line stops when it comes closer than this to a line already drawn. 0 lets lines run on.",
                GH_ParamAccess.item, new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            // The third family, which shells do not have.
            unit.RegisterOutputParam(new Param_Curve { Access = GH_ParamAccess.list }, "Stress Lines Dir3", "SL3",
                "Lines along the smallest principal stress: the most compressive.");

            var menu = new GH_ExtendableMenu(0, "PrincipalStressLines3D_Advanced");
            menu.Name = "Advanced";
            menu.Header = "Advanced parameters (dSep, dTest)";
            menu.RegisterInputPlug(unit.Inputs[5]);
            menu.RegisterInputPlug(unit.Inputs[6]);
            unit.AddMenu(menu);
        }

        private void SolveSolids(IGH_DataAccess DA)
        {
            var model = new Alpaca4d.Model();
            if (!DA.GetData(0, ref model)) return;
            if (!AnalysedModel.Check(this, model)) return;

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
            bool singleLine = true;
            DA.GetData(4, ref singleLine);
            double separation = 0.0;
            DA.GetData(5, ref separation);
            double testDistance = 0.0;
            DA.GetData(6, ref testDistance);

            if (model.Bricks == null || model.Bricks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Model has no solid elements. Nothing to draw. For shells, pick Shells in the drop-down on the component.");
                return;
            }

            // ---------------------------------------------------------------------
            // 1. Each solid's stress: one per element, or one per node of a ten node tetrahedron
            // ---------------------------------------------------------------------
            var stressOf = new Dictionary<Generic.IBrick, double[][]>();
            try
            {
                void ByElement(ElementClass type, (List<double>, List<double>, List<double>, List<double>, List<double>, List<double>) read)
                {
                    var elements = model.Bricks.Where(x => x.ElementClass == type).ToList();
                    for (int i = 0; i < elements.Count && i < read.Item1.Count; i++)
                        stressOf[elements[i]] = new[] { new[] { read.Item1[i], read.Item2[i], read.Item3[i], read.Item4[i], read.Item5[i], read.Item6[i] } };
                }

                if (model.Bricks.Any(x => x.ElementClass == ElementClass.SSPBrick))
                    ByElement(ElementClass.SSPBrick, Alpaca4d.Result.Read.SSPBrickStress(model, step));
                if (model.Bricks.Any(x => x.ElementClass == ElementClass.FourNodeTetrahedron))
                    ByElement(ElementClass.FourNodeTetrahedron, Alpaca4d.Result.Read.TetrahedronStress(model, step));

                var tenNode = model.Bricks.Where(x => x.ElementClass == ElementClass.TenNodeTetrahedron).ToList();
                if (tenNode.Count > 0)
                {
                    var read = Alpaca4d.Result.Read.TenNodeTetrahedronStressAtNodes(model, step);
                    for (int i = 0; i < tenNode.Count && i < read.Count; i++)
                        stressOf[tenNode[i]] = read[i];
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // ---------------------------------------------------------------------
            // 2. The solids on the model's nodes, so that elements Assemble welded are joined
            // ---------------------------------------------------------------------
            var positions = new Dictionary<int, Point3d>();
            foreach (var node in model.Nodes)
                if (node.Id.HasValue)
                    positions[node.Id.Value] = node.Pos;

            var vertexOf = new Dictionary<int, int>();
            var points = new List<Point3d>();
            var elementsOut = new List<int[]>();
            var stresses = new List<double[][]>();
            int withoutResults = 0;
            foreach (var brick in model.Bricks)
            {
                var ids = brick.IndexNodes;
                if (ids == null || !stressOf.TryGetValue(brick, out var stress) ||
                    ids.Any(id => !id.HasValue || !positions.ContainsKey(id.Value)))
                {
                    withoutResults++;
                    continue;
                }

                var element = new int[ids.Count];
                for (int k = 0; k < ids.Count; k++)
                {
                    int id = ids[k].Value;
                    if (!vertexOf.TryGetValue(id, out int vertex))
                    {
                        vertex = points.Count;
                        vertexOf[id] = vertex;
                        points.Add(positions[id]);
                    }
                    element[k] = vertex;
                }
                elementsOut.Add(element);
                stresses.Add(stress);
            }

            if (withoutResults > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{withoutResults} solid element(s) have no stress results and are left out.");

            var field = new SolidPrincipalField(points, elementsOut, stresses);
            if (field.Degenerate > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{field.Degenerate} solid element(s) have no volume and are left out.");
            if (field.Tetrahedra == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No solid elements to draw stress lines in.");
                return;
            }

            int outside = 0;
            foreach (var seed in seeds)
            {
                int hint = -1;
                if (!field.Contains(seed, ref hint)) outside++;
            }
            if (outside > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{outside} seed(s) lie outside the solid and draw no line.");

            // ---------------------------------------------------------------------
            // 3. The lines, step and spacing scaled to the mesh when left at 0
            // ---------------------------------------------------------------------
            if (stepSize <= 0.0) stepSize = field.EdgeLength / 3.0;
            if (separation <= 0.0) separation = 3.0 * field.EdgeLength;
            if (testDistance < 0.0) testDistance = 0.0;

            var lines = new List<Polyline>[3];
            try
            {
                for (int family = 1; family <= 3; family++)
                {
                    var tracer = new SolidStressLineTracer(field, family, stepSize, singleLine ? 0.0 : testDistance)
                    {
                        Cancelled = GH_Document.IsEscapeKeyDown,
                    };
                    lines[family - 1] = singleLine
                        ? seeds.Select(tracer.Trace).ToList()
                        : tracer.TraceField(seeds, separation);
                    if (!singleLine && lines[family - 1].Count >= SolidStressLineTracer.MaxLines)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Family {family} stopped at {SolidStressLineTracer.MaxLines} lines. Raise Separation for fewer.");
                }
            }
            catch (OperationCanceledException)
            {
                OnPingDocument()?.RequestAbortSolution();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Stress line generation aborted (ESC pressed).");
                return;
            }

            for (int k = 0; k < 3; k++)
                DA.SetDataList(k, lines[k]);
        }
    }
}
