using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alpaca4d.Generic;
using Alpaca4d.Material;

namespace Alpaca4d.Section
{
    /// <summary>What an interaction domain is built with.</summary>
    public sealed class DomainSettings
    {
        /// <summary>
        /// εud: the strain, either way, that every fibre that is not Concrete01 is held to -
        /// reinforcement and structural steel alike. Positive. A MinMax wrapper's bounds hold
        /// instead where they are tighter.
        /// </summary>
        public double SteelUltimateStrain { get; set; } = 0.01;

        /// <summary>
        /// εcu: the compressive strain every Concrete01 is held to, positive; null takes each
        /// Concrete01's own EpsilonCu.
        /// </summary>
        public double? ConcreteUltimateStrain { get; set; }

        /// <summary>Levels of N between the two poles, for the surface.</summary>
        public int Levels { get; set; } = 20;

        /// <summary>Points on each level before refinement.</summary>
        public int PointsPerLevel { get; set; } = 72;

        /// <summary>Steps either way on each material's stress-strain curve.</summary>
        public int CurveSteps { get; set; } = 4000;

        internal void Validate()
        {
            if (!(SteelUltimateStrain > 0.0))
                throw new ArgumentOutOfRangeException(nameof(SteelUltimateStrain), "εud has to be a positive strain.");
            if (ConcreteUltimateStrain.HasValue && !(ConcreteUltimateStrain.Value > 0.0))
                throw new ArgumentOutOfRangeException(nameof(ConcreteUltimateStrain), "εcu has to be a positive strain, or left empty.");
            if (Levels < 1)
                throw new ArgumentOutOfRangeException(nameof(Levels), "The domain needs at least one level of N.");
            if (PointsPerLevel < 8)
                throw new ArgumentOutOfRangeException(nameof(PointsPerLevel), "A level needs at least eight points.");
            if (CurveSteps < 10)
                throw new ArgumentOutOfRangeException(nameof(CurveSteps), "A stress-strain curve needs at least ten steps.");
        }
    }

    /// <summary>A material of the section, and the strains it is held to.</summary>
    public sealed class DomainMaterial
    {
        public IMaterial Material { get; internal set; }

        /// <summary>The tag its fibres name in OpenSees: its MinMax wrapper's, when it has one.</summary>
        public int Tag { get; internal set; }

        /// <summary>
        /// Held to Eurocode 2's concrete limits - εcu at the most compressed point, and εc2 at
        /// point C when the section is all in compression - rather than to εud either way.
        /// </summary>
        public bool IsConcrete { get; internal set; }

        /// <summary>The compressive strain it is held to, positive; infinity for none.</summary>
        public double CompressionLimit { get; internal set; }

        /// <summary>The tensile strain it is held to; infinity for none.</summary>
        public double TensionLimit { get; internal set; }

        /// <summary>εc2, positive, for a concrete with a pivot C; NaN otherwise.</summary>
        public double PivotStrain { get; internal set; } = double.NaN;

        /// <summary>
        /// The strain it first yields at, either way - fy over its elastic modulus, for Steel01 and
        /// ReinforcingSteel - or NaN for a material that has no yield of its own to report.
        /// </summary>
        public double YieldStrain { get; internal set; } = double.NaN;

        /// <summary>The material's stress-strain curve, as OpenSees traced it.</summary>
        public StressStrainCurve Curve { get; internal set; }

        /// <summary>Whether a limit came from a MinMax wrapper, which fails the material right at it.</summary>
        internal bool CompressionFromMinMax;
        internal bool TensionFromMinMax;

        /// <summary>The convex hull of its check points, about the centroid.</summary>
        internal double[] HullY = new double[0];
        internal double[] HullZ = new double[0];

        public string Name
        {
            get
            {
                var text = Material?.ToString();
                return string.IsNullOrWhiteSpace(text) || text.Contains("\n") ? $"material {Tag}" : text;
            }
        }

        public override string ToString()
        {
            string limits = IsConcrete
                ? $"εcu {CompressionLimit:0.#####}" + (double.IsNaN(PivotStrain) ? "" : $", εc2 {PivotStrain:0.#####}")
                : $"-{CompressionLimit:0.#####} to +{TensionLimit:0.#####}";
            return $"{Name}: {(IsConcrete ? "concrete" : "steel")}, {limits}";
        }
    }

    /// <summary>A point of the domain, and the ultimate strain plane it comes from.</summary>
    public struct DomainPoint
    {
        /// <summary>Axial force, kN, tension positive.</summary>
        public double N;
        /// <summary>Moment about the local y axis, kN m, about the centroid.</summary>
        public double My;
        /// <summary>Moment about the local z axis, kN m, about the centroid.</summary>
        public double Mz;

        /// <summary>The strain at the centroid.</summary>
        public double Axial;
        /// <summary>Curvature about y, 1/m: strain = Axial - y CurvatureZ + z CurvatureY.</summary>
        public double CurvatureY;
        /// <summary>Curvature about z, 1/m.</summary>
        public double CurvatureZ;

        public bool IsValid => !double.IsNaN(N);

        public static DomainPoint Invalid => new DomainPoint { N = double.NaN, My = double.NaN, Mz = double.NaN };
    }

