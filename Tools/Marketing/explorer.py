#!/usr/bin/env python3
"""The Alpaca4d Explorer in use, as a video: the footbridge sample study filtered axis by axis, the
gallery sorted, a design opened and turned round.

    python3 explorer.py        # marketing/explorer/footbridge-study/explorer-study.mp4 and -poster.png

Drives the Explorer page in Chrome, headless, through Playwright (pip install playwright pillow numpy),
from a local server of the website repository next to this one, one screenshot per step of each drag.
A headless screenshot has no mouse pointer, so the pointer is drawn after, where the mouse was; and a
camera moves over the screenshots: close on the axes while they are filtered, out to the gallery,
in on the design that opens.
"""
import http.server, json, os, shutil, socketserver, subprocess, sys, threading
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SITE = os.path.abspath(os.path.join(HERE, "..", "..", "..", "alpaca4d.github.io"))
OUT = os.path.join(SITE, "marketing", "explorer", "footbridge-study")
WORK = os.path.join(HERE, "out", "explorer")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
FF = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
VW, VH, DPR = 1600, 900, 1.2          # the page, in CSS pixels; screenshots 1920 x 1080
FPS = 30


def ease(p):
    p = min(max(p, 0.0), 1.0)
    return 4 * p ** 3 if p < 0.5 else 1 - (-2 * p + 2) ** 3 / 2


def serve():
    """The website repository on a free port, for as long as this runs."""
    class Quiet(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *a, **k):
            super().__init__(*a, directory=SITE, **k)

        def log_message(self, *a):
            pass
    httpd = socketserver.TCPServer(("127.0.0.1", 0), Quiet)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd


def record(frames_dir):
    """Screenshots of every step, and where the mouse and the camera were at each."""
    from playwright.sync_api import sync_playwright
    httpd = serve()
    url = "http://127.0.0.1:%d/explorer/?study=sample/footbridge/" % httpd.server_address[1]
    shutil.rmtree(frames_dir, ignore_errors=True)
    os.makedirs(frames_dir)
    log = []                                   # per frame: [file, mouse x, mouse y, shot]
    with sync_playwright() as p:
        b = p.chromium.launch(executable_path=CHROME, headless=True)
        pg = b.new_page(viewport={"width": VW, "height": VH}, device_scale_factor=DPR, color_scheme="light")
        pg.goto(url)
        pg.wait_for_timeout(2500)
        pg.keyboard.press("Escape")
        pg.wait_for_timeout(800)
        mouse = [VW * 0.62, VH * 0.42]
        pg.mouse.move(*mouse)

        def snap(shot, hold=1):
            name = "%04d.png" % len(log)
            pg.screenshot(path=os.path.join(frames_dir, name))
            for _ in range(hold):
                log.append([name, mouse[0], mouse[1], shot])

        def glide(x, y, n, shot):
            x0, y0 = mouse
            for i in range(1, n + 1):
                f = ease(i / n)
                mouse[:] = [x0 + (x - x0) * f, y0 + (y - y0) * f]
                pg.mouse.move(*mouse)
                pg.wait_for_timeout(15)
                snap(shot)

        brushes = [h.bounding_box() for h in pg.query_selector_all("g.brush")]
        def axis_y(frac):                      # the axes run from 441 (bottom) to 191 (top)
            return 441 - 250 * frac

        TOP, BOTTOM = 189, 444                 # the brushes' ends, just past the axes' ends

        def brush(i, edge, f1, n, shot):
            """A filter over the whole axis, unseen, then its top (edge "n") or bottom ("s") pulled
            in to f1 of the axis, seen: drawn from nothing, a brush would filter every design out
            for its first frames. The pointer drawn goes to that end and drags it."""
            bb = brushes[i]
            x = bb["x"] + bb["width"] / 2
            y0 = TOP if edge == "n" else BOTTOM
            glide(x, y0, 12, shot)
            pg.mouse.move(x, TOP)
            pg.mouse.down()
            pg.mouse.move(x, BOTTOM, steps=5)
            pg.mouse.up()
            pg.mouse.move(x, y0)
            pg.wait_for_timeout(100)
            pg.mouse.down()
            for k in range(1, n + 1):
                mouse[1] = y0 + (axis_y(f1) - y0) * ease(k / n)
                pg.mouse.move(*mouse)
                pg.wait_for_timeout(40)
                snap(shot)
            pg.mouse.up()
            pg.wait_for_timeout(250)
            snap(shot, 8)

        snap("axes", 8)
        brush(6, "n", 0.9 / 6, 18, "axes")     # Utilisation: up to 0.9
        brush(5, "n", 80 / 350, 18, "axes")    # Deflection: up to 80 mm, span / 250 at 20 m
        brush(7, "s", 0.3, 18, "axes")         # Frequency: from 3 Hz
        sel = pg.locator("select").filter(has_text="Design number").first
        sb = sel.bounding_box()
        glide(sb["x"] + sb["width"] / 2, sb["y"] + sb["height"] / 2, 14, "gallery")
        sel.select_option(label="Mass [t]")
        pg.wait_for_timeout(500)
        snap("gallery", 10)
        card = pg.locator("button.design-card").first
        cb = card.bounding_box()
        glide(cb["x"] + cb["width"] / 2, cb["y"] + cb["height"] * 0.4, 14, "gallery")
        card.click()
        pg.wait_for_timeout(1500)
        snap("design", 6)
        # its 3D model, turned round
        tab = pg.locator("button").filter(has_text="3D").first
        tb = tab.bounding_box()
        glide(tb["x"] + tb["width"] / 2, tb["y"] + tb["height"] / 2, 12, "design")
        tab.click()
        pg.wait_for_timeout(2500)
        snap("design", 4)
        canvas = pg.locator("canvas").last
        vb = canvas.bounding_box()
        cx, cy = vb["x"] + vb["width"] / 2, vb["y"] + vb["height"] / 2
        glide(cx - 90, cy, 10, "design")
        pg.mouse.down()
        for k in range(1, 41):
            mouse[0] = cx - 90 + 200 * ease(k / 40)
            pg.mouse.move(*mouse)
            pg.wait_for_timeout(30)
            snap("design")
        pg.mouse.up()
        snap("design", 16)
        b.close()
    httpd.shutdown()
    json.dump(log, open(os.path.join(frames_dir, "log.json"), "w"))
    return log


