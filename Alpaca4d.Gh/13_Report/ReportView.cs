using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

using Alpaca4d.Report;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A picture for the report, by topic: the model, a static analysis, the natural vibration modes,
    /// the buckling modes or a pushover. It holds no model: it says what to draw - the modes and how
    /// many, the result and its unit, the colours, the view - and the Report draws it of the model of
    /// that topic among its own, when it writes the report.
    ///
    /// A switcher, as each topic asks for its own things: how many modes for the vibration, which
    /// result for a static analysis, how many pictures through a pushover. Where the picture is taken
    /// from and its size are in the Picture menu of each.
    /// </summary>
    public class ReportView : GH_SwitcherComponent
    {
        private readonly List<SubComponent> _topics = new List<SubComponent>();

        public ReportView()
          : base("Report View (Alpaca4d)", "ReportView",
            "A picture for the report, by topic - the model, a static analysis, natural vibration, stability, a pushover or footfall - picked from the menu " +
            "at the bottom. It holds no model: it says what to draw, and the Report draws it of its own model of that topic, from its AlpacaModel input.\n" +
            "Each topic asks for what it needs: how many modes and their colours for natural vibration and stability, the result and its unit for a " +
            "static analysis, how many pictures through the push for a pushover, the colours and range of the response factor for footfall. " +
            "The Picture menu holds where the picture is taken from and its size.\n" +
            "Tables come after the picture, ticked under what they are the tables of, in the topic's Results menu (Model for the model): " +
            "the displacement or forces drawn, the modes drawn, the response factor, the sections or materials the model is coloured by, its supports.",
            "Alpaca4d", "13_Report")
        {
            ((GH_Component)this).Hidden = false;
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override string UnitMenuName => "Topic";

        protected override string DefaultEvaluationUnit => StaticTopic.Name;

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Figure", "Figure",
                "The pictures, for the Content of the Report: what to draw, as JSON, with the tables ticked after them. A static analysis or a pushover " +
                "hands out one for each component ticked under its results. The Report draws them of its own model of this topic.", GH_ParamAccess.list);
            // Flattened into {0}, as the parts of the other report components are: a list output would go
            // into {0;0}, and a Merge puts every {0} before it, the pictures after all the texts.
            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _topics.Add(new ModelTopic());
            _topics.Add(new StaticTopic());
            _topics.Add(new VibrationTopic());
            _topics.Add(new StabilityTopic());
            _topics.Add(new PushoverTopic());
            _topics.Add(new FootfallTopic());

            foreach (var topic in _topics)
            {
                topic.Parent_Component = this;
                topic.registerEvaluationUnits(mngr);
            }
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            foreach (var topic in _topics)
                topic.OnComponentLoaded();
        }

        /// <summary>A Value List wired to View is filled with what it takes.</summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            Fill("View", ReportTopic.ViewNames());
        }

        private void Fill(string input, List<string> names)
        {
            int index = Params.Input.FindIndex(p => p.Name == input);
            if (index >= 0) ValueList.UpdateValueLists(this, index, names, null);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null) return;

            foreach (var topic in _topics)
            {
                if (!unit.Name.Equals(topic.name())) continue;
                topic.SolveInstance(DA, out var msg, out var level);
                if (!string.IsNullOrEmpty(msg))
                    AddRuntimeMessage(level, msg);
                return;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{08439B75-6150-4771-ACC1-204708F25F17}");
    }

    /// <summary>
    /// What every topic of Report View has: a Caption, and the Picture menu - where the picture is
    /// taken from, the zoom, its width on the page, its shape and its legend - after its own inputs.
    /// </summary>
    internal abstract class ReportTopic : SubComponent
    {
        /// <summary>The views offered, by their name in the menu, and the name a figure gives each.</summary>
        private static readonly (string Menu, string Figure)[] Presets =
        {
            ("Iso south-east", "southeast"), ("Iso south-west", "southwest"), ("Iso north-east", "northeast"), ("Iso north-west", "northwest"),
            ("Top", "top"), ("Front", "front"), ("Back", "back"), ("Left", "left"), ("Right", "right"), ("Bottom", "bottom"),
        };

        protected const string CurrentView = "Current Rhino view";

        /// <summary>What View takes: the fixed views, the Rhino view as it is, and the views saved in the Rhino file.</summary>
        public static List<string> ViewNames()
        {
            var names = Presets.Select(p => p.Menu).Concat(new[] { CurrentView }).ToList();
            try
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc != null) names.AddRange(doc.NamedViews.Select(v => "Named: " + v.Name));
            }
            catch
            {
                // No Rhino to ask, as when a definition is written without one.
            }
            return names;
        }

        /// <summary>The caption a topic writes when Caption is left empty; empty for none.</summary>
        protected virtual string DefaultCaption => "";

        protected virtual bool LegendByDefault => true;

        protected void RegisterCaption(EvaluationUnit unit)
        {
            unit.RegisterInputParam(new Param_String(), "Caption", "Caption",
                "Written under the picture. It takes placeholders, such as {mode}, {frequency:F2}, {factor:F2} or {step}." +
                (DefaultCaption.Length > 0 ? $" Left empty: {DefaultCaption}" : ""), GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        /// <summary>The Picture menu's inputs, at the end of the unit's, and the menu that folds them away.</summary>
        protected void RegisterPicture(EvaluationUnit unit, string id)
        {
            int first = unit.Inputs.Count;
            unit.RegisterInputParam(new Param_String(), "View", "View",
                "Where the picture is taken from: Iso south-east (the default), Iso south-west, Iso north-east, Iso north-west, Top, Front, Back, " +
                "Left, Right, Bottom; Current Rhino view, the active view's camera when this component last solved; or Named: and a view saved in " +
                "the Rhino file. Wire a Value List to it to pick one.", GH_ParamAccess.item, new GH_String(Presets[0].Menu));
            unit.RegisterInputParam(new Param_Number(), "Zoom", "Zoom", "Closer than the model fits the picture, above 1, or further, below.", GH_ParamAccess.item, new GH_Number(1.0));
            unit.RegisterInputParam(new Param_Number(), "Width", "Width", "How much of the page's width the picture takes, 0.2 to 1.", GH_ParamAccess.item, new GH_Number(1.0));
            unit.RegisterInputParam(new Param_Number(), "Aspect", "Aspect", "The picture's height over its width, before it is cut down to what is drawn.", GH_ParamAccess.item, new GH_Number(0.62));
            unit.RegisterInputParam(new Param_Boolean(), "Legend", "Legend", "The colour scale under the picture.", GH_ParamAccess.item, new GH_Boolean(LegendByDefault));
            for (int i = first; i < unit.Inputs.Count; i++)
                unit.Inputs[i].Parameter.Optional = true;

            var menu = new GH_ExtendableMenu(0, id) { Name = "Picture", Header = "Where the picture is taken from, and its size on the page" };
            for (int i = first; i < unit.Inputs.Count; i++)
                menu.RegisterInputPlug(unit.Inputs[i]);
            unit.AddMenu(menu);
        }

        protected static void Optional(EvaluationUnit unit)
        {
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        /// <summary>The Picture menu and the caption, as the figure's settings.</summary>
        protected void Picture(IGH_DataAccess DA, JObject figure)
        {
            string view = null;
            DA.GetData("View", ref view);
            view = (view ?? Presets[0].Menu).Trim();
            var preset = Presets.FirstOrDefault(p => string.Equals(p.Menu, view, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Figure, view, StringComparison.OrdinalIgnoreCase));
            if (preset.Figure != null)
            {
                figure["view"] = preset.Figure;
            }
            else if (string.Equals(view, CurrentView, StringComparison.OrdinalIgnoreCase) || string.Equals(view, "current", StringComparison.OrdinalIgnoreCase))
            {
                var vp = RhinoDoc.ActiveDoc?.Views?.ActiveView?.ActiveViewport;
                if (vp != null)
                {
                    figure["camera"] = new JObject
                    {
                        ["location"] = new JArray(vp.CameraLocation.X, vp.CameraLocation.Y, vp.CameraLocation.Z),
                        ["target"] = new JArray(vp.CameraTarget.X, vp.CameraTarget.Y, vp.CameraTarget.Z),
                        ["up"] = new JArray(vp.CameraUp.X, vp.CameraUp.Y, vp.CameraUp.Z),
                        ["lens"] = vp.Camera35mmLensLength,
                        ["parallel"] = vp.IsParallelProjection
                    };
                }
            }
            else if (view.StartsWith("Named:", StringComparison.OrdinalIgnoreCase))
            {
                figure["view"] = "named:" + view.Substring(6).Trim();
            }
            else
            {
                figure["view"] = view;
            }

            double zoom = 1.0, width = 1.0, aspect = 0.62;
            bool legend = LegendByDefault;
            DA.GetData("Zoom", ref zoom);
            DA.GetData("Width", ref width);
            DA.GetData("Aspect", ref aspect);
            DA.GetData("Legend", ref legend);
            if (Math.Abs(zoom - 1.0) > 1e-9) figure["zoom"] = zoom;
            figure["width"] = width;
            figure["aspect"] = aspect;
            figure["legend"] = legend;

            string caption = null;
            DA.GetData("Caption", ref caption);
            if (!string.IsNullOrWhiteSpace(caption)) figure["caption"] = caption;
            else if (DefaultCaption.Length > 0) figure["caption"] = DefaultCaption;
        }

        /// <summary>The colours of a gradient, as #RRGGBB, when any are given.</summary>
        protected static void Colours(IGH_DataAccess DA, JObject figure)
        {
            var colours = new List<Color>();
            if (DA.GetDataList("Colours", colours) && colours.Count > 0)
                figure["colours"] = new JArray(colours.Select(c => ColorTranslator.ToHtml(Color.FromArgb(c.R, c.G, c.B))));
        }

        /// <summary>Which modes: a number for that many from the first, or all, 1-6, 1,3,5 as they are.</summary>
        protected static JToken Modes(string text)
        {
            text = (text ?? "all").Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? (JToken)count : text;
        }

        // ── The Results panel of the topics drawn without Model View's Results menu ─────────────

        private readonly List<(string Name, MenuCheckBoxGroup Tick, MenuCheckBoxGroup Columns, Func<bool> Shown)> _tables =
            new List<(string, MenuCheckBoxGroup, MenuCheckBoxGroup, Func<bool>)>();

        // Numbered past what an older Report View saved in the same panel, so that none is read into another.
        private int _widget = 300;

        /// <summary>
        /// The panel of what the topic draws - as Static analysis has its Results menu - in which
        /// <see cref="Table"/>, <see cref="Choice"/>, <see cref="Check"/>, <see cref="Box"/> and the
        /// captions put the topic's controls, as <see cref="ReportWidgets"/> lays them out: what is drawn
        /// at the left, and what goes with it - its choices, its table - in a frame under it.
        /// </summary>
        protected MenuPanel Panel(EvaluationUnit unit, string id, string name, string header)
        {
            var menu = new GH_ExtendableMenu(1, id) { Name = name, Header = header };
            var panel = new MenuPanel(1, id + "_panel");
            menu.AddControl(panel);
            menu.Expand();
            unit.AddMenu(menu);
            return panel;
        }

        /// <summary>The name of what the frame under it is about: Colour by, Mode shapes.</summary>
        protected static void Heading(IWidgetContainer into, string text) => into.AddControl(ReportWidgets.Heading(text));

        /// <summary>A grey caption over a choice in a frame.</summary>
        protected static void Caption(IWidgetContainer into, string text) => into.AddControl(ReportWidgets.Caption(text));

        /// <summary>The frame of what goes with the control above it.</summary>
        protected MenuGroupBox Box(IWidgetContainer into, string id)
        {
            var box = ReportWidgets.Box(_widget++, id);
            into.AddControl(box);
            return box;
        }

        /// <summary>One choice of what is drawn - the colours by, the component - round boxes in a grid, one ticked.</summary>
        protected MenuCheckBoxGroup Choice(IWidgetContainer into, string id, string[] items, int start = 0, int columns = 0)
        {
            var choice = ReportWidgets.Choice(_widget++, id, items, start, columns);
            choice.ValueChanged += (s, e) => { UpdateTables(); Parent_Component?.ExpireSolution(true); };
            into.AddControl(choice);
            return choice;
        }

        /// <summary>
        /// Something drawn or not - the supports, the loads: a square box at the menu's edge - or, in a
        /// frame, what goes with what is drawn, as the values: a round one.
        /// </summary>
        protected MenuCheckBoxGroup Check(IWidgetContainer into, string id, string label, bool on)
        {
            var box = into is MenuGroupBox ? ReportWidgets.Option(_widget++, id, label, on) : ReportWidgets.Tick(_widget++, id, label, on);
            box.ValueChanged += (s, e) => { UpdateTables(); Parent_Component?.ExpireSolution(true); };
            into.AddControl(box);
            return box;
        }

        /// <summary>
        /// A table of what is drawn, unticked to start with - a round box in a frame, a square one at the
        /// menu's edge - shown only while <paramref name="shown"/> says it has a use, and under it, ticked,
        /// its columns in a frame of their own, round boxes three to a row, all ticked: the columns Report
        /// Result Table offers.
        /// </summary>
        protected void Table(IWidgetContainer into, string id, string label, string name, Func<bool> shown = null)
        {
            const string tooltip = "The table of what is drawn, written after the picture";
            var tick = into is MenuGroupBox
                ? ReportWidgets.Option(_widget++, $"{id}_{name}", label, false, tooltip)
                : ReportWidgets.Tick(_widget++, $"{id}_{name}", label, false, tooltip);
            into.AddControl(tick);

            var columns = new MenuCheckBoxGroup(_widget++, $"{id}_{name}_columns")
            {
                Direction = MenuCheckBoxGroup.LayoutDirection.Grid,
                Round = true,
                Framed = true,
                Indent = 10f,
                // Three to a row - X, Y, Z - or two when a name is long, so the component stays narrow.
                Columns = (ReportTables.Options.TryGetValue(name, out var names) && names.Any(n => n.Length > 14)) ? 2 : 3,
                Header = "The columns of the table to write"
            };
            foreach (var option in ReportTables.Options.TryGetValue(name, out var options) ? options : new string[0])
                columns.Add(option, true);
            into.AddControl(columns);

            columns.ValueChanged += (s, e) => Parent_Component?.ExpireSolution(true);
            tick.ValueChanged += (s, e) => { UpdateTables(); Parent_Component?.ExpireSolution(true); };
            _tables.Add((name, tick, columns, shown ?? (() => true)));
            // Shown as they are to be from the start: a component just placed is not loaded, and has no other call.
            UpdateTables();
        }

        /// <summary>A table only while what it is the table of is drawn, and its columns only while it is ticked.</summary>
        protected void UpdateTables()
        {
            foreach (var (_, tick, columns, shown) in _tables)
            {
                tick.Visible = shown();
                columns.Visible = tick.Visible && tick[0] && columns.Count > 0;
            }
            Parent_Component?.Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        public override void OnComponentLoaded() => UpdateTables();

        /// <summary>What a topic adds to each of its tables, so that it follows the picture: footfall's, sorted by what is drawn.</summary>
        protected virtual void Amend(JObject table) { }

        /// <summary>
        /// A part for each picture a Results menu asks for - one per component ticked - each the
        /// <paramref name="shared"/> settings with its own result and caption, made a part by
        /// <paramref name="part"/>; with nothing ticked, the model drawn as it is, captioned
        /// <paramref name="bare"/> - or not at all - unless the caption given has no {result} in it.
        /// </summary>
        protected static List<JObject> PerPicture(ReportResultsMenu results, JObject shared, Func<JObject, JObject> part,
                                                  out string msg, out GH_RuntimeMessageLevel level, string bare = null)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            var parts = new List<JObject>();
            foreach (var picture in results.Pictures())
            {
                var figure = (JObject)shared.DeepClone();
                figure["results"] = new JArray { picture.Entry };
                if (figure["caption"] != null)
                    figure["caption"] = ReportResultsMenu.Caption((string)figure["caption"], picture);
                parts.Add(part(figure));
            }

            var empty = results.Empty().ToList();
            if (empty.Count > 0)
            {
                level = GH_RuntimeMessageLevel.Warning;
                msg = $"No component is ticked under {string.Join(" and ", empty)}: {(empty.Count == 1 ? "it draws" : "they draw")} nothing. Tick one or more, a picture each.";
            }
            if (parts.Count == 0)
            {
                var figure = (JObject)shared.DeepClone();
                figure["results"] = new JArray();
                string caption = (string)figure["caption"];
                if (caption != null && (caption.Contains("{result}") || caption.Contains("{component}") || caption.Contains("{unit}")))
                {
                    if (bare != null) figure["caption"] = bare;
                    else figure.Remove("caption");
                }
                parts.Add(part(figure));
                if (msg.Length == 0) msg = "No result is ticked under Results: the model is drawn as it is.";
            }
            else if (parts.Count > 1)
            {
                msg = (msg.Length > 0 ? msg + " " : "") + $"{parts.Count} pictures, one for each component ticked.";
            }
            return parts;
        }

        /// <summary>
        /// The picture, with the tables ticked after it, as one part: a group of blocks, or the picture
        /// alone when none is ticked. Each table is of the model of <paramref name="kind"/>.
        /// </summary>
        protected void Hand(IGH_DataAccess DA, JObject json, string kind)
        {
            var tables = new List<JObject>();
            foreach (var (name, tick, columns, _) in _tables.Where(t => t.Shown() && t.Tick[0]))
            {
                var table = new JObject { ["table"] = name, ["of"] = kind };
                var ticked = columns.Ticked.ToList();
                if (ticked.Count < columns.Count)
                    table["columns"] = new JArray(ticked.Select(i => columns.Labels[i]));
                Amend(table);
                tables.Add(table);
            }

            if (tables.Count > 0)
            {
                var group = new JArray { json };
                foreach (var t in tables) group.Add(t);
                json = new JObject { ["group"] = group };
            }

            DA.SetDataList(0, new[] { new GH_ReportPart(new ReportPart { Json = json }) });
        }
    }

    /// <summary>The model as it was built: coloured by element, section or material, with its supports and loads.</summary>
    internal sealed class ModelTopic : ReportTopic
    {
        public const string Name = "Report View Model";
        public override string name() => Name;
        public override string display_name() => "Model";
        protected override bool LegendByDefault => false;

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "The model as it was built.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            unit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", "Only these elements; the rest greyed. Left empty, all of them.", GH_ParamAccess.list);
            Optional(unit);
            RegisterCaption(unit);

            // What is drawn, and in a frame under each its table: the sections or materials it is coloured by, the supports.
            var panel = Panel(unit, "ReportView_Model_Panel", "Model", "What the picture draws of the model, and the table of each");
            Table(panel, "ReportView_Model", "Model summary", "model.summary");
            Heading(panel, "Colour by");
            var colour = Box(panel, "ReportView_Model_ColourBox");
            _colourBy = Choice(colour, "ReportView_Model_ColourBy", ColourNames, 1);
            Table(colour, "ReportView_Model", "Table", "model.sections", () => ColourBy == "Section");
            Table(colour, "ReportView_Model", "Table", "model.materials", () => ColourBy == "Material");
            _supports = Check(panel, "ReportView_Model_Supports", "Supports", true);
            var supports = Box(panel, "ReportView_Model_SupportsBox");
            Table(supports, "ReportView_Model", "Table", "model.supports", () => _supports == null || _supports[0]);
            _loads = Check(panel, "ReportView_Model_Loads", "Loads", true);
            RegisterPicture(unit, "ReportView_Model_Picture");
        }

        private static readonly string[] ColourNames = { "Element", "Section", "Material" };
        private MenuCheckBoxGroup _colourBy, _supports, _loads;

        private string ColourBy => _colourBy == null ? "Section" : _colourBy.Picked;

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            var ids = new List<string>();
            DA.GetDataList("ElementId", ids);

            var figure = new JObject { ["model"] = "model", ["colourBy"] = ColourBy, ["supports"] = _supports == null || _supports[0], ["loads"] = _loads == null || _loads[0] };
            if (ids.Count > 0) figure["elements"] = new JArray(ids);
            Picture(DA, figure);
            Hand(DA, new JObject { ["figure"] = figure }, "model");
        }
    }

    /// <summary>
    /// Results of the static analysis at a step - displacements, beam forces, stresses, reactions -
    /// picked from Model View's own Results menu.
    /// </summary>
    internal sealed class StaticTopic : ReportTopic
    {
        public const string Name = "Report View Static";
        public override string name() => Name;
        public override string display_name() => "Static analysis";

        private ReportResultsMenu _results;

        /// <summary>Each picture's own: the result and the component it draws, in its unit.</summary>
        protected override string DefaultCaption => "{result}: {component} [{unit}]";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "Results of the static analysis, from Run Analysis.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            unit.RegisterInputParam(new Param_Integer(), "Step", "Step", "The recorded step to draw, from 0. Left empty, the last: the loads all on.", GH_ParamAccess.item);
            Optional(unit);
            unit.RegisterInputParam(new Param_Colour(), "Colours", "Colours", "The gradient, low to high: the Colors component's, for one. Left empty, Model View's.", GH_ParamAccess.list);
            Optional(unit);
            unit.RegisterInputParam(new Param_Interval(), "Range", "Range", "The values the gradient spans, in the unit drawn. Left empty, what is drawn.", GH_ParamAccess.item);
            Optional(unit);
            RegisterCaption(unit);
            _results = new ReportResultsMenu(unit, "ReportView_Static_Results", false, () => Parent_Component);
            RegisterPicture(unit, "ReportView_Static_Picture");
        }

        public override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            _results?.UpdateVisibility();
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            int step = 0;
            Interval range = Interval.Unset;
            bool hasStep = DA.GetData("Step", ref step);
            DA.GetData("Range", ref range);

            // What every picture has, then a picture for each component ticked.
            var shared = new JObject { ["model"] = "static" };
            _results.WriteShared(shared);
            if (hasStep) shared["step"] = step;
            if (range.IsValid) shared["range"] = new JArray(range.T0, range.T1);
            Colours(DA, shared);
            Picture(DA, shared);

            var figures = PerPicture(_results, shared, figure => new JObject { ["figure"] = figure }, out msg, out level);
            DA.SetDataList(0, figures.Select(json => new GH_ReportPart(new ReportPart { Json = json })));
        }
    }

    /// <summary>The modes of a model, a picture each, so many to a row: what natural vibration and stability share.</summary>
    internal abstract class ModesTopic : ReportTopic
    {
        protected abstract string Kind { get; }
        protected abstract string DefaultModes { get; }
        protected override bool LegendByDefault => false;

        protected void RegisterModes(EvaluationUnit unit)
        {
            unit.RegisterInputParam(new Param_String(), "Modes", "Modes",
                "Which modes to draw, a picture each: a number for that many from the first, or all, a range such as 2-5, or 1,3,5.",
                GH_ParamAccess.item, new GH_String(DefaultModes));
            Optional(unit);
            unit.RegisterInputParam(new Param_Integer(), "Columns", "Columns", "How many pictures side by side.", GH_ParamAccess.item, new GH_Integer(2));
            Optional(unit);
            unit.RegisterInputParam(new Param_Colour(), "Colours", "Colours",
                "The gradient each mode is coloured with, by how far it moves: the Colors component's, for one. Left empty, Model View's.", GH_ParamAccess.list);
            Optional(unit);
            unit.RegisterInputParam(new Param_Number(), "Scale", "Scale",
                "How far the mode is drawn: at 1, its largest movement stands off the model as far as Model View draws a mode.", GH_ParamAccess.item, new GH_Number(1.0));
            Optional(unit);
            RegisterCaption(unit);
        }

        private static readonly string[] Components = { "Magnitude", "Ux", "Uy", "Uz" };
        private MenuCheckBoxGroup _component;

        /// <summary>The shapes drawn, and in a frame under them the component they are coloured by and the tables of the modes drawn.</summary>
        protected void RegisterShapes(EvaluationUnit unit, string id, string shapes, params (string Label, string Name)[] tables)
        {
            var panel = Panel(unit, id, "Results", "What the pictures draw of the modes, and the tables of the modes drawn");
            Heading(panel, shapes);
            var box = Box(panel, id + "_Box");
            Caption(box, "Coloured by");
            _component = Choice(box, id + "_Component", Components, 0, Components.Length);
            Caption(box, tables.Length == 1 ? "Table" : "Tables");
            foreach (var (label, name) in tables)
                Table(box, id, label, name);
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            string modes = DefaultModes;
            int columns = 2;
            double scale = 1.0;
            DA.GetData("Modes", ref modes);
            DA.GetData("Columns", ref columns);
            DA.GetData("Scale", ref scale);

            string component = _component == null ? "Magnitude" : _component.Picked;
            var figure = new JObject { ["result"] = "Displacement", ["component"] = component, ["deformed"] = true, ["scale"] = scale };
            Colours(DA, figure);
            Picture(DA, figure);
            Hand(DA, new JObject
            {
                ["repeat"] = "mode",
                ["of"] = Kind,
                ["modes"] = Modes(modes),
                ["columns"] = Math.Max(1, columns),
                ["content"] = new JArray { new JObject { ["figure"] = figure } }
            }, Kind);
        }
    }

    internal sealed class VibrationTopic : ModesTopic
    {
        public override string name() => "Report View Natural Vibration";
        public override string display_name() => "Natural vibration";
        protected override string Kind => "vibration";
        protected override string DefaultModes => "6";
        protected override string DefaultCaption => "Mode {mode}: f = {frequency:F2} Hz, T = {period:F3} s";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "The natural vibration modes, from Natural Vibration.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            RegisterModes(unit);
            RegisterShapes(unit, "ReportView_Vibration_Results", "Mode shapes", ("Natural frequencies", "vibration.modes"), ("Participating mass", "vibration.massRatios"));
            RegisterPicture(unit, "ReportView_Vibration_Picture");
        }
    }

    internal sealed class StabilityTopic : ModesTopic
    {
        public override string name() => "Report View Stability";
        public override string display_name() => "Stability";
        protected override string Kind => "buckling";
        protected override string DefaultModes => "4";
        protected override string DefaultCaption => "Buckling mode {mode}: αcr = {factor:F2}";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "The buckling modes, from Linear Buckling.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            RegisterModes(unit);
            RegisterShapes(unit, "ReportView_Stability_Results", "Buckled shapes", ("Buckling factors", "buckling.factors"));
            RegisterPicture(unit, "ReportView_Stability_Picture");
        }
    }

    /// <summary>
    /// A pushover, at its end or at so many steps through it, with its plastic hinges, the results
    /// picked from Model View's own Results menu.
    /// </summary>
    internal sealed class PushoverTopic : ReportTopic
    {
        public override string name() => "Report View Pushover";
        public override string display_name() => "Pushover";
        protected override string DefaultCaption => "{result}: {component} [{unit}], step {step} of {steps}";

        private ReportResultsMenu _results;

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "A pushover, from Pushover Analysis: its deformed shape and plastic hinges.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            unit.RegisterInputParam(new Param_Integer(), "Pictures", "Pictures",
                "How many pictures through the push, spread over it, the last at its end.", GH_ParamAccess.item, new GH_Integer(1));
            Optional(unit);
            unit.RegisterInputParam(new Param_Integer(), "Columns", "Columns", "How many pictures side by side.", GH_ParamAccess.item, new GH_Integer(2));
            Optional(unit);
            unit.RegisterInputParam(new Param_Colour(), "Colours", "Colours", "The gradient, low to high. Left empty, Model View's.", GH_ParamAccess.list);
            Optional(unit);
            RegisterCaption(unit);
            _results = new ReportResultsMenu(unit, "ReportView_Pushover_Results", true, () => Parent_Component);
            RegisterPicture(unit, "ReportView_Pushover_Picture");
        }

        public override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            _results?.UpdateVisibility();
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            int pictures = 1, columns = 2;
            DA.GetData("Pictures", ref pictures);
            DA.GetData("Columns", ref columns);

            // What every picture has, then the steps through the push for each component ticked.
            var shared = new JObject();
            _results.WriteShared(shared);
            Colours(DA, shared);
            Picture(DA, shared);

            var repeats = PerPicture(_results, shared, figure => new JObject
            {
                ["repeat"] = "step",
                ["of"] = "pushover",
                ["steps"] = pictures <= 1 ? (JToken)"last" : new JObject { ["spread"] = pictures },
                ["columns"] = pictures <= 1 ? 1 : Math.Max(1, columns),
                ["content"] = new JArray { new JObject { ["figure"] = figure } }
            }, out msg, out level, "Step {step} of {steps}");
            DA.SetDataList(0, repeats.Select(json => new GH_ReportPart(new ReportPart { Json = json })));
        }
    }

    /// <summary>
    /// A footfall's response over the model: where people walking would feel it most. Coloured by the
    /// response factor R, its steady-state or transient part, or either's acceleration, and the table
    /// sorted by the same.
    /// </summary>
    internal sealed class FootfallTopic : ReportTopic
    {
        public override string name() => "Report View Footfall";
        public override string display_name() => "Footfall";

        private MenuCheckBoxGroup _quantity, _values, _extremes;

        private string Quantity => _quantity == null ? Alpaca4d.FootfallResponse.Quantities[0] : _quantity.Picked;

        /// <summary>The largest of what is drawn, and its node, as the caption's placeholders name them.</summary>
        protected override string DefaultCaption
        {
            get
            {
                switch (Quantity)
                {
                    case "Steady-state R":
                        return "Steady-state response factor R under footfall, after SCI P354: at most {footfall.maxSteadyFactor:F1}, at node {footfall.maxSteadyFactorNode}";
                    case "Transient R":
                        return "Transient response factor R under footfall, after SCI P354: at most {footfall.maxTransientFactor:F1}, at node {footfall.maxTransientFactorNode}";
                    case "Steady-state acceleration":
                        return "Steady-state weighted rms acceleration under footfall, after SCI P354: at most {footfall.maxSteadyAcceleration:F1} mm/s², at node {footfall.maxSteadyAccelerationNode}";
                    case "Transient acceleration":
                        return "Transient weighted rms acceleration under footfall, after SCI P354: at most {footfall.maxTransientAcceleration:F1} mm/s², at node {footfall.maxTransientAccelerationNode}";
                    default:
                        return "Response factor R under footfall, after SCI P354: at most {footfall.maxFactor:F1}, at node {footfall.maxNode}";
                }
            }
        }

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "A footfall's response over the model, from Footfall Analysis.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Report_View__Alpaca4d_;
            mngr.RegisterUnit(unit);
            unit.RegisterInputParam(new Param_Colour(), "Colours", "Colours",
                "The gradient, from no response to the most: the Colors component's, for one. Left empty, Model View's.", GH_ParamAccess.list);
            Optional(unit);
            unit.RegisterInputParam(new Param_Interval(), "Range", "Range",
                "The values the gradient spans, of what the model is coloured by. For R, 0 to a limit of P354 Table 5.3 - 8 for a general office, " +
                "32 for a stair in light use - so that its top colour is the limit; for an acceleration, in mm/s², 5 times that. Left empty, 0 to the largest.",
                GH_ParamAccess.item);
            Optional(unit);
            RegisterCaption(unit);

            // What the model is coloured by, and in the same frame the table, sorted by it.
            var panel = Panel(unit, "ReportView_Footfall_Results", "Results", "What the picture draws of the footfall, and its table");
            Heading(panel, "Footfall response");
            var box = Box(panel, "ReportView_Footfall_Box");
            Caption(box, "Coloured by");
            _quantity = Choice(box, "ReportView_Footfall_Quantity", Alpaca4d.FootfallResponse.Quantities, 0, 1);
            Table(box, "ReportView_Footfall", "Table", "footfall.response");
            _values = Check(box, "ReportView_Footfall_Values", "Show values", false);
            _extremes = Check(box, "ReportView_Footfall_Extremes", "Max and min", true);
            RegisterPicture(unit, "ReportView_Footfall_Picture");
        }

        /// <summary>The table sorted by what the model is coloured by, that column first.</summary>
        protected override void Amend(JObject table) => table["by"] = Quantity;

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;
            Interval range = Interval.Unset;
            bool values = _values != null && _values[0];
            DA.GetData("Range", ref range);

            var figure = new JObject
            {
                ["model"] = "footfall", ["footfall"] = true, ["quantity"] = Quantity, ["result"] = "Displacement", ["component"] = "Magnitude",
                ["deformed"] = false, ["values"] = values, ["extremes"] = _extremes == null || _extremes[0]
            };
            if (range.IsValid) figure["range"] = new JArray(range.T0, range.T1);
            Colours(DA, figure);
            Picture(DA, figure);
            Hand(DA, new JObject { ["figure"] = figure }, "footfall");
        }
    }
}
