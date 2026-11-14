// Analysis Settings' cores for Mumps: chosen from the right-click menu, not an input.
//
// The real component, built in mono as the ColourBy checks build theirs. Its inputs have to be the nine
// files have always had - a tenth would shift every one of them in a saved definition. The menu offers
// every core and all of them, ticks the one chosen, and a pick from it has to reach the Settings the
// component puts out, as -numThreads in the deck, and come back from a saved file.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Windows.Forms;

using Alpaca4d;
using Alpaca4d.Gh;
using GH_IO.Serialization;
using Grasshopper.Kernel;

class AnalysisSettingsTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static int Cores(AnalysisSettings component) =>
        (int)typeof(AnalysisSettings).GetField("cores", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component);

    static ToolStripMenuItem CoresMenu(AnalysisSettings component)
    {
        var menu = new ToolStripDropDown();
        typeof(GH_Component).GetMethod("AppendAdditionalComponentMenuItems", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(component, new object[] { menu });
        return menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text.StartsWith("Cores for Mumps"));
    }

    static AnalysisSettings Make()
    {
        var component = new AnalysisSettings();
        component.CreateAttributes();
        return component;
    }

    static AnalysisSettings SaveAndOpen(AnalysisSettings component)
    {
        var archive = new GH_Archive();
        archive.CreateNewRoot(false);
        component.Write(archive.GetRootNode.CreateChunk("Container"));
        var opened = new GH_Archive();
        opened.Deserialize_Binary(archive.Serialize_Binary());
        var copy = Make();
        copy.Read(opened.GetRootNode.FindChunk("Container"));
        return copy;
    }

    /// <summary>
    /// Grasshopper's data access with nothing connected: every input left to its default, and what is
    /// set on the output kept. A document cannot be made here - it wants Rhino's native library - so
    /// the component's SolveInstance is called with this instead.
    /// </summary>
    class Unconnected : RealProxy
    {
        public object Output;
        public Unconnected() : base(typeof(IGH_DataAccess)) { }

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            object result = null;
            if (call.MethodName == "SetData")
                Output = call.Args[1];
            var returns = ((MethodInfo)call.MethodBase).ReturnType;
            if (returns == typeof(bool)) result = call.MethodName == "SetData";
            else if (returns.IsValueType && returns != typeof(void)) result = Activator.CreateInstance(returns);
            return new ReturnMessage(result, call.Args, call.ArgCount, call.LogicalCallContext, call);
        }
    }

    /// <summary>What the component puts out with nothing connected.</summary>
    static Settings Solve(AnalysisSettings component)
    {
        var access = new Unconnected();
        typeof(AnalysisSettings).GetMethod("SolveInstance", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(component, new[] { access.GetTransparentProxy() });
        return access.Output as Settings;
    }

    static int Main()
    {
        int available = Environment.ProcessorCount;
        Console.WriteLine($"Analysis Settings, cores for Mumps ({available} cores here)\n");

        var component = Make();
        string inputs = string.Join(", ", component.Params.Input.Select(p => p.Name));
        Console.WriteLine("  inputs: " + inputs);
        Check(component.Params.Input.Count == 9 && !component.Params.Input.Any(p => p.Name == "Cores"),
              "nine inputs, as files have always had them - Cores is not one");
        Check(Cores(component) == 0, "a new component takes every core");

        var menu = CoresMenu(component);
        Check(menu != null && menu.Text == $"Cores for Mumps: all ({available})", "right-click menu: " + menu?.Text);
        var items = menu?.DropDownItems.OfType<ToolStripMenuItem>().ToList();
        Check(items != null && items.Count == available + 1, $"All and 1 to {available} on offer ({items?.Count} items)");
        Check(items != null && items[0].Checked && items.Skip(1).All(item => !item.Checked), "All is ticked, nothing else");

        var settings = Solve(component);
        Check(settings != null && settings.SystemEquation.Type == SystemEquation.SystemType.Auto, "System is Auto unless set");
        Check(settings != null && settings.WriteTcl().Contains("-numThreads 0"), "every core: -numThreads 0 in the deck");

        int pick = Math.Min(4, available);
        items?.First(item => item.Text == pick.ToString()).PerformClick();
        Check(Cores(component) == pick, $"picking {pick} from the menu sets it");
        Check(CoresMenu(component)?.Text == $"Cores for Mumps: {pick}", "and the menu says so");
        Check(CoresMenu(component).DropDownItems.OfType<ToolStripMenuItem>().Count(item => item.Checked) == 1 &&
              CoresMenu(component).DropDownItems.OfType<ToolStripMenuItem>().First(item => item.Checked).Text == pick.ToString(),
              $"only {pick} is ticked");

        var opened = SaveAndOpen(component);
        Check(Cores(opened) == pick, $"{pick} comes back from a saved file");
        settings = Solve(opened);
        Check(settings != null && settings.WriteTcl().Contains($"-numThreads {pick}"), $"-numThreads {pick} in the deck of the opened one");

        Check(Cores(SaveAndOpen(Make())) == 0, "a component saved untouched opens taking every core");

        Console.WriteLine(fails == 0 ? "\nAll passed." : $"\n{fails} failed.");
        return fails == 0 ? 0 : 1;
    }
}
