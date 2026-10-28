using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Design
{
    /// <summary>
    /// Beams that share one section, for the section optimiser: the beams whose ElementId matches,
    /// and the sections they may have, in the order to try them. The whole group takes one of them, the
    /// first that every beam of the group can carry its forces with. A member drawn as several beams
    /// is a group of its own, so that it ends up with one section, as it will be built.
    /// </summary>
    public class DesignGroup
    {
        public string Name { get; set; } = "";

        /// <summary>
        /// Which beams belong to the group, as the result filters take them: an ElementId, a wildcard
        /// over them, an element tag or a regular expression.
        /// </summary>
        public List<string> ElementIds { get; set; } = new List<string>();

        /// <summary>The sections the group's beams may have, in the order the optimiser tries them: it never sorts them.</summary>
        public List<IUniaxialSection> Sections { get; set; } = new List<IUniaxialSection>();

        public override string ToString()
        {
            string name = string.IsNullOrWhiteSpace(this.Name) ? "" : $" {this.Name.Trim()}";
            return $"Design Group{name}: {this.Sections.Count} section(s)";
        }
    }
}