# Where the camera looks in each part, in the page's CSS pixels (x0, y0, x1, y1), all 16:9.
CAMERA = {
    "axes": (0, 62, 1160, 714.5),       # the parallel axes, the gallery's first row under them
    "gallery": (0, 0, 1600, 900),       # the whole page
    "design": (560, 50, 1600, 635),     # its line on the axes, its 3D model
}
MOVE = 22                               # frames for the camera to move from one to the next


def pointer(scale):
    """A mouse pointer: white, outlined in ink, as RGBA, and where its tip is."""
    pts = [(0, 0), (0, 17), (4.2, 13.2), (7, 19.6), (9.6, 18.5), (6.9, 12.2), (12.4, 12.2)]
    s = scale * 4
    im = Image.new("RGBA", (int(16 * s), int(23 * s)), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.polygon([(x * s + 2 * s, y * s + 1.5 * s) for x, y in pts], fill=(255, 255, 255, 255), outline=(42, 45, 49, 255),
              width=max(1, int(1.3 * s)))
    im = im.resize((im.width // 4, im.height // 4), Image.LANCZOS)
    return im, (2 * scale, 1.5 * scale)


def compose(frames_dir, log, out_mp4, poster):
    W, H = int(VW * DPR), int(VH * DPR)
    enc = subprocess.Popen([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
                            "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "16",
                            "-pix_fmt", "yuv420p", "-movflags", "+faststart", out_mp4], stdin=subprocess.PIPE)
    cam = np.array(CAMERA[log[0][3]], float)
    start, goal, since = cam.copy(), cam.copy(), MOVE
    cache = {}
    poster_at = max(i for i, e in enumerate(log) if e[3] == "design") - 8
    for i, (name, mx, my, shot) in enumerate(log):
        target = np.array(CAMERA[shot], float)
        if not np.allclose(target, goal):
            start, goal, since = cam.copy(), target, 0
        since += 1
        cam = start + (goal - start) * ease(since / MOVE)
        if name not in cache:
            cache.clear()
            cache[name] = Image.open(os.path.join(frames_dir, name)).convert("RGB")
        x0, y0, x1, y1 = cam * DPR
        frame = cache[name].resize((W, H), Image.LANCZOS, box=(x0, y0, x1, y1)).convert("RGBA")
        zoom = W / (x1 - x0)
        ptr, tip = pointer(DPR * zoom * 1.25)
        px, py = (mx * DPR - x0) * zoom - tip[0], (my * DPR - y0) * zoom - tip[1]
        frame.alpha_composite(ptr, (int(px), int(py)))
        frame = frame.convert("RGB")
        enc.stdin.write(frame.tobytes())
        if i == poster_at:
            frame.save(poster)
    enc.stdin.close()
    enc.wait()


if __name__ == "__main__":
    frames = os.path.join(WORK, "frames")
    if "--compose" in sys.argv:
        log = json.load(open(os.path.join(frames, "log.json")))
    else:
        log = record(frames)
    os.makedirs(OUT, exist_ok=True)
    mp4 = os.path.join(OUT, "explorer-study.mp4")
    compose(frames, log, mp4, os.path.join(OUT, "explorer-study-poster.png"))
    print(mp4, "%.1f s" % (len(log) / FPS))
