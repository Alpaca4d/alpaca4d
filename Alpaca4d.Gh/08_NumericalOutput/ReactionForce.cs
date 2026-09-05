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
    public class ReactionForce : GH_Component
    {
        public ReactionForce()
          : base("Reaction Forces (Alpaca4d)", "Reaction Forces",
            "Reads the force and the moment carried by every support of an analysed model.\n" +
            "One value per support, given in the support's own axes, so a support placed on a Plane " +
            "reports along that plane rather than along the global axes. SupportPosition gives both " +
            "where each support sits and the frame its reactions are in.\n" +
            "Give PointPos to read some of the supports instead of all of them.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. ReactionForce and ReactionMoment then " +
                "become trees with one branch per step, {step}, holding that step's value per " +
                "support. SupportPosition stays a flat list - supports do not move. Step is ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            _filterInput = pManager.ParamCount;
            pManager.AddPointParameter("PointPos", "PointPos", NodeFilterInput.SupportPointFilter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>Where the PointPos filter sits in the input list.</summary>
        private int _filterInput;

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // One output rather than two: the support's plane already sits at the support,
            // so its origin is the position and its axes are the frame the reactions below
            // are given in.
            pManager.Register_PlaneParam("SupportPosition", "SupportPosition",
                "Where each support is, and the axes its reactions are given in. For a support " +
                "placed on a Point those axes are the global ones; for one placed on a Plane " +
                "they are the plane's.");
            pManager.Register_VectorParam("ReactionForce", "ReactionForce",
                $"[{Units.Force}] in the support's own axes.");
            pManager.Register_VectorParam("ReactionMoment", "ReactionMoment",
                $"[{Units.Force}{Units.Length}] in the support's own axes.");
            pManager.Register_IntegerParam("NodeTag", "NodeTag", NodeFilterInput.NodeTagOutput);
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

            var steps = HistorySteps.Of(alpacaModel, history, step, this);
            if (steps == null) return;

            // Filtered on where each support is, which is the handle the user already has - they
            // placed these themselves and can point at them. Matched against the support's own
            // point rather than the coincident auxiliary node a skewed support carries its fix on:
            // the two are at the same place anyway, and the auxiliary one is Alpaca4d's own doing.
            var supportPoints = alpacaModel.Supports.Select(x => x.Pos).ToList();

            var requested = NodeFilterInput.ReadPoints(DA, _filterInput, this);
            var kept = Alpaca4d.Result.NodeFilter.SelectByPoint(
                requested, supportPoints, alpacaModel.Tollerance, out var missed);

            if (missed.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"No support is within {alpacaModel.Tollerance} of " +
                    $"{string.Join("; ", missed.Select(point => point.ToString()))}, so there is no " +
                    $"reaction to report there - a node with nothing holding it carries none. " +
                    $"Plug the same points into PointPos that you gave the Support component.");

            var supports = kept.Select(i => alpacaModel.Supports[i]).ToList();

            // A skewed support carries its fix on a coincident auxiliary node, so that is
            // where OpenSees puts the reaction; the support node itself reads zero. An
            // axis-aligned support has no auxiliary node and is read where it always was.
            var nodes = supports.Select(x => x.AuxiliaryNodeId ?? x.Id).ToList();

            // Reactions come out of the recorder in global components whatever the
            // support is turned to, which for a skewed one spreads a reaction that runs
            // along a single local axis across all three global ones. Resolving them onto
            // the support's own axes is what makes a released direction read as the zero
            // it is.
            var planes = supports.Select(x => x.Plane).ToList();

            var forceTree = new DataTree<Vector3d>();
            var momentTree = new DataTree<Vector3d>();

            foreach (int current in steps)
            {
                var globalForce = Result.Read.NodalOutput(alpacaModel, current, ResultType.REACTION_FORCE, nodes).ToList();
                var globalMoment = Result.Read.NodalOutput(alpacaModel, current, ResultType.REACTION_MOMENT, nodes).ToList();

                var path = new Grasshopper.Kernel.Data.GH_Path(current);
                forceTree.AddRange(globalForce.Select((vector, i) => InAxesOf(vector, planes[i])), path);
                momentTree.AddRange(globalMoment.Select((vector, i) => InAxesOf(vector, planes[i])), path);
            }

            // Finally assign the spiral to the output parameter.
            // The supports do not move, so their planes stay one flat list either way. A
            // single step keeps the flat lists it has always had; a history is a tree with
            // one branch per step.
            DA.SetDataList(0, planes);
            if (history)
            {
                DA.SetDataTree(1, forceTree);
                DA.SetDataTree(2, momentTree);
            }
            else
            {
                DA.SetDataList(1, forceTree.AllData());
                DA.SetDataList(2, momentTree.AllData());
            }

            DA.SetDataList(3, supports.Select(x => x.Id.Value));
        }

        private static Vector3d InAxesOf(Vector3d vector, Plane frame)
        {
            return new Vector3d(vector * frame.XAxis, vector * frame.YAxis, vector * frame.ZAxis);
        }


        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Reaction_Forces__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{C86DB16F-84D1-4EF6-80CD-860B0F0B5A7D}");
    }
}