---
name: yak-release
description: Build an Alpaca4d package for the Rhino Package Manager (Yak), for example a beta 0.12.0-beta.3. Builds without touching the live Grasshopper folder, packages with YAK/publish.py's functions, compares the package with the previous one and hands the user the push command.
disable-model-invocation: true
argument-hint: <version, e.g. 0.12.0-beta.3>
---

# Build a Yak package

Version asked for: $ARGUMENTS. If none was given, ask. Betas are `0.X.0-beta.N`, and are not
git-tagged.

1. **See what is published:**
   `"/Applications/Rhino 8.app/Contents/Resources/bin/yak" search --all --prerelease alpaca4d`.

2. **Check the components against the last release** (`git log --grep "🔖 Release" -1`).
   - Look for inputs or outputs added, removed or moved, renamed inputs and new menus.
   - A file saved with the old version must still open. Report what you find and let the user
     decide whether a component needs an obsolete copy. Do not decide this yourself.

3. **Build** in Release into a scratch Grasshopper folder. Do not run `publish.py` itself: its Mac
   build copies into the live Grasshopper Libraries folder, which breaks a running Rhino.

   ```
   dotnet build Alpaca4d.Gh/Alpaca4d.Gh.csproj -c Release --no-incremental \
       -p:GrasshopperLibrariesPath=<scratchpad>/gh-libraries
   ```

4. **Package** with publish.py's functions:

   ```
   PATH="/Applications/Rhino 8.app/Contents/Resources/bin:$PATH" python3 -c "
   import sys; sys.path.insert(0, 'YAK'); import publish
   from pathlib import Path
   p = publish.project_paths()
   publish.update_version(p['manifest_file'], '<version>')
   d = publish.collect_files(p, '<version>')
   y = publish.build_yak(d)
   publish.cleanup_version_dir(d)
   print(y)"
   ```

5. **Compare** `unzip -Z1` of the new .yak with the previous version's, in `YAK/<version>/`.
   - Explain every file added or gone.
   - Neither `data.bin` nor any OpenSees belongs in the package.

6. **Hand the user the push.** `yak push` cannot run from here: its token is decrypted with a
   Keychain key the sandbox cannot reach. Do not try another way. Give them:

   ```
   cd YAK/<version> && "/Applications/Rhino 8.app/Contents/Resources/bin/yak" push Alpaca4d-<version>-any-any.yak
   ```

7. Only when asked, commit `🔖 Release <version>`, holding only `YAK/manifest.yml`.
