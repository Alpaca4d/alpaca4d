// #! csharp
// Runs FootfallGrasshopperCheck.Run inside a running Rhino 8, with the Alpaca4d Grasshopper has
// loaded. grasshopper.sh fills in __WORK__ and starts this with rhinocode; it writes what the checks
// printed to result.txt.
using System;
using System.IO;
using System.Reflection;

var work = "__WORK__";
var outPath = Path.Combine(work, "result.txt");

try
{
    // Loads Grasshopper, and with it Alpaca4d, in case nobody has opened it in this Rhino yet.
    Rhino.RhinoApp.GetPlugInObject("Grasshopper");

    var check = Assembly.LoadFile(Path.Combine(work, "FootfallGrasshopperCheck.dll")).GetType("FootfallGrasshopperCheck");
    File.WriteAllText(outPath, (string)check.GetMethod("Run").Invoke(null, new object[] { work }));
}
catch (Exception e)
{
    File.WriteAllText(outPath, "  [FAIL] the script ran: " + e);
}
