"""Puts each component's image on its page of the docs.

    python3 docs.py components.json doc_folder

A component's page is the one with its title line, "`Run Analysis (Alpaca4d)` - ...", and its
image goes right under that line, in the figure markup GitBook writes itself. A page documenting
several components gets each one's image under its own title line. A component whose
menus start closed gets a second image under the first, with every menu open. Running it again
replaces the images it put there before rather than adding more, so it can follow the components
as they change. Components with no page are listed at the end.
"""
import json
import os
import re
import struct
import sys

from nodepen import slug

ASSETS = os.path.join(".gitbook", "assets", "components")


def png_width(path):
    """The width a 2x image is shown at: half its pixels."""
    with open(path, "rb") as f:
        header = f.read(24)
    return struct.unpack(">I", header[16:20])[0] // 2


def figure(doc_root, page_dir, image, alt, caption):
    src = os.path.relpath(os.path.join(doc_root, ASSETS, image), page_dir).replace(os.sep, "/")
    width = png_width(os.path.join(doc_root, ASSETS, image))
    cap = f"<p>{caption}</p>" if caption else ""
    return f'<figure><img src="{src}" alt="{alt}" width="{width}"><figcaption>{cap}</figcaption></figure>'


def canonical(name):
    """A component name as the docs title it: without "(Alpaca4d)", spaces and case. The plugin's
    names are not always written the same way - " Run Analysis (Alpaca4d)" sorts first with its
    leading space, "Fiber Point(Alpaca4d)" has none before the bracket, "nD" has no suffix."""
    return " ".join(name.replace("(Alpaca4d)", " ").split()).lower()


# A title line names the component and its tab: "`Counter (Alpaca4d)` — **Alpaca4d ▸ 10_Utility**".
TITLE = re.compile(r"^`([^`]+)`\s*—\s*\*\*Alpaca4d")
OLD_FIGURE = re.compile(r"\n\n<figure><img src=\"[^\"]*" + re.escape(ASSETS.replace(os.sep, "/")) + r"/[^\"]+\.png\"[^\n]*</figure>")


def main():
    data_file, doc_root = sys.argv[1:3]
    components = {canonical(c["name"]): c for c in json.load(open(data_file))}
    placed, pages = set(), []
    for folder, _, files in os.walk(doc_root):
        if ".gitbook" not in folder:
            pages += [os.path.join(folder, f) for f in files if f.endswith(".md")]

    for page in pages:
        original = open(page, encoding="utf-8").read()
        # What an earlier run put here goes first, so running again replaces rather than adds.
        text = OLD_FIGURE.sub("", original)
        lines = text.split("\n")
        out = []
        for line in lines:
            out.append(line)
            match = TITLE.match(line)
            component = components.get(canonical(match.group(1))) if match else None
            if not component:
                continue
            name = slug(component["name"])
            images = [(f"{name}.png", f"{component['nick'].strip()} component", "")]
            if os.path.exists(os.path.join(doc_root, ASSETS, f"{name}-open.png")):
                images.append((f"{name}-open.png", f"{component['nick'].strip()} component with every menu open", "With every menu open"))
            for image, alt, caption in images:
                out += ["", figure(doc_root, os.path.dirname(page), image, alt, caption)]
            placed.add(canonical(component["name"]))
        text = "\n".join(out)
        if text != original:
            with open(page, "w", encoding="utf-8") as f:
                f.write(text)

    missing = sorted(c["name"].strip() for key, c in components.items() if key not in placed)
    print(f"{len(placed)} components placed in the docs")
    if missing:
        print("No page found for:\n  " + "\n  ".join(missing))


if __name__ == "__main__":
    main()
