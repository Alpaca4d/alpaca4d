using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Element
{
    /// <summary>
    /// One of the seven predefined nodal supports, stated as which of the six degrees of
    /// freedom it holds.
    ///
    /// Seven presets over six degrees of freedom: a preset is a *combination* of the six,
    /// never one of them. Two presets can differ only in which translation they let go,
    /// which is why they are written out one by one below rather than derived from
    /// anything - there is no rule to derive them from, only a table.
    ///
    /// These booleans are the engineering meaning and the only thing the solver ever sees:
    /// <see cref="Support.WriteTcl"/> reads <see cref="Support.Tx"/> and its five
    /// neighbours, not this class. What a preset adds is a name, a stable id to serialise,
    /// and a symbol to draw. The symbols live in <see cref="SupportSymbol"/> and can be
    /// redrawn from scratch without a line of this file changing.
    /// </summary>
    public sealed class SupportPreset
    {
        public const string Fixed = "fixed";
        public const string Hinged = "hinged";
        public const string SlidingXY = "sliding-xy";
        public const string SlidingX = "sliding-x";
        public const string SlidingY = "sliding-y";
        public const string SlidingZY = "sliding-zy";
        public const string Free = "free";

        private SupportPreset(string id, string label, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            this.Id = id;
            this.Label = label;
            this.Tx = tx;
            this.Ty = ty;
            this.Tz = tz;
            this.Rx = rx;
            this.Ry = ry;
            this.Rz = rz;
        }

        /// <summary>Stable key. This, and only this, is what needs to survive a round trip.</summary>
        public string Id { get; private set; }

        public string Label { get; private set; }

        /// <summary>true is restrained, false is free.</summary>
        public bool Tx { get; private set; }
        public bool Ty { get; private set; }
        public bool Tz { get; private set; }
        public bool Rx { get; private set; }
        public bool Ry { get; private set; }
        public bool Rz { get; private set; }

        /// <summary>
        /// The seven presets in display order, left to right. The order is part of the
        /// contract - callers index this to lay the symbols out - so append, never insert.
        /// </summary>
        public static readonly IList<SupportPreset> All = new List<SupportPreset>
        {
            //                id          label                                  Tx     Ty     Tz     Rx     Ry     Rz
            new SupportPreset(Fixed,     "Rigid / Fixed",                       true,  true,  true,  true,  true,  true),
            // A ball joint: the symbol carries a sphere, and a sphere means every rotation
            // is let go. The other five partial supports all keep Rz; this one does not,
            // which is what makes it the plain pin rather than a hinge about Z.
            new SupportPreset(Hinged,    "Hinged / Pinned",                     true,  true,  true,  false, false, false),
            new SupportPreset(SlidingXY, "Sliding in X and Y + Rz restraint",   false, false, true,  false, false, true),
            new SupportPreset(SlidingX,  "Sliding in X + Rz restraint",         false, true,  true,  false, false, true),
            new SupportPreset(SlidingY,  "Sliding in Y + Rz restraint",         true,  false, true,  false, false, true),
            new SupportPreset(SlidingZY, "Sliding in Z and Y + Rz restraint",   true,  false, false, false, false, true),
            new SupportPreset(Free,      "Free",                                false, false, false, false, false, false),
        }.AsReadOnly();

        /// <summary>Tx, Ty, Tz, Rx, Ry, Rz, in that order. The order labels are read in.</summary>
        public IList<bool> Dof
        {
            get { return new List<bool> { this.Tx, this.Ty, this.Tz, this.Rx, this.Ry, this.Rz }.AsReadOnly(); }
        }

        /// <summary>The preset with this id, or null if the id is not one of the seven.</summary>
        public static SupportPreset FromId(string id)
        {
            return All.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The preset holding exactly these six degrees of freedom, or null. Null is an
        /// ordinary answer, not a failure: the six booleans describe 64 supports and only
        /// seven of them have a name here.
        /// </summary>
        public static SupportPreset Match(bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            return All.FirstOrDefault(preset =>
                preset.Tx == tx && preset.Ty == ty && preset.Tz == tz &&
                preset.Rx == rx && preset.Ry == ry && preset.Rz == rz);
        }

        public static SupportPreset Match(Support support)
        {
            if (support == null)
                return null;

            return Match(support.Tx, support.Ty, support.Tz, support.Rx, support.Ry, support.Rz);
        }

        /// <summary>A support at <paramref name="position"/> restrained the way this preset says.</summary>
        public Support At(Rhino.Geometry.Point3d position)
        {
            return new Support(position, this.Tx, this.Ty, this.Tz, this.Rx, this.Ry, this.Rz);
        }

        /// <summary>A support on <paramref name="frame"/>, restrained about that frame's axes.</summary>
        public Support On(Rhino.Geometry.Plane frame)
        {
            return new Support(frame, this.Tx, this.Ty, this.Tz, this.Rx, this.Ry, this.Rz);
        }

        /// <summary>The six states, in the order they are always read: Tx, Ty, Tz, Rx, Ry, Rz.</summary>
        public static string DofSummary(bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            return $"Tx {State(tx)}  Ty {State(ty)}  Tz {State(tz)}  Rx {State(rx)}  Ry {State(ry)}  Rz {State(rz)}";
        }

        private static string State(bool locked)
        {
            return locked ? "fixed" : "free";
        }

        /// <summary>Name on one line, the six states on the next - what a tooltip shows.</summary>
        public string Describe()
        {
            return this.Label + Environment.NewLine +
                   DofSummary(this.Tx, this.Ty, this.Tz, this.Rx, this.Ry, this.Rz);
        }

        public override string ToString()
        {
            return this.Describe();
        }
    }
}
