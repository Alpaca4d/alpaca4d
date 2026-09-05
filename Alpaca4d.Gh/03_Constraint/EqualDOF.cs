using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    public class EqualDOF : GH_Component
    {
        public EqualDOF()
          : base("Equal DOF (Alpaca4d)", "Equal DOF",
            "Copy chosen degrees of freedom from one node to another, so the two read the same in " +
            "those and stay independent in the rest.\n" +
            "Copy, not connect: the offset between the nodes plays no part, so a rotation of the " +
            "first does not move the second. That is what separates it from a Rigid Link, which " +
            "carries the offset, and what makes it the one for a shear key, a slider, or two faces " +
            "tied for symmetry.",
            "Alpaca4d", "03_Constraint")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>The degrees of freedom OpenSees numbers 1 to 6, in its own order.</summary>
        private static readonly int[] AllDof = { 1, 2, 3, 4, 5, 6 };

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("RetainedPoint", "RetainedPoint", $"The node that leads [{Units.Length}]. It keeps its own degrees of freedom.", GH_ParamAccess.item);
            pManager.AddPointParameter("ConstrainedPoint", "ConstrainedPoint", $"The node that follows [{Units.Length}]. The degrees of freedom listed below stop being its own.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Dof", "Dof",
                "Which degrees of freedom to tie. Always global - 1, 2, 3 translation along X, Y " +
                "and Z, 4, 5, 6 rotation about them. Left empty all six are tied.\n" +
                "There is no local option: OpenSees ties nodal degrees of freedom, and a node has " +
                "no axes of its own. For a tie along a skewed axis use a Spring Link.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Constraint", "Constraint", "The tie. Feed it to the Constraints input of Assemble Model.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d retainedNode = Point3d.Unset;
            if (!DA.GetData(0, ref retainedNode))
                return;

            Point3d constrainedNode = Point3d.Unset;
            if (!DA.GetData(1, ref constrainedNode))
                return;

            var dof = new List<int>();
            DA.GetDataList(2, dof);

            if (dof.Count == 0)
                dof = AllDof.ToList();

            var outside = dof.Where(value => value < 1 || value > 6).ToList();
            if (outside.Count != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Dof runs 1 to 6; {string.Join(", ", outside)} is outside that. 1-3 are the " +
                    "translations along global X, Y and Z, 4-6 the rotations about them.");
                return;
            }

            // The same degree of freedom twice would be written twice, and OpenSees ties it twice -
            // harmless, but the second is redundant and reads as a mistake in the deck.
            dof = dof.Distinct().ToList();

            var equalDof = new Alpaca4d.Constraints.EqualDOF(retainedNode, constrainedNode,
                dof.Contains(1), dof.Contains(2), dof.Contains(3),
                dof.Contains(4), dof.Contains(5), dof.Contains(6));

            DA.SetData(0, equalDof);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Equal_DOF__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{2B6D4A17-9F30-4E85-B0C2-7A15E8D3946F}");
    }
}
