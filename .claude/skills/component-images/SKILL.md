---
name: component-images
description: Redraw the pictures of the Alpaca4d Grasshopper components in the docs (alpaca4d.github.io/doc/.gitbook/assets/components) after a component's name, inputs, outputs, menus, message or icon changed, or a component was added. Builds the plugin in Release without touching the live Grasshopper folder, runs Tools/ComponentImages/run.sh and checks what it changed.
---

# Redraw the component pictures in the docs

`Tools/ComponentImages/run.sh` reads the components from a Release build and draws every one of
them, NodePen style, into the docs. Its README says what it needs (mono with libgdiplus, Rhino 7
for the Visual Basic runtime, node, the network for fonts).

1. **Build Alpaca4d.Gh in Release**, into a scratch Grasshopper folder so a running Rhino keeps the
   plugin it loaded. `--no-incremental`, because a plain build can reuse stale outputs.

   ```
   dotnet build Alpaca4d.Gh/Alpaca4d.Gh.csproj -c Release --no-incremental \
       -p:GrasshopperLibrariesPath=<scratchpad>/gh-libraries
   ```

2. **Run it**, always naming the docs folder: the default is a path relative to the script, and
   in a worktree under `.claude/worktrees/` it points to the wrong place.

   ```
   DOCS=/Users/mp/Documents/GitHub/alpaca4d.github.io/doc Tools/ComponentImages/run.sh
   ```

3. **Check what changed** with `git -C /Users/mp/Documents/GitHub/alpaca4d.github.io status --short`.
   - The pictures of the components that changed should be there, and pages whose figure moved.
   - run.sh deletes and redraws every picture. If pictures of untouched components show up as
     modified, look at one before keeping it, and tell the user.
   - The end of run.sh's output lists components with no page. A new component there needs a
     page, as `.claude/rules/docs.md` says.

4. **Look at the changed pictures** with Read, to see that they show the change.

5. Leave the docs uncommitted. When asked, commit them in the docs repository, for example
   `🍱 Redraw the Support picture with its new icon`.
