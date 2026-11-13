using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Section;

namespace Alpaca4d.Result
{
    /// <summary>How far a station of a beam has gone.</summary>
    public enum HingeState
    {
        /// <summary>No fibre past its yield strain.</summary>
        Elastic = 0,
        /// <summary>A fibre of a material that yields - a bar, a steel plate - past its yield strain: a plastic hinge.</summary>
        Yielded = 1,
        /// <summary>A fibre at the strain its material fails at: concrete crushed, a bar at its ultimate strain.</summary>
        Ultimate = 2,
    }

    /// <summary>One station of a beam on a fibre section - an integration point - at one step.</summary>
    public sealed class HingeStation
    {
        /// <summary>The beam's place in Model.Beams.</summary>
        public int Beam { get; internal set; }

        /// <summary>The station, from the I end.</summary>
        public int Station { get; internal set; }

        /// <summary>Where it is along the beam: 0 at the I end, 1 at the J end.</summary>
        public double Along { get; internal set; }

        /// <summary>Where it is on the beam as it was built.</summary>
        public Point3d Position { get; internal set; }

        /// <summary>The largest strain of a fibre that yields, over its yield strain: past 1, the section has yielded.</summary>
        public double Yield { get; internal set; }

        /// <summary>The largest strain of any fibre over the strain it fails at: past 1, it has.</summary>
        public double Ultimate { get; internal set; }

        /// <summary>What has gone furthest, in words: "B450C in tension", "C25/30 crushed".</summary>
        public string Governs { get; internal set; }

        /// <summary>Where it is at this step alone: past its yield strain now, or back under it.</summary>
        public HingeState State => Ultimate >= 1.0 ? HingeState.Ultimate : Yield >= 1.0 ? HingeState.Yielded : HingeState.Elastic;

        /// <summary>
        /// The largest <see cref="Yield"/> at this step or any before it: the ductility the station has reached,
        /// its most strained fibre's strain over its yield strain. Past 1 it has yielded, and stays yielded
        /// whatever it does after - a fibre that yielded keeps the strain it took. The step alone, from
        /// <see cref="PlasticHinges.At"/>, has only <see cref="Yield"/> to go by.
        /// </summary>
        public double Ductility { get; internal set; }

        /// <summary>The largest <see cref="Ultimate"/> at this step or any before it.</summary>
        public double UltimateReached { get; internal set; }

        /// <summary>How far it has gone by this step: yielded once it has been past its yield strain, failed once past the strain it fails at.</summary>
        public HingeState Reached =>
            UltimateReached >= 1.0 ? HingeState.Ultimate : Ductility >= 1.0 ? HingeState.Yielded : HingeState.Elastic;

        /// <summary>Yielded at a step before, and back under its yield strain at this one: unloaded, the plastic strain it took left in it.</summary>
        public bool Unloaded => Reached == HingeState.Yielded && Yield < 1.0;
    }

    /// <summary>When a station of a beam first yielded, and first failed, over a run of steps.</summary>
    public sealed class HingeEvent
    {
        public int Beam { get; internal set; }
        public int Station { get; internal set; }
        public double Along { get; internal set; }
        public Point3d Position { get; internal set; }

        /// <summary>The place in the steps given where it first yielded.</summary>
        public int FirstYield { get; internal set; } = -1;

        /// <summary>The place in the steps given where it first failed; -1 when it did not.</summary>
        public int FirstUltimate { get; internal set; } = -1;

        /// <summary>What failed first, in words, when something did.</summary>
        public string Failed { get; internal set; }

        /// <summary>
        /// The ductility it had reached at each of the steps given - its most strained fibre's largest strain
        /// so far over its yield strain - 0 before it yields. It never comes down: a yielded fibre keeps its strain.
        /// </summary>
        public List<double> DuctilityAt { get; internal set; } = new List<double>();

        /// <summary>The ductility it had reached by the last step given.</summary>
        public double Ductility => DuctilityAt.Count == 0 ? 0.0 : DuctilityAt[DuctilityAt.Count - 1];
    }

    /// <summary>
    /// Every station of the beams on fibre sections at every recorded step, read once: <see cref="At"/> gives
    /// a step's stations with what each has reached by then - its ductility so far, and whether it has
    /// failed - without the file. What Model View draws as a push is stepped through.
    /// </summary>
    public sealed class HingeHistory
    {
        private readonly List<HingeStation> _stations;
        private readonly double[][] _yield, _ultimate;

