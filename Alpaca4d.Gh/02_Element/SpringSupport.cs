using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

using Alpaca4d.Element;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A support that holds through springs rather than rigidly: elastic ones, or one that holds only
    /// while the node pushes into it. It never fixes the node itself, so an ordinary Support on the
    /// same point can hold the directions it leaves free.
    /// </summary>
    public class SpringSupport : GH_SwitcherComponent
    {
        private readonly List<SubComponent> _subcomponents = new List<SubComponent>();

        public override string UnitMenuName => "Support Type";
        protected override string DefaultEvaluationUnit => ElasticSpringSupport.Name;

        public SpringSupport()
          : base("Spring Support (Alpaca4d)", "Spring Support",
            "A support that holds through springs rather than rigidly. Elastic: a spring along or about each axis of " +
            "the Position's plane. Compression Only: holds the node only while it pushes into the support, along the " +
            "plane's Z, and lets it lift - after a gap, if there is one.\n" +
            "It never fixes the node itself, so an ordinary Support on the same point can hold what it leaves free: " +
            "a pinned bearing that can lift is this one, Compression Only, with a Support holding Tx and Ty only.\n" +
            "Reaction Force gives it a row of its own, with the spring's force.",
            "Alpaca4d", "02_Element")
        {
            ((GH_Component)this).Hidden = false;
            UpdateMessage();
        }

        private void UpdateMessage()
        {
            string name = ActiveUnit?.DisplayName ?? this.NickName;
            this.Message = $"{name}\n{this.Category}";
        }

        protected override void SwitchUnit(EvaluationUnit unit, bool recompute = true, bool recordEvent = true)
        {
            base.SwitchUnit(unit, recompute, recordEvent);
            UpdateMessage();
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Support", "Support", "The support. Feed it to the Supports input of Assemble Model.");
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _subcomponents.Add(new ElasticSpringSupport());
            _subcomponents.Add(new CompressionOnlySpringSupport());

            foreach (var sub in _subcomponents)
                sub.registerEvaluationUnits(mngr);
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            foreach (var sub in _subcomponents)
                sub.OnComponentLoaded();
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null) return;

            foreach (var sub in _subcomponents)
            {
                if (unit.Name.Equals(sub.name()))
                {
                    sub.SolveInstance(DA, out var msg, out var level);
                    if (!string.IsNullOrEmpty(msg))
                        ((GH_ActiveObject)this).AddRuntimeMessage(level, msg);
                    return;
                }
            }

            throw new Exception("Invalid sub-component");
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Spring_Support__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05");

        internal const string PositionDescription =
            "Point or Plane to support [{0}]. The springs act along and about the plane's own axes; a Point uses the global ones.";
    }

    internal class ElasticSpringSupport : SubComponent
    {
        public const string Name = "Spring Support Elastic (Alpaca4d)";

        public override string name() => Name;
        public override string display_name() => "Elastic";

        private static readonly string[] Axes = { "Kx", "Ky", "Kz", "Krx", "Kry", "Krz" };

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "Elastic springs along and about the axes of the plane.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Spring_Support__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_Plane(), "Position", "Position",
                string.Format(SpringSupport.PositionDescription, Units.Length), GH_ParamAccess.item);

            for (int i = 0; i < Axes.Length; i++)
            {
                var unitText = i < 3 ? $"{Units.Force}/{Units.Length}" : $"{Units.Force}{Units.Length}/{Units.Angle}";
                var what = i < 3 ? $"along the plane's {"XYZ"[i]} axis" : $"about the plane's {"XYZ"[i - 3]} axis";
                unit.RegisterInputParam(new Param_Number(), Axes[i], Axes[i],
                    $"Stiffness {what} [{unitText}]. Left empty, or zero, the node is free that way.", GH_ParamAccess.item);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            }
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            var position = Plane.WorldXY;
            if (!DA.GetData(0, ref position)) return;

            var springs = new List<SupportSpring>();
            for (int i = 0; i < Axes.Length; i++)
            {
                double k = 0.0;
                if (!DA.GetData(1 + i, ref k) || k == 0.0)
                    continue;
                if (!(k > 0.0))
                {
                    msg = $"{Axes[i]} is {k}. A spring's stiffness is greater than zero; leave it empty to free that direction.";
                    level = GH_RuntimeMessageLevel.Error;
                    return;
                }
                springs.Add(new SupportSpring(i + 1, k));
            }

            if (springs.Count == 0)
            {
                msg = "Give at least one stiffness, Kx to Krz: with none the support holds nothing.";
                return;
            }

            DA.SetData(0, Alpaca4d.Element.Support.WithSprings(position, springs));
        }
    }

    internal class CompressionOnlySpringSupport : SubComponent
    {
        public override string name() => "Spring Support Compression Only (Alpaca4d)";
        public override string display_name() => "Compression Only";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "Holds the node along the plane's Z only while it pushes into the support.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Spring_Support__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_Plane(), "Position", "Position",
                string.Format(SpringSupport.PositionDescription, Units.Length) +
                " It holds along the plane's Z only, while the node moves against Z: down, for a Point. Turn the plane over " +
                "for a support that holds only in tension.", GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Number(), "Stiffness", "Stiffness",
                $"[{Units.Force}/{Units.Length}]. Left empty it is rigid - as stiff as the supports Alpaca4d skews with.", GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Gap", "Gap",
                $"How far the node moves into the support before it takes hold [{Units.Length}]. Until then the structure " +
                "has to hold the node by itself.", GH_ParamAccess.item, new GH_Number(0.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            var position = Plane.WorldXY;
            if (!DA.GetData(0, ref position)) return;

            double stiffness = 0.0;
            double? k = DA.GetData(1, ref stiffness) ? stiffness : (double?)null;
            if (k.HasValue && !(k.Value > 0.0))
            {
                msg = $"Stiffness is {k.Value}. Give one greater than zero, or leave it empty for a rigid support.";
                level = GH_RuntimeMessageLevel.Error;
                return;
            }

            double gap = 0.0;
            DA.GetData(2, ref gap);
            if (!(gap >= 0.0))
            {
                msg = $"Gap is {gap}. It is a distance: zero or more.";
                level = GH_RuntimeMessageLevel.Error;
                return;
            }

            var spring = new SupportSpring(3, k, SpringBehaviour.CompressionOnly, gap);
            DA.SetData(0, Alpaca4d.Element.Support.WithSprings(position, new[] { spring }));

            msg = "A compression-only support needs a non-linear analysis to let go: Newton, in several steps.";
            level = GH_RuntimeMessageLevel.Remark;
        }
    }
}
