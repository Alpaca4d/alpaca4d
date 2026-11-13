---
name: marketing-content
description: Make the marketing content of a piece of work - pictures and GIFs in alpaca4d.github.io/marketing/<topic>/<case>/, the LinkedIn README of each case, and the Alpaca4d Posts page. Started only with /marketing-content <topic or case>.
disable-model-invocation: true
argument-hint: <topic or case, e.g. footfall/helical-stair>
---

# Marketing content

Only when the user asks for it. Otherwise pictures go only where the work needs them (a docs page, an
example folder) and nothing is copied to `marketing/`.

## Pictures

- Into `/Users/mp/Documents/GitHub/alpaca4d.github.io/marketing/<topic>/<case>/`: one folder per case
  (`principal-stress-lines/deep-beam`), never cases mixed in one folder or loose files at the topic's
  top. Each round overwrites the files of the round before.
- Made in Grasshopper, with the real components and previews, by a scene of `Tools/Marketing`
  (`run.sh`, then `publish.sh`; its README says how to add a scene). Use the gh-canvas-capture and
  deformation-pictures skills. Not matplotlib, not baked geometry.
- **Animations: GIF only, no MP4** (user, 2026-11-13). `post.py` writes an MP4 too only with `MP4=1`.
- Background light grey 251, the brand palette (memory: docs-picture-palette), the element edges on
  meshed models, colours fading with the swing in every deforming animation, buckling modes with the
  camera still and a jet gradient.
- By topic:
  - **Topology optimisation**: a GIF of the run, design by design along Step (`Iterations` in
    `Shots.cs`), and the final design analysed in Von Mises stress (`Analyse`, only after the run).
  - **Principal stress lines**: a spin GIF and a walk-through GIF of each case (`Turning`, `Walk`,
    `spin_walk` in publish.sh); first family purple #9E35DA, second light blue #4288F7.
    New cases recall Nervi or Musmeci and are named so ("Nervi flat slab").
- Look at one frame per animation and the still, not every frame.

## The LinkedIn post of each case

`marketing/<topic>/<case>/README.md`, one per case. Model: `marketing/earthquakes/girder-bridge-earthquake/README.md`.

- What to attach (the GIF first), alt text, the post, a first comment, two other openings, the numbers.
- The post sells Alpaca4d so people install and buy it: the reader is the hero (a situation they
  know), what they can now see or do, Alpaca4d as the enabler, true numbers. End with
  "Alpaca4d. Structural analysis at the speed of design." and "Try it free on models up to 50
  elements. Link in the comments." (Topology Optimisation: up to 500.)
- Simple words: sentences under 15 words, no jargon in the hook, round numbers in cm and m, 60 to 90
  words, a bold hook, short lines, at most one emoji, 3 or 4 hashtags.
- Only what that case's picture shows: no other examples, no earlier versions.
- Never call an example free to run when it is over the limit, never imply OpenSees is included, no
  prices, no competitors named.
- First comment: the download link https://alpaca4d.github.io/download.html and the example's link.
- Post and first comment in ```text blocks, no markdown inside, one line per paragraph, a blank line
  between. Paste the post in the reply too.

## The Alpaca4d Posts page

https://claude.ai/artifact/B1RoXjpFBpGwjL7zjj3s6c, update it only when the user asks.

- `python3 Tools/Marketing/Social/social.py` builds `Tools/Marketing/out/social/index.html` from every
  README in `marketing/`. A new topic must be in its `TOPICS`. `HOLD`, `NOTES`, `LEFT_OUT` say what
  waits or stays off.
- Read the artifact before publishing: other sessions publish it too.
- Publish with `root` = `Tools/Marketing/out/social` and only the new or changed `media/...` files (at
  most 255 files and 64 MB per publish).

## Hand over

A clickable link to each finished file, grouped by case. Leave everything uncommitted.
