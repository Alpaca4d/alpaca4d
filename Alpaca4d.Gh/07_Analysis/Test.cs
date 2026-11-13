using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Alpaca4d.Gh;

namespace Alpaca4d.Gh
{
    public class TestBase : GH_SwitcherComponent
    {
        private List<SubComponent> _subcomponents = new List<SubComponent>();
        public override string UnitMenuName => "Test";
        protected override string DefaultEvaluationUnit => _subcomponents.Count > 0 ? _subcomponents[0].name() : "EnergyIncr";
        public override Guid ComponentGuid => new Guid("{F8A3B2C1-D4E5-4F6A-B7C8-9D0E1F2A3B4C}");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;

        public TestBase()
            : base("Test (Alpaca4d)", "Test",
              "When an iteration inside a step has converged, and how many iterations to allow " +
              "before the step is declared failed.\n" +
              "NormDispIncr measures how much the model still moves, NormUnbalance how far it is " +
              "from equilibrium, and EnergyIncr the product of the two; the Relative units compare " +
              "against the first iteration instead of an absolute tolerance, and FixedNumIter simply " +
              "iterates a set number of times. Feeds the Test input of Analysis Settings.",
              "Alpaca4d", "07_Analysis")
        {
            ((GH_Component)this).Hidden = false;
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.RegisterParam(new Param_GenericObject(), "Test", "Test", "The convergence test. Feed it to the Test input of Analysis Settings.");
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _subcomponents.Add(new TestEnergyIncr());
            _subcomponents.Add(new TestNormUnbalance());
            _subcomponents.Add(new TestNormDispIncr());
            _subcomponents.Add(new TestNormDispAndUnbalance());
            _subcomponents.Add(new TestNormDispOrUnbalance());
            _subcomponents.Add(new TestRelativeNormUnbalance());
            _subcomponents.Add(new TestRelativeNormDispIncr());
            _subcomponents.Add(new TestRelativeTotalNormDispIncr());
            _subcomponents.Add(new TestRelativeEnergyIncr());
            _subcomponents.Add(new TestFixedNumIter());

            foreach (SubComponent item in _subcomponents)
            {
                item.registerEvaluationUnits(mngr);
            }
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            foreach (SubComponent item in _subcomponents)
            {
                item.OnComponentLoaded();
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null)
            {
                return;
            }
            foreach (SubComponent item in _subcomponents)
            {
                if (unit.Name.Equals(item.name()))
                {
                    item.SolveInstance(DA, out var msg, out var level);
                    if (msg != "")
                    {
                        ((GH_ActiveObject)this).AddRuntimeMessage(level, msg);
                    }
                    return;
                }
            }
            throw new Exception("Invalid sub-component");
        }
        
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
        }
        
