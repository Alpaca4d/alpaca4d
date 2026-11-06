"""Builds the social-media page from the marketing folder's READMEs: one card per post, with its text
and first comment ready to copy, the pictures and the video to attach, and where they are on disk.

    python3 social.py            # out/social/index.html and out/social/media/, for the Artifact tool

Every case folder of the website repository's marketing/ holds a README.md (examples/ holds one .md per
case) with the same sections: What to attach, The post, Other openings, First comment, The numbers.
The page shows web-size copies of the files What to attach names - stills as JPEG, videos as a small
MP4 - made with sips and ffmpeg, and made again only when the original is newer.
"""
import html, json, os, re, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
MARKETING = os.environ.get("MARKETING") or os.path.normpath(os.path.join(HERE, "../../../alpaca4d.github.io/marketing"))
OUT = os.path.join(HERE, "out", "social")
MEDIA = os.path.join(OUT, "media")
FFMPEG = "/opt/homebrew/bin/ffmpeg" if os.path.exists("/opt/homebrew/bin/ffmpeg") else "ffmpeg"

# The topics in the order the page lists them, newest work first, and what each is called there.
TOPICS = [
    ("earthquakes", "Earthquakes"), ("structures", "Structures"), ("section-optimiser", "Section Optimiser"),
    ("examples", "Design checks and studies"), ("domain-moment-curvature", "Column domain"),
    ("topology-optimisation", "Topology optimisation"), ("principal-stress-lines", "Principal stress lines"),
    ("solids", "Solids"), ("model-view", "Model View"), ("how-to", "How-to guides"), ("components", "Components"),
    ("support", "Support"), ("playground", "Playground"), ("twisted-ring", "Twisted ring"),
]
# Posts that should wait, and why; matched against the start of a case's id.
HOLD = {
    "playground/": "The Playground page isn't live yet: it still reads as a sketch and is hidden from search.",
    "twisted-ring": "No page on the website yet, and no analysis behind it.",
}
# Things to know before posting, matched the same way.
NOTES = {
    "support": "Overlaps the Supports guide in How-to: post one of the two.",
    "how-to/supports": "Overlaps the Support post: post one of the two.",
    "examples/footbridge-study": "The picture is the Explorer's sample study, its numbers estimated by hand.",
    "section-optimiser/curved-beam": "Its example page is a draft, so the first comment has only the download link.",
    "section-optimiser/beam-grid": "Its example page is a draft, so the first comment has only the download link.",
}
STILL, VIDEO = (".png", ".jpg", ".jpeg", ".gif"), (".mp4", ".mov")
# Shown at the top of the page while it holds; empty when there is nothing to say.
NOTICE = ("The links in the first comments go live once the website is pushed: the new example, how-to and "
          "Playground pages are not on alpaca4d.github.io yet.")


def section(text, name):
    m = re.search(r"^## " + re.escape(name) + r"[^\n]*\n(.*?)(?=^## |\Z)", text, re.S | re.M)
    return m.group(1).strip() if m else ""


def box(text):
    m = re.search(r"```text\n(.*?)\n```", text, re.S)
    return m.group(1) if m else ""


def bullets(text):
    """The bullets of a section as (label, text), a bullet's indented lines joined to it."""
    out = []
    for line in text.split("\n"):
        if line.startswith("- "):
            out.append(line[2:].strip())
        elif line.startswith("  ") and out:
            out[-1] += " " + line.strip()
    found = []
    for b in out:
        m = re.match(r"\*\*(.+?)\*\*:?\s*(.*)", b)
        found.append((m.group(1).rstrip(":").strip(), m.group(2)) if m else ("", b))
    return found


def files(text, folder):
    """The files a bullet names in backticks that are there, as paths."""
    names = []
    for n in re.findall(r"`([^`]+\.(?:png|jpe?g|gif|mp4|mov))`", text, re.I):
        p = os.path.normpath(os.path.join(folder, n))
        if os.path.exists(p) and p not in names:
            names.append(p)
    return names


def plain(md):
    """A README section as plain text for the page: no bold, no backticks."""
    return re.sub(r"\*\*(.+?)\*\*", r"\1", md).replace("`", "")


