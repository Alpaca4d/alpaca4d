---
paths:
  - "Alpaca4d.Gh/**"
  - "Tools/Examples/**"
---

# Keep the docs in step with the components

Do these as part of the same piece of work, without being asked. The docs are in the docs
repository next to this one, `../alpaca4d.github.io/doc`.

- **A Grasshopper component changes** (`Alpaca4d.Gh/NN_*/`): its name, inputs, outputs, defaults,
  menus, message or behaviour. Update its docs page, the one whose title line reads
  `` `Name (Alpaca4d)` — **Alpaca4d ▸ Tab** ``. Fix the Inputs and Outputs tables
  (Name | Nick | Type | Default | Description) and any text about what changed, then redraw its
  picture (see the next item). Grep the docs for the component's name, because other pages and
  examples may mention it.
- **A component's icon changes** (`Alpaca4d.Gh/Resources/*.png`), or anything else its picture
  shows: redraw the docs pictures with the component-images skill. To draw the icon itself,
  use the alpaca-icon skill.
- **A new component**: add a page in `doc/basics/<section>/`, written like the pages next to it
  (title line, Inputs, Outputs, When to use it). List it in `doc/SUMMARY.md` and draw its picture.
- **A component is removed, hidden or renamed**: remove or rename its page, then update
  `doc/SUMMARY.md` and every link to the page.
- **An example's .gh changes** (`Tools/Examples/`): copy it over the one in the docs example
  folder, keeping the docs file's name.
- **A new example for users**: every downloadable example follows the same pattern.
  - A folder `doc/examples/<slug>/` holds `<slug>.gh`, `<slug>.md` and `<slug>.png`, plus
    `<slug>-2.png` and so on for more pictures.
  - The page opens with `# <emoji> Title`, then the picture and a `[<slug>](…<slug>.gh)` download
    link, both as `https://raw.githubusercontent.com/Alpaca4d/alpaca4d.github.io/…` URLs.
  - It then has an introduction, a table of the definition's groups (Group | What it does), how
    to run it and what it shows.
  - List it under EXAMPLES in `doc/SUMMARY.md`, and link it from the component's page and from
    its section's README.
  - Never point users to `Tools/Examples/` in the Alpaca4d repository.
  - Do not use `doc/assets/` or `.gitbook/assets/` for new examples: those are older homes.
  - An example of a single component can instead be a section of that component's page, as the
    Ten Node Tetrahedron examples are.
    - It goes under `## Examples` as `### Title`, with the same picture, download link and text.
    - It gets no page and no `SUMMARY.md` entry of its own.
    - Its files go in a folder beside the page: `<page>/<slug>/<slug>.gh` and `<slug>.png`.
  - When it is not clear which of the two the user wants, ask.
