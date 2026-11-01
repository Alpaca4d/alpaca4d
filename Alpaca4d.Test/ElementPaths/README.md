# Element paths

    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)
                      # and Grasshopper loaded

Assemble Model takes its elements as a list, so elements on two branches make two models, and the
one without the beams has none of the nodes its supports are at: "Support node ... is not part of
the model!".

ASD Shell and Spring Link make several elements out of one input item, a shell per face and a link
per line, and set them as a list. Grasshopper puts a list set by a component with item inputs on a
branch of its own, one index more than the input's: {0;0} for a mesh on {0}, {0;1} for the next. The
beam components set one beam an item, which stays on {0}. So beams with a shell, or with a link,
wired straight into Assemble Model failed until the input was flattened. Registering the output as a
list does not change it. `ElementOutput.OntoInputBranches`, run after each solution, takes the
index off again.

`GrasshopperChecks.cs` checks, out of the real components:

* where each component puts its elements: beams, a one-face and a two-face mesh, two meshes with an
  ElementId each, two links. All on {0}, and each mesh keeps its own ElementId;
* that a Graft set on ASD Shell's output still grafts;
* a portal frame with a shell on top, and one with a link brace, wired into Assemble Model without
  flattening: one model with all four elements in it.
