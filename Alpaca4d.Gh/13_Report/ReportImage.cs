using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A picture from outside Rhino for the report: an image file by its path, or a bitmap from the
    /// canvas. What does not live in Grasshopper - a site photo, a drawing from the architect, a scan -
    /// still goes in the report.
    /// </summary>
    public class ReportImage : GH_Component
    {
        public ReportImage()
          : base("Report Image (Alpaca4d)", "ReportImage",
            "A picture for the report that does not come from the model: a photo, a drawing, a scan. Give it a file path - PNG, JPG, " +
            "or any picture Rhino reads; a relative one starts from the folder of the .gh file - or a bitmap, from Grasshopper's " +
            "image components. A bitmap is saved as a PNG in the Alpaca4d folder of the temp folder.\n" +
            "Each picture is a figure, numbered by the report as they come, with the Caption under it. A file that is missing or " +
            "cannot be read shows in the report as a bracketed line saying why, and as a message here.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "Image",
                "The picture: the path of an image file - text, or a File Path - or a bitmap. A list gives a figure each.", GH_ParamAccess.list);
            pManager.AddTextParameter("Caption", "Caption",
                "The text under the picture, after its figure number. One for each picture, or one for all.", GH_ParamAccess.list);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Width", "Width",
                "How much of the text's width the picture takes, 0 to 1. Default 1.", GH_ParamAccess.item, 1.0);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "Image", "The picture, for the Content of the Report.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<IGH_Goo>();
            if (!DA.GetDataList(0, items)) return;
            var captions = new List<string>();
            DA.GetDataList(1, captions);
            double width = 1.0;
            DA.GetData(2, ref width);
            if (!(width > 0.0 && width <= 1.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width has to be more than 0 and at most 1.");
                return;
            }
            if (captions.Count > 1 && captions.Count != items.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{captions.Count} captions for {items.Count} pictures: give one for each, or one for all.");

            var parts = new List<GH_ReportPart>();
            for (int i = 0; i < items.Count; i++)
            {
                string path = PathOf(items[i], out string problem);
                if (problem != null)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Picture {i + 1}: {problem}");
                string caption = captions.Count == 0 ? "" : captions[Math.Min(i, captions.Count - 1)] ?? "";
                var image = new JObject { ["path"] = path ?? "", ["width"] = width, ["caption"] = caption };
                parts.Add(new GH_ReportPart(new ReportPart { Json = new JObject { ["image"] = image } }));
            }
            DA.SetDataList(0, parts);
        }

        /// <summary>The file a picture is in: the text or File Path as it is, or a bitmap saved as a PNG; null, with a problem, when it is neither.</summary>
        private static string PathOf(IGH_Goo item, out string problem)
        {
            problem = null;
            object value = item is IGH_Goo goo ? goo.ScriptVariable() : item;
            switch (value)
            {
                case string text when !string.IsNullOrWhiteSpace(text):
                    return text.Trim().Trim('"');
                case Bitmap bitmap:
                    return Save(bitmap, out problem);
                case Image image:
                    return Save(new Bitmap(image), out problem);
                case null:
                    problem = "nothing to show.";
                    return null;
                default:
                    problem = $"{value.GetType().Name} is not a path or a bitmap.";
                    return null;
            }
        }

        /// <summary>Saves a bitmap in the temp folder under a name from its pixels, so the same bitmap is written once.</summary>
        private static string Save(Bitmap bitmap, out string problem)
        {
            problem = null;
            try
            {
                using (var memory = new MemoryStream())
                {
                    bitmap.Save(memory, ImageFormat.Png);
                    var bytes = memory.ToArray();
                    string name;
                    using (var sha = SHA1.Create())
                        name = BitConverter.ToString(sha.ComputeHash(bytes), 0, 8).Replace("-", "").ToLowerInvariant();
                    string folder = Path.Combine(Path.GetTempPath(), "Alpaca4d", "ReportImages");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, name + ".png");
                    if (!File.Exists(file))
                        File.WriteAllBytes(file, bytes);
                    return file;
                }
            }
            catch (Exception e)
            {
                problem = "the bitmap could not be saved: " + e.Message;
                return null;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Image__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{5C2E7A14-8D39-4B60-A1F5-3E9B6D8C4A27}");
    }
}
