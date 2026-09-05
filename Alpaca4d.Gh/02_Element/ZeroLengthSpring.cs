using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    public class ZeroLengthSpring : GH_Component
    {
        public ZeroLengthSpring()
          : base("Zero Length Spring (Alpaca4d)", "Zero Length Spring",
            "An elastic support: a spring between a node and the ground - a bearing pad, a soil " +
            "spring, a foundation that gives.\n" +
            "One material per direction, and only the directions given are held: a spring on " +
            "direction 3 alone props the node up and leaves it free to slide and turn.\n" +
            "Use a Support to hold a node outright, and a Spring Link to join two nodes to each other.\n" +
            "Reaction Forces does not report these: the spring reacts against a node of its own, " +
            "which is not a support.",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Position", "Position",
                $"Point or Plane at the node to hold [{Units.Length}]. A Point counts the directions " +
                "along the global axes; a Plane counts them along its own, which is how an inclined " +
                "bearing is written. The node has to be one the model already has.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "Material",
                "One uniaxial material per direction, in the order the directions are given.\n" +
                $"A spring's material is read as a stiffness: its E is [{Units.Force}/{Units.Length}] " +
                $"for a translation and [{Units.Force}{Units.Length}/{Units.Angle}] for a rotation.",
                GH_ParamAccess.list);
            pManager.AddIntegerParameter("Direction", "Direction",
                "Which directions the materials act in: 1, 2, 3 translation along the plane's x, y " +
                "and z axes; 4, 5, 6 rotation about them. Anything left out is free.",
                GH_ParamAccess.list);
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Element", "Element", "The springs. Feed them to the Elements input of Assemble Model.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // A Point arriving on this input is cast to a world-aligned Plane at that point, the
            // same way the Support component takes one.
            var positions = new List<Plane>();
            if (!DA.GetDataList(0, positions) || positions.Count == 0)
                return;

            var materials = new List<IUniaxialMaterial>();
            if (!DA.GetDataList(1, materials) || materials.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect one uniaxial material per direction.");
                return;
            }

            var directions = new List<int>();
            if (!DA.GetDataList(2, directions) || directions.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Say which directions the spring holds: 1, 2, 3 translation along the plane's x, " +
                    "y and z axes, 4, 5, 6 rotation about them.");
                return;
            }

            // One material against several directions is a spring that is the same each way.
            if (materials.Count == 1 && directions.Count > 1)
                materials = Enumerable.Repeat(materials[0], directions.Count).ToList();

            if (!LinkInput.Check(materials, directions, this))
                return;

            string elementId = null;
            DA.GetData(3, ref elementId);

            var springs = new List<Alpaca4d.Element.ZeroLengthSpring>();
            foreach (var position in positions)
            {
                var spring = new Alpaca4d.Element.ZeroLengthSpring(
                    position.Origin, materials.ToList(), directions.ToList(), position);
                spring.ElementId = elementId;
                springs.Add(spring);
            }

            DA.SetDataList(0, springs);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Support__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8C41B7D2-6E09-4F31-A5B8-D307E92C6F4A}");
    }
}
