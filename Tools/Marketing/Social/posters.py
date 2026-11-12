#!/usr/bin/env python3
"""Posters of what Alpaca4d does, for a LinkedIn post: a cover, one poster a capability, an end card,
in the teaser's look, 4:5 (the shape LinkedIn gives the most room in the feed), and all of them as one
PDF for a document (carousel) post.

    python3 posters.py        # marketing/teaser/posters/*.png and alpaca4d-carousel.pdf

Needs Pillow and numpy and ffmpeg, as teaser.py, whose type, colours and logo it uses. Each picture is a
still from marketing/ (or a frame of one of its videos), cut to what is drawn in it as the teaser cuts
its clips.
"""
import io, os, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import teaser as T

OUT = os.path.join(T.MARKETING, "teaser", "posters")
BW, BH = 1080, 1350               # the poster, in its base units
K = 2                             # drawn twice that size: 2160 x 2700
INK2 = (75, 80, 88)
LINE = (220, 221, 224)
M = 72                            # the margin

# (eyebrow, headline, colour, caption, pictures, layout)
#   pictures: [(a still in marketing/, or (a video, the second to take), how), ...]
#   how: keep = where to look (fractions), cover = fill the box, anchor = where to cut a cover from
#   layout: "row" side by side, "stack" one above the other, "explorer" the Explorer's two windows
Q = "earthquakes/%s-earthquake/%s-earthquake-shaking.mp4"
TOWER = dict(keep=(0.1, 0, 0.42, 1))
POSTERS = [
    ("Static analysis", "Frames, trusses, shells", T.BLUE,
     "A steel hall's trusses and a concrete vault in the wind, their deformed shapes in colour.",
     [("structures/truss-roof-hall/truss-roof-hall-deformed.png", dict(keep=(0.2, 0, 1, 1))),
      ("structures/barrel-vault/barrel-vault-deformed.png", dict(keep=(0.22, 0, 1, 1)))], "stack"),
    ("Natural vibration · beams, shells, solids", "Every mode", T.PURPLE,
     "A concrete tank's first hundred natural modes, from one analysis, each on its own copy.",
     [("structures/tank-modes/tank-modes.png", dict(cover=True))], "row"),
    ("Time history · response spectrum", "Earthquakes", T.RED,
     "Five towers of 100 storeys and more, shaken by the El Centro earthquake record.",
     [((Q % (n, n), t), TOWER) for n, t in [("bundled-tube", 13.8), ("braced-tube", 4.1),
                                            ("buttressed-core", 3.7), ("outrigger-tower", 14.0),
                                            ("diagrid-tower", 13.5)]], "row"),
    ("Pushover · plasticity", "Past the elastic limit", T.ORANGE,
     "A concrete building pushed sideways until it yields. Each ball is a plastic hinge.",
     [("pushover/irregular-building-pushover/irregular-building-pushover.png", dict())], "row"),
    ("Footfall vibration", "Feel every footstep", T.GREEN,
     "How much a helical steel stair shakes when people walk on it, after SCI P354.",
     [("footfall/helical-stair/footfall-helical-stair-response.png", dict(keep=(0.3, 0, 1, 1)))], "row"),
    ("3D concrete printing · buckling", "Print it before it buckles", T.BLUE,
     "A twisting column of fresh concrete, checked for buckling layer by layer as it is printed.",
     [("3d-printing/twisted-column/twisted-column-buckled.png", dict())], "row"),
    ("Section optimiser", "Every member sized", T.PURPLE,
     "A 20 m grid of steel beams, every element sized on its own: a colour for each section.",
     [("section-optimiser/beam-grid/beam-grid-sections.png", dict())], "row"),
    ("Design explorer", "Explore every option", T.GREEN,
     "108 footbridges in your browser: filter them, sort them, open the best in 3D.",
     [("explorer/footbridge-study/explorer-study-poster.png", dict(window=True))], "explorer"),
    ("Topology optimisation", "Only the material it needs", T.RED,
     "A node where nine truss members meet, found in two materials by Multi-Material Topology.",
     [(("topology-optimisation/truss-node/truss-node-multi-material-run.mp4", 11.4), dict())], "row"),
    ("Principal stress lines", "See the forces flow", T.ORANGE,
     "Slabs on one column, ribbed along their own stress lines, as Nervi built them.",
     [("principal-stress-lines/nervi-umbrella/nervi-umbrella-below.png", dict(cover=True))], "row"),
]
# the cover's mosaic: (poster index, which of its pictures, anchor of its cover cut)
MOSAIC = [(1, 0, 0.5), (2, 3, 0.1), (9, 0, 0.5), (8, 0, 0.5), (4, 0, 0.5), (5, 0, 0.4), (0, 1, 0.5),
          (3, 0, 0.5), (6, 0, 0.5)]
