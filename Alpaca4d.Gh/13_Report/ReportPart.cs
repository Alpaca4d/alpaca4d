using System;

using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// One part of a report, as the report components hand it to the Report: a block of the
    /// template - a text, a table, a figure, a page break - or the page itself, as the JSON a template
    /// would hold for it, and the model it is of, when it is of one. The JSON is the same as one
    /// written by hand, so a part shown in a panel can be copied into a template, and a template's
    /// text can stand where a part goes.
    /// </summary>
    public sealed class ReportPart
    {
        /// <summary>What the part is, as a template writes it.</summary>
        public JObject Json = new JObject();

        /// <summary>The model a figure or a table is of; null for a text, a page break, the page.</summary>
        public Alpaca4d.Model Model;

        /// <summary>The page - paper, margins, title block - rather than a block of the content.</summary>
        public bool IsPage;

        public ReportPart Copy() => new ReportPart { Json = (JObject)Json.DeepClone(), Model = Model, IsPage = IsPage };

        public override string ToString() => Json.ToString(Formatting.Indented);
    }

    public sealed class GH_ReportPart : GH_Goo<ReportPart>
    {
        public GH_ReportPart() { }

        public GH_ReportPart(ReportPart part) : base(part) { }

        public override bool IsValid => Value != null;

        public override string TypeName => "Report part";

        public override string TypeDescription => "A part of a report: a block of its content, or its page, as JSON.";

        public override IGH_Goo Duplicate() => new GH_ReportPart(Value?.Copy());

        public override string ToString() => Value?.ToString() ?? "No report part";

        public override bool CastTo<Q>(ref Q target)
        {
            if (Value != null && typeof(Q).IsAssignableFrom(typeof(GH_String)))
            {
                target = (Q)(object)new GH_String(Value.ToString());
                return true;
            }
            return false;
        }

        public override bool CastFrom(object source)
        {
            switch (source)
            {
                case ReportPart part:
                    Value = part;
                    return true;
                case GH_String text:
                    return CastFrom(text.Value);
                case string json:
                    try
                    {
                        Value = new ReportPart { Json = JObject.Parse(json) };
                        return true;
                    }
                    catch (JsonException)
                    {
                        return false;
                    }
                default:
                    return false;
            }
        }
    }
}
