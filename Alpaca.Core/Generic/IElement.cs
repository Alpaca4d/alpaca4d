using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Element;

namespace Alpaca4d.Generic
{
    public interface IElement : ISerialize
    {
        public ElementType Type { get; }
        public int? Id { get; set;  }

        /// <summary>
        /// The identifier the user typed on the element component, or null when they typed none.
        /// It is what the element result components filter on.
        ///
        /// Free text and deliberately not unique. One ASD Shell component fans a mesh out into one
        /// element per face and gives every face the same one; a list of curves and a matching list
        /// of identifiers gives each beam its own. Whether twenty elements share an identifier or
        /// carry twenty different ones is the user's choice, and nothing here rewrites what they
        /// typed - a shared identifier is a group, and asking for it returns the whole group.
        ///
        /// Not to be confused with <see cref="Id"/>, which is the number Assemble hands out and
        /// OpenSees knows the element by. That one is always unique and is never authored.
        /// </summary>
        public string ElementId { get; set; }
        public void SetTags();
        public void SetTopologyRTree(Alpaca4d.Model model);

    }
}