using System;
using System.IO;
using System.Reflection;

using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

// Runs WriteGh.py in IronPython, with Grasshopper, GH_IO, RhinoCommon and System.Drawing loaded,
// so that an example script can build its definition from the real components outside Rhino.
//
//     mono GhHost.exe WriteGh.py <script folder> <module> <out.gh> <library.gha> ...
class GhHost
{
    // Assemblies Grasshopper refers to that a Mac does not have: the Windows script editor and
    // Eto's Windows platform. IronPython reads every type of an assembly it loads, so they are
    // answered with empty ones; the few types that need them are skipped, and none is used here.
    static readonly string[] WindowsOnly = { "QWhale.", "Eto.Wpf" };

    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            foreach (var prefix in WindowsOnly)
                if (e.Name.StartsWith(prefix))
                    return System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                        new AssemblyName(e.Name), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            return null;
        };
        var engine = Python.CreateEngine();
        foreach (var name in new[] { "Grasshopper.dll", "GH_IO.dll", "RhinoCommon.dll" })
            engine.Runtime.LoadAssembly(Assembly.LoadFrom(Path.GetFullPath(name)));
        engine.Runtime.LoadAssembly(typeof(System.Drawing.Color).Assembly);

        var scope = engine.CreateScope();
        var rest = new string[args.Length - 1];
        Array.Copy(args, 1, rest, 0, rest.Length);
        scope.SetVariable("ARGS", rest);
        try
        {
            engine.ExecuteFile(args[0], scope);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(engine.GetService<ExceptionOperations>().FormatException(e));
            return 1;
        }
    }
}
