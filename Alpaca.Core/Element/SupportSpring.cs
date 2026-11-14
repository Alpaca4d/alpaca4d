using System;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Element
{
    /// <summary>Which way a spring of a <see cref="Support"/> pushes back.</summary>
    public enum SpringBehaviour
    {
        /// <summary>Both ways, an ordinary spring.</summary>
        Both,

        /// <summary>
        /// Only while the node pushes into the support: against the axis of the support's plane. Free
        /// when the node moves the other way, and free until a gap closes when there is one. A plane
        /// turned over makes it tension only.
        /// </summary>
        CompressionOnly,
    }

    /// <summary>
    /// One spring of a spring support: a direction of the support's plane, 1-3 along its axes and 4-6
    /// about them, and how stiff it is in that direction.
    /// </summary>
    public class SupportSpring
    {
        /// <summary>The OpenSees yield force of a gap spring that never yields [kN].</summary>
        private const double NeverYields = 1.0e30;

        public int Direction { get; }

        /// <summary>[kN/m] for a translation, [kNm/rad] for a rotation; null for rigid, which Assemble makes a penalty stiffness.</summary>
        public double? Stiffness { get; }

        public SpringBehaviour Behaviour { get; }

        /// <summary>How far the node moves before the spring takes hold [m]. Zero or more.</summary>
        public double Gap { get; }

        public SupportSpring(int direction, double? stiffness, SpringBehaviour behaviour = SpringBehaviour.Both, double gap = 0.0)
        {
            if (direction < 1 || direction > 6)
                throw new ArgumentException($"A support spring acts in direction 1 to 6, not {direction}.");
            if (stiffness.HasValue && !(stiffness.Value > 0.0))
                throw new ArgumentException($"A spring needs a stiffness greater than zero, not {stiffness.Value}. Leave a direction out to free it.");
            if (!(gap >= 0.0))
                throw new ArgumentException($"A gap is a distance, zero or more, not {gap}.");
            if (gap > 0.0 && behaviour != SpringBehaviour.CompressionOnly)
                throw new ArgumentException("Only a compression-only spring has a gap.");

            this.Direction = direction;
            this.Stiffness = stiffness;
            this.Behaviour = behaviour;
            this.Gap = gap;
        }

        /// <summary>True when the spring gives a different stiffness depending on where the node is: it needs a non-linear analysis.</summary>
        public bool IsOneWay => this.Behaviour != SpringBehaviour.Both;

        /// <summary>
        /// The OpenSees material for this spring, <paramref name="rigid"/> standing in for a stiffness
        /// left out. The spring runs from a fixed node to the support's node, so its deformation is
        /// the node's movement along the direction: negative is compression.
        /// </summary>
        public IMaterial Material(double rigid)
        {
            double k = this.Stiffness ?? rigid;

            if (this.Behaviour == SpringBehaviour.Both)
                return new Alpaca4d.Material.UniaxialMaterialElastic("Alpaca4d support spring", k, k, 0.0, 0.0, 0.0);

            // ENT is stiff at zero deformation; an Elastic with no tension stiffness would not be.
            if (this.Gap == 0.0)
                return new Alpaca4d.Material.UniaxialMaterialENT(k);

            return new Alpaca4d.Material.UniaxialMaterialElasticPPGap(k, -NeverYields, -this.Gap);
        }

        /// <summary>"Kz 1000", "Kz rigid, compression only, gap 0.5" - what a panel shows.</summary>
        public string Describe()
        {
            var names = new[] { "x", "y", "z", "rx", "ry", "rz" };
            var text = $"K{names[this.Direction - 1]} {(this.Stiffness.HasValue ? TclNumber.Write(this.Stiffness.Value) : "rigid")}";
            if (this.Behaviour == SpringBehaviour.CompressionOnly)
                text += ", compression only";
            if (this.Gap > 0.0)
                text += $", gap {TclNumber.Write(this.Gap)}";
            return text;
        }
    }
}