def preview(src, case_id):
    """A web-size copy of src in media/: stills as JPEG 1600 px wide at most, videos as an MP4 720 px wide
    with a JPEG poster. Gives (kind, file, poster) as published paths."""
    stem = case_id.replace("/", "__") + "__" + os.path.splitext(os.path.basename(src))[0]
    ext = os.path.splitext(src)[1].lower()
    fresh = lambda dst: os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src)
    if ext in VIDEO:
        dst, poster = os.path.join(MEDIA, stem + ".mp4"), os.path.join(MEDIA, stem + "-poster.jpg")
        if not fresh(dst):
            subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-an", "-vf",
                            "scale='min(720,iw)':-2:flags=lanczos", "-c:v", "libx264", "-preset", "slow", "-crf", "30",
                            "-pix_fmt", "yuv420p", "-movflags", "+faststart", dst], check=True)
        if not fresh(poster):
            subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-sseof", "-0.5", "-i", src, "-frames:v", "1",
                            "-vf", "scale='min(720,iw)':-2:flags=lanczos", "-q:v", "4", "-update", "1", poster], check=True)
        return "video", "media/" + os.path.basename(dst), "media/" + os.path.basename(poster)
    dst = os.path.join(MEDIA, stem + ".jpg")
    if not fresh(dst):
        subprocess.run(["sips", "-s", "format", "jpeg", "-s", "formatOptions", "80", "-Z", "1600", src, "--out", dst],
                       check=True, capture_output=True)
    return ("gif" if ext == ".gif" else "image"), "media/" + os.path.basename(dst), None


def cases():
    found = []
    for root, dirs, names in os.walk(MARKETING):
        for n in names:
            rel = os.path.relpath(os.path.join(root, n), MARKETING)
            if n == "README.md" and rel != "README.md":
                found.append((os.path.dirname(rel), os.path.join(root, n)))
            elif n.endswith(".md") and os.path.dirname(rel) == "examples":
                found.append((os.path.splitext(rel)[0], os.path.join(root, n)))
    order = {t: i for i, (t, _) in enumerate(TOPICS)}
    return sorted(found, key=lambda c: (order.get(c[0].split("/")[0], 99), c[0]))


def build():
    os.makedirs(MEDIA, exist_ok=True)
    posts, used = [], set()
    for case_id, path in cases():
        text = open(path, encoding="utf-8").read()
        folder = os.path.dirname(path)
        attach = bullets(section(text, "What to attach"))
        main = [f for label, b in attach if label.lower() == "the post" for f in files(b, folder)]
        carousel = [f for label, b in attach if label.lower().startswith("a carousel") for f in files(b, folder) if f not in main]
        alt = next((b for label, b in attach if label.lower() == "alt text"), "")
        extra = [(label, plain(b)) for label, b in attach if label.lower() not in ("the post", "alt text") and not label.lower().startswith("a carousel")]
        media = []
        for role, group in (("post", main), ("carousel", carousel)):
            for f in group:
                kind, src, poster = preview(f, case_id)
                used.add(os.path.basename(src))
                if poster:
                    used.add(os.path.basename(poster))
                media.append({"role": role, "kind": kind, "src": src, "poster": poster, "name": os.path.basename(f),
                              "path": f, "mb": round(os.path.getsize(f) / 1e6, 1)})
        title = re.sub(r":\s*LinkedIn post\s*$", "", re.search(r"^# (.+)$", text, re.M).group(1)).strip()
        intro = text.split("\n\n")[1].strip() if text.count("\n\n") > 1 else ""
        topic = case_id.split("/")[0]
        hold = next((v for k, v in HOLD.items() if case_id.startswith(k)), "")
        note = next((v for k, v in NOTES.items() if case_id == k or case_id.startswith(k + "/")), "")
        posts.append({
            "id": case_id, "topic": topic, "title": title, "intro": plain(intro).replace("\n", " "),
            "post": box(section(text, "The post")), "comment": box(section(text, "First comment")),
            "openings": [plain(b).strip('"') for _, b in bullets(section(text, "Other openings"))],
            "alt": plain(alt), "extra": extra, "numbers": plain(section(text, "The numbers, if someone asks")),
            "media": media, "folder": folder, "readme": path, "hold": hold, "note": note,
            "changed": time.strftime("%Y-%m-%d", time.localtime(os.path.getmtime(path))),
        })
    for f in os.listdir(MEDIA):
        if f not in used:
            os.remove(os.path.join(MEDIA, f))
    data = {"posts": posts, "topics": [{"id": t, "name": n} for t, n in TOPICS if any(p["topic"] == t for p in posts)],
            "built": time.strftime("%d %B %Y, %H:%M"), "marketing": MARKETING, "notice": NOTICE,
            "finder": "http://127.0.0.1:" + os.environ.get("PORT", "47321"), "helper": os.path.join(HERE, "finder.py")}
    page = open(os.path.join(HERE, "social.html"), encoding="utf-8").read()
    page = page.replace("__DATA__", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
    open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write(page)
    size = sum(os.path.getsize(os.path.join(MEDIA, f)) for f in os.listdir(MEDIA))
    print("%d posts, %d media files, %.1f MB -> %s" % (len(posts), len(os.listdir(MEDIA)), size / 1e6, OUT))


if __name__ == "__main__":
    build()
