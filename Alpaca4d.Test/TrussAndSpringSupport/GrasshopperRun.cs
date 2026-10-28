// #! csharp
// Runs GrasshopperCheck.Run inside a running Rhino 8, which must have Grasshopper loaded. grasshopper.sh
// fills in __WORK__ and starts this with rhinocode; it writes what the checks printed to result.txt.
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

try
{
    // This build of the plugin and everything of ours it needs go into one load context, beside the
    // installed Alpaca4d Grasshopper may already have loaded. Grasshopper, RhinoCommon, Eto and the
    // framework come from Rhino. See RhinoRun.cs for why it is one context made through reflection.
    var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
    var context = Activator.CreateInstance(contextType, new object[] { "GrasshopperCheck " + Path.GetFileName(work), false });
    var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");

    string[] fromRhino = { "RhinoCommon", "Grasshopper", "GH_IO", "Eto" };
    var loaded = Directory.GetFiles(work)
        .Where(f => f.EndsWith(".dll") || f.EndsWith(".gha"))
        .Where(f => { var n = Path.GetFileNameWithoutExtension(f); return !fromRhino.Contains(n) && !n.StartsWith("System.") && !n.StartsWith("Microsoft."); })
        .Select(f => (Assembly)loadFrom.Invoke(context, new object[] { f }))
        .ToList();

    var check = loaded.Single(a => a.GetName().Name == "GrasshopperCheck").GetType("GrasshopperCheck");
    File.WriteAllText(outPath, (string)check.GetMethod("Run").Invoke(null, new object[] { work }));
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
