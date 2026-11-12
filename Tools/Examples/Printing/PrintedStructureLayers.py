# Printed Layers, the GhPython component of PrintedStructure.gh: a 3D printed shape as bricks, as far as
# it is printed at a given time, the print paths its own sections.
#
# The shape - a surface - is cut every layer height: by horizontal planes, or, given a Path, by planes
# square to it every Height along it, as a printer whose nozzle tilts to follow the path lays them - a
# column that rises and then leans over is printed in layers that lean with it, thicker on the outside of
# a bend and thinner on its inside, rather than in flat layers that each overhang the one below. Each section is a print path: a filament swept
# along it, its section an oval, flat where the layers touch and round at the sides, cut into bricks
# across and up. A wall that leans or curves puts each layer a little off the one below it, so a layer
# is not extruded straight up: its bricks run from the nodes on its own section up to the nodes of the
# section above, which are where those nodes land when they are carried up - each one moved to the
# closest point of the section above. That is what keeps the layers bonded, node for node, as the wall
# leans. Where the section above runs on with nothing under it - two loops merging into one, a wall
# opening out - new nodes are put along it, a segment apart, and the filament bridges between the ones
# carried up. A node with no section close enough above it is where the wall ends.
#
# The nozzle prints the sections level by level, each loop of a level in turn, at a constant speed: a
# brick is printed once the nozzle has passed its far end, and its age is the time since it passed its
# middle. With Joint above 0, the bottom of every layer above the first is a band that thick, its bricks
# flagged for the joint's material.
#
# PrintedStructure.py puts this file's text in the component, followed by the call to printed_layers.
# The geometry below the Rhino part is plain Python, points as (x, y, z) tuples, so that it can be tried
# outside Rhino. Written for IronPython 2.7, which GhPython runs in Rhino 7 and 8: no f-strings.

import math

# Orange as it is printed, purple half way, blue for the oldest: Alpaca4d's colours.
ORANGE, PURPLE, BLUE = (235, 108, 63), (158, 53, 218), (66, 136, 247)


# ---------------------------------------------------------------- plain geometry

def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _lerp(a, b, t):
    return (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]), a[2] + t * (b[2] - a[2]))


def _dist(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _unit(a):
    l = math.sqrt(_dot(a, a))
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-12 else None


UP = (0.0, 0.0, 1.0)


def _segments(points, closed):
    n = len(points)
    return [(points[i], points[(i + 1) % n]) for i in range(n if closed else n - 1)]


def _length(points, closed):
    return sum(_dist(a, b) for a, b in _segments(points, closed))


def _divide(points, closed, count):
    """count equal steps along a polyline: count points if closed, count + 1 if open."""
    segs = _segments(points, closed)
    total = sum(_dist(a, b) for a, b in segs)
    out, s, k = [], 0.0, 0
    targets = [total * i / float(count) for i in range(count if closed else count + 1)]
    for a, b in segs:
        l = _dist(a, b)
        while k < len(targets) and targets[k] <= s + l + 1e-12:
            out.append(_lerp(a, b, (targets[k] - s) / l if l > 0 else 0.0))
            k += 1
        s += l
    while len(out) < len(targets):
        out.append(points[-1])
    return out


def _closest(points, closed, p):
    """The closest point of a polyline to p: (arc length to it, distance, point)."""
    best, s = (0.0, float("inf"), points[0]), 0.0
    for a, b in _segments(points, closed):
        ab = _sub(b, a)
        l2 = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2
        t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1] + (p[2] - a[2]) * ab[2]) / l2))
        q = _lerp(a, b, t)
        d = _dist(p, q)
        if d < best[1]:
            best = (s + t * math.sqrt(l2), d, q)
        s += math.sqrt(l2)
    return best


def _at_arc(points, closed, s):
    total = 0.0
    for a, b in _segments(points, closed):
        l = _dist(a, b)
        if total + l >= s - 1e-12:
            return _lerp(a, b, (s - total) / l if l > 0 else 0.0)
        total += l
    return points[0] if closed else points[-1]


def _area(points, normal=UP):
    """The area a loop encloses, seen down its normal: above zero if it runs counter-clockwise."""
    total = (0.0, 0.0, 0.0)
    for a, b in _segments(points, True):
        c = _cross(a, b)
        total = (total[0] + c[0], total[1] + c[1], total[2] + c[2])
    return 0.5 * _dot(total, normal)


# ---------------------------------------------------------------- the levels' nodes

