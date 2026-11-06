using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alpaca4d.Generic
{
    public interface ISection : ISerialize
    {
        public int? Id { get; set; }

        /// <summary>What the section is called - IPE200 out of the Section Library - or null when it was given no name.</summary>
        public string SectionName { get; set; }

        /// <summary>
        /// The colour the user gave the section, or null when they gave none: Model View then picks
        /// one of its own for each section when it colours the elements by section. Display only -
        /// the solver never reads it.
        /// </summary>
        public System.Drawing.Color? Color { get; set; }
    }
}
