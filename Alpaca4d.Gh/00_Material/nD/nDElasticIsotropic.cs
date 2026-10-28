using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    internal class nDElasticIsotropic : SubComponent
    {
        public override string name() => "ElasticIsotropic (Alpaca4d)";
        public override string display_name() => "ElasticIsotropic";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Elastic Isotropic Material");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Elastic_Isotropic_Material__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_String(), "Material Name", "MatName", "", GH_ParamAccess.item, new GH_String(""));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "E", "E", $"Young's Modulus [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(210000000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "G", "G", $"Shear Modulus [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(80760000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "ν", "ν", "Poisson's Ratio", GH_ParamAccess.item, new GH_Number(0.3));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Rho", "Rho", $"Density [{Units.Mass}/{Units.Length}³]", GH_ParamAccess.item, new GH_Number(7850));
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
            double g = 80760000;
            double nu = 0.3;
            double rho = 7850;

            DA.GetData(0, ref matName);
            DA.GetData(1, ref e);
            DA.GetData(2, ref g);
            DA.GetData(3, ref nu);
            DA.GetData(4, ref rho);

            var material = new Alpaca4d.Material.ElasticIsotropicMaterial(matName, e, g, nu, rho);
            material.Color = ColourInput.Read(DA, 5);

            DA.SetData(0, material);
        }
    }
}