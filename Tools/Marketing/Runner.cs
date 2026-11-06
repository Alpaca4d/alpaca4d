// #! csharp
// Loads the Shots.dll run.sh built and starts the scenes; run.sh fills in the names in capitals.
using System;
using System.IO;
using System.Reflection;

try
{
    var shots = Assembly.LoadFrom("__DLL__").GetType("Shots");
    shots.GetMethod("Run").Invoke(null, new object[] { "__SCENES__", "__OUT__", "__REPO__", "__LICENSE__", "__QUIT__" });
}
catch (Exception ex)
{
    File.WriteAllText("__OUT__/done.txt", "failed: could not start: " + ex);
}
