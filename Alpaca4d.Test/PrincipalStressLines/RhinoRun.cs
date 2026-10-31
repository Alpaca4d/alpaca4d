// #! csharp
// Runs StressLinesCheck.Run and SolidStressLinesCheck.Run inside a running Rhino 8, which must have
// Grasshopper loaded. run.sh fills in the placeholders and starts this with rhinocode; it writes what
// the checks printed to result.txt in the work folder.
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
    // framework come from Rhino. The script compiler does not reference System.Runtime.Loader, hence
    // reflection; Assembly.LoadFile would not do, as it gives each file a context of its own.
    var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
    var context = Activator.CreateInstance(contextType, new object[] { "StressLinesCheck " + Path.GetFileName(work), false });
    var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");

    string[] fromRhino = { "RhinoCommon", "Grasshopper", "GH_IO", "Eto" };
    var loaded = Directory.GetFiles(work)
        .Where(f => f.EndsWith(".dll") || f.EndsWith(".gha"))
        .Where(f => { var n = Path.GetFileNameWithoutExtension(f); return !fromRhino.Contains(n) && !n.StartsWith("System.") && !n.StartsWith("Microsoft."); })
        .Select(f => (Assembly)loadFrom.Invoke(context, new object[] { f }))
        .ToList();

    // The license tests run under, put in place first, so that models of any size solve.
    var license = loaded.Single(a => a.GetName().Name == "Alpaca4d.Core").GetType("Alpaca4d.License.License");
    if (File.Exists("__LICENSE__"))
        license.GetMethod("Install").Invoke(null, new object[] { "__LICENSE__" });

    var checks = loaded.Single(a => a.GetName().Name == "StressLinesCheck");
    var parts = "__PARTS__".Split(',');
    var text = "";
    if (parts.Contains("2d"))
        text += (string)checks.GetType("StressLinesCheck").GetMethod("Run").Invoke(null, new object[] { work, "__OPENSEES__" });
    if (parts.Contains("3d"))
        text += (string)checks.GetType("SolidStressLinesCheck").GetMethod("Run").Invoke(null, new object[] { work, "__OPENSEES_SOLIDS__" });
    File.WriteAllText(outPath, text);
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