def close_gaps(levels, gap):
    """The sections with every open curve whose ends are less than gap apart, and less than a tenth of its
    length, closed: a filament that wide fills the gap anyway. A surface lofted through curves that do not
    quite close gives such sections, and printed open they would leave a slit up the wall. A short arc whose
    ends are close only because it is short - the top of a slanted rim - stays open."""
    def shut(points, closed):
        d = _dist(points[0], points[-1])
        return closed or (d < gap and d < 0.1 * _length(points, False))
    return [[(points, shut(points, closed)) for points, closed in curves] for curves in levels]


def level_nodes(levels, segment, reach, normals=None):
    """The nodes of every level, as loops: a list per level of (points, closed). levels: per level, the
    section's curves as polylines (points, closed), bottom first. The first level's are divided into equal
    segments no longer than segment. A later level's curve that takes every node of one loop below carries
    them evenly, turned as the curve turned (_carried_evenly); any other takes the nodes below to their
    closest point on it, within reach, merges those that land on one another and adds nodes a segment apart
    where it runs on with none. Also returns, per level from the second, where each node of the level below
    went: (loop, index) or None. normals: per level, the normal of its plane, pointing the way the print
    grows; straight up if not given."""
    normals = normals or [UP] * len(levels)
    nodes, carried, sections = [], [], []   # sections: per level, each loop's curve, run the way its nodes run
    for k, curves in enumerate(levels):
        if k == 0:
            loops, oriented = [], []
            for points, closed in curves:
                count = max(3 if closed else 1, int(math.ceil(_length(points, closed) / segment - 1e-9)))
                loop = _divide(points, closed, count)
                # Closed loops counter-clockwise seen from above - down the level's normal - so that "left"
                # is the same side everywhere.
                if closed and _area(loop, normals[k]) < 0:
                    loop.reverse()
                    points = list(reversed(points))
                loops.append((loop, closed))
                oriented.append((points, closed))
            nodes.append(loops)
            sections.append(oriented)
            continue

        below = nodes[k - 1]
        # Each node below to the closest curve of this level, if one is within reach.
        hits = [[] for _ in curves]   # per curve: (arc, (loop below, index below))
        for lb, (loop, _) in enumerate(below):
            for ib, p in enumerate(loop):
                best = None
                for c, (points, closed) in enumerate(curves):
                    s, d, _ = _closest(points, closed, p)
                    if d <= reach and (best is None or d < best[2]):
                        best = (c, s, d)
                if best is not None:
                    hits[best[0]].append((best[1], (lb, ib)))

        loops, where, oriented = [], {}, []
        for c, (points, closed) in enumerate(curves):
            total = _length(points, closed)
            got = hits[c]
            # Run the curve the way the nodes below run, so that left stays left.
            if len(got) > 2:
                forward = 0
                for i in range(len(got) - 1):
                    (s0, (l0, i0)), (s1, (l1, i1)) = got[i], got[i + 1]
                    if l0 == l1 and i1 == i0 + 1:
                        forward += 1 if s1 > s0 else -1
                if forward < 0:
                    points = list(reversed(points))
                    got = [(total - s, w) for s, w in got]
            elif closed and _area(points, normals[k]) < 0:
                points = list(reversed(points))
                got = [(total - s, w) for s, w in got]
            got.sort(key=lambda h: h[0])
            # Nodes carried up, in order along the curve, with new ones a segment apart in the gaps.
            arcs = []   # (arc, [the nodes below carried to it])
            even = _carried_evenly(got, below, sections[k - 1], closed, total, segment)
            if even is not None:
                arcs = even
            elif not got:
                count = max(3 if closed else 1, int(math.ceil(total / segment - 1e-9)))
                arcs = [(total * i / float(count), []) for i in range(count if closed else count + 1)]
            else:
                # Nodes that land within a quarter segment of the one before are one node: the brick
                # between them below ends there, rather than squashed flat.
                kept = []
                for s, w in got:
                    if kept and s - kept[-1][0] < 0.25 * segment:
                        kept[-1][1].append(w)
                    else:
                        kept.append((s, [w]))
                if closed and len(kept) > 1 and kept[0][0] + total - kept[-1][0] < 0.25 * segment:
                    kept[0][1].extend(kept.pop()[1])
                first, last = kept[0][0], kept[-1][0]
                if not closed and first > 0.5 * segment:
                    n = int(math.ceil(first / segment - 1e-9))
                    arcs += [(first * j / float(n), []) for j in range(n)]
                for i, (s, ws) in enumerate(kept):
                    arcs.append((s, ws))
                    if i + 1 < len(kept) or closed:
                        b = kept[i + 1][0] if i + 1 < len(kept) else kept[0][0] + total
                        if b - s > 1.5 * segment:
                            n = int(math.ceil((b - s) / segment - 1e-9))
                            arcs += [((s + (b - s) * j / float(n)) % total if closed else s + (b - s) * j / float(n), []) for j in range(1, n)]
                if not closed and total - last > 0.5 * segment:
                    n = int(math.ceil((total - last) / segment - 1e-9))
                    arcs += [(last + (total - last) * j / float(n), []) for j in range(1, n + 1)]
                arcs.sort(key=lambda h: h[0])
            loop = [_at_arc(points, closed, s) for s, _ in arcs]
            for i, (_, ws) in enumerate(arcs):
                for w in ws:
                    where[w] = (len(loops), i)
            loops.append((loop, closed))
            oriented.append((points, closed))
        nodes.append(loops)
        sections.append(oriented)
        carried.append(where)
    return nodes, carried