        private void Menu_ActivateUnit(object sender, EventArgs e)
        {
            try
            {
                EvaluationUnit evaluationUnit = (EvaluationUnit)((ToolStripMenuItem)sender).Tag;
                if (evaluationUnit != null)
                {
                    SwitchUnit(evaluationUnit);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    // Base class for all test subcomponents to share common parameter registration
    internal abstract class TestSubComponentBase : SubComponent
    {
        protected abstract Alpaca4d.Test.TestType TestType { get; }

        /// <summary>
        /// FixedNumIter stops after a set number of iterations whatever the residual is,
        /// so it has no tolerance to offer - and OpenSees reads its arguments as ints, so
        /// a tolerance written into the deck stops the analysis outright.
        /// </summary>
        protected virtual bool HasTolerance => true;

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), $"{TestType} Test");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            if (HasTolerance)
            {
                evaluationUnit.RegisterInputParam(new Param_Number(), "Tolerance", "Tol", "Convergence tolerance", GH_ParamAccess.item, new GH_Number(1e-8));
                evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
            }

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Iteration", "Iter", "Maximum number of iterations", GH_ParamAccess.item, new GH_Integer(10));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Flag", "Flag", "0 - Nothing\n1 - EachTime\n2 - Successful\n4 - EachStep\n5 - ErrorMessage", GH_ParamAccess.item, new GH_Integer(0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Norm", "Norm", "0 - MaxNorm\n1 - OneNorm\n2 - TwoNorm", GH_ParamAccess.item, new GH_Integer(2));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            // The inputs shift up by one when there is no tolerance to read.
            int index = 0;

            double tol = 1e-8;
            if (HasTolerance)
                DA.GetData(index++, ref tol);

            int iter = 10;
            DA.GetData(index++, ref iter);

            int flag = 0;
            DA.GetData(index++, ref flag);
            var flagEnum = (Alpaca4d.Test.FlagType)flag;

            int norm = 2;
            DA.GetData(index++, ref norm);
            var normEnum = (Alpaca4d.Test.NormType)norm;

            // No MaxIncr input: the bundled OpenSees reads a fifth number as maxTol, a
            // divergence guard, so any value offered here would cut analyses short rather
            // than cap increments. See Alpaca4d.Test.WriteTcl.
            var test = new Alpaca4d.Test(TestType, tol, iter, flagEnum, normEnum);

            DA.SetData(0, test);
        }
    }

    internal class TestEnergyIncr : TestSubComponentBase
    {
        public override string name() => "EnergyIncr";
        public override string display_name() => "EnergyIncr";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.EnergyIncr;
    }

    internal class TestNormUnbalance : TestSubComponentBase
    {
        public override string name() => "NormUnbalance";
        public override string display_name() => "NormUnbalance";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.NormUnbalance;
    }

    internal class TestNormDispIncr : TestSubComponentBase
    {
        public override string name() => "NormDispIncr";
        public override string display_name() => "NormDispIncr";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.NormDispIncr;
    }

    internal class TestNormDispAndUnbalance : SubComponent
    {
        public override string name() => "NormDispAndUnbalance";
        public override string display_name() => "NormDispAndUnbalance";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "NormDispAndUnbalance Test");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_Number(), "TolIncr", "TolIncr", "Tolerance on displacement increment", GH_ParamAccess.item, new GH_Number(1e-8));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "TolR", "TolR", "Tolerance on residual force", GH_ParamAccess.item, new GH_Number(1e-8));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Iteration", "Iter", "Maximum number of iterations", GH_ParamAccess.item, new GH_Integer(10));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Flag", "Flag", "0 - Nothing\n1 - EachTime\n2 - Successful\n4 - EachStep\n5 - ErrorMessage", GH_ParamAccess.item, new GH_Integer(0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            // No Norm or MaxIncr here. OpenSees reads this test's trailing pair back over
            // the iteration limit and the print flag rather than into the norm and maxIncr
            // slots - see NormDispAndUnbalance.cpp, which writes into idata[0] where
            // NormDispOrUnbalance beside it writes into idata[2]. Offering them would only
            // let a user overwrite their own iteration limit. Use NormDispOrUnbalance if
            // you need to choose the norm.
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            double tolIncr = 1e-8;
            DA.GetData(0, ref tolIncr);

            double tolR = 1e-8;
            DA.GetData(1, ref tolR);

            int iter = 10;
            DA.GetData(2, ref iter);

            int flag = 0;
            DA.GetData(3, ref flag);
            var flagEnum = (Alpaca4d.Test.FlagType)flag;

            var test = Alpaca4d.Test.NormDispAndUnbalance(tolIncr, tolR, iter, flagEnum);

            DA.SetData(0, test);
        }
    }

    internal class TestNormDispOrUnbalance : SubComponent
    {
        public override string name() => "NormDispOrUnbalance";
        public override string display_name() => "NormDispOrUnbalance";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "NormDispOrUnbalance Test");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_Number(), "TolIncr", "TolIncr", "Tolerance on displacement increment", GH_ParamAccess.item, new GH_Number(1e-8));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "TolR", "TolR", "Tolerance on residual force", GH_ParamAccess.item, new GH_Number(1e-8));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Iteration", "Iter", "Maximum number of iterations", GH_ParamAccess.item, new GH_Integer(10));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Flag", "Flag", "0 - Nothing\n1 - EachTime\n2 - Successful\n4 - EachStep\n5 - ErrorMessage", GH_ParamAccess.item, new GH_Integer(0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Integer(), "Norm", "Norm", "0 - MaxNorm\n1 - OneNorm\n2 - TwoNorm", GH_ParamAccess.item, new GH_Integer(2));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            double tolIncr = 1e-8;
            DA.GetData(0, ref tolIncr);

            double tolR = 1e-8;
            DA.GetData(1, ref tolR);

            int iter = 10;
            DA.GetData(2, ref iter);

            int flag = 0;
            DA.GetData(3, ref flag);
            var flagEnum = (Alpaca4d.Test.FlagType)flag;

            int norm = 2;
            DA.GetData(4, ref norm);
            var normEnum = (Alpaca4d.Test.NormType)norm;



            var test = Alpaca4d.Test.NormDispOrUnbalance(tolIncr, tolR, iter, flagEnum, normEnum);

            DA.SetData(0, test);
        }
    }

    internal class TestRelativeNormUnbalance : TestSubComponentBase
    {
        public override string name() => "RelativeNormUnbalance";
        public override string display_name() => "RelativeNormUnbalance";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.RelativeNormUnbalance;
    }

    internal class TestRelativeNormDispIncr : TestSubComponentBase
    {
        public override string name() => "RelativeNormDispIncr";
        public override string display_name() => "RelativeNormDispIncr";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.RelativeNormDispIncr;
    }

    internal class TestRelativeTotalNormDispIncr : TestSubComponentBase
    {
        public override string name() => "RelativeTotalNormDispIncr";
        public override string display_name() => "RelativeTotalNormDispIncr";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.RelativeTotalNormDispIncr;
    }

    internal class TestRelativeEnergyIncr : TestSubComponentBase
    {
        public override string name() => "RelativeEnergyIncr";
        public override string display_name() => "RelativeEnergyIncr";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.RelativeEnergyIncr;
    }

    internal class TestFixedNumIter : TestSubComponentBase
    {
        public override string name() => "FixedNumIter";
        public override string display_name() => "FixedNumIter";
        protected override Alpaca4d.Test.TestType TestType => Alpaca4d.Test.TestType.FixedNumIter;
        protected override bool HasTolerance => false;
    }
}
