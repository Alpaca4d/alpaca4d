# The converged plate-theory frequencies of NAFEMS FV16, the cantilevered thin square plate, that
# tables.py compares Alpaca4d with. NAFEMS's own targets are older Ritz solutions - Young's and
# Barton's of 1950-51 for modes 1 to 5, Leissa's of 1973 for mode 6 - upper bounds up to 1% high;
# this converges the same Kirchhoff problem properly, and agrees with Sukhoterin et al. (2016). See
# README.md.
#
# Rayleigh-Ritz on a square Kirchhoff plate clamped along x = 0 and free on the other three edges,
# with trial functions xi^2 L_i(xi) L_j(eta) - shifted Legendre polynomials, the xi^2 satisfying the
# clamped edge - and the order raised until five figures stop moving. Needs numpy and scipy:
#     python3 fv16_reference.py
import numpy as np
from numpy.polynomial import legendre as L
from scipy.linalg import eigh


def one_dimensional(N, clamped, points=80):
    """Integrals over [0, 1] of the products of the p-th and q-th derivatives of the N trial functions."""
    t, w = L.leggauss(points)
    x, w = (t + 1) / 2, w / 2
    F = np.zeros((3, N, points))
    for i in range(N):
        c = np.zeros(i + 1)
        c[-1] = 1
        l0, l1, l2 = L.legval(t, c), 2 * L.legval(t, L.legder(c)), 4 * L.legval(t, L.legder(c, 2))
        if clamped:
            F[0, i], F[1, i], F[2, i] = x * x * l0, 2 * x * l0 + x * x * l1, 2 * l0 + 4 * x * l1 + x * x * l2
        else:
            F[0, i], F[1, i], F[2, i] = l0, l1, l2
    return {(p, q): (F[p] * w) @ F[q].T for p in range(3) for q in range(3)}


def frequency_parameters(N, nu=0.3, modes=6):
    """lambda = omega a^2 sqrt(rho h / D) of the first modes, N trial functions each way."""
    A, B = one_dimensional(N, True), one_dimensional(N, False)
    K = np.kron(A[2, 2], B[0, 0]) + np.kron(A[0, 0], B[2, 2]) \
        + nu * (np.kron(A[2, 0], B[0, 2]) + np.kron(A[0, 2], B[2, 0])) + 2 * (1 - nu) * np.kron(A[1, 1], B[1, 1])
    M = np.kron(A[0, 0], B[0, 0])
    return np.sqrt(eigh(K, M, eigvals_only=True)[:modes])


if __name__ == "__main__":
    for N in (6, 10, 14, 18, 22):
        print("N = %2d   lambda = %s" % (N, "  ".join("%.5f" % v for v in frequency_parameters(N))))
    E, nu, t, rho, a = 2.0e11, 0.3, 0.05, 8000.0, 10.0
    D = E * t ** 3 / (12 * (1 - nu ** 2))
    scale = np.sqrt(D / (rho * t)) / (2 * np.pi * a * a)
    print("FV16, Hz            %s" % "  ".join("%.4f" % v for v in frequency_parameters(22) * scale))
    print("NAFEMS's sources, Hz %s" % "  ".join("%.4f" % v for v in np.array([3.494, 8.547, 21.44, 27.46, 31.17, 54.443]) * scale))
