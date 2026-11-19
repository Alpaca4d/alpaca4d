using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alpaca4d.Menu;

namespace Alpaca4d.Gh
{
    public class Info : GH_AssemblyInfo
    {
        public override string Name => "Alpaca4d";
        public override Bitmap Icon => null;
        public override string Description => "Alpaca4d is a Grasshopper plugin which has been developed on top of OpenSees. It lets you analyse beam, shell and brick elements through Static, Dynamic, Linear and Not Linear Analysis.";
        public override Guid Id => new Guid("52A09029-AC68-4356-803D-724D05588549");
        public override string AuthorName => "Marco Pellegrino";
        public override string AuthorContact => "pellegrino.marco@icloud.com";
        public override string AssemblyVersion
        {
            get
            {
                IEnumerable<AssemblyName> assembly = Assembly.GetExecutingAssembly().GetReferencedAssemblies().Where(x => x.Name.Contains("Alpaca4d.Core"));
                string assemblyVersion = assembly.First().Version?.ToString();
                return assemblyVersion;
            }
        }
    }

    public class Alpaca4dCategoryIcon : Grasshopper.Kernel.GH_AssemblyPriority
    {
        public override Grasshopper.Kernel.GH_LoadingInstruction PriorityLoad()
        {
            Instances.CanvasCreated += MenuLoad.OnStartup;

            // Files saved by an older Alpaca4d hold its parameter descriptions: show this version's.
            ParameterDescriptions.Watch();
            Instances.CanvasCreated += canvas => ParameterDescriptions.Watch();

            Grasshopper.Instances.ComponentServer.AddCategoryIcon("Alpaca4d", Alpaca4d.Gh.Properties.Resources.Logo);
            Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Alpaca4d", 'A');
            return Grasshopper.Kernel.GH_LoadingInstruction.Proceed;
        }
    }
}