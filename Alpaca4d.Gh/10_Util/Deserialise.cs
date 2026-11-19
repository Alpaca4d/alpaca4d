using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
	public class Deserialize : GH_Component
	{
		public Deserialize()
		  : base("Deserialise (Alpaca4d)", "Deserialize",
			"Read an OpenSees .tcl file back into an Alpaca model, ready to view and to analyse. " +
			"Not every OpenSees command has an Alpaca equivalent; whatever cannot be read is reported " +
			"on the component and left out of the model.",
			"Alpaca4d", "10_Utility")
		{
			// Draw a Description Underneath the component
			this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
		}

		/// <summary>
		/// Registers all the input parameters for this component.
		/// </summary>
		protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
		{
			pManager.AddTextParameter("FilePath", "FilePath", "Path of the .tcl file to read.", GH_ParamAccess.item);
			pManager[pManager.ParamCount - 1].Optional = true;
			pManager.AddTextParameter("Text", "Text", "The content of a .tcl file, as text. Used when no FilePath is given.", GH_ParamAccess.list);
			pManager[pManager.ParamCount - 1].Optional = true;
			pManager.AddNumberParameter("Tolerance", "Tolerance", "Distance below which two node positions are treated as the same node.", GH_ParamAccess.item, 0.01);
			pManager[pManager.ParamCount - 1].Optional = true;
		}

		/// <summary>
		/// Registers all the output parameters for this component.
		/// </summary>
		protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
		{
			pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The assembled model. Plug it into Model View to see it, or into Run Analysis to solve it.");
		}

		/// <summary>
		/// This is the method that actually does the work.
		/// </summary>
		/// <param name="DA">The DA object can be used to retrieve data from input parameters and 
		/// to store data in output parameters.</param>
		protected override void SolveInstance(IGH_DataAccess DA)
		{
			string filePath = null;
			DA.GetData(0, ref filePath);

			var textFile = new List<string>();
			DA.GetDataList(1, textFile);

			double tolerance = 0.01;
			DA.GetData(2, ref tolerance);

			bool hasFilePath = !string.IsNullOrWhiteSpace(filePath);
			bool hasText = textFile.Any(line => !string.IsNullOrWhiteSpace(line));

			if (!hasFilePath && !hasText)
			{
				AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give either a FilePath or the Text of a .tcl file.");
				return;
			}

			TclReader reader;
			try
			{
				reader = hasFilePath
					? Alpaca4d.TclReader.ReadFile(filePath, tolerance)
					: Alpaca4d.TclReader.ReadText(textFile, tolerance);
			}
			catch (Exception ex)
			{
				AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
				return;
			}

			foreach (var warning in reader.Warnings)
				AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

			DA.SetData(0, reader.Model);
		}

		public override GH_Exposure Exposure => GH_Exposure.quarternary;
		protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Deserialize__Alpaca4d_;
		public override Guid ComponentGuid => new Guid("{D5309B00-6784-42E9-9219-1B68B6D3320F}");
	}
}
