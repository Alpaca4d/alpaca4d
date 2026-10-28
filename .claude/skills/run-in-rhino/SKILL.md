---
name: run-in-rhino
description: Run Alpaca4d code, or real Grasshopper components, inside a live Rhino 8 on this Mac with rhinocode. Use it for checks and benchmarks that need RhinoCommon's native geometry (Assemble, meshes, curves, Breps) or a solved Grasshopper definition, which tests under mono cannot do.
---

# Run code inside a live Rhino 8

`rhinocode` is `/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode`.

## Copy a committed pattern

- `Alpaca4d.Test/TrussAndSpringSupport/rhino.sh` with `RhinoRun.cs` runs Core code: it builds and
  assembles models, solves them with OpenSees and reads them back.
- `Alpaca4d.Test/TrussAndSpringSupport/grasshopper.sh`, with `GrasshopperRun.cs` and
  `GrasshopperChecks.cs`, runs the real components headless. It builds a `GH_Document` in code,
  calls `CreateAttributes` and switches units with `SwitchUnit` by reflection. It fills inputs
  with `PersistentData`, wires them with `AddSource` and sets `Document.FilePath`, so that
  Run Analysis has a folder. It then solves with `NewSolution(true, Silent)` and reads
  `VolatileData` and `RuntimeMessages`.
- `Alpaca4d.Test/BrepMesher/run.sh` is the same idea, smaller.

## Rhino

- **Start one:** `open -na "Rhino 8" --args -nosplash -runscript="_StartScriptServer"`, then wait
  until `rhinocode list` shows `rhinocode_remotepipe_<pid>`. Read only the lines starting with
  `rhinocode`.
- **If a Rhino is already open:** a second one started beside it runs no scripts and quits by
  itself. Use the open one if `rhinocode list` shows it; otherwise ask the user to run
  `StartScriptServer` in it.
- **Other sessions drive Rhino too.** Target your own with
  `rhinocode --rhino rhinocode_remotepipe_<pid> script ...`.
- **Close only a Rhino you started:** `rhinocode --rhino <id> command "_-Exit"`.
- **While Rhino runs, never build into the live Grasshopper folder.** Build with
  `-p:GrasshopperLibrariesPath=<scratch>`.

## Scripts

- The first line is `// #! csharp`.
- rhinocode returns before the script has run and prints nothing back. Have the script write a
  `result.txt` and poll for it from the shell.
- A script that fails to compile writes nothing.
  - Its error may be in the newest `~/.rhinocode/logs/rhinocode_<pid>`.
  - If the log is empty, compile the script in a scratch net8 project referencing Rhino 8's
    RhinoCommon.dll. WinForms errors there are false alarms.
- Keep script paths free of spaces: the Mac launcher splits them, and the script silently does
  not run.

## Loading Alpaca4d's DLLs

- **Copy each build to a fresh folder** (`mktemp -d`). Never overwrite a DLL Rhino has loaded:
  that gave BadImageFormatException.
- **Load every DLL of the run into one AssemblyLoadContext, made by reflection.**
  - Leave out RhinoCommon, Grasshopper, GH_IO, Eto, `System.*` and `Microsoft.*`.
  - It is needed because Grasshopper may already have loaded the installed Alpaca4d.Core, and a
    reference by name would bind to that one.
  - Reflection, because the script compiler does not reference System.Runtime.Loader.
  - `Assembly.LoadFile` does not do it either: it gives each file a context of its own.
  - `RhinoRun.cs` shows how. Call your entry point by reflection.
- **To time old against new Core,** load each build into its own context from its own folder, call
  the same code in each, and alternate the runs.
- **Settings and licence.**
  - The OpenSees path comes from `settings.json`: `grasshopper.sh` writes one into the work
    folder, and the user's own is in `~/Library/Application Support/Alpaca4d`.
  - Licensed components (View Results, Model View, Interaction Domain and others) need a licence
    in that Rhino. A refusal opens the License window there.

## Pictures

For viewport or canvas pictures, see the gh-canvas-capture skill.
- A DisplayConduit enabled from a script stays enabled, so restart Rhino before each capture run.
- `screencapture` is not permitted from the shell.
