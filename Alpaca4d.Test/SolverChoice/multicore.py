#!/usr/bin/env python3
"""Multi-core solver benchmark: the sequential solvers against `system Mumps` on one core
and on all of them, on small, large and very large frames, slabs and solid blocks.

Every run is checked against the UmfPack answer first - a fast wrong answer is not a fast
answer - and then timed. The time is the whole `analyze`, assembly included, with the
Alpaca4d defaults around it (Transformation, RCM, EnergyIncr, Newton), so it is what a
Run Analysis waits for on the solve. Total is the process from start to exit.

A banded solver stores n x bandwidth numbers, which on a very large solid is more memory
than a laptop has; those runs are skipped, not attempted, when the band alone would pass
--memory gigabytes, and any other solve is stopped once it grows past it. Peak memory is
sampled while the solver runs (psutil if installed, ps otherwise; not on Windows without
psutil)."""
import argparse, os, platform, subprocess, sys, tempfile, time
import models

OPENSEES = os.environ.get("OPENSEES", "OpenSees")

# tier, kind, size: frame n is an n x n x n-bay building, shell n an n x n slab, brick n an n^3 block
CASES = [
    ("small", "frame", 5), ("small", "shell", 30), ("small", "brick", 8),
    ("large", "frame", 15), ("large", "shell", 120), ("large", "brick", 24),
    ("very large", "frame", 25), ("very large", "shell", 200), ("very large", "brick", 36),
]


def systems(threads):
    """Label and `system` line of every solver timed. Mumps runs on one core, on the
    thread counts asked for, and on every core (-numThreads 0 leaves it to OpenMP)."""
    rows = [("BandSPD", "system BandSPD"), ("UmfPack", "system UmfPack"),
            ("Mumps 1 core", "system Mumps -matrixType 1 -numThreads 1")]
    rows += [("Mumps %d cores" % t, "system Mumps -matrixType 1 -numThreads %d" % t) for t in threads]
    rows.append(("Mumps all cores", "system Mumps -matrixType 1 -numThreads 0"))
    return rows


def band_gb(kind, n):
    """Memory of the band alone after RCM, from the mesh: the widest node layer times its DOF."""
    if kind == "frame":
        dof, width = models.frame(n, n, n)[2], (n + 1) * (n + 1) * 6
    elif kind == "shell":
        dof, width = models.shell(n)[2], (n + 1) * 6
    else:
        dof, width = models.brick(n)[2], (n + 1) * (n + 1) * 3
    return dof * (width + 1) * 8 / 1e9


def deck(kind, n, system_line):
    lines, probe, dof = {"frame": lambda a: models.frame(a, a, a),
                         "shell": models.shell, "brick": models.brick}[kind](n)
    lines += [
        "constraints Transformation",
        "numberer RCM",
        system_line,
        "test EnergyIncr 1E-08 10 0 2",
        "algorithm Newton",
        "integrator LoadControl 1",
        "analysis Static",
        "set t0 [clock milliseconds]",
        "set ok [analyze 1]",
        "set t1 [clock milliseconds]",
        'puts "BENCH $ok [expr $t1-$t0] [nodeDisp %d 3]"' % probe,
    ]
    return "\n".join(lines) + "\n", dof


def rss_gb(pid):
    """Resident memory of a running process, or None where it cannot be read."""
    try:
        import psutil
        return psutil.Process(pid).memory_info().rss / 1e9
    except ImportError:
        pass
    except Exception:
        return None
    if os.name == "nt":
        return None
    try:
        kb = subprocess.run(["ps", "-o", "rss=", "-p", str(pid)],
                            capture_output=True, text=True).stdout.strip()
        return int(kb) * 1024 / 1e9 if kb else None
    except (OSError, ValueError):
        return None


