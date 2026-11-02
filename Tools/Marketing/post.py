"""Trims run.sh's captures and makes them into pictures and animations with ffmpeg, for publish.sh.

    python3 post.py trim in.png out.png [margin] [max_width] [grow] # cut to what is drawn, on white
    python3 post.py anim frames_dir out_base [margin] [width] [fps] [hold]
                                                                    # out_base.gif + out_base.mp4
    python3 post.py pair out.png left.png right.png [height]        # side by side, both as tall
    python3 post.py stack out.png top.png bottom.png [gap]          # bottom scaled to top's width
"""
import glob, os, re, shutil, subprocess, sys

FF = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
# Rhino's capture background is 255,254,255: made pure white.
WHITE = "format=rgb24,lutrgb=r='if(gte(val,250),255,val)':g='if(gte(val,250),255,val)':b='if(gte(val,250),255,val)'"


def run(args):
    return subprocess.run([FF, "-hide_banner", "-y"] + args, capture_output=True, text=True)


def size(path):
    out = subprocess.run(["sips", "-g", "pixelWidth", "-g", "pixelHeight", path], capture_output=True, text=True).stdout
    return int(re.search(r"pixelWidth: (\d+)", out).group(1)), int(re.search(r"pixelHeight: (\d+)", out).group(1))


def box(inputs, grow=0):
    """The box of what is drawn, over every input: x0, y0, x1, y1. cropdetect keeps a row or a column
    only when its average is at least 1, so a thin line across a wide picture - a load arrow - is
    lost; grow thickens what is drawn by 2 px a pass first, for the box only."""
    args = []
    if len(inputs) == 1:
        args = ["-i", inputs[0]]
    else:
        args = ["-framerate", "10", "-i", inputs]
    thicken = ",dilation" * grow
    r = run(args + ["-vf", WHITE + ",negate" + thicken + ",cropdetect=limit=0:round=1:reset=0:skip=0", "-f", "null", "-"])
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
    vf = WHITE + "," + f
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
    chain = WHITE + "," + f + ",scale=%d:-2:flags=lanczos" % width
    if hold:
        chain += ",tpad=stop_mode=clone:stop_duration=%d" % hold
    r = run(["-framerate", str(fps), "-i", pattern, "-vf", chain + ",split[a][b];[a]palettegen=stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a",
             "-loop", "0", base + ".gif"])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    r = run(["-framerate", str(fps), "-i", pattern, "-vf", chain.replace("scale=%d:-2" % width, "scale=%d:-2" % max(width * 2, 1600)),
             "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-movflags", "+faststart", base + ".mp4"])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("%d frames -> %s.gif, %s.mp4" % (len(files), base, base))


def stack(dst, top, bottom, gap=40):
    tw, th = size(top)
    r = run(["-i", top, "-i", bottom, "-filter_complex",
             "[1]scale=%d:-2:flags=lanczos[b];[0]pad=iw:ih+%d:0:0:white[t];[t][b]vstack" % (tw, gap), "-frames:v", "1", "-update", "1", dst])
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    print("stacked -> %s (%s)" % (dst, "x".join(map(str, size(dst)))))


def pair(dst, left, right, height=1400, gap=60):
    """The canvas on the left, the result on the right, both made height tall, on white."""
    r = run(["-i", left, "-i", right, "-filter_complex",
             "[0]scale=-2:%d:flags=lanczos,pad=iw+%d:ih:0:0:white[l];[1]scale=-2:%d:flags=lanczos[r];[l][r]hstack" % (height, gap, height),
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
    elif cmd == "pair":
        pair(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 1400)
    elif cmd == "stack":
        stack(a[0], a[1], a[2], int(a[3]) if len(a) > 3 else 40)
