using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Section
{
    /// <summary>
    /// A section made of fibres, each a point with an area and a material of its own.
    ///
    /// It is a beam section too: a ForceBeamColumn on it integrates the fibres' materials along its
    /// length, and yields where they yield - what a pushover needs. On a beam the fibres' y and z are
    /// the beam's local y and z, as OpenSees reads them and as Fiber Section draws them, and the beam's
    /// axis runs through the area centroid of the fibres, where Fiber Section draws its axes and where
    /// Interaction Domain takes its moments. So a section drawn anywhere in Rhino makes a beam on its
    /// centroid, and the forces a beam on it reports are the ones Domain Check reads.
    ///
    /// What a beam asks of a section beyond the fibres - an area, inertias, a material to read a
    /// modulus and a density from - is worked out from the fibres at their initial stiffness: see
    /// <see cref="Material"/>. OpenSees never sees any of it; it is for the self-weight, the mass and
    /// the drawing, and for whatever else reads a beam's section.
    /// </summary>
    public partial class FiberSection : IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string SectionName { get; set; }
        public System.Drawing.Color? Color { get; set; }

        public List<PointFiber> PointFibers { get; set; } = new List<PointFiber>();
        public List<Patch> Patches { get; set; } = new List<Patch>();
        public List<Layer> Layers { get; set; } = new List<Layer>();
        public List<PointFiber> Fibers
        {
            get
            {
                // Point fibres, then layers, then patches - the order WriteTcl declares
                // them in, and so the order OpenSees holds them in and reports them in.
                // These two had layers and patches the other way round, which only stayed
                // invisible while every fibre had a recorder of its own to be matched by
                // coordinate.
                var fibers = new List<PointFiber>();
                fibers.AddRange(this.PointFibers);
                fibers.AddRange(this.Layers.SelectMany(x => x.Fibers));
                fibers.AddRange(this.Patches.SelectMany(x => x.Fibers));
                var i = 0;
                foreach(var fiber in fibers)
                {
                    fiber.Index = i;
                    i++;
                }
                return fibers;
            }
        }

        public double? GJ { get; set; }
        public FiberSection()
        {
        }

        public FiberSection(List<PointFiber> pointFibers, List<Layer> layers, List<Patch> patches, double gj)
        {
            this.PointFibers = pointFibers;
            this.Layers = layers;
            this.Patches = patches;
            this.GJ = gj;
        }

        /// <summary>The materials of the fibres, each once, in the order the fibres name them first.</summary>
        public List<IMaterial> Materials
        {
            get
            {
                var materials = new List<IMaterial>();
                materials.AddRange(this.PointFibers.Select(x => x.Material));
                materials.AddRange(this.Layers.Select(x => x.Material));
                materials.AddRange(this.Patches.Select(x => x.Material));
                return materials.Where(x => x != null).Distinct().ToList();
            }
        }

        /// <summary>
        /// The section as a beam sees it, worked out once from the fibres. A fibre section is not
        /// changed after it is made - Fiber Section makes a new one each solve - so it is kept.
        /// Reading a patch's fibres explodes its mesh, and a beam reads its section often.
        /// </summary>
        private Stiffness _stiffness;
        private Stiffness Initial => _stiffness ??= Stiffness.Of(this);

        /// <summary>The area centroid of the fibres, in their own y and z: where a beam's axis runs.</summary>
        public double CentroidY => Initial.CentroidY;
        public double CentroidZ => Initial.CentroidZ;

        /// <summary>
        /// The area of the fibres, every one counted in the material of <see cref="Material"/>: a
        /// bar's area times its modulus over that material's. Times Material.E it is the section's
        /// initial axial stiffness, and times Material.Rho its mass per metre.
        /// </summary>
        public double Area => Initial.Area;

        /// <summary>The second moment of area about the local z axis - of y² - through the centroid, as <see cref="Area"/>.</summary>
        public double Izz => Initial.Izz;

        /// <summary>The second moment of area about the local y axis - of z² - through the centroid, as <see cref="Area"/>.</summary>
        public double Iyy => Initial.Iyy;

        /// <summary>GJ over Material.G, so that the two give back the torsional stiffness the section was given.</summary>
        public double J => Initial.G > 0.0 ? (this.GJ ?? 0.0) / Initial.G : 0.0;

        /// <summary>A fibre section has no shear deformation: OpenSees records no shear for it.</summary>
        public double AlphaY => 0.0;
        public double AlphaZ => 0.0;

        /// <summary>
        /// A stand-in for the section's materials, never written for OpenSees: the modulus and the
        /// density of the fibres, at their initial stiffness, in the material with most area -
        /// concrete in a reinforced concrete section. It is what the parts of Alpaca4d that read one
        /// material off a beam read: the mass and the self-weight, the colour by material, the
        /// stiffness a spring support is scaled against. Setting it changes nothing.
        /// </summary>
        public IUniaxialMaterial Material
        {
            get => Initial.Material;
            set { }
        }

        /// <summary>
        /// The section's outline for drawing a beam: each patch's outline, or a circle for each bar
        /// when there are no patches. About the centroid, and turned so that Model View, which lays
        /// out every beam's section with x along local z and y along local y, puts the fibres where
        /// OpenSees has them.
        /// </summary>
        public List<Curve> Curves => (_outline ??= Stiffness.Outline(this, CentroidY, CentroidZ))
                                     .Select(curve => curve.DuplicateCurve()).ToList();

        /// <summary>Kept apart from the stiffness: it takes Rhino's geometry, which only runs inside Rhino.</summary>
        private List<Curve> _outline;

        public Brep Brep
        {
            get
            {
                var curves = this.Curves;
                if (curves.Count == 0)
                    return null;
                var planarBreps = Rhino.Geometry.Brep.CreatePlanarBreps(curves, 0.001);
                return planarBreps != null && planarBreps.Length > 0 ? planarBreps[0] : null;
            }
        }

        /// <summary>The section's name - what a Panel shows - or the materials it is made of.</summary>
        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(this.SectionName))
                return this.SectionName;
            var names = this.Materials.Select(MaterialName).ToList();
            return names.Count == 0 ? "Fiber Section" : $"Fiber Section ({string.Join(" + ", names)})";
        }

        /// <summary>
        /// The section as a beam takes it: under its own Id, with the fibres about their area
        /// centroid, so that the beam's axis runs through it.
        /// </summary>
        public string WriteTcl()
        {
            return Write(this.Id, this.CentroidY, this.CentroidZ);
        }

        /// <summary>
        /// The section, under <paramref name="tag"/> in place of its own Id, with the fibres where
        /// they were drawn.
        ///
        /// A deck holding a single section - the moment-curvature one - wants a known
        /// tag, and used to get one by assigning to Id. That reached back into the object
        /// the upstream component still holds and hands to everything else downstream; a
        /// tag a caller needs for the length of one deck belongs to the call, not to the
        /// section.
        /// </summary>
        public string WriteTcl(int? tag)
        {
            return Write(tag ?? this.Id, 0.0, 0.0);
        }

        private string Write(int? tag, double dy, double dz)
        {
            var sb = new StringBuilder();

            sb.Append($"section Fiber {tag} -GJ {TclNumber.Write(this.GJ)} {{\n");
            foreach (var element in PointFibers)
            {
                sb.Append(element.WriteTcl(dy, dz));
            }

            foreach (var element in Layers)
            {
                sb.Append(element.WriteTcl(dy, dz));
            }

            foreach (var element in Patches)
            {
                sb.Append(element.WriteTcl(dy, dz));
            }

            sb.Append("}\n");

            return sb.ToString();
        }

        /// <summary>
        /// A fibre material's initial modulus, kN/m2: Steel01's E0, ReinforcingSteel's Es,
        /// Concrete01's 2 fpc / epsc0 - the slope its parabola starts with - and an elastic
        /// material's E. 0 for a material it does not know.
        /// </summary>
        public static double InitialModulus(IMaterial material)
        {
            switch (material)
            {
                case Alpaca4d.Material.Steel01 steel:
                    return steel.E0;
                case Alpaca4d.Material.ReinforcingSteel bar:
                    return bar.Es;
                case Alpaca4d.Material.Concrete01 concrete:
                    return concrete.EpsilonCo != 0.0 ? Math.Abs(2.0 * concrete.FpCo / concrete.EpsilonCo) : 0.0;
                case IUniaxialMaterial elastic:
                    return elastic.E;
                case Alpaca4d.Material.UniaxialMaterialENT ent:
                    return ent.E;
                case Alpaca4d.Material.UniaxialMaterialElasticPPGap gap:
                    return gap.E;
                default:
                    return 0.0;
            }
        }

        /// <summary>Concrete, unless told otherwise, weighs 2 500 kg/m3.</summary>
        public const double ConcreteDensity = 2500.0;

        /// <summary>Steel, unless told otherwise, weighs 7 850 kg/m3.</summary>
        public const double SteelDensity = 7850.0;

        /// <summary>
        /// A fibre material's density, kg/m3: its own, or, when it has none - the fibre material
        /// components set none - 2 500 for Concrete01 and 7 850 for Steel01 and ReinforcingSteel. 0
        /// for any other material without one.
        /// </summary>
        public static double Density(IMaterial material)
        {
            if (material?.Rho != null)
                return material.Rho.Value;
            switch (material)
            {
                case Alpaca4d.Material.Concrete01 _:
                    return ConcreteDensity;
                case Alpaca4d.Material.Steel01 _:
                case Alpaca4d.Material.ReinforcingSteel _:
                    return SteelDensity;
                default:
                    return 0.0;
            }
        }

        private static string MaterialName(IMaterial material)
        {
            return string.IsNullOrWhiteSpace(material.MatName) ? material.GetType().Name : material.MatName;
        }

        /// <summary>What <see cref="Stiffness.Of"/> works out: see the properties of the section that read it.</summary>
        private sealed class Stiffness
        {
            public double CentroidY, CentroidZ, Area, Izz, Iyy, G;
            public IUniaxialMaterial Material;

            public static Stiffness Of(FiberSection section)
            {
                var fibres = section.Fibers.Where(f => f.Area > 0.0 && f.Material != null).ToList();
                var result = new Stiffness();

                double grossArea = fibres.Sum(f => f.Area);
                if (grossArea > 0.0)
                {
                    result.CentroidY = fibres.Sum(f => f.Area * f.Pos.X) / grossArea;
                    result.CentroidZ = fibres.Sum(f => f.Area * f.Pos.Y) / grossArea;
                }

                // The material with most area sets the modulus everything is counted in.
                var main = fibres.GroupBy(f => f.Material)
                                 .OrderByDescending(group => group.Sum(f => f.Area))
                                 .Select(group => group.Key)
                                 .FirstOrDefault();
                double reference = InitialModulus(main);

                double mass = 0.0;
                foreach (var fibre in fibres)
                {
                    // Counted in the reference material; at its own area when there is none to count in.
                    double share = reference > 0.0 ? InitialModulus(fibre.Material) / reference : 1.0;
                    double area = fibre.Area * share;
                    double y = fibre.Pos.X - result.CentroidY;
                    double z = fibre.Pos.Y - result.CentroidZ;
                    result.Area += area;
                    result.Izz += area * y * y;
                    result.Iyy += area * z * z;
                    mass += fibre.Area * Density(fibre.Material);
                }

                double nu = main is Alpaca4d.Material.Concrete01 ? 0.2 : 0.3;
                double e = reference > 0.0 ? reference : 1.0;
                result.G = e / (2.0 * (1.0 + nu));

                string name = string.Join(" + ", section.Materials.Select(MaterialName));
                result.Material = new Alpaca4d.Material.UniaxialMaterialElastic(
                    string.IsNullOrWhiteSpace(name) ? "Fiber Section" : name, e, e, 0.0, result.G, nu,
                    result.Area > 0.0 ? mass / result.Area : 0.0);

                return result;
            }

            /// <summary>See <see cref="FiberSection.Curves"/>.</summary>
            public static List<Curve> Outline(FiberSection section, double cy, double cz)
            {
                var curves = new List<Curve>();

                // Model View maps a section's x onto local z and its y onto local y; a fibre's y is
                // its x in the drawing. Swapping the two, about the centroid, puts each fibre where
                // OpenSees has it.
                var toBeam = Transform.Translation(-cy, -cz, 0.0);
                toBeam = Transform.Mirror(new Plane(Point3d.Origin, new Vector3d(1.0, -1.0, 0.0))) * toBeam;

                foreach (var patch in section.Patches)
                {
                    var outlines = patch.PatchGeometry?.GetNakedEdges();
                    if (outlines == null)
                        continue;
                    foreach (var outline in outlines)
                    {
                        if (outline == null || outline.Count < 3)
                            continue;
                        var curve = outline.ToNurbsCurve();
                        curve.Transform(toBeam);
                        curves.Add(curve);
                    }
                }

                if (curves.Count == 0)
                {
                    foreach (var fibre in section.Fibers.Where(f => f.Area > 0.0))
                    {
                        var circle = new Circle(fibre.Pos, Math.Sqrt(fibre.Area / Math.PI)).ToNurbsCurve();
                        circle.Transform(toBeam);
                        curves.Add(circle);
                    }
                }

                return curves;
            }
        }
    }
}