def _carried_evenly(got, below, below_sections, closed, total, segment):
    """Where a section takes every node of one loop below, closed to closed or open to open, in their order:
    the nodes at the same share of its length as they were below, turned as a whole by the mean of how far
    their closest points turned - so that they keep their spacing as the wall twists, leans, grows or
    shrinks, rather than bunching where closest points crowd and spreading where they part. As (arc, [node
    below]); None where that does not hold, or would space them closer than half a segment or further than
    one and a half."""
    if not got or len(set(w[0] for _, w in got)) != 1:
        return None
    lb = got[0][1][0]
    loop, loop_closed = below[lb]
    n = len(loop)
    if loop_closed != closed or len(got) != n or n < (3 if closed else 2):
        return None
    order = [w[1] for _, w in got]
    start = order.index(0) if closed else 0
    if order[start:] + order[:start] != list(range(n)):
        return None
    spacing = total / float(n if closed else n - 1)
    if not 0.5 * segment <= spacing <= 1.5 * segment:
        return None
    # Each node's share of the length of the section below, from its first node, measured along that
    # section: chords between the nodes would cut its corners, and the nodes creep off them layer by layer.
    section, _ = below_sections[lb]
    length = _length(section, closed)
    arcs = [_closest(section, closed, p)[0] for p in loop]
    shares = [((a - arcs[0]) % length if closed else a) / length for a in arcs]
    arc_of = dict((w[1], a) for a, w in got)
    if not closed:
        return [(shares[i] * total, [(lb, i)]) for i in range(n)]
    # The turn, as the mean of where the closest points fell against where the shares put the nodes, taken
    # round the loop so that a turn across its start counts the same as any other.
    x = sum(math.cos(2 * math.pi * (arc_of[i] - shares[i] * total) / total) for i in range(n))
    y = sum(math.sin(2 * math.pi * (arc_of[i] - shares[i] * total) / total) for i in range(n))
    turn = math.atan2(y, x) / (2 * math.pi) * total
    return sorted((((shares[i] * total + turn) % total), [(lb, i)]) for i in range(n))


def _frames(loop, closed, half, normal=UP):
    """At each node: the direction to its left across the filament, in the level's plane, how much a corner widens
    it - 1 / cos of half the turn, up to three times - and how far its left and its right side reach out,
    as shares of that. On the inside of a tight turn the mitred corners of the filament, half wide each side,
    would cross those of the next node: there the inside is drawn in, just enough that the inner edge of
    every brick keeps 30% of its length and none folds through itself."""
    n, frames = len(loop), []

    def left(a, b):
        return _unit(_cross(normal, _sub(b, a)))

    # The turn at each node: its tangent of half the angle, and which way, 1 to the left, -1 to the right.
    turns = []
    for k in range(n):
        before = left(loop[k - 1], loop[k]) if (closed or k > 0) else None
        after = left(loop[k], loop[(k + 1) % n]) if (closed or k < n - 1) else None
        if before is None or after is None:
            frames.append((before or after or _unit(_cross(normal, (1.0, 0.0, 0.0))) or (0.0, 1.0, 0.0), 1.0))
            turns.append((0.0, 0))
            continue
        out = _unit((before[0] + after[0], before[1] + after[1], before[2] + after[2]))
        if out is None:
            out = after
        cos = _dot(out, after)
        scale = min(3.0, 1.0 / max(cos, 1.0 / 3.0))
        frames.append((out, scale))
        # before and after are the left normals: the path turns left when after is turned left of before.
        side = _dot(_cross(before, after), normal)
        turns.append((math.sqrt(max(0.0, scale * scale - 1.0)), 1 if side > 0 else -1))

    reach = [[1.0, 1.0] for _ in range(n)]   # left, right
    for a in range(n if closed else n - 1):
        b = (a + 1) % n
        length = _dist(loop[a], loop[b])
        for s, sign in ((0, 1), (1, -1)):
            drawn = half * sum(t for t, way in (turns[a], turns[b]) if way == sign)
            if drawn > 0.7 * length:
                f = 0.7 * length / drawn
                for k in (a, b):
                    if turns[k][1] == sign:
                        reach[k][s] = min(reach[k][s], f)
    return [(out, scale, reach[k][0], reach[k][1]) for k, (out, scale) in enumerate(frames)]


