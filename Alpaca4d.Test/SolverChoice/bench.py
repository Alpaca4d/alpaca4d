#!/usr/bin/env python3
"""Times every `system` and every `algorithm` OpenSees offers, on models shaped like the
ones Alpaca4d builds, and checks the answer as well as the clock - a fast wrong answer is
not a fast answer. Prints the table the defaults in AnalysisSettings were chosen from."""
import os, subprocess, sys, tempfile, time
import models

OPENSEES = os.environ.get("OPENSEES", "OpenSees")

SYSTEMS = ["BandGen", "BandSPD", "ProfileSPD", "SuperLU", "UmfPack",
           "SparseSYM", "SparseSPD", "SparseGeneral", "FullGeneral",
           # only on an OpenSees built with it (Tools/OpenSees); "did not run" on another
           "Mumps -matrixType 1 -numThreads 0"]
ALGORITHMS = ["Linear", "Newton", "ModifiedNewton", "KrylovNewton",
              "NewtonLineSearch", "SecantNewton", "BFGS", "Broyden"]

# Model, size, and how many solves to average over.
CASES = [("frame", 5, 10), ("shell", 30, 10), ("brick", 12, 10)]


def deck(kind, arg, system, algorithm, reps):
    lines, probe, _ = {"frame": lambda a: models.frame(a, a, a),
                       "shell": models.shell,
                       "brick": models.brick}[kind](arg)
    lines += [
        "constraints Transformation",
        "numberer RCM",
        "system %s" % system,
        # test before algorithm: KrylovNewton and the line-search algorithms read the
        # convergence test at construction, which is the order Settings.WriteTcl uses.
        "test EnergyIncr 1E-08 10 0 2",
        "algorithm %s" % algorithm,
        "integrator LoadControl 1",
        "analysis Static",
        "set t0 [clock milliseconds]",
        "for {set r 0} {$r < %d} {incr r} { set ok [analyze 1] }" % reps,
        "set t1 [clock milliseconds]",
        'puts "BENCH $ok [expr ($t1-$t0)/double(%d)] [nodeDisp %d 3]"' % (reps, probe),
    ]
    return "\n".join(lines) + "\n"


def run(kind, arg, system, algorithm, reps):
    with tempfile.TemporaryDirectory() as work:
        path = os.path.join(work, "case.tcl")
        with open(path, "w") as fh:
            fh.write(deck(kind, arg, system, algorithm, reps))
        try:
            done = subprocess.run([OPENSEES, "case.tcl"], cwd=work, timeout=900,
                                  capture_output=True, text=True)
            # OpenSees puts its banner and every `puts` on stderr, not stdout.
            out = done.stdout + done.stderr
        except (subprocess.TimeoutExpired, FileNotFoundError):
            return None, None, None
    for line in out.splitlines():
        if line.startswith("BENCH "):
            _, ok, ms, disp = line.split()
            return int(ok), float(ms), float(disp)
    return None, None, None


def table(title, kind, arg, reps, variants, fixed, vary):
    print("### %s - %s %d" % (title, kind, arg))
    rows, reference = [], None
    for v in variants:
        ok, ms, disp = run(kind, arg, v if vary == "system" else fixed,
                           v if vary == "algorithm" else fixed, reps)
        if ok is None:
            print("  %-18s did not run" % v)
            continue
        if reference is None and ok == 0:
            reference = disp
        # A wrong answer disqualifies a solver however fast it was.
        agrees = reference is not None and abs(disp - reference) <= 1e-9 * abs(reference)
        rows.append((ms, v, ok, disp, agrees))
        print("  %-18s %9.1f ms   %s   disp % .17e"
              % (v, ms, "ok " if ok == 0 and agrees else "BAD", disp))
    good = sorted(r for r in rows if r[2] == 0 and r[4])
    if good:
        print("  -> fastest correct: %s at %.1f ms" % (good[0][1], good[0][0]))
    print()


if __name__ == "__main__":
    print("OpenSees: %s\n" % OPENSEES)
    for kind, arg, reps in CASES:
        table("systems, algorithm Newton", kind, arg, reps, SYSTEMS, "Newton", "system")
    for kind, arg, reps in CASES:
        table("algorithms, system BandSPD", kind, arg, reps, ALGORITHMS, "BandSPD", "algorithm")
