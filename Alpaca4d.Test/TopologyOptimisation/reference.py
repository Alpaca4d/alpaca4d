"""top3d in numpy, to check Alpaca4d's topology optimisation against.

top3d (Liu and Tovar 2014, "An efficient 3D topology optimization code written in Matlab", SMO
50:1175-1196) solves minimum compliance by SIMP on a grid of eight node bricks: its own stiffness
matrix, its own assembly and its own solver. This is that code, step for step, on the problems
TopologyOptimisation.cs solves through OpenSees, so the two runs share nothing but the problem and the
algorithm. The one change is the one Alpaca4d makes: the bisection for the volume runs on
sensitivities scaled to a largest ratio of one (see TopologyOptimiser.OptimalityCriteria). --top3d
runs top3d's bisection instead, to show what that changes.

    python3 reference.py cantilever|simply-supported <results folder> [--top3d] [--plot]

Reads <name>_history.csv and <name>_densities.csv from the folder, which ./run.sh bench <name> writes,
compares them with its own run and writes <name>_reference.csv. With --plot (and matplotlib) it draws
both designs, as the mean density through the depth, to <name>.png.
"""
import csv
import os
import sys
import time

import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as spla


def brick_stiffness(nu, size=1.0):
    """The eight node brick of unit modulus, by 2x2x2 Gauss points: top3d's lk_H8 for size 1.
    Nodes in OpenSees's order, which is top3d's too: counter-clockwise underneath, then on top."""
    corners = np.array([[-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1],
                        [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1]], dtype=float)
    lam = nu / ((1 + nu) * (1 - 2 * nu))
    mu = 1 / (2 * (1 + nu))
    D = np.zeros((6, 6))
    D[:3, :3] = lam
    D[np.arange(3), np.arange(3)] += 2 * mu
    D[3, 3] = D[4, 4] = D[5, 5] = mu
    g = 1 / np.sqrt(3)
    K = np.zeros((24, 24))
    for xi in (-g, g):
        for eta in (-g, g):
            for zeta in (-g, g):
                dN = np.array([[c[0] * (1 + c[1] * eta) * (1 + c[2] * zeta),
                                c[1] * (1 + c[0] * xi) * (1 + c[2] * zeta),
                                c[2] * (1 + c[0] * xi) * (1 + c[1] * eta)] for c in corners]) / 8
                dN *= 2 / size                     # d/dx = (2/size) d/dxi on a cube
                B = np.zeros((6, 24))
                for a in range(8):
                    x, y, z = dN[a]
                    B[0, 3 * a] = x
                    B[1, 3 * a + 1] = y
                    B[2, 3 * a + 2] = z
                    B[3, 3 * a], B[3, 3 * a + 1] = y, x
                    B[4, 3 * a + 1], B[4, 3 * a + 2] = z, y
                    B[5, 3 * a], B[5, 3 * a + 2] = z, x
                K += B.T @ D @ B * (size / 2) ** 3
    return K


class Grid:
    """nx along the span (x), ny deep (y), nz high (z); x fastest, as TopologyOptimisation.cs numbers them."""

    def __init__(self, nx, ny, nz):
        self.nx, self.ny, self.nz = nx, ny, nz

    def node(self, i, j, k):
        return i + j * (self.nx + 1) + k * (self.nx + 1) * (self.ny + 1)

    @property
    def nodes(self):
        return (self.nx + 1) * (self.ny + 1) * (self.nz + 1)

    def elements(self):
        return [(i, j, k) for k in range(self.nz) for j in range(self.ny) for i in range(self.nx)]

    def corners(self, i, j, k):
        n = self.node
        return [n(i, j, k), n(i + 1, j, k), n(i + 1, j + 1, k), n(i, j + 1, k),
                n(i, j, k + 1), n(i + 1, j, k + 1), n(i + 1, j + 1, k + 1), n(i, j + 1, k + 1)]


