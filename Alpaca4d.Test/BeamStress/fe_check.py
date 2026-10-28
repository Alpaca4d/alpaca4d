"""The exact-elasticity reference numbers that BeamStress.cs checks against.

sectionproperties solves the Saint-Venant warping function over a finite element mesh, so its
stresses are the exact ones for a prismatic bar, not beam theory's. Direct stress has to agree with
Alpaca4d to the mesh; shear from a shear force does not, and these numbers pin by how much.

Not run by run.sh - the results are written into BeamStress.cs - but rerun it after changing a
formula, or to look at another shape:

    python -m venv venv && ./venv/bin/pip install sectionproperties
    ./venv/bin/python fe_check.py

Written against sectionproperties 3.10.2. Steel, nu = 0.3; units N and mm.
"""
import math

import numpy as np
import sectionproperties.pre.library as lib
from sectionproperties.analysis import Section
from sectionproperties.pre.pre import Material

STEEL = Material(name="steel", elastic_modulus=200e3, poissons_ratio=0.3, yield_strength=355,
                 density=7.85e-6, color="grey")


def analyse(geometry, mesh):
    geometry.create_mesh(mesh_sizes=[mesh])
    section = Section(geometry)
    section.calculate_geometric_properties()
    section.calculate_warping_properties()
    return section


def peak(section, key, **load):
    """The largest magnitude of one stress anywhere on the section."""
    stress = section.calculate_stress(**load).get_stress()[0]
    return float(np.max(np.abs(stress[key])))


def at(section, point, **load):
    """Direct stress and resultant shear at one point."""
    sig_zz, sig_zx, sig_zy = section.get_stress_at_points(pts=[point], **load)[0]
    return sig_zz, math.hypot(sig_zx, sig_zy)


# Hibbeler 7-2: flanges 200 x 20, web 20, depth 340, sharp corners. Its own peak shear is in those
# corners, where the exact solution is singular, so shear is also read where beam theory puts it.
i = analyse(lib.i_section(d=340, b=200, t_f=20, t_w=20, r=0, n_r=1, material=STEEL), 4.0)
cx, cy = i.get_c()
print("I 340 x 200 x 20 x 20")
print("  sigma, Mxx 10 kN.m          ", peak(i, "sig_zz", mxx=10e6))
print("  sigma, Myy 10 kN.m          ", peak(i, "sig_zz", myy=10e6))
combined = dict(n=100e3, vy=20e3, vx=5e3, mxx=10e6, myy=2e6, mzz=0.2e6)
print("  sigma, all at once          ", peak(i, "sig_zz", **combined))
print("  von Mises, all at once      ", peak(i, "sig_vm", **combined))
print("  tau, Vy 20 kN, centroid     ", at(i, (cx, cy), vy=20e3)[1])
print("  tau, Vx 20 kN, flange b/4   ", at(i, (cx + 50, cy + 160), vx=20e3)[1])
print("  tau, T 1 kN.m, top face b/4 ", at(i, (cx + 50, cy + 170 - 1e-6), mzz=1e6)[1])
print("  tau, peak anywhere, Vy / T  ", peak(i, "sig_zxy", vy=20e3), peak(i, "sig_zxy", mzz=1e6),
      "(in the sharp corners - singular)")

# Solid bar, 100 diameter: a 128-gon of the circle's area.
bar = analyse(lib.circular_section_by_area(area=math.pi * 50**2, n=128, material=STEEL), 10.0)
print("bar 100")
print("  sigma, Mxx 3 + Myy 4 kN.m   ", peak(bar, "sig_zz", mxx=3e6, myy=4e6))
print("  tau, T 1 kN.m               ", peak(bar, "sig_zxy", mzz=1e6))
print("  tau, V 10 kN                ", peak(bar, "sig_zxy", vy=10e3))

# A thick tube - Hoogenboom's 200 x 20 - and a thin CHS.
for d, t in [(200.0, 20.0), (168.3, 8.0)]:
    tube = analyse(lib.circular_hollow_section(d=d, t=t, n=160, material=STEEL), 4.0)
    print(f"tube {d} x {t}")
    print("  tau, T 1 kN.m               ", peak(tube, "sig_zxy", mzz=1e6))
    print("  tau, V 10 kN                ", peak(tube, "sig_zxy", vy=10e3))