# the Explorer's last frame, as explorer.py recorded it, and its two panels in it (pixels)
EXPLORER_FRAMES = os.path.join(T.HERE, "..", "out", "explorer", "frames")
AXES_PANEL, GALLERY_PANEL, DESIGN_PANEL = (17, 86, 1382, 602), (17, 622, 1382, 1063), (1399, 86, 1903, 1063)


def px(v):
    return int(round(v * K))


# ------------------------------------------------------------------------------------------- pictures

def picture(src, keep=(0, 0, 1, 1), cover=False, window=False, **_):
    """The picture, its near-white ground made the teaser's grey, cut to its main parts."""
    if isinstance(src, tuple):
        png = subprocess.check_output([T.FF, "-v", "error", "-ss", "%.2f" % src[1], "-i",
                                       os.path.join(T.MARKETING, src[0]), "-frames:v", "1",
                                       "-f", "image2pipe", "-vcodec", "png", "-"])
        im = Image.open(io.BytesIO(png)).convert("RGB")
    else:
        im = Image.open(os.path.join(T.MARKETING, src)).convert("RGB")
    if window:
        return im
    w, h = im.size
    im = im.crop((int(keep[0] * w), int(keep[1] * h), int(keep[2] * w), int(keep[3] * h)))
    a = np.asarray(im)
    a = np.where(a >= T.BG - 3, T.BG, a).astype(np.uint8)
    if cover:
        return Image.fromarray(a)
    small = a[::4, ::4].astype(np.int16)
    q = T._main_part(np.abs(small - T.BG).max(axis=2) > 18)
    if q is None:
        return Image.fromarray(a)
    x0, y0, x1, y1 = (v * 4 for v in q)
    pad = int(0.04 * max(x1 - x0, y1 - y0))
    return Image.fromarray(a).crop((max(0, x0 - pad), max(0, y0 - pad), min(a.shape[1], x1 + pad),
                                    min(a.shape[0], y1 + pad)))


