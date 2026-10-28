using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    public class InteractionDomain : GH_Component
    {
        // Two spaces before the name, one before Domain Check's: Grasshopper lays a toolbar group out
        // in the order of its components' names, and the domain comes first, its check second.
        public InteractionDomain()
          : base("  Interaction Domain (Alpaca4d)", "Interaction Domain",
            "The N-My-Mz resistance domain of a fibre section at the ultimate limit state: every combination of axial force and " +
            "biaxial bending it carries before a fibre passes its material's ultimate strain.\n" +
            "OpenSees traces each material's stress-strain curve; the ultimate strain planes are then swept through the section " +
            "the way Eurocode 2 6.1 pivots them - concrete at εcu, or εc2 at point C in full compression, everything else at εud.\n" +
            "Moments are about the area centroid of the fibres, as Moment Curvature's are.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("FiberSection", "FiberSection", "The section, from Fiber Section.", GH_ParamAccess.item);

            pManager.AddNumberParameter("εud", "εud",
                "Ultimate strain, either way, of every fibre that is not Concrete01: reinforcement and structural steel alike.\n" +
                "0.01 suits reinforcement. For a steel section, 0.05 gives the plastic resistance (Class 1 and 2) and fy/E the " +
                "elastic one (Class 3).\nA MinMax bound on the material holds instead where it is tighter.",
                GH_ParamAccess.item, 0.01);

            pManager.AddNumberParameter("εcu", "εcu",
                "Ultimate compressive strain of the concrete, positive.\nLeave it empty to take each Concrete01's own EpsilonCu.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddIntegerParameter("Levels", "Levels",
                "Levels of N the surface is drawn at between pure tension and pure compression, closer together towards both.",
                GH_ParamAccess.item, 20);

            pManager.AddIntegerParameter("Points", "Points",
                "Points on each level, before more are added where the domain turns sharply.",
                GH_ParamAccess.item, 72);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Domain", "Domain", "The domain. Feed it to Domain Check and to the views: Domain 3D, N-M Diagram and My-Mz Diagram.");
            pManager.Register_DoubleParam("N", "N", "Axial force round each level, in kN, tension positive: one branch a level, from tension to compression.");
            pManager.Register_DoubleParam("My", "My", "Moment about the local y axis round each level, in kN m.");
            pManager.Register_DoubleParam("Mz", "Mz", "Moment about the local z axis round each level, in kN m.");
            pManager.Register_DoubleParam("Nc", "Nc", "Axial resistance in compression, in kN: negative.");
            pManager.Register_DoubleParam("Nt", "Nt", "Axial resistance in tension, in kN: zero for plain concrete.");
            pManager.Register_PointParam("Centroid", "Centroid", "The point the moments are taken about: the area centroid of every fibre, steel included, where OpenSees takes them.");
            pManager.Register_StringParam("Materials", "Materials", "Each material, and the strains it is held to.");
            pManager.Register_GenericParam("log", "log", "The OpenSees deck that traced the materials, followed by the solver's console output.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Alpaca4d.Section.FiberSection section = null;
            if (!DA.GetData(0, ref section) || section == null)
                return;

            if (!LicenseGate.Allows(this, "Interaction Domain"))
                return;

            double steel = 0.01;
            DA.GetData(1, ref steel);

            double concrete = 0.0;
            bool hasConcrete = DA.GetData(2, ref concrete);

            int levels = 20;
            DA.GetData(3, ref levels);

            int points = 72;
            DA.GetData(4, ref points);

            var settings = new Alpaca4d.Section.DomainSettings
            {
                SteelUltimateStrain = steel,
                ConcreteUltimateStrain = hasConcrete ? concrete : (double?)null,
                Levels = levels,
                PointsPerLevel = points,
            };

            Alpaca4d.Section.SectionLayout layout;
            try
            {
                layout = Alpaca4d.Section.SectionLayout.Of(section);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var files = AnalysisFiles.For(this, DA);
            Func<string, (string output, string error, int exitCode)> run = deckPath => new Model { FileName = deckPath }.RunOpenSees();

            string deck = null, log = null;
            Alpaca4d.Section.InteractionDomain domain;
            try
            {
                domain = Alpaca4d.Section.InteractionDomain.Analyse(layout, settings, files.Folder,
                                                                    $"domain_materials_{files.Run}", run, out deck, out log);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                if (deck != null || log != null)
                    DA.SetData(8, (deck ?? "") + Environment.NewLine + (log ?? ""));
                return;
            }

            var n = new DataTree<double>();
            var my = new DataTree<double>();
            var mz = new DataTree<double>();
            for (int l = 0; l < domain.Grid.Length; l++)
            {
                var path = new GH_Path(l);
                n.AddRange(domain.Grid[l].Select(p => p.N), path);
                my.AddRange(domain.Grid[l].Select(p => p.My), path);
                mz.AddRange(domain.Grid[l].Select(p => p.Mz), path);
            }

            DA.SetData(0, domain);
            DA.SetDataTree(1, n);
            DA.SetDataTree(2, my);
            DA.SetDataTree(3, mz);
            DA.SetData(4, domain.Nc);
            DA.SetData(5, domain.Nt);
            DA.SetData(6, new Point3d(domain.CentroidY, domain.CentroidZ, 0.0));
            DA.SetDataList(7, domain.Materials.Select(m => m.ToString()));
            DA.SetData(8, deck + Environment.NewLine + log);

            if (domain.Nt <= 0.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "The section carries no tension, so the unloaded section is on the boundary: check it with N held.");
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Interaction_Domain__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{1ACCF37E-EA71-4C37-8D0D-1D0C55179BAD}");
    }
}
