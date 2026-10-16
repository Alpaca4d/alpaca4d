using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Explorer
{
    /// <summary>
    /// Every combination of a set of inputs, or the part of it a study is asked to run.
    ///
    /// Each input has a number of steps: the ticks of a slider, the items of a value list, the
    /// lines of a panel. The design space is every combination of one step per input, numbered
    /// with the first input changing fastest, as Colibri numbered it, so that a range of designs
    /// means the same designs in either.
    ///
    /// Two ways to run less than all of it, both from the Subset component:
    ///   Divisions  per input, how many of its steps to take. 0 takes them all, 1 only the one it
    ///              is on now, more takes that many spread evenly from the first to the last.
    ///   Ranges     which combinations to run, by number, counted after the divisions. This is how a
    ///              study is split between machines: 0 to 499 on one, 500 to 999 on the other.
    ///
    /// Nothing is listed up front. A slider with two decimals can have a thousand steps, and four
    /// of them make a design space no list could hold; a combination is worked out from its number
    /// when it is reached.
    /// </summary>
    public sealed class DesignSpace
    {
        /// <summary>For each input, the steps the study takes of it.</summary>
        public IReadOnlyList<IReadOnlyList<int>> Steps { get; }

        /// <summary>How many combinations there are of every step of every input.</summary>
        public long Total { get; }

        /// <summary>How many the divisions leave.</summary>
        public long Selected { get; }

        /// <summary>How many the study runs: what the divisions leave, inside the ranges.</summary>
        public long Count { get; }

        /// <summary>True when there were divisions, but not one per input, so they were not used.</summary>
        public bool DivisionsIgnored { get; }

        /// <summary>True when the space has more designs than can be counted, and only a range of it can be run.</summary>
        public bool Overflows { get; }

        /// <summary>The ranges run, inclusive, sorted and not overlapping.</summary>
        public IReadOnlyList<KeyValuePair<long, long>> Ranges => _ranges;

        private readonly List<KeyValuePair<long, long>> _ranges;

        /// <param name="stepCounts">How many steps each input has.</param>
        /// <param name="current">The step each input is on now; used by a division of 1.</param>
        /// <param name="divisions">How many steps to take of each input; null or empty for all of them.</param>
        /// <param name="ranges">Which combinations to run, by number; null or empty for all of them.</param>
        public DesignSpace(IList<int> stepCounts, IList<int> current = null, IList<int> divisions = null, IList<KeyValuePair<double, double>> ranges = null)
        {
            int n = stepCounts.Count;
            bool useDivisions = divisions != null && divisions.Count == n;
            DivisionsIgnored = divisions != null && divisions.Count > 0 && !useDivisions;

            var steps = new List<IReadOnlyList<int>>();
            for (int i = 0; i < n; i++)
            {
                int now = current != null && i < current.Count ? current[i] : 0;
                steps.Add(Spread(stepCounts[i], useDivisions ? divisions[i] : 0, now));
            }
            Steps = steps;

            bool overflows = false;
            Total = n == 0 ? 0 : Product(stepCounts.Select(c => (long)Math.Max(c, 0)), ref overflows);
            Selected = n == 0 ? 0 : Product(steps.Select(s => (long)s.Count), ref overflows);
            Overflows = overflows;

            _ranges = Merge(ranges, Selected);
            Count = _ranges.Sum(r => r.Value - r.Key + 1);
        }

        /// <summary>
        /// The steps taken of an input with <paramref name="count"/> steps: all of them, the current
        /// one, or <paramref name="take"/> spread evenly so that the first and the last are always in.
        /// All of them is a range rather than a list: a slider with five decimals has a hundred
        /// million steps, and this is worked out on every solution.
        /// </summary>
        public static IReadOnlyList<int> Spread(int count, int take, int current)
        {
            if (count <= 0) return new int[0];
            if (take <= 0 || take >= count) return new Every(count);
            if (take == 1) return new[] { Math.Min(Math.Max(current, 0), count - 1) };

            return Enumerable.Range(0, take)
                .Select(j => (int)Math.Round(j * (count - 1) / (double)(take - 1), MidpointRounding.AwayFromZero))
                .Distinct()
                .ToArray();
        }

        /// <summary>The steps of combination number <paramref name="index"/>, counted after the divisions.</summary>
        public int[] At(long index)
        {
            var result = new int[Steps.Count];
            for (int i = 0; i < Steps.Count; i++)
            {
                int length = Steps[i].Count;
                result[i] = Steps[i][(int)(index % length)];
                index /= length;
            }
            return result;
        }

        /// <summary>The <paramref name="ordinal"/>-th combination the study runs, from 0 to Count - 1.</summary>
        public int[] Nth(long ordinal)
        {
            foreach (var range in _ranges)
            {
                long length = range.Value - range.Key + 1;
                if (ordinal < length) return At(range.Key + ordinal);
                ordinal -= length;
            }
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        /// <summary>Every combination the study runs, in order.</summary>
        public IEnumerable<int[]> Enumerate()
        {
            foreach (var range in _ranges)
                for (long index = range.Key; index <= range.Value; index++)
                    yield return At(index);
        }

        /// <summary>The steps 0 to count - 1, without a list of them.</summary>
        private sealed class Every : IReadOnlyList<int>
        {
            public Every(int count) { Count = count; }
            public int Count { get; }
            public int this[int index] => index;
            public IEnumerator<int> GetEnumerator() => Enumerable.Range(0, Count).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static long Product(IEnumerable<long> factors, ref bool overflows)
        {
            long product = 1;
            foreach (long factor in factors)
            {
                if (factor == 0) return 0;
                if (product > long.MaxValue / factor)
                {
                    overflows = true;
                    product = long.MaxValue;
                }
                else product *= factor;
            }
            return product;
        }

        /// <summary>The ranges as whole numbers inside 0 to count - 1, sorted, overlaps joined.</summary>
        private static List<KeyValuePair<long, long>> Merge(IList<KeyValuePair<double, double>> ranges, long count)
        {
            var result = new List<KeyValuePair<long, long>>();
            if (count <= 0) return result;
            if (ranges == null || ranges.Count == 0)
            {
                result.Add(new KeyValuePair<long, long>(0, count - 1));
                return result;
            }

            var clipped = new List<KeyValuePair<long, long>>();
            foreach (var range in ranges)
            {
                double low = Math.Min(range.Key, range.Value);
                double high = Math.Max(range.Key, range.Value);
                long from = (long)Math.Max(0, Math.Ceiling(low - 1e-9));
                long to = (long)Math.Min(count - 1, Math.Floor(Math.Min(high + 1e-9, long.MaxValue / 2.0)));
                if (from <= to) clipped.Add(new KeyValuePair<long, long>(from, to));
            }

            foreach (var range in clipped.OrderBy(r => r.Key))
            {
                if (result.Count > 0 && range.Key <= result[result.Count - 1].Value + 1)
                {
                    var last = result[result.Count - 1];
                    result[result.Count - 1] = new KeyValuePair<long, long>(last.Key, Math.Max(last.Value, range.Value));
                }
                else result.Add(range);
            }
            return result;
        }
    }
}
