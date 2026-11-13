"""Trims run.sh's captures and makes them into pictures and animations with ffmpeg, for publish.sh.

    python3 post.py trim in.png out.png [margin] [max_width] [grow] # cut to what is drawn
    python3 post.py anim frames_dir out_base [margin] [width] [fps] [hold]
                                                                    # out_base.gif (+ out_base.mp4 with MP4=1)
    python3 post.py pagegif frames_dir out.gif [margin] [width] [fps]
                                                                    # a lighter GIF, for a page
    python3 post.py animcharts frames_dir charts_dir out_base [margin] [width] [fps] [hold]
                                                                    # the same, each frame's charts under it
    python3 post.py besidecharts frames_dir charts_dir out_base [margin] [height] [fps] [hold]
                                                                    # for a tower: its charts beside it
    python3 post.py pagemp4 in.mp4 out.mp4 [width]                  # a lighter video, for a page
    python3 post.py sidebyside frames_a charts_a end_a label_a frames_b charts_b end_b label_b out_base [width] [fps] [hold]
                                                                    # two prints at the same moment, side by side
    python3 post.py pair out.png left.png right.png [height]        # side by side, both as tall
    python3 post.py stack out.png top.png bottom.png [gap]          # bottom scaled to top's width
"""
import glob, os, re, shutil, subprocess, sys, tempfile

FF = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
# GIFs only (user, 2026-11-13). MP4=1 makes the animations write an MP4 beside each GIF again.
MP4 = os.environ.get("MP4") == "1"


def made(base):
    return base + ".gif" + (", " + base + ".mp4" if MP4 else "")
# The pictures' background: a light grey, 251, which Shots.cs fills the viewport with. Rhino captures it
# a shade off in one channel or another; anything that light is made exactly that grey, and for finding
# what is drawn, white.
BG = 251
BG_HEX = "0x%02X%02X%02X" % (BG, BG, BG)
_LIGHT = "if(gte(val,%d),%%d,val)" % (BG - 3)
BACKGROUND = "format=rgb24,lutrgb=r='{0}':g='{0}':b='{0}'".format(_LIGHT % BG)
WHITE = "format=rgb24,lutrgb=r='{0}':g='{0}':b='{0}'".format(_LIGHT % 255)


def run(args):
    return subprocess.run([FF, "-hide_banner", "-y"] + args, capture_output=True, text=True)


def size(path):
    out = subprocess.run(["sips", "-g", "pixelWidth", "-g", "pixelHeight", path], capture_output=True, text=True).stdout
    return int(re.search(r"pixelWidth: (\d+)", out).group(1)), int(re.search(r"pixelHeight: (\d+)", out).group(1))


def box(inputs, grow=0, pre=""):
    """The box of what is drawn, over every input: x0, y0, x1, y1. cropdetect keeps a row or a column
    only when its average is at least 1, so a thin line across a wide picture - a load arrow - is
    lost; grow thickens what is drawn by 2 px a pass first, for the box only. pre is a filter run
    first, such as a crop to look in one part of the picture only."""
    args = []
    if len(inputs) == 1:
        args = ["-i", inputs[0]]
    else:
        args = ["-framerate", "10", "-i", inputs]
    thicken = ",dilation" * grow
    r = run(args + ["-vf", (pre + "," if pre else "") + WHITE + ",negate" + thicken +
                    ",cropdetect=limit=0:round=1:reset=0:skip=0", "-f", "null", "-"])
    found = re.findall(r"x1:(\d+) x2:(\d+) y1:(\d+) y2:(\d+)", r.stderr)
    if not found:
        raise SystemExit("no box: " + r.stderr[-800:])
    x0 = min(int(f[0]) for f in found); x1 = max(int(f[1]) for f in found)
    y0 = min(int(f[2]) for f in found); y1 = max(int(f[3]) for f in found)
    return x0, y0, x1, y1


def crop_filter(b, w, h, margin):
    x0, y0, x1, y1 = b
    x0, y0 = max(0, x0 - margin), max(0, y0 - margin)
    x1, y1 = min(w - 1, x1 + margin), min(h - 1, y1 + margin)
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    cw -= cw % 2; ch -= ch % 2
    return "crop=%d:%d:%d:%d" % (cw, ch, x0, y0), cw, ch


def trim(src, dst, margin=40, max_width=0, grow=0):
    w, h = size(src)
    f, cw, ch = crop_filter(box([src], grow), w, h, margin)
    vf = BACKGROUND + "," + f
    if max_width and cw > max_width:
        vf += ",scale=%d:-2:flags=lanczos" % max_width
    r = run(["-i", src, "-vf", vf, "-frames:v", "1", "-update", "1", dst])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("%s -> %s (%s)" % (os.path.basename(src), dst, "x".join(map(str, size(dst)))))


