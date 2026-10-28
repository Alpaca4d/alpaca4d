# FemDesign.Communication.dll

    ./build-net48.sh

FEM-Design's StruXml classes, from StruSoft's FEM-Design API, femdesign-api-2.0
(`src/integration/femdesign/communication`). They hold FEM-Design's StruXml schema, its material
libraries for each national annex and its section library, and they read, write and check
`.struxml` files. `Alpaca4d.FemDesign` builds FEM-Design models with them, and the DLL ships with
Alpaca4d.

## Why it is built here

femdesign-api-2.0 targets net9.0. Rhino 7 runs plugins on .NET Framework 4.8, and so does Rhino 8
unless it is switched to .NET Core, which is why Alpaca4d targets net48. A net9.0 assembly loads in
neither. `build-net48.sh` builds the same sources for net48 instead, without changing
femdesign-api-2.0:

1. It copies `strusoft.interop/struxml` and `strusoft.interop/utils` to a temporary folder. The
   named-pipe connection to a running FEM-Design (`connection/`), the result tables (`results/`) and
   FdScript are left out: they are not needed to write a file, and the connection is Windows-only.
2. It writes a project file of its own: net48, the latest C# version, and the same embedded
   resources, under their real file names so that they are found on a case-sensitive file system
   too.
3. It adds what the sources use from .NET 5 to 9 and .NET Framework lacks: `IsExternalInit`, for
   `init` and `record`; `CallerArgumentExpression`; `KeyValuePair` deconstruction;
   `Dictionary.TryAdd` and `GetValueOrDefault`; LINQ's `CountBy` and parameterless `ToDictionary`.
4. It drops `using System.Reflection.Metadata;` from `partials/loads/LineLoad.cs`, which uses
   nothing from it and does not compile on net48.

The DLL's informational version names the femdesign-api-2.0 commit it was built from:

    femdesign-api-2.0 d04f54d, StruXml only, net48

## Rebuilding

Run `./build-net48.sh` with femdesign-api-2.0 checked out next to this repository, under
`ms-structural`, or point `SRC` at its `src/integration/femdesign/communication`. It needs the
.NET SDK. Then run `Alpaca4d.Test/FemDesignExport/run.sh`, which writes models with the new DLL and
checks them against the schema it carries.
