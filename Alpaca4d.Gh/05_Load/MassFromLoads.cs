using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Turns the weight of loads into mass, for whatever solves the modes - Natural Vibration, and
    /// Footfall Analysis and response spectra after it - or shakes the model in time. The structure's
    /// own weight is mass already, from the density of its materials; this adds what it carries: the
    /// superimposed dead load and some of the imposed one, by a factor per kind of load.
    /// </summary>
    public class MassFromLoadsComponent : GH_Component
    {
        public MassFromLoadsComponent()
          : base("Mass from Loads (Alpaca4d)", "Mass from Loads",
            "Makes the weight of loads into mass, on the nodes the loads land on. The structure's own weight is already " +
            "mass, through the density of its materials; this adds what it carries - screed, finishes, partitions, " +
            "services and a share of the imposed load - for Natural Vibration and what reads its modes.\n" +
            "For a footfall check SCI P354 takes the superimposed dead load in full and about 10% of the imposed load. For " +
            "an earthquake NTC 2018 and EN 1998-1 3.2.4 take the permanent loads in full and ψ2 of the variable ones.\n" +
            "Name the Load Patterns whose loads are to be mass - by the Name you gave each one - with the factor for them: " +
            "\"Finishes\" at 1, \"Imposed\" at 0.1. Assemble Model looks the names up among the patterns it is given. " +
            "Or give the patterns, or the loads, themselves.\n" +
            "Only the downward part of a load weighs, and it is mass along every axis. One component per factor, all into " +
            "Assemble Model; or a tree, one branch per factor.",
            "Alpaca4d", "05_Load")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(PatternsInput, PatternsInput,
                "The names of the Load Patterns whose loads become mass, as their Name input has them - Imposed - looked up " +
                "among the patterns given to Assemble Model, whatever their case. Or the Load Patterns themselves, or Point, " +
                "Linear and Mesh loads.\n" +
                "The loads are taken at their own size: a pattern's Factor is not applied, Factor here is. Gravity Load is " +
                "left out.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Factor", "Factor",
                "How much of their weight is mass: 1 for permanent loads.\n" +
                "Footfall (SCI P354): about 0.1 of the imposed load.\n" +
                "Earthquake: ψ2 of a variable load - 0.3 for homes and offices, 0.6 for crowds and shops, 0.8 for storage, " +
                "0 for roofs and for snow below 1000 m. EN 1998-1 4.2.4 takes φ ψ2 instead.",
                GH_ParamAccess.item, 1.0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Mass", "Mass",
                "The mass. Feed it to the LoadPatterns input of Assemble Model, beside the load patterns, as a Mass Point. " +
                "Assemble Model's Mass output then counts it.");
        }

        private const string PatternsInput = "Load Patterns";

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<IGH_Goo>();
            double factor = 1.0;
            if (!DA.GetDataList(0, items)) return;
            DA.GetData(1, ref factor);

            if (!(factor >= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Factor is {factor}. It is zero or more: 1 for permanent loads, a share for variable ones.");
                return;
            }

            var loads = new List<ILoad>();
            var names = new List<string>();
            foreach (var goo in items)
            {
                switch (goo?.ScriptVariable())
                {
                    case string name:
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name.Trim());
                        break;
                    case Alpaca4d.Loads.LoadPattern pattern:
                        loads.AddRange(pattern.Load ?? new List<ILoad>());
                        break;
                    case ILoad load:
                        loads.Add(load);
                        break;
                    case null:
                        break;
                    case var unknown:
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{unknown.GetType().Name} is not a load. Give the names of load patterns, the patterns themselves, or point, linear or mesh loads.");
                        return;
                }
            }

            var kept = new List<ILoad>();
            int gravity = 0, local = 0, other = 0, notDown = 0, partly = 0;
            foreach (var load in loads)
            {
                Rhino.Geometry.Vector3d force;
                switch (load)
                {
                    case Alpaca4d.Loads.Gravity _:
                        gravity++;
                        continue;
                    case Alpaca4d.Loads.LineLoad line when line.IsLocal:
                    case Alpaca4d.Loads.MeshLoad mesh when mesh.IsLocal:
                        local++;
                        continue;
                    case Alpaca4d.Loads.PointLoad point:
                        force = point.Force;
                        break;
                    case Alpaca4d.Loads.LineLoad line:
                        force = line.GlobalForce;
                        break;
                    case Alpaca4d.Loads.MeshLoad mesh:
                        force = mesh.GlobalForce;
                        break;
                    default:
                        other++;
                        continue;
                }

                // A load with no downward part weighs nothing; one that also pushes sideways weighs its downward part.
                if (!(force.Z < 0.0))
                {
                    notDown++;
                    continue;
                }
                if (force.X != 0.0 || force.Y != 0.0)
                    partly++;
                kept.Add(load);
            }

            if (gravity > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Gravity Load is left out: the elements are already as heavy as the density of their materials makes them.");
            if (local > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{local} load{(local == 1 ? " is" : "s are")} in local axes and left out.");
            if (other > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{other} load{(other == 1 ? " is" : "s are")} not a point, linear or mesh load and left out.");
            if (notDown > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{notDown} load{(notDown == 1 ? " pushes" : "s push")} up or only sideways, so weigh{(notDown == 1 ? "s" : "")} nothing, and {(notDown == 1 ? "is" : "are")} left out.");
            if (partly > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{partly} load{(partly == 1 ? " pushes" : "s push")} sideways as well as down. Only the downward part weighs.");
            if (kept.Count == 0 && names.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No load here weighs anything, so there is no mass.");
                return;
            }

            // The names are looked up by Assemble, which has the patterns: what it cannot find, it says.
            DA.SetData(0, new Alpaca4d.Loads.MassFromLoads(kept, factor, names));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Mass_from_Loads__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{D47E2A90-6B15-4F83-A2C6-91E8B04F5D27}");
    }
}
