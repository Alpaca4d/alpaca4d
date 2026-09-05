using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Section
{
    /// <summary>
    /// One layer of a <see cref="LayeredShellSection"/>: a material and how thick it is.
    /// </summary>
    public class ShellLayer
    {
        public IMultiDimensionMaterial Material { get; set; }
        public double Thickness { get; set; }

        public ShellLayer(IMultiDimensionMaterial material, double thickness)
        {
            this.Material = material;
            this.Thickness = thickness;
        }

        public override string ToString()
        {
            return $"material {this.Material?.Id} x {this.Thickness}";
        }
    }

    /// <summary>
    /// A shell section built as a stack of layers, each with its own material and thickness -
    /// OpenSees' LayeredShell.
    ///
    /// Where a PlateFiber section is one material sampled at five Lobatto points through a single
    /// thickness, this is an explicit stack: reinforced concrete as cover, steel, core, steel,
    /// cover; a timber panel as crossed plies; a composite as its laminae. It is also what makes a
    /// nonlinear through-thickness answer meaningful, since each layer carries its own material
    /// state.
    ///
    /// Two things follow from how OpenSees builds it, and both show up in the results:
    ///
    ///   - OpenSees requires at least three layers.
    ///   - Its integration points sit at the layer CENTRES, not at the faces. A PlateFiber section
    ///     reports its outermost stresses exactly on the top and bottom faces, because Lobatto
    ///     includes the endpoints; a layered section cannot. Its outermost reported stress is half
    ///     a layer in from the face, so a thick outer layer reads low against a hand check. Thin
    ///     outer layers if the face value is what matters.
    ///
    /// Any nD material may be a layer, orthotropic ones included: OpenSees wraps each through
    /// NDMaterial::getCopy("PlateFiber"), which builds itself out of the material's own 3D form, so
    /// ElasticOrthotropic works without anything special. That is what makes cross-laminated timber
    /// modelable - with one caveat. The section carries NO per-layer orientation, so a crossed ply
    /// is expressed by declaring a second material with Ex and Ey exchanged rather than by rotating
    /// the layer.
    ///
    /// The one thing this section CANNOT do is interlayer slip. Every layer shares a single strain
    /// field, in-plane strain running as eps0 - z*kappa across the whole stack and transverse shear
    /// held constant through it (LayeredShellFiberSection::setTrialSectionDeformation). Plane
    /// sections stay plane over the full depth, so a stiff-soft-stiff laminate held together by a
    /// compliant core - laminated glass on a PVB interlayer, a sandwich panel on a weak adhesive -
    /// comes out very near its monolithic limit rather than somewhere between monolithic and fully
    /// layered. Measured on a cantilever strip of 6mm glass / 1.52mm PVB / 6mm glass: this section
    /// gives a tip deflection 21% above monolithic, where two independently bending panes would be
    /// 5.7 times above it. Stiff by a factor of about five, and unsafe in that direction. Model
    /// those as two shells with an explicit shear connection instead.
    /// </summary>
    public partial class LayeredShellSection : ISerialize, IMultiDimensionSection
    {
        /// <summary>OpenSees refuses a LayeredShell section with fewer than this many layers.</summary>
        public const int MinLayers = 3;

        public string SectionName { get; set; }
        public List<ShellLayer> Layers { get; set; } = new List<ShellLayer>();
        public int? Id { get; set; } = IdGenerator.GenerateId();

        /// <summary>
        /// The first layer's material, which is what the single-material half of
        /// <see cref="IMultiDimensionSection"/> can express. <see cref="Materials"/> has them all.
        /// </summary>
        public IMultiDimensionMaterial Material
        {
            get { return this.Layers.Count > 0 ? this.Layers[0].Material : null; }
            set
            {
                if (this.Layers.Count > 0)
                    this.Layers[0].Material = value;
            }
        }

        /// <summary>The whole stack. Setting it scales every layer by the same factor.</summary>
        public double Thickness
        {
            get { return this.Layers.Sum(layer => layer.Thickness); }
            set
            {
                double current = this.Thickness;
                if (current <= 0.0 || value <= 0.0)
                    return;

                double factor = value / current;
                foreach (var layer in this.Layers)
                    layer.Thickness *= factor;
            }
        }

        /// <summary>
        /// Each distinct material once. Distinct because Assemble writes one declaration per entry
        /// and a symmetric stack names the same material several times over.
        /// </summary>
        public IEnumerable<IMaterial> Materials
        {
            get
            {
                return this.Layers.Where(layer => layer.Material != null)
                                  .Select(layer => (IMaterial)layer.Material)
                                  .Distinct();
            }
        }

        public LayeredShellSection(string sectionName, IEnumerable<ShellLayer> layers)
        {
            this.SectionName = sectionName;
            this.Layers = (layers ?? Enumerable.Empty<ShellLayer>()).ToList();
        }

        /// <summary>
        /// One material split into <paramref name="layerCount"/> equal layers. The same stack
        /// OpenSees builds from its own two-argument form, written out layer by layer so that the
        /// deck says plainly what it is.
        /// </summary>
        public static LayeredShellSection Uniform(string sectionName, double thickness, int layerCount, IMultiDimensionMaterial material)
        {
            if (layerCount < MinLayers)
                layerCount = MinLayers;

            var layers = Enumerable.Range(0, layerCount)
                                   .Select(_ => new ShellLayer(material, thickness / layerCount));

            return new LayeredShellSection(sectionName, layers);
        }

        public string WriteTcl()
        {
            if (this.Layers.Count < MinLayers)
                throw new Exception(
                    $"A layered shell section needs at least {MinLayers} layers; \"{this.SectionName}\" has {this.Layers.Count}. " +
                    "OpenSees refuses fewer.");

            var stack = new StringBuilder();
            foreach (var layer in this.Layers)
                stack.Append($" {layer.Material.Id} {layer.Thickness}");

            return $"section LayeredShell {this.Id} {this.Layers.Count}{stack}\n";
        }

        public override string ToString()
        {
            return this.WriteTcl();
        }
    }
}
