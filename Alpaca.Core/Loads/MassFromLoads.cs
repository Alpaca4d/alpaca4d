using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d.Generic;

namespace Alpaca4d.Loads
{
    /// <summary>
    /// Mass made of loads: the weight of what the loads stand for, as mass on the nodes they load. A
    /// structure vibrates with more than its own mass. SCI P354 takes the self-weight, the superimposed
    /// dead load and 10% of the imposed load for a floor's footfall check; the seismic mass of NTC 2018
    /// 3.2.4 and EN 1998-1 3.2.4 is G1 + G2 + Σ ψ2j Qkj. The structure's own part is already in the
    /// model, from the density of its materials. This is the rest: one per kind of load, with its
    /// factor.
    ///
    /// Only the downward part of a load weighs anything, and it weighs as much along every axis: a
    /// mass shakes sideways as readily as it falls. It lands where Assemble puts the load itself - a
    /// point load at its point, half a line load at each end of each beam it goes on, a mesh load shared
    /// among the corners of each shell - and is worked out there, once the elements a load names by
    /// ElementId are known.
    ///
    /// The loads are given themselves, or by the name of the Load Pattern that holds them - the load
    /// case, as a mass source names it in other programs: "Imposed" at 0.1. A name is looked up among the
    /// patterns given to Assemble, when it lumps the mass.
    /// </summary>
    public class MassFromLoads : ILoad
    {
        /// <summary>The loads whose weight becomes mass, given themselves.</summary>
        public IReadOnlyList<ILoad> Loads { get; }

        /// <summary>The names of the load patterns whose loads' weight becomes mass, looked up in the model's patterns.</summary>
        public IReadOnlyList<string> PatternNames { get; }

        /// <summary>How much of their weight: 1 for a permanent load, 0.1 of an imposed one for footfall, ψ2 for an earthquake.</summary>
        public double Factor { get; }

        public LoadType Type { get; set; } = LoadType.Mass;

        public ITimeSeries TimeSeries { get; set; }

        public MassFromLoads(IEnumerable<ILoad> loads, double factor, IEnumerable<string> patternNames = null)
        {
            Loads = (loads ?? Enumerable.Empty<ILoad>()).Where(load => load != null).ToList();
            PatternNames = (patternNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
            Factor = factor;
        }

        /// <summary>Whether a pattern's name is <paramref name="name"/>: the same text, whatever its case and the spaces round it.</summary>
        public static bool Names(string patternName, string name) =>
            !string.IsNullOrWhiteSpace(patternName) &&
            string.Equals(patternName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The loads of the patterns of <paramref name="model"/> that <see cref="PatternNames"/> name. What
        /// cannot be mass is said on the model, for Assemble to show: a name no pattern has, a name two
        /// patterns share, and a named pattern's Gravity Load and loads in local axes, which are left out.
        /// </summary>
        private List<ILoad> NamedLoads(Model model)
        {
            var loads = new List<ILoad>();
            foreach (var name in PatternNames)
            {
                var patterns = model.LoadPatterns.Where(pattern => Names(pattern.Name, name)).ToList();
                if (patterns.Count == 0)
                {
                    var known = model.LoadPatterns.Select(pattern => pattern.Name?.Trim())
                        .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                    model.Warnings.Add($"Mass from Loads names the load pattern \"{name}\", which none of the Load Patterns given to Assemble has" +
                        (known.Count > 0 ? $" - they are {string.Join(", ", known.Select(n => $"\"{n}\""))}" : ": none of them has a Name") +
                        ". No mass is made of it.");
                    continue;
                }
                if (patterns.Count > 1)
                    model.Remarks.Add($"{patterns.Count} load patterns are named \"{name}\": Mass from Loads makes mass of the loads of all of them.");

                int gravity = 0, local = 0;
                foreach (var load in patterns.SelectMany(pattern => pattern.Load ?? new List<ILoad>()))
                {
                    if (load is Gravity) gravity++;
                    else if (load is LineLoad line && line.IsLocal || load is MeshLoad mesh && mesh.IsLocal) local++;
                    loads.Add(load);
                }
                if (gravity > 0)
                    model.Remarks.Add($"Mass from Loads leaves out the Gravity Load in \"{name}\": the elements' own weight is mass already, through the density of their materials.");
                if (local > 0)
                    model.Warnings.Add($"Mass from Loads leaves out {local} load{(local == 1 ? "" : "s")} in local axes in \"{name}\": only a load along the global axes is weighed.");
            }
            return loads;
        }

        /// <summary>
        /// The masses, in kg, on the points of <paramref name="model"/> the loads go on. Called by
        /// Assemble once the elements have their tags, which a load naming them by ElementId needs.
        /// </summary>
        public List<MassLoad> Lump(Model model)
        {
            var masses = new List<MassLoad>();

            void Add(Point3d point, double weight)
            {
                if (weight > 0.0)
                    masses.Add(new MassLoad(point, Isotropic(weight * Factor), Vector3d.Zero));
            }

            foreach (var load in Loads.Concat(NamedLoads(model)))
            {
                switch (load)
                {
                    case PointLoad point:
                        Add(point.Pos, Weight(point.Force));
                        break;

                    case LineLoad line when !line.IsLocal:
                        foreach (var beam in line.AppliedTo(model.Beams))
                        {
                            double half = Weight(line.GlobalForce) * beam.Curve.GetLength() / 2.0;
                            Add(beam.Curve.PointAtStart, half);
                            Add(beam.Curve.PointAtEnd, half);
                        }
                        break;

                    case MeshLoad mesh when !mesh.IsLocal:
                        foreach (var shell in mesh.AppliedTo(model.Shells))
                        {
                            // As MeshLoad.PointLoads shares the load: the area over the corners, evenly.
                            var corners = shell.Mesh.Vertices.ToPoint3dArray();
                            double share = Weight(mesh.GlobalForce) * Alpaca4d.Utils.MeshArea(shell.Mesh) / corners.Length;
                            foreach (var corner in corners)
                                Add(corner, share);
                        }
                        break;
                }
            }

            return masses;
        }

        /// <summary>What a force weighs: its downward part, nothing for one that pushes up.</summary>
        public static double Weight(Vector3d force) => Math.Max(0.0, -force.Z);

        /// <summary>A weight in the model's force unit as a mass in kg, the same along every axis.</summary>
        public static Vector3d Isotropic(double weight)
        {
            double kg = ModelGravity.WeightToKg(weight);
            return new Vector3d(kg, kg, kg);
        }

        public void SetTag(Model model)
        {
        }

        /// <summary>Nothing of its own: Assemble writes the masses it makes with every other mass, a line per node.</summary>
        public string WriteTcl() => "";

        public override string ToString()
        {
            var from = PatternNames.Select(name => $"\"{name}\"").ToList();
            if (Loads.Count > 0 || from.Count == 0)
                from.Add($"{Loads.Count} load{(Loads.Count == 1 ? "" : "s")}");
            return $"Mass from {string.Join(", ", from)} × {Factor:0.###}";
        }
    }
}
