using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Ready-made reports, put on the canvas as components rather than handed to the Report as JSON:
    /// a Panel for each text, typed into as any panel is, wired into a Report Text in its style;
    /// Report Views on their topic with their tables ticked; a Merge for each chapter, in order. A
    /// template holds no model - the views and tables draw the Report's own, by topic - so it works
    /// with whatever models the Report is given, and once placed it is the user's to change: a text
    /// rewritten, a picture more, a chapter left out.
    ///
    /// The Templates bar under the Report lists them (<see cref="ReportComponentAttributes"/>); its
    /// right-click menu too.
    /// </summary>
    internal static class ReportTemplates
    {
        private static readonly Guid MergeId = new Guid("3CADDDEF-1E2B-4C09-9390-0E8F78F7609F");

        // ------------------------------------------------------------------ what a template holds

        /// <summary>One part of a chapter, in order: a text, a picture with its tables, a page break.</summary>
        internal abstract class Part { }

        internal sealed class TextPart : Part
        {
            public string Style;
            public string[] Lines;
        }

        internal sealed class ViewPart : Part
        {
            public string Topic;
            public string Caption;
            public Action<ReportView> Set;
        }

        internal sealed class BreakPart : Part { }

        /// <summary>A chapter: a group of its own on the canvas, its parts through a Merge.</summary>
        internal sealed class Chapter
        {
            public string Name;
            public string Tooltip;
            internal readonly List<Part> Parts = new List<Part>();

            public Chapter(string name, string tooltip)
            {
                Name = name;
                Tooltip = tooltip;
            }

            public Chapter Text(string style, params string[] lines)
            {
                Parts.Add(new TextPart { Style = style, Lines = lines });
                return this;
            }

            public Chapter View(string topic, string caption = null, Action<ReportView> set = null)
            {
                Parts.Add(new ViewPart { Topic = topic, Caption = caption, Set = set });
                return this;
            }

            public Chapter Break()
            {
                Parts.Add(new BreakPart());
                return this;
            }
        }

        // ------------------------------------------------------------------ the chapters

        internal static Chapter TitlePage() => new Chapter("Title page", "The report's title and subtitle, and what it is for. A page break after it.")
            .Text("Title", "Structural analysis report", "The project - the structure")
            .Text("Paragraph",
                  "This report sets out the model of the structure and the results of its analyses, made in Grasshopper with Alpaca4d and OpenSees.")
            .Break();

        internal static Chapter TheModel() => TheModel(true);

        /// <param name="sections">The table of the beams' sections: only for a model with beams, which it lists.</param>
        internal static Chapter TheModel(bool sections) => new Chapter("The model",
                sections ? "The model: its size and mass, a picture coloured by section with its supports and loads, and the tables of its sections and supports."
                         : "The model: its size and mass, a picture coloured by section with its supports and loads, and the table of its supports.")
            .Text("Heading", "The model")
            .Text("Paragraph",
                  "Describe the structure here: its form, its members and how it is held.",
                  "The model has {model.nodes} nodes and {model.elements} elements, and weighs {model.mass:N0} kg.")
            .View(ModelTopic.Name, "The model, coloured by section, with its supports and loads", view =>
            {
                var topic = view.Topic<ModelTopic>();
                topic.TickTable("model.summary");
                if (sections) topic.TickTable("model.sections");
                topic.TickTable("model.supports");
            });

        internal static Chapter StaticAnalysis() => new Chapter("Static analysis", "The static analysis: the displacement, then the reactions at the supports, each with its table.")
            .Text("Heading", "Static analysis")
            .Text("Paragraph", "Under the loads of the analysis, the structure moves {static.maxDisplacement:F1} mm at most, at node {static.maxNode}.")
            .Text("Subheading", "Displacement")
            .View(StaticTopic.Name, null, view => Results(view, menu =>
            {
                Show(menu, ResultFamily.Displacement, 0);          // with its table
            }))
            .Text("Subheading", "Reactions")
            .View(StaticTopic.Name, null, view => Results(view, menu =>
            {
                Hide(menu, ResultFamily.Displacement);
                Show(menu, ResultFamily.Reaction, 0);
                menu.Deformed = false;
            }));

        internal static Chapter MemberForces() => new Chapter("Member forces", "Under Static analysis: the axial force and the bending moment in the beams and columns, a picture each, and their table.")
            .Text("Subheading", "Member forces")
            .Text("Paragraph", "The forces in the beams and columns under the loads of the analysis.")
            .View(StaticTopic.Name, null, view => Results(view, menu =>
            {
                Hide(menu, ResultFamily.Displacement);
                Show(menu, ResultFamily.BeamForce, 0, 5);          // N and Mz, a picture each
                menu.Deformed = false;
            }));

        internal static Chapter ShellForces() => new Chapter("Shell forces", "Under Static analysis: the bending moments mxx and myy in the shells, a picture each, and their table.")
            .Text("Subheading", "Shell forces")
            .Text("Paragraph", "The bending moments in the shells under the loads of the analysis, per metre of width, in the shells' own axes.")
            .View(StaticTopic.Name, null, view => Results(view, menu =>
            {
                Hide(menu, ResultFamily.Displacement);
                Show(menu, ResultFamily.ShellForce, 3, 4);         // mxx and myy
                menu.Deformed = false;
            }));

        internal static Chapter SolidStresses() => new Chapter("Solid stresses", "Under Static analysis: the von Mises stress in the solid elements, and its table.")
            .Text("Subheading", "Stresses in the solids")
            .Text("Paragraph", "The von Mises stress in the solid elements under the loads of the analysis.")
            .View(StaticTopic.Name, null, view => Results(view, menu =>
            {
                Hide(menu, ResultFamily.Displacement);
                Show(menu, ResultFamily.BrickStress, 6);           // von Mises
                menu.Deformed = false;
            }));

        internal static Chapter NaturalVibration() => new Chapter("Natural vibration", "The first six modes, two to a row, with the tables of their frequencies and participating mass.")
            .Text("Heading", "Natural vibration")
            .Text("Paragraph", "The first mode vibrates at {vibration.frequency[1]:F2} Hz. The frequencies of the modes, and the mass each sets moving, follow the pictures.")
            .View("Report View Natural Vibration", null, view =>
            {
                var topic = view.Topic<VibrationTopic>();
                topic.TickTable("vibration.modes");
                topic.TickTable("vibration.massRatios");
            });

        internal static Chapter Stability() => new Chapter("Stability", "The first four buckling modes, two to a row, with the table of their load factors.")
            .Text("Heading", "Stability")
            .Text("Paragraph", "The structure buckles first at {buckling.factor[1]:F2} times its loads.")
            .View("Report View Stability", null, view => view.Topic<StabilityTopic>().TickTable("buckling.factors"));

        internal static Chapter Footfall() => new Chapter("Footfall", "The response factor of the floors to people walking on them, and its table.")
            .Text("Heading", "Footfall")
            .Text("Paragraph",
                  "People walking on the floors, after SCI P354, make them respond up to R = {footfall.maxFactor:F1}, at node {footfall.maxNode}.")
            .View("Report View Footfall", null, view => view.Topic<FootfallTopic>().TickTable("footfall.response"));

        internal static Chapter Pushover() => new Chapter("Pushover", "Four pictures through the push, two to a row, with the plastic hinges.")
            .Text("Heading", "Pushover")
            .Text("Paragraph", "The structure pushed sideways until it yields: its deformed shape at four steps through the push, and where its members have yielded.")
            .View("Report View Pushover", null, view => Input(view, "Pictures", new GH_Integer(4)));

        internal static Chapter Conclusions() => new Chapter("Conclusions", "A heading and a paragraph to write the conclusions in.")
            .Text("Heading", "Conclusions")
            .Text("Paragraph", "Write the conclusions here: what the results mean for the design, and what is still to be checked.");

        /// <summary>The chapters offered one at a time, in the order a report has them; null for a line between them.</summary>
        internal static readonly Func<Chapter>[] Chapters =
        {
            TitlePage, TheModel, null, StaticAnalysis, MemberForces, ShellForces, SolidStresses, null,
            NaturalVibration, Stability, Footfall, Pushover, null, Conclusions
        };

        /// <summary>Whole reports, by their name in the menu: the chapters each is made of.</summary>
        internal static readonly (string Name, string Tooltip, Func<Chapter>[] Chapters)[] Reports =
        {
            ("Static", "A report of a static analysis: title page, the model, the displacement and reactions, the member forces, conclusions.",
                new Func<Chapter>[] { TitlePage, TheModel, StaticAnalysis, MemberForces, Conclusions }),
            ("Static and dynamic", "Title page, the model, the static analysis with the member forces, natural vibration, stability, conclusions.",
                new Func<Chapter>[] { TitlePage, TheModel, StaticAnalysis, MemberForces, NaturalVibration, Stability, Conclusions }),
        };

        /// <summary>
        /// The report the models given to the Report ask for: the model, a chapter for each analysis
        /// among them - the static one with the forces of the elements it has - and conclusions.
        /// </summary>
        internal static List<Chapter> Fitted(IEnumerable<Alpaca4d.Model> models)
        {
            var all = (models ?? Enumerable.Empty<Alpaca4d.Model>()).Where(m => m != null).ToList();
            bool Has(string kind) => all.Any(m => Alpaca4d.Report.ModelSet.KindOf(m) == kind);
            var built = all.FirstOrDefault();

            var chapters = new List<Chapter> { TitlePage(), TheModel(HasBeams(all)) };
            if (Has("static"))
            {
                chapters.Add(StaticAnalysis());
                if (built?.Beams.Count > 0) chapters.Add(MemberForces());
                if (built?.Shells.Count > 0) chapters.Add(ShellForces());
                if (built?.Bricks.Count > 0) chapters.Add(SolidStresses());
            }
            if (Has("vibration")) chapters.Add(NaturalVibration());
            if (Has("buckling")) chapters.Add(Stability());
            if (Has("footfall")) chapters.Add(Footfall());
            if (Has("pushover")) chapters.Add(Pushover());
            chapters.Add(Conclusions());
            return chapters;
        }

        /// <summary>Whether any of the models has beams: the table of sections lists theirs, and says so of a model with none.</summary>
        private static bool HasBeams(IEnumerable<Alpaca4d.Model> models) => models.Any(m => m?.Beams?.Count > 0);

        // ------------------------------------------------------------------ setting the views

        private static void Results(ReportView view, Action<ReportResultsMenu> set)
        {
            var menu = view.Topic<StaticTopic>().Results;
            set(menu);
            menu.UpdateVisibility();
        }

        /// <summary>A result ticked, and these of its components alone - a picture each - each with its table.</summary>
        private static void Show(ReportResultsMenu menu, ResultFamily family, params int[] components)
        {
            var result = menu.Results.First(r => r.Family == family);
            result.Tick[0] = true;
            result.Table[0] = true;
            result.Pick(components[0]);
            foreach (int c in components.Skip(1))
                result.Components[c] = true;
        }

        private static void Hide(ReportResultsMenu menu, ResultFamily family) =>
            menu.Results.First(r => r.Family == family).Tick[0] = false;

        /// <summary>An input of the topic shown, typed in.</summary>
        private static void Input(GH_Component component, string name, IGH_Goo value)
        {
            if (!(component.Params.Input.FirstOrDefault(p => p.Name == name) is IGH_Param param)) return;
            switch (param)
            {
                case Param_Integer integer when value is GH_Integer i: integer.PersistentData.Clear(); integer.PersistentData.Append(i); break;
                case Param_String text when value is GH_String s: text.PersistentData.Clear(); text.PersistentData.Append(s); break;
            }
        }

        // ------------------------------------------------------------------ the menu

        /// <summary>
        /// The templates, as menu items: a report for the models given, whole reports, and chapters one
        /// at a time. A click puts it on the canvas beside the Report and wires it into Content.
        /// </summary>
        internal static void Fill(ToolStripItemCollection items, ReportComponent report)
        {
            Header(items, "Whole report");
            var models = report.LastModels;
            // The model's chapter with the beams' sections only when the models given have beams; any, before there are models.
            bool beams = models.Count == 0 || HasBeams(models);
            Func<Chapter> Fit(Func<Chapter> make) => make.Method.Name == nameof(TheModel) ? () => TheModel(beams) : make;
            var fitted = Item(items, "For the models given",
                models.Count > 0
                    ? "The model, then a chapter for each analysis wired into AlpacaModel, and the forces of the elements it has: " +
                      string.Join(", ", Fitted(models).Select(c => c.Name)) + "."
                    : "Wire the models into AlpacaModel first: the chapters follow the analyses given.",
                () => Insert(report, "Report", Fitted(report.LastModels)));
            fitted.Enabled = models.Count > 0;
            foreach (var (name, tooltip, chapters) in Reports)
            {
                var make = chapters.Select(Fit).ToList();
                Item(items, name, tooltip, () => Insert(report, "Report", make.Select(c => c()).ToList()));
            }

            items.Add(new ToolStripSeparator());
            Header(items, "Chapter");
            foreach (var make in Chapters.Where(c => c != null).Select(Fit))
            {
                var chapter = make();
                bool section = chapter.Parts.FirstOrDefault() is TextPart text && text.Style == "Subheading";
                Item(items, (section ? "    " : "") + chapter.Name, chapter.Tooltip, () => Insert(report, chapter.Name, new List<Chapter> { make() }));
            }
        }

        private static void Header(ToolStripItemCollection items, string text)
        {
            var header = new ToolStripMenuItem(text) { Enabled = false };
            header.Font = new Font(header.Font, FontStyle.Bold);
            items.Add(header);
        }

        private static ToolStripMenuItem Item(ToolStripItemCollection items, string text, string tooltip, Action click)
        {
            var item = new ToolStripMenuItem(text) { ToolTipText = tooltip };
            item.Click += (s, e) => click();
            items.Add(item);
            return item;
        }

        // ------------------------------------------------------------------ putting it on the canvas

        private const float PanelWidth = 220f, Gap = 24f, ColumnGap = 80f;

        /// <summary>
        /// The chapters as components, a column each, left of the Report where the canvas is free; wired
        /// into its Content - after what is there already: into the Merge that feeds it, as its last
        /// inputs, when one does. One undo takes it all away.
        /// </summary>
        internal static void Insert(ReportComponent report, string name, List<Chapter> chapters)
        {
            var doc = report.OnPingDocument();
            if (doc == null || chapters.Count == 0) return;

            var added = new List<IGH_DocumentObject>();
            var outputs = new List<IGH_Param>();
            var groups = new List<(string Name, List<IGH_DocumentObject> Members)>();
            float x = 0f;
            foreach (var chapter in chapters)
            {
                var members = new List<IGH_DocumentObject>();
                var merge = Lay(chapter, x, members);
                added.AddRange(members);
                outputs.Add(merge.Params.Output[0]);
                groups.Add((chapter.Name, members));
                x = Extent(members).Right + ColumnGap;
            }

            // A report of more than one chapter: the chapters in order through one Merge more.
            IGH_Param content;
            if (outputs.Count > 1)
            {
                var all = NewMerge("Report content", outputs.Count);
                Move(all, x, 0f);
                for (int i = 0; i < outputs.Count; i++)
                    all.Params.Input[i].AddSource(outputs[i]);
                added.Add(all);
                content = all.Params.Output[0];
            }
            else content = outputs[0];

            // Left of the Report, its top level with the Report's, moved down past what is in the way.
            var box = Extent(added);
            var at = report.Attributes.Bounds;
            var shift = new SizeF(at.Left - ColumnGap - box.Right, at.Top - box.Top);
            // A group's frame is taken too: a chapter inside one would look a part of it.
            var taken = doc.Objects.Where(o => o.Attributes != null).Select(o => o.Attributes.Bounds).ToList();
            for (int tries = 0; tries < 200; tries++)
            {
                var placed = new RectangleF(box.X + shift.Width, box.Y + shift.Height, box.Width, box.Height);
                placed.Inflate(Gap, Gap);
                var hit = taken.Where(r => r.IntersectsWith(placed)).ToList();
                if (hit.Count == 0) break;
                shift.Height = hit.Max(r => r.Bottom) + 2 * Gap - box.Top;
            }
            foreach (var obj in added)
                Move(obj, obj.Attributes.Bounds.X + shift.Width, obj.Attributes.Bounds.Y + shift.Height);

            // Into the document, as one undo.
            var record = new GH_UndoRecord("Report template: " + name);
            var target = report.Params.Input[ReportComponent.ContentInput];
            var feeding = target.Sources.Count == 1 && target.Sources[0].Attributes?.GetTopLevel?.DocObject is IGH_Component m &&
                          m.ComponentGuid == MergeId ? m : null;
            if (feeding != null) record.AddAction(new GH_GenericObjectAction(feeding));
            else record.AddAction(new GH_WireAction(target));

            doc.DeselectAll();
            foreach (var obj in added)
            {
                doc.AddObject(obj, false);
                obj.Attributes.Selected = true;
            }
            foreach (var (chapter, members) in groups)
            {
                var group = new GH_Group { NickName = chapter, Colour = Color.FromArgb(60, 120, 120, 120) };
                group.CreateAttributes();
                foreach (var member in members) group.AddObject(member.InstanceGuid);
                doc.AddObject(group, false);
                added.Add(group);
            }
            foreach (var obj in added)
                record.AddAction(new GH_AddObjectAction(obj));

            if (feeding != null)
            {
                // Its empty last input, which a Merge keeps for the next wire, or a new one after it.
                var param = feeding.Params.Input.LastOrDefault();
                if (param == null || param.SourceCount > 0)
                {
                    var variable = (IGH_VariableParameterComponent)feeding;
                    int index = feeding.Params.Input.Count;
                    param = variable.CreateParameter(GH_ParameterSide.Input, index);
                    feeding.Params.RegisterInputParam(param, index);
                    variable.VariableParameterMaintenance();
                    feeding.Params.OnParametersChanged();
                }
                param.AddSource(content);
                feeding.Attributes.ExpireLayout();
            }
            else target.AddSource(content);

            doc.UndoServer.PushUndoRecord(record);
            report.ExpireSolution(true);
            Grasshopper.Instances.ActiveCanvas?.Refresh();
        }

        /// <summary>A chapter's components, in a column from <paramref name="left"/>: each text's panel left of its Report Text, all through a Merge at the right.</summary>
        private static IGH_Component Lay(Chapter chapter, float left, List<IGH_DocumentObject> into)
        {
            float y = 0f, column = left + PanelWidth + Gap;
            var parts = new List<IGH_Param>();
            foreach (var part in chapter.Parts)
            {
                switch (part)
                {
                    case TextPart text:
                    {
                        var component = new ReportText();
                        component.CreateAttributes();
                        component.SetStyle(text.Style);
                        var panel = Panel(text.Style, text.Lines);
                        Layout(component);
                        Move(panel, left, y);
                        Move(component, column, y + Math.Max(0f, (panel.Attributes.Bounds.Height - component.Attributes.Bounds.Height) / 2f));
                        component.Params.Input[0].AddSource(panel);
                        into.Add(panel);
                        into.Add(component);
                        parts.Add(component.Params.Output[0]);
                        y += Math.Max(panel.Attributes.Bounds.Height, component.Attributes.Bounds.Height) + Gap;
                        break;
                    }
                    case ViewPart view:
                    {
                        var component = new ReportView();
                        component.CreateAttributes();
                        component.SwitchUnit(view.Topic, false, false);
                        // Its topic's menus, as placing it on the canvas puts them on: a switch to the topic it
                        // starts on, Static analysis, is no switch, and left it with no menus, its Picture
                        // inputs at the canvas' origin and the component stretched out to them.
                        ((GH_SwitcherComponentAttributes)component.Attributes).OnSwitchUnit();
                        view.Set?.Invoke(component);
                        if (view.Caption != null) Input(component, "Caption", new GH_String(view.Caption));
                        Layout(component);
                        Move(component, column, y);
                        into.Add(component);
                        parts.Add(component.Params.Output[0]);
                        y += component.Attributes.Bounds.Height + Gap;
                        break;
                    }
                    case BreakPart _:
                    {
                        var component = new ReportPageBreak();
                        component.CreateAttributes();
                        Layout(component);
                        Move(component, column, y);
                        into.Add(component);
                        parts.Add(component.Params.Output[0]);
                        y += component.Attributes.Bounds.Height + Gap;
                        break;
                    }
                }
            }

            var merge = NewMerge(chapter.Name, parts.Count);
            for (int i = 0; i < parts.Count; i++)
                merge.Params.Input[i].AddSource(parts[i]);
            Layout(merge);
            Move(merge, Extent(into).Right + 2 * Gap, 0f);
            into.Add(merge);
            return merge;
        }

        /// <summary>A text to type over, a line a paragraph - for a Title, the title then the subtitle - as tall as it wraps.</summary>
        private static GH_Panel Panel(string style, string[] lines)
        {
            var panel = new GH_Panel { NickName = style };
            panel.CreateAttributes();
            panel.Properties.Multiline = false;
            panel.SetUserText(string.Join(Environment.NewLine, lines));
            int rows = lines.Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / 34.0)));
            panel.Attributes.Bounds = new RectangleF(0f, 0f, PanelWidth, Math.Max(30f, 14f + 13f * rows));
            return panel;
        }

        private static IGH_Component NewMerge(string name, int inputs)
        {
            var merge = (IGH_Component)Grasshopper.Instances.ComponentServer.EmitObject(MergeId);
            merge.CreateAttributes();
            merge.NickName = name;
            var variable = (IGH_VariableParameterComponent)merge;
            while (merge.Params.Input.Count < inputs)
            {
                int index = merge.Params.Input.Count;
                merge.Params.RegisterInputParam(variable.CreateParameter(GH_ParameterSide.Input, index), index);
            }
            variable.VariableParameterMaintenance();
            merge.Params.OnParametersChanged();
            Layout(merge);
            return merge;
        }

        private static void Layout(IGH_DocumentObject obj)
        {
            obj.Attributes.ExpireLayout();
            obj.Attributes.PerformLayout();
        }

        /// <summary>An object moved so that its bounds start at (<paramref name="left"/>, <paramref name="top"/>).</summary>
        private static void Move(IGH_DocumentObject obj, float left, float top)
        {
            var a = obj.Attributes;
            if (!(obj is GH_Panel)) Layout(obj);
            var b = a.Bounds;
            a.Pivot = new PointF(a.Pivot.X + left - b.X, a.Pivot.Y + top - b.Y);
            if (obj is GH_Panel) a.Bounds = new RectangleF(left, top, b.Width, b.Height);
            Layout(obj);
        }

        private static RectangleF Extent(IEnumerable<IGH_DocumentObject> objects)
        {
            var boxes = objects.Select(o => o.Attributes.Bounds).ToList();
            if (boxes.Count == 0) return RectangleF.Empty;
            float l = boxes.Min(b => b.Left), t = boxes.Min(b => b.Top), r = boxes.Max(b => b.Right), btm = boxes.Max(b => b.Bottom);
            return new RectangleF(l, t, r - l, btm - t);
        }
    }
}
