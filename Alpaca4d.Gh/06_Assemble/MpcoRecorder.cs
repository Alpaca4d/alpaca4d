using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What Run Analysis writes to the results file, chosen by hand rather than by analysis type.
    ///
    /// One tick box per result a result component can read back, named as the "recorder mpco" command
    /// names it, so the name on the box is the one a result component asks for when it finds nothing
    /// to read. MPCO records far more than this, but a result nothing in Alpaca4d reads is not offered.
    /// Every box starts ticked, so dropping this in changes nothing until something is unticked.
    /// </summary>
    public class MpcoRecorder : GH_ExtendableComponent
    {
        private const string DefaultFileName = "recorder.mpco";

        // Filled in Setup, in the order of Recorder.NodeResultTypes and Recorder.ElementResultTypes:
        // a box is saved by its position, so the lists and the catalogues must stay in step.
        private readonly List<MenuCheckBox> _nodeBoxes = new List<MenuCheckBox>();
        private readonly List<MenuCheckBox> _elementBoxes = new List<MenuCheckBox>();

        public MpcoRecorder()
          : base("MPCO Recorder (Alpaca4d)", "MPCO Recorder",
            "Chooses which results Run Analysis writes to the results file, in place of the set it " +
            "picks for the analysis type.\n" +
            "Every result a result component can read has a box in the Nodes or Elements menu, and all " +
            "of them start ticked. Untick what is not needed - a big analysis runs faster and writes a " +
            "smaller file - and connect the Recorder to the Recorders input of Assemble Model. A result " +
            "component asked for something that was not recorded says so.\n" +
            "The file is an MPCO (HDF5) file, which STKO also opens.",
            "Alpaca4d", "06_Assemble")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("FileName", "FileName",
                "Name of the results file, written beside the Grasshopper document. Letters, digits, " +
                "'_', '-' and '.' only. Two analyses in one document keep their results apart by " +
                "writing to files of their own.",
                GH_ParamAccess.item, DefaultFileName);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Recorder", "Recorder", "The recorder. Feed it to the Recorders input of Assemble Model.");
        }

        #region UI

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            _nodeBoxes.Clear();
            _elementBoxes.Clear();

            attr.AddMenu(Menu(0, "Nodes", "Results at the nodes",
                Alpaca4d.Recorder.NodeResultTypes, Alpaca4d.Recorder.TransientNodeResults, _nodeBoxes));
            attr.AddMenu(Menu(1, "Elements", "Results in the elements",
                Alpaca4d.Recorder.ElementResultTypes, Alpaca4d.Recorder.DefaultElementResults, _elementBoxes));

            attr.MinWidth = 230f;
        }

        private GH_ExtendableMenu Menu(int index, string name, string header,
            IReadOnlyList<string> results, IReadOnlyList<string> ticked, List<MenuCheckBox> boxes)
        {
            var menu = new GH_ExtendableMenu(index, name) { Name = name, Header = header };
            var panel = new MenuPanel(index, name.ToLowerInvariant() + "_panel");

            for (int i = 0; i < results.Count; i++)
            {
                var box = new MenuCheckBox(i, results[i], results[i]) { Active = ticked.Contains(results[i]) };
                box.ValueChanged += OnWidgetChanged;
                boxes.Add(box);
                panel.AddControl(box);
            }

            menu.AddControl(panel);
            return menu;
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();

            // Setup has hooked these up already. Off and on again rather than on a second time: every
            // tick reruns Run Analysis downstream, and a box hooked twice would run it twice.
            foreach (var box in _nodeBoxes.Concat(_elementBoxes))
            {
                box.ValueChanged -= OnWidgetChanged;
                box.ValueChanged += OnWidgetChanged;
            }
        }

        private void OnWidgetChanged(object sender, EventArgs e) => ExpireSolution(true);

        #endregion

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string fileName = DefaultFileName;

            DA.GetData(0, ref fileName);

            // The name goes on the Tcl line bare, and is opened beside the Grasshopper document, so
            // it is kept to what needs neither quoting nor a folder: no spaces, no separators, and no
            // leading dot, which would let ".." walk out of the document's folder.
            fileName = (fileName ?? "").Trim();
            if (!Regex.IsMatch(fileName, @"^[A-Za-z0-9_\-][A-Za-z0-9_.\-]*$"))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"\"{fileName}\" can't be a FileName. Use letters, digits, '_', '-' and '.' only - " +
                    "the file is always written beside the Grasshopper document.");
                return;
            }
            if (!System.IO.Path.HasExtension(fileName))
                fileName += ".mpco";

            var nodeResults = Ticked(_nodeBoxes, Alpaca4d.Recorder.NodeResultTypes);
            var elementResults = Ticked(_elementBoxes, Alpaca4d.Recorder.ElementResultTypes);

            if (nodeResults.Count == 0 && elementResults.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Nothing is ticked, so the file will hold the model and no results.");

            DA.SetData(0, new Alpaca4d.Recorder(fileName, nodeResults, elementResults));
        }

        private static List<string> Ticked(List<MenuCheckBox> boxes, IReadOnlyList<string> results)
        {
            return results.Where((name, i) => i < boxes.Count && boxes[i].Active).ToList();
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("3F6C2B8E-9D41-4A7E-B5C3-1E8F0A2D7C94");
    }
}
