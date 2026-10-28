# Colour by

    ./run.sh

Sections and materials carry a colour, given at the **Colour** input of every component that
makes one - the section components, the Section and Material Libraries, Uniaxial and nD - and
Model View's **Colour by** draws the elements in their element's, section's or material's colour.
`ElementColours`, in the core, decides which.

## What is checked here

* **Sections of one name are one colour.** Two Section Library components both set to IPE200 make
  two objects, and two sections in OpenSees, but one section to the eye. IPE200 and HEA200 are
  told apart, and the colours are picked in the order the model meets the sections.
* **A colour given to one of them colours them all**, and the sections given none still take the
  picked colours from the first.
* **Unnamed sections and materials go by what they are**, the Tcl they write with their own tag
  left out: the elastic steel each Section Library falls back on is one material, not one per
  component.
* **Shells and solids**: a shell goes by its section, or its section's material; a solid, which
  has no section, by its material either way.
* **The picked colours** are thirty different ones before they repeat, and all opaque, as the
  viewport draws them.
* **Old files open.** Grasshopper reads a component's inputs by position, and only as many as the
  component has: a file saved before an input was added has nothing for it, which Grasshopper
  reports as a corrupt archive. Section Library, Material Library and Model View are read from
  `Tools/Examples/SteelCantilever.gh`, saved before the Colour input and Colour by, and must come
  back with their new input or dropdown and no message.
* **Every section and material component has a Colour input, last**, in each material unit of
  Uniaxial and nD too.
* **Each of them opens from a file saved before it had one**, with its other inputs the ones saved
  - so their wires find them - their values kept, and no message. The file is made here by taking
  the input off and saving.
* **And from files people have**: every section and material component in `SteelCantilever.gh`
  and `MixedModelSpeed.gh`, and, when the website repository sits next to this one, in the docs'
  `beam.gh`, `earthquake.gh` and `connection-design.gh`. Uniaxial and nD keep their inputs per
  unit, and read them back only when the file had exactly as many as the unit has now - none at
  all otherwise, which an added input would have made every old file. They now read them by name.
  `earthquake.gh` also has a Uniaxial saved before its units were renamed, under `UniaxialElastic`
  with an input called `v`: it used to open with no unit and no inputs, and now opens as
  `UniaxialElastic (Alpaca4d)` with `v` read into `ν`.
* **Section Library's Gap survives a save.** The input is added by the component when the family
  is 2L, after Grasshopper has read the file - so it used to come back new each time, at 10 mm and
  with its wires gone. It must now be the one saved, with its value, whether the file has a Colour
  input or not.
* **Colour, Colour by and Show Values are kept** through a save, and Model View opens from a file
  saved with its Info output, which it no longer has, without complaint.

The negative control - that the archive does report a missing input, so that "no message" means
something - was run by hand when this was written: Legend reading a Section Library's saved
inputs reports three.

## What is not checked here

Everything that needs Rhino:

* that Model View draws the colours, extruded and as lines, and that switching Colour by redraws
* the load values, where they sit and how they read, and the thicker load arrows
* that a wire from Model View's old Info output goes quietly when a file is opened
* that a Colour wired into a Section Library reaches the sections it gives, one or All
* that wires to Gap come back on a real canvas - checked here only by the input keeping the id
  its wires point at