        internal HingeHistory(List<List<HingeStation>> steps)
        {
            // The stations as the first step has them; the others matched to them by beam and station, so
            // that a beam a step leaves out shifts nothing.
            _stations = steps.Count > 0 ? steps[0] : new List<HingeStation>();
            var index = new Dictionary<(int, int), int>();
            for (int i = 0; i < _stations.Count; i++)
                index[(_stations[i].Beam, _stations[i].Station)] = i;
            _yield = new double[steps.Count][];
            _ultimate = new double[steps.Count][];
            for (int k = 0; k < steps.Count; k++)
            {
                _yield[k] = new double[_stations.Count];
                _ultimate[k] = new double[_stations.Count];
                foreach (var s in steps[k])
                {
                    if (!index.TryGetValue((s.Beam, s.Station), out int i)) continue;
                    _yield[k][i] = s.Yield;
                    _ultimate[k][i] = s.Ultimate;
                }
            }
        }

        /// <summary>How many steps it holds.</summary>
        public int Steps => _yield.Length;

        /// <summary>
        /// Every station at <paramref name="step"/> - held to the steps there are - with its strains then, and
        /// the largest it has reached at that step or any before it.
        /// </summary>
        public List<HingeStation> At(int step)
        {
            var result = new List<HingeStation>(_stations.Count);
            if (Steps == 0) return result;
            step = Math.Max(0, Math.Min(step, Steps - 1));
            for (int i = 0; i < _stations.Count; i++)
            {
                double ductility = 0.0, ultimate = 0.0;
                for (int k = 0; k <= step; k++)
                {
                    ductility = Math.Max(ductility, _yield[k][i]);
                    ultimate = Math.Max(ultimate, _ultimate[k][i]);
                }
                var s = _stations[i];
                result.Add(new HingeStation
                {
                    Beam = s.Beam, Station = s.Station, Along = s.Along, Position = s.Position,
                    Yield = _yield[step][i], Ultimate = _ultimate[step][i],
                    Ductility = ductility, UltimateReached = ultimate,
                });
            }
            return result;
        }
    }

    /// <summary>
    /// The plastic hinges of beams on fibre sections: at every station of every such beam, how far its
    /// fibres are past their yield strain and past the strain they fail at.
    ///
    /// Read from the section deformations OpenSees records - the axial strain at the centroid and the
    /// two curvatures, a station - rather than from the fibres' own stresses, which would fill the file
    /// with every fibre of every station at every step. A fibre section keeps plane sections plane, so
    /// the strain anywhere on it follows exactly: eps - y kappaZ + z kappaY, y and z from the centroid,
    /// as FiberSection3d strains its fibres. It is checked where Interaction Domain checks it: at a
    /// patch's mesh vertices, which reach its outline, and at each bar - the most strained point of a
    /// steel flange is its edge, not the middle of the fibre next to it.
    ///
    /// Yielding is the yield strain of a material that has one, fy / E: Steel01 and ReinforcingSteel.
    /// Failing is ReinforcingSteel's strain at fu, and Concrete01's crushing strain, and a MinMax
    /// wrapper's limits where they come first. The stresses themselves are not worked out: they hang on
    /// each fibre's history, which only OpenSees has.
    /// </summary>
    public static class PlasticHinges
    {
        /// <summary>Whether a model has a beam on a fibre section, which is what has hinges to show.</summary>
        public static bool HasFibreBeams(Model model) =>
            model?.Beams != null && model.Beams.Any(beam => !(beam is Alpaca4d.Element.Truss) && beam.Section is FiberSection);

        /// <summary>
        /// The state of every station of every beam on a fibre section, at a recorded step, and that step
        /// alone: a station that yielded at a step before and has unloaded since reads elastic. For what each
        /// has reached by a step, read the analysis once with <see cref="Track"/>.
        /// </summary>
        public static List<HingeStation> At(Model model, int step)
        {
            var stations = Evaluate(model, Read.SectionDeformations(model, step), new Dictionary<FiberSection, Limits>());
            foreach (var s in stations)
            {
                s.Ductility = s.Yield;
                s.UltimateReached = s.Ultimate;
            }
            return stations;
        }

        /// <summary>
        /// Every station at every recorded step, read once - the file opened once - for <see cref="HingeHistory.At"/>
        /// to give any step's stations with what each has reached by then.
        /// </summary>
        public static HingeHistory Track(Model model)
        {
            int count = Read.StepCount(model);
            var steps = Enumerable.Range(0, Math.Max(0, count)).ToList();
            var limits = new Dictionary<FiberSection, Limits>();
            var all = steps.Count == 0 ? new List<Dictionary<int, double[][]>>() : Read.SectionDeformations(model, steps);
            return new HingeHistory(all.Select(step => Evaluate(model, step, limits)).ToList());
        }