def run(text, timeout, memory):
    """Runs one deck, sampling its memory: a solver past `memory` gigabytes is stopped
    rather than left to push the machine into swap."""
    with tempfile.TemporaryDirectory() as work:
        with open(os.path.join(work, "case.tcl"), "w") as fh:
            fh.write(text)
        # OpenSees puts its banner and every `puts` on stderr; a file, unlike a pipe,
        # cannot fill up and stall it while it is being sampled.
        with open(os.path.join(work, "out.txt"), "w") as out:
            start = time.perf_counter()
            proc = subprocess.Popen([OPENSEES, "case.tcl"], cwd=work, stdout=out, stderr=out)
            peak, stopped = None, None
            while proc.poll() is None:
                rss = rss_gb(proc.pid)
                if rss is not None:
                    peak = max(peak or 0.0, rss)
                    if rss > memory:
                        stopped = "stopped at %.1f GB" % rss
                if stopped or time.perf_counter() - start > timeout:
                    proc.kill()
                    proc.wait()
                    return {"status": stopped or "timeout"}
                time.sleep(0.05)
            total = time.perf_counter() - start
        with open(os.path.join(work, "out.txt")) as fh:
            output = fh.read()
    for line in output.splitlines():
        if line.startswith("BENCH "):
            _, ok, ms, disp = line.split()
            return {"status": "ok" if ok == "0" else "failed", "code": int(ok), "ms": float(ms),
                    "disp": float(disp), "total": total, "rss": peak}
    return {"status": "error", "message": " | ".join(output.strip().splitlines()[-3:])}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--tiers", default="small,large,very large",
                        help="comma-separated: small, large, very large")
    parser.add_argument("--threads", default="", help="extra Mumps thread counts, e.g. 2,4,8")
    parser.add_argument("--memory", type=float, default=6.0,
                        help="gigabytes one solve may use: a banded solve whose band alone needs more "
                             "is skipped, and any solve that grows past it is stopped")
    parser.add_argument("--timeout", type=float, default=900)
    args = parser.parse_args()

    tiers = [t.strip() for t in args.tiers.split(",")]
    threads = [int(t) for t in args.threads.split(",") if t.strip()]
    print("OpenSees: %s on %s, %d logical cores\n" % (OPENSEES, platform.platform(), os.cpu_count()))

    failures = 0
    for tier, kind, n in CASES:
        if tier not in tiers:
            continue
        rows = []
        for label, line in systems(threads):
            text, dof = deck(kind, n, line)
            if label == "BandSPD" and band_gb(kind, n) > args.memory:
                rows.append((label, {"status": "skipped (band %.1f GB)" % band_gb(kind, n)}))
                continue
            rows.append((label, run(text, args.timeout, args.memory)))
        # The general sparse LU is the answer; where it ran out of memory, one-core Mumps is.
        reference, referee = dict(rows)["UmfPack"], "UmfPack"
        if reference.get("status") != "ok":
            reference, referee = dict(rows)["Mumps 1 core"], "Mumps 1 core"

        print("### %s %s %d - %d DOF, answers checked against %s" % (tier, kind, n, dof, referee))
        print("| system | analyze | total | peak memory | answer |")
        print("| --- | ---: | ---: | ---: | --- |")
        for label, r in rows:
            # Mumps is what is being vetted: it has to solve, and agree. Another solver running out
            # of time or memory, or failing outright, is a measurement; a crash is a defect anywhere.
            vetted = label.startswith("Mumps")
            if r["status"] not in ("ok", "failed"):
                print("| %s | %s | | | |" % (label, r.get("message", r["status"])))
                if r["status"] == "error" or vetted:
                    failures += 1
                continue
            rss = "%.2f GB" % r["rss"] if r.get("rss") else ""
            if r["status"] == "failed":
                print("| %s | failed after %.0f ms | %.2f s | %s | analyze returned %d |" % (
                    label, r["ms"], r["total"], rss, r["code"]))
                failures += vetted
                continue
            same = (reference.get("status") == "ok"
                    and abs(r["disp"] - reference["disp"]) <= 1e-9 * abs(reference["disp"]))
            failures += not same
            print("| %s | %.0f ms | %.2f s | %s | %s |" % (
                label, r["ms"], r["total"], rss, "same" if same else "DIFFERENT %.6e" % r["disp"]))
        print()
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
