using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Design Group: beams the Section Optimiser gives one section, and the sections it may be. The
    /// beams are named by ElementId, the way the result components filter them, so a group is drawn on
    /// the beam components and needs no wire back to them.
    /// </summary>
    public class DesignGroupComponent : GH_Component
    {
        public DesignGroupComponent()
          : base("Design Group (Alpaca4d)", "Design Group",
            "Beams that the Section Optimiser gives one section: the beams with these ElementIds, and the sections it may be. " +
            "The whole group takes the first section of the list, in the order given, that every one of its beams can carry its forces with. " +
            "Give a member drawn as several beams a group of its own, so that it ends up with one section, as it will be built.",
            "Alpaca4d", "12_Optimisation")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "Name", "What to call the group in the optimiser's messages and its Group output.", GH_ParamAccess.item, "");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId",
                "Which beams belong to the group: an ElementId (Col_3), a wildcard over them (Col*), an element tag (47), or a " +
                "regular expression (regex:^Col), mixed freely in one list. A beam can be in one group only.",
                GH_ParamAccess.list);
            pManager.AddGenericParameter("Sections", "Sections",
                "The sections the group's beams may have, from the 01_Section components or Section Library, in the order to try them: " +
                "the group takes the first that works, so put first the one you would rather have - the lightest, the cheapest, the one " +
                "in stock. The list is never sorted. Each needs a steel material and a shape to be checked - an Elastic Section cannot be.",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("DesignGroup", "DesignGroup", "The group, for the DesignGroups input of Section Optimiser.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            var elementIds = new List<string>();
            var items = new List<object>();

            DA.GetData(0, ref name);
            if (!DA.GetDataList(1, elementIds)) return;
            if (!DA.GetDataList(2, items)) return;

            ElementFilter.Create(elementIds, out var badTerms);
            foreach (var problem in badTerms)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);

            var sections = new List<IUniaxialSection>();
            foreach (var item in items)
            {
                var value = item is GH_ObjectWrapper wrapper ? wrapper.Value : item;
                if (value is IUniaxialSection section)
                    sections.Add(section);
                else if (value != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{value.GetType().Name} is not a beam section.");
                    return;
                }
            }

            var notSteel = sections.Where(section => !(DesignGrades.Of(section.Material) is SteelGrade)).ToList();
            if (notSteel.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{notSteel.Count} section(s) have a material with no steel grade, so they cannot be checked and will never be picked " +
                    $"({Names(notSteel)}). Make the material with Material Library Elastic, or name it after its grade (\"S355\").");

            var shapeless = sections.Except(notSteel)
                                    .Where(section => SteelSection.Of(section, (SteelGrade)DesignGrades.Of(section.Material), Fabrication.Rolled) == null)
                                    .ToList();
            if (shapeless.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{shapeless.Count} section(s) have no shape to check - an Elastic Section - and will never be picked ({Names(shapeless)}).");

            DA.SetData(0, new DesignGroup
            {
                Name = name ?? "",
                ElementIds = elementIds,
                Sections = sections,
            });
        }

        private static string Names(List<IUniaxialSection> sections)
        {
            var names = sections.Select(section => section.ToString()).ToList();
            return string.Join(", ", names.Take(5)) + (names.Count > 5 ? ", ..." : "");
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Design_Group__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{35B2FF27-7BCC-4138-9CC7-267397D6A45A}");
    }
}
