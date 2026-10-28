"""Whether an OpenSees runs the ten node tetrahedron, and runs it right: the cantilever of
cantilever.py, 60 elements, against beam theory - solved with BandSPD, and with system Mumps on
every core where the OpenSees has it.

    OPENSEES=/path/to/OpenSees python3 solver_check.py [--mumps]

Exits 1 when the OpenSees does not know TenNodeTetrahedronStd (3.8.0 and older), when the tip is
off - 3.8.0's element, before OpenSees PR #1833, was six times too soft - or, with --mumps, when
it has no system Mumps. Needs Python alone, so the workflow runs it on Windows too."""
import os
import pathlib
import re
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
OPENSEES = os.environ.get("OPENSEES", "OpenSees")
BEAM = 0.4024     # beam theory, with shear: see cantilever.py
GOT = 0.4017793   # what the 60 ten node elements give, 0.2 % under it


def solve(deck, system):
    """The tip deflection with `system`, or None, and what OpenSees printed."""
    run = deck.with_name("run.tcl")
    run.write_text(deck.read_text().replace("system BandSPD\n", f"system {system}\n"))
    out = subprocess.run([OPENSEES, str(run)], capture_output=True, text=True, cwd=deck.parent)
    log = out.stdout + out.stderr
    found = re.search(r"TIP_UZ (\S+)", log)
    return (abs(float(found.group(1))) if found else None), log


def main():
    mumps = "--mumps" in sys.argv[1:]
    fails = 0
    with tempfile.TemporaryDirectory() as folder:
        deck = pathlib.Path(folder) / "cantilever10.tcl"
        subprocess.run([sys.executable, str(HERE / "cantilever.py"), "tet10", "10", "1", "1", str(deck)], check=True)

        for system in ["BandSPD", "Mumps -numThreads 0"]:
            tip, log = solve(deck, system)
            if tip is None:
                if "TenNodeTetrahedronStd not known" in log:
                    print(f"  [FAIL] {system}: this OpenSees does not know TenNodeTetrahedronStd - "
                          "it needs a master build since 16 October 2026")
                    fails += 1
                    break
                if system.startswith("Mumps") and not mumps:
                    print(f"  [SKIP] {system}: this OpenSees has no system Mumps")
                    continue
                print(f"  [FAIL] {system}: no tip deflection printed\n" + log[-800:])
                fails += 1
                continue

            ok = abs(tip / GOT - 1) < 1e-4
            print(f"  [{'PASS' if ok else 'FAIL'}] {system}: tip {tip:.7f}, {100 * (tip / BEAM - 1):+.2f} % "
                  f"against beam theory's {BEAM}" + ("" if ok else f" - want {GOT}"))
            fails += 0 if ok else 1

    print("All passed." if fails == 0 else f"{fails} failed.")
    return 0 if fails == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