def _half_width(v, width, height, rounding, thick=None):
    """Half the filament's width at v up a layer thick high - Height unless the layers lean against one
    another: its sides round, as deep as half of Height, so that a layer's flat top is as wide as the next
    one's flat bottom however thick each is where they meet."""
    c, h = rounding * height / 2.0, (thick or height) / 2.0
    s = max(0.0, 1.0 - ((v - h) / h) ** 2)
    return width / 2.0 - c + c * math.sqrt(s)


def bricks_of(nodes, carried, width, height, rounding, across, up, joint, speed, normals=None):
    """Every brick of the print, in the order printed: (eight corners, layer, time printed at its middle,
    time its far end is printed, in a joint band), the base points on the print bed, and the print path of
    each layer as (loop points, closed, time it starts, its level's normal, the plane of the level above as
    (a point, its normal)) for the nozzle; and the minutes the whole print takes. normals: per level, the
    normal of its plane; straight up if not given. Where the levels lean against one another, a layer is as
    thick at each node as the level above is from it, along its own level's normal."""
    normals = normals or [UP] * len(nodes)
    frames = [[_frames(loop, closed, width / 2.0, normals[k]) for loop, closed in level] for k, level in enumerate(nodes)]
    out, base, seen, paths = [], [], set(), []
    clock = 0.0
    for k in range(len(nodes) - 1):
        band = k > 0 and joint > 0
        start = joint if band else 0.0

        def rows(thick):
            return [0.0] + ([start] if band else []) + [start + (thick - start) * j / float(up) for j in range(1, up + 1)]

        above = (nodes[k + 1][0][0][0], normals[k + 1])
        level_paths = []
        for lb, (loop, closed) in enumerate(nodes[k]):
            level_paths.append((loop, closed, clock, normals[k], above))
            n, along = len(loop), 0.0
            for ia in range(n if closed else n - 1):
                ib = (ia + 1) % n
                a, b = loop[ia], loop[ib]
                length = _dist(a, b)
                middle, end = clock + (along + length / 2.0) / speed, clock + (along + length) / speed
                along += length
                up_a, up_b = carried[k].get((lb, ia)), carried[k].get((lb, ib))
                if up_a is None or up_b is None or up_a[0] != up_b[0]:
                    continue   # the wall ends here, or runs into another loop above
                upper, upper_closed = nodes[k + 1][up_a[0]]
                gap = (up_b[1] - up_a[1]) % len(upper) if upper_closed else up_b[1] - up_a[1]
                if not 1 <= gap <= 2:
                    continue   # carried up out of order, or too far apart: no brick
                A, B = upper[up_a[1]], upper[up_b[1]]
                fa, fb = frames[k][lb][ia], frames[k][lb][ib]
                fA, fB = frames[k + 1][up_a[0]][up_a[1]], frames[k + 1][up_b[0]][up_b[1]]
                # How thick the layer is at each end, square to its level: Height between flat levels.
                ra = rows(max(0.25 * height, _dot(_sub(A, a), normals[k])))
                rb = rows(max(0.25 * height, _dot(_sub(B, b), normals[k])))

                def at(p, P, f, F, r, i, j):
                    v, thick = r[j], r[-1]
                    t = v / thick
                    c = _lerp(p, P, t)
                    o = _lerp(f[0], F[0], t)
                    lo = math.sqrt(_dot(o, o)) or 1.0
                    side = -1.0 + 2.0 * i / across
                    reach = f[2] + t * (F[2] - f[2]) if side > 0 else f[3] + t * (F[3] - f[3])
                    scale = (f[1] + t * (F[1] - f[1])) * reach
                    u = _half_width(v, width, height, rounding, thick) * side * scale / lo
                    return (c[0] + u * o[0], c[1] + u * o[1], c[2] + u * o[2])

                for j in range(len(ra) - 1):
                    for i in range(across):
                        corners = [at(a, A, fa, fA, ra, i, j), at(b, B, fb, fB, rb, i, j), at(b, B, fb, fB, rb, i + 1, j), at(a, A, fa, fA, ra, i + 1, j),
                                   at(a, A, fa, fA, ra, i, j + 1), at(b, B, fb, fB, rb, i, j + 1), at(b, B, fb, fB, rb, i + 1, j + 1), at(a, A, fa, fA, ra, i + 1, j + 1)]
                        out.append((corners, k, middle, end, band and j == 0))
                        if k == 0 and j == 0:
                            for p in corners[:4]:
                                key = (round(p[0], 7), round(p[1], 7), round(p[2], 7))
                                if key not in seen:
                                    seen.add(key)
                                    base.append(p)
            clock += _length(loop, closed) / speed
        paths.append(level_paths)
    return out, base, paths, clock


