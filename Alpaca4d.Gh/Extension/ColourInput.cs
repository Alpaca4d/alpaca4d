using System;
using System.Drawing;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The optional Colour input of the section and material components: what the section or
    /// material is drawn in when Model View colours the elements by it.
    /// </summary>
    internal static class ColourInput
    {
        public const string Name = "Colour";

        public const string SectionDescription =
            "The colour the section is drawn in when Model View colours the elements by section. " +
            "Left empty, Model View gives each section a colour of its own.";

        public const string MaterialDescription =
            "The colour the material is drawn in when Model View colours the elements by material. " +
            "Left empty, Model View gives each material a colour of its own.";

        public static IGH_Param Make(string description)
        {
            return new Param_Colour
            {
                Name = Name,
                NickName = Name,
                Description = description,
                Access = GH_ParamAccess.item,
                Optional = true,
            };
        }

        /// <summary>The colour given, or null when none was - wherever the input sits among the others.</summary>
        public static Color? Read(GH_Component component, IGH_DataAccess DA)
        {
            return Read(DA, component.Params.Input.FindIndex(p => p.Name == Name));
        }

        /// <summary>The colour given at input <paramref name="index"/>, or null when none was.</summary>
        public static Color? Read(IGH_DataAccess DA, int index)
        {
            var colour = Color.Empty;
            if (index < 0 || !DA.GetData(index, ref colour)) return null;
            return colour;
        }

        /// <summary>
        /// Reads a component that has a Colour input from a file that may have been saved before it
        /// had one - see <see cref="StoredInputs"/>. <paramref name="read"/> is the component's own
        /// base.Read.
        /// </summary>
        public static bool ReadComponent(GH_Component component, GH_IReader reader, Func<GH_IReader, bool> read, string description)
        {
            StoredInputs.Match(component, reader, name => name == Name ? Make(description) : null);
            bool result = read(reader);
            StoredInputs.Ensure(component, () => Make(description), Name);
            return result;
        }
    }
}
