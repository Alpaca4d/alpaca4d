namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// How a topology optimisation runs. The defaults are those of top3d (Liu and Tovar 2014), which
    /// are those of the 88 line code (Andreassen et al. 2011) in three dimensions.
    /// </summary>
    public sealed class TopologySettings
    {
        /// <summary>The share of the design elements' volume the result keeps, 0 to 1.</summary>
        public double VolumeFraction { get; set; } = 0.3;

        /// <summary>
        /// The SIMP penalty p: an element of density x has x^p of its stiffness, so that a half-dense
        /// element costs half its volume for an eighth of its stiffness, and the optimum is pushed
        /// to solid or void.
        /// </summary>
        public double Penalty { get; set; } = 3.0;

        /// <summary>
        /// The radius of the density filter, in model units. Zero or less takes 1.5 times the typical
        /// element size, top3d's rmin of 1.5 elements.
        /// </summary>
        public double FilterRadius { get; set; }

        /// <summary>How many iterations at most, counted from the first: those of a run gone on from count too.</summary>
        public int MaxIterations { get; set; } = 100;

        /// <summary>The run stops when no density moved by more than this in an iteration.</summary>
        public double Tolerance { get; set; } = 0.01;

        /// <summary>
        /// What is left of the stiffness of a void element, as a fraction of the solid's: top3d's Emin
        /// over E0. Small enough to be no structure, large enough to keep every node held, so a void
        /// region never has to be cut out of the model.
        /// </summary>
        public double MinStiffness { get; set; } = 1e-9;

        /// <summary>The most a density can change in one iteration.</summary>
        public double Move { get; set; } = 0.2;
    }
}
