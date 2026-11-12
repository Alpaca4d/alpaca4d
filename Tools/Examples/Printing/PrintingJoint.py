# Surface Connection, the GhPython component of PrintedCylinder.gh and PrintedStructure.gh: the joint between two printed
# layers, given a stiffness per area normal to it (Kn) and along it (Ks), as the material of the thin
# band of bricks Printed Layers puts at the bottom of every layer above the first.
#
# A band t thick given Ez = Kn t and Gxz = Gyz = Ks t closes by s / Kn under a pressure s across it and
# slips by tau / Ks under a shear stress tau along it, as springs spread over the joint would; in its
# own plane it is the concrete it joins. The joint is horizontal, as a printed layer is: a solid's
# material has the global axes. Left empty, Kn and Ks are the concrete's own, E / t and G / t.
#
# The band's material is marked as a surface connection (IsSurfaceConnection), for Linear Buckling to
# give the band the stress stiffness of a joint rather than of a band: a band t thick under a pressure
# s would also soften against slip by s / t, which springs of no thickness never do, and buckle in
# shear at s = Ks t, a pressure that vanishes as the band is made thinner.
#
# PrintedCylinder.py and PrintedStructure.py put this file's text in the component, followed by the call to
# surface_connection. Written for IronPython 2.7, which GhPython runs in Rhino 7 and 8.

import clr
clr.AddReference("Alpaca4d.Core")
from Alpaca4d.Material import ElasticIsotropicMaterial, ElasticOrthotropicMaterial


def surface_connection(materials, joints, thickness, kn, ks):
    """For each material, the joint's where its Joint flag is true and the material itself where it is
    false; and the Kn and Ks used [force/length3], None outside the joint."""
    thickness = thickness or 0.001
    out, kns, kss = [], [], []
    for k, material in enumerate(materials):
        if not (k < len(joints) and joints[k]):
            out.append(material)
            kns.append(None)
            kss.append(None)
            continue
        if not isinstance(material, ElasticIsotropicMaterial):
            raise ValueError("The joint is made from an elastic isotropic material, such as Fresh Concrete gives.")
        if thickness <= 0:
            raise ValueError("The joint band needs a thickness greater than zero.")
        normal = kn if kn else material.E / thickness
        shear = ks if ks else material.G / thickness
        ez, gz = normal * thickness, shear * thickness
        # Poisson's effect across the joint only while the band is as stiff across it as the concrete:
        # a softer band with it would not be positive definite.
        across = material.Nu if abs(ez - material.E) <= 1e-9 * material.E else 0.0
        joint = ElasticOrthotropicMaterial("Surface connection, kn %.4g ks %.4g" % (normal, shear),
                                           material.E, material.E, ez, material.G, gz, gz,
                                           material.Nu, across, across, material.Rho)
        joint.IsSurfaceConnection = True
        joint.Color = material.Color
        out.append(joint)
        kns.append(normal)
        kss.append(shear)
    return out, kns, kss