        /// <summary>
        /// The ductility a hinge is drawn by: four bands, from first yield to twice the yield strain, to four
        /// times, to eight, and past - their lower ends. Failed is a fifth state, drawn on its own.
        /// </summary>
        public static readonly double[] DuctilityBands = { 1.0, 2.0, 4.0, 8.0 };

        /// <summary>The band, 0 to 3, a ductility of at least 1 falls in.</summary>
        public static int Band(double ductility)
        {
            int band = 0;
            for (int i = 1; i < DuctilityBands.Length; i++)
                if (ductility >= DuctilityBands[i]) band = i;
            return band;
        }

        /// <summary>
        /// The colour of each band: yellow, orange, red and dark red - from Alpaca4d's orange, the colour a hinge
        /// had before it had bands - and purple for failed, as before.
        /// </summary>
        public static readonly System.Drawing.Color[] BandColours =
        {
            System.Drawing.Color.FromArgb(242, 201, 76),
            System.Drawing.Color.FromArgb(235, 108, 63),
            System.Drawing.Color.FromArgb(214, 40, 40),
            System.Drawing.Color.FromArgb(122, 14, 28),
        };

        /// <summary>A failed station's colour: Alpaca4d's purple.</summary>
        public static readonly System.Drawing.Color FailedColour = System.Drawing.Color.FromArgb(158, 53, 218);

        /// <summary>
        /// When each station first yielded, and first failed, over <paramref name="steps"/> - a pushover's
        /// recorded steps, say - in the order they first yielded. Stations that never yielded are left out.
        /// </summary>
        public static List<HingeEvent> History(Model model, IReadOnlyList<int> steps)
        {
            var events = new Dictionary<(int, int), HingeEvent>();
            var limits = new Dictionary<FiberSection, Limits>();
            var all = Read.SectionDeformations(model, steps);
            // Every station's largest yield ratio so far, for the ductility a hinge has reached at each step.
            var most = new Dictionary<(int, int), double>();
            for (int k = 0; k < steps.Count; k++)
            {
                foreach (var station in Evaluate(model, all[k], limits))
                {
                    var key = (station.Beam, station.Station);
                    most.TryGetValue(key, out double sofar);
                    most[key] = Math.Max(sofar, station.Yield);
                    if (station.State == HingeState.Elastic)
                        continue;
                    if (!events.TryGetValue(key, out var e))
                    {
                        events[key] = e = new HingeEvent
                        {
                            Beam = station.Beam, Station = station.Station, Along = station.Along, Position = station.Position,
                            FirstYield = k,
                        };
                    }
                    if (station.State == HingeState.Ultimate && e.FirstUltimate < 0)
                    {
                        e.FirstUltimate = k;
                        e.Failed = station.Governs;
                    }
                }
                // A hinge's steps before it yielded, 0; from then on, the most it has reached.
                foreach (var e in events.Values)
                {
                    while (e.DuctilityAt.Count < k) e.DuctilityAt.Add(0.0);
                    e.DuctilityAt.Add(most[(e.Beam, e.Station)]);
                }
            }
            return events.Values.OrderBy(e => e.FirstYield).ThenBy(e => e.Beam).ThenBy(e => e.Station).ToList();
        }

        private static List<HingeStation> Evaluate(Model model, Dictionary<int, double[][]> deformations, Dictionary<FiberSection, Limits> limits)
        {
            var stations = new List<HingeStation>();
            for (int b = 0; b < model.Beams.Count; b++)
            {
                var beam = model.Beams[b];
                if (beam is Alpaca4d.Element.Truss || !(beam.Section is FiberSection section) || !beam.Id.HasValue)
                    continue;
                if (!deformations.TryGetValue(beam.Id.Value, out var recorded))
                    continue;

                if (!limits.TryGetValue(section, out var limit))
                    limits[section] = limit = Limits.Of(section);

                var start = beam.Curve.PointAtStart;
                var end = beam.Curve.PointAtEnd;
                var along = beam.BeamIntegration?.SectionLocations(start.DistanceTo(end));

                for (int s = 0; s < recorded.Length; s++)
                {
                    double t = along != null && s < along.Count ? along[s] : (recorded.Length == 1 ? 0.5 : (double)s / (recorded.Length - 1));
                    var station = limit.Check(recorded[s][0], recorded[s][1], recorded[s][2]);
                    station.Beam = b;
                    station.Station = s;
                    station.Along = t;
                    station.Position = start + (end - start) * t;
                    stations.Add(station);
                }
            }
            return stations;
        }

