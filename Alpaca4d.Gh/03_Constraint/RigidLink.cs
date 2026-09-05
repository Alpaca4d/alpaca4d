using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    public class RigidLink : GH_Component
    {
        public RigidLink()
          : base("Rigid Link (Alpaca4d)", "Rigid Link",
            "Join two nodes with no give at all, so the second follows the first as a rigid body.\n" +
            "An exact constraint, not a stiff spring: there is no number to choose and nothing for " +
            "the solver to condition around. Use it for an offset, a haunch, a rigid bracket.\n" +
            "For a join with real flexibility, or one stiff in some directions and free in others, " +
            "use a Spring Link.",
            "Alpaca4d", "03_Constraint")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("RetainedPoint", "RetainedPoint", $"The node that leads [{Units.Length}]. It keeps its own degrees of freedom.", GH_ParamAccess.item);
            pManager.AddPointParameter("ConstrainedPoint", "ConstrainedPoint", $"The node that follows [{Units.Length}], as a rigid body about the one above.", GH_ParamAccess.item);
            pManager.AddTextParameter("Type", "Type",
                "Connect a 'ValueList'.\n" +
                "beam - a full rigid body, so a rotation of the retained node also moves this one " +
                "by the offset between them.\n" +
                "bar - the translations only, with no rotation carried and no offset lever.",
                GH_ParamAccess.item, "beam");
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Constraint", "Constraint", "The rigid link. Feed it to the Constraints input of Assemble Model.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d retainedNode = Point3d.Unset;
            if (!DA.GetData(0, ref retainedNode))
                return;

            Point3d constrainedNode = Point3d.Unset;
            if (!DA.GetData(1, ref constrainedNode))
                return;

            string _type = "beam";
            DA.GetData(2, ref _type);

            var type = (Alpaca4d.Constraints.RigidLinkType)Enum.Parse(typeof(Alpaca4d.Constraints.RigidLinkType), _type);
            var rigidLink = new Alpaca4d.Constraints.RigidLink(retainedNode, constrainedNode, type);
            DA.SetData(0, rigidLink);
        }

        protected override void BeforeSolveInstance()
        {
            List<string> resultTypes;
            
            resultTypes = Enum.GetNames(typeof(Alpaca4d.Constraints.RigidLinkType)).ToList();
            ValueListUtils.UpdateValueLists(this, 2, resultTypes);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Equal_DOF__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("C7A1A1B5-8C17-4B51-B9E2-3E8E9F3C6475");
    }
}
