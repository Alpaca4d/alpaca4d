# Commits

- Do not commit after every change. Leave the work uncommitted and wait until you are asked to
  commit. This holds for this repository and for the docs repository, alpaca4d.github.io.
- Before committing, read the diff. If a component or an icon changed and its docs did not,
  perhaps because it was changed by hand, say so and offer to update the docs first
  (see `docs.md`).
- When asked, commit in small, single-purpose chunks, each one building, in gitmoji style.
  - Start each subject with a gitmoji, then a plain English sentence:
    `✨ Add the Topology Optimisation component`.
  - The gitmojis used here: ✨ feature, 🐛 fix, ✅ tests, 📝 docs, 🍱 icons and pictures,
    💄 look, 💬 text and messages, ♻️ refactor, ⚡️ speed, 🔥 removal, 🚚 move or rename,
    🔧 config, 👷 build scripts, 🔀 merge, 🔖 release.
  - Order the commits so each one builds: Core, then tests, the Gh component, icons, docs.
  - Split shared files (csproj, Resources.resx and Resources.Designer.cs) by hunk.
  - Commit the docs repository separately, in the same style.
- Do not push unless asked.
