// #! csharp
// Runs TrussCheck.Rhino inside a running Rhino 8. rhino.sh fills in __WORK__ and __OPENSEES__ and starts
// this with rhinocode; it writes what the checks printed to result.txt in the work folder.
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

try
{
    // Every assembly the checks need goes into one load context of its own, so that they find each
    // other there first: Grasshopper may have loaded the installed Alpaca4d.Core into the default
    // context, and a reference by name would bind to that one. Assembly.LoadFile would not do - it
    // gives each file a context of its own. The script compiler does not reference
    // System.Runtime.Loader, hence reflection. RhinoCommon still comes from Rhino.
    var contextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
    var context = Activator.CreateInstance(contextType, new object[] { "TrussCheck " + Path.GetFileName(work), false });
    var loadFrom = contextType.GetMethod("LoadFromAssemblyPath");

    var loaded = Directory.GetFiles(work, "*.dll")
        .Where(dll => Path.GetFileName(dll) != "RhinoCommon.dll")
        .Select(dll => (Assembly)loadFrom.Invoke(context, new object[] { dll }))
        .ToList();

    var check = loaded.Single(assembly => assembly.GetName().Name == "TrussCheck").GetType("TrussCheck");
    var text = (string)check.GetMethod("Rhino").Invoke(null, new object[] { work, "__OPENSEES__" });
    File.WriteAllText(outPath, text);
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
