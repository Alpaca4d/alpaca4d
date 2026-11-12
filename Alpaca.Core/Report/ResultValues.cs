using System.Collections.Generic;

namespace Alpaca4d.Report
{
    /// <summary>
    /// One component of a result as a picture of the report draws it - its values in the unit the
    /// picture shows, at the step it shows, through the layer and in the axes it shows - so that the
    /// table written after the picture says what the picture does. Read by the figure maker, which
    /// knows how Model View reads a result; laid out as a table by <see cref="ReportTables.OfValues"/>.
    /// </summary>
    public sealed class ResultValues
    {
        /// <summary>The result, as Model View's Results menu names it: Displacement, Beam forces, Reactions...</summary>
        public string Result = "";

        /// <summary>The component, as Model View names it: Uz, Mz, σ11, Fx...</summary>
        public string Component = "";

        /// <summary>The unit the values are in, as the picture writes it: mm, kN, kNm, MPa...</summary>
        public string Unit = "";

        /// <summary>What the values are at: <see cref="AtNode"/>, <see cref="AtElement"/> or <see cref="AtSupport"/>.</summary>
        public string At = AtNode;

        public const string AtNode = "Node", AtElement = "Element", AtSupport = "Support";

        /// <summary>
        /// The values with where each is: a node's or an element's id - an element once for each of its
        /// points - or a support's, in the model's order of supports.
        /// </summary>
        public List<KeyValuePair<string, double>> Values = new List<KeyValuePair<string, double>>();

        /// <summary>Whether the values of a column add up to something: the forces at the supports, not their moments.</summary>
        public bool Summed;

        /// <summary>What else the reader needs: the layer, the axes.</summary>
        public string Note = "";
    }
}
