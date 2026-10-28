# Working on Alpaca4d

The rules for working here are in `.claude/rules/`, one file per topic:

- `commits.md`: when and how to commit. Always loaded.
- `docs.md`: what to update in the docs when a component, icon or example changes. Loaded when
  working on `Alpaca4d.Gh/` or `Tools/Examples/`.

Add a new topic as a new file there. To load a file only for some folders, give it a `paths:` list
at the top, as `docs.md` has.

How-to procedures are skills, in `.claude/skills/<name>/SKILL.md`. They are loaded only when used:

- `component-images`: redraw the component pictures in the docs.
- `alpaca-icon`: draw a 24 px icon, and add it to the plugin's resources.
- `run-in-rhino`: run Core code or real components inside a live Rhino 8.
- `gh-canvas-capture`: pictures of the canvas and the Rhino viewport.
- `yak-release`: build a Yak package. Started only with `/yak-release <version>`.
