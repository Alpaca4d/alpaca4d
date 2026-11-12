"""Opens marketing folders in Finder for the social page's buttons.

    python3 finder.py              # listen on 127.0.0.1:47321 until Ctrl-C

A page in the browser cannot open Finder itself. Its Open folder and Show in Finder buttons are links
to this helper instead, in a tab of their own: http://127.0.0.1:47321/open?path=<folder> opens a folder,
/reveal?path=<file> shows a file selected in its folder; the tab then closes itself. It answers only
this computer, and only for paths inside the website repository's marketing/ folder; it never runs a file.
"""
import html, os, subprocess, sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

HERE = os.path.dirname(os.path.abspath(__file__))
MARKETING = os.path.realpath(os.environ.get("MARKETING") or os.path.join(HERE, "../../../../alpaca4d.github.io/marketing"))
PORT = int(os.environ.get("PORT", "47321"))

PAGE = """<!doctype html><meta charset="utf-8"><title>{title}</title>
<body style="font:15px -apple-system,Helvetica,sans-serif;background:#f3f5f8;color:#2a2d31;padding:40px">
<p>{text}</p><script>{script}</script></body>"""


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        path = os.path.realpath(parse_qs(url.query).get("path", [""])[0])
        inside = path == MARKETING or path.startswith(MARKETING + os.sep)
        if url.path not in ("/open", "/reveal") or not inside or not os.path.exists(path):
            return self.answer(404, "Not opened", "Only folders and files inside %s can be opened." % html.escape(MARKETING), "")
        # A folder opens as a window; a file, or anything a click would launch, is only shown selected.
        if url.path == "/open" and os.path.isdir(path) and not path.endswith((".app", ".bundle")):
            subprocess.run(["open", path])
        else:
            subprocess.run(["open", "-R", path])
        self.answer(200, "Opened in Finder", "Opened in Finder: %s" % html.escape(path), "window.close()")

    def answer(self, code, title, text, script):
        body = PAGE.format(title=title, text=text, script=script).encode()
        self.send_response(code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


if __name__ == "__main__":
    print("Opening marketing folders in Finder on http://127.0.0.1:%d - Ctrl-C to stop" % PORT)
    try:
        HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
    except KeyboardInterrupt:
        sys.exit(0)
