using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class LayeredShellSection : GH_Component
    {
        /// <summary>
        /// How many equal layers a single material and thickness is split into.
        ///
        /// There is no input for it. A stack that matters is described by listing its materials and
        /// thicknesses, and for the short form - one material through the depth - the count only
        /// sets how finely the section is sampled through the thickness. Five is enough to see the
        /// bending stress vary and cheap enough not to think about, so the component says what it
        /// did in a remark rather than asking.
        /// </summary>
        private const int DefaultLayers = 5;

        public LayeredShellSection()
          : base("Layered Shell Section (Alpaca4d)", "Layered Shell",
            "Construct a shell section as a stack of layers, each with its own material and " +
            "thickness - reinforced concrete, cross-laminated timber, a laminate.\n" +
            $"One material and one thickness is split into {DefaultLayers} equal layers; a list of " +
            "materials with matching thicknesses builds the stack, bottom face first.\n" +
            "Orthotropic materials work, but nothing rotates a layer: a crossed ply is a second " +
            "material with Ex and Ey exchanged.",
            "Alpaca4d", "01_Section")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("SectionName", "SecName", "A label for the section, carried on the object and readable through Deconstruct. Nothing in the analysis reads it.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Material", "Material",
                "Materials the layers are made of, bottom face first. Any nD material, orthotropic " +
                "included.\n" +
                "Every layer shares one strain field, so in-plane slip across a soft core is not " +
                "modelled and the section comes out stiff. Model a laminate that matters as two " +
                "shells with a shear connection.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Thickness", "Thickness",
                $"[{Units.Length}] Thickness of each layer, bottom face first, matching Material.\n" +
                $"A single value against a single material is the whole section, split into " +
                $"{DefaultLayers} equal layers.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "The shell section. Feed it to the Section input of an ASD Shell.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string secName = "";
            DA.GetData(0, ref secName);

            var materials = new List<IMultiDimensionMaterial>();
            if (!DA.GetDataList(1, materials) || materials.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect at least one nD material.");
                return;
            }

            var thicknesses = new List<double>();
            if (!DA.GetDataList(2, thicknesses) || thicknesses.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give a thickness.");
                return;
            }

            Alpaca4d.Section.LayeredShellSection section;

            // One material and one thickness is the whole section, split into equal layers - the
            // same stack OpenSees builds from its own short form. Anything else is an explicit
            // stack, and then the split has nothing to say.
            bool autoSplit = materials.Count == 1 && thicknesses.Count == 1;

            if (autoSplit)
            {
                section = Alpaca4d.Section.LayeredShellSection.Uniform(secName, thicknesses[0], DefaultLayers, materials[0]);
            }
            else
            {
                // A single material against several thicknesses is the common case of one material
                // in layers of differing depth, so it is repeated rather than refused.
                if (materials.Count == 1)
                    materials = Enumerable.Repeat(materials[0], thicknesses.Count).ToList();

                if (materials.Count != thicknesses.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"{materials.Count} materials against {thicknesses.Count} thicknesses. Give one " +
                        "thickness per material, or a single material to use for all of them.");
                    return;
                }

                if (materials.Count < Alpaca4d.Section.LayeredShellSection.MinLayers)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"A layered shell needs at least {Alpaca4d.Section.LayeredShellSection.MinLayers} " +
                        $"layers; {materials.Count} were given. OpenSees refuses fewer - use a Plate Fiber " +
                        "Section for a single-material shell.");
                    return;
                }

                var layers = materials.Zip(thicknesses, (m, t) => new Alpaca4d.Section.ShellLayer(m, t));
                section = new Alpaca4d.Section.LayeredShellSection(secName, layers);
            }

            if (section.Layers.Any(layer => layer.Thickness <= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Every layer needs a thickness above zero.");
                return;
            }

            // Worth saying, because it is the one thing that reads differently from a Plate Fiber
            // section: results come from the layer centres, so the outermost stress reported is
            // half a layer in from the face. The split is worth saying too, since nothing on the
            // component asked for it.
            string stack = autoSplit
                ? $"One material and one thickness: split automatically into {DefaultLayers} equal layers, {section.Thickness} total."
                : $"{section.Layers.Count} layers, {section.Thickness} total.";

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{stack} Shell Stresses reports the layer centres, so Top and Bottom sit half a " +
                "layer in from the faces.");

            DA.SetData(0, section);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Layered_Shell__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{3A7E1C64-9D0B-4F52-8C71-2E6B5A9D41F8}");
    }
}
