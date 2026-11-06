# Explorer

    ./run.sh

The Explorer components run a design study and save it for the Alpaca4d Explorer, the web page
at <https://alpaca4d.github.io/explorer/>. What they write is the contract between the two, so
that is what is checked here.

## What is checked here

The three parts that do not need Rhino, from `Alpaca4d.Core/Explorer`:

* **StudyFile** - data.csv. The columns (`in:`, `out:`, `img` or `img:View`, `threeD`), CSV
  quoting of commas and quotes, numbers written the invariant way, no byte-order mark in front
  of the first column. That a design is never written twice, that a file of other columns is
  refused rather than appended to, that a study reopened knows what it holds, and that starting
  over deletes data.csv and the files it names and nothing else - not a file of the user's in
  the same folder, not a file outside it that a row points at.
* **DesignSpace** - which designs a study runs. Every combination with the first input fastest,
  as Colibri numbered them, so a range means the same designs in both. Divisions, ranges split
  between two machines adding up to the whole study once each, and a slider with a hundred
  million steps counted rather than listed.
* **GlbWriter** - the 3D model. Rhino's Z up and model units turned into glTF's Y up and metres,
  colours into linear space, the layout of the binary file; and, when `npm` is on the path, the
  Khronos glTF validator on the result, which has to report no errors. Points, and text dots
  and text as labels in the scene's extras; a model of labels only is a valid file too.

## What is not checked here

Everything that needs Grasshopper running. Check these in Rhino, on Windows and on Mac:

* Wiring a slider into the last input of **Explorer Inputs** adds the next pair of input and
  output; the input and output take the slider's name; the pairs survive a save and reopen, and
  inserting or removing one with the zoomable UI keeps them paired.
* **Fly** solves every design once, the count on the component matches the rows in data.csv,
  and each picture shows its own design - change a slider that moves the geometry a lot and
  compare the first and last pictures. Same in Shaded, Rendered and Arctic.
* **Fly** again, and *Esc*, stop after the current design; flying again runs only what is
  missing and says how many it skipped.
* A value list is stepped without starting extra solutions, and data.csv holds its item names.
  A panel with *Multiline Data* steps through its lines, and its **Val** output follows.
* *Test fly* runs three designs. **Fly** set from a Boolean Toggle starts once per rising edge.
* **Explorer Record** with **Write** on and a slider moved by hand records that design once.
  Right-click ▸ *Start a new study…* empties the study and leaves other files in the folder.
* **Model** with an AlpacaModel writes a .glb that opens in the explorer's 3D view, in the
  elements' colours; a coloured result mesh keeps its colours.
* Points, a point cloud with colours, text dots and text in **Model** come out as dots and as
  labels in the 3D view, in their colours; the label button hides and shows them.
* A text dot picked from Rhino keeps its object colour, or its layer's when it is by layer; a
  colour in **Colours** takes its place. The same for a referenced curve, Brep or mesh without
  vertex colours.
* **Explorer Image** with two viewports writes two pictures per design and an `img:` column
  each; a named view is restored into the active viewport and the view is put back after.
* Without a license **Explorer Inputs** is red with the license message and its Fly button refuses,
  and **Explorer Record** refuses once Write is on; adding a license from the License window
  runs both again without touching anything upstream.

The page itself was checked in Chromium against the sample study, a study written by
`StudyFile` and `GlbWriter`, and a Design Explorer study.
