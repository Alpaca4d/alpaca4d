# Printed Layers, the GhPython component of PrintedCylinder.gh: a 3D printed object as bricks, as far
# as it is printed at a given time.
#
# Each layer is a filament swept along its print path, its section an oval - flat where it rests on
# the layer below and carries the one above, round at the sides where the nozzle let it bulge - cut
# into bricks across, up and along the path. The nozzle runs the paths one after the other at a
# constant speed: a brick is printed once the nozzle has passed its far end, and its age is the time
# since the nozzle passed its middle. With Joint above 0, the bottom of every layer above the first
# is a band that thick, its bricks flagged in InJoint for the joint's material.
#
# PrintedCylinder.py puts this file's text in the component, followed by the call to printed_layers.
# Written for IronPython 2.7, which GhPython runs in Rhino 7 and 8: no f-strings.

import math

import Rhino.Geometry as rg
import System.Drawing as sd

# Orange as it is printed, purple half way, blue for the oldest: Alpaca4d's colours.
ORANGE, PURPLE, BLUE = (235, 108, 63), (158, 53, 218), (66, 136, 247)


def _half_width(v, width, height, rounding):
    """Half the filament's width at v above the bottom of its layer: the sides are half ellipses that
    reach in rounding x height / 2."""
    c, h = rounding * height / 2.0, height / 2.0
    s = max(0.0, 1.0 - ((v - h) / h) ** 2)
    return width / 2.0 - c + c * math.sqrt(s)


def _rows(band, height, up, joint):
    """The heights of a layer's rows of nodes from its bottom; the joint band, when it has one, first."""
    start = joint if band else 0.0
    rows = [0.0] + ([start] if band else [])
    for j in range(1, up + 1):
        rows.append(start + (height - start) * j / float(up))
    return rows


def _left(a, b):
    """The horizontal unit vector to the left of going from a to b."""
    x, y = b.X - a.X, b.Y - a.Y
    length = math.sqrt(x * x + y * y)
    if length < 1e-12:
        raise ValueError("A print path runs straight up, or two of its points are in one place: a layer is printed flat.")
    return (-y / length, x / length)


def _frames(points, closed):
    """At each point, the direction across the filament - square to the path at its ends, along the
    bisector at a corner - and how much a corner widens the section: 1 / cos of half the turn, so that
    the wall keeps its width, up to three times at the sharpest corners."""
    n, frames = len(points), []
    for k in range(n):
        first, last = k == 0 and not closed, k == n - 1 and not closed
        before = None if first else _left(points[(k - 1) % n], points[k])
        after = None if last else _left(points[k], points[(k + 1) % n])
        if first or last:
            frames.append((after if first else before, 1.0))
            continue
        bx, by = before[0] + after[0], before[1] + after[1]
        size = math.sqrt(bx * bx + by * by)
        out = (bx / size, by / size) if size > 1e-9 else after
        cos = out[0] * after[0] + out[1] * after[1]
        frames.append((out, min(3.0, 1.0 / max(cos, 1.0 / 3.0))))
    return frames


def _hexahedron(corners):
    """A closed eight-vertex mesh: four corners at the bottom, then the four above them."""
    mesh = rg.Mesh()
    for p in corners:
        mesh.Vertices.Add(p)
    for a, b, c, d in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (3, 7, 6, 2), (1, 2, 6, 5), (0, 4, 7, 3)):
        mesh.Faces.AddFace(a, b, c, d)
    mesh.Normals.ComputeNormals()
    return mesh


def _colour(age, oldest):
    t = max(0.0, min(1.0, age / oldest)) if oldest > 0 else 0.0
    a, b, s = (ORANGE, PURPLE, 2 * t) if t < 0.5 else (PURPLE, BLUE, 2 * t - 1)
    return sd.Color.FromArgb(*[int(round(a[i] + s * (b[i] - a[i]))) for i in range(3)])


