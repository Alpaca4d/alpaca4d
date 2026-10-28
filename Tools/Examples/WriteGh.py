# Writes a Grasshopper definition built by an example script straight to a .gh file, without Rhino.
#
# Run by GhHost.exe, in IronPython on mono (make.sh sets it up): ARGS is the script's folder, its
# module name, the .gh to write, and the component libraries the script uses. The script's
# build(doc) makes the real Grasshopper and Alpaca4d objects, wires and places them, as it does on
# the canvas; this file hands it the objects by GUID and a document to collect them in, then saves
# them as Grasshopper saves a document. Grasshopper's own document cannot be made outside Rhino,
# so the archive is written here, chunk by chunk, in the layout Grasshopper reads.

import clr
import sys
import System
from System import Activator, Guid
from System.Drawing import Color, Point
from System.Reflection import Assembly, ReflectionTypeLoadException
from Grasshopper.Kernel import IGH_DocumentObject, GH_AssemblyInfo
from GH_IO.Serialization import GH_Archive

script_dir, module_name, out_path = ARGS[0], ARGS[1], ARGS[2]
library_paths = list(ARGS)[3:]


def types_of(assembly):
    try:
        return assembly.GetTypes()
    except ReflectionTypeLoadException as e:
        return [t for t in e.Types if t is not None]


# Every object the libraries hold, by GUID, and the plug-in each library belongs to. Making each
# one is the only way to read its GUID; the few that need Rhino's native code to be made are
# skipped, and an example that used one would say so below.
DOCUMENT_OBJECT = clr.GetClrType(IGH_DocumentObject)
ASSEMBLY_INFO = clr.GetClrType(GH_AssemblyInfo)
object_types = {}
plugin_of = {}
plugins = []
for path in library_paths:
    assembly = Assembly.LoadFrom(path)
    clr.AddReference(assembly)
    info = None
    for t in types_of(assembly):
        try:
            if t.IsAbstract or t.GetConstructor(System.Type.EmptyTypes) is None:
                continue
            if ASSEMBLY_INFO.IsAssignableFrom(t):
                info = Activator.CreateInstance(t)
            elif DOCUMENT_OBJECT.IsAssignableFrom(t):
                obj = Activator.CreateInstance(t)
                object_types[str(obj.ComponentGuid).lower()] = t
        except Exception:
            pass
    # Grasshopper's own component libraries need no entry: a file lists the plug-ins it needs.
    if info is not None and info.Name and "McNeel" not in (info.AuthorName or ""):
        plugins.append(info)
        plugin_of[assembly.FullName] = info


def make_object(guid):
    t = object_types.get(guid.lower())
    if t is None:
        raise Exception("No object with GUID {0} in {1}.".format(guid, ", ".join(library_paths)))
    return Activator.CreateInstance(t)


class Document(object):
    """What build(doc) needs of a Grasshopper document: somewhere to put its objects."""

    def __init__(self):
        self.Objects = []

    def AddObject(self, obj, update):
        self.Objects.append(obj)
        return True


sys.path.append(script_dir)
example = __import__(module_name)
example.make_object = make_object
doc = Document()
example.build(doc)


def implementation(obj):
    return getattr(obj, "__implementation__", obj)


objects = [implementation(o) for o in doc.Objects]

# Laid out where each object now is: a script may move an object without laying it out again, and
# its size and place are read below. Groups take theirs from their members, in a document.
for obj in objects:
    if obj.GetType().Name != "GH_Group":
        obj.Attributes.ExpireLayout()
        obj.Attributes.PerformLayout()


def derives_from(t, name):
    while t is not None:
        if t.Name == name:
            return True
        t = t.BaseType
    return False


# Sizes that can be trusted here. Alpaca4d's components with menus (the extendable and the
# switcher ones) widen themselves to their menus' text, measured with Windows Forms, which mono can
# measure far too wide; Rhino measures them properly when it opens the file, and only their place
# is saved.
def sized(obj):
    attributes = obj.Attributes.GetType()
    return obj.GetType().Name != "GH_Group" and obj.Attributes.Bounds.Width > 0 and \
        not derives_from(attributes, "GH_ExtendableComponentAttributes") and \
        not derives_from(attributes, "GH_SwitcherComponentAttributes")