        /// <summary>Where a station is along its beam, in words: an end, or a share of its length.</summary>
        public static string Where(double along)
        {
            if (along <= 1e-9) return "I end";
            if (along >= 1.0 - 1e-9) return "J end";
            return $"{along:0.##} L";
        }

        /// <summary>A fibre section's points to check, about its centroid, and the strains they yield and fail at.</summary>
        private sealed class Limits
        {
            private readonly List<(double Y, double Z, double Yield, double Tension, double Compression, string Name, bool Concrete)> _points =
                new List<(double, double, double, double, double, string, bool)>();

            public static Limits Of(FiberSection section)
            {
                var limits = new Limits();
                var layout = SectionLayout.Of(section);
                double cy = section.CentroidY, cz = section.CentroidZ;
                foreach (var point in layout.CheckPoints)
                {
                    var (yieldStrain, tension, compression, concrete) = StrainsOf(point.Material);
                    if (double.IsNaN(yieldStrain) && double.IsInfinity(tension) && double.IsInfinity(compression))
                        continue;
                    string name = string.IsNullOrWhiteSpace(point.Material.MatName) ? point.Material.GetType().Name : point.Material.MatName;
                    limits._points.Add((point.Y - cy, point.Z - cz, yieldStrain, tension, compression, name, concrete));
                }
                return limits;
            }

            /// <summary>The state of a section bent eps, kappaZ, kappaY.</summary>
            public HingeStation Check(double eps, double kappaZ, double kappaY)
            {
                double yielded = 0.0, failed = 0.0;
                string governs = null, failing = null;
                foreach (var p in _points)
                {
                    double strain = eps - p.Y * kappaZ + p.Z * kappaY;
                    if (p.Yield > 0.0)
                    {
                        double r = Math.Abs(strain) / p.Yield;
                        if (r > yielded)
                        {
                            yielded = r;
                            governs = $"{p.Name} in {(strain >= 0.0 ? "tension" : "compression")}";
                        }
                    }
                    double u = strain >= 0.0 ? strain / p.Tension : -strain / p.Compression;
                    if (u > failed)
                    {
                        failed = u;
                        failing = p.Concrete ? $"{p.Name} crushed" : $"{p.Name} at its ultimate strain in {(strain >= 0.0 ? "tension" : "compression")}";
                    }
                }
                return new HingeStation { Yield = yielded, Ultimate = failed, Governs = failed >= 1.0 ? failing : governs };
            }

            /// <summary>
            /// The yield strain of a material - NaN for one that does not yield - and the strains it
            /// fails at in tension and in compression, positive; infinity for none.
            /// </summary>
            private static (double Yield, double Tension, double Compression, bool Concrete) StrainsOf(IMaterial material)
            {
                double yieldStrain = double.NaN, tension = double.PositiveInfinity, compression = double.PositiveInfinity;
                bool concrete = false;
                MinMax wrapper = null;
                switch (material)
                {
                    case Steel01 steel:
                        if (steel.E0 > 0.0) yieldStrain = Math.Abs(steel.Fy) / steel.E0;
                        wrapper = steel.MinMax;
                        break;
                    case ReinforcingSteel bar:
                        if (bar.Es > 0.0) yieldStrain = Math.Abs(bar.Fy) / bar.Es;
                        if (bar.EpsilonUlt > 0.0) tension = compression = bar.EpsilonUlt;
                        wrapper = bar.MinMax;
                        break;
                    case Concrete01 c:
                        concrete = true;
                        if (c.EpsilonCu != 0.0) compression = Math.Abs(c.EpsilonCu);
                        wrapper = c.MinMax;
                        break;
                    case IFibreMaterial fibre:
                        concrete = fibre.IsConcrete;
                        if (!double.IsNaN(fibre.YieldStrain)) yieldStrain = fibre.YieldStrain;
                        if (fibre.CrushingStrain > 0.0) compression = fibre.CrushingStrain;
                        wrapper = fibre.MinMax;
                        break;
                }
                if (wrapper != null)
                {
                    if (wrapper.MaxStrain > 0.0) tension = Math.Min(tension, wrapper.MaxStrain);
                    if (wrapper.MinStrain < 0.0) compression = Math.Min(compression, -wrapper.MinStrain);
                }
                return (yieldStrain, tension, compression, concrete);
            }
        }
    }
}