def _colour(age, oldest):
    t = max(0.0, min(1.0, age / oldest)) if oldest > 0 else 0.0
    a, b, s = (ORANGE, PURPLE, 2 * t) if t < 0.5 else (PURPLE, BLUE, 2 * t - 1)
    return tuple(int(round(a[i] + s * (b[i] - a[i]))) for i in range(3))


def _on_top(p, normal, above):
    """Where the nozzle is when it lays p: on top of the layer, out along its level's normal to the level
    above."""
    origin, up = above
    across = _dot(normal, up)
    d = _dot(_sub(origin, p), up) / across if abs(across) > 1e-9 else 0.0
    return (p[0] + d * normal[0], p[1] + d * normal[1], p[2] + d * normal[2])


def printed(bricks, paths, height, time, speed):
    """The bricks printed by time: (corners, age, in joint, colour); the nozzle, on top of the layer it is
    printing, and the way it points, out of the print along the layer's normal; and the layers printed, the
    one being printed counted by how far along it is."""
    done = [(c, time - middle, joint) for c, k, middle, end, joint in bricks if end <= time + 1e-12]
    oldest = max([age for _, age, _ in done] or [0.0])
    nozzle, axis, layers = None, UP, float(len(paths))
    for k, level in enumerate(paths):
        level_start = level[0][2]
        level_length = sum(_length(path[0], path[1]) for path in level)
        for loop, closed, start, normal, above in level:
            length = _length(loop, closed)
            if start <= time < start + length / speed:
                arc = (time - start) * speed
                nozzle, axis = _on_top(_at_arc(loop, closed, arc), normal, above), normal
                layers = k + (time - level_start) * speed / level_length
                break
        if nozzle is not None:
            break
    if nozzle is None:
        if time <= 0 or not paths:
            if paths:
                loop, closed, _, normal, above = paths[0][0]
                nozzle, axis = _on_top(loop[0], normal, above), normal
            else:
                nozzle = (0.0, 0.0, height)
            layers = 0.0
        else:
            loop, closed, _, normal, above = paths[-1][-1]
            nozzle, axis = _on_top(loop[0] if closed else loop[-1], normal, above), normal
    return [(c, age, joint, _colour(age, oldest)) for c, age, joint in done], nozzle, axis, layers


# ---------------------------------------------------------------- in Grasshopper

