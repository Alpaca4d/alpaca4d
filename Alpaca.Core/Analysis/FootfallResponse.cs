using System;
using System.Linq;

namespace Alpaca4d
{
    /// <summary>
    /// What a footfall analysis found, kept on the model Footfall Analysis hands on: at each node it
    /// read, by the node's tag, the response factor R, the steady-state and transient parts of it and
    /// their accelerations, and the pace that gives it. The Report draws the model coloured by any of
    /// them and lists the worst nodes.
    /// </summary>
    public sealed class FootfallResponse
    {
        /// <summary>What can be drawn and sorted by, by the names Report View and a figure give them.</summary>
        public static readonly string[] Quantities =
        {
            "Response factor R", "Steady-state R", "Transient R", "Steady-state acceleration", "Transient acceleration"
        };

        /// <summary>The nodes the response was read at, by their tags.</summary>
        public int[] Nodes = new int[0];

        /// <summary>The response factor R at each node, the larger of the steady state and the transient: 1 is the threshold of perception.</summary>
        public double[] Factors = new double[0];

        /// <summary>The pace that gives each node its response factor [Hz].</summary>
        public double[] CriticalFrequencies = new double[0];

        /// <summary>The steady-state response factor at each node, from the paces whose harmonics resonate with the modes.</summary>
        public double[] SteadyStateFactors = new double[0];

        /// <summary>The transient response factor at each node, from each footstep's impulse; zero where it was not checked.</summary>
        public double[] TransientFactors = new double[0];

        /// <summary>The steady-state weighted rms acceleration at each node [m/s²].</summary>
        public double[] SteadyStateAccelerations = new double[0];

        /// <summary>The transient weighted rms acceleration at each node [m/s²].</summary>
        public double[] TransientAccelerations = new double[0];

        /// <summary>Whether the transient response was checked: not for a stair, nor for a floor above the cut-off.</summary>
        public bool TransientChecked = true;

        /// <summary>Walking or Stairs.</summary>
        public string Activity = "";

        /// <summary>The damping ratio of every mode: 0.03 is 3%.</summary>
        public double Damping;

        /// <summary>The quantity <paramref name="name"/> names, as <see cref="Quantities"/> spells it; null for none.</summary>
        public static string Quantity(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Quantities[0];
            return Quantities.FirstOrDefault(q => string.Equals(q, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The unit a quantity is given in: none for a response factor, mm/s² for an acceleration, as R = 1 is 5 mm/s².</summary>
        public static string UnitOf(string quantity) => (quantity ?? "").EndsWith("acceleration", StringComparison.OrdinalIgnoreCase) ? "mm/s²" : "";

        /// <summary>Whether the quantity is of the transient response.</summary>
        public static bool IsTransient(string quantity) => (quantity ?? "").StartsWith("Transient", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A quantity at each node, in <see cref="UnitOf"/>; empty for one the analysis did not give, as
        /// the transient of a stair, or a model saved before it was kept.
        /// </summary>
        public double[] Of(string quantity)
        {
            switch (Quantity(quantity))
            {
                case "Response factor R": return Factors;
                case "Steady-state R": return SteadyStateFactors;
                case "Transient R": return TransientChecked ? TransientFactors : new double[0];
                case "Steady-state acceleration": return SteadyStateAccelerations.Select(a => a * 1000.0).ToArray();
                case "Transient acceleration": return TransientChecked ? TransientAccelerations.Select(a => a * 1000.0).ToArray() : new double[0];
                default: return new double[0];
            }
        }

        /// <summary>The largest response factor and its node; (0, -1) for none.</summary>
        public (double Factor, int Node) Largest() => Largest(Quantities[0]);

        /// <summary>The largest of a quantity and its node; (0, -1) for none.</summary>
        public (double Factor, int Node) Largest(string quantity)
        {
            var values = Of(quantity);
            if (values.Length == 0) return (0.0, -1);
            int at = Enumerable.Range(0, values.Length).OrderByDescending(i => values[i]).First();
            return (values[at], at < Nodes.Length ? Nodes[at] : -1);
        }
    }
}
