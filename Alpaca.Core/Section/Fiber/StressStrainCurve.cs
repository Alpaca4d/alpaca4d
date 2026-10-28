using System;

namespace Alpaca4d.Section
{
    /// <summary>
    /// A material's stress under a strain taken monotonically from zero, as OpenSees reports it at
    /// evenly spaced strains between -<see cref="MaxStrain"/> and +<see cref="MaxStrain"/>.
    ///
    /// Monotonic from zero is all an interaction domain asks of a material. Every ultimate state is
    /// reached along a straight path from the unloaded section, so each fibre's strain grows in
    /// proportion to the end, and a rate-independent material's stress at that end depends on the
    /// strain alone. Sampled once, the material can be read at any strain without OpenSees.
    /// </summary>
    public sealed class StressStrainCurve
    {
        /// <summary>Stresses from -MaxStrain to +MaxStrain, one step apart.</summary>
        private readonly double[] _stress;
        private readonly double _step;

        /// <summary>The largest strain sampled, either way. Past it a curve holds its last stress.</summary>
        public double MaxStrain { get; }

        /// <summary>The steps from zero to <see cref="MaxStrain"/>, either way.</summary>
        public int Steps { get; }

        /// <param name="maxStrain">The strain at the end of each branch, positive.</param>
        /// <param name="compression">Stress at -k MaxStrain/Steps, k from 0.</param>
        /// <param name="tension">Stress at +k MaxStrain/Steps, k from 0.</param>
        public StressStrainCurve(double maxStrain, double[] compression, double[] tension)
        {
            if (!(maxStrain > 0.0))
                throw new ArgumentOutOfRangeException(nameof(maxStrain), "A stress-strain curve needs a positive strain range.");
            if (compression == null || tension == null || compression.Length < 2 || compression.Length != tension.Length)
                throw new ArgumentException("A stress-strain curve needs two branches of the same length, each with at least two points.");

            Steps = tension.Length - 1;
            MaxStrain = maxStrain;
            _step = maxStrain / Steps;
            _stress = new double[2 * Steps + 1];

            for (int k = 0; k <= Steps; k++)
            {
                _stress[Steps - k] = compression[k];
                _stress[Steps + k] = tension[k];
            }

            // Both branches start from the same unloaded material; tension's is the one kept.
            _stress[Steps] = tension[0];
        }

        /// <summary>The stress at <paramref name="strain"/>, linear between samples.</summary>
        public double Stress(double strain)
        {
            double x = (strain + MaxStrain) / _step;
            if (!(x > 0.0))
                return _stress[0];

            int last = _stress.Length - 1;
            if (x >= last)
                return _stress[last];

            int i = (int)x;
            double t = x - i;
            return _stress[i] + t * (_stress[i + 1] - _stress[i]);
        }
    }
}
