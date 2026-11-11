#!/usr/bin/env python3
"""The Alpaca4d teaser: a fast cut through everything Alpaca4d does, made from the clips in marketing/.

    python3 teaser.py               # marketing/teaser/capabilities/alpaca4d-teaser.mp4, 1920 x 1080
    python3 teaser.py --small       # 960 x 540, four times faster, to check the cut
    python3 teaser.py --stills      # also a still of every shot, in out/teaser/, to check the framing

Needs ffmpeg, and Pillow and numpy (pip install pillow numpy). The fonts are the site's, Overpass and
Overpass Mono, fetched from Google Fonts into out/fonts the first time.

The cut sits on a grid of 120 beats a minute, a beat every 0.5 s: every shot is a whole number of beats
(the opening flashes half a beat), so a 120 BPM track laid under it lands on the cuts.

A chapter is a capability: its label (the site's eyebrow and a headline ending in a coloured dot) slides
in over the band at the bottom, a rainbow wipe brings it in. A shot plays one clip, or a row of them side
by side. Each clip is cut to what is drawn in it (`keep` first drops a legend or a chart), sped up, and
pushed in slowly; `cover` fills its cell instead, for the walk-throughs and anything drawn to its edges.
The clips are drawn on 251 grey, as Shots.cs draws them, and so is the teaser: no edge shows.
"""
import argparse, json, os, shutil, subprocess, sys, urllib.request
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
MARKETING = os.path.abspath(os.path.join(REPO, "..", "alpaca4d.github.io", "marketing"))
OUT_DIR = os.path.join(MARKETING, "teaser", "capabilities")
WORK = os.path.join(HERE, "out", "teaser")
FONTS = os.path.join(HERE, "out", "fonts")
LOGO = os.path.join(REPO, "Assets", "Logo.png")
FF = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
FP = shutil.which("ffprobe") or "/opt/homebrew/bin/ffprobe"
FONT_URLS = {
    "Overpass.ttf": "https://github.com/google/fonts/raw/main/ofl/overpass/Overpass%5Bwght%5D.ttf",
    "OverpassMono.ttf": "https://github.com/google/fonts/raw/main/ofl/overpassmono/OverpassMono%5Bwght%5D.ttf",
}

FPS = 30
BEAT = 15                        # frames in a beat: 120 BPM at 30 fps
TRANSITION = 12                  # frames of the rainbow wipe into a chapter
ZOOM = 1.04                      # each shot pushes in from 1 to this

BG = 251                         # the clips' grey
RED, ORANGE, PURPLE = (254, 0, 0), (235, 108, 63), (158, 53, 218)
GREEN, BLUE, NAVY = (175, 201, 48), (66, 136, 247), (1, 30, 254)
INK, INK3 = (42, 45, 49), (111, 116, 124)
RAINBOW = [RED, ORANGE, PURPLE, GREEN, BLUE, NAVY]
# Rhino draws the grey a shade off and video coding moves it; anything that light is made the grey again.
_LIGHT = "if(gte(val,%d),%d,val)" % (BG - 3, BG)
BACKGROUND = "format=rgb24,lutrgb=r='{0}':g='{0}':b='{0}'".format(_LIGHT)


# ---------------------------------------------------------------------------------------------- the cut

class C:
    """A clip: a video in marketing/, from t seconds, played `speed` times as fast. keep = the part of the
    frame to look in (fractions x0, y0, x1, y1), then cut to what is drawn there unless trim is False.
    trim="box" keeps all that is drawn there, a chart or a legend too. loop: play it round again rather than
    hold its last frame. cover: fill the cell, cutting the clip to its shape (anchor: where to cut from,
    0 top, 1 bottom). window: a screen recording, drawn as a window with rounded corners and a shadow,
    and not pushed in."""
    def __init__(self, src, t=0.0, speed=1.0, keep=(0, 0, 1, 1), trim=True, loop=False, cover=False,
                 pad=0.05, anchor=0.5, window=False):
        self.src, self.t, self.speed, self.keep = src, t, speed, keep
        self.trim, self.loop, self.cover, self.pad, self.anchor = trim, loop, cover, pad, anchor
        self.window = window


class Shot:
    """beats long; its clips side by side, or one above the other with stack=True."""
    def __init__(self, beats, *clips, full=False, stack=False):
        self.beats, self.clips, self.full, self.stack = beats, clips, full, stack


class Chapter:
    def __init__(self, eyebrow, headline, colour, *shots):
        self.eyebrow, self.headline, self.colour, self.shots = eyebrow, headline, colour, shots