    /// <summary>
    /// The N-My-Mz resistance domain of a fibre section at the ultimate limit state: every
    /// combination of axial force and biaxial bending the section carries without a fibre passing
    /// its material's ultimate strain.
    ///
    /// The domain is swept over strain planes, not over loads. Each direction on the sphere of
    /// planes (ε0, κy h, κz h) is scaled until the first fibre reaches its limit - the pivots of
    /// Eurocode 2 6.1, generalised to any section - and the fibres integrated there give one point
    /// of the boundary. Nothing has to converge, which a held axial force near the section's
    /// capacity never does, and the materials are OpenSees' own, traced once
    /// (<see cref="StressStrainCurve"/>). φ = 0 is pure tension and φ = π pure compression; θ is the
    /// direction of the curvature.
    ///
    /// Moments are about the area centroid of every fibre, steel included, which is the point
    /// OpenSees' fibre section takes them about, so they agree with Moment Curvature's.
    /// </summary>
    public sealed class InteractionDomain
    {
        public DomainSettings Settings { get; }
        public IReadOnlyList<DomainMaterial> Materials { get; }

        /// <summary>The point moments are taken about, in the section's own y and z.</summary>
        public double CentroidY { get; }
        public double CentroidZ { get; }

        /// <summary>The strain every curve was traced to, either way.</summary>
        public double MaxStrain { get; }

        /// <summary>The section in uniform tension at its limit: the largest N.</summary>
        public DomainPoint TensionPole { get; }

        /// <summary>The section in uniform compression at its limit: the smallest N.</summary>
        public DomainPoint CompressionPole { get; }

        /// <summary>The axial resistance in tension, kN, positive (zero for plain concrete).</summary>
        public double Nt => TensionPole.N;

        /// <summary>The axial resistance in compression, kN, negative.</summary>
        public double Nc => CompressionPole.N;

        /// <summary>The levels of N the surface is drawn at, from tension to compression.</summary>
        public double[] Levels { get; }

        /// <summary>The curvature directions every level is drawn at, ascending from 0.</summary>
        public double[] Thetas { get; }

        /// <summary>The surface: Grid[level][theta].</summary>
        public DomainPoint[][] Grid { get; }

        /// <summary>The extent of the domain, for scaling plots: the largest |My| and |Mz| of the surface.</summary>
        public double MaxMy { get; }
        public double MaxMz { get; }
        public double MinMy { get; }
        public double MinMz { get; }

        /// <summary>Fibres, about the centroid.</summary>
        private readonly double[] _y, _z, _area;
        private readonly int[] _material;
        private readonly StressStrainCurve[] _curves;
        private readonly DomainMaterial[] _materials;

        /// <summary>Every check point's hull, held to the traced range.</summary>
        private readonly double[] _allY, _allZ;

        /// <summary>The section's size, so that φ spreads evenly over planes of any section.</summary>
        private readonly double _h;

        /// <summary>
        /// The materials of <paramref name="layout"/>, once each, with the limits <paramref name="settings"/>
        /// and their own properties give them. Their curves are not traced yet.
        /// </summary>
        public static List<DomainMaterial> MaterialsOf(SectionLayout layout, DomainSettings settings)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            settings.Validate();

            var byTag = new Dictionary<int, DomainMaterial>();
            var all = layout.Fibres.Select(f => f.Material).Concat(layout.CheckPoints.Select(p => p.Material));

            foreach (var material in all)
            {
                int tag = Template.MaterialCurves.Tag(material);
                if (byTag.ContainsKey(tag))
                    continue;
                byTag[tag] = Limits(material, tag, settings);
            }

            return byTag.Values.ToList();
        }

        private static DomainMaterial Limits(IMaterial material, int tag, DomainSettings settings)
        {
            var result = new DomainMaterial { Material = material, Tag = tag };

            if (material is Concrete01 concrete)
            {
                result.IsConcrete = true;
                result.TensionLimit = double.PositiveInfinity;
                result.CompressionLimit = settings.ConcreteUltimateStrain ?? Math.Abs(concrete.EpsilonCu);

                if (concrete.MinMax != null && concrete.MinMax.MinStrain < 0.0 &&
                    -concrete.MinMax.MinStrain <= result.CompressionLimit)
                {
                    result.CompressionLimit = -concrete.MinMax.MinStrain;
                    result.CompressionFromMinMax = true;
                }

                if (!(result.CompressionLimit > 0.0))
                    throw new InvalidOperationException(
                        $"{result.Name} has no crushing strain to be held to; give it an EpsilonCu or set εcu.");

                // Point C exists only when the peak comes before the crushing strain; a concrete with
                // its peak at or past εcu is held at the edge alone.
                double peak = Math.Abs(concrete.EpsilonCo);
                if (peak > 0.0 && peak < result.CompressionLimit)
                    result.PivotStrain = peak;
            }
            else
            {
                result.IsConcrete = false;
                result.TensionLimit = settings.SteelUltimateStrain;
                result.CompressionLimit = settings.SteelUltimateStrain;

                if (material is Steel01 steel && steel.E0 > 0.0)
                    result.YieldStrain = Math.Abs(steel.Fy) / steel.E0;
                else if (material is ReinforcingSteel bar && bar.Es > 0.0)
                    result.YieldStrain = Math.Abs(bar.Fy) / bar.Es;

                var wrapper = MinMaxOf(material);
                if (wrapper != null)
                {
                    if (wrapper.MaxStrain > 0.0 && wrapper.MaxStrain <= result.TensionLimit)
                    {
                        result.TensionLimit = wrapper.MaxStrain;
                        result.TensionFromMinMax = true;
                    }
                    if (wrapper.MinStrain < 0.0 && -wrapper.MinStrain <= result.CompressionLimit)
                    {
                        result.CompressionLimit = -wrapper.MinStrain;
                        result.CompressionFromMinMax = true;
                    }
                }
            }

            return result;
        }

        private static MinMax MinMaxOf(IMaterial material)
        {
            switch (material)
            {
                case Steel01 steel: return steel.MinMax;
                case ReinforcingSteel bar: return bar.MinMax;
                default: return null;
            }
        }