# On the surface, point by point - what View Results paints a solid beam with. Alpaca4d gives the
# shear running along the surface at a point of it; this is the exact stress a hair inside.
#
# Alpaca4d's local y is sectionproperties' y and its local z is sectionproperties' x, which makes
# sectionproperties' beam axis Alpaca4d's -x and its face the other face of the cut. In OpenSees'
# forces, whose Vy acts down -y: n = N, vy = Vy, vx = -Vz, mzz = T, myy = -My, mxx = -Mz.
from shapely.geometry import Polygon
from sectionproperties.pre.geometry import Geometry


def surface(section, y, z, inward, **load):
    """Direct stress and resultant shear a hair inside the surface at (y, z) from the centroid."""
    cx, cy = section.get_c()
    dy, dz = inward
    return at(section, (cx + z + 0.02 * dz, cy + y + 0.02 * dy), **load)


def rect(x1, x2, y1, y2):
    return [(x1, y1), (x2, y1), (x2, y2), (x1, y2)]


print("\nOn the surface")
print("I 340 x 200 x 20 x 20")
print("  tau, Vy 20 kN, web face at the centroid      ", surface(i, 0, 10, (0, -1), vy=20e3)[1])
print("  tau, Vy 20 kN, flange underside, z = 55      ", surface(i, 150, 55, (1, 0), vy=20e3)[1])
print("  tau, Vz 20 kN, flange top over the web       ", surface(i, 170, 0, (-1, 0), vx=-20e3)[1])
print("  tau, T 1 kN.m, web face at the centroid      ", surface(i, 0, 10, (0, -1), mzz=1e6)[1])
everything = dict(n=100e3, vy=20e3, vx=-5e3, mzz=0.2e6, myy=-2e6, mxx=10e6)
sig, tau = surface(i, 0, 10, (0, -1), **everything)
print("  von Mises, all at once, web face, centroid   ", math.sqrt(sig * sig + 3 * tau * tau))
sig, tau = surface(i, 170, 99.9, (-1, 0), **everything)
print("  von Mises, all at once, flange tip, top      ", math.sqrt(sig * sig + 3 * tau * tau))

# A box with unequal flanges, so its cell needs the constant flow that stops it twisting under Vz.
box = Geometry(Polygon(rect(-100, 100, -150, 150), [rect(-90, 90, -140, 134)]), material=STEEL)
box.create_mesh(mesh_sizes=[2.0])
box = Section(box)
box.calculate_geometric_properties()
box.calculate_warping_properties()
yc = box.get_c()[1]
print("box 200 wide x 300 tall, sides 10, top 16, bottom 10, centroid", yc)
print("  tau, Vy 20 kN, side face at the centroid     ", surface(box, 0, 100, (0, -1), vy=20e3)[1])
print("  tau, Vz 20 kN, bottom face, middle           ", surface(box, -150 - yc, 0, (1, 0), vx=-20e3)[1])
print("  tau, T 1 kN.m, side face at the centroid     ", surface(box, 0, 100, (0, -1), mzz=1e6)[1])
print("  tau, T 1 kN.m, top face, middle              ", surface(box, 150 - yc, 0, (-1, 0), mzz=1e6)[1])

chs = analyse(lib.circular_hollow_section(d=168.3, t=8.0, n=160, material=STEEL), 4.0)
print("CHS 168.3 x 8")
print("  tau, Vy 10 kN, side                          ", surface(chs, 0, 84.15, (0, -1), vy=10e3)[1])
print("  tau, Vy 10 kN, top                           ", surface(chs, 84.15, 0, (-1, 0), vy=10e3)[1])
print("bar 100")
print("  tau, Vy 10 kN, side                          ", surface(bar, 0, 50, (0, -1), vy=10e3)[1])

rectangle = analyse(lib.rectangular_section(d=400, b=200, material=STEEL), 40.0)
print("rectangle 200 wide x 400 tall")
print("  tau, Vy 10 kN, side face at the centroid     ", surface(rectangle, 0, 100, (0, -1), vy=10e3)[1])
print("  tau, Vz 10 kN, top face, middle              ", surface(rectangle, 200, 0, (-1, 0), vx=-10e3)[1])
print("  tau, T 1 kN.m, side face, middle             ", surface(rectangle, 0, 100, (0, -1), mzz=1e6)[1])
print("  tau, T 1 kN.m, top face, middle              ", surface(rectangle, 200, 0, (-1, 0), mzz=1e6)[1])
print("  tau, T 1 kN.m, side face, y = 150            ", surface(rectangle, 150, 100, (0, -1), mzz=1e6)[1])
