using System;
using System.Reflection;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Something Non-Linear Optimisation moves: a number slider, which is one variable, or a
    /// Galapagos gene pool, which is one variable per gene.
    ///
    /// Each variable runs between its object's bounds, and takes only the values its object can
    /// show: a slider rounds to its decimals, a gene pool to its own. The optimiser asks for a
    /// value, and what the object took is what the definition sees and what is recorded.
    /// Moving one never starts a solution: every variable is moved first, then the definition is
    /// solved once, so a design is never solved half-set.
    /// </summary>
    internal abstract class OptimisationVariables
    {
        protected OptimisationVariables(IGH_DocumentObject obj)
        {
            Object = obj;
        }

        public IGH_DocumentObject Object { get; }

        /// <summary>What it is, for messages: "slider", "gene pool".</summary>
        public abstract string Kind { get; }

        /// <summary>How many variables it holds.</summary>
        public abstract int Count { get; }

        public abstract double Lower(int i);
        public abstract double Upper(int i);

        /// <summary>The value variable i is at now.</summary>
        public abstract double Value(int i);

        /// <summary>Moves variable i as near to <paramref name="value"/> as it can go, without solving, and returns where it went.</summary>
        public abstract double Set(int i, double value);

        /// <summary>The smallest step variable i can take.</summary>
        public abstract double Step(int i);

        /// <summary>Expires the object once its variables are set, so the next solution reads them.</summary>
        public abstract void Expire();

        /// <summary>The name of variable i, for the history.</summary>
        public abstract string Name(int i);

        /// <summary>What can be a variable: a number slider, or a gene pool this version of Galapagos lets us read.</summary>
        public static bool CanBe(IGH_DocumentObject obj) => obj is GH_NumberSlider || GenePoolVariables.Is(obj);

        /// <summary>The variables of <paramref name="obj"/>, or null when it cannot be one.</summary>
        public static OptimisationVariables From(IGH_DocumentObject obj)
        {
            if (obj is GH_NumberSlider slider) return new SliderVariable(slider);
            if (GenePoolVariables.Is(obj)) return GenePoolVariables.TryCreate(obj);
            return null;
        }

        protected static decimal ToDecimal(double value)
        {
            if (double.IsNaN(value)) return 0m;
            if (value >= (double)decimal.MaxValue) return decimal.MaxValue;
            if (value <= (double)decimal.MinValue) return decimal.MinValue;
            return (decimal)value;
        }
    }

    /// <summary>
    /// A number slider. Its value, not its expression: a slider with an expression gives the
    /// definition the expression of its value, and its value is what lies between its bounds.
    /// </summary>
    internal sealed class SliderVariable : OptimisationVariables
    {
        private readonly GH_NumberSlider _slider;

        public SliderVariable(GH_NumberSlider slider) : base(slider)
        {
            _slider = slider;
        }

        public override string Kind => "slider";
        public override int Count => 1;
        public override double Lower(int i) => (double)_slider.Slider.Minimum;
        public override double Upper(int i) => (double)_slider.Slider.Maximum;
        public override double Value(int i) => (double)_slider.Slider.Value;

        public override double Set(int i, double value)
        {
            // Clamps, rounds to the slider's accuracy, and expires it without solving.
            _slider.SetSliderValue(ToDecimal(value));
            return (double)_slider.Slider.Value;
        }

        public override double Step(int i)
        {
            switch (_slider.Slider.Type)
            {
                case Grasshopper.GUI.Base.GH_SliderAccuracy.Float:
                    return Math.Pow(10, -_slider.Slider.DecimalPlaces);
                case Grasshopper.GUI.Base.GH_SliderAccuracy.Even:
                case Grasshopper.GUI.Base.GH_SliderAccuracy.Odd:
                    return 2;
                default:
                    return 1;
            }
        }

        public override void Expire() { }

        public override string Name(int i) => _slider.ImpliedNickName;
    }

    /// <summary>
    /// A Galapagos gene pool, read and moved through reflection: Galapagos ships with Rhino on
    /// Windows and on Mac, but referencing it would tie Alpaca4d's build to a Rhino installation.
    /// The members used - Minimum, Maximum, Count, Decimals and the Value indexer - are those
    /// Galapagos's own solver uses; when they are not there, a gene pool is not offered as a
    /// variable.
    /// </summary>
    internal sealed class GenePoolVariables : OptimisationVariables
    {
        private static readonly Guid GenePoolId = new Guid("{21553C44-EA62-475e-A8BB-62B2A3EE5CA5}");

        private readonly PropertyInfo _value;
        private readonly decimal _min, _max;
        private readonly int _count, _decimals;

        private GenePoolVariables(IGH_DocumentObject pool, PropertyInfo value, decimal min, decimal max, int count, int decimals) : base(pool)
        {
            _value = value;
            _min = min;
            _max = max;
            _count = count;
            _decimals = decimals;
        }

        public static bool Is(IGH_DocumentObject obj) => obj != null && obj.ComponentGuid == GenePoolId;

        public static GenePoolVariables TryCreate(IGH_DocumentObject pool)
        {
            try
            {
                var type = pool.GetType();
                var value = type.GetProperty("Value", typeof(decimal), new[] { typeof(int) });
                var min = (decimal)type.GetProperty("Minimum").GetValue(pool);
                var max = (decimal)type.GetProperty("Maximum").GetValue(pool);
                var count = (int)type.GetProperty("Count").GetValue(pool);
                var decimals = (int)type.GetProperty("Decimals").GetValue(pool);
                if (value == null || !value.CanWrite) return null;
                return new GenePoolVariables(pool, value, min, max, count, decimals);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override string Kind => "gene pool";
        public override int Count => _count;
        public override double Lower(int i) => (double)_min;
        public override double Upper(int i) => (double)_max;
        public override double Value(int i) => (double)(decimal)_value.GetValue(Object, new object[] { i });

        public override double Set(int i, double value)
        {
            decimal v = Math.Round(ToDecimal(value), _decimals);
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            _value.SetValue(Object, v, new object[] { i });
            return Value(i);
        }

        public override double Step(int i) => Math.Pow(10, -_decimals);

        public override void Expire()
        {
            if (Object is IGH_ActiveObject active) active.ExpireSolution(false);
        }

        public override string Name(int i) => $"{Object.NickName}[{i}]";
    }
}
