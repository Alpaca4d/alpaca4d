using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;


using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    public class NodeForce : GH_Component
    {
        public NodeForce()
          : base("Node Forces (Alpaca4d)", "Node Forces",
            "Reads the forces that every beam and truss brings into a node, for designing the joint there.\n" +
            "Pick the nodes by Point or by NodeTag; left empty, every node a beam meets is reported. " +
            "One branch per node, keyed by its tag, with an item per member meeting there.\n" +
            "N to Mz are each member's section forces at that end, in its local axes and signed as " +
            "Beam Forces signs them. Force and Moment are what the member puts on the node, in global " +
            "axes.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override IEnumerable<string> Keywords => new string[] { "joint", "connection", "member end forces" };

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. Every output then carries a step index in " +
                "front of the node's tag, so a branch reads {step; tag}. Step is ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddPointParameter("Point", "Point",
                "The nodes to report, by where they are: a point on a beam end picks the node there. " +
                "A point counts as on a node when it is within the model's tolerance of it.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("NodeTag", "NodeTag",
                "The nodes to report, by tag - the number OpenSees knows each node by, as Nodal " +
                "Displacements and Model View show it. Taken together with Point. Leave both empty " +
                "to report every node a beam meets.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Every output has the same tree, a branch per node, so any two of them pair up branch
            // for branch - Force with Position into Vector Display, N with Element into a label.
            pManager.Register_GenericParam("N", "N", $"[{Units.Force}] Axial force in each member at the node. Positive is tension.");
            pManager.Register_GenericParam("Vy", "Vy", $"[{Units.Force}] Shear along the member's local y at the node, Vy = dMz/dx, as Beam Forces signs it.");
            pManager.Register_GenericParam("Vz", "Vz", $"[{Units.Force}] Shear along the member's local z at the node, Vz = dMy/dx, as Beam Forces signs it.");
            pManager.Register_GenericParam("Mx", "Mx", $"[{Units.Force}{Units.Length}] Torsion in the member at the node, as Beam Forces signs it.");
            pManager.Register_GenericParam("My", "My", $"[{Units.Force}{Units.Length}] Bending about the member's local y at the node. Positive puts the +z side in tension.");
            pManager.Register_GenericParam("Mz", "Mz", $"[{Units.Force}{Units.Length}] Bending about the member's local z at the node. Positive puts the +y side in compression.");
            pManager.Register_VectorParam("Force", "Force",
                $"[{Units.Force}] The force each member puts on the node, in global axes: a member in " +
                "tension pulls the node towards itself. Added up over a node, they balance the load and " +
                "the support reaction there, and with neither they add up to zero.");
            pManager.Register_VectorParam("Moment", "Moment",
                $"[{Units.Force}{Units.Length}] The moment each member puts on the node, about the global axes.");
            pManager.Register_GenericParam("Element", "Element",
                "The members meeting at the node, in the same order as the results. Deconstruct them " +
                "for the tag, the ElementId and the curve.");
            pManager.Register_PointParam("Position", "Position", "Where the node is, once per branch: the point to draw Force and Moment at.");
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
            var points = new List<Point3d>();
            var tags = new List<int>();

            if (!DA.GetData(0, ref alpacaModel)) return;
            if (!AnalysedModel.Check(this, alpacaModel)) return;
            DA.GetData(1, ref history);
            DA.GetData(2, ref step);
            DA.GetDataList(3, points);
            DA.GetDataList(4, tags);

            var beams = alpacaModel.Beams;
            if (beams.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The model has no beams or trusses, so no node has member forces to report.");
                return;
            }

            var graph = NodeGraph.Of(alpacaModel);
            var nodes = Nodes(graph, points, tags, alpacaModel.Tollerance);

            // Nothing found reads nothing and sets empty trees, as Beam Forces does for a filter that
            // matches nothing. Returning before the outputs are set would leave an empty branch {0}.
            var steps = nodes.Count == 0 ? Enumerable.Empty<int>() : HistorySteps.Of(alpacaModel, history, step, this);
            if (steps == null) return;

            var nTree = new DataTree<object>();
            var vyTree = new DataTree<object>();
            var vzTree = new DataTree<object>();
            var mxTree = new DataTree<object>();
            var myTree = new DataTree<object>();
            var mzTree = new DataTree<object>();
            var forceTree = new DataTree<Vector3d>();
            var momentTree = new DataTree<Vector3d>();
            var elementTree = new DataTree<IBeam>();
            var positionTree = new DataTree<Point3d>();
            int unread = 0;

            // Only the members at the nodes asked for are copied out of the recorder file, which on
            // a large model is most of the time a single node would otherwise take.
            var only = points.Count == 0 && tags.Count == 0 ? null : graph.BeamsAt(nodes);

            foreach (int current in steps)
            {
                var forces = Alpaca4d.Result.Read.ForceBeamColumn(alpacaModel, current, null, only);
                var displacement = Alpaca4d.Result.NodeForces.Displacements(alpacaModel, current);

                foreach (int tag in nodes)
                {
                    var ends = Alpaca4d.Result.NodeForces.At(graph, tag, beams, forces, displacement, out int missing);
                    unread = Math.Max(unread, missing);

                    var path = history ? new GH_Path(current, tag) : new GH_Path(tag);
                    nTree.AddRange(ends.Select(end => (object)end.N), path);
                    vyTree.AddRange(ends.Select(end => (object)end.Vy), path);
                    vzTree.AddRange(ends.Select(end => (object)end.Vz), path);
                    mxTree.AddRange(ends.Select(end => (object)end.Mx), path);
                    myTree.AddRange(ends.Select(end => (object)end.My), path);
                    mzTree.AddRange(ends.Select(end => (object)end.Mz), path);
                    forceTree.AddRange(ends.Select(end => end.Force), path);
                    momentTree.AddRange(ends.Select(end => end.Moment), path);
                    elementTree.AddRange(ends.Select(end => beams[end.Beam]), path);
                    positionTree.Add(graph.PositionOf(tag), path);
                }
            }

            if (unread > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The recorder file holds no forces for some of the members at these nodes, so they " +
                    "are left out and those nodes do not balance. Run the analysis again with " +
                    "\"section.force\" and \"axialForce\" ticked in the MPCO Recorder.");

            // A shell, a solid or a link at the node carries force into it too, and nothing here
            // reads that. Said once, naming the nodes, rather than letting a joint that does not
            // balance pass for one that does.
            var shared = nodes.Where(tag => graph.OthersAt(tag) > 0).ToList();
            if (shared.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    (shared.Count == 1 ? $"Node {shared[0]} also meets" : $"Nodes {Preview(shared)} also meet") +
                    " shells, solids or links. What those carry into a node is not in these outputs, " +
                    "so the members alone do not balance there.");

            DA.SetDataTree(0, nTree);
            DA.SetDataTree(1, vyTree);
            DA.SetDataTree(2, vzTree);
            DA.SetDataTree(3, mxTree);
            DA.SetDataTree(4, myTree);
            DA.SetDataTree(5, mzTree);
            DA.SetDataTree(6, forceTree);
            DA.SetDataTree(7, momentTree);
            DA.SetDataTree(8, elementTree);
            DA.SetDataTree(9, positionTree);
        }

        /// <summary>
        /// The tags of the nodes to report, each node once. Nothing asked for is every node a beam
        /// meets. What finds no such node is said in a warning rather than dropped.
        ///
        /// The order here does not reach the outputs: each node's branch is keyed by its tag, and a
        /// tree keeps its branches in path order, so they come out in tag order however the points
        /// were given.
        /// </summary>
        private List<int> Nodes(NodeGraph graph, List<Point3d> points, List<int> tags, double tolerance)
        {
            if (points.Count == 0 && tags.Count == 0)
                return graph.BeamNodes.ToList();

            var nodes = new List<int>();
            var seen = new HashSet<int>();
            var missedPoints = new List<Point3d>();
            var unknown = new List<int>();
            var bare = new List<int>();

            foreach (var point in points)
            {
                int tag = graph.NodeAt(point, tolerance);
                if (tag == 0)
                    missedPoints.Add(point);
                else if (seen.Add(tag))
                    nodes.Add(tag);
            }

            foreach (int tag in tags)
            {
                if (graph.Degree(tag) > 0)
                {
                    if (seen.Add(tag))
                        nodes.Add(tag);
                }
                else if (graph.Has(tag))
                    bare.Add(tag);
                else
                    unknown.Add(tag);
            }

            if (missedPoints.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"No beam or truss ends within {tolerance} of " +
                    $"{string.Join("; ", missedPoints.Take(5).Select(point => point.ToString()))}" +
                    $"{(missedPoints.Count > 5 ? $" and {missedPoints.Count - 5} more" : "")}. Put the " +
                    "point on a member's end, or snap it there.");

            if (unknown.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The model has no node {Preview(unknown)}. Nodes that beams meet: " +
                    $"{NodeFilter.Preview(graph.BeamNodes.Select(tag => (int?)tag))}.");

            if (bare.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"No beam or truss meets node {Preview(bare)}, so there are no member forces " +
                    "there to report.");

            return nodes;
        }

        private static string Preview(List<int> tags)
        {
            const int shown = 8;
            return tags.Count <= shown
                ? string.Join(", ", tags)
                : string.Join(", ", tags.Take(shown)) + $", ... ({tags.Count} in all)";
        }


        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Node_Forces__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{31884292-E436-4A72-B51E-7D0D67E33845}");
    }
}
