using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Shown as "Spring Link" and called Link in the code, after OpenSees' twoNodeLink.
    ///
    /// The display name earns its extra word next to Rigid Link, which sits two tabs away and does
    /// a job that overlaps this one. Both join two nodes; the difference is that a rigid link is an
    /// exact constraint with no stiffness to choose, and this is an element with a real stiffness
    /// per direction. Naming them Link and Rigid Link left the reader to guess which was which.
    /// </summary>
    public class Link : GH_Component
    {
        public Link()
          : base("Spring Link (Alpaca4d)", "Spring Link",
            "A spring between two nodes that are apart - a bearing, a bolt, a damper, the " +
            "interlayer of a laminate.\n" +
            "One material per direction, and only the directions given carry anything: a link on " +
            "direction 1 alone pulls along its own axis and resists nothing else.\n" +
            "Directions are local. 1 runs along the link, 2 and 3 across it, unless a Plane says " +
            "otherwise.\n" +
            "For a join with no give at all use a Rigid Link, which is exact and needs no stiffness.",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Line", "Line",
                $"One line per link [{Units.Length}], drawn from the node it starts at to the node " +
                "it ends at. Both ends have to land on nodes the model already has - a link joins " +
                "parts together, it does not make nodes of its own.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "Material",
                "One uniaxial material per direction, in the order the directions are given.\n" +
                $"A spring's material is read as a stiffness: its E is [{Units.Force}/{Units.Length}] " +
                $"for a translation and [{Units.Force}{Units.Length}/{Units.Angle}] for a rotation.",
                GH_ParamAccess.list);
            pManager.AddIntegerParameter("Direction", "Direction",
                "Which local directions the materials act in: 1, 2, 3 translation along the local " +
                "x, y and z axes; 4, 5, 6 rotation about them. Anything left out is free.",
                GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "Plane",
                "The frame the directions count along. Left empty its X axis runs along the link " +
                "and the other two are across it, which is what a bearing or an interlayer wants.\n" +
                "Give the world XY plane to count them along the global axes instead.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
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

            if (!Check(materials, directions))
                return;

            var plane = Plane.Unset;
            DA.GetData(3, ref plane);

            string elementId = null;
            DA.GetData(4, ref elementId);

            var links = new List<Alpaca4d.Element.Link>();
            foreach (var line in lines)
            {
                if (line.Length <= 0.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "A link needs two different points. Alpaca4d puts one node at each point, " +
                        "so there is nothing at a zero length line to join.");
                    return;
                }

                var link = new Alpaca4d.Element.Link(line, materials.ToList(), directions.ToList(), plane);
                link.ElementId = elementId;
                links.Add(link);
            }

            DA.SetDataList(0, links);
        }

        /// <summary>
        /// Everything that would otherwise reach OpenSees as a parse error, caught on the component
        /// that can be fixed rather than several steps downstream on Assemble Model. The element
        /// checks the same things when it writes its tcl, because a link built in code has to be
        /// caught too; these run first only so the message lands somewhere useful.
        /// </summary>
        private bool Check(List<IUniaxialMaterial> materials, List<int> directions)
        {
            if (materials.Count != directions.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{materials.Count} materials against {directions.Count} directions. Give one " +
                    "material per direction, or a single material to use in all of them.");
                return false;
            }

            var outside = directions.Where(direction => direction < 1 || direction > 6).ToList();
            if (outside.Count != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Direction runs 1 to 6; {string.Join(", ", outside)} is outside that. 1-3 are " +
                    "translation along the local x, y and z axes, 4-6 rotation about them.");
                return false;
            }

            if (directions.Distinct().Count() != directions.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The same direction is listed more than once. Each direction takes one material; " +
                    "for two springs in parallel, use two of these.");
                return false;
            }

            if (materials.Any(material => material == null))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "One of the materials is empty. Every direction needs one.");
                return false;
            }

            return true;
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{5E9C2F84-1D67-4A03-9B58-C4F2E7A06D31}");
    }
}