def printed_layers(shape, path, height, width, rounding, across, up, segment, joint, speed, time):
    """The bricks of shape - a surface - printed by time [min]: meshes, ages [min], whether each is in a
    joint band, colours by age, the base points on the print bed, the nozzle and the way it points, the
    layers printed and the minutes the whole print takes. With path - a curve from the shape's foot up
    through it - the shape is cut square to the path every Height along it, rather than flat every Height
    up. The sections and bricks are kept between solutions while only Time changes, so that moving it
    costs no more than picking the bricks printed."""
    import Rhino.Geometry as rg
    import System.Drawing as sd
    import scriptcontext

    height = height or 0.01
    width = width or 0.04
    rounding = 1.0 if rounding is None else rounding
    across, up = across or 1, up or 2
    segment, joint, speed = segment or 0.04, joint or 0.0, speed or 5.0
    if width <= 0 or height <= 0 or not 0 <= rounding <= 1 or rounding * height >= width or not 0 <= joint < height:
        raise ValueError("Width and Height above zero, Round from 0 to 1 with the round sides not meeting, Joint below Height.")

    box = shape.GetBoundingBox(True)
    # The component's own code is part of the key, so that an edit to it is never answered from the cache.
    try:
        code = hash(ghenv.Component.Code)  # noqa: F821 - GhPython's own
    except NameError:
        code = None
    along = None
    if path is not None:
        a, b = path.PointAtStart, path.PointAtEnd
        along = (round(a.X, 9), round(a.Y, 9), round(a.Z, 9), round(b.X, 9), round(b.Y, 9), round(b.Z, 9), round(path.GetLength(), 9))
    key = ("printed structure", code, round(box.Min.X, 9), round(box.Min.Y, 9), round(box.Min.Z, 9), round(box.Max.X, 9),
           round(box.Max.Y, 9), round(box.Max.Z, 9), shape.Faces.Count, along, height, width, rounding, across, up, segment,
           joint, speed)
    cache = scriptcontext.sticky.get("alpaca4d printed structure")
    if cache is None or cache[0] != key:
        # A level a hair above the bottom of the shape, then one every Height: each a section, its curves
        # joined and turned into polylines 4 mm apart for the arithmetic above. Along a path, a level a hair
        # along from its start, then one every Height along it, each square to it; of what such a plane cuts,
        # only the curves about that point of the path, not where the plane runs on through the shape
        # further along or back.
        levels, normals, k = [], [], 0
        length = path.GetLength() if path is not None else 0.0
        while True:
            if path is None:
                z = box.Min.Z + 1e-4 + k * height
                if z > box.Max.Z - 1e-4:
                    break
                plane = rg.Plane(rg.Point3d(0, 0, z), rg.Vector3d.ZAxis)
                curves = rg.Curve.JoinCurves(rg.Brep.CreateContourCurves(shape, plane), 1e-6)
            else:
                s = 1e-4 + k * height
                if s > length - 1e-4:
                    break
                ok, t = path.LengthParameter(s)
                plane = rg.Plane(path.PointAt(t), path.TangentAt(t))
                cut = rg.Intersect.Intersection.BrepPlane(shape, plane, 1e-5)
                curves = []
                for c in rg.Curve.JoinCurves(cut[1] or [], 1e-6):
                    ok, tc = c.ClosestPoint(plane.Origin)
                    ok, tp = path.ClosestPoint(c.PointAt(tc))
                    if abs(path.GetLength(rg.Interval(path.Domain.Min, tp)) - s) < max(3 * height, 0.01):
                        curves.append(c)
            loops = []
            for c in curves:
                if c.GetLength() < 1e-3:
                    continue   # where a plane grazes the shape: no print path
                ts = c.DivideByLength(0.004, True) or []
                pts = [c.PointAt(t) for t in ts]
                if not c.IsClosed and (not pts or pts[-1].DistanceTo(c.PointAtEnd) > 1e-6):
                    pts.append(c.PointAtEnd)
                if len(pts) >= (3 if c.IsClosed else 2):
                    loops.append(([(p.X, p.Y, p.Z) for p in pts], c.IsClosed))
            if loops:
                levels.append(loops)
                normals.append((plane.ZAxis.X, plane.ZAxis.Y, plane.ZAxis.Z))
            k += 1
        if len(levels) < 2:
            raise ValueError("The shape is not tall enough for two layers of that Height.")
        nodes, carried = level_nodes(close_gaps(levels, width), segment, reach=3 * height + 0.5 * segment, normals=normals)
        cache = (key, bricks_of(nodes, carried, width, height, rounding, across, up, joint, speed, normals))
        scriptcontext.sticky["alpaca4d printed structure"] = cache
    bricks, base, paths, duration = cache[1]
    if time is None:
        time = duration

    done, nozzle, axis, layers = printed(bricks, paths, height, time, speed)
    meshes = []
    for corners, age, in_joint, colour in done:
        mesh = rg.Mesh()
        for p in corners:
            mesh.Vertices.Add(p[0], p[1], p[2])
        for a, b, c, d in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (3, 7, 6, 2), (1, 2, 6, 5), (0, 4, 7, 3)):
            mesh.Faces.AddFace(a, b, c, d)
        mesh.Normals.ComputeNormals()
        meshes.append(mesh)
    return (meshes, [age for _, age, _, _ in done], [j for _, _, j, _ in done],
            [sd.Color.FromArgb(*c) for _, _, _, c in done], [rg.Point3d(*p) for p in base],
            rg.Point3d(*nozzle), rg.Vector3d(*axis), layers, duration)
