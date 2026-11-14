---
paths:
  - "Alpaca4d.Gh/**"
  - "Tools/Examples/**"
---

# Keep the docs in step with the components

The docs follow Diátaxis: four kinds of page, each with one job, in its own sidebar group of
`doc/SUMMARY.md`. Keep each page to its kind.

- **Learn** (`doc/learn/NN-area.md`): lessons, one per area of the website's Playground island,
  in its order, listed straight in the sidebar as `🌊 1. River`. A picture of what you build and
  its start and finished `.gh` first, then a few steps, a check against a hand calculation,
  something to try, the usual mistakes.
- **How-to** (`doc/how-to/<topic>/`): one task each, on one screen: You need, Steps, Check, Good
  to know, See also. Link to the reference for the inputs rather than repeating them. Each guide
  goes under a topic - Supports and releases, Loading, Geometry, Analysis, Results, Design checks,
  Optimisation, Exchange, or a new one - whose `README.md` lists its guides; list it there, in
  `doc/how-to/README.md` and in `SUMMARY.md` under its topic. A guide starts with a picture of what it gets you, beside it in
  its topic folder: a Model View capture from `Tools/Marketing` (`HowToPictures.py` and the `howto`
  scene hold the ones made for the guides), an example's picture, or one a reference page has.
- **Reference** (`doc/basics/`): one page per component, as below. What it takes and gives, when to
  use it, what it writes for OpenSees.
- **Concepts** (`doc/concepts/`, and the benchmarks): why and which - how a model is built, axes and
  signs, units, which element, which analysis. No steps.

The overview pages of Learn, How-to and Concepts (their `README.md`) are not in the sidebar: the
Welcome page's cards lead to them, and the sidebar lists the lessons, the topics and the concepts
themselves.

A change to a component can touch more than its reference page: grep `doc/how-to/` and
`doc/concepts/` for it too.

Emoji mark only the top of the tree: a section's page and a page at the top of its sidebar group
(`# 🔗 Constraints`) have one, in its title and its `SUMMARY.md` entry; a page under a section
(`# Rigid Diaphragm`) has none.

Do these as part of the same piece of work, without being asked. The docs are in the docs
repository next to this one, `../alpaca4d.github.io/doc`.

- **A Grasshopper component changes** (`Alpaca4d.Gh/NN_*/`): its name, inputs, outputs, defaults,
  menus, message or behaviour. Update its docs page, the one whose title line reads
  `` `Name (Alpaca4d)` — **Alpaca4d ▸ Tab** ``. Fix the Inputs and Outputs tables
  (Name | Nick | Type | Default | Description) and any text about what changed, then redraw its
  picture (see the next item). Keep the tables in that form, under `### Inputs` (or
  `### Inputs — <type>`, one per type of a switcher) and `### Outputs`: the docs site folds them
  and the picture into one card (`docs-site/scripts/cards.mjs`), with the first sentence of each
  Description on the port's line and the rest a click away, so put the gist first. Name a type as
  the component's drop-down does (`### Inputs — Elastic`), also on a page about one type only
  (`### Inputs — WithHinges`): each tab then shows that type's picture, `<name>--<type>.png`,
  which the component-images tool draws for every type of a switcher. Grep the docs for the component's name, because other pages and
  examples may mention it.
- **A component's icon changes** (`Alpaca4d.Gh/Resources/*.png`), or anything else its picture
  shows: redraw the docs pictures with the component-images skill. To draw the icon itself,
  use the alpaca-icon skill.
- **A new component**: add a page in `doc/basics/<section>/`, written like the pages next to it
  (title line, Inputs, Outputs, When to use it). List it in `doc/SUMMARY.md` and draw its picture.
- **A component is removed, hidden or renamed**: remove or rename its page, then update
  `doc/SUMMARY.md` and every link to the page.
- **An example's .gh changes** (`Tools/Examples/`): copy it over the one in its example folder
  in the docs repository, keeping that file's name, and run `Tools/GhCanvas/run.sh` on it: it
  writes `<slug>-definition.json` beside it, which the example's page draws as the definition.
- **A new example for users**: examples live on the website, not in the docs. Each is a folder
  `examples/<slug>/` at the top of the docs repository, which
  `docs-site/scripts/examples.mjs` turns into a card in the gallery at /examples/ and a page at
  /examples/<slug>/ (the README in `docs-site/` says how to preview it).
  - The folder holds `<slug>.gh`, `<slug>.md` and `<slug>.png`, plus `<slug>-2.png` and so on for
    more pictures. The card's small picture is made from `<slug>.png` by the build. Run
    `Tools/GhCanvas/run.sh` for `<slug>-definition.json`, the definition the page draws under
    the picture.
  - More definitions of one example (`examples/3d-printed-structure/` has four shapes) go in the
    same folder, each linked alone on its line where the page describes it, `[name](name.gh)`: the
    build attaches it there like the first.
  - `<slug>.md` starts with front matter: `order:` (its place in the gallery), `summary:` (one
    sentence, on the card and under the title) and `tags:`, from the filters `examples.mjs`
    lists (Beams & frames, Shells, Solids, Dynamics, Sections, Design checks, Optimisation,
    Topology, Scripting). `draft: true` keeps an example out of the gallery, with no page, until it
    is ready.
  - Then `# <emoji> Title`, the picture as `<figure><img src="<slug>.png" …></figure>` and the
    download link `[<slug>](<slug>.gh)`, each alone on its line and in the folder itself: the
    page shows the picture first, with the .gh attached under it.
  - It then has an introduction, a table of the definition's groups (Group | What it does), how
    to run it and what it shows. Links to the docs go up into them: `../../docs/basics/….md`.
  - Link it from the component's page and from its section's README in the docs, as
    `https://alpaca4d.github.io/examples/<slug>/`.
  - Never point users to `Tools/Examples/` in the Alpaca4d repository.
  - Do not use `doc/examples/`, `doc/assets/` or `.gitbook/assets/` for new examples: those are
    older homes.
  - An example of a single component can instead be a section of that component's page, as the
    Ten Node Tetrahedron examples are.
    - It goes under `## Examples` as `### Title`, with the same picture, download link and text.
    - It gets no page and no `SUMMARY.md` entry of its own.
    - Its files go in a folder beside the page: `<page>/<slug>/<slug>.gh` and `<slug>.png`.
    - It still gets a card in the gallery: a folder `examples/<slug>/` with only `<slug>.md`,
      whose front matter gives `title`, `summary`, `tags`, `order`, and `link`, `picture` and
      `gh` as paths from /examples/ (see `examples/spot-the-cow/spot-the-cow.md`).
  - When it is not clear which of the two the user wants, ask.
