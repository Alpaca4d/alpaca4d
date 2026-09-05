using Grasshopper.Kernel;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The checks the two spring components share.
    ///
    /// The Core classes check the same things when they write their tcl, because a link built in
    /// code has to be caught too. These run first so that the message lands on the component that
    /// can be fixed rather than on Assemble Model, several steps downstream.
    /// </summary>
    internal static class LinkInput
    {
        /// <summary>True when the materials and directions make a link that can be written.</summary>
        public static bool Check(List<IUniaxialMaterial> materials, List<int> directions, GH_Component component)
        {
            if (materials.Count != directions.Count)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{materials.Count} materials against {directions.Count} directions. Give one " +
                    "material per direction, or a single material to use in all of them.");
                return false;
            }

            var outside = directions.Where(direction => direction < 1 || direction > 6).ToList();
            if (outside.Count != 0)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Direction runs 1 to 6; {string.Join(", ", outside)} is outside that. 1-3 are " +
                    "translation along the local x, y and z axes, 4-6 rotation about them.");
                return false;
            }

            if (directions.Distinct().Count() != directions.Count)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The same direction is listed more than once. Each direction takes one material; " +
                    "for two springs in parallel, use two of these.");
                return false;
            }

            if (materials.Any(material => material == null))
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "One of the materials is empty. Every direction needs one.");
                return false;
            }

            return true;
        }
    }
}
