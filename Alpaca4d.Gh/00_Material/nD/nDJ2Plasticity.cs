using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A metal that yields: von Mises plasticity, OpenSees' J2Plasticity. See
    /// <see cref="Alpaca4d.Material.J2PlasticityMaterial"/> for what it writes and why.
    /// </summary>
    internal class nDJ2Plasticity : SubComponent
    {
        public override string name() => "J2Plasticity (Alpaca4d)";
        public override string display_name() => "J2Plasticity";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(),
                "A metal that yields - von Mises plasticity. Elastic until the von Mises stress reaches Fy, then it yields, " +
                "with the yield stress rising by H per unit of plastic strain. For steel and aluminium.\n" +
                "Works best on Ten Node Tetrahedra; on shells, through a Layered Shell section. An SSP Brick is right up to " +
                "first yield and too strong after it.\n" +
                "Solve it with Newton and the load in steps: a Linear Time Series in the Load Pattern, and Load Control of 0.05 with an Analysis Step of 20, say.");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.J2_Plasticity_Material__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_String(), "Material Name", "MatName", "", GH_ParamAccess.item, new GH_String(""));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "E", "E", $"Young's Modulus [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(210000000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "ν", "ν", "Poisson's Ratio. The shear modulus follows from E and ν.", GH_ParamAccess.item, new GH_Number(0.3));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Fy", "Fy",
                $"Yield stress: the von Mises stress the material starts to yield at [{Units.Force}/{Units.Length}²]. 355000 is S355.",
                GH_ParamAccess.item, new GH_Number(355000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "H", "H",
                $"Hardening modulus: how much the yield stress rises per unit of plastic strain [{Units.Force}/{Units.Length}²]. " +
                "0 is perfectly plastic: past Fy the stress stays at Fy.",
                GH_ParamAccess.item, new GH_Number(0.0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Rho", "Rho",
                $"Density [{Units.Mass}/{Units.Length}³]. Gravity Load reads it for the self-weight. OpenSees takes no density " +
                "for this material, so these elements have no mass in a natural vibration or a time history.",
                GH_ParamAccess.item, new GH_Number(7850));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            // Last, so the inputs before it keep their indices.
            evaluationUnit.RegisterInputParam(new Param_Colour(), ColourInput.Name, ColourInput.Name, ColourInput.MaterialDescription, GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            string matName = null;
            double e = 210000000;
            double nu = 0.3;
            double fy = 355000;
            double h = 0.0;
            double rho = 7850;

            DA.GetData(0, ref matName);
            DA.GetData(1, ref e);
            DA.GetData(2, ref nu);
            DA.GetData(3, ref fy);
            DA.GetData(4, ref h);
            DA.GetData(5, ref rho);

            Alpaca4d.Material.J2PlasticityMaterial material;
            try
            {
                material = new Alpaca4d.Material.J2PlasticityMaterial(matName, e, nu, fy, h, rho);
            }
            catch (ArgumentException ex)
            {
                msg = ex.Message;
                level = GH_RuntimeMessageLevel.Error;
                return;
            }

            material.Color = ColourInput.Read(DA, 6);

            DA.SetData(0, material);
        }
    }
}
