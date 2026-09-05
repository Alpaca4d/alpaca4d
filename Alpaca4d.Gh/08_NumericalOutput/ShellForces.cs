using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;


using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    public class ShellForces : GH_Component
    {
        public ShellForces()
          : base("Shell Forces (Alpaca4d)", "Shell Forces",
            "Reads the stress resultants of every shell element of an analysed model - membrane " +
            "forces pxx, pyy and pxy, bending moments mxx, myy and mxy, and transverse shears vxz " +
            "and vyz.\n" +
            "All per unit width and in the shell's local axes. One branch per element, keyed by " +
            "its tag, holding the values at the integration points.\n" +
            "Give ElementId to read part of a big model.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. The outputs then carry a step index in " +
                "front of the element's tag, so a branch reads {step; tag}. Step is ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            _filterInput = pManager.ParamCount;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>Where the ElementId filter sits in the input list.</summary>
        private int _filterInput;

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("pxx", "pxx", $"[{Units.Force}/{Units.Length}]");
            pManager.Register_GenericParam("pyy", "pyy", $"[{Units.Force}/{Units.Length}]");
            pManager.Register_GenericParam("pxy", "pxy", $"[{Units.Force}/{Units.Length}]");
            pManager.Register_GenericParam("mxx", "mxx", $"[{Units.Force}{Units.Length}/{Units.Length}]");
            pManager.Register_GenericParam("myy", "myy", $"[{Units.Force}{Units.Length}/{Units.Length}]");
            pManager.Register_GenericParam("mxy", "mxy", $"[{Units.Force}{Units.Length}/{Units.Length}]");
            pManager.Register_GenericParam("vxz", "vxz", $"[{Units.Force}/{Units.Length}]");
            pManager.Register_GenericParam("vyz", "vyz", $"[{Units.Force}/{Units.Length}]");
            pManager.Register_GenericParam("Element", "Element", ElementIdentity.ElementOutput);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var alpacaModel = new Alpaca4d.Model();
            bool history = false;
            int step = 0;

            if (!DA.GetData(0, ref alpacaModel)) return;
            DA.GetData(1, ref history);
            DA.GetData(2, ref step);

            var filter = ElementFilterInput.Read(DA, _filterInput, this);

            var steps = HistorySteps.Of(alpacaModel, history, step, this);
            if (steps == null) return;

            // The recorder keeps one dataset per element class, and Read.ASDQ4Forces and
            // Read.ASDT3Forces each give one entry per shell of their own class in the order the
            // model holds them - so the two classes are filtered and sliced apart, then merged
            // into one tree keyed by element tag.
            var quadShells = alpacaModel.Shells.Where(x => x.ElementClass == Element.ElementClass.ASDShellQ4).ToList();
            var triShells = alpacaModel.Shells.Where(x => x.ElementClass == Element.ElementClass.ASDShellT3).ToList();

            var keptQuad = filter.SelectIndices(quadShells);
            var keptTri = filter.SelectIndices(triShells);

            var quadShellTag = keptQuad.Select(i => quadShells[i].Id).ToList();
            var triShellTag = keptTri.Select(i => triShells[i].Id).ToList();

            if (keptQuad.Count == 0 && keptTri.Count == 0 && !filter.MatchesEverything)
            {
                // Nothing to report and no results to show for it, which on its own is
                // indistinguishable from an analysis that wrote nothing. Say which it is.
                ElementFilterInput.Select(filter, alpacaModel.Shells, alpacaModel, "shells", this);
            }

            // The elements being reported, sorted by tag. The quads and the triangles are read
            // from two separate datasets and merged into one tree keyed by element tag, and
            // Grasshopper walks a tree's branches in path order - so anything handed out as a
            // flat list beside that tree has to be in tag order as well, or it reads against the
            // wrong branch in any model that mixes the two kinds.
            var keptShells = keptQuad.Select(i => quadShells[i])
                                     .Concat(keptTri.Select(i => triShells[i]))
                                     .OrderBy(x => x.Id)
                                     .ToList();

            var fxTree = new DataTree<object>();
            var fyTree = new DataTree<object>();
            var fxyTree = new DataTree<object>();
            var mxTree = new DataTree<object>();
            var myTree = new DataTree<object>();
            var mxyTree = new DataTree<object>();
            var vxzTree = new DataTree<object>();
            var vyzTree = new DataTree<object>();

            foreach (int current in steps)
            {
                var fxQuad = new List<List<double>>();
                var fyQuad = new List<List<double>>();
                var fxyQuad = new List<List<double>>();
                var mxQuad = new List<List<double>>();
                var myQuad = new List<List<double>>();
                var mxyQuad = new List<List<double>>();
                var vxzQuad = new List<List<double>>();
                var vyzQuad = new List<List<double>>();

                var fxTri = new List<List<double>>();
                var fyTri = new List<List<double>>();
                var fxyTri = new List<List<double>>();
                var mxTri = new List<List<double>>();
                var myTri = new List<List<double>>();
                var mxyTri = new List<List<double>>();
                var vxzTri = new List<List<double>>();
                var vyzTri = new List<List<double>>();


                if (alpacaModel.HasQuadShell)
                    (fxQuad, fyQuad, fxyQuad, mxQuad, myQuad, mxyQuad, vxzQuad, vyzQuad) = Alpaca4d.Result.Read.ASDQ4Forces(alpacaModel, current);
                if(alpacaModel.HasTriShell)
                    (fxTri, fyTri, fxyTri, mxTri, myTri, mxyTri, vxzTri, vyzTri) = Alpaca4d.Result.Read.ASDT3Forces(alpacaModel, current);

                // Convert Nested List to DataTree, one branch per shell being reported, keyed by
                // the element's tag. It used to be keyed by Id-1, so branch {0} meant element 1;
                // the tag itself is unique, which is what lets a branch say which element it
                // belongs to without an output of its own.
                var fxQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fxQuad, keptQuad), quadShellTag);
                var fyQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fyQuad, keptQuad), quadShellTag);
                var fxyQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fxyQuad, keptQuad), quadShellTag);
                var mxQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(mxQuad, keptQuad), quadShellTag);
                var myQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(myQuad, keptQuad), quadShellTag);
                var mxyQuadTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(mxyQuad, keptQuad), quadShellTag);
                var vxzQuadTree =  Utils.DataTreeFromNestedList(ElementFilterInput.Slice(vxzQuad, keptQuad), quadShellTag);
                var vyzQuadTree =  Utils.DataTreeFromNestedList(ElementFilterInput.Slice(vyzQuad, keptQuad), quadShellTag);

                // Convert Nested List to DataTree
                var fxTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fxTri, keptTri), triShellTag);
                var fyTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fyTri, keptTri), triShellTag);
                var fxyTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(fxyTri, keptTri), triShellTag);
                var mxTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(mxTri, keptTri), triShellTag);
                var myTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(myTri, keptTri), triShellTag);
                var mxyTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(mxyTri, keptTri), triShellTag);
                var vxzTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(vxzTri, keptTri), triShellTag);
                var vyzTriTree = Utils.DataTreeFromNestedList(ElementFilterInput.Slice(vyzTri, keptTri), triShellTag);
            

                fxQuadTree.MergeTree(fxTriTree);
                fyQuadTree.MergeTree(fyTriTree);
                fxyQuadTree.MergeTree(fxyTriTree);
                mxQuadTree.MergeTree(mxTriTree);
                myQuadTree.MergeTree(myTriTree);
                mxyQuadTree.MergeTree(mxyTriTree);
                vxzQuadTree.MergeTree(vxzTriTree);
                vyzQuadTree.MergeTree(vyzTriTree);


                HistorySteps.Collect(fxTree, fxQuadTree, current, history);
                HistorySteps.Collect(fyTree, fyQuadTree, current, history);
                HistorySteps.Collect(fxyTree, fxyQuadTree, current, history);
                HistorySteps.Collect(mxTree, mxQuadTree, current, history);
                HistorySteps.Collect(myTree, myQuadTree, current, history);
                HistorySteps.Collect(mxyTree, mxyQuadTree, current, history);
                HistorySteps.Collect(vxzTree, vxzQuadTree, current, history);
                HistorySteps.Collect(vyzTree, vyzQuadTree, current, history);
            }

            // Finally assign the spiral to the output parameter.
            DA.SetDataTree(0, fxTree);
            DA.SetDataTree(1, fyTree);
            DA.SetDataTree(2, fxyTree);
            DA.SetDataTree(3, mxTree);
            DA.SetDataTree(4, myTree);
            DA.SetDataTree(5, mxyTree);
            DA.SetDataTree(6, vxzTree);
            DA.SetDataTree(7, vyzTree);
            DA.SetDataList(8, keptShells);
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.shellStress;

        public override Guid ComponentGuid => new Guid("{8A27E0D6-4D39-417E-A9E9-202AB291CE01}");
    }
}