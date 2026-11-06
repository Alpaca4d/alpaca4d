using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using GH_IO.Serialization;
using Grasshopper.Kernel;

// Reads Grasshopper definitions and writes, beside each <name>.gh, <name>-definition.json: what
// the website needs to draw the definition as it sits on the canvas. Every object's place and
// size, its inputs and outputs and the wires into them; sliders, toggles, value lists, panels,
// groups and scribbles with what they show; each object's 24 px icon and, for the components of
// the assemblies given, their name as the plugin has it now.
//
//     mono Read.exe assembly.gha ... -- definition.gh ...
//
// The definition is read from its archive, so the components need not load: a definition with
// components of plug-ins not given still draws, with their names instead of icons.
class Read
{
    // Each object's icon and present name, by its component GUID, from the assemblies given.
    static readonly Dictionary<Guid, string> Icons = new Dictionary<Guid, string>();
    static readonly Dictionary<Guid, string> Names = new Dictionary<Guid, string>();

    static void Main(string[] args)
    {
        int split = Array.IndexOf(args, "--");
        foreach (var path in args.Take(split)) LoadIcons(path);
        foreach (var path in args.Skip(split + 1))
        {
            var archive = new GH_Archive();
            if (!archive.ReadFromFile(path)) { Console.Error.WriteLine("cannot read " + path); continue; }
            string json = Definition(archive.GetRootNode);
            string output = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-definition.json");
            File.WriteAllText(output, json, new UTF8Encoding(false));
            Console.WriteLine(Path.GetFileName(output));
        }
    }

