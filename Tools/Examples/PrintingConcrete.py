# Fresh Concrete, the GhPython component of PrintedCylinder.gh and PrintedStructure.gh: concrete minutes after it is printed,
# as an elastic material whose stiffness grows with its age - one material per age.
#
# Wolfs, Bos and Salet ("Early age mechanical behaviour of 3D printed concrete", Cement and Concrete
# Research 106, 2018) measured a printable mortar over its first 90 minutes: its Young's modulus grows
# in a straight line, E(t) = 0.078 + 0.0012 t MPa with t in minutes, its Poisson's ratio stays at 0.3,
# and it weighs 2070 kg/m3. Those are the defaults. A Final stiffness makes it level off instead, as
# an accelerated concrete does (Suiker, Int. J. Mech. Sci. 137, 2018), starting at the same rate:
# E(t) = Final - (Final - E0) exp(-Rate t / (Final - E0)).
#
# PrintedCylinder.py and PrintedStructure.py put this file's text in the component, followed by the call to fresh_concrete.
# Written for IronPython 2.7, which GhPython runs in Rhino 7 and 8.

import math

import clr
clr.AddReference("Alpaca4d.Core")
from Alpaca4d import ModelStress
from Alpaca4d.Material import ElasticIsotropicMaterial


def modulus(age, e0, rate, final):
    """Young's modulus at age minutes [MPa]."""
    if age < 0:
        raise ValueError("An age below zero: concrete not yet printed has no stiffness to give.")
    if final is None:
        return e0 + rate * age
    span = final - e0
    return final - span * math.exp(-rate / span * age)


def fresh_concrete(ages, e0, rate, final, nu, rho):
    """An elastic isotropic material for each age [min], in the model's units, and its E [MPa]."""
    e0 = 0.078 if e0 is None else e0
    rate = 0.0012 if rate is None else rate
    nu = 0.3 if nu is None else nu
    rho = 2070.0 if rho is None else rho
    if final is not None and final <= e0:
        raise ValueError("The final stiffness has to be greater than E0: it is what the concrete stiffens towards.")
    materials, moduli = [], []
    for age in ages:
        mpa = modulus(age, e0, rate, final)
        e = ModelStress.FromMPa(mpa)
        materials.append(ElasticIsotropicMaterial("Fresh concrete, %.2f min" % age, e, e / (2.0 * (1.0 + nu)), nu, rho))
        moduli.append(mpa)
    return materials, moduli