def swing(src, t=0.3, speed=1.4, keep=(0, 0, 1, 1), **kw):
    """A structure deforming or a mode swinging: a loop from rest to the deformed shape and back."""
    return C(src, t, speed, keep, loop=True, **kw)


NO_LEGEND = (0.2, 0, 1, 1)       # the structures' legends stand on the left fifth
QUAKE_TOP = (0.06, 0, 1, 0.6)    # an earthquake: the model over its charts
TOWER = (0.1, 0, 0.42, 1)        # a tower: the model, left of its charts
PLASTIC = (0.1, 0.04, 1, 0.6)    # plasticity: the model over its curve, the legend left
P = "principal-stress-lines/"

FLASHES = []                     # shots before the title, half a beat each; none since 2026-11-09

CHAPTERS = [
    Chapter("Static analysis", "Frames, trusses, shells", BLUE,
        Shot(2, swing("structures/truss-roof-hall/truss-roof-hall-deforming.mp4", keep=NO_LEGEND)),
        Shot(2, swing("structures/steel-tower/steel-tower-deforming.mp4", keep=NO_LEGEND),
                swing("structures/vertical-truss-facade/vertical-truss-facade-deforming.mp4", keep=NO_LEGEND)),
        Shot(2, swing("structures/barrel-vault/barrel-vault-deforming.mp4", keep=NO_LEGEND)),
        Shot(2, swing("structures/compressed-shell/compressed-shell-deforming.mp4", keep=NO_LEGEND))),
    Chapter("Natural vibration · beams, shells, solids", "Every mode", PURPLE,
        Shot(3, swing("structures/tank-modes/tank-modes-swinging.mp4", 0.3, 1.4, cover=True)),
        Shot(3, swing("structures/steel-tower/steel-tower-mode.mp4", 0.3, 1.4, keep=NO_LEGEND),
                swing("solids/rebellious-slave/mode-1.mp4", 0.3, 1.5, keep=(0.2, 0, 1, 1)),
                swing("solids/rebellious-slave/mode-2.mp4", 0.3, 1.5, keep=(0.2, 0, 1, 1)))),
    Chapter("Time history · response spectrum", "Earthquakes", RED,
        Shot(6, C("earthquakes/earthquake-city/earthquake-city-shaking.mp4", 2.0, 1, QUAKE_TOP)),  # real time
        Shot(4, C("earthquakes/cable-stayed-bridge-earthquake/cable-stayed-bridge-earthquake-shaking.mp4",
                  4.5, 1, QUAKE_TOP)),                                                       # real time
        Shot(4, C("earthquakes/outrigger-tower-earthquake/outrigger-tower-earthquake-shaking.mp4", 14, 3, TOWER),
                C("earthquakes/diagrid-tower-earthquake/diagrid-tower-earthquake-shaking.mp4", 22, 3, TOWER),
                C("earthquakes/buttressed-core-earthquake/buttressed-core-earthquake-shaking.mp4", 2, 3, TOWER),
                C("earthquakes/twisting-tower-earthquake/twisting-tower-earthquake-shaking.mp4", 3.5, 3,
                  (0.1, 0, 0.5, 1)),
                C("earthquakes/bundled-tube-earthquake/bundled-tube-earthquake-shaking.mp4", 17, 3, TOWER)),
        Shot(3, C("earthquakes/response-spectrum/response-spectrum-shaking.mp4", 0.6, 2.5, (0.08, 0.05, 1, 0.535)))),
    Chapter("Pushover · plasticity", "Past the elastic limit", ORANGE,
        Shot(3, C("pushover/irregular-building-pushover/irregular-building-pushover-pushing.mp4", 0.0, 3, trim="box"),
                C("plasticity/beam-with-holes/beam-with-holes-pushing.mp4", 0.0, 5, (0.1, 0.06, 1, 0.55))),
        Shot(2, C("plasticity/pipe-t-joint/pipe-t-joint-pushing.mp4", 2.0, 5, PLASTIC),
                C("plasticity/yield-lines/yield-lines-pushing.mp4", 2.0, 5, PLASTIC)),
        Shot(2, C("plasticity/plate-with-a-hole/plate-with-a-hole-pushing.mp4", 2.0, 5, PLASTIC),
                C("plasticity/thick-pipe/thick-pipe-pushing.mp4", 2.0, 5, PLASTIC))),
    Chapter("Footfall vibration", "Feel every footstep", GREEN,
        Shot(2, swing("footfall/double-landing-stair/footfall-stair-mode.mp4", 0.3, 2.0, keep=(0.12, 0, 1, 1))),
        Shot(2, swing("footfall/helical-stair/footfall-helical-stair-mode.mp4", 0.3, 2.0, keep=(0.12, 0, 1, 1)))),
    Chapter("3D concrete printing · buckling", "Print it before it buckles", BLUE,
        Shot(4, C("3d-printing/printed-cylinder/printed-cylinder-printing.mp4", 0, 8, (0, 0, 1, 0.8)),
                C("3d-printing/twisted-column/twisted-column-printing.mp4", 0, 8, (0, 0, 1, 0.86)),
                C("3d-printing/leaning-column/leaning-column-printing.mp4", 0, 8, (0, 0, 1, 0.84)),
                C("3d-printing/dome/printed-dome-printing.mp4", 0, 8, (0, 0, 1, 0.78))),
        Shot(2, swing("3d-printing/printed-cylinder/printed-cylinder-buckling.mp4", 0.4, 1.3),
                swing("3d-printing/twisted-column/twisted-column-buckling.mp4", 0.4, 1.3),
                swing("3d-printing/leaning-column/leaning-column-buckling.mp4", 0.4, 1.3),
                swing("3d-printing/dome/printed-dome-buckling.mp4", 0.4, 1.3))),
    Chapter("Section optimiser", "Every member sized", PURPLE,
        Shot(3, C("section-optimiser/curved-beam-moving-load/curved-beam-moving-load.mp4", 0.0, 2.5, trim="box")),
        Shot(3, C("section-optimiser/beam-grid-moving-supports/beam-grid-moving-supports.mp4", 0.0, 3, trim="box"))),
    Chapter("Design explorer", "Explore every option", GREEN,
        Shot(7, C("explorer/footbridge-study/explorer-study.mp4", 0.0, 2.35, trim=False, window=True))),
    Chapter("Topology optimisation", "Only the material it needs", RED,
        Shot(6, C("topology-optimisation/bridge/bridge-iterations.mp4", 0.0, 3.2)),
        Shot(2, C("topology-optimisation/truss-node/truss-node-multi-material-run.mp4", 4.0, 7))),
    Chapter("Principal stress lines", "See the forces flow", ORANGE,
        Shot(4, C(P + "deep-beam/deep-beam-load-walking.mp4", 0.0, 2.5)),
        Shot(2, C(P + "nervi-umbrella/nervi-umbrella-walk-through.mp4", 0.0, 1.5, cover=True))),
]
TITLE_BEATS, END_BEATS = 4, 8


