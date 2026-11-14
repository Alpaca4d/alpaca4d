using System;
using GH_IO.Serialization;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// How Non-Linear Optimisation runs: the algorithm, the direction, and when to stop. Saved
    /// with the document, so a definition opens ready to run again as it last ran.
    /// </summary>
    internal sealed class OptimisationSettings
    {
        public OptimisationAlgorithm Algorithm = OptimisationAlgorithm.DirectL;
        public bool Maximise;

        /// <summary>The most evaluations, each a solution of the definition. 0 for no limit.</summary>
        public int MaxEvaluations = 1000;

        /// <summary>The most seconds to run. 0 for no limit.</summary>
        public double MaxTime;

        /// <summary>Stop when a step moves the variables by less than this fraction of them.</summary>
        public double VariableTolerance = 1e-4;

        /// <summary>Stop when a step changes the objective by less than this fraction of it.</summary>
        public double ObjectiveTolerance = 1e-4;

        /// <summary>Stop when a step changes the objective by less than this. 0 for off.</summary>
        public double ObjectiveAbsoluteTolerance;

        /// <summary>Stop as soon as the objective is as good as <see cref="StopValue"/>.</summary>
        public bool UseStopValue;
        public double StopValue;

        /// <summary>The seed of the random numbers CRS2-LM and ISRES draw: the same seed, the same run.</summary>
        public uint Seed = 1;

        public OptimisationSettings Copy() => (OptimisationSettings)MemberwiseClone();

        /// <summary>The optimiser these settings describe, for <paramref name="dimension"/> variables.</summary>
        public Optimiser CreateOptimiser(int dimension)
        {
            return new Optimiser(Algorithm, dimension)
            {
                Maximise = Maximise,
                MaxEvaluations = Math.Max(0, MaxEvaluations),
                MaxTime = Math.Max(0, MaxTime),
                RelativeVariableTolerance = Math.Max(0, VariableTolerance),
                RelativeFunctionTolerance = Math.Max(0, ObjectiveTolerance),
                AbsoluteFunctionTolerance = Math.Max(0, ObjectiveAbsoluteTolerance),
                StopValue = UseStopValue ? StopValue : (double?)null,
                Seed = Seed,
            };
        }

        public void Write(GH_IWriter writer)
        {
            // The algorithm by name, so that the order of the enum is free to change.
            writer.SetString("algorithm", Algorithm.ToString());
            writer.SetBoolean("maximise", Maximise);
            writer.SetInt32("max_evaluations", MaxEvaluations);
            writer.SetDouble("max_time", MaxTime);
            writer.SetDouble("variable_tolerance", VariableTolerance);
            writer.SetDouble("objective_tolerance", ObjectiveTolerance);
            writer.SetDouble("objective_absolute_tolerance", ObjectiveAbsoluteTolerance);
            writer.SetBoolean("use_stop_value", UseStopValue);
            writer.SetDouble("stop_value", StopValue);
            writer.SetInt32("seed", unchecked((int)Seed));
        }

        public void Read(GH_IReader reader)
        {
            if (reader.ItemExists("algorithm") &&
                Enum.TryParse(reader.GetString("algorithm"), out OptimisationAlgorithm algorithm))
                Algorithm = algorithm;
            if (reader.ItemExists("maximise")) Maximise = reader.GetBoolean("maximise");
            if (reader.ItemExists("max_evaluations")) MaxEvaluations = reader.GetInt32("max_evaluations");
            if (reader.ItemExists("max_time")) MaxTime = reader.GetDouble("max_time");
            if (reader.ItemExists("variable_tolerance")) VariableTolerance = reader.GetDouble("variable_tolerance");
            if (reader.ItemExists("objective_tolerance")) ObjectiveTolerance = reader.GetDouble("objective_tolerance");
            if (reader.ItemExists("objective_absolute_tolerance")) ObjectiveAbsoluteTolerance = reader.GetDouble("objective_absolute_tolerance");
            if (reader.ItemExists("use_stop_value")) UseStopValue = reader.GetBoolean("use_stop_value");
            if (reader.ItemExists("stop_value")) StopValue = reader.GetDouble("stop_value");
            if (reader.ItemExists("seed")) Seed = unchecked((uint)reader.GetInt32("seed"));
        }
    }
}