        /// <summary>The strain the curves have to reach either way: a little past every finite limit.</summary>
        public static double SampleRange(IEnumerable<DomainMaterial> materials)
        {
            double largest = materials
                .SelectMany(m => new[] { m.CompressionLimit, m.TensionLimit })
                .Where(x => !double.IsInfinity(x))
                .DefaultIfEmpty(0.01)
                .Max();
            return 1.05 * largest;
        }

        /// <summary>
        /// The domain of <paramref name="layout"/>, start to end: the limits worked out, the curves
        /// traced by OpenSees in <paramref name="folder"/>, and the surface swept.
        /// </summary>
        public static InteractionDomain Analyse(SectionLayout layout, DomainSettings settings, string folder, string stem,
                                                Func<string, (string output, string error, int exitCode)> run,
                                                out string deck, out string log)
        {
            var materials = MaterialsOf(layout, settings);
            double range = SampleRange(materials);

            var curves = Template.MaterialCurves.Sample(materials.Select(m => m.Material).ToList(), range,
                                                        settings.CurveSteps, folder, stem, run, out deck, out log);

            return new InteractionDomain(layout, settings, materials, curves, range);
        }

        /// <param name="materials">From <see cref="MaterialsOf"/>.</param>
        /// <param name="curves">Each material's curve, by its tag, traced to <paramref name="maxStrain"/>.</param>
        public InteractionDomain(SectionLayout layout, DomainSettings settings, IList<DomainMaterial> materials,
                                 IDictionary<int, StressStrainCurve> curves, double maxStrain)
        {
            settings.Validate();
            Settings = settings;
            MaxStrain = maxStrain;

            if (layout.Fibres.Count == 0)
                throw new ArgumentException("The section has no fibres.");

            double area = layout.Fibres.Sum(f => f.Area);
            if (!(area > 0.0))
                throw new ArgumentException("The section's fibres have no area.");

            CentroidY = layout.Fibres.Sum(f => f.Area * f.Y) / area;
            CentroidZ = layout.Fibres.Sum(f => f.Area * f.Z) / area;

            _materials = materials.ToArray();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < _materials.Length; i++)
            {
                var material = _materials[i];
                if (!curves.TryGetValue(material.Tag, out var curve))
                    throw new ArgumentException($"There is no stress-strain curve for {material.Name}.");
                material.Curve = curve;
                index[material.Tag] = i;

                // MinMax fails a material right at its bound, and a curve read between the last
                // sample inside it and the first outside drops towards zero. Two steps short of it,
                // the limit stays on what the material really carries.
                double step = maxStrain / curve.Steps;
                if (material.CompressionFromMinMax)
                    material.CompressionLimit = Math.Max(material.CompressionLimit - 2.0 * step, 0.5 * material.CompressionLimit);
                if (material.TensionFromMinMax)
                    material.TensionLimit = Math.Max(material.TensionLimit - 2.0 * step, 0.5 * material.TensionLimit);
            }
            Materials = _materials;
            _curves = _materials.Select(m => m.Curve).ToArray();

            int n = layout.Fibres.Count;
            _y = new double[n];
            _z = new double[n];
            _area = new double[n];
            _material = new int[n];
            for (int i = 0; i < n; i++)
            {
                var fibre = layout.Fibres[i];
                _y[i] = fibre.Y - CentroidY;
                _z[i] = fibre.Z - CentroidZ;
                _area[i] = fibre.Area;
                _material[i] = index[Template.MaterialCurves.Tag(fibre.Material)];
            }

            // A material with no check point of its own - fibres added without one - is held where
            // its fibres are.
            var points = layout.CheckPoints.Select(p => (p.Y, p.Z, p.Material)).ToList();
            var checkedTags = new HashSet<int>(layout.CheckPoints.Select(p => Template.MaterialCurves.Tag(p.Material)));
            foreach (var group in layout.Fibres.GroupBy(f => Template.MaterialCurves.Tag(f.Material)))
                if (!checkedTags.Contains(group.Key))
                    points.AddRange(group.Select(f => (f.Y, f.Z, f.Material)));

            foreach (var group in points.GroupBy(p => Template.MaterialCurves.Tag(p.Material)))
            {
                var material = _materials[index[group.Key]];
                Hull(group.Select(p => (p.Y - CentroidY, p.Z - CentroidZ)), out var hy, out var hz);
                material.HullY = hy;
                material.HullZ = hz;
            }

            Hull(points.Select(p => (p.Y - CentroidY, p.Z - CentroidZ)), out _allY, out _allZ);

            double spanY = _allY.Length > 0 ? _allY.Max() - _allY.Min() : 0.0;
            double spanZ = _allZ.Length > 0 ? _allZ.Max() - _allZ.Min() : 0.0;
            _h = Math.Max(spanY, spanZ);
            if (!(_h > 0.0))
                _h = 1.0;

            TensionPole = Point(0.0, 0.0);
            CompressionPole = Point(Math.PI, 0.0);

            double span = Nt - Nc;
            if (!(span > 1e-9 * (Math.Abs(Nt) + Math.Abs(Nc) + 1.0)))
                throw new InvalidOperationException("The section carries no axial force, so it has no domain to draw.");

            // Closer together towards the poles, where the surface turns fastest.
            Levels = new double[settings.Levels];
            for (int i = 0; i < settings.Levels; i++)
            {
                double t = 0.5 * (1.0 - Math.Cos(Math.PI * (i + 1) / (settings.Levels + 1)));
                Levels[i] = Nt - t * span;
            }

            var levels = Levels;
            var thetas = Uniform(settings.PointsPerLevel);
            var columns = Columns(thetas, theta => levels.Select(level => AtLevel(level, theta)).ToArray());