# ------------------------------------------------------------------------------------------- the clips

_probe, _boxes = {}, {}


def probe(src):
    if src not in _probe:
        j = json.loads(subprocess.check_output([FP, "-v", "error", "-select_streams", "v:0", "-show_entries",
                        "stream=width,height:format=duration", "-of", "json", os.path.join(MARKETING, src)]))
        _probe[src] = (j["streams"][0]["width"], j["streams"][0]["height"], float(j["format"]["duration"]))
    return _probe[src]


def keep_px(clip):
    w, h, _ = probe(clip.src)
    x0, y0, x1, y1 = clip.keep
    return int(x0 * w) // 2 * 2, int(y0 * h) // 2 * 2, int(x1 * w) // 2 * 2, int(y1 * h) // 2 * 2


def _runs(profile):
    """The runs of non-empty places along a profile: (start, end, how much ink)."""
    runs, start = [], None
    for i, v in enumerate(list(profile) + [0]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            runs.append((start, i, int(profile[start:i].sum())))
            start = None
    return runs


def _main_part(ink, share=0.15):
    """Cut the ink to its main parts: across, then down, twice, keep the runs with at least `share` of the
    ink of the largest, from the first such to the last. A legend's title or its numbers, apart from the
    model, go; two towers side by side both stay."""
    ink = ink.copy()
    for _ in range(2):
        for axis in (0, 1):
            runs = _runs(ink.sum(axis=axis))
            if not runs:
                return None
            big = max(r[2] for r in runs)
            kept = [r for r in runs if r[2] >= share * big]
            a, b = kept[0][0], kept[-1][1]
            if axis == 0:
                ink[:, :a] = False; ink[:, b:] = False
            else:
                ink[:a] = False; ink[b:] = False
    rows, cols = np.where(ink.any(axis=1))[0], np.where(ink.any(axis=0))[0]
    if len(rows) == 0:
        return None
    return cols[0], rows[0], cols[-1] + 1, rows[-1] + 1


def drawn_box(clip, span):
    """What is drawn in the clip's keep, over the time it plays: every pixel not the grey on six frames,
    a quarter size, cut to its main parts (trim="box": all of it)."""
    # the file's time too: a scene shot again may be framed differently
    key = "%s|%s|%s|%s|%s|%d" % (clip.src, clip.t, span, clip.keep, clip.trim,
                                 os.path.getmtime(os.path.join(MARKETING, clip.src)))
    if key in _boxes:
        return _boxes[key]
    _, _, dur = probe(clip.src)
    kx0, ky0, kx1, ky1 = keep_px(clip)
    kw, kh = kx1 - kx0, ky1 - ky0
    sw, sh = kw // 4, kh // 4
    a = clip.t if not clip.loop else 0.0
    b = min(dur - 0.05, clip.t + span) if not clip.loop else dur - 0.05
    ink = np.zeros((sh, sw), bool)
    for i in range(6):
        t = a + (b - a) * i / 5
        raw = subprocess.check_output([FF, "-v", "error", "-ss", "%.3f" % t, "-i", os.path.join(MARKETING, clip.src),
                                       "-frames:v", "1", "-vf", "crop=%d:%d:%d:%d,scale=%d:%d" % (kw, kh, kx0, ky0, sw, sh),
                                       "-f", "rawvideo", "-pix_fmt", "rgb24", "-"])
        if len(raw) == sw * sh * 3:
            im = np.frombuffer(raw, np.uint8).reshape(sh, sw, 3).astype(np.int16)
            ink |= np.abs(im - BG).max(axis=2) > 18
    if clip.trim == "box":
        rows, cols = np.where(ink.any(axis=1))[0], np.where(ink.any(axis=0))[0]
        q = (cols[0], rows[0], cols[-1] + 1, rows[-1] + 1) if len(rows) else None
    else:
        q = _main_part(ink)
    box = (0, 0, kw, kh) if q is None else tuple(v * 4 for v in q)
    pad = int(clip.pad * max(box[2] - box[0], box[3] - box[1]))
    box = tuple(int(v) for v in (max(0, box[0] - pad) + kx0, max(0, box[1] - pad) + ky0,
                                 min(kw, box[2] + pad) + kx0, min(kh, box[3] + pad) + ky0))
    _boxes[key] = box
    return box


class Reader:
    """A clip as frames the size of its cell, pushed in by ZOOM, from ffmpeg as they are needed."""
    def __init__(self, clip, cell, n):
        cw, ch = cell[2] - cell[0], cell[3] - cell[1]
        if clip.trim and not clip.cover:
            x0, y0, x1, y1 = drawn_box(clip, n / FPS * clip.speed)
        else:
            x0, y0, x1, y1 = keep_px(clip)
        bw, bh = x1 - x0, y1 - y0
        if clip.cover:
            if bw / bh > cw / ch:
                nw = int(bh * cw / ch)
                x0, bw = x0 + (bw - nw) // 2, nw
            else:
                nh = int(bw * ch / cw)
                y0, bh = y0 + int((bh - nh) * clip.anchor), nh
        s = min(cw / bw, ch / bh) * (0.96 if clip.window else ZOOM)
        self.w, self.h = max(2, round(bw * s)), max(2, round(bh * s))
        vf = "crop=%d:%d:%d:%d,setpts=(PTS-STARTPTS)/%s,fps=%d,scale=%d:%d:flags=lanczos,%s" % (
            bw, bh, x0, y0, clip.speed, FPS, self.w, self.h, BACKGROUND)
        cmd = [FF, "-v", "error"] + (["-stream_loop", "-1"] if clip.loop else []) + [
            "-ss", "%.3f" % clip.t, "-i", os.path.join(MARKETING, clip.src), "-vf", vf, "-frames:v", str(n),
            "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
        self.p = subprocess.Popen(cmd, stdout=subprocess.PIPE)
        self.last, self.src, self.window = None, clip.src, clip.window

    def next(self):
        size = self.w * self.h * 3
        buf = self.p.stdout.read(size)
        if len(buf) == size:
            self.last = np.frombuffer(buf, np.uint8).reshape(self.h, self.w, 3)
        if self.last is None:
            raise RuntimeError("no frames from " + self.src)
        return self.last

    def close(self):
        self.p.stdout.close()
        self.p.wait()


# ------------------------------------------------------------------------------------------- type

def ease(p):
    p = min(max(p, 0.0), 1.0)
    return 1 - (1 - p) ** 3


def ease_io(p):
    p = min(max(p, 0.0), 1.0)
    return 4 * p ** 3 if p < 0.5 else 1 - (-2 * p + 2) ** 3 / 2


def fetch_fonts():
    os.makedirs(FONTS, exist_ok=True)
    for name, url in FONT_URLS.items():
        path = os.path.join(FONTS, name)
        if not os.path.exists(path):
            print("fetching", name)
            urllib.request.urlretrieve(url, path)


_fonts = {}


def font(size, weight, mono=False):
    key = (round(size), weight, mono)
    if key not in _fonts:
        f = ImageFont.truetype(os.path.join(FONTS, "OverpassMono.ttf" if mono else "Overpass.ttf"), key[0])
        f.set_variation_by_axes([weight])
        _fonts[key] = f
    return _fonts[key]


def text_width(text, f, tracking=0.0):
    return f.getlength(text) + tracking * f.size * max(0, len(text) - 1)


def draw_text(d, x, y, text, f, fill, tracking=0.0):
    """Text from its left baseline at x, y, letter-spaced by `tracking` em, keeping the font's kerning."""
    for i, ch in enumerate(text):
        d.text((x + f.getlength(text[:i]) + tracking * f.size * i, y), ch, font=f, fill=fill, anchor="ls")
    return x + text_width(text, f, tracking)


def rainbow(w, h):
    xs = np.linspace(0, len(RAINBOW) - 1, w)
    i = np.minimum(xs.astype(int), len(RAINBOW) - 2)
    f = (xs - i)[:, None]
    c = np.array(RAINBOW, float)
    row = (c[i] * (1 - f) + c[i + 1] * f).astype(np.uint8)
    return np.repeat(row[None], h, axis=0)


def pill(w, h):
    """The site's rainbow bar, rounded, as RGBA."""
    im = Image.fromarray(rainbow(w, h)).convert("RGBA")
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), radius=h // 2, fill=255)
    im.putalpha(m)
    return im


def layer(w, h):
    return Image.new("RGBA", (max(1, int(w)), max(1, int(h))), (0, 0, 0, 0))


def eyebrow_layer(text, k, number=None, colour=INK3, size=24):
    """The site's eyebrow: a rainbow bar, then mono capitals, letter-spaced; a chapter's number first."""
    f = font(size * k, 600, mono=True)
    fb = font(size * k, 700, mono=True)
    bar_w, bar_h, gap = int(46 * k), max(2, int(8 * k)), int(16 * k)
    text = text.upper()
    num = ("%02d" % number) if number is not None else ""
    w = bar_w + gap + (text_width(num + "  ", fb, 0.16) if num else 0) + text_width(text, f, 0.16) + 4
    h = size * k * 1.5
    im = layer(w, h)
    base = size * k * 1.1
    im.alpha_composite(pill(bar_w, bar_h), (0, int(base - size * k * 0.36 - bar_h / 2)))
    d = ImageDraw.Draw(im)
    x = bar_w + gap
    if num:
        x = draw_text(d, x, base, num, fb, colour, 0.16)
        x += text_width("  ", fb, 0.16)
    draw_text(d, x, base, text, f, INK3, 0.16)
    return im, base


def headline_layer(text, k, colour, size=80, weight=900, dot=True):
    """The site's display type: Overpass 900, tight, a full stop in the chapter's colour."""
    f = font(size * k, weight)
    w = text_width(text + ".", f, -0.035) + size * k * 0.1
    im = layer(w, size * k * 1.25)
    d = ImageDraw.Draw(im)
    base = size * k * 0.98
    x = draw_text(d, 0, base, text, f, INK, -0.035)
    if dot:
        draw_text(d, x - 0.035 * f.size, base, ".", f, colour)
    return im, base


def logo(height):
    """The alpaca, on nothing: its white ground filled away from the corners."""
    im = Image.open(LOGO).convert("RGB")
    for xy in [(0, 0), (im.width - 1, 0), (0, im.height - 1), (im.width - 1, im.height - 1)]:
        ImageDraw.floodfill(im, xy, (255, 0, 255), thresh=60)
    a = np.asarray(im).copy()
    alpha = np.where((a[..., 0] == 255) & (a[..., 1] == 0) & (a[..., 2] == 255), 0, 255).astype(np.uint8)
    rgba = Image.fromarray(np.dstack([a, alpha]), "RGBA")
    rgba = rgba.crop(rgba.getbbox())
    return rgba.resize((round(rgba.width * height / rgba.height), round(height)), Image.LANCZOS)


def reveal(base_img, lay, x, y, p, rise):
    """Paste a layer wiped in from the left by p (0 to 1), rising `rise` pixels into place."""
    p = ease(p)
    if p <= 0:
        return
    w = max(1, int(lay.width * p))
    base_img.alpha_composite(lay.crop((0, 0, w, lay.height)), (int(x), int(y + rise * (1 - p))))


# ------------------------------------------------------------------------------------------- frames

class Teaser:
    def __init__(self, W, H):
        self.W, self.H, self.k = W, H, H / 1080
        k = self.k
        self.band = int(200 * k)
        self.margin = int(56 * k)
        self.content = (self.margin, int(36 * k), W - self.margin, H - self.band)
        self.full = (int(40 * k), int(40 * k), W - int(40 * k), H - int(40 * k))
        self.gap = int(24 * k)
        self.progress = rainbow(W, max(2, int(6 * k)))
        ys, xs = np.mgrid[0:H, 0:W]
        self.slant = np.tan(np.radians(18))
        self.u = (xs - self.slant * (H - ys)).astype(np.float32)
        self.stripe = int(64 * k)
        # the brand mark at the band's right
        self.mark = logo(70 * k)
        self.word, self.word_base = headline_layer("Alpaca4d", k, GREEN, size=40, dot=False)
        self._windows = {}

    def cells(self, n, rect, stack=False):
        x0, y0, x1, y1 = rect
        if stack:
            h = (y1 - y0 - self.gap * (n - 1)) / n
            return [(x0, int(y0 + i * (h + self.gap)), x1, int(y0 + i * (h + self.gap) + h)) for i in range(n)]
        w = (x1 - x0 - self.gap * (n - 1)) / n
        return [(int(x0 + i * (w + self.gap)), y0, int(x0 + i * (w + self.gap) + w), y1) for i in range(n)]

    def place(self, canvas, img, cell, z):
        """img was decoded at ZOOM; draw it at z, centred in its cell and cut to it."""
        x0, y0, x1, y1 = cell
        f = z / ZOOM
        if abs(f - 1) > 1e-3:
            img = np.asarray(Image.fromarray(img).resize((max(1, round(img.shape[1] * f)),
                                                           max(1, round(img.shape[0] * f))), Image.BILINEAR))
        h, w = img.shape[:2]
        px, py = round((x0 + x1 - w) / 2), round((y0 + y1 - h) / 2)
        sx0, sy0 = max(x0 - px, 0), max(y0 - py, 0)
        sx1, sy1 = min(w, x1 - px), min(h, y1 - py)
        canvas[py + sy0:py + sy1, px + sx0:px + sx1] = img[sy0:sy1, sx0:sx1]

    def place_window(self, canvas, img, cell):
        """A screen recording as a window: centred in its cell, rounded corners, a soft shadow under it."""
        h, w = img.shape[:2]
        k = self.k
        if (w, h) not in self._windows:
            r, pad, drop = int(16 * k), int(48 * k), int(12 * k)
            m = Image.new("L", (w, h), 0)
            ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), radius=r, fill=255)
            sh = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
            ImageDraw.Draw(sh).rounded_rectangle((pad, pad + drop, pad + w - 1, pad + h - 1 + drop), radius=r, fill=255)
            sh = sh.filter(ImageFilter.GaussianBlur(20 * k))
            self._windows[(w, h)] = (np.asarray(m, np.float32)[..., None] / 255,
                                     np.asarray(sh, np.float32)[..., None] / 255 * 0.22, pad)
        m, sh, pad = self._windows[(w, h)]
        x0, y0, x1, y1 = cell
        px, py = round((x0 + x1 - w) / 2), round((y0 + y1 - h) / 2)
        ys, xs = slice(max(0, py - pad), min(self.H, py + h + pad)), slice(max(0, px - pad), min(self.W, px + w + pad))
        a = sh[ys.start - (py - pad):ys.stop - (py - pad), xs.start - (px - pad):xs.stop - (px - pad)]
        region = canvas[ys, xs].astype(np.float32)
        canvas[ys, xs] = (region * (1 - a) + np.array(INK, np.float32) * a).astype(np.uint8)
        region = canvas[py:py + h, px:px + w].astype(np.float32)
        canvas[py:py + h, px:px + w] = (img * m + region * (1 - m)).astype(np.uint8)

    def wipe(self, old, new, p):
        """The rainbow wipe: a slanted band of the six colours crossing left to right, the new frame behind it."""
        reach = self.stripe * len(RAINBOW)
        L = -self.slant * self.H + ease_io(p) * (self.W + reach + self.slant * self.H)
        d = L - self.u
        out = np.where((d < 0)[..., None], old, new)
        for i, c in enumerate(reversed(RAINBOW)):
            m = (d >= i * self.stripe) & (d < (i + 1) * self.stripe)
            out[m] = c
        return out

    def chapter_layers(self, ch, number):
        k = self.k
        eb, eb_base = eyebrow_layer(ch.eyebrow, k, number, ch.colour)
        hl, hl_base = headline_layer(ch.headline, k, ch.colour)
        return eb, eb_base, hl, hl_base

    def band_layers(self, im, ch_layers, f_in):
        """The chapter's label at the left of the band, the brand at its right."""
        k, H = self.k, self.H
        x = int(96 * k)
        eb, eb_base, hl, hl_base = ch_layers
        reveal(im, eb, x, H - 150 * k - eb_base, (f_in - 2) / 10, 10 * k)
        reveal(im, hl, x - 4 * k, H - 58 * k - hl_base, (f_in - 5) / 12, 18 * k)
        wx = self.W - int(96 * k) - self.word.width
        im.alpha_composite(self.word, (wx, int(H - 84 * k - self.word_base)))
        im.alpha_composite(self.mark, (int(wx - 14 * k - self.mark.width), int(H - 76 * k - self.mark.height)))

    def title(self, f, n):
        """Alpaca4d: the alpaca, the eyebrow and the name."""
        k, W, H = self.k, self.W, self.H
        if not hasattr(self, "_title"):
            self._title = (logo(540 * k), eyebrow_layer("OpenSees for Grasshopper", k, size=30),
                           headline_layer("Alpaca4d", k, GREEN, size=210))
        alp, (eb, eb_base), (hl, hl_base) = self._title
        im = Image.new("RGBA", (W, H), (BG, BG, BG, 255))
        gw = alp.width + 60 * k + hl.width
        x0 = (W - gw) / 2
        p = ease(f / 10)
        a = alp.copy()
        a.putalpha(Image.eval(a.getchannel("A"), lambda v: int(v * p)))
        im.alpha_composite(a, (int(x0), int((H - alp.height) / 2 + 40 * k * (1 - p))))
        tx = x0 + alp.width + 60 * k
        reveal(im, eb, tx + 8 * k, H / 2 - 130 * k - eb_base, (f - 4) / 10, 10 * k)
        reveal(im, hl, tx, H / 2 + 110 * k - hl_base, (f - 6) / 12, 30 * k)
        return np.asarray(im.convert("RGB"))

    def end(self, f, n):
        """The alpaca, the name, the line, and where to try it."""
        k, W, H = self.k, self.W, self.H
        if not hasattr(self, "_end"):
            lines = [headline_layer("Structural analysis", k, GREEN, size=64, weight=800, dot=False),
                     headline_layer("at the speed of design", k, GREEN, size=64, weight=800)]
            fb = font(34 * k, 800)
            label = "Try it free"
            bw, bh = text_width(label, fb) + 2 * 34 * k, 76 * k
            btn = layer(bw + 6 * k, bh + 8 * k)
            d = ImageDraw.Draw(btn)
            d.rounded_rectangle((0, 6 * k, bw, bh + 6 * k), radius=bh / 2, fill=INK)
            d.rounded_rectangle((0, 0, bw, bh), radius=bh / 2, fill=GREEN, outline=INK, width=max(2, int(3 * k)))
            d.text((bw / 2, bh / 2 + 2 * k), label, font=fb, fill=INK, anchor="mm")
            url = layer(560 * k, 60 * k)
            draw_text(ImageDraw.Draw(url), 0, 40 * k, "alpaca4d.github.io", font(34 * k, 600, mono=True), INK, 0.02)
            self._end = (logo(560 * k), eyebrow_layer("OpenSees for Grasshopper", k, size=26),
                         headline_layer("Alpaca4d", k, GREEN, size=150), lines, btn, url,
                         eyebrow_layer("Free for models up to 50 elements", k, size=22))
        alp, (eb, eb_base), (hl, hl_base), lines, btn, url, (fr, fr_base) = self._end
        im = Image.new("RGBA", (W, H), (BG, BG, BG, 255))
        gw = alp.width + 70 * k + max(hl.width, lines[1][0].width, btn.width + 40 * k + url.width)
        x0 = (W - gw) / 2
        im.alpha_composite(alp, (int(x0), int((H - alp.height) / 2)))
        tx = x0 + alp.width + 70 * k
        top = H / 2 - 250 * k
        reveal(im, eb, tx + 6 * k, top - eb_base, (f - 6) / 10, 10 * k)
        reveal(im, hl, tx, top + 160 * k - hl_base, (f - 8) / 12, 24 * k)
        reveal(im, lines[0][0], tx + 4 * k, top + 250 * k - lines[0][1], (f - 14) / 12, 16 * k)
        reveal(im, lines[1][0], tx + 4 * k, top + 322 * k - lines[1][1], (f - 17) / 12, 16 * k)
        p = ease((f - 24) / 8)
        if p > 0:
            b = btn.copy()
            b.putalpha(Image.eval(b.getchannel("A"), lambda v: int(v * p)))
            im.alpha_composite(b, (int(tx + 4 * k), int(top + 380 * k + 20 * k * (1 - p))))
        reveal(im, url, tx + 4 * k + btn.width + 34 * k, top + 384 * k, (f - 28) / 10, 0)
        reveal(im, fr, tx + 8 * k, top + 520 * k - fr_base, (f - 32) / 10, 0)
        return np.asarray(im.convert("RGB"))

    def segments(self):
        """(kind, beats, payload) in order; the frame counts come from rounding the beats' running sum."""
        segs = [("flash", s.beats, s) for s in FLASHES]
        segs.append(("title", TITLE_BEATS, None))
        for i, ch in enumerate(CHAPTERS):
            for j, s in enumerate(ch.shots):
                segs.append(("shot", s.beats, (ch, i + 1, s, j == 0)))
        segs.append(("end", END_BEATS, None))
        return segs

    def render(self, path, stills_dir=None):
        segs = self.segments()
        total = round(sum(b for _, b, _ in segs) * BEAT)
        enc = subprocess.Popen([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (self.W, self.H),
                                "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "18",
                                "-pix_fmt", "yuv420p", "-movflags", "+faststart", path], stdin=subprocess.PIPE)
        cum, done, prev = 0.0, 0, None
        layers_for = {}
        for kind, beats, payload in segs:
            cum += beats
            n = round(cum * BEAT) - done
            readers, cells = [], []
            if kind in ("flash", "shot"):
                shot = payload if kind == "flash" else payload[2]
                rect = self.full if (kind == "flash" or shot.full) else self.content
                cells = self.cells(len(shot.clips), rect, shot.stack)
                readers = [Reader(c, cell, n) for c, cell in zip(shot.clips, cells)]
            if kind == "shot":
                ch, number, shot, first = payload
                if number not in layers_for:
                    layers_for[number] = self.chapter_layers(ch, number)
                    ch_start = done
                    self._ch_start = done
            wipe_in = kind == "end" or (kind == "shot" and payload[3] and payload[1] > 0)
            frozen = prev
            for f in range(n):
                if kind == "title":
                    frame = self.title(f, n)
                elif kind == "end":
                    frame = self.end(f - TRANSITION // 2, n)
                else:
                    canvas = np.full((self.H, self.W, 3), BG, np.uint8)
                    z = 1 + (ZOOM - 1) * f / max(1, n - 1)
                    for r, cell in zip(readers, cells):
                        if r.window:
                            self.place_window(canvas, r.next(), cell)
                        else:
                            self.place(canvas, r.next(), cell, z)
                    if kind == "shot":
                        im = Image.fromarray(canvas).convert("RGBA")
                        self.band_layers(im, layers_for[payload[1]], done + f - self._ch_start - TRANSITION // 2)
                        frame = np.asarray(im.convert("RGB")).copy()
                    else:
                        frame = canvas
                if wipe_in and frozen is not None and f < TRANSITION:
                    frame = self.wipe(frozen, frame, (f + 1) / TRANSITION)
                frame = np.array(frame)  # writable, contiguous
                g = done + f
                pw = int(self.W * (g + 1) / total)
                frame[self.H - self.progress.shape[0]:, :pw] = self.progress[:, :pw]
                enc.stdin.write(frame.tobytes())
                if stills_dir and f == n // 2:
                    Image.fromarray(frame).save(os.path.join(stills_dir, "%04d-%s.jpg" % (g, kind)), quality=85)
                prev = frame
            for r in readers:
                r.close()
            done += n
            sys.stdout.write("\r%d / %d frames" % (done, total))
            sys.stdout.flush()
        enc.stdin.close()
        enc.wait()
        print("\n" + path, "%.1f s" % (total / FPS))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--small", action="store_true", help="960 x 540, to check the cut")
    ap.add_argument("--stills", action="store_true", help="a still of each shot in out/teaser/stills")
    ap.add_argument("--out", help="where to write the MP4")
    a = ap.parse_args()
    fetch_fonts()
    os.makedirs(WORK, exist_ok=True)
    W, H = (960, 540) if a.small else (1920, 1080)
    out = a.out or (os.path.join(WORK, "teaser-small.mp4") if a.small else os.path.join(OUT_DIR, "alpaca4d-teaser.mp4"))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    stills = None
    if a.stills:
        stills = os.path.join(WORK, "stills")
        shutil.rmtree(stills, ignore_errors=True)
        os.makedirs(stills)
    cache = os.path.join(WORK, "boxes.json")
    if os.path.exists(cache):
        _boxes.update(json.load(open(cache)))
    try:
        Teaser(W, H).render(out, stills)
    finally:
        json.dump(_boxes, open(cache, "w"))


if __name__ == "__main__":
    main()
