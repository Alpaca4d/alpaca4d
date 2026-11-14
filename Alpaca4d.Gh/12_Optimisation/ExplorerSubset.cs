using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Runs part of a study rather than all of it, in one of two ways or both.
    ///
    /// Divisions coarsen it: a slider with fifty ticks taken five times is a first look at the whole
    /// range for a tenth of the work. Ranges split it: the designs are numbered, and 0 to 499 on one
    /// machine and 500 to 999 on another make the whole study between them. Both work the way they
    /// did in Colibri, so a study split there splits the same way here.
    /// </summary>
    public class ExplorerSubset : GH_Component
    {
        public ExplorerSubset()
          : base("Explorer Subset (Alpaca4d)", "Explorer Subset",
            "Runs part of a study. Divisions take fewer steps of each input; Ranges run some of the designs, " +
            "by number, to split a study between machines.",
            "Alpaca4d", "12_Optimisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Explorer_Subset__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{5CDF20EB-84FF-43F3-A681-954537681541}");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Divisions", "Divisions",
                "How many steps to take of each input of Explorer Inputs, one number per input, in order.\n" +
                "0 takes every step, 1 only the one the input is on now, more takes that many spread evenly from the first step to the last.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Ranges", "Ranges",
                "Which designs to run, by number, counted from 0 after the divisions: 0 To 499 runs the first five hundred. " +
                "Give each machine a range of its own to split a study between them.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Subset", "Subset", "Connect to the Subset of Explorer Inputs.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var divisions = new List<int>();
            var ranges = new List<Interval>();
            DA.GetDataList(0, divisions);
            DA.GetDataList(1, ranges);

            if (divisions.Any(division => division < 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Divisions are 0 or more.");
                return;
            }
            if (ranges.Any(range => Math.Max(range.T0, range.T1) < 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Designs are numbered from 0, so a range below 0 holds none.");
                return;
            }

            DA.SetData(0, new ExplorerSubsetSettings(divisions, ranges.Select(range => new KeyValuePair<double, double>(range.T0, range.T1))));
        }
    }
}
