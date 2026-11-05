"""Prints speed.sh's results, speed_summary.csv, as one Markdown table per problem."""
import csv
import sys

NAMES = {"simp": "SIMP", "beso": "BESO", "multi": "Multi-Material", "multi1": "Multi-Material, steel alone"}
PROBLEMS = {"bridge": "Topology Bridge block, bricks per metre", "truss": "Truss node, bricks a side",
            "joint": "Beam-column joint, bricks a side"}


def main(path):
    rows = list(csv.DictReader(open(path)))
    for problem in [p for p in PROBLEMS if any(r["problem"] == p for r in rows)]:
        print(f"\n**{PROBLEMS[problem]}**\n")
        print("| n | Bricks | Method | Iterations | Total s | Start-up | Solves | Updates | Other | A solve s | An update s "
              "| Within 1% at | Compliance |")
        print("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
        for r in sorted((r for r in rows if r["problem"] == problem), key=lambda r: (int(r["n"]), list(NAMES).index(r["method"]))):
            f = {k: float(r[k]) for k in ("total_s", "startup_s", "solve_s", "update_s", "other_s", "solve_median_s",
                                           "update_median_s", "settled_s", "final_compliance")}
            note = "" if r["converged"] == "1" else f" (broke after {r['broke_after']})" if r["broke_after"] else " (cap)"
            print(f"| {r['n']} | {int(r['bricks']):,} | {NAMES[r['method']]} | {r['iterations']}{note} | {f['total_s']:.1f} "
                  f"| {f['startup_s']:.1f} | {f['solve_s']:.1f} | {f['update_s']:.1f} | {f['other_s']:.1f} "
                  f"| {f['solve_median_s']:.3f} | {f['update_median_s']:.3f} | {r['settled_iteration']}, {f['settled_s']:.1f} s "
                  f"| {f['final_compliance']:.4g} |")
    solve = sum(float(r["solve_s"]) + float(r["startup_s"]) for r in rows)
    total = sum(float(r["total_s"]) for r in rows)
    print(f"\nOpenSees, starting and solving, took {100 * solve / total:.0f}% of the {total / 60:.0f} minutes.")

    # Every method solves the same model, so a solve should take as long whichever calls it: one much slower than
    # the others on its case was slowed by something else on the machine, and its case is worth running again.
    for case in sorted({(r["problem"], r["n"]) for r in rows}):
        solves = sorted(float(r["solve_median_s"]) for r in rows if (r["problem"], r["n"]) == case)
        middle = solves[len(solves) // 2]
        for r in rows:
            if (r["problem"], r["n"]) == case and abs(float(r["solve_median_s"]) - middle) > 0.15 * middle:
                print(f"note: {r['problem']} {r['n']} {r['method']}: a solve took {float(r['solve_median_s']):.3f} s against "
                      f"{middle:.3f} s for the others; the machine was probably busy, so run it again")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "results/speed_summary.csv")
