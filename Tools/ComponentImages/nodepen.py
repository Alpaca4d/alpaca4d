"""Draws Alpaca4d's components as NodePen draws Grasshopper components, as SVG, for the docs.

Geometry, colours and fonts follow NodePen's generic node (github.com/nodepen/nodes,
src/components/nodes/generic-node and src/constants.ts): a white body with a 2 px dark outline and
a solid 2 px lip under it, a 32 px label column holding the icon or the nickname written upwards,
ports 40 px apart on the edges, each with its own lip, labels in Overpass Mono.

Two additions to NodePen's look, for the docs: the component's message as a dark tab under the body,
as Grasshopper hangs its message balloon there, and a larger icon in a wider label column. The tab is
sized in the browser (render.mjs), which knows how wide the text really is. Extendable components
show their fold-out menus inside the body, and a button they draw (Explorer Inputs' Fly) as a dark bar.
An object linked by grips rather than wires (Non-Linear Optimisation) is drawn with its grips where
it has them, on the left and underneath, and what it shows over itself above the body.

    python3 nodepen.py components.json out_dir [ComponentType ...] [--names]

writes <slug>.svg per component, and <slug>-open.svg with every menu open for a component
whose menus start closed. --names writes the nickname in the label column instead of the icon.
"""
import base64
import html
import json
import os
import sys

DARK = "#414141"
LIGHT = "#FFFFFF"

PAD = 5                 # NODE_INTERNAL_PADDING
LABEL_W = 32            # NODE_LABEL_WIDTH
LABEL_FONT = 16         # NODE_LABEL_FONT_SIZE
ICON = 40               # NodePen draws 24; larger reads better in a document
LABEL_W_ICON = ICON + 12
MESSAGE_FONT = 13
MESSAGE_LINE = 16
MESSAGE_TUCK = 7        # how far the tab reaches up under the body
PALE = "#EFF2F2"        # NodePen's neutral, for the inside of an open menu
MUTED = "#8A8A8A"

# Fold-out menus of extendable components: a bar per menu inside the body, under the ports;
# an open menu shows its controls on a pale panel below its bar.
MENU_HEADER = 26
MENU_GAP = 6
MENU_FONT = 12
CONTROL_FONT = 14
CHAR = 8.6              # Overpass Mono at 14 px, per character
ROW = {"check": 24, "dropdown": 32, "slider": 28, "text": 18, "plug": 32}
MIN_H = 80              # NODE_MINIMUM_HEIGHT
PORT_FONT = 18          # NODE_PORT_LABEL_FONT_SIZE
PORT_OFFSET = 12        # NODE_PORT_LABEL_OFFSET
PORT_H = 40             # NODE_PORT_HEIGHT
PORT_MIN_W = 50         # NODE_PORT_MINIMUM_WIDTH
R = 5                   # NODE_PORT_RADIUS
CHIP = 9                # NODE_PORT_CHIP_SIZE
MARGIN = 10


def label_width(text):
    # getLabelWidth: NodePen's own estimate of a monospace label, plus the chip and padding.
    return len(text) * 15 + CHIP + PAD + 4


def port_ys(count, height):
    # getNodeDimensions: ports centred on the body, PORT_H apart.
    delta_max = ((max(count - 1, 0) - 1) * PORT_H) / 2
    start = height / 2 - delta_max - PORT_H / 2
    return [start + PORT_H * i for i in range(count)]


def left_rounded(x, y, w, h, r):
    return (f"M {x + r} {y} H {x + w} V {y + h} H {x + r} "
            f"A {r} {r} 0 0 1 {x} {y + h - r} V {y + r} A {r} {r} 0 0 1 {x + r} {y} Z")


def visible_controls(menu):
    return [c for c in menu["controls"] if c.get("visible", True) and c["kind"] in ROW]


def slider_text(c):
    return f"{c['value']:.{max(int(c.get('decimals', 2)), 0)}f}"


