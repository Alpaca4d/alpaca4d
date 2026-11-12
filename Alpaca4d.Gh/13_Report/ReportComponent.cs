using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;

using Alpaca4d.Report;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Writes a PDF report: its page, from Report Page, and its content in order - the parts the other
    /// report components hand out, Report Text, Table, Result Table, View and Page Break, or a template
    /// written in JSON - much as a documentation template lists them in FEM-Design. The same parts
    /// make the same report of another model, or of this one after a change.
    ///
    /// The pictures are drawn by Model View, without one on the canvas (<see cref="ReportFigures"/>):
    /// each picture's settings are the ones its menus would have. The report is written once the
    /// solution is over, as the pictures need the Rhino view to themselves, and the component then
    /// solves again to hand out what it wrote. The Open report bar under the component opens it.
    /// </summary>
    public class ReportComponent : GH_Component
    {
        public ReportComponent()
          : base("Report (Alpaca4d)", "Report",
            "Write a PDF report: tables, text and pictures of the models drawn by Model View - without a Model View on the canvas.\n" +
            "Page takes the paper, margins and title block from Report Page. Content takes what goes in the report, in order: the outputs of " +
            "Report Text, Report Table, Report Result Table, Report View and Report Page Break - wire them through a Merge to keep their order - " +
            "or a template written in JSON, or the path of a .json file.\n" +
            "Left empty, Content is a report of each model given to AlpacaModel; the Template output hands out the JSON of what was written, " +
            "to copy and change.\n" +
            "Set Run to True to write it: the pictures borrow the active Rhino view for a moment. Then click Open report, under the component, to read it.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private const int ModelsInput = 0, PageInput = 1, ContentInput = 2, FileInput = 3, RunInput = 4;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The models the report is of: from Assemble, Run Analysis, Natural Vibration, Linear Buckling and Pushover Analysis. Report View " +
                "and Report Result Table hold none: each is drawn or made of the model here of its topic - the model as built, the static analysis, " +
                "the natural vibration, the stability, the pushover. A template written in JSON names them so too, or by their place here from 0.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Page", "Page", "The paper, the margins and the title block, from Report Page. Left empty, A4 with a plain header and footer.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Content", "Content",
                "What goes in the report, in order: the outputs of Report Text, Report Table, Report Result Table, Report View and Report Page Break, " +
                "or a template in JSON, or the path of a .json file. A panel's lines are joined into one text.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            // One list, whatever tree a Merge hands it: a part is not lost to a second solution.
            pManager[pManager.ParamCount - 1].DataMapping = GH_DataMapping.Flatten;
            pManager.AddTextParameter("File", "File",
                "Where to write the PDF. Left empty, beside the Grasshopper file, named after it; a folder, in it. " +
                "The pictures go in a folder beside it.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Run", "Run", "True to write the report: a Button, or a Toggle to write it again whenever the models change.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File", "File", "The PDF written. Open report, under the component, opens it.", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log", "What could not be made, and why. It is marked in the report too, where it would have gone.", GH_ParamAccess.list);
            pManager.AddTextParameter("Template", "Template", "The report as a template in JSON: to read what goes in it, or to copy and change.", GH_ParamAccess.item);
        }

        /// <summary>What a run wrote, handed out until the next one.</summary>
        private sealed class Written
        {
            public string File;
            public string Template;
            public int Pages;
            public List<string> Pictures = new List<string>();
            public List<string> Log = new List<string>();
            public string Error;
        }

        /// <summary>What a run is to write, held from the solution that asked for it to the idle moment after.</summary>
        private sealed class Job
        {
            public List<Alpaca4d.Model> Models;
            public string Template;
            public string File;
            public string Folder;
        }

        private const int FileOutput = 0, LogOutput = 1, TemplateOutput = 2;

        private Written _written;
        private bool _fresh;

        /// <summary>Where the PDF goes, as File says, from the last solution: what Open report opens, written now or before.</summary>
        private string _target;
        private Job _pending;
        private bool _scheduled;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (DA.Iteration > 0) return;

            // The solution that follows a run: what it wrote, as it is.
            if (_fresh)
            {
                _fresh = false;
                Hand(DA, _written);
                return;
            }

            var models = new List<Alpaca4d.Model>();
            DA.GetDataList(ModelsInput, models);
            models = models.Where(m => m != null).ToList();

            Grasshopper.Kernel.Types.IGH_Goo page = null;
            DA.GetData(PageInput, ref page);
            var content = new List<Grasshopper.Kernel.Types.IGH_Goo>();
            DA.GetDataList(ContentInput, content);

            string file = null;
            DA.GetData(FileInput, ref file);

            bool run = false;
            DA.GetData(RunInput, ref run);

            var document = OnPingDocument();
            string folder = document != null && document.IsFilePathDefined ? Path.GetDirectoryName(document.FilePath) : null;
            _target = PdfPath(file, document);

            string template;
            try
            {
                template = Assemble(models, page, content, folder);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (models.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No model to report on: wire the models into AlpacaModel - from Assemble and from the analyses.");
                return;
            }
            DA.SetData(TemplateOutput, template);

            if (!run)
            {
                if (_written != null) Hand(DA, _written);
                else AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    ReportToOpen != null ? "Set Run to True to write the report again, or click Open report to read the one written before."
                                         : "Set Run to True to write the report, then click Open report to read it.");
                return;
            }

            // Free up to the element limit, as Model View is: its pictures are Model View's.
            foreach (var model in models)
                if (!LicenseGate.AllowsModel(this, model, Alpaca4d.License.License.FreeLimitFor(model), "Report draws"))
                    return;

            _pending = new Job { Models = models, Template = template, File = _target, Folder = folder };
            if (!_scheduled)
            {
                RhinoApp.Idle += OnIdle;
                _scheduled = true;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Writing the report...");
        }

        /// <summary>
        /// The report as one template: a template given in Content, or the default one when Content
        /// is empty; then the parts in Content after its own, in order, each with its model added to
        /// <paramref name="models"/> and named by its place there; then the page over it.
        /// </summary>
        private static string Assemble(List<Alpaca4d.Model> models, Grasshopper.Kernel.Types.IGH_Goo page,
                                       List<Grasshopper.Kernel.Types.IGH_Goo> content, string folder)
        {
            JObject root = null;
            var blocks = new JArray();
            var pages = new List<JObject>();
            var text = new List<string>();

            void Part(ReportPart part)
            {
                if (part.IsPage) { pages.Add(part.Json); return; }
                var json = (JObject)part.Json.DeepClone();
                if (part.Model != null)
                {
                    models.Add(part.Model);
                    Attach(json, models.Count - 1);
                }
                blocks.Add(json);
            }

            foreach (var goo in content.Where(g => g != null))
            {
                var part = (goo as GH_ReportPart)?.Value ?? (goo.ScriptVariable() as ReportPart);
                if (part != null) { Part(part); continue; }

                // Text: a block or a whole template in JSON each, or the lines of one between them.
                string line = goo.ToString();
                if (goo is Grasshopper.Kernel.Types.GH_String s) line = s.Value;
                JToken parsed = null;
                try { parsed = JToken.Parse(line); } catch (JsonException) { }
                if (parsed is JObject obj && text.Count == 0)
                {
                    if (obj["content"] != null && root == null) root = obj;
                    else blocks.Add(obj);
                }
                else if (parsed is JArray array && text.Count == 0)
                {
                    foreach (var item in array.OfType<JObject>()) blocks.Add(item);
                }
                else
                {
                    text.Add(line);
                }
            }

            // Lines that make a template only together, or name the file that holds one.
            if (text.Count > 0)
            {
                var joined = string.Join("\n", text).Trim();
                if (!joined.StartsWith("{") && joined.IndexOf('\n') < 0)
                {
                    var path = joined.Trim('"');
                    if (!Path.IsPathRooted(path) && folder != null) path = Path.Combine(folder, path);
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"Content holds text that is neither JSON, which starts with {{, nor a file there is: {path}");
                    joined = File.ReadAllText(path);
                }
                JObject whole;
                try { whole = JObject.Parse(joined); }
                catch (JsonReaderException ex) { throw new FormatException($"The template in Content is not valid JSON, at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}"); }
                if (whole["content"] != null && root == null) root = whole;
                else blocks.Add(whole);
            }

            if (root == null && blocks.Count == 0)
                root = JObject.Parse(ReportBuilder.DefaultTemplate(models));
            if (root == null)
                root = new JObject { ["cover"] = false };
            if (!(root["content"] is JArray all))
                root["content"] = all = new JArray();
            foreach (var block in blocks) all.Add(block);

            if (page != null)
            {
                var part = (page as GH_ReportPart)?.Value ?? (page.ScriptVariable() as ReportPart);
                if (part == null && page is Grasshopper.Kernel.Types.GH_String json)
                {
                    var goo = new GH_ReportPart();
                    if (goo.CastFrom(json.Value)) part = goo.Value;
                }
                if (part == null) throw new InvalidCastException("Page takes what Report Page hands out.");
                pages.Insert(0, part.Json);
            }
            foreach (var settings in pages)
                foreach (var property in settings.Properties())
                    root[property.Name] = property.Value.DeepClone();

            return root.ToString(Formatting.Indented);
        }

        /// <summary>A part's own model, by its place among the models: what its figure draws, its repeat goes over, its table reads.</summary>
        private static void Attach(JObject json, int model)
        {
            if (json["figure"] is JObject figure && figure["model"] == null) figure["model"] = model;
            if (json["repeat"] != null && json["of"] == null) json["of"] = model;
            if (json["table"] != null && json["table"].Type == JTokenType.String && json["of"] == null) json["of"] = model;
        }

        /// <summary>Where the PDF goes: as File says, or beside the Grasshopper file and named after it.</summary>
        private static string PdfPath(string file, GH_Document document)
        {
            string ghFolder = document != null && document.IsFilePathDefined ? Path.GetDirectoryName(document.FilePath) : null;
            string name = document != null && document.IsFilePathDefined ? Path.GetFileNameWithoutExtension(document.FilePath) + " report" : "Alpaca4d report";

            if (string.IsNullOrWhiteSpace(file))
                return Path.Combine(ghFolder ?? Path.Combine(Path.GetTempPath(), "Alpaca4d"), name + ".pdf");

            file = file.Trim().Trim('"');
            if (Directory.Exists(file) || file.EndsWith("/") || file.EndsWith("\\"))
                return Path.Combine(file, name + ".pdf");
            if (!Path.IsPathRooted(file) && ghFolder != null)
                file = Path.Combine(ghFolder, file);
            return string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase) ? file : file + ".pdf";
        }

        /// <summary>
        /// The run itself, once the solution that asked for it is over: the pictures need the Rhino
        /// view, and a view drawn mid-solution would show whatever the solution had got to.
        /// </summary>
        private void OnIdle(object sender, EventArgs e)
        {
            var document = OnPingDocument();
            if (document != null && document.SolutionState == GH_ProcessStep.Process)
                return;

            RhinoApp.Idle -= OnIdle;
            _scheduled = false;

            var job = _pending;
            _pending = null;
            if (job == null || document == null) return;

            _written = Write(job);
            _fresh = true;
            ExpireSolution(true);
        }

        private static Written Write(Job job)
        {
            var written = new Written { File = job.File, Template = job.Template };
            try
            {
                string pictures = Path.Combine(Path.GetDirectoryName(job.File) ?? "", Path.GetFileNameWithoutExtension(job.File) + " pictures");
                if (Directory.Exists(pictures))
                    foreach (var old in Directory.GetFiles(pictures, "figure-*.png"))
                        File.Delete(old);

                ReportDocument report;
                using (var figures = new ReportFigures(pictures))
                {
                    report = ReportBuilder.Build(job.Template, job.Models, figures, job.Folder, written.Log);
                    written.Log.AddRange(figures.Remarks);
                    written.Pictures.AddRange(figures.Pictures);
                }

                var pages = ReportLayout.Lay(report);
                written.Pages = pages.Pages.Count;
                ReportPdf.Write(pages, job.File);
            }
            catch (IOException ex)
            {
                written.Error = $"The report could not be written to {job.File}: {ex.Message} Is it open in another program?";
            }
            catch (Exception ex)
            {
                written.Error = ex.Message;
            }
            return written;
        }

        private void Hand(IGH_DataAccess DA, Written written)
        {
            if (written == null) return;

            DA.SetData(TemplateOutput, written.Template);
            if (written.Error != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, written.Error);
                DA.SetDataList(LogOutput, written.Log);
                return;
            }

            DA.SetData(FileOutput, written.File);
            DA.SetDataList(LogOutput, written.Log);

            if (written.Log.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{written.Log.Count} thing{(written.Log.Count == 1 ? "" : "s")} could not be made as asked: see Log.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{written.Pages} page{(written.Pages == 1 ? "" : "s")} and {written.Pictures.Count} picture{(written.Pictures.Count == 1 ? "" : "s")} written to {written.File}. Click Open report to read it.");
        }

        // ── Open report ────────────────────────────────────────────────────────────────────────

        /// <summary>The PDF Open report opens: the one just written, or one written before where File points; null while there is none.</summary>
        internal string ReportToOpen
        {
            get
            {
                if (_written != null && _written.Error == null && File.Exists(_written.File)) return _written.File;
                return _target != null && File.Exists(_target) ? _target : null;
            }
        }

        /// <summary>The bar's caption, also for the pictures of the components in the docs.</summary>
        internal string ButtonText => "Open report";

        /// <summary>The PDF opened in the program the computer opens PDFs with.</summary>
        internal void OpenReport()
        {
            var path = ReportToOpen;
            if (path == null) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The report could not be opened: {ex.Message} It is at {path}");
                OnDisplayExpired(true);
            }
        }

        public override void CreateAttributes() => m_attributes = new ReportComponentAttributes(this);

        public override void RemovedFromDocument(GH_Document document)
        {
            if (_scheduled)
            {
                RhinoApp.Idle -= OnIdle;
                _scheduled = false;
            }
            base.RemovedFromDocument(document);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{4D6285D0-F64A-4CF1-A9A6-B62EDC615D7D}");
    }

    /// <summary>
    /// The Open report bar under the Report, drawn as a menu's header is drawn on the components with
    /// menus: dark while there is a report to open, faded while there is none.
    /// </summary>
    internal sealed class ReportComponentAttributes : GH_ComponentAttributes
    {
        private RectangleF _button;

        public ReportComponentAttributes(ReportComponent owner) : base(owner) { }

        private ReportComponent Report => (ReportComponent)Owner;

        /// <summary>Its text, and 4 px above and below: deeper than a menu's header, to read as a button.</summary>
        private int BarHeight => TextRenderer.MeasureText(Caption, Alpaca4d.UIWidgets.WidgetServer.Instance.MenuHeaderFont).Height + 8;

        private string Caption => Report.ButtonText;

        protected override void Layout()
        {
            base.Layout();
            var bounds = GH_Convert.ToRectangle(Bounds);
            int bar = BarHeight;
            bounds.Height += bar + 5;
            Bounds = bounds;
            _button = new RectangleF(bounds.X + 3, bounds.Bottom - bar - 3, bounds.Width - 6, bar);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            bool ready = Report.ReportToOpen != null;
            var rectangle = GH_Convert.ToRectangle(_button);
            using (var capsule = GH_Capsule.CreateTextCapsule(rectangle, rectangle, GH_Palette.Black, Caption,
                       Alpaca4d.UIWidgets.WidgetServer.Instance.MenuHeaderFont, 0, 2, 5))
                capsule.Render(graphics, false, !ready || Owner.Locked, false);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && _button.Contains(e.CanvasLocation) && !Owner.Locked)
            {
                Report.OpenReport();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override bool IsTooltipRegion(PointF canvasPoint) => _button.Contains(canvasPoint) || base.IsTooltipRegion(canvasPoint);

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            if (!_button.Contains(canvasPoint))
            {
                base.SetupTooltip(canvasPoint, e);
                return;
            }
            var path = Report.ReportToOpen;
            e.Title = Caption;
            e.Text = path != null ? "Opens the report in the program that opens PDFs." : "There is no report yet: set Run to True to write it.";
            e.Description = path ?? "";
        }
    }
}