    static void LoadIcons(string path)
    {
        Assembly assembly;
        try { assembly = Assembly.LoadFrom(path); } catch (Exception) { return; }
        Type[] types;
        try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        bool ours = Path.GetExtension(path) == ".gha" && Path.GetFileName(path).StartsWith("Alpaca4d");
        foreach (var t in types)
        {
            // Under mono some of Grasshopper's own types do not load; they have no icon here.
            try
            {
                if (t.IsAbstract || !typeof(GH_DocumentObject).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                var o = (GH_DocumentObject)Activator.CreateInstance(t);
                if (ours) Names[o.ComponentGuid] = o.Name;
                if (Icons.ContainsKey(o.ComponentGuid) || o.Icon_24x24 == null) continue;
                using (var stream = new MemoryStream())
                {
                    o.Icon_24x24.Save(stream, ImageFormat.Png);
                    Icons[o.ComponentGuid] = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception) { }
        }
    }

    static string Definition(GH_Chunk root)
    {
        var definition = (GH_Chunk)root.FindChunk("Definition");
        var objects = (GH_Chunk)definition.FindChunk("DefinitionObjects");
        var entries = new List<string>();
        var used = new HashSet<Guid>();
        foreach (GH_Chunk o in objects.Chunks)
        {
            if (o.Name != "Object") continue;
            string entry = Object(o, used);
            if (entry != null) entries.Add(entry);
        }
        var icons = used.Where(Icons.ContainsKey).Select(g => Q(g.ToString()) + ":" + Q(Icons[g]));
        return "{\"objects\":[\n" + string.Join(",\n", entries) + "\n],\n\"icons\":{" + string.Join(",\n", icons) + "}}\n";
    }

    static string Object(GH_Chunk o, HashSet<Guid> used)
    {
        Guid type = o.GetGuid("GUID");
        var c = (GH_Chunk)o.FindChunk("Container");
        if (c == null) return null;
        string typeName = o.GetString("Name");
        var a = (GH_Chunk)c.FindChunk("Attributes");
        RectangleF box = a != null && a.ItemExists("Bounds") ? a.GetDrawingRectangleF("Bounds") : RectangleF.Empty;
        var f = new List<string>
        {
            "\"id\":" + Q(Str(c, "InstanceGuid")),
            "\"type\":" + Q(type.ToString()),
            "\"name\":" + Q(Names.ContainsKey(type) ? Names[type] : (Str(c, "Name") ?? typeName)),
            "\"nick\":" + Q(Str(c, "NickName") ?? ""),
            "\"desc\":" + Q(Str(c, "Description") ?? ""),
            "\"box\":" + Box(box),
        };
        used.Add(type);

        switch (typeName)
        {
            case "Group":
                f.Insert(0, "\"kind\":\"group\"");
                f.Add("\"colour\":" + Colour(c, "Colour"));
                f.Add("\"members\":[" + string.Join(",", Guids(c, "ID")) + "]");
                return "{" + string.Join(",", f) + "}";
            case "Scribble":
                f.Insert(0, "\"kind\":\"scribble\"");
                f.Add("\"text\":" + Q(Str(c, "Text") ?? ""));
                f.Add("\"size\":" + (c.ItemExists("Size") ? Num(c.GetSingle("Size")) : "25"));
                return "{" + string.Join(",", f) + "}";
        }

        var inputs = new List<string>();
        var outputs = new List<string>();
        foreach (GH_Chunk p in c.Chunks)
        {
            if (p.Name == "param_input") inputs.Add(Param(p));
            else if (p.Name == "param_output") outputs.Add(Param(p));
        }
        // A component whose inputs and outputs can be added and removed, such as Expression or
        // a Python script, keeps them in ParameterData.
        var data = (GH_Chunk)c.FindChunk("ParameterData");
        if (data != null)
            foreach (GH_Chunk p in data.Chunks)
            {
                if (p.Name == "InputParam") inputs.Add(Param(p));
                else if (p.Name == "OutputParam") outputs.Add(Param(p));
            }
        // A switcher component (Alpaca4d's) keeps its active unit's inputs and outputs apart,
        // without a place on the canvas: they follow the component's own.
        string unit = null;
        var active = (GH_Chunk)c.FindChunk("ActiveUnit");
        var units = (GH_Chunk)c.FindChunk("EvalUnits");
        if (active != null && units != null)
        {
            unit = active.GetString("unitname");
            int count = 0;
            foreach (GH_Chunk u in units.Chunks)
            {
                count++;
                if (u.GetString("name") != unit) continue;
                var ps = (GH_Chunk)u.FindChunk("params");
                if (ps == null) continue;
                foreach (GH_Chunk side in ps.Chunks)
                    foreach (GH_Chunk p in side.Chunks)
                        (side.Name == "input" ? inputs : outputs).Add(Param(p));
            }
            if (count < 2) unit = null;
        }

        // (ChunkExists finds no chunk with an index, such as param_input#0: count what was read.)
        if (inputs.Count > 0 || outputs.Count > 0 || units != null)
        {
            f.Insert(0, "\"kind\":\"component\"");
            // Its pivot, the middle of its icon: a definition written outside Rhino can have a box
            // stretched by menus it could not lay out, but not a pivot.
            if (a != null && a.ItemExists("Pivot")) { var pv = a.GetDrawingPointF("Pivot"); f.Add("\"pivot\":[" + Num(pv.X) + "," + Num(pv.Y) + "]"); }
            f.Add("\"inputs\":[" + string.Join(",", inputs) + "]");
            f.Add("\"outputs\":[" + string.Join(",", outputs) + "]");
            if (unit != null) f.Add("\"unit\":" + Q(unit.Replace("(Alpaca4d)", "").Trim()));
            return "{" + string.Join(",", f) + "}";
        }

        // A parameter on its own: a slider, a panel, a toggle, a list, or a plain one.
        f.Add("\"src\":[" + string.Join(",", Guids(c, "Source")) + "]");
        var slider = (GH_Chunk)c.FindChunk("Slider");
        if (slider != null)
        {
            f.Insert(0, "\"kind\":\"slider\"");
            f.Add("\"min\":" + Value(slider.GetDouble("Min")) + ",\"max\":" + Value(slider.GetDouble("Max")) + ",\"value\":" + Value(slider.GetDouble("Value")) + ",\"digits\":" + (slider.ItemExists("Digits") ? slider.GetInt32("Digits") : 0));
        }
        else if (c.ItemExists("ToggleValue"))
        {
            f.Insert(0, "\"kind\":\"toggle\"");
            f.Add("\"value\":" + (c.GetBoolean("ToggleValue") ? "true" : "false"));
        }
        else if (c.ItemExists("ListCount"))
        {
            f.Insert(0, "\"kind\":\"list\"");
            var items = new List<string>();
            foreach (GH_Chunk item in c.Chunks)
                if (item.Name == "ListItem")
                    items.Add("{\"name\":" + Q(Str(item, "Name") ?? "") + ",\"selected\":" + (item.ItemExists("Selected") && item.GetBoolean("Selected") ? "true" : "false") + "}");
            f.Add("\"items\":[" + string.Join(",", items) + "]");
        }
        else if (typeName == "Panel")
        {
            f.Insert(0, "\"kind\":\"panel\"");
            f.Add("\"text\":" + Q(Str(c, "UserText") ?? ""));
            var props = (GH_Chunk)c.FindChunk("PanelProperties");
            f.Add("\"colour\":" + (props != null ? Colour(props, "Colour") : "null"));
        }
        else f.Insert(0, "\"kind\":\"param\"");
        return "{" + string.Join(",", f) + "}";
    }

    // One input or output: its id, names, where its grip is when the file says, and its sources.
    static string Param(GH_Chunk p)
    {
        var a = (GH_Chunk)p.FindChunk("Attributes");
        string at = "";
        if (a != null && a.ItemExists("Bounds"))
        {
            var b = a.GetDrawingRectangleF("Bounds");
            at = ",\"box\":" + Box(b);
        }
        return "{\"id\":" + Q(Str(p, "InstanceGuid")) + ",\"name\":" + Q(Str(p, "Name") ?? "") + ",\"nick\":" + Q(Str(p, "NickName") ?? "") +
               at + ",\"src\":[" + string.Join(",", Guids(p, "Source")) + "]}";
    }

    static IEnumerable<string> Guids(GH_Chunk c, string name)
    {
        var list = new List<string>();
        foreach (var item in c.Items)
            if (item.Name == name && item.Index >= 0) list.Add(Q(item._guid.ToString()));
        return list;
    }

    static string Str(GH_Chunk c, string name)
    {
        if (!c.ItemExists(name)) return null;
        var item = c.FindItem(name);
        return item.Type == GH_IO.Types.GH_Types.gh_guid ? item._guid.ToString() : item._string;
    }

    static string Colour(GH_Chunk c, string name)
    {
        if (!c.ItemExists(name)) return "null";
        var k = c.GetDrawingColor(name);
        return "[" + k.A + "," + k.R + "," + k.G + "," + k.B + "]";
    }

    static string Box(RectangleF b) => "[" + Num(b.X) + "," + Num(b.Y) + "," + Num(b.Width) + "," + Num(b.Height) + "]";

    static string Num(double v) => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
    // A slider's numbers keep their digits: a tube's 0.0603 m is not 0.06.
    static string Value(double v) => Math.Round(v, 9).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static string Q(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char ch in s ?? "")
        {
            if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
            else if (ch == '\n') b.Append("\\n");
            else if (ch == '\r') continue;
            else if (ch < 32) b.Append($"\\u{(int)ch:x4}");
            else b.Append(ch);
        }
        return b.Append('"').ToString();
    }
}