def menu_rows(menu):
    """The rows an open menu shows, top to bottom: its grips, an input and an output to a row,
    then its controls."""
    ins, outs = menu["plugs"], menu.get("outputs") or []
    rows = [("plug", (ins[i] if i < len(ins) else "", outs[i] if i < len(outs) else ""))
            for i in range(max(len(ins), len(outs)))]
    for c in visible_controls(menu):
        if c["kind"] == "slider" and c.get("name"):
            rows.append(("text", {"kind": "text", "text": c["name"]}))
        rows.append((c["kind"], c))
    return rows


def menu_height(menu, open_):
    if not open_:
        return MENU_HEADER
    rows = menu_rows(menu)
    return MENU_HEADER + (4 + 8 + sum(ROW[k] for k, _ in rows) + 8 if rows else 0)


def menu_width(menu):
    need = len(menu["name"]) * 9 + 60
    for kind, c in menu_rows(menu):
        if kind == "check":
            need = max(need, 30 + len(c["text"]) * CHAR)
        elif kind == "dropdown":
            item = c["items"][c["selected"]] if 0 <= c["selected"] < len(c["items"]) else ""
            need = max(need, 56 + len(item) * CHAR)
        elif kind == "slider":
            need = max(need, 200)
        elif kind == "text":
            need = max(need, 16 + len(c["text"]) * 7.4)
        elif kind == "plug":
            need = max(need, 40 + (len(c[0]) + len(c[1])) * CHAR + (24 if c[0] and c[1] else 0))
    return need + 24


def chevron(x, y, open_):
    if open_:
        return f'<path d="M {x - 4} {y - 2} L {x} {y + 2.5} L {x + 4} {y - 2}" fill="none" stroke="{DARK}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
    return f'<path d="M {x - 2} {y - 4} L {x + 2.5} {y} L {x - 2} {y + 4}" fill="none" stroke="{DARK}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'


