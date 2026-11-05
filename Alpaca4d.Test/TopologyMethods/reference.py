"""BESO in numpy, to check Alpaca4d's BesoOptimiser against.

esoL.m (Xia, Xia, Huang and Xie 2018, "Bi-directional evolutionary structural optimization on advanced
structures and materials: a comprehensive review", Arch Computat Methods Eng 25:437-478) step for step, in
three dimensions on the eight node bricks of ../TopologyOptimisation/reference.py - its own stiffness, its
own assembly and SciPy's solver - in the soft-kill form BesoOptimiser uses: a void element has
x_min^p of its stiffness, x_min = 1e-9^(1/3). Only the problem and the algorithm are shared with the run
through OpenSees.

    python3 reference.py beso-3d|beso-cantilever <results folder> [nx nz ny] [--plot]

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

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "TopologyOptimisation"))
from reference import brick_stiffness, Grid  # noqa: E402


def problem(name, nx, nz, ny):
    """TopologyMethods.cs's problems: its cantilever (load at the bottom of the free end) and esoL's clamped
    cantilever (load at the middle of the free end), held along x = 0."""
    grid = Grid(nx, ny, nz)
    fixed = [3 * grid.node(0, j, k) + d for k in range(nz + 1) for j in range(ny + 1) for d in range(3)]
    F = np.zeros(3 * grid.nodes)
    if name == "beso-3d":
        for j in range(ny + 1):
            F[3 * grid.node(nx, j, 0) + 2] = -1.0
    else:
        for j in range(ny + 1):
            F[3 * grid.node(nx, j, nz // 2) + 2] = -1.0 / (ny + 1)
    return grid, np.array(sorted(fixed)), F


def beso(grid, fixed, F, volfrac, er, rmin, penal=3.0, emin=1e-9, maxloop=300, tol=1e-3, nu=0.3):
    xmin = emin ** (1.0 / penal)
    elements = grid.elements()
    nele = len(elements)
    KE = brick_stiffness(nu)
    edof = np.array([[3 * n + d for n in grid.corners(*e) for d in range(3)] for e in elements])
    iK = np.repeat(edof, 24, axis=1).ravel()
    jK = np.tile(edof, (1, 24)).ravel()
    free = np.setdiff1d(np.arange(3 * grid.nodes), fixed)

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

    def target(iteration):
        vol = 1.0
        for _ in range(iteration):
            vol = max(vol * (1.0 - er), volfrac)
        return vol

    x = np.ones(nele)
    olddc = None
    history = []
    for loop in range(1, maxloop + 1):
        t0 = time.time()
        vol, reached = target(loop), target(loop - 1) <= volfrac
        E = x ** penal
        K = sp.csc_matrix(((KE.ravel()[None, :] * E[:, None]).ravel(), (iK, jK)), shape=(3 * grid.nodes, 3 * grid.nodes))
        K = (K + K.T) / 2
        U = np.zeros(3 * grid.nodes)
        U[free] = spla.spsolve(K[free][:, free], F[free])
        Ue = U[edof]
        energy = E * np.einsum("ij,jk,ik->i", Ue, KE, Ue)
        c = float(energy.sum())
        dc = (H @ (energy / x)) / Hs
        if olddc is not None:
            dc = 0.5 * (dc + olddc)
        olddc = dc
        cs = [h[1] for h in history] + [c]
        change = abs(sum(cs[-10:-5]) - sum(cs[-5:])) / sum(cs[-5:]) if len(history) >= 10 else float("nan")
        history.append((loop, c, float((x == 1.0).mean()), change if reached else float("nan"), time.time() - t0))
        l1, l2 = dc.min(), dc.max()
        while (l2 - l1) / (l1 + l2) > 1e-9:
            th = 0.5 * (l1 + l2)
            xnew = np.where(dc > th, 1.0, xmin)
            if (xnew == 1.0).mean() - vol > 0:
                l1 = th
            else:
                l2 = th
        x = xnew
        if loop % 10 == 0 or loop == 1:
            print(f"  It.{loop:5d} Obj.:{c:11.4f} Vol.:{history[-1][2]:7.3f} ch.:{change:7.4f}")
        if reached and change <= tol:
            break
    return history, x


def read_csv(path):
    with open(path) as f:
        return list(csv.DictReader(f))


def main():
    name, folder = sys.argv[1], sys.argv[2]
    numbers = [int(a) for a in sys.argv[3:] if a.isdigit()]
    flat = name == "beso-cantilever"
    nx, nz = (numbers[0], numbers[1]) if len(numbers) >= 2 else ((100, 50) if flat else (40, 20))
    ny = 1 if flat else (numbers[2] if len(numbers) >= 3 else 4)
    volfrac, rmin = (0.6, 6.0) if flat else (0.4, 3.0)
    grid, fixed, F = problem(name, nx, nz, ny)
    print(f"reference: BESO in numpy, {name} {nx} x {nz} x {ny}, volume {volfrac}, rmin {rmin}")
    t0 = time.time()
    history, x = beso(grid, fixed, F, volfrac, 0.02, rmin)
    print(f"  {len(history)} iterations in {time.time() - t0:.1f} s")

    with open(os.path.join(folder, name + "_reference.csv"), "w") as f:
        f.write("iteration,compliance,volume,change\n")
        for it, c, v, ch, _ in history:
            f.write(f"{it},{c!r},{v!r},{ch!r}\n")

    ours = read_csv(os.path.join(folder, name + "_history.csv"))
    densities = np.array([float(line.strip()) for line in open(os.path.join(folder, name + "_densities.csv"))])
    n = min(len(ours), len(history))
    gaps = [abs(float(ours[k]["compliance"]) - history[k][1]) / history[k][1] for k in range(n)]
    print(f"  iterations: Alpaca {len(ours)}, reference {len(history)}")
    print(f"  first compliance: Alpaca {float(ours[0]['compliance']):.10g}, reference {history[0][1]:.10g}, {gaps[0]:.2g} apart")
    print(f"  last compliance: Alpaca {float(ours[n - 1]['compliance']):.10g}, reference {history[n - 1][1]:.10g}")
    print(f"  largest gap over the run: {max(gaps):.2g} (iteration {int(np.argmax(gaps)) + 1})")
    same = int(((densities == 1.0) == (x == 1.0)).sum())
    print(f"  final designs: {same} of {len(x)} elements the same")

    if "--plot" in sys.argv:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        def side(d):
            return d.reshape(grid.nz, grid.ny, grid.nx).mean(axis=1)
        fig, axes = plt.subplots(1, 2, figsize=(10, 3))
        for ax, d, title in [(axes[0], densities, "Alpaca4d (OpenSees)"), (axes[1], x, "reference (numpy)")]:
            ax.imshow(side(d), origin="lower", cmap="Greys", vmin=0, vmax=1)
            ax.set_title(title)
            ax.axis("off")
        fig.savefig(os.path.join(folder, name + ".png"), dpi=150, bbox_inches="tight")


if __name__ == "__main__":
    main()
