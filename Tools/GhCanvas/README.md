# Grasshopper canvas previews

    ./run.sh                    # every example of the website
    ./run.sh path/to/a.gh ...   # some definitions

Reads the examples' Grasshopper definitions so that the website can draw each one on its example
page, interactively: `examples/<slug>/<slug>.gh` in the website repository next to this one gets a
`<slug>-definition.json` beside it, which `assets/site/gh-canvas.js` there draws and
`docs-site/scripts/examples.mjs` puts under the example's picture. Commit the JSON with the `.gh`,
and run this again whenever an example's `.gh` changes.

## What it needs

* mono with libgdiplus (`brew install mono`);
* a Release build of `Alpaca4d.Gh`, for the components' icons and the names they have now, which
  the docs pages are found by;
* Grasshopper and RhinoCommon 7.18 from the NuGet cache, which the build has already restored;
* Rhino 7 for Mac, for the icons of Grasshopper's own components. Without it they are drawn with
  their names.

## What it reads

**Read.cs** reads each `.gh` as an archive, with GH_IO, so no component has to load:

* every object's place and size, and those of its inputs and outputs, with the wires into them;
* the inputs of an Alpaca4d switcher's active unit, which it keeps apart and with no place of
  their own, and the unit's name;
* the inputs and outputs of a component that can have more or fewer, such as Expression, kept in
  `ParameterData`;
* sliders (range and value), toggles, value lists (the selected item), panels (their text, when
  nothing feeds them), groups (colour and members) and scribbles.

A definition written by `Tools/Examples/make.sh`, outside Rhino, has some components measured a
little small and a menu's inputs left at the origin. Each component's pivot is read too, and the
page rebuilds the component around it.
