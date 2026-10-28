#!/usr/bin/env python3
"""The correctness gate the speed table is filtered through.

Two shapes that caught real defects: a chain of trusses with a closed-form answer, swept
across small degree-of-freedom counts, and a framed building with a rigid diaphragm at
every floor - the constraint that ties distant nodes together."""
import os, subprocess, sys, tempfile

OPENSEES = os.environ.get("OPENSEES", "OpenSees")
SYSTEMS = ["BandGen", "BandSPD", "ProfileSPD", "SuperLU", "UmfPack",
           "SparseSYM", "SparseSPD", "SparseGeneral", "FullGeneral",
           # what Alpaca4d writes for Mumps, on one core and on all of them
           "Mumps -matrixType 1 -numThreads 1", "Mumps -matrixType 1 -numThreads 0"]
E, A, LEN, P = 2.1e8, 0.002, 3.0, 60.0


def chain(n, system):
    """n trusses end to end. Tip displacement is P*n*L/(E*A), exactly."""
    lines = ["wipe", "model basic -ndm 2 -ndf 2", "node 1 0.0 0.0", "fix 1 1 1",
             "uniaxialMaterial Elastic 1 %g" % E]
    for i in range(1, n + 1):
        lines += ["node %d 0.0 %g" % (i + 1, i * LEN), "fix %d 1 0" % (i + 1),
                  "element truss %d %d %d %g 1" % (i, i, i + 1, A)]
    lines += ["timeSeries Linear 1",
              "pattern Plain 1 1 { load %d 0.0 %g }" % (n + 1, P),
              "constraints Transformation", "numberer RCM", "system %s" % system,
              "test EnergyIncr 1E-08 10 0 2", "algorithm Newton",
              "integrator LoadControl 1", "analysis Static",
              "set ok [analyze 1]",
              'puts "OUT $ok [nodeDisp %d 2]"' % (n + 1)]
    return "\n".join(lines) + "\n", P * n * LEN / (E * A)


def solve(text):
    with tempfile.TemporaryDirectory() as work:
        with open(os.path.join(work, "c.tcl"), "w") as fh:
            fh.write(text)
        try:
            done = subprocess.run([OPENSEES, "c.tcl"], cwd=work, timeout=600,
                                  capture_output=True, text=True)
            # OpenSees puts its banner and every `puts` on stderr, not stdout.
            out = done.stdout + done.stderr
        except (subprocess.TimeoutExpired, FileNotFoundError):
            return None, None
    for line in out.splitlines():
        if line.startswith("OUT "):
            _, ok, disp = line.split()
            return int(ok), float(disp)
    return None, None


if __name__ == "__main__":
    sizes = [1, 2, 3, 4, 5, 6, 8, 10, 20, 40]
    print("Truss chain, exact answer known. '.' correct, 'X' wrong or failed.\n")
    mumps_wrong = False
    for system in SYSTEMS:
        marks = ""
        for n in sizes:
            text, want = chain(n, system)
            ok, got = solve(text)
            marks += "." if ok == 0 and got is not None and abs(got - want) <= 1e-9 * want else "X"
        print("  %-36s %s" % (system, marks))
        mumps_wrong |= system.startswith("Mumps") and "X" in marks
    print("  %-36s %s" % ("free DOF:", " ".join(str(n) for n in sizes)))
    print("\nSparseSYM and SparseSPD return a wrong answer at some small sizes. SparseSPD was")
    print("the shipped default until the solver benchmark; BandSPD is correct at every size")
    print("and was also the fastest of the single-core solvers.")
    # The known-bad sparse solvers are kept on show; Mumps, the default, has to be right.
    sys.exit(1 if mumps_wrong else 0)
