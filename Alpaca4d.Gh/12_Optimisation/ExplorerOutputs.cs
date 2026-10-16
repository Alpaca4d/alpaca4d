using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.Explorer;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Names the results a design is measured by - its mass, its largest deflection, its first
    /// period - so that Explorer Record can write them as the out: columns of the study.
    ///
    /// Each input is one result, named after what is wired into it; rename the input to name the
    /// column, units and all ("Mass [t]"). An input given a list gives one column per item,
    /// Name_1, Name_2 and on, up to ten: more than that is nearly always a list that was meant to be
    /// reduced to one number first. An input with no value writes an empty cell, which the explorer
    /// shows as no data rather than as a number that would stretch the axis.
    /// </summary>
    public class ExplorerOutputs : GH_Component, IGH_VariableParameterComponent
    {
        private const int MaxValuesPerInput = 10;
        private const string ResultDescription =
            "A result of the design: a number, or a text for a category. The input's name is the column's name, so rename it to say what it is, units included.\n" +
            "A new input appears when the last one is used.";

        public ExplorerOutputs()
          : base("Explorer Outputs (Alpaca4d)", "Explorer Outputs",
            "Names the results of a design - mass, deflection, utilisation, period - for Explorer Record to save, " +
            "and for the explorer to show as the axes to the right of the inputs.\n" +
            "Each input is one result, named after the input: rename an input to name its column.",
            "Alpaca4d", "12_Optimisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
            Params.ParameterSourcesChanged += OnSourcesChanged;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Explorer_Outputs__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{E48E97B1-621B-44C9-8685-4C0D23EB96DF}");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddParameter(NewResultInput(), "Result", "Out 1", ResultDescription, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager[pManager.ParamCount - 1].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Outputs", "Outputs", "The results by name. Connect to the Outputs of Explorer Record.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var results = new List<ExplorerValue>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Params.Input.Count; i++)
            {
                var input = Params.Input[i];
                if (input.SourceCount == 0) continue;

                string name = input.NickName.Trim();
                var data = new List<IGH_Goo>();
                DA.GetDataList(i, data);

                if (data.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{name} has no value, so it is written as no data.");
                    data.Add(null);
                }
                if (data.Count > MaxValuesPerInput)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"{name} has {data.Count} values; the first {MaxValuesPerInput} are kept. Reduce it to the number that matters - the largest, the sum - before it gets here.");
                    data = data.Take(MaxValuesPerInput).ToList();
                }

                for (int j = 0; j < data.Count; j++)
                {
                    string column = data.Count == 1 ? name : $"{name}_{j + 1}";
                    if (!names.Add(column))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Two results are called {column}. Rename one of the inputs.");
                        return;
                    }
                    results.Add(new ExplorerValue(column, ValueOf(data[j])));
                }
            }

            DA.SetDataList(0, results);
        }

        /// <summary>A value as data.csv holds it: numbers the invariant way, everything else as text.</summary>
        internal static string ValueOf(IGH_Goo goo)
        {
            switch (goo)
            {
                case null:
                    return "";
                case GH_Number number:
                    return ExplorerValue.Format(number.Value);
                case GH_Integer integer:
                    return integer.Value.ToString(CultureInfo.InvariantCulture);
                case GH_Boolean boolean:
                    return boolean.Value ? "true" : "false";
                case GH_String text:
                    return text.Value ?? "";
            }

            double value = 0;
            if (GH_Convert.ToDouble_Primary(goo, ref value)) return ExplorerValue.Format(value);
            return goo.ToString();
        }

        #region Variable parameters

        private static Param_GenericObject NewResultInput()
        {
            return new Param_GenericObject
            {
                Name = "Result",
                NickName = "",
                Description = ResultDescription,
                Access = GH_ParamAccess.list,
                Optional = true,
                DataMapping = GH_DataMapping.Flatten,
            };
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

        public bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && Params.Input.Count > 1;

        public IGH_Param CreateParameter(GH_ParameterSide side, int index) => NewResultInput();

        public bool DestroyParameter(GH_ParameterSide side, int index) => true;

        public void VariableParameterMaintenance()
        {
            for (int i = 0; i < Params.Input.Count; i++)
            {
                var input = Params.Input[i];
                input.Name = "Result";
                input.Description = ResultDescription;
                input.Access = GH_ParamAccess.list;
                input.Optional = true;
                input.DataMapping = GH_DataMapping.Flatten;
                if (string.IsNullOrWhiteSpace(input.NickName) || IsDefaultName(input.NickName))
                    input.NickName = $"Out {i + 1}";
            }
        }

        private static bool IsDefaultName(string name) => name.StartsWith("Out ") && int.TryParse(name.Substring(4), out _);

        /// <summary>
        /// Names an input after what is wired into it, unless it has been named already, and keeps
        /// one free input at the end.
        /// </summary>
        private void OnSourcesChanged(object sender, GH_ParamServerEventArgs e)
        {
            if (e.ParameterSide != GH_ParameterSide.Input) return;

            var input = e.Parameter;
            if (input.SourceCount > 0 && IsDefaultName(input.NickName))
            {
                string name = input.Sources[0].NickName?.Trim();
                if (!string.IsNullOrEmpty(name)) input.NickName = name;
            }

            if (Params.Input[Params.Input.Count - 1].SourceCount > 0)
                Params.RegisterInputParam(NewResultInput(), Params.Input.Count);
            VariableParameterMaintenance();
            Params.OnParametersChanged();
        }

        #endregion
    }
}
