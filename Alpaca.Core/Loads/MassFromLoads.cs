using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d.Generic;

namespace Alpaca4d.Loads
{
    /// <summary>
    /// Mass made of loads: the weight of what the loads stand for, as mass on the nodes they load. The
    /// seismic mass of NTC 2018 3.2.4 and EN 1998-1 3.2.4 is the structure's own, the permanent loads
    /// and ψ2 of the variable ones - G1 + G2 + Σ ψ2j Qkj - and the structure's own is already in the
    /// model, from the density of its materials. This is the rest: one per kind of load, with its
    /// factor.
    ///
    /// Only the downward part of a load weighs anything, and it weighs as much along every axis: a
    /// mass shakes sideways as readily as it falls. It lands where Assemble puts the load itself - a
    /// point load at its point, half a line load at each end of each beam it goes on, a mesh load shared
    /// among the corners of each shell - and is worked out there, once the elements a load names by
    /// ElementId are known.
    /// </summary>
    public class MassFromLoads : ILoad
    {
        /// <summary>The loads whose weight becomes mass.</summary>
        public IReadOnlyList<ILoad> Loads { get; }

        /// <summary>How much of their weight: 1 for a permanent load, ψ2 for a variable one.</summary>
        public double Factor { get; }

        public LoadType Type { get; set; } = LoadType.Mass;

        public ITimeSeries TimeSeries { get; set; }

        public MassFromLoads(IEnumerable<ILoad> loads, double factor)
        {
            Loads = (loads ?? Enumerable.Empty<ILoad>()).Where(load => load != null).ToList();
            Factor = factor;
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

            foreach (var load in Loads)
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
            double kg = ModelMass.ToKg(weight / Alpaca4d.Seismic.ResponseSpectrum.ModelG);
            return new Vector3d(kg, kg, kg);
        }

        public void SetTag(Model model)
        {
        }

        /// <summary>Nothing of its own: Assemble writes the masses it makes with every other mass, a line per node.</summary>
        public string WriteTcl() => "";

        public override string ToString() => $"Mass from {Loads.Count} load{(Loads.Count == 1 ? "" : "s")} × {Factor:0.###}";
    }
}
