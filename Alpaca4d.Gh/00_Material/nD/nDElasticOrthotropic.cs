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
    internal class nDElasticOrthotropic : SubComponent
    {
        public override string name() => "ElasticOrthotropic (Alpaca4d)";
        public override string display_name() => "ElasticOrthotropic";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Elastic Orthotropic Material");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Elastic_Orthotropic_Material__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_String(), "Material Name", "MatName", "", GH_ParamAccess.item, new GH_String(""));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Ex", "Ex", $"Young's Modulus X [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(210000000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Ey", "Ey", $"Young's Modulus Y [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(210000000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Ez", "Ez", $"Young's Modulus Z [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(210000000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Gxy", "Gxy", $"Shear Modulus XY [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(80760000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Gyz", "Gyz", $"Shear Modulus YZ [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(80760000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Gzx", "Gzx", $"Shear Modulus ZX [{Units.Force}/{Units.Length}²]", GH_ParamAccess.item, new GH_Number(80760000));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "NuXy", "NuXy", "Poisson's Ratio XY", GH_ParamAccess.item, new GH_Number(0.3));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "νYz", "νYz", "Poisson's Ratio YZ", GH_ParamAccess.item, new GH_Number(0.3));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "νZx", "νZx", "Poisson's Ratio ZX", GH_ParamAccess.item, new GH_Number(0.3));
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
            double ex = 210000000;
            double ey = 210000000;
            double ez = 210000000;
            double gxy = 80760000;
            double gyz = 80760000;
            double gzx = 80760000;
            double nuXy = 0.3;
            double nuYz = 0.3;
            double nuZx = 0.3;
            double rho = 7850;

            DA.GetData(0, ref matName);
            DA.GetData(1, ref ex);
            DA.GetData(2, ref ey);
            DA.GetData(3, ref ez);
            DA.GetData(4, ref gxy);
            DA.GetData(5, ref gyz);
            DA.GetData(6, ref gzx);
            DA.GetData(7, ref nuXy);
            DA.GetData(8, ref nuYz);
            DA.GetData(9, ref nuZx);
            DA.GetData(10, ref rho);

            var material = new Alpaca4d.Material.ElasticOrthotropicMaterial(matName, ex, ey, ez, gxy, gyz, gzx, nuXy, nuYz, nuZx, rho);
            material.Color = ColourInput.Read(DA, 11);

            DA.SetData(0, material);
        }
    }
}