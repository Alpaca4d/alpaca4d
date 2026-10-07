using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Alpaca4d.Interop;
using Alpaca4d.Karamba3D;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Turns an assembled Karamba3D model into Alpaca parts - elements, supports, masses and one load
    /// pattern per load case - for Assemble Model, so that the user can add elements, loads,
    /// constraints or a dynamic excitation of their own before assembling.
    ///
    /// Karamba3D is optional for Alpaca4d, so this component names no Karamba type: the model comes in
    /// as a generic object and goes to <see cref="KarambaImport"/>, which only reaches Karamba's API
    /// once it has checked that Karamba3D is loaded.
    /// </summary>
    public class Karamba3DToAlpaca4D : GH_Component
    {
        public Karamba3DToAlpaca4D()
          : base("Karamba3DToAlpaca4D", "K3D_A4D",
            "Convert an assembled Karamba3D 3.1 model into the parts of an Alpaca model: elements, supports, masses and one load pattern per load case. " +
            "Plug them into Assemble Model together with elements, loads and constraints of your own, or with a dynamic excitation in place of Karamba's loads. " +
            "LoadPatterns is a tree with one branch per load case, so Assemble Model builds one model per load case; flatten the branches that should act together. " +
            "Whatever cannot be converted is reported: as an error when leaving it out changes the structure or its loads, " +
            "as a warning when it is approximated, and as a remark when it changes nothing but is worth knowing.",
            "Alpaca4d", "06_Assemble")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("KarambaModel", "KarambaModel", "An assembled model from Karamba3D's Assemble component, or an analysed one.", GH_ParamAccess.item);
            pManager.AddTextParameter("LoadCases", "LoadCases", "The load cases to convert, by name. Left empty, every load case is converted.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("AllowPartial", "AllowPartial",
                "When something cannot be converted, the component outputs nothing, so that a model differing from Karamba's is never analysed by accident. " +
                "Set to True to get the output anyway, without what could not be converted.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Elements", "Elements", "Beams and shells. Plug into Assemble Model's Elements, with any of your own.");
            pManager.Register_GenericParam("Supports", "Supports", "Plug into Assemble Model's Supports.");
            pManager.Register_GenericParam("LoadPatterns", "LoadPatterns",
                "A tree with one branch per load case, each holding that case's plain load pattern, self-weight included. Plugged into Assemble Model " +
                "as it is, it gives one model per load case. Assemble Model applies every pattern in a branch at the same time, so to make load cases " +
                "act together, flatten their branches into one. The patterns act on these Elements, so plug in all of them; elements of your own get " +
                "no self-weight from these.");
            pManager.Register_StringParam("LoadCase", "LoadCase", "The load case of each branch of LoadPatterns, in a tree of the same shape.");
            pManager.Register_GenericParam("Masses", "Masses",
                "Karamba's point masses, for a dynamic analysis: plug them into Assemble Model's LoadPatterns, which takes masses too, with your own excitation. " +
                "A static analysis does not need them. The elements carry their own mass from their material.");
            pManager.Register_DoubleParam("Tolerance", "Tolerance",
                $"A node tolerance that keeps Karamba's nodes apart [{Units.Length}]. Plug it into Assemble Model's Tolerance: Assemble's default can merge " +
                "Karamba nodes that lie closer than that. Elements of your own have to meet these within this tolerance.");
            pManager.Register_StringParam("Report", "Report", "Everything the conversion left out or changed, errors first.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo goo = null;
            if (!DA.GetData(0, ref goo) || goo == null)
                return;

            var loadCases = new List<string>();
            DA.GetDataList(1, loadCases);
            bool allowPartial = false;
            DA.GetData(2, ref allowPartial);

            object value = goo is GH_ObjectWrapper wrapper ? wrapper.Value : goo.ScriptVariable();

            var imported = ReadKarambaModel(value);
            if (imported == null)
                return;

            var parts = ImportModelBuilder.BuildParts(imported, loadCases, allowPartial);

            foreach (var entry in parts.Report.Entries)
                AddRuntimeMessage(LevelOf(entry.Level), entry.Message);

            if (parts.Report.HasErrors && !allowPartial)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Nothing is output while there are errors. Set AllowPartial to True to get the output anyway, without what could not be converted.");

            DA.SetDataList(6, parts.Report.Lines());
            if (parts.Elements.Count == 0)
                return;

            DA.SetDataList(0, parts.Elements);
            DA.SetDataList(1, parts.Supports);
            DA.SetDataList(4, parts.Masses);
            DA.SetData(5, parts.Tolerance);

            // One branch per load case, below this solution's own path: {0;0}, {0;1} ... for one model.
            // Assemble Model then runs once per branch, reusing the single branch of Elements and
            // Supports each time, which is one model per load case.
            var patterns = new DataTree<object>();
            var names = new DataTree<string>();
            var basePath = DA.ParameterTargetPath(2);
            for (int i = 0; i < parts.LoadPatterns.Count; i++)
            {
                var path = basePath.AppendElement(i);
                patterns.Add(parts.LoadPatterns[i], path);
                names.Add(parts.LoadCases[i], path);
            }
            DA.SetDataTree(2, patterns);
            DA.SetDataTree(3, names);
        }

        /// <summary>
        /// Checks that Karamba3D is loaded and is the right version, then reads the model - or returns
        /// null, with the reason on the component.
        /// </summary>
        private ImportModel ReadKarambaModel(object value)
        {
            var version = KarambaImport.LoadedVersion();
            if (version == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Karamba3D is not loaded. Install Karamba3D 3.1 to convert its models.");
                return null;
            }

            if (version.Major != KarambaImport.SupportedMajorVersion)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"This converter reads Karamba3D {KarambaImport.SupportedMajorVersion}.x models, and the Karamba3D loaded is {version}.");
                return null;
            }

            if (!KarambaImport.IsKarambaModel(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"KarambaModel takes a model from Karamba3D's Assemble or Analyze component, not {value?.GetType().Name ?? "nothing"}.");
                return null;
            }

            try
            {
                return Read(value);
            }
            catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is System.IO.FileLoadException ||
                                       ex is TypeLoadException || ex is MissingMemberException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"The Karamba3D loaded ({version}) does not match the version this converter was built for (3.1): {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Reading the Karamba3D model failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Kept apart so that a Karamba3D that cannot be loaded fails here, inside the try block of
        /// <see cref="ReadKarambaModel"/>, rather than when that method itself is compiled.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ImportModel Read(object value) => KarambaImport.Read(value);

        private static GH_RuntimeMessageLevel LevelOf(ReportLevel level)
        {
            switch (level)
            {
                case ReportLevel.Error: return GH_RuntimeMessageLevel.Error;
                case ReportLevel.Warning: return GH_RuntimeMessageLevel.Warning;
                default: return GH_RuntimeMessageLevel.Remark;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Karamba_to_Alpaca__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8E21C4A7-5B3D-4F09-9C6E-1A7D32B0E584}");
    }
}
