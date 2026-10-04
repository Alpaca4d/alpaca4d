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
