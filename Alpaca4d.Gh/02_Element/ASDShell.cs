using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Drawing;
using System.Collections.Generic;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class ASDShell : GH_Component
    {
        public ASDShell()
          : base("ASD Shell (Alpaca4d)", "ASDQ4/ASDT3",
            "Construct a ASDShellQ4 element or ASDShellT3 Shell",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "Mesh", $"[{Units.Length}]", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section", "Section", "Shell section, which carries the thickness and the material. Connect a Plate Fiber Section.", GH_ParamAccess.item);
            pManager.AddColourParameter("Colour", "Colour", "Colour to draw the element in. Display only; it changes nothing in the analysis.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddVectorParameter("Local X Axis", "LocalX", "Direction the section's local x axis points in, which is what shell forces and stresses are reported in. Left empty each element takes its own default orientation, so an orthotropic material or a moment read per direction needs this set.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Is Corotational", "IsCorotational", "Use the corotational formulation, which keeps the element honest through large rotations at the cost of a slower analysis. Leave it off for small displacements.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Element", "Element", "Element");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh _mesh = null;
            DA.GetData(0, ref _mesh);

            IMultiDimensionSection section = null;
            DA.GetData(1, ref section);

            Color color = Alpaca4d.Colors.DefaultShell;
            DA.GetData(2, ref color);

            Vector3d localX = default;
            DA.GetData(3, ref localX);

            bool isCorotational = false;
            DA.GetData(4, ref isCorotational);

            string elementId = null;
            DA.GetData(5, ref elementId);

            var meshes = new List<Mesh>();

            if (_mesh.Faces.Count > 0)
            {
                meshes = Utils.ExplodeMesh(_mesh);
            }
            else
            {
                meshes.Add(_mesh);
            }

			var elements = new List<Alpaca4d.Generic.IShell>();
			foreach (var mesh in meshes)
            {
				if (mesh.Vertices.Count == 4)
                {
                    var element = new Alpaca4d.Element.ASDShellQ4(mesh, section, localX, isCorotational);
                    element.Color = color;
                    // Every face of the mesh takes the one identifier typed here, so a whole slab
                    // is one group and asking for that identifier returns all of it. Give a list of
                    // identifiers to label the faces apart instead.
                    element.ElementId = elementId;

                    elements.Add(element);
                }
                else if (mesh.Vertices.Count == 3)
                {
                    var element = new Alpaca4d.Element.ASDShellT3(mesh, section, localX, isCorotational);
                    element.Color = color;
                    element.ElementId = elementId;

                    elements.Add(element);
                }
            }
			DA.SetDataList(0, elements);
        }


        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.ASD_ShellQ4__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{ee45cffd-ccdc-41d4-aa93-551fb576c2fc}");
    }

}