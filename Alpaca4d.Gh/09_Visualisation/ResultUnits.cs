using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What kind of quantity a result is, which decides the units it can be shown in. A beam force
    /// is a force or a moment depending on which one, so this goes with the component as well as
    /// the family - see <see cref="ResultField.QuantityOf"/>.
    ///
    /// View Results keeps one Unit dropdown per entry and saves each by its position, so an entry
    /// is only ever added at the end.
    /// </summary>
    internal enum Quantity
    {
        Length,
        Force,
        Moment,
        Stress,
        ForcePerLength,
        MomentPerLength
    }

    /// <summary>One unit a result can be shown in.</summary>
    internal class ResultUnit
    {
        /// <summary>What it is written as after a number, in Info.</summary>
        public readonly string Symbol;

        /// <summary>What the dropdown offers, which can say what else the unit is called.</summary>
        public readonly string Caption;

        /// <summary>How big it is in newtons and metres.</summary>
        public readonly double InSI;

        public ResultUnit(string symbol, double inSI, string caption = null)
        {
            Symbol = symbol;
            InSI = inSI;
            Caption = caption ?? symbol;
        }
    }

    /// <summary>
    /// The units View Results can show a result in, and the factor that takes a result there from
    /// the unit it comes out of the solver in.
    ///
    /// The solver works in kN and m, so a displacement comes out in metres and a shell stress in
    /// kN/m², and the numbers a component hands on cannot be rescaled downstream without the reader
    /// knowing what they were in. Shown in the unit picked, Info, Values, the values written on the
    /// model and the Range read in it all agree, and nobody has to remember that 0.0123 means 12 mm.
    /// </summary>
    internal static class ResultUnits
    {
        // Each list is saved as a position in it, so a unit is only ever added at the end.
        private static readonly ResultUnit[] Length =
        {
            new ResultUnit("mm", 1e-3),
            new ResultUnit("cm", 1e-2),
            new ResultUnit("m", 1.0)
        };

        private static readonly ResultUnit[] Force =
        {
            new ResultUnit("N", 1.0),
            new ResultUnit("kN", 1e3),
            new ResultUnit("MN", 1e6)
        };

        private static readonly ResultUnit[] Moment =
        {
            new ResultUnit("N·mm", 1e-3),
            new ResultUnit("N·m", 1.0),
            new ResultUnit("kN·cm", 10.0),
            new ResultUnit("kN·m", 1e3),
            new ResultUnit("MN·m", 1e6)
        };

        private static readonly ResultUnit[] Stress =
        {
            new ResultUnit("kPa", 1e3, "kPa = kN/m²"),
            new ResultUnit("MPa", 1e6, "MPa = N/mm²"),
            new ResultUnit("kN/cm²", 1e7)
        };

        private static readonly ResultUnit[] ForcePerLength =
        {
            new ResultUnit("N/m", 1.0),
            new ResultUnit("kN/m", 1e3, "kN/m = N/mm"),
            new ResultUnit("MN/m", 1e6)
        };

        private static readonly ResultUnit[] MomentPerLength =
        {
            new ResultUnit("N·m/m", 1.0),
            new ResultUnit("kN·m/m", 1e3),
            new ResultUnit("MN·m/m", 1e6)
        };

        /// <summary>The units a quantity can be shown in, in the order its dropdown offers them.</summary>
        public static ResultUnit[] Of(Quantity quantity)
        {
            switch (quantity)
            {
                case Quantity.Length: return Length;
                case Quantity.Force: return Force;
                case Quantity.Moment: return Moment;
                case Quantity.Stress: return Stress;
                case Quantity.ForcePerLength: return ForcePerLength;
                default: return MomentPerLength;
            }
        }

        /// <summary>The unit at a dropdown position, or the default one when there is nothing there.</summary>
        public static ResultUnit At(Quantity quantity, int index)
        {
            var units = Of(quantity);
            return index >= 0 && index < units.Length ? units[index] : units[Default(quantity)];
        }

        /// <summary>Where a unit sits in its quantity's list, by its symbol; -1 when it is not there.</summary>
        public static int IndexOf(Quantity quantity, string symbol)
        {
            return Array.FindIndex(Of(quantity), x => x.Symbol == symbol);
        }

        /// <summary>
        /// The unit a newly placed View Results starts in: the one an engineer reads the quantity
        /// in, rather than the one the solver happens to work in - a deflection in mm and a stress
        /// in MPa.
        /// </summary>
        public static int Default(Quantity quantity)
        {
            switch (quantity)
            {
                case Quantity.Length: return IndexOf(quantity, "mm");
                case Quantity.Force: return IndexOf(quantity, "kN");
                case Quantity.Moment: return IndexOf(quantity, "kN·m");
                case Quantity.Stress: return IndexOf(quantity, "MPa");
                case Quantity.ForcePerLength: return IndexOf(quantity, "kN/m");
                default: return IndexOf(quantity, "kN·m/m");
            }
        }

        /// <summary>
        /// Where the unit the solver works in sits in the list - the unit every result was shown in
        /// before there was a choice, bar the beam stresses. The default one when it is not offered.
        /// </summary>
        public static int ModelUnit(Quantity quantity)
        {
            var units = Of(quantity);
            double model = ModelUnitInSI(quantity);

            int found = Array.FindIndex(units, x => Math.Abs(x.InSI / model - 1.0) < 1e-9);
            return found >= 0 ? found : Default(quantity);
        }

        /// <summary>What a result read off the solver is multiplied by to be in the unit at <paramref name="index"/>.</summary>
        public static double Factor(Quantity quantity, int index)
        {
            return ModelUnitInSI(quantity) / At(quantity, index).InSI;
        }

        /// <summary>How big the solver's own unit of the quantity is, in newtons and metres.</summary>
        private static double ModelUnitInSI(Quantity quantity)
        {
            double force = Units.Force == ForceUnit.kN ? 1e3 : 1.0;
            double length = ModelLength.MillimetresPerUnit / 1000.0;

            switch (quantity)
            {
                case Quantity.Length: return length;
                case Quantity.Force: return force;
                case Quantity.Moment: return force * length;
                case Quantity.Stress: return force / (length * length);
                case Quantity.ForcePerLength: return force / length;
                default: return force;  // a moment per length, force × length / length
            }
        }
    }
}
