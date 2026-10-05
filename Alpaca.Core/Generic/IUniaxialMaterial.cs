using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Generic;

namespace Alpaca4d.Generic
{
    public interface IUniaxialMaterial : IMaterial, ISerialize
    {
        public double E { get; set; }
        public double G { get; set; }
        public double Nu { get; set; }

        /// <summary>
        /// What the material is for design - its family and characteristic strengths - or null when
        /// nothing says. The solver never reads it; the Utilisation component does.
        /// </summary>
        public Alpaca4d.Material.IDesignGrade Grade { get; set; }
    }
}