def draw_menu(menu, open_, x0, x1, y, left_x, right_x):
    """One menu at the top y, between x0 and x1; left_x and right_x are the body's edges, for its grips."""
    out = [f'<rect x="{x0}" y="{y}" width="{x1 - x0}" height="{MENU_HEADER}" rx="6" ry="6" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>',
           f'<text x="{(x0 + x1) / 2}" y="{y + MENU_HEADER / 2 + MENU_FONT * 0.36}" font-family="Overpass, sans-serif" font-weight="800" '
           f'font-size="{MENU_FONT}" letter-spacing="0.6" fill="{DARK}" text-anchor="middle">{html.escape(menu["name"].upper())}</text>',
           chevron(x1 - 14, y + MENU_HEADER / 2, open_)]
    rows = menu_rows(menu) if open_ else []
    if not rows:
        return out
    top = y + MENU_HEADER + 4
    height = 8 + sum(ROW[k] for k, _ in rows) + 8
    out.append(f'<rect x="{x0}" y="{top}" width="{x1 - x0}" height="{height}" rx="6" ry="6" fill="{PALE}"/>')
    cy = top + 8
    cx0, cx1 = x0 + 12, x1 - 12
    mono = f'font-family="Overpass Mono, monospace" font-weight="600" font-size="{CONTROL_FONT}" fill="{DARK}"'
    for kind, c in rows:
        h = ROW[kind]
        mid = cy + h / 2
        if kind == "plug":
            name_in, name_out = c
            if name_in:
                out.append(f'<circle cx="{left_x}" cy="{mid}" r="{R}" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
                out.append(f'<text x="{cx0}" y="{mid + CONTROL_FONT * 0.35}" {mono}>{html.escape(name_in)}</text>')
            if name_out:
                out.append(f'<circle cx="{right_x}" cy="{mid}" r="{R}" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
                out.append(f'<text x="{cx1}" y="{mid + CONTROL_FONT * 0.35}" {mono} text-anchor="end">{html.escape(name_out)}</text>')
        elif kind == "check":
            fill = DARK if c["checked"] else LIGHT
            out.append(f'<rect x="{cx0}" y="{mid - 7}" width="14" height="14" rx="3" ry="3" fill="{fill}" stroke="{DARK}" stroke-width="2"/>')
            if c["checked"]:
                out.append(f'<path d="M {cx0 + 3.5} {mid} L {cx0 + 6.2} {mid + 3} L {cx0 + 10.5} {mid - 3}" fill="none" stroke="{LIGHT}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>')
            out.append(f'<text x="{cx0 + 24}" y="{mid + CONTROL_FONT * 0.35}" {mono}>{html.escape(c["text"])}</text>')
        elif kind == "dropdown":
            item = c["items"][c["selected"]] if 0 <= c["selected"] < len(c["items"]) else ""
            out.append(f'<rect x="{cx0}" y="{mid - 12}" width="{cx1 - cx0}" height="24" rx="6" ry="6" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
            out.append(f'<text x="{cx0 + 10}" y="{mid + CONTROL_FONT * 0.35}" {mono}>{html.escape(item)}</text>')
            out.append(chevron(cx1 - 12, mid, True))
        elif kind == "slider":
            value = slider_text(c)
            track_end = cx1 - (len(value) * CHAR + 12)
            span = (c["max"] - c["min"]) or 1
            t = min(max((c["value"] - c["min"]) / span, 0), 1)
            hx = cx0 + 6 + t * (track_end - cx0 - 12)
            out.append(f'<line x1="{cx0 + 6}" y1="{mid}" x2="{track_end - 6}" y2="{mid}" stroke="{DARK}" stroke-width="2" stroke-linecap="round"/>')
            out.append(f'<circle cx="{hx}" cy="{mid}" r="6" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
            out.append(f'<text x="{cx1}" y="{mid + CONTROL_FONT * 0.35}" {mono} text-anchor="end">{value}</text>')
        elif kind == "text":
            out.append(f'<text x="{cx0}" y="{mid + 4}" font-family="Overpass Mono, monospace" font-weight="600" font-size="12" fill="{MUTED}">{html.escape(c["text"])}</text>')
        cy += h
    return out


def node_svg(component, icon_dir, names=False, open_all=False):
    menus = component.get("menus") or []
    opened = [open_all or m["expanded"] for m in menus]
    # An open menu holds its own grips; the body keeps the rest. A menu that folds (a switcher
    # component's) takes its grips away with it when closed, as Grasshopper draws them.
    held = [(m["plugs"], m.get("outputs") or []) for m, o in zip(menus, opened) if o or m.get("folds")]
    in_menus = {plug for ins, _ in held for plug in ins}
    out_menus = {plug for _, outs in held for plug in outs}
    inputs = [p["nick"] for p in component["inputs"] if p["nick"] not in in_menus]
    outputs = [p["nick"] for p in component["outputs"] if p["nick"] not in out_menus]
    has_outputs = bool(component["outputs"])
    nick = component["nick"]

    label_w = LABEL_W if names else LABEL_W_ICON
    in_col = max([label_width(t) for t in inputs] + [PORT_MIN_W])
    out_col = max([label_width(t) for t in outputs] + [PORT_MIN_W])
    width = PAD + in_col + PAD + label_w + (PAD + out_col + PAD if has_outputs else PAD)
    label_cx = PAD + in_col + PAD + label_w / 2
    label_min = len(nick) * 15 if names else ICON + 8
    ports_h = PAD * 2 + max(MIN_H, len(inputs) * PORT_H, len(outputs) * PORT_H, label_min)
    button = component.get("button") or ""
    menus_h = sum(menu_height(m, o) + MENU_GAP for m, o in zip(menus, opened)) + (PAD + 2 if menus else 0)
    if button:
        menus_h += MENU_GAP + MENU_HEADER + PAD + 2
    height = ports_h + menus_h
    if menus:
        width = max(width, max(menu_width(m) for m in menus) + 2 * PAD)

    message = [line for line in (component.get("message") or "").split("\n") if line.strip()]
    tab_h = (MESSAGE_TUCK + 6 + MESSAGE_LINE * len(message) + 6) if message else 0

    ox, oy = MARGIN + R, MARGIN
    total_w = width + 2 * (MARGIN + R)
    total_h = height + 2 * MARGIN + 2 + max(tab_h - MESSAGE_TUCK, 0)

    def body(y_shift, fill):
        if has_outputs:
            return (f'<rect x="{ox}" y="{oy + y_shift}" width="{width}" height="{height}" rx="7" ry="7" '
                    f'fill="{fill}" stroke="{DARK}" stroke-width="2"/>')
        return f'<path d="{left_rounded(ox, oy + y_shift, width, height, 7)}" fill="{fill}" stroke="{DARK}" stroke-width="2"/>'

    ports = [(ox, y, t, "input") for y, t in zip(port_ys(len(inputs), ports_h), inputs)]
    ports += [(ox + width, y, t, "output") for y, t in zip(port_ys(len(outputs), ports_h), outputs)]
    menu_grips = []
    yy = oy + ports_h + MENU_GAP
    for m, o in zip(menus, opened):
        if o:
            py = yy + MENU_HEADER + 4 + 8
            for kind, (name_in, name_out) in [r for r in menu_rows(m) if r[0] == "plug"]:
                menu_grips += [(x, py + ROW["plug"] / 2 - oy) for x, name in ((ox, name_in), (ox + width, name_out)) if name]
                py += ROW["plug"]
        yy += menu_height(m, o) + MENU_GAP

    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{total_w}" height="{total_h}" viewBox="0 0 {total_w} {total_h}">']
    # The message tab hangs under the body, its top tucked behind it. Its width is set when it is
    # rendered, from the text; data-cx is where it centres.
    if message:
        top = oy + height - MESSAGE_TUCK
        out.append(f'<g class="message" data-cx="{ox + width / 2}">')
        out.append(f'<rect x="{ox}" y="{top}" width="{width}" height="{tab_h}" rx="7" ry="7" fill="{DARK}" stroke="{DARK}" stroke-width="2"/>')
        out.append(f'<text font-family="Overpass, sans-serif" font-weight="700" font-size="{MESSAGE_FONT}" fill="{LIGHT}" text-anchor="middle">')
        for i, line in enumerate(message):
            y = oy + height + 2 + 6 + MESSAGE_LINE * i + MESSAGE_FONT * 0.8
            out.append(f'<tspan x="{ox + width / 2}" y="{y}">{html.escape(line)}</tspan>')
        out.append('</text></g>')
    # Shadow: the body and every port again, 2 px lower, solid.
    out.append(body(2, DARK))
    for x, y in [(x, y) for x, y, _, _ in ports] + menu_grips:
        out.append(f'<rect x="{x - R}" y="{oy + y - R}" width="{2 * R}" height="{2 * R + 2}" rx="{R}" ry="{R}" fill="{DARK}" stroke="{DARK}" stroke-width="2"/>')
    out.append(body(0, LIGHT))

    # Label column: the icon, or the nickname reading upwards.
    lx = ox + label_cx - label_w / 2
    out.append(f'<rect x="{lx}" y="{oy + PAD}" width="{label_w}" height="{ports_h - 2 * PAD}" rx="7" ry="7" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
    icon = component.get("icon")
    if not names and icon and os.path.exists(os.path.join(icon_dir, icon)):
        data = base64.b64encode(open(os.path.join(icon_dir, icon), "rb").read()).decode()
        out.append(f'<image href="data:image/png;base64,{data}" x="{ox + label_cx - ICON / 2}" y="{oy + ports_h / 2 - ICON / 2}" '
                   f'width="{ICON}" height="{ICON}" preserveAspectRatio="xMidYMid meet"/>')
    else:
        px = ox + label_cx + LABEL_FONT / 2 - 2
        path_id = "label-" + component["type"]
        out.append(f'<path id="{path_id}" fill="none" d="M {px} {oy + ports_h} L {px - 1} {oy}"/>')
        out.append(f'<text font-family="Overpass, sans-serif" font-weight="800" font-size="{LABEL_FONT}" fill="{DARK}">'
                   f'<textPath href="#{path_id}" startOffset="50%" text-anchor="middle">{html.escape(nick.upper())}</textPath></text>')

    # A button the component draws under itself, as a dark bar.
    if button:
        out.append(f'<line x1="{ox}" y1="{oy + ports_h}" x2="{ox + width}" y2="{oy + ports_h}" stroke="{DARK}" stroke-width="2"/>')
        by = oy + ports_h + MENU_GAP
        out.append(f'<rect x="{ox + PAD + 2}" y="{by}" width="{width - 2 * PAD - 4}" height="{MENU_HEADER}" rx="6" ry="6" fill="{DARK}" stroke="{DARK}" stroke-width="2"/>')
        out.append(f'<text x="{ox + width / 2}" y="{by + MENU_HEADER / 2 + MENU_FONT * 0.36}" font-family="Overpass, sans-serif" font-weight="800" '
                   f'font-size="{MENU_FONT}" letter-spacing="0.6" fill="{LIGHT}" text-anchor="middle">{html.escape(button.upper())}</text>')

    # Menus, under a line across the body.
    if menus:
        out.append(f'<line x1="{ox}" y1="{oy + ports_h}" x2="{ox + width}" y2="{oy + ports_h}" stroke="{DARK}" stroke-width="2"/>')
        yy = oy + ports_h + MENU_GAP
        for m, o in zip(menus, opened):
            out.extend(draw_menu(m, o, ox + PAD + 2, ox + width - PAD - 2, yy, ox, ox + width))
            yy += menu_height(m, o) + MENU_GAP

    for x, y, text, direction in ports:
        cy = oy + y
        out.append(f'<circle cx="{x}" cy="{cy}" r="{R}" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
        tx = x + PORT_OFFSET if direction == "input" else x - PORT_OFFSET
        anchor = "start" if direction == "input" else "end"
        out.append(f'<text x="{tx}" y="{cy + 1.5 + PORT_FONT / 4}" font-family="Overpass Mono, monospace" font-weight="600" '
                   f'font-size="{PORT_FONT}" fill="{DARK}" text-anchor="{anchor}">{html.escape(text)}</text>')
    out.append("</svg>")
    return "\n".join(out)


GRIP_LABEL = 11          # Overpass Mono at 18 px, per character
STATUS_FONT = 13
STATUS_H = 22


def object_svg(obj, icon_dir):
    """An object linked by grips of its own rather than wires, such as Non-Linear Optimisation: its
    grips on the left edge and under the body, each named inside the body next to it, the icon in
    the label column, and what it shows over itself on the canvas, as Grasshopper draws it there."""
    left = [g["name"] for g in obj["grips"] if g["side"] == "left"]
    bottom = [g["name"] for g in obj["grips"] if g["side"] == "bottom"]
    rows = max(len(left), 1) + (1 if bottom else 0)
    # The names under the body sit in cells of their own, side by side, each over its grip.
    cells = [len(t) * GRIP_LABEL + 24 for t in bottom]
    left_w = max([PORT_OFFSET + len(t) * GRIP_LABEL + 16 for t in left] + [sum(cells), PORT_MIN_W])
    width = PAD + left_w + PAD + LABEL_W_ICON + PAD
    height = 2 * PAD + max(MIN_H, rows * PORT_H)
    status = (obj.get("message") or "").strip()

    ox, oy = MARGIN + R, MARGIN + (STATUS_H if status else 0)
    total_w = width + 2 * (MARGIN + R)
    total_h = oy + height + R + 2 + MARGIN
    mono = f'font-family="Overpass Mono, monospace" font-weight="600" font-size="{PORT_FONT}" fill="{DARK}"'

    grips = [(ox, oy + PAD + PORT_H * (i + 0.5)) for i in range(len(left))]
    bottom_x, x = [], ox + PAD
    for w in cells:
        bottom_x.append(x + w / 2)
        x += w
    grips += [(gx, oy + height) for gx in bottom_x]

    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{total_w}" height="{total_h}" viewBox="0 0 {total_w} {total_h}">']
    if status:
        out.append(f'<text x="{ox + width / 2}" y="{MARGIN + STATUS_FONT}" font-family="Overpass Mono, monospace" font-weight="600" '
                   f'font-size="{STATUS_FONT}" fill="{MUTED}" text-anchor="middle">{html.escape(status)}</text>')
    # Shadow: the body and every grip again, 2 px lower, solid.
    out.append(f'<rect x="{ox}" y="{oy + 2}" width="{width}" height="{height}" rx="7" ry="7" fill="{DARK}" stroke="{DARK}" stroke-width="2"/>')
    for gx, gy in grips:
        out.append(f'<rect x="{gx - R}" y="{gy - R}" width="{2 * R}" height="{2 * R + 2}" rx="{R}" ry="{R}" fill="{DARK}" stroke="{DARK}" stroke-width="2"/>')
    out.append(f'<rect x="{ox}" y="{oy}" width="{width}" height="{height}" rx="7" ry="7" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')

    lx = ox + PAD + left_w + PAD
    out.append(f'<rect x="{lx}" y="{oy + PAD}" width="{LABEL_W_ICON}" height="{height - 2 * PAD}" rx="7" ry="7" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
    icon = obj.get("icon")
    if icon and os.path.exists(os.path.join(icon_dir, icon)):
        data = base64.b64encode(open(os.path.join(icon_dir, icon), "rb").read()).decode()
        out.append(f'<image href="data:image/png;base64,{data}" x="{lx + LABEL_W_ICON / 2 - ICON / 2}" y="{oy + height / 2 - ICON / 2}" '
                   f'width="{ICON}" height="{ICON}" preserveAspectRatio="xMidYMid meet"/>')

    for (gx, gy), name in zip(grips[:len(left)], left):
        out.append(f'<text x="{gx + PORT_OFFSET}" y="{gy + 1.5 + PORT_FONT / 4}" {mono}>{html.escape(name)}</text>')
    label_y = oy + PAD + PORT_H * (rows - 0.5) + 1.5 + PORT_FONT / 4
    for gx, name in zip(bottom_x, bottom):
        out.append(f'<text x="{gx}" y="{label_y}" {mono} text-anchor="middle">{html.escape(name)}</text>')
    for gx, gy in grips:
        out.append(f'<circle cx="{gx}" cy="{gy}" r="{R}" fill="{LIGHT}" stroke="{DARK}" stroke-width="2"/>')
    out.append("</svg>")
    return "\n".join(out)


def slug(name):
    """The file name of a component's image: "Run Analysis (Alpaca4d)" -> "run-analysis"."""
    stem = name.replace("(Alpaca4d)", "").strip().lower()
    return "-".join("".join(c if c.isalnum() else " " for c in stem).split())


def main():
    data_file, out_dir, *rest = sys.argv[1:]
    names = "--names" in rest
    wanted = [r for r in rest if not r.startswith("--")]
    components = json.load(open(data_file))
    icon_dir = os.path.dirname(data_file)
    os.makedirs(out_dir, exist_ok=True)
    for component in components:
        if wanted and component["type"] not in wanted:
            continue
        name = slug(component["name"])
        with open(os.path.join(out_dir, name + ".svg"), "w") as f:
            f.write(object_svg(component, icon_dir) if component.get("object") else node_svg(component, icon_dir, names))
        # A component with a menu that starts closed gets a second image with every menu open,
        # so every option it has can be seen in the docs.
        if any(not m["expanded"] for m in component.get("menus") or []):
            with open(os.path.join(out_dir, name + "-open.svg"), "w") as f:
                f.write(node_svg(component, icon_dir, names, open_all=True))
        print(name)


if __name__ == "__main__":
    main()
