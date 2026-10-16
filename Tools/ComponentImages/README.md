# Component images

    ./run.sh

Draws every Alpaca4d component the way [NodePen](https://github.com/nodepen/nodes) draws
Grasshopper components and puts each image on the component's page of the docs, under its title
line. The inputs, outputs, menus, icons and messages are read from the built plugin, so the
images show what the plugin really has. Run it again whenever components change.

## What it needs

* A Release build of `Alpaca4d.Gh`.
* mono with libgdiplus (`brew install mono`), to load the plugin outside Rhino.
* Grasshopper and RhinoCommon 7.18 from the NuGet cache, which the build has already restored.
* The Visual Basic runtime Grasshopper needs and Homebrew's mono lacks. Rhino 7 for Mac ships it,
  and `run.sh` finds it there.
* python3, node and npm.
* The network: Playwright's Chromium is downloaded on the first run, and NodePen's fonts come
  from Google Fonts on every run.

`run.sh` writes into `../../../alpaca4d.github.io/doc`, the website repository next to this one.
Point it elsewhere with `DOCS=/path/to/doc`, and at other builds with `GHA`, `GH`, `RHINO` and
`VB`; the script names each one it cannot find.

## What it does

1. **Export.cs** loads the plugin with Grasshopper's own types and writes every component shown
   in the toolbar to `components.json`, with its 24 px icon as a PNG. That covers the name,
   nickname, tab, inputs and outputs, message, the fold-out menus of extendable components, and
   the button of those that draw one. Objects linked by grips of their own rather than wires,
   such as Non-Linear Optimisation, come after the components: their grips are read from the
   `...Grip` points their attributes declare, and what they show over themselves from `Status`.
2. **nodepen.py** turns each component into an SVG: `<slug>.svg` as it is placed, plus
   `<slug>-open.svg` with every menu open for a component whose menus start closed.
3. **render.mjs** renders them in Chromium, at twice their size and on a transparent
   background, into `doc/.gitbook/assets/components/`. It sizes the message tabs there, because
   only the browser knows how wide their text is, and stops if the fonts did not load.
4. **docs.py** puts each image under its component's title line, ``` `Name (Alpaca4d)` — **Alpaca4d ▸ Tab** ```,
   as a GitBook `<figure>`, and the open image under it. A page documenting several components
   gets each image under its own title. Images from an earlier run are replaced, not added to.
   Components with no page are listed at the end.

The images in the assets folder are deleted and drawn again each time, so a component that has
been removed or renamed leaves nothing behind.

## The look

NodePen's generic node, from its `src/constants.ts` and `generic-node` components: a white body
with a 2 px dark outline and lip, ports 40 px apart, labels in Overpass Mono. On top of that,
for the docs:

* a larger icon, 40 px in a 52 px column, rather than NodePen's 24 px;
* the component's message, nickname and "Alpaca4d", as a dark tab under the body, where
  Grasshopper hangs it;
* the fold-out menus of extendable components inside the body, with their controls on a pale
  panel when open;
* a button the component draws, such as Explorer Inputs' **Fly**, as a dark bar;
* an object linked by grips rather than wires, such as Non-Linear Optimisation, with its grips
  where it has them, on the left and underneath, each named inside the body, and what it shows
  over itself above the body, as Grasshopper draws it.

`python3 nodepen.py components.json out_dir --names` writes the nickname in the label column
instead of the icon, as NodePen does by default.
