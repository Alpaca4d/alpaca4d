// #! csharp
// Runs GrasshopperCheck.Run inside a running Rhino 8. grasshopper.sh fills in __WORK__ and __LICENSE__
// and starts this with rhinocode; it writes what the checks printed to result.txt.
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

try
{
    // Loads Grasshopper, in case nobody has opened it in this Rhino yet, and has it load its own
    // plugins now: they load on the component server's first use, and with this build already in Rhino
    // the installed Alpaca4d would bind to this build's Core and its Section Library fail to load.
    Rhino.RhinoApp.GetPlugInObject("Grasshopper");
    int proxies = Grasshopper.Instances.ComponentServer.ObjectProxies.Count;

    // This build and everything of ours it needs in one load context, beside the installed Alpaca4d.
    // See TrussAndSpringSupport/RhinoRun.cs for why it is one context made through reflection.
    var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
    var context = Activator.CreateInstance(contextType, new object[] { "BucklingCheck " + Path.GetFileName(work), false });
    var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");

    string[] fromRhino = { "RhinoCommon", "Grasshopper", "GH_IO", "Eto" };
    var loaded = Directory.GetFiles(work)
        .Where(f => f.EndsWith(".dll") || f.EndsWith(".gha"))
        .Where(f => { var n = Path.GetFileNameWithoutExtension(f); return !fromRhino.Contains(n) && !n.StartsWith("System.") && !n.StartsWith("Microsoft."); })
        .Select(f => (Assembly)loadFrom.Invoke(context, new object[] { f }))
        .ToList();

    var check = loaded.Single(a => a.GetName().Name == "GrasshopperCheck").GetType("GrasshopperCheck");
    File.WriteAllText(outPath, (string)check.GetMethod("Run").Invoke(null, new object[] { work, "__LICENSE__" }));
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
