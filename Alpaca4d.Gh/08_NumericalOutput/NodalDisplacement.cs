using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;


using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    public class NodalDisplacement : GH_Component
    {
        public NodalDisplacement()
          : base("Nodal Displacements (Alpaca4d)", "Nodal Displacements",
            "Reads displacement, rotation, velocity and acceleration at every node of an analysed " +
            "model.\n" +
            "One value per node, in global axes, in assembly order. Velocity and acceleration come " +
            "from a transient analysis only. After a Natural Vibration Analysis, Step picks the mode " +
            "and Displacement and Rotation are its shape.\n" +
            "Give NodeTag to read part of a big model.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override IEnumerable<string> Keywords => new string[] { "nd" };

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. Each output then becomes a tree with " +
                "one branch per step, {step}, holding that step's value per node. Step is ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step",
                "Which recorded step to read, or which mode after a modal analysis.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            _filterInput = pManager.ParamCount;
            pManager.AddIntegerParameter("NodeTag", "NodeTag", NodeFilterInput.NodeTagFilter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>Where the NodeTag filter sits in the input list.</summary>
        private int _filterInput;

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_PointParam("Position", "Position", NodeFilterInput.PositionOutput);
            pManager.Register_VectorParam("Displacement", "Displacement", $"[{Units.Length}]");
            pManager.Register_VectorParam("Rotation", "Rotation", $"[{Units.Angle}]");
            pManager.Register_GenericParam("--------", "--------", "Separator. Nothing comes out of it - it keeps the static results above apart from the transient ones below.");
            pManager.Register_VectorParam("Velocity", "Velocity", $"[{Units.Length}/{Units.Time}] Translational, along the global axes.");
            pManager.Register_VectorParam("AngularVelocity", "AngularVelocity", $"[{Units.Angle}/{Units.Time}] Rotational, about the global axes.");
            pManager.Register_VectorParam("Acceleration", "Acceleration", $"[{Units.Length}/{Units.Time}²] Translational, along the global axes.");
            pManager.Register_VectorParam("AngularAcceleration", "AngularAcceleration", $"[{Units.Angle}/{Units.Time}²] Rotational, about the global axes.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var alpacaModel = new Alpaca4d.Model();
            bool history = false;
            int step = 0;

            if (!DA.GetData(0, ref alpacaModel)) return;
            DA.GetData(1, ref history);
            DA.GetData(2, ref step);

            // Every node of the model, in the order the recorder wrote them - Assemble numbers the
            // unique points it finds from 1, adding them to Nodes as it goes, so a node's tag is
            // its position here plus one.
            var nodeTags = alpacaModel.Nodes.Select(node => node.Id).ToList();

            var requested = NodeFilterInput.Read(DA, _filterInput, this);
            var kept = NodeFilterInput.Select(requested, nodeTags, out var missing);

            if (missing.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The model has no node {string.Join(", ", missing)}. Its nodes are tagged " +
                    $"1 to {nodeTags.Count}.");

            // Handed to Read.NodalOutput as the rows to pick out. Left unfiltered this is every
            // node in order, which is the whole recorder table and what came out before.
            var readTags = NodeFilterInput.Slice(nodeTags, kept);


			if (alpacaModel.IsModal == false)
			{
                // What the recorder file holds, asked once rather than per step. An MPCO Recorder can
                // leave any of these out, and a file from an older Alpaca4d has no angular velocity or
                // acceleration. Either way that output comes out empty and the others still stand.
                var wanted = new List<ResultType> { ResultType.DISPLACEMENT, ResultType.ROTATION };
                if (alpacaModel.IsTransient)
                    wanted.AddRange(new[] { ResultType.VELOCITY, ResultType.ANGULAR_VELOCITY,
                                            ResultType.ACCELERATION, ResultType.ANGULAR_ACCELERATION });

                var held = new HashSet<ResultType>(wanted.Where(type => Alpaca4d.Result.Read.Holds(alpacaModel, type)));
                var notHeld = wanted.Where(type => !held.Contains(type)).Select(Alpaca4d.Result.Read.RecorderName).ToList();
                if (notHeld.Count > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Not in the recorder file, so left empty: {string.Join(", ", notHeld)}.");

                IEnumerable<Vector3d> ReadHeld(int at, ResultType type) => held.Contains(type)
                    ? Alpaca4d.Result.Read.NodalOutput(alpacaModel, at, type, readTags)
                    : Enumerable.Empty<Vector3d>();

                if(history == false)
                {
                    var disp = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                    var rot = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                    var vel = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                    var angVel = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                    var acc = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                    var angAcc = Enumerable.Empty<Rhino.Geometry.Vector3d>();

                    disp = ReadHeld(step, ResultType.DISPLACEMENT);
                    rot = ReadHeld(step, ResultType.ROTATION);
                    if (alpacaModel.IsTransient)
                    {
                        vel = ReadHeld(step, ResultType.VELOCITY);
                        acc = ReadHeld(step, ResultType.ACCELERATION);
                        angVel = ReadHeld(step, ResultType.ANGULAR_VELOCITY);
                        angAcc = ReadHeld(step, ResultType.ANGULAR_ACCELERATION);
                    }

                    // Finally assign the spiral to the output parameter.
                    SetPositions(DA, alpacaModel, kept);
                    DA.SetDataList(1, disp);
                    DA.SetDataList(2, rot);
                    DA.SetDataList(4, vel);
                    DA.SetDataList(5, angVel);
                    DA.SetDataList(6, acc);
                    DA.SetDataList(7, angAcc);
                }
                else if(history == true)
                {
                    var disp = new DataTree<Vector3d>();
                    var rot = new DataTree<Vector3d>();
                    var vel = new DataTree<Vector3d>();
                    var angVel = new DataTree<Vector3d>();
                    var acc = new DataTree<Vector3d>();
                    var angAcc = new DataTree<Vector3d>();

                    // How many steps were written, not how many were asked for: a model can
                    // be run with no Settings at all, and an analysis that stops early writes
                    // fewer steps than NumIncr.
                    var steps = HistorySteps.Of(alpacaModel, true, step, this);
                    if (steps == null) return;

                    foreach (int current in steps)
                    {
                        var path = new Grasshopper.Kernel.Data.GH_Path(current);
                        disp.AddRange(ReadHeld(current, ResultType.DISPLACEMENT), path);
                        rot.AddRange(ReadHeld(current, ResultType.ROTATION), path);
                        if (alpacaModel.IsTransient)
                        {
                            vel.AddRange(ReadHeld(current, ResultType.VELOCITY), path);
                            acc.AddRange(ReadHeld(current, ResultType.ACCELERATION), path);
                            angVel.AddRange(ReadHeld(current, ResultType.ANGULAR_VELOCITY), path);
                            angAcc.AddRange(ReadHeld(current, ResultType.ANGULAR_ACCELERATION), path);
                        }
                    }

                    // Position leads, so 3 is the "--------" separator and the four transient
                    // outputs are 4 to 7 - the same indices RegisterOutputParams gives them.
                    SetPositions(DA, alpacaModel, kept);
                    DA.SetDataTree(1, disp);
                    DA.SetDataTree(2, rot);
                    DA.SetDataTree(4, vel);
                    DA.SetDataTree(5, angVel);
                    DA.SetDataTree(6, acc);
                    DA.SetDataTree(7, angAcc);
                }
			}
			else
			{
                var disp = Enumerable.Empty<Rhino.Geometry.Vector3d>();
                var rot = Enumerable.Empty<Rhino.Geometry.Vector3d>();

                disp = Alpaca4d.Result.Read.NodalOutput(alpacaModel, step, Alpaca4d.Result.ResultType.MODES_OF_VIBRATION_U, readTags);
                rot = Alpaca4d.Result.Read.NodalOutput(alpacaModel, step, Alpaca4d.Result.ResultType.MODES_OF_VIBRATION_R, readTags);

                // Finally assign the spiral to the output parameter.
                SetPositions(DA, alpacaModel, kept);
                DA.SetDataList(1, disp);
                DA.SetDataList(2, rot);
            }
        }

        /// <summary>
        /// Where the nodes being reported sit, undeformed - the first output, so the results read
        /// against the points they belong to.
        ///
        /// Worth having whether or not a filter was given, and the one thing a filtered component
        /// has to hand back: the model's own node list no longer lines up with the output, so
        /// without these there is nothing to draw the displacements against. The tags need no
        /// output of their own - filtered they are exactly what was typed into NodeTag, and
        /// unfiltered they are one to the number of nodes, in order.
        /// </summary>
        private static void SetPositions(IGH_DataAccess DA, Alpaca4d.Model alpacaModel, System.Collections.Generic.List<int> kept)
        {
            DA.SetDataList(0, kept.Select(i => alpacaModel.Nodes[i].Pos));
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Nodal_Displacements__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{25085EAB-6C66-487E-B83B-BAB5AB02B1BB}");
    }
}