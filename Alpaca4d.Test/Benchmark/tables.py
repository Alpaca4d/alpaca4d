# The documentation's benchmark tables, from the results.json that benchmarks.py writes. Plain
# Python, run outside Rhino:
#     python3 tables.py [results.json]
# prints the tables of doc/benchmark/simple-beam.md, simple-shell.md and simple-brick.md with
# Alpaca4d's values filled in. See README.md.
import json, math, os, sys, tempfile

path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), "alpaca4d-benchmark", "results.json")
R = json.load(open(path))

# NAFEMS FV16 by Kirchhoff theory, converged: fv16_reference.py, 22 x 22 trial functions.
FV16_LAMBDA = [3.47101, 8.50629, 21.28409, 27.19874, 30.95460, 54.18437]


def diff(value, reference):
    d = 100 * (value / reference - 1)
    return "0.00%" if abs(d) < 0.005 else ("%+.2f%%" % d).replace("-", "−")


def table(header, rows):
    lines = ["| " + " | ".join(header) + " |", "| " + " | ".join("---" for _ in header) + " |"]
    lines += ["| " + " | ".join(str(c) for c in row) + " |" for row in rows]
    return "\n".join(lines)


def mm(metres, digits=4):
    return "%.*f mm" % (digits, metres * 1000)


def section(title, body):
    print("### " + title + "\n\n" + body + "\n")


# ------------------------------------------------------------------------------------ beams

for key, label in (("beam-simply", "Simply supported"), ("beam-cantilever", "Cantilever")):
    r, t = R[key], R[key]["theory"]
    section(label, table([label, "Beam theory", "Alpaca4d", "Difference"], [
        ("Shear force", "%.2f kN" % t["V"], "%.2f kN" % r["V"], diff(r["V"], t["V"])),
        ("Bending moment", "%.2f kNm" % t["M"], "%.2f kNm" % r["M"], diff(r["M"], t["M"])),
        ("Displacement, bending and shear", mm(t["db"] + t["ds"]), mm(r["d"]), diff(r["d"], t["db"] + t["ds"])),
        ("Displacement, bending only", mm(t["db"]), mm(r["d"]), diff(r["d"], t["db"]) + " — the shear term")]))

coarse, fine = R["beam-vibration"], R["beam-vibration:n=40"]
rows = []
for i, th in enumerate(coarse["theory"]):
    a, b = coarse["f"][2 * i], fine["f"][2 * i]
    rows.append(("%d and %d" % (2 * i + 1, 2 * i + 2), "%.3f Hz" % th, "%.3f Hz" % a, diff(a, th), "%.3f Hz" % b, diff(b, th)))
section("Cantilever, natural vibration",
        table(["Modes", "Euler-Bernoulli", "Alpaca4d, 20 elements", "Difference", "Alpaca4d, 40 elements", "Difference"], rows))

# ------------------------------------------------------------------------------------ shells

rows = []
for key, mesh in (("plate-pressure:n=8", "8 × 8"), ("plate-pressure", "16 × 16 — the definition"), ("plate-pressure:n=32", "32 × 32"),
                  ("plate-pressure:support=soft", "16 × 16, soft support"), ("plate-pressure:n=32,support=soft", "32 × 32, soft support")):
    r = R[key]
    rows.append((mesh, "%.4f · 10⁻⁴ m" % (r["theory"]["d"] * 1e4), "%.4f · 10⁻⁴ m" % (r["d"] * 1e4), diff(r["d"], r["theory"]["d"])))
section("Plate under pressure, centre deflection", table(["ASDShellQ4 mesh", "Plate theory", "Alpaca4d", "Difference"], rows))

coarse, fine = R["plate-vibration"], R["plate-vibration:n=32"]
section("FV16 against NAFEMS", table(["Mode", "NAFEMS", "Alpaca4d, 16 × 16", "Difference"],
        [(i + 1, "%.3f Hz" % th, "%.3f Hz" % f, diff(f, th)) for i, (f, th) in enumerate(zip(coarse["f"], coarse["theory"]))]))
E, nu, t, rho, a = 2.0e11, 0.3, 0.05, 8000.0, 10.0
scale = math.sqrt(E * t ** 3 / (12 * (1 - nu ** 2)) / (rho * t)) / (2 * math.pi * a * a)
rows = []
for i, lam in enumerate(FV16_LAMBDA):
    th = lam * scale
    rows.append((i + 1, "%.4f Hz" % th, "%.4f Hz" % coarse["f"][i], diff(coarse["f"][i], th), "%.4f Hz" % fine["f"][i], diff(fine["f"][i], th)))
section("FV16 against converged plate theory",
        table(["Mode", "Plate theory, converged", "Alpaca4d, 16 × 16", "Difference", "Alpaca4d, 32 × 32", "Difference"], rows))

# ------------------------------------------------------------------------------------ bricks

rows = []
for key, model in (("brick-cantilever", "20 × 4 × 4, ν = 0.3 — the definition"), ("brick-cantilever:n=40,m=8", "40 × 8 × 8, ν = 0.3"),
                   ("brick-cantilever:nu=0", "20 × 4 × 4, ν = 0")):
    r, th = R[key], R[key]["theory"]
    rows.append((model, mm(th["db"] + th["ds"]), mm(r["d_centre"]), diff(r["d_centre"], th["db"] + th["ds"])))
section("Brick cantilever, tip deflection", table(["SSP bricks", "Timoshenko", "Alpaca4d", "Difference"], rows))

r, fine = R["solid-plate-vibration"], R["solid-plate-vibration:n=16,layers=4"]
rigid = max(abs(f) for f in r["f"][:3])
rows = [("1 to 3", "0 — rigid body", "0 — rigid body, below 10⁻⁴ Hz" if rigid < 1e-4 else "%.1e Hz" % rigid, "—", "—")]
for i in range(3, 10):
    # NAFEMS gives five figures; so does the table.
    rows.append((i + 1, "%.5g Hz" % r["theory"][i], "%.5g Hz" % r["f"][i], diff(r["f"][i], r["theory"][i]), "%.5g Hz" % fine["f"][i]))
section("FV52", table(["Mode", "NAFEMS", "Alpaca4d, 8 × 8 × 3", "Difference", "16 × 16 × 4"], rows))
