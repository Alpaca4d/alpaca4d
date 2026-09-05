using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    public class Link : GH_Component
    {
        public Link()
          : base("Link (Alpaca4d)", "Link",
            "A spring between two nodes that are apart - a bearing, a bolt, a damper, the " +
            "interlayer of a laminate.\n" +
            "One material per direction, and only the directions given carry anything: a link on " +
            "direction 1 alone pulls along its own axis and resists nothing else.\n" +
            "Directions are local. 1 runs along the link, 2 and 3 across it, unless a Plane says " +
            "otherwise.",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Line", "Line",
                "One line per link, drawn from the node it starts at to the node it ends at. Both " +
                "ends have to land on nodes the model already has - a link joins parts together, " +
                "it does not make nodes of its own.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "Material",
                "One uniaxial material per direction, in the order the directions are given.\n" +
                $"A spring's material is read as a stiffness, so its E is a force per length " +
                $"[{Units.Force}/{Units.Length}] for a translation and a moment per radian for a rotation.",
                GH_ParamAccess.list);
            pManager.AddIntegerParameter("Direction", "Direction",
                "Which local directions the materials act in: 1, 2, 3 translation along the local " +
                "x, y and z axes; 4, 5, 6 rotation about them. Anything left out is free.",
                GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "Plane",
                "The local frame the directions count along. Left empty its X axis runs along the " +
                "link and the other two are across it, which is what a bearing or an interlayer wants.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Mass", "Mass",
                $"Mass of the link itself [{Units.Mass}], split between its two nodes. A spring " +
                "normally has none; a bearing heavy enough to matter is a Mass Point.",
                GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Element", "Element", "The links. Feed them to the Elements input of Assemble Model.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var lines = new List<Line>();
            if (!DA.GetDataList(0, lines) || lines.Count == 0)
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
                    "Say which directions the link acts in: 1, 2, 3 translation along the local x, y " +
                    "and z axes, 4, 5, 6 rotation about them.");
                return;
            }

            // One material against several directions is the ordinary case of a spring that is the
            // same each way, so it is repeated rather than refused.
            if (materials.Count == 1 && directions.Count > 1)
                materials = Enumerable.Repeat(materials[0], directions.Count).ToList();

            if (!LinkInput.Check(materials, directions, this))
                return;

            var plane = Plane.Unset;
            DA.GetData(3, ref plane);

            string elementId = null;
            DA.GetData(4, ref elementId);

            double mass = 0.0;
            DA.GetData(5, ref mass);

            var links = new List<Alpaca4d.Element.Link>();
            foreach (var line in lines)
            {
                if (line.Length <= 0.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "A link needs two different points. To hold a single node against the " +
                        "ground, use Zero Length Spring instead.");
                    return;
                }

                var link = new Alpaca4d.Element.Link(line, materials.ToList(), directions.ToList(), plane);
                link.ElementId = elementId;
                link.Mass = mass;
                links.Add(link);
            }

            DA.SetDataList(0, links);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{5E9C2F84-1D67-4A03-9B58-C4F2E7A06D31}");
    }
}
