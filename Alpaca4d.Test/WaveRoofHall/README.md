# Wave roof hall, in a live Rhino

    RHINO_PIPE=rhinocode_remotepipe_<pid> ./example.sh

Builds `Tools/Examples/Structures/WaveRoofHall.py`'s definition in a running Rhino 8 (its script
server started) with this build of the plugin, solves it over its eight load combinations, and
reads what it gives:

- the four Utilisations - timber arches and purlins on the Timber type, steel columns and the rest
  on the Steel type - over the six ULS combinations: the largest of each, where and by which check,
  and the arches' k_mod of each combination (0.6, 0.9, 1.1 from the patterns' Durations);
- the largest displacement under SLS 1, against span/300;
- the two Section Optimisers over the six ULS combinations, the hall as drawn (one iteration) and
  sized: each kind of member's section and largest utilisation, the mass of each iteration, that
  the sized hall converges with every member at or below 1, and that each utilisation plot draws
  every beam.

Every member must be at or below 1 in the hall as drawn too. Needs a Release build of Alpaca4d.Gh,
OpenSees, and a licence (the hall is past the free limit): `Alpaca4d.Utils/data_august_28.bin` by
default.
