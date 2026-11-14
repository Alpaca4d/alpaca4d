# Iso-surfaces

    ./run.sh

With **Iso-surfaces** ticked, View Results draws each colour of the gradient as one band of
values - the range cut into as many equal bands as there are colours - instead of blending the
colours across it.

Giving each corner its band's colour is not enough: the display blends corner colours across a
face, so a face with a corner in each of two bands still comes out as the gradient between them.
`IsoBands` cuts every face where its value crosses from one band into the next, with the value
running straight across the face as it does in a smooth plot, and draws each piece in its band's
colour alone. A quad is cut as four triangles about its middle, unless it lies in one band, so the
band edges do not lean the way of whichever diagonal was picked. Beams are cut the same way along
their length.

## What is checked here

Each triangle under test stands its value up as its *z*, so a point's value can be read off it.

* **Each value falls in one of the bands**, the range's top in the last band rather than one past
  it, and anything below or above the range in the first or last, as the gradient holds its end
  colours.
* **The edges View Results hands Legend are where its colours change.** With Iso-surfaces on, its
  Values output is the edges of the bands, one more than there are colours, from the range's min
  to its max exactly; the colour changes at each.
* **A triangle is cut into its bands, which cover it exactly**: 1800 random triangles over three
  ranges and three band counts, values running past either end of the range. The pieces' areas
  add up to the triangle's, every corner of a piece is inside its band, there is one piece per band
  from the lowest corner's to the highest's, and none is flat.
* **A band edge through a corner leaves no sliver.**
* **A beam is cut into stretches of one band** where its value crosses an edge, end to end, going
  up and coming back down.

The test loads the built `.gha` and reaches `ColourScale` and `IsoBands` by reflection, because
they are internal to that assembly.

## What is not checked here

Everything that needs Rhino running, to be looked at there:

* the cut mesh `IsoBands.Paint` builds, and that it draws with no colour bleeding across a band
  edge
* that **Element edges** draws the elements' own edges, not every cut
* the extruded beams, which take two rings at every cut, one in either colour
* that Legend writes its labels on the lines between its swatches when it is given one value more
  than there are colours, in all three positions, and that the end labels clear the title
* that a definition saved before Iso-surfaces existed opens with it off