            // A first, even sweep to measure the domain by, so refinement knows what "far apart" is.
            var all = columns.SelectMany(c => c).Concat(new[] { TensionPole, CompressionPole }).Where(p => p.IsValid).ToList();
            MaxMy = all.Max(p => p.My);
            MinMy = all.Min(p => p.My);
            MaxMz = all.Max(p => p.Mz);
            MinMz = all.Min(p => p.Mz);

            Refine(thetas, columns, theta => levels.Select(level => AtLevel(level, theta)).ToArray(),
                   3 * settings.PointsPerLevel);

            Thetas = thetas.ToArray();
            Grid = new DomainPoint[Levels.Length][];
            for (int l = 0; l < Levels.Length; l++)
            {
                Grid[l] = new DomainPoint[Thetas.Length];
                for (int j = 0; j < Thetas.Length; j++)
                    Grid[l][j] = columns[j][l];
            }
        }

        // ---------------------------------------------------------------- strain planes

        /// <summary>
        /// How far the plane (e0, ky, kz) has taken the section towards its ultimate strains: the
        /// largest strain over its limit of every material, so 1 where the first fibre reaches one.
        /// Zero for the unstrained section.
        /// </summary>
        public double UltimateRatio(double e0, double ky, double kz) => Ratio(e0, ky, kz, out _);

        /// <summary>What reaches its limit first under the plane (e0, ky, kz), in words.</summary>
        public string Governs(double e0, double ky, double kz)
        {
            Ratio(e0, ky, kz, out var governing);
            if (governing.Material == null)
                return "Nothing is strained.";

            string at = governing.Point >= 0
                ? $" at y = {governing.Material.HullY[governing.Point] + CentroidY:0.###}, z = {governing.Material.HullZ[governing.Point] + CentroidZ:0.###}"
                : "";
            switch (governing.Kind)
            {
                case LimitKind.Pivot:
                    return $"{governing.Material.Name} reaches εc2 = {governing.Material.PivotStrain:0.#####} at point C, all in compression.";
                case LimitKind.Compression:
                    return governing.Material.IsConcrete
                        ? $"{governing.Material.Name} crushes at εcu = {governing.Material.CompressionLimit:0.#####}{at}."
                        : $"{governing.Material.Name} reaches -{governing.Material.CompressionLimit:0.#####} in compression{at}.";
                default:
                    return $"{governing.Material.Name} reaches +{governing.Material.TensionLimit:0.#####} in tension{at}.";
            }
        }

        private enum LimitKind { Compression, Tension, Pivot }

        private struct Limit
        {
            public DomainMaterial Material;
            public LimitKind Kind;
            public int Point;
        }

        private double Ratio(double e0, double ky, double kz, out Limit governing)
        {
            double ratio = 0.0;
            var best = new Limit { Point = -1 };

            void Take(double r, DomainMaterial material, LimitKind kind, int point)
            {
                if (r > ratio)
                {
                    ratio = r;
                    best = new Limit { Material = material, Kind = kind, Point = point };
                }
            }

            foreach (var material in _materials)
            {
                var hy = material.HullY;
                var hz = material.HullZ;
                if (hy.Length == 0)
                    continue;

                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                int lowAt = -1, highAt = -1;
                for (int i = 0; i < hy.Length; i++)
                {
                    double strain = e0 - hy[i] * kz + hz[i] * ky;
                    if (strain < low) { low = strain; lowAt = i; }
                    if (strain > high) { high = strain; highAt = i; }
                }

                if (low < 0.0 && !double.IsInfinity(material.CompressionLimit))
                    Take(-low / material.CompressionLimit, material, LimitKind.Compression, lowAt);
                if (high > 0.0 && !double.IsInfinity(material.TensionLimit))
                    Take(high / material.TensionLimit, material, LimitKind.Tension, highAt);

                // Eurocode 2 6.1(5), Figure 6.1: with the concrete all in compression the strain
                // pivots about C, (1 - εc2/εcu2) of the depth from the most compressed edge, where it
                // is held to εc2. Held always, it binds only then: with the far edge in tension and
                // the near one at εcu, the strain at C is above -εc2.
                if (material.IsConcrete && !double.IsNaN(material.PivotStrain))
                {
                    double atC = low + (high - low) * (1.0 - material.PivotStrain / material.CompressionLimit);
                    if (atC < 0.0)
                        Take(-atC / material.PivotStrain, material, LimitKind.Pivot, -1);
                }
            }

            governing = best;
            return ratio;
        }

        /// <summary>
        /// The factor that takes the plane (e0, ky, kz) to its first limit: the most any fibre's
        /// strain can grow by before one reaches its material's ultimate strain.
        /// </summary>
        public double LimitFactor(double e0, double ky, double kz)
        {
            double ratio = UltimateRatio(e0, ky, kz);
            if (ratio > 0.0)
                return 1.0 / ratio;

            // No limit at all binds a plane that only stretches concrete; it stops where the curves do.
            // Only then: the concrete's tension edge lies past the bars, and capped at the curves'
            // end along with the rest it stopped a plane the bars govern before they reached εud.
            double largest = 0.0;
            for (int i = 0; i < _allY.Length; i++)
                largest = Math.Max(largest, Math.Abs(e0 - _allY[i] * kz + _allZ[i] * ky));
            return largest > 0.0 ? MaxStrain / largest : double.PositiveInfinity;
        }

        /// <summary>
        /// How far the plane (e0, ky, kz) has taken the section towards first yield: the largest
        /// strain over its yield strain of every material that has one, so 1 where the first such
        /// fibre yields. NaN when no material of the section yields.
        /// </summary>
        public double YieldRatio(double e0, double ky, double kz)
        {
            double ratio = double.NaN;
            foreach (var material in _materials)
            {
                if (double.IsNaN(material.YieldStrain) || !(material.YieldStrain > 0.0) || material.HullY.Length == 0)
                    continue;

                double largest = 0.0;
                for (int i = 0; i < material.HullY.Length; i++)
                    largest = Math.Max(largest, Math.Abs(e0 - material.HullY[i] * kz + material.HullZ[i] * ky));

                double r = largest / material.YieldStrain;
                ratio = double.IsNaN(ratio) ? r : Math.Max(ratio, r);
            }
            return ratio;
        }

        /// <summary>
        /// <see cref="UltimateRatio"/> as a Tcl proc, for a deck to stop itself at the ultimate state:
        /// <c>name e0 kz ky</c>, the order "section deformation" reports them in, returns the ratio.
        /// </summary>
        public string UltimateRatioTcl(string name)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"proc {name} {{e0 kz ky args}} {{\n");
            sb.Append("\tset ratio 0.0\n");
            foreach (var material in _materials)
            {
                if (material.HullY.Length == 0)
                    continue;

                // ASCII only: the interpreter reads the deck in the system's encoding.
                sb.Append($"\t# material {material.Tag}\n");
                sb.Append("\tset low 1e30\n\tset high -1e30\n");
                sb.Append("\tforeach {y z} {");
                for (int i = 0; i < material.HullY.Length; i++)
                    sb.Append(i == 0 ? "" : " ").Append(TclNumber.Write(material.HullY[i])).Append(' ').Append(TclNumber.Write(material.HullZ[i]));
                sb.Append("} {\n");
                sb.Append("\t\tset s [expr {$e0 - $y*$kz + $z*$ky}]\n");
                sb.Append("\t\tif {$s < $low} {set low $s}\n");
                sb.Append("\t\tif {$s > $high} {set high $s}\n");
                sb.Append("\t}\n");
                if (!double.IsInfinity(material.CompressionLimit))
                    sb.Append($"\tif {{$low < 0.0}} {{set ratio [expr {{max($ratio, -$low / {TclNumber.Write(material.CompressionLimit)})}}]}}\n");
                if (!double.IsInfinity(material.TensionLimit))
                    sb.Append($"\tif {{$high > 0.0}} {{set ratio [expr {{max($ratio, $high / {TclNumber.Write(material.TensionLimit)})}}]}}\n");
                if (material.IsConcrete && !double.IsNaN(material.PivotStrain))
                {
                    double share = 1.0 - material.PivotStrain / material.CompressionLimit;
                    sb.Append($"\tset c [expr {{$low + ($high - $low) * {TclNumber.Write(share)}}}]\n");
                    sb.Append($"\tif {{$c < 0.0}} {{set ratio [expr {{max($ratio, -$c / {TclNumber.Write(material.PivotStrain)})}}]}}\n");
                }
            }
            sb.Append("\treturn $ratio\n}\n");
            return sb.ToString();
        }

        /// <summary>The section's forces under the strain plane (e0, ky, kz), as OpenSees' fibre section gives them.</summary>
        public DomainPoint Forces(double e0, double ky, double kz)
        {
            double n = 0.0, my = 0.0, mz = 0.0;
            for (int i = 0; i < _y.Length; i++)
            {
                double strain = e0 - _y[i] * kz + _z[i] * ky;
                double force = _curves[_material[i]].Stress(strain) * _area[i];
                n += force;
                my += force * _z[i];
                mz -= force * _y[i];
            }

            return new DomainPoint { N = n, My = my, Mz = mz, Axial = e0, CurvatureY = ky, CurvatureZ = kz };
        }

        /// <summary>The ultimate plane in the direction (φ, θ) on the sphere of planes.</summary>
        public void Ultimate(double phi, double theta, out double e0, out double ky, out double kz)
        {
            double a = Math.Cos(phi);
            double k = Math.Sin(phi) / _h;
            double y = k * Math.Cos(theta);
            double z = k * Math.Sin(theta);

            double factor = LimitFactor(a, y, z);
            e0 = a * factor;
            ky = y * factor;
            kz = z * factor;
        }

        /// <summary>The point of the domain in the direction (φ, θ).</summary>
        public DomainPoint Point(double phi, double theta)
        {
            Ultimate(phi, theta, out double e0, out double ky, out double kz);
            return Forces(e0, ky, kz);
        }

        /// <summary>
        /// The point at axial force <paramref name="n"/> whose plane curves in the direction θ; invalid
        /// when n is not strictly between the poles.
        /// </summary>
        public DomainPoint AtLevel(double n, double theta)
        {
            if (!(n < Nt && n > Nc))
                return DomainPoint.Invalid;

            // N falls from Nt at φ = 0 to Nc at φ = π.
            double phi = Root(p => Point(p, theta).N - n, 0.0, Math.PI, Nt - n, Nc - n, 1e-11);
            return Point(phi, theta);
        }

        // ---------------------------------------------------------------- levels and cuts

        /// <summary>
        /// The level of the domain at axial force <paramref name="n"/>, as a closed loop of points in
        /// θ order, refined where they fall far apart. Empty when n is not strictly between the poles.
        /// </summary>
        public List<DomainPoint> Contour(double n, int count = 0)
        {
            if (!(n < Nt && n > Nc))
                return new List<DomainPoint>();

            if (count < 8)
                count = Settings.PointsPerLevel;

            var thetas = Uniform(count);
            var columns = Columns(thetas, theta => new[] { AtLevel(n, theta) });
            Refine(thetas, columns, theta => new[] { AtLevel(n, theta) }, 3 * count);
            return columns.Select(c => c[0]).ToList();
        }

        /// <summary>
        /// Where the level at <paramref name="n"/> meets the line through the N axis in the moment
        /// direction β - My = t cos β, Mz = t sin β - nearest and furthest along it. False when the
        /// line misses the level, or n is not strictly between the poles.
        /// </summary>
        public bool LineHits(double n, double beta, out DomainPoint near, out DomainPoint far)
        {
            near = far = DomainPoint.Invalid;
            if (!(n < Nt && n > Nc))
                return false;

            double ux = Math.Cos(beta), uy = Math.Sin(beta);
            double Cross(DomainPoint p) => ux * p.Mz - uy * p.My;
            double Along(DomainPoint p) => ux * p.My + uy * p.Mz;

            // A convex level meets a line at most twice; a coarse loop finds the sides it changes on.
            const int coarse = 24;
            var thetas = Uniform(coarse);
            var points = thetas.Select(theta => AtLevel(n, theta)).ToArray();
            if (points.Any(p => !p.IsValid))
                return false;

            var hits = new List<DomainPoint>();
            for (int j = 0; j < coarse; j++)
            {
                var a = points[j];
                var b = points[(j + 1) % coarse];
                double ca = Cross(a), cb = Cross(b);

                if (ca == 0.0)
                {
                    hits.Add(a);
                    continue;
                }
                if (cb == 0.0 || (ca > 0.0) == (cb > 0.0))
                    continue;

                double t0 = thetas[j];
                double t1 = j + 1 < coarse ? thetas[j + 1] : 2.0 * Math.PI;
                double theta = Root(t => Cross(AtLevel(n, t)), t0, t1, ca, cb, 1e-10);
                hits.Add(AtLevel(n, theta));
            }

            if (hits.Count == 0)
                return false;

            near = hits.OrderBy(Along).First();
            far = hits.OrderBy(Along).Last();
            return true;
        }

        /// <summary>
        /// The domain cut by the plane holding the N axis and the moment direction α: a closed loop,
        /// down the far side from tension to compression and back up the near side. Each point is a
        /// point of the domain; its moment along α, My cos α + Mz sin α, is where it plots.
        /// </summary>
        public List<DomainPoint> Cut(double alpha, int count = 60)
        {
            count = Math.Max(count, 4);
            double span = Nt - Nc;
            var list = new List<double>();
            for (int i = 0; i < count; i++)
                list.Add(Nt - span * 0.5 * (1.0 - Math.Cos(Math.PI * (i + 1) / (count + 1))));

            // Pure bending, exactly: the widest a steel section's cut gets, and where the levels
            // either side of it would cut the corner by a fraction of a percent.
            if (Nt > 0.0 && Nc < 0.0)
                list.Add(0.0);
            var levels = list.OrderByDescending(x => x).ToArray();
            count = levels.Length;

            var near = new DomainPoint[count];
            var far = new DomainPoint[count];
            var hit = new bool[count];
            Parallel.For(0, count, i =>
            {
                hit[i] = LineHits(levels[i], alpha, out near[i], out far[i]);
            });

            double ux = Math.Cos(alpha), uy = Math.Sin(alpha);
            double scale = Math.Max(Math.Max(Math.Abs(MaxMy), Math.Abs(MinMy)), Math.Max(Math.Abs(MaxMz), Math.Abs(MinMz)));
            bool OnPlane(DomainPoint p) => Math.Abs(ux * p.Mz - uy * p.My) <= 1e-6 * (scale + 1e-12);

            var loop = new List<DomainPoint>();
            if (OnPlane(TensionPole))
                loop.Add(TensionPole);
            for (int i = 0; i < count; i++)
                if (hit[i])
                    loop.Add(far[i]);
            if (OnPlane(CompressionPole))
                loop.Add(CompressionPole);
            for (int i = count - 1; i >= 0; i--)
                if (hit[i])
                    loop.Add(near[i]);

            return loop;
        }

        // ---------------------------------------------------------------- checks

        /// <summary>
        /// The safety factor of the design forces (N, My, Mz): how far they can grow before they reach
        /// the boundary, with N held (<see cref="DomainCheckMethod.ConstantAxial"/>) or with all three
        /// in proportion (<see cref="DomainCheckMethod.Proportional"/>).
        /// </summary>
        public DomainCheck Check(double n, double my, double mz, DomainCheckMethod method)
        {
            var check = new DomainCheck { N = n, My = my, Mz = mz, Method = method };
            double moment = Math.Sqrt(my * my + mz * mz);
            double scale = Math.Max(Math.Max(Math.Abs(MaxMy), Math.Abs(MinMy)), Math.Max(Math.Abs(MaxMz), Math.Abs(MinMz)));

            if (method == DomainCheckMethod.ConstantAxial && moment <= 1e-12 * (scale + 1.0))
            {
                Proportional(check, n, my, mz, moment, scale);
                check.Method = DomainCheckMethod.Proportional;
                check.Note = Join("There is no moment to scale with N held, so the factor is on N and the moments together.", check.Note);
                return check;
            }

            if (method == DomainCheckMethod.ConstantAxial)
                ConstantAxial(check, n, my, mz, moment);
            else
                Proportional(check, n, my, mz, moment, scale);

            return check;
        }

        private void ConstantAxial(DomainCheck check, double n, double my, double mz, double moment)
        {
            if (!(n < Nt && n > Nc))
            {
                check.Fail($"N = {n:0.##} kN is beyond the axial resistance, {Nc:0.##} to {Nt:0.##} kN.");
                return;
            }

            double beta = Math.Atan2(mz, my);
            double ux = Math.Cos(beta), uy = Math.Sin(beta);
            if (!LineHits(n, beta, out var near, out var far))
            {
                check.Fail($"With N = {n:0.##} kN the section carries no moment in this direction.");
                return;
            }

            double tNear = ux * near.My + uy * near.Mz;
            double tFar = ux * far.My + uy * far.Mz;
            if (tFar <= 0.0)
            {
                check.Fail($"With N = {n:0.##} kN the section carries no moment in this direction.");
                return;
            }
            if (tNear > moment)
            {
                // The level does not hold zero moment: with this N the section needs at least
                // tNear of moment in this direction, and the design moment is less.
                check.Fail($"With N = {n:0.##} kN the section needs at least {tNear:0.##} kN m in this direction; " +
                           $"the design moment is {moment:0.##} kN m.");
                return;
            }

            check.SafetyFactor = tFar / moment;
            check.SetCapacity(far);
        }

        private void Proportional(DomainCheck check, double n, double my, double mz, double moment, double scale)
        {
            double size = Math.Sqrt(n * n / ((Nt - Nc) * (Nt - Nc)) + moment * moment / ((scale + 1e-12) * (scale + 1e-12)));
            if (size <= 1e-12)
            {
                check.SafetyFactor = double.PositiveInfinity;
                check.Note = "Nothing loads the section.";
                return;
            }

            double beta = moment > 0.0 ? Math.Atan2(mz, my) : 0.0;
            double ux = Math.Cos(beta), uy = Math.Sin(beta);

            bool Inside(double factor, out DomainPoint far)
            {
                far = DomainPoint.Invalid;
                double level = factor * n;
                double m = factor * moment;
                if (!(level < Nt && level > Nc))
                    return false;
                if (!LineHits(level, beta, out var near, out far))
                    return false;
                double tNear = ux * near.My + uy * near.Mz;
                double tFar = ux * far.My + uy * far.Mz;
                return tNear <= m && m <= tFar;
            }

            if (!Inside(0.0, out _))
            {
                check.Fail("The unloaded section lies on or outside its own domain, so there is no factor to scale the forces by - " +
                           "a section with no tensile resistance, for one. Hold N instead.");
                return;
            }

            // Out along the ray until it leaves the domain: past a pole at once when there is N.
            double low = 0.0, high;
            if (n > 0.0)
                high = Nt / n;
            else if (n < 0.0)
                high = Nc / n;
            else
            {
                high = 1.0;
                for (int i = 0; i < 80 && Inside(high, out _); i++)
                    high *= 2.0;
            }

            while (high - low > 1e-7 * high)
            {
                double mid = 0.5 * (low + high);
                if (Inside(mid, out _))
                    low = mid;
                else
                    high = mid;
            }

            check.SafetyFactor = low;
            check.CapacityN = low * n;
            check.CapacityMy = low * my;
            check.CapacityMz = low * mz;
        }

        /// <summary>What a Panel shows of the domain.</summary>
        public override string ToString() =>
            $"Interaction domain: N {Nc:0.#} to {Nt:0.#} kN, My {MinMy:0.#} to {MaxMy:0.#} kN m, Mz {MinMz:0.#} to {MaxMz:0.#} kN m " +
            $"({Levels.Length} levels x {Thetas.Length} points)";

        private static string Join(string a, string b) =>
            string.IsNullOrEmpty(b) ? a : string.IsNullOrEmpty(a) ? b : a + " " + b;

        // ---------------------------------------------------------------- helpers

        private static List<double> Uniform(int count) =>
            Enumerable.Range(0, count).Select(j => 2.0 * Math.PI * j / count).ToList();

        private static List<DomainPoint[]> Columns(List<double> thetas, Func<double, DomainPoint[]> column)
        {
            var columns = new DomainPoint[thetas.Count][];
            Parallel.For(0, thetas.Count, j => columns[j] = column(thetas[j]));
            return columns.ToList();
        }

        /// <summary>
        /// More θ where neighbouring points fall far apart. Evenly spaced curvature directions bunch
        /// up at the domain's corners - a plastic steel section's level has some - and leave its
        /// flatter faces with few points, so a chord well over the average gets a point in its middle,
        /// a few times over, up to <paramref name="cap"/> in all. One set of θ serves every level, so
        /// the surface stays a grid.
        /// </summary>
        private void Refine(List<double> thetas, List<DomainPoint[]> columns, Func<double, DomainPoint[]> column, int cap)
        {
            double sy = Math.Max(MaxMy - MinMy, 1e-12);
            double sz = Math.Max(MaxMz - MinMz, 1e-12);
            if (sy < 1e-6 * sz) sy = sz;
            if (sz < 1e-6 * sy) sz = sy;

            for (int pass = 0; pass < 4 && thetas.Count < cap; pass++)
            {
                int count = thetas.Count;
                var chords = new double[count];
                for (int j = 0; j < count; j++)
                {
                    var a = columns[j];
                    var b = columns[(j + 1) % count];
                    double chord = 0.0;
                    for (int l = 0; l < a.Length; l++)
                    {
                        if (!a[l].IsValid || !b[l].IsValid)
                            continue;
                        double dy = (a[l].My - b[l].My) / sy, dz = (a[l].Mz - b[l].Mz) / sz;
                        chord = Math.Max(chord, Math.Sqrt(dy * dy + dz * dz));
                    }
                    chords[j] = chord;
                }

                double mean = chords.Average();
                var split = Enumerable.Range(0, count)
                    .Where(j => chords[j] > 1.5 * mean)
                    .OrderByDescending(j => chords[j])
                    .Take(cap - count)
                    .OrderByDescending(j => j)
                    .ToList();
                if (split.Count == 0)
                    break;

                var middles = split.Select(j => 0.5 * (thetas[j] + (j + 1 < count ? thetas[j + 1] : 2.0 * Math.PI))).ToList();
                var added = Columns(middles, column);

                // From the back, so the indices still to come are unchanged.
                for (int k = 0; k < split.Count; k++)
                {
                    thetas.Insert(split[k] + 1, middles[k]);
                    columns.Insert(split[k] + 1, added[k]);
                }
            }
        }

        /// <summary>Andrew's monotone chain: the extremes of a linear strain are on the hull.</summary>
        private static void Hull(IEnumerable<(double y, double z)> source, out double[] hy, out double[] hz)
        {
            var points = source.Distinct().OrderBy(p => p.y).ThenBy(p => p.z).ToList();
            if (points.Count < 3)
            {
                hy = points.Select(p => p.y).ToArray();
                hz = points.Select(p => p.z).ToArray();
                return;
            }

            double Turn((double y, double z) o, (double y, double z) a, (double y, double z) b) =>
                (a.y - o.y) * (b.z - o.z) - (a.z - o.z) * (b.y - o.y);

            var hull = new List<(double y, double z)>();
            foreach (var p in points)
            {
                while (hull.Count >= 2 && Turn(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var p = points[i];
                while (hull.Count >= lower && Turn(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);

            hy = hull.Select(p => p.y).ToArray();
            hz = hull.Select(p => p.z).ToArray();
        }

        /// <summary>
        /// Brent's method for f(x) = 0 between a and b, where f has opposite signs (Numerical Recipes'
        /// zbrent). N along φ is continuous but only piecewise smooth - a plastic section's has kinks -
        /// which bisection's fallback inside it handles.
        /// </summary>
        private static double Root(Func<double, double> f, double a, double b, double fa, double fb, double tolerance)
        {
            if (fa == 0.0) return a;
            if (fb == 0.0) return b;

            double c = b, fc = fb, d = b - a, e = d;
            for (int iteration = 0; iteration < 200; iteration++)
            {
                if ((fb > 0.0 && fc > 0.0) || (fb < 0.0 && fc < 0.0))
                {
                    c = a; fc = fa; d = b - a; e = d;
                }
                if (Math.Abs(fc) < Math.Abs(fb))
                {
                    a = b; b = c; c = a;
                    fa = fb; fb = fc; fc = fa;
                }

                double tol = 2.0 * 1e-16 * Math.Abs(b) + 0.5 * tolerance;
                double xm = 0.5 * (c - b);
                if (Math.Abs(xm) <= tol || fb == 0.0)
                    return b;

                if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
                {
                    double s = fb / fa, p, q;
                    if (a == c)
                    {
                        p = 2.0 * xm * s;
                        q = 1.0 - s;
                    }
                    else
                    {
                        q = fa / fc;
                        double r = fb / fc;
                        p = s * (2.0 * xm * q * (q - r) - (b - a) * (r - 1.0));
                        q = (q - 1.0) * (r - 1.0) * (s - 1.0);
                    }
                    if (p > 0.0) q = -q;
                    p = Math.Abs(p);

                    double min1 = 3.0 * xm * q - Math.Abs(tol * q);
                    double min2 = Math.Abs(e * q);
                    if (2.0 * p < Math.Min(min1, min2))
                    {
                        e = d;
                        d = p / q;
                    }
                    else
                    {
                        d = xm;
                        e = d;
                    }
                }
                else
                {
                    d = xm;
                    e = d;
                }

                a = b;
                fa = fb;
                b += Math.Abs(d) > tol ? d : (xm >= 0.0 ? tol : -tol);
                fb = f(b);
            }

            return b;
        }
    }

    public enum DomainCheckMethod
    {
        /// <summary>N held at its design value, the moments scaled: how RC-SEC and most column checks read it.</summary>
        ConstantAxial,
        /// <summary>N and both moments scaled together, along the ray from the origin.</summary>
        Proportional,
    }

    /// <summary>One set of design forces checked against a domain.</summary>
    public sealed class DomainCheck
    {
        /// <summary>The design forces: kN and kN m, tension positive, about the centroid.</summary>
        public double N { get; internal set; }
        public double My { get; internal set; }
        public double Mz { get; internal set; }

        /// <summary>Where the forces meet the boundary, scaled as <see cref="Method"/> says; NaN when they never do.</summary>
        public double CapacityN { get; internal set; } = double.NaN;
        public double CapacityMy { get; internal set; } = double.NaN;
        public double CapacityMz { get; internal set; } = double.NaN;

        /// <summary>
        /// How many times the forces fit in the domain: at least 1 when the section carries them.
        /// Zero when nothing scaled this way reaches the domain at all, which <see cref="Note"/> says
        /// why; infinity when nothing loads the section.
        /// </summary>
        public double SafetyFactor { get; internal set; }

        /// <summary>1 / <see cref="SafetyFactor"/>: at most 1 when the section carries the forces.</summary>
        public double Utilisation => SafetyFactor > 0.0 ? 1.0 / SafetyFactor : double.PositiveInfinity;

        public bool IsSafe => SafetyFactor >= 1.0;

        public DomainCheckMethod Method { get; internal set; }

        /// <summary>Why the factor is what it is, when that needs saying; null otherwise.</summary>
        public string Note { get; internal set; }

        internal void Fail(string note)
        {
            SafetyFactor = 0.0;
            Note = note;
        }

        internal void SetCapacity(DomainPoint point)
        {
            CapacityN = point.N;
            CapacityMy = point.My;
            CapacityMz = point.Mz;
        }

        public override string ToString() =>
            $"N {N:0.##} kN, My {My:0.##} kN m, Mz {Mz:0.##} kN m: safety factor {SafetyFactor:0.###}" +
            (Note != null ? " - " + Note : "");
    }
}