# Objects that cover one another: worth a look before the file is opened. Groups and the panels
# inside them are left out, as a group is meant to cover what it holds.
placed = [o for o in objects if sized(o)]
for i in range(len(placed)):
    for j in range(i + 1, len(placed)):
        a, b = placed[i].Attributes.Bounds, placed[j].Attributes.Bounds
        if a.IntersectsWith(b):
            print("overlap: {0} at ({2:.0f}, {3:.0f}) to ({4:.0f}, {5:.0f}) and {1} at ({6:.0f}, {7:.0f}) to ({8:.0f}, {9:.0f})".format(
                placed[i].NickName or placed[i].Name, placed[j].NickName or placed[j].Name,
                a.Left, a.Top, a.Right, a.Bottom, b.Left, b.Top, b.Right, b.Bottom))

# Open on the whole definition: the canvas shows a point at (x * zoom + target).
boxes = [o.Attributes.Bounds for o in objects if sized(o)]
left = min(b.Left for b in boxes)
top = min(b.Top for b in boxes)
width = max(b.Right for b in boxes) - left
height = max(b.Bottom for b in boxes) - top
zoom = min(1400.0 / width, 800.0 / height, 1.0)
target = Point(int(40 - left * zoom), int(40 - top * zoom))

archive = GH_Archive()
definition = archive.CreateTopLevelNode("Definition")
definition.SetVersion("plugin_version", 1, 0, 7)

header = definition.CreateChunk("DocumentHeader")
header.SetGuid("DocumentID", Guid.NewGuid())
header.SetString("Preview", "Shaded")
header.SetInt32("PreviewMeshType", 1)
header.SetDrawingColor("PreviewNormal", Color.FromArgb(100, 150, 0, 0))
header.SetDrawingColor("PreviewSelected", Color.FromArgb(100, 0, 150, 0))

properties = definition.CreateChunk("DefinitionProperties")
properties.SetDate("Date", System.DateTime.Now)
properties.SetString("Description", "")
properties.SetBoolean("KeepOpen", False)
properties.SetString("Name", System.IO.Path.GetFileName(out_path))
properties.CreateChunk("Revisions").SetInt32("RevisionCount", 0)
projection = properties.CreateChunk("Projection")
projection.SetDrawingPoint("Target", target)
projection.SetSingle("Zoom", System.Single(zoom))
properties.CreateChunk("Views").SetInt32("ViewCount", 0)

definition.CreateChunk("RcpLayout").SetInt32("GroupCount", 0)

libraries = definition.CreateChunk("GHALibraries")
libraries.SetInt32("Count", len(plugins))
for i, info in enumerate(plugins):
    library = libraries.CreateChunk("Library", i)
    library.SetString("Author", info.AuthorName or "")
    library.SetGuid("Id", info.Id)
    library.SetString("Name", info.Name)
    library.SetString("Version", info.Version or "")

def is_switcher(obj):
    t = obj.GetType()
    while t is not None:
        if t.Name == "GH_SwitcherComponent":
            return True
        t = t.BaseType
    return False


# Alpaca4d's switcher components (ForceBeamColumn, Load Pattern) set their unit's parameters aside
# to save, and write to Rhino's command line when one still has a parent - which here is not
# there to write to. With the parent cleared, the component sets it back itself as it saves.
for obj in objects:
    if is_switcher(obj):
        for param in list(obj.Params.Input) + list(obj.Params.Output):
            if param.Attributes is not None:
                param.Attributes.Parent = None

chunks = definition.CreateChunk("DefinitionObjects")
chunks.SetInt32("ObjectCount", len(objects))
for i, obj in enumerate(objects):
    chunk = chunks.CreateChunk("Object", i)
    chunk.SetGuid("GUID", obj.ComponentGuid)
    plugin = plugin_of.get(obj.GetType().Assembly.FullName)
    if plugin is not None:
        chunk.SetGuid("Lib", plugin.Id)
    chunk.SetString("Name", obj.Name)
    try:
        written = obj.Write(chunk.CreateChunk("Container"))
    except Exception as e:
        raise Exception("{0} ({1}) could not be written: {2}".format(obj.NickName, obj.Name, e))
    if not written:
        raise Exception("{0} ({1}) could not be written.".format(obj.NickName, obj.Name))

if not archive.WriteToFile(out_path, True, False):
    raise Exception("Could not write " + out_path)
print("{0}: {1} objects, opening at {2:.0%} zoom".format(out_path, len(objects), zoom))
