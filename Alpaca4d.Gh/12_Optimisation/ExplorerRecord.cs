using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;

using Alpaca4d.Explorer;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Saves each design of a study - its inputs, its results, a picture and its 3D model - into
    /// a folder the Alpaca4d Explorer opens: data.csv, and a .png and a .glb per design next to it.
    ///
    /// A design is noted during its solution and written once the solution is over and Rhino has
    /// gone idle, because only then is the viewport showing it. During a flight Explorer Inputs
    /// tells it when; otherwise it waits for idle by itself, so moving a slider by hand records the
    /// design too, and so does anything else that drives the definition.
    ///
    /// The study only grows. A design already in data.csv is not written again, which is what lets a
    /// stopped study go on where it left off. Starting over is on the right-click menu, and removes
    /// data.csv and the files it names, and nothing else in the folder.
    /// </summary>
    public class ExplorerRecord : GH_Component
    {
        public const string ExplorerUrl = "https://alpaca4d.github.io/explorer/";
        private const string DefaultFolderName = "AlpacaExplorer";

        private StudyFile _study;
        private Pending _pending;
        private bool _recording;
        private bool _waitingForIdle;
        private List<string> _inputNames;
        private string _folder;
        private string _lastProblem;

        // Why the last design that should have been saved was not; a flight stops on it.
        private string _failure;

        public ExplorerRecord()
          : base("Explorer Record (Alpaca4d)", "Explorer Record",
            "Saves each design of a study into a folder the Alpaca4d Explorer opens: data.csv with the inputs and results, " +
            "and a picture and a 3D model of each design.\n" +
            "Set Write to true and press Fly on Explorer Inputs. Designs already saved are not saved again. " +
            "Right-click to open the folder or the explorer, or to start the study over.",
            "Alpaca4d", "12_Optimisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>The nickname and tab every Alpaca4d component shows, with whether it is recording under them.</summary>
        private string Status(string status) => Alpaca4d.Gh.ComponentMessage.MyMessage(this) + "\n" + status;

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Explorer_Record__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{CAC9F5C3-18E1-4C99-91EE-94640F700825}");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Folder", "Folder",
                $"The folder to save the study in. A relative path is taken from the .gh file's folder. " +
                $"Left empty, {DefaultFolderName}\\<name of the .gh file> next to the .gh file, or under Documents\\Alpaca4d for a definition not saved yet.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Inputs", "Inputs", "The inputs of the design, from the Inputs of Explorer Inputs.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Outputs", "Outputs", "The results of the design, from Explorer Outputs.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Image", "Image", "Which views to take a picture of, from Explorer Image. Left empty, the active viewport at 800 × 600.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Model", "Model",
                "What to save as the design's 3D model: the AlpacaModel, or meshes - vertex colours kept - Breps, curves and points. " +
                "Text dots and text become labels the explorer shows facing the camera. Left empty, no 3D model.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colours", "Colours", "Colour for each Model item, or one for all of them: a text dot's background, a text's colour. Left empty, meshes keep their vertex colours, " +
                "an AlpacaModel its elements' colours, and anything picked from Rhino the colour Rhino draws it in, as a text dot keeps its colour in Rhino; " +
                "dots, text and points made in Grasshopper are dark.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Write", "Write", "True to save designs.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Folder", "Folder", "The folder the study is saved in. Open it in the Alpaca4d Explorer, or drop it onto the explorer's page.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Designs", "Designs", "How many designs the study holds.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string folderText = null;
            var inputData = new List<IGH_Goo>();
            var outputData = new List<IGH_Goo>();
            object imageData = null;
            var modelData = new List<IGH_Goo>();
            var colours = new List<Color>();
            bool write = false;
            DA.GetData(0, ref folderText);
            DA.GetDataList(1, inputData);
            DA.GetDataList(2, outputData);
            DA.GetData(3, ref imageData);
            DA.GetDataList(4, modelData);
            DA.GetDataList(5, colours);
            DA.GetData(6, ref write);

            _recording = false;
            _failure = null;
            var document = OnPingDocument();
            try
            {
                _folder = ResolveFolder(document, folderText);
                if (_study == null || !string.Equals(_study.Folder, _folder, StringComparison.OrdinalIgnoreCase))
                {
                    _pending = null;
                    _study = StudyFile.Open(_folder);
                }
                else _study.Refresh();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The folder cannot be used: {ex.Message}");
                return;
            }
            DA.SetData(0, _folder);
            if (string.IsNullOrWhiteSpace(folderText) && (document == null || !document.IsFilePathDefined))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "This definition is not saved yet, so its study goes to Documents/Alpaca4d. Save the .gh file to keep the study next to it.");

            if (_lastProblem != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _lastProblem);
                _lastProblem = null;
            }

            var inputs = Values(inputData, "Inputs", "Explorer Inputs");
            var outputs = Values(outputData, "Outputs", "Explorer Outputs");
            var image = imageData == null ? ExplorerImageSettings.Default : ExplorerGoo.As<ExplorerImageSettings>(imageData);
            if (image == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Image takes what Explorer Image makes; the active viewport is taken instead.");
                image = ExplorerImageSettings.Default;
            }

            if (!write)
            {
                _pending = null;
                DA.SetData(1, _study.Count);
                Message = Status($"Not recording\n{Designs(_study.Count)}");
                return;
            }

            // Saving a study needs a license; showing what a folder already holds does not.
            if (!LicenseGate.Allows(this, "Explorer Record"))
            {
                _pending = null;
                DA.SetData(1, _study.Count);
                Message = Status($"Not recording\n{Designs(_study.Count)}");
                return;
            }

            // A design noted in the last solution and not written yet: something is solving the
            // definition without letting Rhino go idle in between, as Galapagos does. Write it now,
            // rather than lose it; its picture may show the design after it.
            if (_pending != null) Commit();

            if (inputs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the Inputs of Explorer Inputs: a design is known by its inputs.");
                DA.SetData(1, _study.Count);
                return;
            }

            var duplicate = inputs.Concat(outputs).GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Two columns are called {duplicate.Key}. Rename one of them.");
                DA.SetData(1, _study.Count);
                return;
            }

            bool hasModel = Params.Input[4].SourceCount > 0;
            var columns = StudyFile.ColumnsFor(inputs.Select(v => v.Name), outputs.Select(v => v.Name), image.ImageNames, hasModel);
            if (!_study.Accepts(columns))
            {
                _failure = "The folder holds a study with other columns than this design has. " + Difference(_study.Columns, columns) +
                    "\nRecord into another folder, or right-click and start a new study. A result given a list of a different length " +
                    "from one design to the next changes its columns too.";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _failure);
                DA.SetData(1, _study.Count);
                Message = Status($"Other study in folder\n{Designs(_study.Count)}");
                return;
            }

            _recording = true;
            _inputNames = inputs.Select(value => value.Name).ToList();
            var inputValues = inputs.Select(value => value.Value).ToList();
            if (_study.Contains(inputValues))
            {
                DA.SetData(1, _study.Count);
                Message = Status($"Recording\n{Designs(_study.Count)}\nthis one is in");
                return;
            }

            var warnings = new List<string>();
            byte[] glb = null;
            if (hasModel)
            {
                try
                {
                    glb = ExplorerModel.Build(modelData, colours, RhinoDoc.ActiveDoc, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"The 3D model could not be made: {ex.Message}");
                }
            }
            foreach (var warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            _pending = new Pending
            {
                Columns = columns,
                InputValues = inputValues,
                Cells = inputValues.Concat(outputs.Select(value => value.Value)).ToList(),
                Image = image,
                HasModel = hasModel,
                Glb = glb,
            };

            if (!ExplorerFlight.IsFlying(document)) CommitWhenIdle();

            DA.SetData(1, _study.Count + 1);
            Message = Status($"Recording\n{Designs(_study.Count + 1)}");
        }

        private static string Designs(int count) => count == 1 ? "1 design" : $"{ExplorerInputs.Count(count)} designs";

        /// <summary>Which columns the study has that the design has not, and the other way round.</summary>
        private static string Difference(IEnumerable<string> study, IEnumerable<string> design)
        {
            var have = study.ToList();
            var want = design.ToList();
            var parts = new List<string>();
            var missing = have.Except(want).ToList();
            var extra = want.Except(have).ToList();
            if (missing.Count > 0) parts.Add("Only the study has " + string.Join(", ", missing) + ".");
            if (extra.Count > 0) parts.Add("Only this design has " + string.Join(", ", extra) + ".");
            if (parts.Count == 0) parts.Add("The same columns are in another order.");
            return string.Join(" ", parts);
        }

        /// <summary>The named values in a list input, warning about anything else that arrived there.</summary>
        private List<ExplorerValue> Values(List<IGH_Goo> data, string input, string from)
        {
            var values = new List<ExplorerValue>();
            int other = 0;
            foreach (var goo in data)
            {
                var value = ExplorerGoo.As<ExplorerValue>(goo);
                if (value != null) values.Add(value);
                else if (goo != null) other++;
            }
            if (other > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{input} takes the named values {from} makes; {other} other item(s) are left out.");
            return values;
        }

        /// <summary>The folder to record into: as given, from the .gh file's folder when relative, or the default.</summary>
        private static string ResolveFolder(GH_Document document, string folder)
        {
            bool saved = document != null && document.IsFilePathDefined;
            string baseFolder = saved
                ? Path.GetDirectoryName(document.FilePath)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Alpaca4d");

            if (string.IsNullOrWhiteSpace(folder))
            {
                // Each unsaved definition gets a folder of its own, or two of them with the same
                // column names would each take the other's designs as already recorded.
                string study = saved
                    ? Path.GetFileNameWithoutExtension(document.FilePath)
                    : "Untitled-" + (document?.DocumentID.ToString("N").Substring(0, 8) ?? "0");
                return Path.GetFullPath(Path.Combine(baseFolder, DefaultFolderName, study));
            }
            return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(baseFolder, folder));
        }

        #region Recording

        private sealed class Pending
        {
            public List<string> Columns;
            public List<string> InputValues;
            public List<string> Cells;
            public ExplorerImageSettings Image;
            public bool HasModel;
            public byte[] Glb;
        }

        /// <summary>Whether this component saved its last design, or would have: Write is on and the study takes it.</summary>
        internal bool IsRecording => _recording && !Locked;

        /// <summary>Writes every design noted in this document and not written yet.</summary>
        internal static void CommitAll(GH_Document document)
        {
            foreach (var record in document.Objects.OfType<ExplorerRecord>())
                record.Commit();
        }

        /// <summary>Forgets every design noted and not written: its solution was aborted half way.</summary>
        internal static void DropAll(GH_Document document)
        {
            foreach (var record in document.Objects.OfType<ExplorerRecord>())
                record._pending = null;
        }

        /// <summary>Why a design that should have been saved was not, or null.</summary>
        internal static string Failure(GH_Document document)
        {
            return document.Objects.OfType<ExplorerRecord>().Select(record => record._failure).FirstOrDefault(failure => failure != null);
        }

        internal static bool AnyRecording(GH_Document document)
        {
            return document.Objects.OfType<ExplorerRecord>().Any(record => record.IsRecording);
        }

        /// <summary>
        /// Whether every recording Explorer Record already holds a design with these inputs, so the
        /// flight can skip it without solving. False when none is recording, or one is recording
        /// other inputs as well: those have to be solved to be known.
        /// </summary>
        internal static bool AllRecorded(GH_Document document, IList<ExplorerValue> inputs)
        {
            var records = document.Objects.OfType<ExplorerRecord>().Where(record => record.IsRecording).ToList();
            return records.Count > 0 && records.All(record => record.Holds(inputs));
        }

        private bool Holds(IList<ExplorerValue> inputs)
        {
            if (_study == null || _inputNames == null) return false;
            if (!_inputNames.SequenceEqual(inputs.Select(value => value.Name), StringComparer.Ordinal)) return false;
            return _study.Contains(inputs.Select(value => value.Value));
        }

        private void CommitWhenIdle()
        {
            if (_waitingForIdle) return;
            _waitingForIdle = true;
            RhinoApp.Idle += OnIdle;
        }

        private void OnIdle(object sender, EventArgs e)
        {
            var document = OnPingDocument();
            if (document != null && document.SolutionState == GH_ProcessStep.Process) return;
            RhinoApp.Idle -= OnIdle;
            _waitingForIdle = false;
            if (!ExplorerFlight.IsFlying(document)) Commit();
        }

        /// <summary>Takes the pictures, writes the model and adds the design to data.csv.</summary>
        internal void Commit()
        {
            var pending = _pending;
            _pending = null;
            if (pending == null || _study == null) return;

            var written = new List<string>();
            try
            {
                Directory.CreateDirectory(_study.Folder);
                var cells = new List<string>(pending.Cells);
                var problems = new List<string>();

                var views = pending.Image.Views.Count == 0 ? new List<string> { null } : pending.Image.Views.ToList();
                Func<string, string> pictureOf = (view) => view == null || views.Count == 1 ? null : StudyFile.FileStem(new[] { view });
                string stem = _study.StemFor(pending.InputValues, candidate =>
                    views.Select(view => PictureFile(candidate, pictureOf(view))).Concat(pending.HasModel ? new[] { candidate + ".glb" } : new string[0]));

                var doc = RhinoDoc.ActiveDoc;
                foreach (var view in views)
                {
                    string file = PictureFile(stem, pictureOf(view));
                    string path = Path.Combine(_study.Folder, file);
                    string problem;
                    bool saved = SavePicture(doc, view, pending.Image, path, out problem);
                    if (saved) written.Add(path);
                    cells.Add(saved ? file : "");
                    if (problem != null) problems.Add(problem);
                }

                if (pending.HasModel)
                {
                    string file = stem + ".glb";
                    if (pending.Glb != null)
                    {
                        string path = Path.Combine(_study.Folder, file);
                        File.WriteAllBytes(path, pending.Glb);
                        written.Add(path);
                        cells.Add(file);
                    }
                    else cells.Add("");
                }

                _study.Append(pending.Columns, pending.InputValues, cells);
                if (problems.Count > 0) _lastProblem = string.Join("\n", problems.Distinct());
                Message = Status($"Recording\n{Designs(_study.Count)}");
            }
            catch (Exception ex)
            {
                // data.csv open in Excel, a full disk: the pictures and the model of a design that is
                // not in data.csv belong to nothing, so they go too.
                foreach (var path in written)
                {
                    try { File.Delete(path); } catch (Exception) { }
                }
                _failure = _lastProblem = $"The last design was not saved: {ex.Message}";
                RhinoApp.WriteLine($"Explorer Record: {_lastProblem}");
            }
            OnDisplayExpired(true);
        }

        private static string PictureFile(string stem, string view) => view == null ? stem + ".png" : $"{stem}_{view}.png";

        /// <summary>
        /// A picture of a view, or of a named view shown in the active viewport and put back after.
        /// Grid and axes are left out: what is compared is the design.
        /// </summary>
        private static bool SavePicture(RhinoDoc doc, string viewName, ExplorerImageSettings settings, string path, out string problem)
        {
            problem = null;
            if (doc == null)
            {
                problem = "There is no Rhino document to take pictures in.";
                return false;
            }

            RhinoView view = viewName == null ? doc.Views.ActiveView : FindView(doc, viewName);
            RhinoViewport restored = null;
            if (view == null)
            {
                int index = doc.NamedViews.FindByName(viewName);
                view = doc.Views.ActiveView;
                if (index < 0 || view == null)
                {
                    problem = $"There is no viewport or named view called {viewName}.";
                    return false;
                }
                restored = view.ActiveViewport;
                restored.PushViewProjection();
                doc.NamedViews.Restore(index, restored);
            }
            if (view == null)
            {
                problem = "There is no viewport to take a picture of.";
                return false;
            }

            try
            {
                var capture = new ViewCapture
                {
                    Width = settings.Width,
                    Height = settings.Height,
                    ScaleScreenItems = false,
                    DrawAxes = false,
                    DrawGrid = false,
                    DrawGridAxes = false,
                    TransparentBackground = settings.Transparent,
                };
                using (var bitmap = capture.CaptureToBitmap(view))
                {
                    if (bitmap == null)
                    {
                        problem = $"Rhino could not take a picture of {viewName ?? "the active viewport"}.";
                        return false;
                    }
                    bitmap.Save(path, ImageFormat.Png);
                }
                return true;
            }
            finally
            {
                if (restored != null)
                {
                    restored.PopViewProjection();
                    view.Redraw();
                }
            }
        }

        /// <summary>The viewport called <paramref name="name"/>, ignoring case, or null.</summary>
        internal static RhinoView FindView(RhinoDoc doc, string name)
        {
            return doc.Views.FirstOrDefault(view =>
                string.Equals(view.ActiveViewport.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(view.MainViewport.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Menu

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Open the study folder", (sender, e) => OpenFolder(), _folder != null);
            Menu_AppendItem(menu, "Open the Alpaca4d Explorer", (sender, e) => OpenExplorer());
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Start a new study…", (sender, e) => StartOver(), _study != null && _study.Count > 0);
            Menu_AppendSeparator(menu);
        }

        private void OpenFolder()
        {
            Directory.CreateDirectory(_folder);
            Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true });
        }

        /// <summary>
        /// The explorer is a web page, and a web page cannot open a folder on this computer by
        /// itself. So the folder is opened too, ready to be dropped onto the page.
        /// </summary>
        private void OpenExplorer()
        {
            Process.Start(new ProcessStartInfo { FileName = ExplorerUrl, UseShellExecute = true });
            if (_folder != null && Directory.Exists(_folder)) OpenFolder();
        }

        private void StartOver()
        {
            var answer = Rhino.UI.Dialogs.ShowMessage(
                $"Delete the {Designs(_study.Count)} in\n{_study.Folder}\n\nThis removes data.csv and the pictures and models it lists. Other files in the folder are left alone.",
                "Explorer Record", Rhino.UI.ShowMessageButton.YesNo, Rhino.UI.ShowMessageIcon.Warning);
            if (answer != Rhino.UI.ShowMessageResult.Yes) return;

            try
            {
                _pending = null;
                _study.Clear();
            }
            catch (Exception ex)
            {
                Rhino.UI.Dialogs.ShowMessage($"The study could not be removed: {ex.Message}", "Explorer Record");
            }
            ExpireSolution(true);
        }

        #endregion

        public override void RemovedFromDocument(GH_Document document)
        {
            Unhook();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded) Unhook();
            base.DocumentContextChanged(document, context);
        }

        private void Unhook()
        {
            if (_waitingForIdle) RhinoApp.Idle -= OnIdle;
            _waitingForIdle = false;
            _pending = null;
        }
    }
}
