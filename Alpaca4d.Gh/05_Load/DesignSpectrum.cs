using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

using Alpaca4d.Seismic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The response spectrum a Response Spectrum Analysis shakes the model with, by the code it is drawn
    /// to: the generic Eurocode (EN 1998-1 with its recommended values), the Italian NTC 2018, or a curve
    /// given point by point. A switcher, one unit per code, so a National Annex or another code is one
    /// more unit; every unit hands on the same outputs.
    /// </summary>
    public class DesignSpectrum : GH_SwitcherComponent
    {
        private readonly List<SubComponent> _subcomponents = new List<SubComponent>();

        public override string UnitMenuName => "Code";
        protected override string DefaultEvaluationUnit => GenericSpectrum.Name;

        public DesignSpectrum()
          : base("Design Spectrum (Alpaca4d)", "Design Spectrum",
            "The response spectrum to shake the model with in a Response Spectrum Analysis: the peak acceleration of " +
            "an oscillator, by its period, in g.\n" +
            "Generic: EN 1998-1 with the values it recommends, before any National Annex. NTC 2018: the Italian code, " +
            "from the ag, Fo and Tc* of the site. Custom: a curve given point by point. Pick the code from the menu " +
            "below the component.\n" +
            "With q the design spectrum, for the ultimate limit states; without it the elastic one.",
            "Alpaca4d", "05_Load")
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
            pManager.Register_GenericParam("Spectrum", "Spectrum",
                "The spectrum. Feed it to SpectrumX, SpectrumY or SpectrumZ of Response Spectrum Analysis.");
            pManager.Register_DoubleParam("Period", "Period", $"The periods the curve is drawn at, every 0.01 {Units.Time} and at its corners [{Units.Time}]");
            pManager.Register_DoubleParam("Acceleration", "Acceleration", "The spectral acceleration at each period [g]");
            pManager.Register_CurveParam("Curve", "Curve", "The spectrum drawn on the XY plane: period along X, acceleration in g along Y.");
            pManager.Register_StringParam("Parameters", "Parameters", "What the spectrum was worked out with: its soil factor, its corner periods, η or q.");
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _subcomponents.Add(new GenericSpectrum());
            _subcomponents.Add(new Ntc2018Spectrum());
            _subcomponents.Add(new CustomPointsSpectrum());

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

        /// <summary>The outputs every unit sets, from the spectrum it made, or an error when it could not make one.</summary>
        internal static void Output(IGH_DataAccess DA, Func<ResponseSpectrum> make, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            ResponseSpectrum spectrum;
            try
            {
                spectrum = make();
            }
            catch (ArgumentException ex)
            {
                msg = ex.Message;
                level = GH_RuntimeMessageLevel.Error;
                return;
            }

            double until = Math.Max(4.0, spectrum.Corners.DefaultIfEmpty(0.0).Max());
            var (periods, accelerations) = spectrum.Sample(until);

            DA.SetData(0, spectrum);
            DA.SetDataList(1, periods);
            DA.SetDataList(2, accelerations);
            DA.SetData(3, new PolylineCurve(periods.Zip(accelerations, (t, sa) => new Point3d(t, sa, 0.0))));
            DA.SetDataList(4, spectrum.Parameters);
        }

        /// <summary>The Component input both code units have.</summary>
        internal static void RegisterComponent(EvaluationUnit unit)
        {
            unit.RegisterInputParam(new Param_String(), "Component", "Component",
                "Horizontal or Vertical: which component of the ground motion. Connect a Value List to pick.",
                GH_ParamAccess.item, new GH_String("Horizontal"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            unit.Inputs[unit.Inputs.Count - 1].EnumInput = Enum.GetNames(typeof(SpectrumComponent)).ToList();
        }

        internal static SpectrumComponent ReadComponent(string text)
        {
            if (Enum.TryParse((text ?? "").Trim(), true, out SpectrumComponent component))
                return component;
            throw new ArgumentException($"Component is \"{text}\". It is Horizontal or Vertical.");
        }

        /// <summary>The q and Damping inputs both code units end with.</summary>
        internal static void RegisterBehaviour(EvaluationUnit unit)
        {
            unit.RegisterInputParam(new Param_Number(), "q", "q",
                "Behaviour factor. Given, the design spectrum, for the ultimate limit states. Left empty, the elastic " +
                "spectrum, for the damage limitation states and for a structure designed to stay elastic.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            RegisterDamping(unit, "It scales the elastic spectrum by η; a design spectrum assumes 5%. ");
        }

        internal static void RegisterDamping(EvaluationUnit unit, string what)
        {
            unit.RegisterInputParam(new Param_Number(), "Damping", "Damping",
                "Viscous damping of the structure [%]. " + what +
                "Response Spectrum Analysis also correlates the modes with it (CQC).",
                GH_ParamAccess.item, new GH_Number(5.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Design_Spectrum__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8A3F61D2-4C7B-4E95-9B10-2D6E5C8F7A31}");
    }

    /// <summary>EN 1998-1 with its recommended values: the generic Eurocode spectrum.</summary>
    internal class GenericSpectrum : SubComponent
    {
        public const string Name = "Design Spectrum Generic (Alpaca4d)";

        public override string name() => Name;
        public override string display_name() => "Generic";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "EN 1998-1 3.2.2, with the values it recommends.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Design_Spectrum__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_Number(), "agR", "agR",
                "Reference peak ground acceleration on type A ground, from the national hazard map [g].", GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Number(), "γI", "γI",
                "Importance factor: 0.8, 1.0, 1.2 or 1.4 for classes I to IV. ag = γI agR.", GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Ground", "Ground",
                "Ground type, A (rock) to E. Connect a Value List to pick.", GH_ParamAccess.item, new GH_String("B"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            unit.Inputs[unit.Inputs.Count - 1].EnumInput = new List<string> { "A", "B", "C", "D", "E" };

            unit.RegisterInputParam(new Param_Integer(), "Type", "Type",
                "Spectrum type: 1 where earthquakes above magnitude 5.5 govern the hazard, 2 where smaller ones do.",
                GH_ParamAccess.item, new GH_Integer(1));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            DesignSpectrum.RegisterComponent(unit);
            DesignSpectrum.RegisterBehaviour(unit);

            unit.RegisterInputParam(new Param_Number(), "β", "β",
                "Lower bound of the design spectrum, as a share of ag. 0.2 is recommended.", GH_ParamAccess.item, new GH_Number(0.2));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            double agR = 0.0, importance = 1.0, damping = 5.0, beta = 0.2;
            string ground = "B", component = "Horizontal";
            int type = 1;
            double? q = null;

            if (!DA.GetData(0, ref agR)) return;
            DA.GetData(1, ref importance);
            DA.GetData(2, ref ground);
            DA.GetData(3, ref type);
            DA.GetData(4, ref component);
            double value = 0.0;
            if (DA.GetData(5, ref value)) q = value;
            DA.GetData(6, ref damping);
            DA.GetData(7, ref beta);

            DesignSpectrum.Output(DA,
                () => new Ec8Spectrum(agR, importance, ground, type, DesignSpectrum.ReadComponent(component), q, damping, beta),
                out msg, out level);
        }
    }

    /// <summary>The Italian NTC 2018, 3.2.3.</summary>
    internal class Ntc2018Spectrum : SubComponent
    {
        public override string name() => "Design Spectrum NTC 2018 (Alpaca4d)";
        public override string display_name() => "NTC 2018";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "NTC 2018 3.2.3, from the ag, Fo and Tc* of the site.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Design_Spectrum__Alpaca4d_;
            mngr.RegisterUnit(unit);

            const string grid = " The hazard grid gives it for the site and the return period of the limit state (Allegato B; Spettri-NTC).";

            unit.RegisterInputParam(new Param_Number(), "ag", "ag", "Peak ground acceleration on type A ground [g]." + grid, GH_ParamAccess.item);
            unit.RegisterInputParam(new Param_Number(), "Fo", "Fo", "Peak amplification of the spectrum." + grid, GH_ParamAccess.item);
            unit.RegisterInputParam(new Param_Number(), "Tc*", "Tc*", $"Period at the start of the constant velocity branch [{Units.Time}]." + grid, GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_String(), "Soil", "Soil",
                "Ground category, A (rock) to E, as 3.2.2 defines them. Connect a Value List to pick.", GH_ParamAccess.item, new GH_String("B"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            unit.Inputs[unit.Inputs.Count - 1].EnumInput = new List<string> { "A", "B", "C", "D", "E" };

            unit.RegisterInputParam(new Param_String(), "Topography", "Topography",
                "Topographic category, T1 (flat) to T4. ST is taken at its largest, the top of the slope or the ridge. " +
                "Connect a Value List to pick.", GH_ParamAccess.item, new GH_String("T1"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            unit.Inputs[unit.Inputs.Count - 1].EnumInput = new List<string> { "T1", "T2", "T3", "T4" };

            DesignSpectrum.RegisterComponent(unit);
            DesignSpectrum.RegisterBehaviour(unit);
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            double ag = 0.0, f0 = 0.0, tcStar = 0.0, damping = 5.0;
            string soil = "B", topography = "T1", component = "Horizontal";
            double? q = null;

            if (!DA.GetData(0, ref ag)) return;
            if (!DA.GetData(1, ref f0)) return;
            if (!DA.GetData(2, ref tcStar)) return;
            DA.GetData(3, ref soil);
            DA.GetData(4, ref topography);
            DA.GetData(5, ref component);
            double value = 0.0;
            if (DA.GetData(6, ref value)) q = value;
            DA.GetData(7, ref damping);

            DesignSpectrum.Output(DA,
                () => new NtcSpectrum(ag, f0, tcStar, soil, topography, DesignSpectrum.ReadComponent(component), q, damping),
                out msg, out level);
        }
    }

    /// <summary>A spectrum given point by point.</summary>
    internal class CustomPointsSpectrum : SubComponent
    {
        public override string name() => "Design Spectrum Custom (Alpaca4d)";
        public override string display_name() => "Custom";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(name(), display_name(), "A spectrum given point by point.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Design_Spectrum__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_Number(), "Period", "Period",
                $"The periods, from the shortest up [{Units.Time}]. Straight between them, flat past the first and the last.",
                GH_ParamAccess.list);
            unit.RegisterInputParam(new Param_Number(), "Acceleration", "Acceleration",
                "The spectral acceleration at each period [g].", GH_ParamAccess.list);

            DesignSpectrum.RegisterDamping(unit, "Give the damping the curve was drawn for. ");
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            var periods = new List<double>();
            var accelerations = new List<double>();
            double damping = 5.0;

            if (!DA.GetDataList(0, periods)) return;
            if (!DA.GetDataList(1, accelerations)) return;
            DA.GetData(2, ref damping);

            DesignSpectrum.Output(DA, () => new CustomSpectrum(periods, accelerations, damping), out msg, out level);
        }
    }
}