def cover_cut(im, w, h, anchor=0.5):
    """im cut to w:h, about its middle across and at `anchor` down, then sized to w x h."""
    iw, ih = im.size
    if iw / ih > w / h:
        nw = int(ih * w / h)
        im = im.crop(((iw - nw) // 2, 0, (iw - nw) // 2 + nw, ih))
    else:
        nh = int(iw * h / w)
        top = int((ih - nh) * anchor)
        im = im.crop((0, top, iw, top + nh))
    return im.resize((w, h), Image.LANCZOS)


def rounded(im, r):
    m = Image.new("L", im.size, 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius=r, fill=255)
    out = im.convert("RGBA")
    out.putalpha(m)
    return out


def shadow(size, r, blur, drop, alpha):
    w, h = size
    pad = blur * 3
    sh = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
    ImageDraw.Draw(sh).rounded_rectangle((pad, pad + drop, pad + w, pad + h + drop), radius=r, fill=int(255 * alpha))
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    layer = Image.new("RGBA", sh.size, T.INK + (0,))
    layer.putalpha(sh)
    return layer, pad


def draw_window(poster, im, x, y):
    sh, pad = shadow(im.size, px(14), px(16), px(10), 0.25)
    poster.alpha_composite(sh, (x - pad, y - pad))
    poster.alpha_composite(rounded(im, px(14)), (x, y))


def explorer_windows(poster, box):
    """The Explorer as three windows: its parallel axes, its gallery under them, and the design opened
    over their corner."""
    import json
    log = json.load(open(os.path.join(EXPLORER_FRAMES, "log.json")))
    frame = Image.open(os.path.join(EXPLORER_FRAMES, log[-1][0])).convert("RGB")
    x0, y0, x1, y1 = box
    axes = frame.crop(AXES_PANEL)
    s = (x1 - x0) * 0.92 / axes.width
    axes = axes.resize((round(axes.width * s), round(axes.height * s)), Image.LANCZOS)
    gallery = frame.crop(GALLERY_PANEL)
    s = (x1 - x0) * 0.66 / gallery.width
    gallery = gallery.resize((round(gallery.width * s), round(gallery.height * s)), Image.LANCZOS)
    design = frame.crop(DESIGN_PANEL)
    s = (y1 - y0) * 0.66 / design.height
    design = design.resize((round(design.width * s), round(design.height * s)), Image.LANCZOS)
    draw_window(poster, axes, x0, y0 + px(6))
    draw_window(poster, gallery, x0, y0 + px(6) + axes.height + px(28))
    draw_window(poster, design, x1 - design.width - px(6), y1 - design.height - px(10))


def place_all(poster, pics, box, layout):
    """Several pictures in the box, side by side or one above the other, each in an equal share."""
    if layout == "explorer":
        return explorer_windows(poster, box)
    x0, y0, x1, y1 = box
    n, gap = len(pics), px(24)
    for i, (im, how) in enumerate(pics):
        if layout == "stack":
            h = (y1 - y0 - gap * (n - 1)) / n
            cell = (x0, int(y0 + i * (h + gap)), x1, int(y0 + i * (h + gap) + h))
        else:
            w = (x1 - x0 - gap * (n - 1)) / n
            cell = (int(x0 + i * (w + gap)), y0, int(x0 + i * (w + gap) + w), y1)
        place(poster, im, cell, **how)


def place(poster, im, box, cover=False, window=False, anchor=0.5, **_):
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    if cover:
        tile = rounded(cover_cut(im, bw, bh, anchor), px(18))
        poster.alpha_composite(tile, (x0, y0))
        return
    s = min(bw / im.width, bh / im.height) * (0.94 if window else 1)
    im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    x, y = x0 + (bw - im.width) // 2, y0 + (bh - im.height) // 2
    if window:
        draw_window(poster, im, x, y)
    else:
        poster.alpha_composite(im.convert("RGBA"), (x, y))


# ------------------------------------------------------------------------------------------- type

def wrap(text, f, width, tracking=0.0):
    lines, line = [], ""
    for word in text.split():
        t = (line + " " + word).strip()
        if line and T.text_width(t, f, tracking) > width:
            lines.append(line)
            line = word
        else:
            line = t
    return lines + [line]


def headline(poster, text, colour, x, top, size, width):
    """The display type, wrapped, the last line ending in the coloured dot; returns its bottom."""
    f = T.font(size * K, 900)
    lines = wrap(text, f, width * K - T.text_width(".", f), -0.035)
    y = top
    for i, line in enumerate(lines):
        lay, base = T.headline_layer(line, K, colour, size=size, dot=i == len(lines) - 1)
        poster.alpha_composite(lay, (px(x - 3), px(y)))
        y += size * 1.0
    return y + size * 0.2


def caption(poster, text, x, top, size, width):
    f = T.font(size * K, 500)
    d = ImageDraw.Draw(poster)
    y = top
    for line in wrap(text, f, width * K):
        d.text((px(x), px(y + size)), line, font=f, fill=INK2, anchor="ls")
        y += size * 1.38
    return y


def footer(poster):
    """The brand at the bottom left, the address at the right, the rainbow along the foot."""
    mark = T.logo(px(62))
    word, wb = T.headline_layer("Alpaca4d", K, T.GREEN, size=40, dot=False)
    base = 1286
    poster.alpha_composite(mark, (px(M), px(base + 8) - mark.height))
    poster.alpha_composite(word, (px(M) + mark.width + px(14), px(base) - int(wb)))
    f = T.font(26 * K, 600, mono=True)
    url = "alpaca4d.github.io"
    T.draw_text(ImageDraw.Draw(poster), px(BW - M) - T.text_width(url, f, 0.02), px(base - 4), url, f, T.INK3, 0.02)
    bar = Image.fromarray(T.rainbow(px(BW), px(12)))
    poster.paste(bar, (0, px(BH - 12)))


def blank():
    return Image.new("RGBA", (px(BW), px(BH)), (T.BG, T.BG, T.BG, 255))


def eyebrow(poster, text, number=None, colour=T.INK3, size=22):
    lay, base = T.eyebrow_layer(text, K, number, colour, size=size)
    poster.alpha_composite(lay, (px(M), px(84)))


# ------------------------------------------------------------------------------------------- posters

def capability(i, spec, pics):
    eb, head, colour, cap, _, layout = spec
    p = blank()
    eyebrow(p, eb, i + 1, colour)
    y = headline(p, head, colour, M, 138, 92, BW - 2 * M)
    y = caption(p, cap, M, y + 4, 33, BW - 2 * M - 40)
    place_all(p, pics, (px(M), px(y + 36), px(BW - M), px(1196)), layout)
    footer(p)
    return p


def cover(pictures):
    p = blank()
    eyebrow(p, "OpenSees for Grasshopper")
    y = headline(p, "Everything Alpaca4d can do", T.GREEN, M, 138, 104, BW - 2 * M)
    y = caption(p, "Ten things to ask your structure, all in Grasshopper, on your own computer.",
                M, y + 4, 33, BW - 2 * M - 40)
    top, bottom, gap = y + 36, 1196, 16
    tw = (BW - 2 * M - 2 * gap) / 3
    th = (bottom - top - 2 * gap) / 3
    for n, (idx, which, anchor) in enumerate(MOSAIC):
        r, c = divmod(n, 3)
        x0, y0 = M + c * (tw + gap), top + r * (th + gap)
        box = (px(x0), px(y0), px(x0 + tw), px(y0 + th))
        im = pictures[idx][which][0]
        tile = rounded(cover_cut(im, box[2] - box[0], box[3] - box[1], anchor), px(16))
        p.alpha_composite(tile, box[:2])
        ImageDraw.Draw(p).rounded_rectangle(box, radius=px(16), outline=LINE, width=px(1.5))
    footer(p)
    return p


def end():
    """The alpaca, the name, the line, and where to try it."""
    p = blank()
    alp = T.logo(px(560))
    p.alpha_composite(alp, ((px(BW) - alp.width) // 2, px(150)))
    word, wb = T.headline_layer("Alpaca4d", K, T.GREEN, size=150)
    p.alpha_composite(word, ((px(BW) - word.width) // 2, px(870) - int(wb)))
    d = ImageDraw.Draw(p)
    f = T.font(54 * K, 800)
    for n, line in enumerate(["Structural analysis", "at the speed of design."]):
        d.text((px(BW / 2), px(960 + n * 66)), line, font=f, fill=T.INK, anchor="ms")
    fb = T.font(36 * K, 800)
    label = "Try it free"
    bw, bh = T.text_width(label, fb) + px(2 * 40), px(84)
    bx, by = (px(BW) - bw) // 2, px(1080)
    d.rounded_rectangle((bx, by + px(6), bx + bw, by + bh + px(6)), radius=bh / 2, fill=T.INK)
    d.rounded_rectangle((bx, by, bx + bw, by + bh), radius=bh / 2, fill=T.GREEN, outline=T.INK, width=px(3))
    d.text((bx + bw / 2, by + bh / 2 + px(2)), label, font=fb, fill=T.INK, anchor="mm")
    fm = T.font(30 * K, 600, mono=True)
    d.text((px(BW / 2), px(1236)), "alpaca4d.github.io", font=fm, fill=T.INK, anchor="ms")
    fr = T.font(20 * K, 600, mono=True)
    free = "FREE FOR MODELS UP TO 50 ELEMENTS"
    T.draw_text(d, (px(BW) - T.text_width(free, fr, 0.16)) / 2, px(1290), free, fr, T.INK3, 0.16)
    bar = Image.fromarray(T.rainbow(px(BW), px(12)))
    p.paste(bar, (0, px(BH - 12)))
    return p


def main():
    T.fetch_fonts()
    os.makedirs(OUT, exist_ok=True)
    slug = lambda s: s.lower().replace(" ", "-").replace(",", "")
    pictures = [[(picture(src, **how), how) for src, how in spec[4]] for spec in POSTERS]
    pages = [("00-cover", cover(pictures))]
    for i, spec in enumerate(POSTERS):
        pages.append(("%02d-%s" % (i + 1, slug(spec[1])), capability(i, spec, pictures[i])))
    pages.append(("%02d-try-it-free" % (len(POSTERS) + 1), end()))
    rgb = []
    for name, page in pages:
        im = page.convert("RGB")
        im.save(os.path.join(OUT, name + ".png"), optimize=True)
        rgb.append(im)
        print(os.path.join(OUT, name + ".png"))
    pdf = os.path.join(OUT, "alpaca4d-carousel.pdf")
    rgb[0].save(pdf, save_all=True, append_images=rgb[1:], resolution=200, quality=90)
    print(pdf)


if __name__ == "__main__":
    main()