def anim(frames, base, margin=40, width=900, fps=12, hold=2):
    files = sorted(glob.glob(os.path.join(frames, "*.png")))
    w, h = size(files[0])
    pattern = os.path.join(frames, "%03d.png")
    f, cw, ch = crop_filter(box(pattern), w, h, margin)
    chain = BACKGROUND + "," + f + ",scale=%d:-2:flags=lanczos" % width
    if hold:
        chain += ",tpad=stop_mode=clone:stop_duration=%d" % hold
    r = run(["-framerate", str(fps), "-i", pattern, "-vf", chain + ",split[a][b];[a]palettegen=stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a",
             "-loop", "0", base + ".gif"])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    if MP4:
        r = run(["-framerate", str(fps), "-i", pattern, "-vf", chain.replace("scale=%d:-2" % width, "scale=%d:-2" % max(width * 2, 1600)),
                 "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-movflags", "+faststart", base + ".mp4"])
        if r.returncode:
            raise SystemExit(r.stderr[-800:])
    print("%d frames -> %s" % (len(files), made(base)))


def pagegif(frames, dst, margin=40, width=600, fps=15, budget=4500):
    """A lighter GIF, for a web page: 64 colours in an ordered dither, which compresses far better than
    anim's error diffusion - every brick's edge drawn makes a heavy GIF - and looks the same at that size;
    made narrower, 100 px at a time, until it is under budget KB."""
    files = sorted(glob.glob(os.path.join(frames, "*.png")))
    w, h = size(files[0])
    pattern = os.path.join(frames, "%03d.png")
    f, cw, ch = crop_filter(box(pattern), w, h, margin)
    while True:
        chain = BACKGROUND + "," + f + ",scale=%d:-2:flags=lanczos" % width
        r = run(["-framerate", str(fps), "-i", pattern, "-vf", chain + ",split[a][b];[a]palettegen=max_colors=64:stats_mode=full[p];"
                 "[b][p]paletteuse=dither=bayer:bayer_scale=3", "-loop", "0", dst])
        if r.returncode:
            raise SystemExit(r.stderr[-800:])
        if os.path.getsize(dst) <= budget * 1024 or width <= 400:
            break
        width -= 100
    print("%d frames -> %s (%d px, %d KB)" % (len(files), dst, width, os.path.getsize(dst) // 1024))


def pagemp4(src, dst, width=900):
    """A video lighter for a web page: width wide, at a quality that still shows every brick's edge."""
    r = run(["-i", src, "-vf", "scale=%d:-2:flags=lanczos" % width, "-c:v", "libx264", "-preset", "slow", "-crf", "26",
             "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", dst])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("%s -> %s (%d KB)" % (os.path.basename(src), dst, os.path.getsize(dst) // 1024))


def animcharts(frames, charts, base, margin=40, width=900, fps=10, hold=2, grow=0, split=0.0):
    """anim, with the charts of each frame under it, from a folder of their own with the same names:
    the frames cut to what is drawn over all of them, the charts scaled to the same width. grow keeps
    a thin line, a load arrow, inside the cut, as trim's does. split, a share of the frames' height,
    cuts them in two there - the model above, a legend under it - and cuts each part to what is drawn
    in it, so that no empty band is left between the two."""
    files = sorted(glob.glob(os.path.join(frames, "*.png")))
    w, h = size(files[0])
    pattern, under = os.path.join(frames, "%03d.png"), os.path.join(charts, "%03d.png")
    if split:
        cut = int(h * split)
        ft, tw, th = crop_filter(box(pattern, grow, "crop=%d:%d:0:0" % (w, cut)), w, cut, margin)
        fb, bw, bh = crop_filter(box(pattern, 0, "crop=%d:%d:0:%d" % (w, h - cut, cut)), w, h - cut, margin)
        wide_ = max(tw, bw)
        pad = "pad=%d:ih:(ow-iw)/2:0:color=%s" % (wide_, BG_HEX)
        f = ("split[t][b];[t]crop=%d:%d:0:0,%s,%s[t2];[b]crop=%d:%d:0:%d,%s,%s[b2];[t2][b2]vstack" %
             (w, cut, ft, pad, w, h - cut, cut, fb, pad))
    else:
        f, cw, ch = crop_filter(box(pattern, grow), w, h, margin)
    for kind, wide in (("gif", width), ("mp4", max(width * 2, 1600)))[:2 if MP4 else 1]:
        graph = ("[0]" + BACKGROUND + "," + f + ",scale=%d:-2:flags=lanczos[a];" % wide +
                 "[1]format=rgb24,scale=%d:-2:flags=lanczos[b];[a][b]vstack" % wide)
        if hold:
            graph += ",tpad=stop_mode=clone:stop_duration=%d" % hold
        inputs = ["-framerate", str(fps), "-i", pattern, "-framerate", str(fps), "-i", under]
        if kind == "gif":
            graph += ",split[p][q];[p]palettegen=stats_mode=full[pal];[q][pal]paletteuse=dither=sierra2_4a"
            r = run(inputs + ["-filter_complex", graph, "-loop", "0", base + ".gif"])
        else:
            r = run(inputs + ["-filter_complex", graph, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18",
                              "-movflags", "+faststart", base + ".mp4"])
        if r.returncode:
            raise SystemExit(r.stderr[-800:])
    print("%d frames with their charts -> %s" % (len(files), made(base)))


def besidecharts(frames, charts, base, margin=40, height=900, fps=10, hold=2):
    """animcharts for something tall, a tower: each frame's charts beside it, on the right, rather than
    under it, both made height tall, the frames cut to what is drawn over all of them."""
    files = sorted(glob.glob(os.path.join(frames, "*.png")))
    w, h = size(files[0])
    pattern, beside = os.path.join(frames, "%03d.png"), os.path.join(charts, "%03d.png")
    f, cw, ch = crop_filter(box(pattern), w, h, margin)
    for kind, tall in (("gif", int(height * 0.8)), ("mp4", max(int(height * 1.4), 1100)))[:2 if MP4 else 1]:
        graph = ("[0]" + BACKGROUND + "," + f + ",scale=-2:%d:flags=lanczos[a];" % tall +
                 "[1]format=rgb24,scale=-2:%d:flags=lanczos[b];[a][b]hstack" % tall)
        if hold:
            graph += ",tpad=stop_mode=clone:stop_duration=%d" % hold
        inputs = ["-framerate", str(fps), "-i", pattern, "-framerate", str(fps), "-i", beside]
        if kind == "gif":
            graph += ",split[p][q];[p]palettegen=stats_mode=full[pal];[q][pal]paletteuse=dither=sierra2_4a"
            r = run(inputs + ["-filter_complex", graph, "-loop", "0", base + ".gif"])
        else:
            graph += ",pad=ceil(iw/2)*2:ceil(ih/2)*2"
            r = run(inputs + ["-filter_complex", graph, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23",
                              "-movflags", "+faststart", base + ".mp4"])
        if r.returncode:
            raise SystemExit(r.stderr[-800:])
    print("%d frames with their charts beside them -> %s" % (len(files), made(base)))


def sidebyside(a, b, base, width=1200, fps=12, hold=2):
    """Two prints side by side at the same moment, each with its label over it and its charts under it:
    a and b are (frames dir, charts dir, the minutes its last frame shows, label picture or "-"). Each frame
    of a is paired with b's frame of the same time, b's last held if it is done sooner; both are cut to what
    is drawn over all of them, so that they show at one scale. The MP4 has every frame at fps; the GIF, for
    posts, every third at half that, width wide, so that it stays light."""
    fa, ca, enda, la = a
    fb, cb, endb, lb = b
    A = sorted(glob.glob(os.path.join(fa, "*.png")))
    B = sorted(glob.glob(os.path.join(fb, "*.png")))
    tmp = tempfile.mkdtemp()
    try:
        # The four sequences, every frame and every third, numbered from 0.
        sets = {"all": range(len(A)), "third": range(2, len(A), 3)}
        for name, picks in sets.items():
            for d in ("a", "ac", "b", "bc"):
                os.makedirs(os.path.join(tmp, name, d))
            for i, f in enumerate(picks):
                t = (f + 1.0) / len(A) * float(enda)
                g = min(len(B) - 1, max(0, int(round(t / float(endb) * len(B))) - 1))
                for src, d in ((A[f], "a"), (os.path.join(ca, os.path.basename(A[f])), "ac"),
                               (B[g], "b"), (os.path.join(cb, os.path.basename(B[g])), "bc")):
                    shutil.copy(src, os.path.join(tmp, name, d, "%03d.png" % i))
        w, h = size(A[0])
        x0, y0, x1, y1 = box(os.path.join(tmp, "all", "a", "%03d.png"))
        u0, v0, u1, v1 = box(os.path.join(tmp, "all", "b", "%03d.png"))
        f, cw, ch = crop_filter((min(x0, u0), min(y0, v0), max(x1, u1), max(y1, v1)), w, h, 40)
        for kind, name, wide, rate in (("gif", "third", width, max(1, fps // 2)), ("mp4", "all", max(width * 2, 1600), fps))[:2 if MP4 else 1]:
            half = wide // 2
            half -= half % 2
            inputs = []
            for d in ("a", "ac", "b", "bc"):
                inputs += ["-framerate", str(rate), "-i", os.path.join(tmp, name, d, "%03d.png")]
            graph = ""
            for i, side in ((0, "A"), (2, "B")):
                graph += ("[%d]" % i + BACKGROUND + "," + f + ",scale=%d:-2:flags=lanczos[f%s];" % (half, side) +
                          "[%d]format=rgb24,scale=%d:-2:flags=lanczos[c%s];" % (i + 1, half, side))
            if la != "-" and lb != "-":
                inputs += ["-loop", "1", "-i", la, "-loop", "1", "-i", lb]
                graph += "[4]format=rgb24,scale=%d:-2:flags=lanczos[lA];[5]format=rgb24,scale=%d:-2:flags=lanczos[lB];" % (half, half)
                graph += "[lA][fA][cA]vstack=3:shortest=1[A];[lB][fB][cB]vstack=3:shortest=1[B];[A][B]hstack"
            else:
                graph += "[fA][cA]vstack[A];[fB][cB]vstack[B];[A][B]hstack"
            if hold:
                graph += ",tpad=stop_mode=clone:stop_duration=%d" % hold
            if kind == "gif":
                graph += ",split[p][q];[p]palettegen=stats_mode=full[pal];[q][pal]paletteuse=dither=sierra2_4a"
                r = run(inputs + ["-filter_complex", graph, "-loop", "0", base + ".gif"])
            else:
                r = run(inputs + ["-filter_complex", graph, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18",
                                  "-movflags", "+faststart", base + ".mp4"])
            if r.returncode:
                raise SystemExit(r.stderr[-800:])
    finally:
        shutil.rmtree(tmp)
    print("%d frames side by side -> %s" % (len(A), made(base)))


def stack(dst, top, bottom, gap=40):
    tw, th = size(top)
    r = run(["-i", top, "-i", bottom, "-filter_complex",
             "[1]scale=%d:-2:flags=lanczos[b];[0]pad=iw:ih+%d:0:0:%s[t];[t][b]vstack" % (tw, gap, BG_HEX), "-frames:v", "1", "-update", "1", dst])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("stacked -> %s (%s)" % (dst, "x".join(map(str, size(dst)))))


def pair(dst, left, right, height=1400, gap=60):
    """The canvas on the left, the result on the right, both made height tall, on the background grey."""
    r = run(["-i", left, "-i", right, "-filter_complex",
             "[0]scale=-2:%d:flags=lanczos,pad=iw+%d:ih:0:0:%s[l];[1]scale=-2:%d:flags=lanczos[r];[l][r]hstack" % (height, gap, BG_HEX, height),
             "-frames:v", "1", "-update", "1", dst])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("paired -> %s (%s)" % (dst, "x".join(map(str, size(dst)))))


if __name__ == "__main__":
    cmd, a = sys.argv[1], sys.argv[2:]
    if cmd == "trim":
        trim(a[0], a[1], int(a[2]) if len(a) > 2 else 40, int(a[3]) if len(a) > 3 else 0, int(a[4]) if len(a) > 4 else 0)
    elif cmd == "anim":
        anim(a[0], a[1], int(a[2]) if len(a) > 2 else 40, int(a[3]) if len(a) > 3 else 900, int(a[4]) if len(a) > 4 else 12,
             int(a[5]) if len(a) > 5 else 2)
    elif cmd == "pagegif":
        pagegif(a[0], a[1], int(a[2]) if len(a) > 2 else 40, int(a[3]) if len(a) > 3 else 600, int(a[4]) if len(a) > 4 else 15)
    elif cmd == "animcharts":
        animcharts(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 40, int(a[4]) if len(a) > 4 else 900,
                   int(a[5]) if len(a) > 5 else 10, int(a[6]) if len(a) > 6 else 2, int(a[7]) if len(a) > 7 else 0,
                   float(a[8]) if len(a) > 8 else 0.0)
    elif cmd == "besidecharts":
        besidecharts(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 40, int(a[4]) if len(a) > 4 else 900,
                     int(a[5]) if len(a) > 5 else 10, int(a[6]) if len(a) > 6 else 2)
    elif cmd == "pagemp4":
        pagemp4(a[0], a[1], int(a[2]) if len(a) > 2 else 900)
    elif cmd == "sidebyside":
        sidebyside(a[0:4], a[4:8], a[8], int(a[9]) if len(a) > 9 else 1200, int(a[10]) if len(a) > 10 else 12,
                   int(a[11]) if len(a) > 11 else 2)
    elif cmd == "pair":
        pair(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 1400)
    elif cmd == "stack":
        stack(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 40)