def problem(name):
    """The grid, the fixed dofs and the load vector of TopologyOptimisation.cs's problem of that name."""
    if name == "cantilever":
        grid = Grid(60, 4, 20)
        fixed = [3 * grid.node(0, j, k) + d for k in range(grid.nz + 1) for j in range(grid.ny + 1) for d in range(3)]
        loads = [grid.node(grid.nx, j, 0) for j in range(grid.ny + 1)]
    elif name == "simply-supported":
        grid = Grid(120, 4, 20)
        fixed = [3 * grid.node(0, j, 0) + d for j in range(grid.ny + 1) for d in range(3)]
        fixed += [3 * grid.node(grid.nx, j, 0) + d for j in range(grid.ny + 1) for d in (1, 2)]
        loads = [grid.node(grid.nx // 2, j, grid.nz) for j in range(grid.ny + 1)]
    else:
        raise SystemExit(f"unknown problem {name}")
    F = np.zeros(3 * grid.nodes)
    for n in loads:
        F[3 * n + 2] = -1.0
    return grid, np.array(sorted(fixed)), F


def top3d(grid, fixed, F, volfrac=0.3, penal=3.0, rmin=1.5, maxloop=200, tolx=0.01, nu=0.3, scaled=True):
    E0, Emin, move = 1.0, 1e-9, 0.2
    elements = grid.elements()
    nele = len(elements)
    KE = brick_stiffness(nu)
    edof = np.array([[3 * n + d for n in grid.corners(*e) for d in range(3)] for e in elements])
    iK = np.repeat(edof, 24, axis=1).ravel()
    jK = np.tile(edof, (1, 24)).ravel()
    free = np.setdiff1d(np.arange(3 * grid.nodes), fixed)

    # The filter, top3d's: rmin - distance between centroids, here found by brute force over the box.
    reach = int(np.ceil(rmin)) - 1
    rows, cols, vals = [], [], []
    index = {e: n for n, e in enumerate(elements)}
    for e1, (i1, j1, k1) in enumerate(elements):
        for k2 in range(max(k1 - reach, 0), min(k1 + reach, grid.nz - 1) + 1):
            for j2 in range(max(j1 - reach, 0), min(j1 + reach, grid.ny - 1) + 1):
                for i2 in range(max(i1 - reach, 0), min(i1 + reach, grid.nx - 1) + 1):
                    w = rmin - np.sqrt((i1 - i2) ** 2 + (j1 - j2) ** 2 + (k1 - k2) ** 2)
                    if w > 0:
                        rows.append(e1)
                        cols.append(index[(i2, j2, k2)])
                        vals.append(w)
    H = sp.csr_matrix((vals, (rows, cols)), shape=(nele, nele))
    Hs = np.asarray(H.sum(axis=1)).ravel()

    x = np.full(nele, volfrac)
    xPhys = x.copy()
    history = []
    change, loop = 1.0, 0
    while change > tolx and loop < maxloop:
        loop += 1
        t0 = time.time()
        sK = (KE.ravel()[None, :] * (Emin + xPhys ** penal * (E0 - Emin))[:, None]).ravel()
        K = sp.csc_matrix((sK, (iK, jK)), shape=(3 * grid.nodes, 3 * grid.nodes))
        K = (K + K.T) / 2
        U = np.zeros(3 * grid.nodes)
        U[free] = spla.spsolve(K[free][:, free], F[free])
        Ue = U[edof]
        ce = np.einsum("ij,jk,ik->i", Ue, KE, Ue)
        c = float(((Emin + xPhys ** penal * (E0 - Emin)) * ce).sum())
        dc = -penal * (E0 - Emin) * xPhys ** (penal - 1) * ce
        dv = np.ones(nele)
        dc = H @ (dc / Hs)
        dv = H @ (dv / Hs)
        volume = xPhys.mean()

        ratio = -dc / dv
        if scaled:
            ratio = np.maximum(ratio, 0) / np.max(ratio)
        l1, l2 = 0.0, 1e9
        while (l2 - l1) / (l1 + l2) > 1e-3:
            lmid = 0.5 * (l2 + l1)
            xnew = np.maximum(0, np.maximum(x - move, np.minimum(1, np.minimum(x + move, x * np.sqrt(ratio / lmid)))))
            xPhys = (H @ xnew) / Hs
            if xPhys.sum() > volfrac * nele:
                l1 = lmid
            else:
                l2 = lmid
        change = float(np.abs(xnew - x).max())
        x = xnew
        history.append((loop, c, volume, change, time.time() - t0))
        if loop % 10 == 0 or loop == 1:
            print(f"  It.{loop:5d} Obj.:{c:11.4f} Vol.:{volume:7.3f} ch.:{change:7.3f}")
    return history, xPhys


def read_csv(path):
    with open(path) as f:
        return list(csv.DictReader(f))


def main():
    name, folder = sys.argv[1], sys.argv[2]
    scaled = "--top3d" not in sys.argv
    grid, fixed, F = problem(name)
    print(f"reference top3d on {name}: {len(grid.elements())} elements ({'scaled' if scaled else 'top3d'} bisection)")
    t0 = time.time()
    history, xPhys = top3d(grid, fixed, F, scaled=scaled)
    print(f"  {len(history)} iterations in {time.time() - t0:.1f} s, final compliance {history[-1][1]:.6f}")

    suffix = "" if scaled else "_top3d"
    with open(os.path.join(folder, f"{name}_reference{suffix}.csv"), "w") as f:
        f.write("iteration,compliance,volume,change,seconds\n")
        for row in history:
            f.write(",".join(repr(v) for v in row) + "\n")

    ours = read_csv(os.path.join(folder, f"{name}_history.csv"))
    dens = read_csv(os.path.join(folder, f"{name}_densities.csv"))
    alpaca = np.array([float(r["compliance"]) for r in ours])
    reference = np.array([h[1] for h in history])
    m = min(len(alpaca), len(reference))
    relative = np.abs(alpaca[:m] - reference[:m]) / reference[:m]
    x_alpaca = np.array([float(r["density"]) for r in dens])
    print(f"  Alpaca4d: {len(alpaca)} iterations, final compliance {alpaca[-1]:.6f}")
    print(f"  compliance, iteration by iteration: largest relative difference {relative.max():.3e} "
          f"(iteration {int(relative.argmax()) + 1}), first {relative[0]:.3e}")
    print(f"  final densities: largest difference {np.abs(x_alpaca - xPhys).max():.3e}, "
          f"mean {np.abs(x_alpaca - xPhys).mean():.3e}")
    grey = lambda x: 4 * np.mean(x * (1 - x))
    print(f"  non-discreteness 4x(1-x): Alpaca4d {grey(x_alpaca):.3f}, reference {grey(xPhys):.3f}")

    if "--plot" in sys.argv:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        shape = (grid.nz, grid.ny, grid.nx)
        fig, axes = plt.subplots(2, 1, figsize=(10, 2 * 10 * grid.nz / grid.nx + 1))
        for ax, x, title in ((axes[0], x_alpaca, "Alpaca4d + OpenSees"), (axes[1], xPhys, "top3d reference")):
            ax.imshow(x.reshape(shape).mean(axis=1), cmap="gray_r", origin="lower", vmin=0, vmax=1)
            ax.set_title(title, fontsize=10)
            ax.axis("off")
        fig.tight_layout()
        fig.savefig(os.path.join(folder, f"{name}.png"), dpi=150)
        print(f"  drawn to {os.path.join(folder, name + '.png')}")


if __name__ == "__main__":
    main()
