// #! csharp
// Runs GrasshopperCheck.Run inside a running Rhino 8. grasshopper.sh
// fills in __WORK__ and starts this with rhinocode; it writes what the checks printed to result.txt.
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

try
{
    // Loads Grasshopper, in case nobody has opened it in this Rhino yet, and has it load its own
    // plugins now: were this build loaded first, the installed Alpaca4d would bind to its Core.
    Rhino.RhinoApp.GetPlugInObject("Grasshopper");
    _ = Grasshopper.Instances.ComponentServer.ObjectProxies.Count;

    // This build and everything of ours it needs go into one load context, beside the installed
    // Alpaca4d. Grasshopper, RhinoCommon, Eto and the framework come from Rhino.
    var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
    var context = Activator.CreateInstance(contextType, new object[] { "GmshCheck " + Path.GetFileName(work), false });
    var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");

    // The DLLs before the .gha: Grasshopper looks into a .gha as soon as it is loaded, and a .gha
    // loaded before its Alpaca4d.Core binds for good to the installed one, which lacks what is new.
    string[] fromRhino = { "RhinoCommon", "Grasshopper", "GH_IO", "Eto" };
    var loaded = Directory.GetFiles(work)
        .Where(f => f.EndsWith(".dll") || f.EndsWith(".gha"))
        .Where(f => { var n = Path.GetFileNameWithoutExtension(f); return !fromRhino.Contains(n) && !n.StartsWith("System.") && !n.StartsWith("Microsoft."); })
        .OrderBy(f => f.EndsWith(".gha"))
        .ThenBy(f => Path.GetFileName(f) == "Alpaca4d.Core.dll" ? 0 : 1)
        .Select(f => (Assembly)loadFrom.Invoke(context, new object[] { f }))
        .ToList();

    var check = loaded.Single(a => a.GetName().Name == "GrasshopperCheck").GetType("GrasshopperCheck");
    File.WriteAllText(outPath, (string)check.GetMethod("Run").Invoke(null, new object[] { work }));
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
