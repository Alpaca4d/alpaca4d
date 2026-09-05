using System.Collections.Generic;

using Rhino.Geometry;

namespace Alpaca4d.Generic
{
    /// <summary>
    /// What the two spring elements have in common.
    ///
    /// A spring is a set of uniaxial materials, one per degree of freedom it acts in, and a local
    /// frame those degrees of freedom count along. Directions left out of the list carry no
    /// stiffness at all - a link with only direction 1 is a pin that resists nothing but the pull
    /// along its own axis - so a release is exact rather than a small number standing in for zero.
    ///
    /// Directions are numbered the way OpenSees numbers them: 1, 2, 3 translate along the local x,
    /// y and z axes and 4, 5, 6 rotate about them. They are local, not global, which is the whole
    /// point of <see cref="Orient"/>.
    /// </summary>
    public interface ILink : IElement
    {
        /// <summary>One material per entry of <see cref="Directions"/>, in the same order.</summary>
        List<IUniaxialMaterial> Materials { get; set; }

        /// <summary>Which local degrees of freedom the materials act in, 1-6.</summary>
        List<int> Directions { get; set; }

        /// <summary>
        /// The local frame the directions count along. Its X axis is local 1, its Y axis local 2
        /// and its Z axis local 3.
        /// </summary>
        Plane Orient { get; set; }
    }
}
