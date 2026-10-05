using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Design
{
    /// <summary>The six section forces at one integration section, as Read.ForceBeamColumn names them.</summary>
    public struct SectionForces
    {
        /// <summary>Axial force, positive in tension.</summary>
        public double N;
        public double Vy, Vz, T, My, Mz;

        public SectionForces(double n, double vy, double vz, double t, double my, double mz)
        {
            N = n; Vy = vy; Vz = vz; T = t; My = my; Mz = mz;
        }
    }

    /// <summary>
    /// How much of its resistance a member uses, check by check: 1 is exactly at the limit. Filled by
    /// the check of whatever the member is made of - steel today - so that one component can report on
    /// a model of mixed materials in one set of outputs.
    ///
    /// A check that does not apply, or could not be made, is null rather than zero: a member left
    /// unchecked must not read as one that passed.
    /// </summary>
    public class MemberUtilisation
    {
        /// <summary>"Steel", "Timber", ... from the material's design grade; "Unknown" when it has none.</summary>
        public string Type = "Unknown";

        /// <summary>Whether the checks ran. False for a Class 4 section, a section with no shape, or a material with no grade.</summary>
        public bool Checked;

        /// <summary>EN 1993-1-1 cross-section class, the worst along the member.</summary>
        public int? Class;

        /// <summary>
        /// The class member buckling is judged by: Table 5.2 alone, which 5.5.2(10) requires. It can
        /// be 4 where <see cref="Class"/> is 3, and then buckling cannot be checked.
        /// </summary>
        public int? BucklingClass;

        /// <summary>
        /// Some part failed Class 3 by Table 5.2 and was taken as Class 3 under 5.5.2(9), at the low
        /// stress it carries: checked elastically, but worth knowing.
        /// </summary>
        public bool LowStressClass3;

        public double? Axial;
        public double? ShearY;
        public double? ShearZ;
        public double? Torsion;
        public double? BendingY;
        public double? BendingZ;
        /// <summary>Bending and axial force together, with the effect of shear.</summary>
        public double? Combined;
        public double? BucklingY;
        public double? BucklingZ;

        /// <summary>Everything worked out on the way, for checking by hand.</summary>
        public string Report = "";

        /// <summary>
        /// Checks that apply to the member but could not be made - buckling of a section that is Class 4
        /// in compression, say. While any are missing there is no <see cref="Max"/>: a largest value
        /// over the checks that were made would read as a verdict on the member, and it is not one.
        /// </summary>
        public List<string> Missing = new List<string>();

        /// <summary>The checks in the order they are reported, by name.</summary>
        public IEnumerable<KeyValuePair<string, double?>> Checks
        {
            get
            {
                yield return new KeyValuePair<string, double?>("Axial", Axial);
                yield return new KeyValuePair<string, double?>("ShearY", ShearY);
                yield return new KeyValuePair<string, double?>("ShearZ", ShearZ);
                yield return new KeyValuePair<string, double?>("Torsion", Torsion);
                yield return new KeyValuePair<string, double?>("BendingY", BendingY);
                yield return new KeyValuePair<string, double?>("BendingZ", BendingZ);
                yield return new KeyValuePair<string, double?>("Combined", Combined);
                yield return new KeyValuePair<string, double?>("BucklingY", BucklingY);
                yield return new KeyValuePair<string, double?>("BucklingZ", BucklingZ);
            }
        }

        /// <summary>The largest utilisation, or null when nothing was checked or a check that applies is missing.</summary>
        public double? Max
        {
            get
            {
                if (Missing.Count > 0) return null;
                var values = Checks.Where(c => c.Value.HasValue).Select(c => c.Value.Value).ToList();
                return values.Count > 0 ? values.Max() : (double?)null;
            }
        }

        /// <summary>The name of the check that gives <see cref="Max"/>, or what was not checked.</summary>
        public string Governing
        {
            get
            {
                if (Missing.Count > 0) return "Not checked: " + string.Join(", ", Missing);
                var checks = Checks.Where(c => c.Value.HasValue).ToList();
                if (checks.Count == 0) return null;
                return checks.Aggregate((a, b) => b.Value.Value > a.Value.Value ? b : a).Key;
            }
        }
    }
}
