// #! csharp
// Runs TemplateCheck inside a running Rhino 8. templates.sh fills in __WORK__ and starts this with
// rhinocode; TemplateCheck writes what it found to result.txt once the report is written.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Rhino;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

// From an idle moment: a Rhino document to draw the pictures in, opened if there is none - an empty one.
EventHandler start = null;
start = (s, e) =>
{
    RhinoApp.Idle -= start;
    try
    {
        RhinoApp.GetPlugInObject("Grasshopper");
        if (RhinoDoc.ActiveDoc == null)
        {
            var empty = Path.Combine(work, "empty.3dm");
            new Rhino.FileIO.File3dm().Write(empty, 7);
            RhinoDoc.Open(empty, out bool _);
        }

        // This build of the plugin and everything of ours it needs, in one load context beside the
        // installed Alpaca4d, as Alpaca4d.Test/TrussAndSpringSupport/GrasshopperRun.cs does.
        var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
        var context = Activator.CreateInstance(contextType, new object[] { "TemplateCheck " + Path.GetFileName(work), false });
        var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");
        string[] fromRhino = { "RhinoCommon", "Grasshopper", "GH_IO", "Eto" };
        var loaded = Directory.GetFiles(work)
            .Where(f => f.EndsWith(".dll") || f.EndsWith(".gha"))
            .Where(f => { var n = Path.GetFileNameWithoutExtension(f); return !fromRhino.Contains(n) && !n.StartsWith("System.") && !n.StartsWith("Microsoft."); })
            .Select(f => (Assembly)loadFrom.Invoke(context, new object[] { f }))
            .ToList();
        var check = loaded.Single(a => a.GetName().Name == "TemplateCheck").GetType("TemplateCheck");
        var done = (string)check.GetMethod("Start").Invoke(null, new object[] { work });
        if (done != null) File.WriteAllText(outPath, done);
    }
    catch (Exception ex)
    {
        File.WriteAllText(outPath, "  [FAIL] the script ran: " + ex);
    }
};
RhinoApp.Idle += start;
