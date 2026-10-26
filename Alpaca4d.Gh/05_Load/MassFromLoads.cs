using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Turns the weight of loads into mass, for a natural vibration or response spectrum analysis: the
    /// G2 + Σ ψ2 Qk of the seismic mass, the structure's own G1 being in the elements already.
    /// </summary>
    public class MassFromLoadsComponent : GH_Component
    {
        public MassFromLoadsComponent()
          : base("Mass from Loads (Alpaca4d)", "Mass from Loads",
            "Makes the weight of loads into mass, on the nodes the loads land on: the permanent loads and ψ2 of the " +
            "variable ones, as NTC 2018 3.2.4 and EN 1998-1 3.2.4 make up the seismic mass. The structure's own weight " +
            "is already mass, through the density of its materials.\n" +
            "Only the downward part of a load weighs, and it is mass along every axis. One component per kind of load, " +
            "each with its factor; or a tree of loads, one branch per kind, with a factor per branch.",
            "Alpaca4d", "05_Load")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Loads", "Loads",
                "Point, Linear and Mesh loads, or Load Patterns, whose loads are taken at their own size: a pattern's " +
                "Factor is not applied, Factor here is. Gravity Load is left out.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Factor", "Factor",
                "How much of their weight is mass: 1 for permanent loads, ψ2 for variable ones - 0.3 for homes and " +
                "offices, 0.6 for crowds and shops, 0.8 for storage, 0 for roofs and for snow below 1000 m. EN 1998-1 " +
                "4.2.4 takes φ ψ2 instead.",
                GH_ParamAccess.item, 1.0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Mass", "Mass",
                "The mass. Feed it to the LoadPatterns input of Assemble Model, beside the load patterns, as a Mass Point. " +
                "Assemble Model's Mass output then counts it.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<IGH_Goo>();
            double factor = 1.0;
            if (!DA.GetDataList(0, items)) return;
            DA.GetData(1, ref factor);

            if (!(factor >= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Factor is {factor}. It is zero or more: 1 for permanent loads, ψ2 for variable ones.");
                return;
            }

            var loads = new List<ILoad>();
            foreach (var goo in items)
            {
                switch (goo?.ScriptVariable())
                {
                    case Alpaca4d.Loads.LoadPattern pattern:
                        loads.AddRange(pattern.Load ?? new List<ILoad>());
                        break;
                    case ILoad load:
                        loads.Add(load);
                        break;
                    case null:
                        break;
                    case var unknown:
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{unknown.GetType().Name} is not a load. Give point, linear or mesh loads, or load patterns.");
                        return;
                }
            }

            var kept = new List<ILoad>();
            int gravity = 0, local = 0, other = 0, notDown = 0;
            foreach (var load in loads)
            {
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
                        if (point.Force.X != 0.0 || point.Force.Y != 0.0 || point.Force.Z > 0.0) notDown++;
                        break;
                    case Alpaca4d.Loads.LineLoad line:
                        if (line.GlobalForce.X != 0.0 || line.GlobalForce.Y != 0.0 || line.GlobalForce.Z > 0.0) notDown++;
                        break;
                    case Alpaca4d.Loads.MeshLoad mesh:
                        if (mesh.GlobalForce.X != 0.0 || mesh.GlobalForce.Y != 0.0 || mesh.GlobalForce.Z > 0.0) notDown++;
                        break;
                    default:
                        other++;
                        continue;
                }
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
                    $"{notDown} load{(notDown == 1 ? " pushes" : "s push")} up or sideways as well as down. Only the downward part weighs.");
            if (kept.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No load here weighs anything, so there is no mass.");
                return;
            }

            DA.SetData(0, new Alpaca4d.Loads.MassFromLoads(kept, factor));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Mass_from_Loads__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{D47E2A90-6B15-4F83-A2C6-91E8B04F5D27}");
    }
}
