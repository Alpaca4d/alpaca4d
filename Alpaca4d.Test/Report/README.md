# Report

    ./run.sh

The Report component writes a PDF from a JSON template. Everything but the pictures is worked out
in Alpaca4d.Core - the template, the tables, the placeholders, the pages - and checked here under
mono, with a stand-in that records which model and step each figure asked for instead of drawing
it. The pictures, which need Rhino's view, were checked by writing the report of
`Tools/Examples/Report/ReportFrame.gh` in a live Rhino 8, from the report components: Report Page, Title
Block, Text, Table, Result Table, View and Page Break, through a Merge.

## What is checked here

* **Wrapping.** Every line fits its width, no word is lost, a line break is kept, and a word too
  long for a line is broken.
* **Tables over pages.** A long table carries its header onto each page, marked "continued", and
  no row goes below the margin. Its last two rows and its note go over together, and a short table
  goes over whole.
* **Figures.** A figure most of the way onto what is left of a page is made smaller to fit, and
  stays inside the margin.
* **The template.** Placeholders are filled in - the frequency of a mode, the number of modes, a
  repeat's mode - and a repeat over three modes, two to a row, makes two rows of the same width.
  Mode n is drawn as Model View's step n - 1, of the model the repeat names.
* **The tables of modes.** f and T of each natural vibration mode from its eigenvalue, and the
  buckling factors of the modes asked for: `"modes": 2` is the first two, `"modes": "2"` mode 2.
* **What goes wrong.** A table with no model of its kind, a mode the model does not have, an unknown
  placeholder or entry: each is noted in the report and the log, and the rest is made. A template
  that is not JSON is refused, saying where.
* **The default template** has a chapter for each analysis given, and draws its modes.
* **The page.** A paper of your own in mm and turned to landscape, the margins in mm with the
  content inside them, a title block on every page - its lines, fills and texts placed from its frame,
  y up, {project} filled in when the report is built and {page} and {pages} page by page - and no
  plain footer beside it.
* **What the report components hand out.** A cover where its block is and none on the first page,
  a note in small type, a table of your own with its units in the header and its numbers on the
  right, and {frequency} of the mode a repeat is on.
* **Topics.** A pushover's model is a kind of its own; "stability" finds the buckling model and
  "natural vibration" the vibration one; a repeat over a pushover's steps draws it at each step asked
  for, {step} in the caption.
* **Footfall.** The model Footfall Analysis hands on is a kind of its own though it is modal, its
  table lists the nodes that respond most, worst first, and {footfall.maxFactor} reads its largest
  response factor. Sorted "by" its steady-state R or transient acceleration, that column comes first,
  in mm/s² for an acceleration, and stays though not ticked; {footfall.maxSteadyAcceleration} and the
  like read the largest of each part; a stair, whose transient was not checked, has no transient to
  sort by, and the report says why.
* **Numbered headings.** Headings, subheadings and sub-subheadings are numbered as they come - 1,
  1.1, 1.2, 1.3, 1.3.1, 1.3.2, 2, 2.1 - a heading "numbered": false is left out of the count, and a
  template "numbered": false numbers none; the number is drawn on the heading's line, its text after
  it, and headings at the foot of a page go over with the picture after them.
* **A picture with its tables.** A "group", as Report View hands out a picture with the tables
  ticked in its menus, makes its entries in their order - table, pictures, table - each table with
  only the columns ticked, the pictures of the model it names. The note under a table is kept by
  "Note below", or by "Note", as a file saved before names it.
* **The tables of what a picture draws.** A figure whose results ask for a table is followed by
  its tables, read once: a row for the component drawn with its largest and smallest value and their
  nodes, and the reactions as a row per support and a column per force, summed (6E-17 read as 0); in
  a repeat, one set of tables, of its last pass, after all the pictures.
