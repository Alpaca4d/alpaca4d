# Legend

    ./run.sh

Legend draws the colours View Results paints with and writes a value beside each. Both now place
the colours through `ColourScale`, so what is checked here is that the labels sit on those stops.

## What is checked here

* **Each label sits where View Results puts its colour.** View Results puts colour *k* of *N* at
  `min + k * span / (N - 1)` and blends in between. Legend used to label the edges of *N* equal
  bands instead, which put each colour up to a band away from where the model shows it - with
  nothing looking wrong. The labels are pinned against `ColourScale.Stops`, which View Results
  paints by, for several palette sizes and ranges.
* **A flat range is one swatch**, at the value the model shows in its first colour.
* **Every label of a legend reads to the same step**, four significant figures of the larger end
  of the range. Two fixed decimals printed every displacement in metres as `0.00`; four figures of
  each label's own put a `5E-05` among `0.0012` and `-0.0034`.
* **Every preset gradient reaches its own top colour.** `Colors.Gradient` took every tenth of the
  101 entries, stopping at the 91st, so neither the model nor the legend ever showed the top of a
  map.

The test loads the built `.gha` and reaches `ColourScale` by reflection, because it is internal to
that assembly.

## What is not checked here

Everything that needs a viewport - to be looked at in Rhino:

* that the legend fits a small viewport: a column of ten swatches at the default scale was taller
  than most viewports, and the labels of a legend on the right ran off the edge
* that the legend goes when its document is closed or another one is brought forward, and when
  Grasshopper's preview is switched off
* that an older definition opens with the old Legend, tagged OLD, and no "Input parameter chunk is
  missing" error, and that Upgrade Components swaps it for the one with a Range input