def printed_layers(paths, layers, width, height, rounding, across, up, segment, joint, speed, time):
    """The bricks printed by time [min] along paths, one per layer in the order printed - or, with a
    single path, layers of it each height above the last - at speed [length/min]. Returns the meshes,
    their ages [min], whether each is in a joint band, its colour by age, the base points on the print
    bed, the nozzle, the layers printed and the minutes the whole print takes."""
    layers = layers or 1
    width = width or 0.04
    height = height or 0.01
    rounding = 1.0 if rounding is None else rounding
    across, up = across or 2, up or 2
    segment, joint, speed = segment or 0.04, joint or 0.0, speed or 5.0
    if width <= 0 or height <= 0:
        raise ValueError("The filament needs a width and a layer height greater than zero.")
    if not 0 <= rounding <= 1 or rounding * height >= width:
        raise ValueError("Round is a share of half the layer height, 0 to 1, and the round sides may not meet.")
    if not 0 <= joint < height:
        raise ValueError("The joint band has to be thinner than the layer, or 0 for none.")

    # Each path divided into equal segments no longer than Segment, at the height its layer rests on.
    divided = []
    for curve in [c for c in paths if c is not None]:
        count = max(3 if curve.IsClosed else 1, int(math.ceil(curve.GetLength() / segment - 1e-9)))
        points = [curve.PointAt(t) for t in curve.DivideByCount(count, True)]
        divided.append((points, curve.IsClosed))
    if len(divided) == 1 and layers > 1:
        points, closed = divided[0]
        divided = [([p + rg.Vector3d(0, 0, l * height) for p in points], closed) for l in range(layers)]

    def segments(points, closed):
        return [(points[s], points[(s + 1) % len(points)]) for s in range(len(points) if closed else len(points) - 1)]

    lengths = [sum(a.DistanceTo(b) for a, b in segments(p, c)) for p, c in divided]
    duration = sum(lengths) / speed
    if time is None:
        time = duration

    bricks, base, seen = [], [], set()
    nozzle, printed = divided[0][0][0] + rg.Vector3d(0, 0, height), 0.0
    started, done = 0.0, False
    for l, (points, closed) in enumerate(divided):
        if time <= started or done:
            break
        band = l > 0 and joint > 0
        rows = _rows(band, height, up, joint)
        frames = _frames(points, closed)
        along = 0.0
        for s, (start, end) in enumerate(segments(points, closed)):
            length = start.DistanceTo(end)
            if started + (along + length) / speed > time + 1e-12:
                # The nozzle is in this segment.
                share = max(0.0, (time - started) * speed - along) / length if length > 0 else 0.0
                nozzle = start + share * (end - start) + rg.Vector3d(0, 0, height)
                printed = l + (along + share * length) / lengths[l]
                done = True
                break
            age = time - (started + (along + length / 2.0) / speed)
            (a, fa), (b, fb) = frames[s], frames[(s + 1) % len(frames)]

            def at(o, f, scale, i, j):
                v = rows[j]
                u = _half_width(v, width, height, rounding) * (-1.0 + 2.0 * i / across) * scale
                return rg.Point3d(o.X + u * f[0], o.Y + u * f[1], o.Z + v)

            for j in range(len(rows) - 1):
                for i in range(across):
                    corners = [at(start, a, fa, i, j), at(end, b, fb, i, j), at(end, b, fb, i + 1, j), at(start, a, fa, i + 1, j)]
                    corners += [at(start, a, fa, i, j + 1), at(end, b, fb, i, j + 1), at(end, b, fb, i + 1, j + 1), at(start, a, fa, i + 1, j + 1)]
                    bricks.append((corners, age, band and j == 0))
                    if l == 0 and j == 0:
                        for p in corners[:4]:
                            key = (round(p.X, 7), round(p.Y, 7), round(p.Z, 7))
                            if key not in seen:
                                seen.add(key)
                                base.append(p)
            along += length
        if not done:
            started += lengths[l] / speed
            nozzle, printed = points[-1 if not closed else 0] + rg.Vector3d(0, 0, height), l + 1.0

    oldest = max([age for _, age, _ in bricks] or [0.0])
    meshes = [_hexahedron(c) for c, _, _ in bricks]
    ages = [age for _, age, _ in bricks]
    in_joint = [j for _, _, j in bricks]
    colours = [_colour(age, oldest) for age in ages]
    return meshes, ages, in_joint, colours, base, nozzle, printed, duration
