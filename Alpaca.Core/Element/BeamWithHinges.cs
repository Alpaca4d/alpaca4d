using System;
using System.Collections.Generic;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.BeamIntegration;
using Alpaca4d.Section;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Element
{
    public partial class BeamWithHinges : EntityBase, IStructure, IBeam, ISerialize
    {
        public Curve Curve { get; set; }
        public IUniaxialSection Section { get; set; }
        public GeomTransf GeomTransf { get; set; } = new GeomTransf();
        public IIntegration BeamIntegration { get; set; }
        public Vector3d LocalZAxis
        {
            get { return this.GeomTransf.LocalZ; }
        }
        public ElementType Type => ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public int Ndf => 6;
        public double? MassDens => this.Section.Area * this.Section.Material.Rho;
        public System.Drawing.Color Color { get; set; } = Alpaca4d.Colors.DefaultBeamWithHinges;

        private HingeRadauIntegration HingeIntegration => (HingeRadauIntegration)this.BeamIntegration;

        /// <summary>Hinge length ratio lp/L used when the caller does not supply one.</summary>
        public const double DefaultLpRatio = 0.05;

        /// <summary>Lower bound on lp/L. Below this the 1e-6 hinge softening stops reading as a release.</summary>
        public const double MinLpRatio = 0.02;

        /// <summary>
        /// Upper bound on lp/L. HingeRadau gives the two interior points a weight of
        /// 0.5 - 2*(lpI+lpJ)/L (HingeRadauBeamIntegration::getSectionWeights), so lpI+lpJ
        /// must stay below L/4; capping each end at 0.10*L keeps that weight at 0.30 or more.
        /// </summary>
        public const double MaxLpRatio = 0.10;

        /// <summary>Node-to-node distance, which is the length L that OpenSees integrates over.</summary>
        public static double ChordLength(Curve curve) => curve.PointAtStart.DistanceTo(curve.PointAtEnd);

        /// <summary>Hinge length at the I end as a fraction of L.</summary>
        public double LpRatioI => LpRatioOf(this.HingeIntegration.LpI);

        /// <summary>Hinge length at the J end as a fraction of L.</summary>
        public double LpRatioJ => LpRatioOf(this.HingeIntegration.LpJ);

        private double LpRatioOf(double lp)
        {
            double length = ChordLength(this.Curve);
            return length > 0.0 ? lp / length : 0.0;
        }

        /// <summary>
        /// Turns a requested lp/L into one that is safe for HingeRadau: no value at all, or a
        /// non-positive one, falls back to <see cref="DefaultLpRatio"/>; anything else is clamped
        /// to [<see cref="MinLpRatio"/>, <see cref="MaxLpRatio"/>]. The hinge is modelled by scaling
        /// the section stiffness by 1e-6, so the released flexibility follows lp/L - which is why
        /// the ratio, and not a length, is what a caller sets: it carries over unchanged between a
        /// model drawn in metres and the same model in millimetres.
        /// </summary>
        public static double ResolveLpRatio(double? ratio)
        {
            if (!ratio.HasValue || ratio.Value <= 0.0) return DefaultLpRatio;
            return Math.Min(Math.Max(ratio.Value, MinLpRatio), MaxLpRatio);
        }

        /// <summary>The hinge length HingeRadau integrates over, for a requested lp/L.</summary>
        public static double ResolveLp(double? ratio, double length) => ResolveLpRatio(ratio) * length;

        /// <param name="lpRatioI">Hinge length at the I end as a fraction of L; null for the default.</param>
        /// <param name="lpRatioJ">Hinge length at the J end as a fraction of L; null for the default.</param>
        public BeamWithHinges(
            Curve curve,
            IUniaxialSection section,
            GeomTransf geomTransf,
            Release releaseI, double? lpRatioI,
            Release releaseJ, double? lpRatioJ)
        {
            this.Curve = curve;
            this.Section = section;
            this.GeomTransf = geomTransf;

            var sectionI = CreateHingeSection(section, releaseI);
            var sectionJ = CreateHingeSection(section, releaseJ);

            double length = ChordLength(curve);
            this.BeamIntegration = new HingeRadauIntegration(
                sectionI, ResolveLp(lpRatioI, length),
                sectionJ, ResolveLp(lpRatioJ, length),
                section);
        }

        private static ElasticSection CreateHingeSection(IUniaxialSection baseSection, Release release)
        {
            const double factor = 1e-6;

            double area   = release.Tx ? baseSection.Area   : baseSection.Area   * factor;
            double izz    = release.Mz ? baseSection.Izz    : baseSection.Izz    * factor;
            double iyy    = release.My ? baseSection.Iyy    : baseSection.Iyy    * factor;
            double j      = release.Rx ? baseSection.J      : baseSection.J      * factor;
            double alphaY = release.Ty ? baseSection.AlphaY : baseSection.AlphaY * factor;
            double alphaZ = release.Tz ? baseSection.AlphaZ : baseSection.AlphaZ * factor;

            return new ElasticSection(
                baseSection.GetType().Name + "_hinge",
                area, izz, iyy, j, alphaY, alphaZ,
                baseSection.Material);
        }

        public void SetTags()
        {
            this.GeomTransf.Id = this.Id;
            this.BeamIntegration.Id = this.Id;
        }

        public void SetTopologyRTree(Model model)
        {
            var tol = model.Tollerance;
            var pointAtStart = this.Curve.PointAtStart;
            var pointAtEnd = this.Curve.PointAtEnd;
            var curvePoints = new List<Point3d> { pointAtStart, pointAtEnd };

            var closestIndexes = new List<int>();

            void SearchCallback(object sender, RTreeEventArgs e)
            {
                closestIndexes.Add(e.Id + 1);
            }

            foreach (var pt in curvePoints)
            {
                model.RTreeCloudPointSixNDF.Search(new Sphere(pt, tol), SearchCallback);
            }

            this.INode = closestIndexes[0] + model.UniquePointsThreeNDF.Count;
            this.JNode = closestIndexes[1] + model.UniquePointsThreeNDF.Count;
        }

        public override string WriteTcl()
        {
            string geomTransf  = this.GeomTransf.WriteTcl();
            string sectionI    = this.HingeIntegration.SectionI.WriteTcl();
            string sectionJ    = this.HingeIntegration.SectionJ.WriteTcl();
            string integration = this.HingeIntegration.WriteTcl();
            // The legacy inline form takes the spec as separate words: TclForceBeamColumnCommand
            // reads argv[6] as the type and argv[7..11] as secTagI lpI secTagJ lpJ secTagE, so no braces.
            string beam = $"element forceBeamColumn {Id} {INode} {JNode} {GeomTransf.Id} {integration} -mass {Alpaca4d.ModelMass.FromKg(MassDens)}\n";
            return geomTransf + sectionI + sectionJ + beam;
        }
    }
}
