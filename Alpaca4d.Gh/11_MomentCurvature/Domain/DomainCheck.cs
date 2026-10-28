using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Alpaca4d.Section;

namespace Alpaca4d.Gh
{
    public class DomainCheck : GH_Component
    {
        private static readonly List<string> Methods = new List<string> { "Constant N", "Proportional" };

        // One space before the name: second in its toolbar group, after Interaction Domain.
        public DomainCheck()
          : base(" Domain Check (Alpaca4d)", "Domain Check",
            "Checks design forces against an interaction domain: the safety factor of each combination, and the point where " +
            "it meets the boundary.\n" +
            "Constant N holds the axial force and scales the moments, as RC-SEC and most column checks do; Proportional scales " +
            "all three together, along the ray from the origin.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Domain", "Domain", "The domain, from Interaction Domain.", GH_ParamAccess.item);
            pManager.AddNumberParameter("N", "N", "Design axial force of each combination, in kN: tension positive, compression negative.", GH_ParamAccess.list, 0.0);
            pManager.AddNumberParameter("My", "My", "Design moment about the local y axis, in kN m, about the domain's centroid.", GH_ParamAccess.list, 0.0);
            pManager.AddNumberParameter("Mz", "Mz", "Design moment about the local z axis, in kN m, about the domain's centroid.", GH_ParamAccess.list, 0.0);
            pManager.AddTextParameter("Method", "Method",
                "\"Constant N\" holds N and scales the moments; \"Proportional\" scales N and the moments together.",
                GH_ParamAccess.item, Methods[0]);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_DoubleParam("SF", "SF", "Safety factor of each combination: how many times its forces fit in the domain, at least 1 when the section carries them. 0 when no scaling of them reaches the domain (see Notes).");
            pManager.Register_DoubleParam("Utilisation", "Utilisation", "1 / SF: at most 1 when the section carries the forces.");
            pManager.Register_DoubleParam("NRd", "NRd", "Axial force where the combination meets the boundary, in kN.");
            pManager.Register_DoubleParam("MyRd", "MyRd", "Moment about y where the combination meets the boundary, in kN m.");
            pManager.Register_DoubleParam("MzRd", "MzRd", "Moment about z where the combination meets the boundary, in kN m.");
            pManager.Register_BooleanParam("OK", "OK", "True where the section carries the combination.");
            pManager.Register_GenericParam("Checks", "Checks", "The checks, for the views to draw: Domain 3D, N-M Diagram and My-Mz Diagram.");
            pManager.Register_StringParam("Notes", "Notes", "Why a combination's factor is what it is, where that needs saying; empty otherwise.");
        }

        protected override void BeforeSolveInstance()
        {
            ValueListUtils.UpdateValueLists(this, 4, Methods);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object input = null;
            if (!DA.GetData(0, ref input))
                return;
            var domain = Unwrap(input);
            if (domain == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Domain needs the Domain output of Interaction Domain.");
                return;
            }

            if (!LicenseGate.Allows(this, "Domain Check"))
                return;

            var n = new List<double>();
            var my = new List<double>();
            var mz = new List<double>();
            DA.GetDataList(1, n);
            DA.GetDataList(2, my);
            DA.GetDataList(3, mz);

            string text = Methods[0];
            DA.GetData(4, ref text);
            DomainCheckMethod method;
            if (string.Equals(text?.Trim(), Methods[0], StringComparison.OrdinalIgnoreCase))
                method = DomainCheckMethod.ConstantAxial;
            else if (string.Equals(text?.Trim(), Methods[1], StringComparison.OrdinalIgnoreCase))
                method = DomainCheckMethod.Proportional;
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Method has to be \"{Methods[0]}\" or \"{Methods[1]}\", not \"{text}\".");
                return;
            }

            // The longest list sets how many combinations there are, and a shorter one repeats its
            // last value, as Grasshopper matches lists.
            int count = Math.Max(n.Count, Math.Max(my.Count, mz.Count));
            double At(List<double> list, int i) => list.Count == 0 ? 0.0 : list[Math.Min(i, list.Count - 1)];

            var checks = new Alpaca4d.Section.DomainCheck[count];
            Parallel.For(0, count, i => checks[i] = domain.Check(At(n, i), At(my, i), At(mz, i), method));

            DA.SetDataList(0, checks.Select(c => c.SafetyFactor));
            DA.SetDataList(1, checks.Select(c => c.Utilisation));
            DA.SetDataList(2, checks.Select(c => c.CapacityN));
            DA.SetDataList(3, checks.Select(c => c.CapacityMy));
            DA.SetDataList(4, checks.Select(c => c.CapacityMz));
            DA.SetDataList(5, checks.Select(c => c.IsSafe));
            DA.SetDataList(6, checks);
            DA.SetDataList(7, checks.Select(c => c.Note ?? ""));

            int unsafeCount = checks.Count(c => !c.IsSafe);
            if (unsafeCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{unsafeCount} of {count} combination{(count == 1 ? "" : "s")} {(unsafeCount == 1 ? "is" : "are")} outside the domain.");
            if (checks.Any(c => c.SafetyFactor == 0.0))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "A factor of 0 means no scaling of those forces reaches the domain; Notes says why.");
        }

        internal static Alpaca4d.Section.InteractionDomain Unwrap(object input)
        {
            object value = input is Grasshopper.Kernel.Types.IGH_Goo goo ? goo.ScriptVariable() : input;
            return value as Alpaca4d.Section.InteractionDomain;
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Domain_Check__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{82267846-9782-4E58-8628-383D5E0B4329}");
    }
}
