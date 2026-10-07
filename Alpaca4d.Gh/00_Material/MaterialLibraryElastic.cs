using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Reflection;
using static Alpaca4d.Gh.ComponentMessage;
using GH_IO.Serialization;

namespace Alpaca4d.Gh
{
	public class MaterialLibraryElastic : GH_ExtendableComponent
	{
		private MenuDropDown modelDrop;
		private MenuDropDown typeDrop;
		private MenuDropDown gradeDrop;

		private Dictionary<string, List<string>> typeToGrades = new Dictionary<string, List<string>>();
		private JObject customDb;
		private string lastCustomDbPath;
		private DateTime lastCustomDbWriteTimeUtc;

		private static readonly object jsonLock = new object();
		private static JObject steelDb;
		private static JObject concreteDb;
		private static JObject timberDb;
		private static JObject plasticDb;

		// Persisted selection state (for GH file save/load)
		private string storedType = "Steel";
		private string storedGrade = "S235";
		private string storedModel = "Uniaxial";

		/// <summary>
		/// The first entry of the Grade list, asking for every grade of the type rather than one: what
		/// an optimisation wants to pick from, with List Item and a slider.
		/// </summary>
		private const string All = "All";

		private bool TryLoadCustomDb(string path)
		{
			// Returns true only when the effective DB source changed (loaded, reloaded, or cleared)
			if (string.IsNullOrWhiteSpace(path))
			{
				if (customDb != null)
				{
					customDb = null;
					lastCustomDbPath = null;
					lastCustomDbWriteTimeUtc = DateTime.MinValue;
					return true;
				}
				return false;
			}
			try
			{
				if (!File.Exists(path))
				{
					AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Custom material database not found: {path}");
					if (customDb != null)
					{
						customDb = null;
						lastCustomDbPath = null;
						lastCustomDbWriteTimeUtc = DateTime.MinValue;
						return true;
					}
					return false;
				}
				var fi = new FileInfo(path);
				if (string.Equals(path, lastCustomDbPath, StringComparison.OrdinalIgnoreCase) &&
					fi.LastWriteTimeUtc == lastCustomDbWriteTimeUtc)
				{
					// No change
					return false;
				}
				var json = File.ReadAllText(path);
				var parsed = JObject.Parse(json);
				// Accept and record
				customDb = parsed;
				lastCustomDbPath = path;
				lastCustomDbWriteTimeUtc = fi.LastWriteTimeUtc;
				return true;
			}
			catch (Exception ex)
			{
				AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to load custom material DB: {ex.Message}");
				return false;
			}
		}

		private static JObject LoadJsonResourceOnce(ref JObject cache, string resourceName)
		{
			if (cache != null) return cache;
			lock (jsonLock)
			{
				if (cache != null) return cache;
				var coreAsm = typeof(Alpaca4d.Material.ElasticIsotropicMaterial).Assembly;
				using (var stream = coreAsm.GetManifestResourceStream(resourceName))
				{
					if (stream == null) throw new FileNotFoundException("Embedded resource not found", resourceName);
					using (var reader = new StreamReader(stream))
					{
						var json = reader.ReadToEnd();
						cache = JObject.Parse(json);
						return cache;
					}
				}
			}
		}

		private static double TryGetDouble(JObject obj, string key)
		{
			if (obj == null) return double.NaN;
			var token = obj[key];
			return token != null ? token.Value<double>() : double.NaN;
		}

		// Convert MPa to kN/m^2 (1 MPa = 1000 kN/m^2)
		private static double MPaTokN_m2(double xMpa) => double.IsNaN(xMpa) ? double.NaN : xMpa * 1000.0;

		private static double ComputeNuFromEG(double e, double g)
		{
			if (g <= 0 || e <= 0) return double.NaN;
			var nu = (e / (2.0 * g)) - 1.0;
			// Clamp to a physical range
			if (double.IsNaN(nu) || double.IsInfinity(nu)) return double.NaN;
			return Math.Max(0.0, Math.Min(0.4999, nu));
		}

		public override Guid ComponentGuid => new Guid("{F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6}");
		public override GH_Exposure Exposure => GH_Exposure.secondary;
		protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Material_Library__Alpaca4d_;

		public MaterialLibraryElastic()
			: base("Material Library Elastic (Alpaca4d)", "Material Library Elastic",
			  "Select a material type and grade to create an Elastic Material",
			  "Alpaca4d", "00_Material")
        {
            // Draw a Description Underneath the component
            this.Message = MyMessage(this);
        }

	protected override void RegisterInputParams(GH_InputParamManager pManager)
	{
		// Grade name input (optional) - allows user to type grade name directly
		pManager.AddTextParameter("Material Name", "Name", "Type grade name directly (e.g., 'S235', 'C30/37'). Overrides dropdown selection.", GH_ParamAccess.item);
		pManager[pManager.ParamCount - 1].Optional = true;
		
		pManager.AddGenericParameter("DatabasePath", "DBPath", "Optional custom material database JSON (steel_properties schema).", GH_ParamAccess.item);
		pManager[pManager.ParamCount - 1].Optional = true;
	}

		protected override void RegisterOutputParams(GH_OutputParamManager pManager)
		{
			pManager.Register_GenericParam("Material", "Material", "Material. With Grade set to All, every grade of the type as a list, in the database's order - weakest first for steel.");
		}

		protected override void Setup(GH_ExtendableComponentAttributes attr)
		{
			var menu = new GH_ExtendableMenu(0, "Library");
			menu.Name = "Library";
			menu.Header = "Select model, type and grade";

			var panel = new MenuPanel(0, "Library");
			panel.Header = "Library";

			typeDrop = new MenuDropDown(0, "Type", "Type");
			typeDrop.VisibleItemCount = 6;
			typeDrop.ValueChanged += OnTypeChanged;

			gradeDrop = new MenuDropDown(1, "Grade", "Grade");
			gradeDrop.VisibleItemCount = 8;
			gradeDrop.ValueChanged += OnGradeChanged;
			
			modelDrop = new MenuDropDown(2, "Model", "Model");
			modelDrop.VisibleItemCount = 2;
			modelDrop.ValueChanged += OnModelChanged;

			panel.AddControl(typeDrop);
			panel.AddControl(gradeDrop);
			panel.AddControl(modelDrop);
			menu.AddControl(panel);
			menu.Expand();
			attr.AddMenu(menu);
			attr.MinWidth = 160f;

			// Populate dropdown items
			InitializeModelOptions();
			InitializeTypeOptions();

			// Apply any stored selections (for loaded components) and
			// also build the grade list for the selected type.
			RestoreSelectionsFromStoredValues();
		}

		protected override void OnComponentLoaded()
		{
			base.OnComponentLoaded();

			// Rebuild dropdown contents and then reapply the stored selection
			// that we read in from the GH file.
			InitializeModelOptions();
			InitializeTypeOptions();
			RestoreSelectionsFromStoredValues();
		}

		private void OnModelChanged(object sender, EventArgs e)
		{
			this.ExpireSolution(true);
		}

		private void OnTypeChanged(object sender, EventArgs e)
		{
			InitializeGradeOptions();
			this.ExpireSolution(true);
		}

		private void OnGradeChanged(object sender, EventArgs e)
		{
			this.ExpireSolution(true);
		}

		private void InitializeModelOptions()
		{
			if (modelDrop == null) return;
			modelDrop.Clear();
			// Keep default as nD to preserve previous behavior
			modelDrop.AddItem("Uniaxial", "Uniaxial");
			modelDrop.AddItem("nD", "nD");
		}

		private void InitializeTypeOptions()
		{
			// Build the dynamic dictionary from embedded JSON resources
			typeToGrades = BuildTypeToGrades();

			// Repopulate type dropdown
			if (typeDrop == null) return;
			typeDrop.Clear();
			var types = typeToGrades.Keys.ToList();
			for (int i = 0; i < types.Count; i++)
			{
				var t = types[i];
				typeDrop.AddItem(t, t);
			}
		}

		private Dictionary<string, List<string>> BuildTypeToGrades()
		{
			var result = new Dictionary<string, List<string>>();

			void addFromDb(JObject db)
			{
				if (db == null) return;
				// Discover material_type from first entry and collect all names (keys)
				var first = db.Properties().Select(p => p.Value as JObject).FirstOrDefault(v => v != null);
				if (first == null) return;
				var matType = first["material_type"]?.Value<string>() ?? "Custom";
				var names = db.Properties().Select(p => p.Name).ToList();
				if (names.Count == 0) return;
				if (!result.ContainsKey(matType)) result[matType] = new List<string>();
				foreach (var n in names)
				{
					if (!result[matType].Contains(n)) result[matType].Add(n);
				}
			}

			// Load each DB as needed
			try { addFromDb(LoadJsonResourceOnce(ref steelDb, "Alpaca4d.Resources.Material.steel_properties.json")); } catch { }
			try { addFromDb(LoadJsonResourceOnce(ref concreteDb, "Alpaca4d.Resources.Material.concrete_properties.json")); } catch { }
			try { addFromDb(LoadJsonResourceOnce(ref timberDb, "Alpaca4d.Resources.Material.timber_properties.json")); } catch { }
			try { addFromDb(LoadJsonResourceOnce(ref plasticDb, "Alpaca4d.Resources.Material.plastic_properties.json")); } catch { }
			// Add from custom DB if provided
			try { addFromDb(customDb); } catch { }

			return result;
		}

		private void InitializeGradeOptions()
		{
			if (typeDrop == null || gradeDrop == null) return;
			string selType = GetSelected(typeDrop, defaultName: "Steel");
			string previous = gradeDrop.Items.Count > 0 ? GetSelected(gradeDrop, null) : null;
			gradeDrop.Clear();
			gradeDrop.AddItem(All, All);
			if (typeToGrades.TryGetValue(selType, out var grades))
			{
				for (int i = 0; i < grades.Count; i++)
				{
					var g = grades[i];
					gradeDrop.AddItem(g, g);
				}
			}

			// What was chosen, if the new list still has it - a custom database reloading keeps the
			// grade, and All stays All across a change of type. Otherwise the first grade, not All,
			// so a new component or a new type gives one material as it always did.
			int keep = previous != null ? gradeDrop.FindIndex(previous) : -1;
			gradeDrop.Value = keep >= 0 ? keep : (gradeDrop.Items.Count > 1 ? 1 : 0);
		}

		/// <summary>
		/// Apply the stored selection (type/grade/model) to the dropdowns after they
		/// have been populated. Safe to call when any of the dropdowns are null.
		/// </summary>
		private void RestoreSelectionsFromStoredValues()
		{
			// Restore model selection
			if (modelDrop != null && !string.IsNullOrEmpty(storedModel))
			{
				int modelIdx = modelDrop.FindIndex(storedModel);
				if (modelIdx >= 0)
					modelDrop.Value = modelIdx;
			}

			// Restore type selection
			if (typeDrop != null && !string.IsNullOrEmpty(storedType))
			{
				int typeIdx = typeDrop.FindIndex(storedType);
				if (typeIdx >= 0)
					typeDrop.Value = typeIdx;
			}

			// Rebuild and restore grade selection based on the (possibly updated) type
			if (gradeDrop != null)
			{
				InitializeGradeOptions();

				if (!string.IsNullOrEmpty(storedGrade))
				{
					int gradeIdx = gradeDrop.FindIndex(storedGrade);
					if (gradeIdx >= 0)
						gradeDrop.Value = gradeIdx;
				}
			}
		}

		private static string GetSelected(MenuDropDown dd, string defaultName)
		{
			if (dd.Items.Count == 0) return defaultName;
			int idx = Math.Max(0, Math.Min(dd.Value, dd.Items.Count - 1));
			return dd.Items[idx].name ?? dd.Items[idx].content ?? defaultName;
		}

		private static string GetMaterialTypeFromDb(JObject db)
		{
			if (db == null) return null;
			var first = db.Properties().Select(p => p.Value as JObject).FirstOrDefault(v => v != null);
			return first?["material_type"]?.Value<string>();
		}

		private void GetParametersFromDb(JObject db, string grade, out double E, out double nu, out double rho)
		{
			SetDefaultElastic(out E, out nu, out rho);
			if (db == null) return;
			var entry = db?[grade] as JObject;
			if (entry == null) return;

			var eMpa = TryGetDouble(entry, "E");
			var gMpa = TryGetDouble(entry, "G");
			var rhoVal = TryGetDouble(entry, "rho");

			if (!double.IsNaN(eMpa)) E = MPaTokN_m2(eMpa);
			if (!double.IsNaN(rhoVal)) rho = rhoVal;
			var nuComputed = ComputeNuFromEG(MPaTokN_m2(eMpa), MPaTokN_m2(gMpa));
			if (!double.IsNaN(nuComputed)) nu = nuComputed;
		}

	protected override void SolveInstance(IGH_DataAccess DA)
	{
		// Grade Name input (optional) - typed grade name overrides dropdown
		string typedGradeName = null;
		try { DA.GetData(0, ref typedGradeName); } catch { }

		// 1) Try to load/refresh custom DB if provided
		string customPath = null;
		if (Params.Input.Count > 1)
		{
			try { DA.GetData(1, ref customPath); } catch { customPath = null; }
		}
		var dbChanged = TryLoadCustomDb(customPath);
		if (dbChanged)
		{
			// Rebuild types/grades when DB source changes
			InitializeTypeOptions();
			InitializeGradeOptions();
		}

		// Current selections from UI
		string selType = typeDrop != null ? GetSelected(typeDrop, "Steel") : "Steel";
		string selGrade = gradeDrop != null ? GetSelected(gradeDrop, "S235") : "S235";
		string selModel = modelDrop != null ? GetSelected(modelDrop, "nD") : "nD";

		// If user provided a typed grade name, use that and update the dropdowns
		if (!string.IsNullOrWhiteSpace(typedGradeName))
		{
			var searchResult = FindGradeInDatabase(typedGradeName.Trim());
			if (searchResult.found)
			{
				selType = searchResult.type;
				selGrade = searchResult.gradeName;
				
				// Update the UI dropdowns to reflect the typed input
				UpdateDropdownsFromTypedInput(selType, selGrade);
			}
			else
			{
				AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
					$"Material grade '{typedGradeName}' not found in the material database. Please check the spelling or use the dropdown menu.");
				return;
			}
		}

		// Keep the persisted selection state in sync with the UI
		storedType = selType;
		storedGrade = selGrade;
		storedModel = selModel;

		if (selGrade == All)
		{
			var grades = typeToGrades.TryGetValue(selType, out var names) ? names : new List<string>();
			DA.SetDataList(0, grades.Select(grade => CreateMaterial(selType, grade, selModel)));
			return;
		}

		DA.SetData(0, CreateMaterial(selType, selGrade, selModel));
	}

	/// <summary>One grade as the material the Model dropdown asks for, in kN and m.</summary>
	private object CreateMaterial(string type, string grade, string model)
	{
		double E, nu, rho;
		GetElasticParameters(type, grade, out E, out nu, out rho);
		double G = E / (2.0 * (1.0 + nu));

		if (string.Equals(model, "Uniaxial", StringComparison.OrdinalIgnoreCase))
		{
			double eNeg = E;
			double eta = 0.0;
			var material = new Alpaca4d.Material.UniaxialMaterialElastic(grade, E, eNeg, eta, G, nu, rho);
			material.Grade = DesignGradeOf(type, grade);
			return material;
		}

		return new Alpaca4d.Material.ElasticIsotropicMaterial(grade, E, G, nu, rho);
	}

		/// <summary>
		/// What the selection is for design - its family and strengths - from whichever database it came
		/// out of, for the Utilisation component to check it against. Only steel has one so far.
		/// </summary>
		private Alpaca4d.Material.IDesignGrade DesignGradeOf(string type, string grade)
		{
			JObject entry = null;
			var customType = GetMaterialTypeFromDb(customDb);
			if (!string.IsNullOrEmpty(customType) && string.Equals(type, customType, StringComparison.OrdinalIgnoreCase))
				entry = customDb?[grade] as JObject;
			else if (string.Equals(type, "Steel", StringComparison.OrdinalIgnoreCase))
				entry = LoadJsonResourceOnce(ref steelDb, "Alpaca4d.Resources.Material.steel_properties.json")?[grade] as JObject;

			return Alpaca4d.Material.SteelGrade.FromEntry(grade, entry);
		}

		private void GetElasticParameters(string type, string grade, out double E, out double nu, out double rho)
		{
			SetDefaultElastic(out E, out nu, out rho);
			// If custom DB is present and matches the selected material type, prefer it
			var customType = GetMaterialTypeFromDb(customDb);
			if (!string.IsNullOrEmpty(customType) && string.Equals(type, customType, StringComparison.OrdinalIgnoreCase))
			{
				GetParametersFromDb(customDb, grade, out E, out nu, out rho);
				return;
			}
			switch (type)
			{
				case "Steel":
					GetSteelParameters(grade, out E, out nu, out rho);
					break;
				case "Concrete":
					GetConcreteParameters(grade, out E, out nu, out rho);
					break;
				case "Timber":
					GetTimberParameters(grade, out E, out nu, out rho);
					break;
				case "Plastic":
					GetPlasticParameters(grade, out E, out nu, out rho);
					break;
				default:
					// keep defaults
					break;
			}
		}

		private static void SetDefaultElastic(out double E, out double nu, out double rho)
		{
			// Sensible defaults if JSON lookups fail
			E = 210000000;   // kN/m^2
			nu = 0.30;
			rho = 7850;      // kg/m^3
		}

		private void GetSteelParameters(string grade, out double E, out double nu, out double rho)
		{
			SetDefaultElastic(out E, out nu, out rho);
			var db = LoadJsonResourceOnce(ref steelDb, "Alpaca4d.Resources.Material.steel_properties.json");
			var entry = db?[grade] as JObject;
			if (entry == null) return;

			var eMpa = TryGetDouble(entry, "E");
			var gMpa = TryGetDouble(entry, "G");
			var rhoVal = TryGetDouble(entry, "rho");
			if (!double.IsNaN(eMpa)) E = MPaTokN_m2(eMpa);
			if (!double.IsNaN(rhoVal)) rho = rhoVal;
			var nuComputed = ComputeNuFromEG(MPaTokN_m2(eMpa), MPaTokN_m2(gMpa));
			if (!double.IsNaN(nuComputed)) nu = nuComputed;
		}

		private void GetConcreteParameters(string grade, out double E, out double nu, out double rho)
		{
			SetDefaultElastic(out E, out nu, out rho);
			var db = LoadJsonResourceOnce(ref concreteDb, "Alpaca4d.Resources.Material.concrete_properties.json");
			var entry = db?[grade] as JObject;
			if (entry == null)
			{
				nu = 0.20;
				return;
			}

			var eMpa = TryGetDouble(entry, "E_mean");
			var gMpa = TryGetDouble(entry, "G_mean");
			var rhoMean = TryGetDouble(entry, "rho_mean");
			var rhoK = TryGetDouble(entry, "rho_k");

			if (!double.IsNaN(eMpa)) E = MPaTokN_m2(eMpa);
			if (!double.IsNaN(rhoMean)) rho = rhoMean;
			else if (!double.IsNaN(rhoK)) rho = rhoK;

			var nuComputed = ComputeNuFromEG(MPaTokN_m2(eMpa), MPaTokN_m2(gMpa));
			nu = !double.IsNaN(nuComputed) ? nuComputed : 0.20;
		}

		private void GetTimberParameters(string grade, out double E, out double nu, out double rho)
		{
			SetDefaultElastic(out E, out nu, out rho);
			var db = LoadJsonResourceOnce(ref timberDb, "Alpaca4d.Resources.Material.timber_properties.json");
			var entry = db?[grade] as JObject;
			if (entry == null)
			{
				nu = 0.35;
				rho = 500;
				return;
			}

			var eMpa = TryGetDouble(entry, "E_mean");
			var gMpa = TryGetDouble(entry, "G_mean");
			var rhoMean = TryGetDouble(entry, "rho_mean");
			var rhoK = TryGetDouble(entry, "rho_k");

			if (!double.IsNaN(eMpa)) E = MPaTokN_m2(eMpa);
			if (!double.IsNaN(rhoMean)) rho = rhoMean;
			else if (!double.IsNaN(rhoK)) rho = rhoK;

			var nuComputed = ComputeNuFromEG(MPaTokN_m2(eMpa), MPaTokN_m2(gMpa));
			nu = !double.IsNaN(nuComputed) ? nuComputed : 0.35;
		}

	private void GetPlasticParameters(string grade, out double E, out double nu, out double rho)
	{
		SetDefaultElastic(out E, out nu, out rho);
		var db = LoadJsonResourceOnce(ref plasticDb, "Alpaca4d.Resources.Material.plastic_properties.json");
		var entry = db?[grade] as JObject;
		if (entry == null)
		{
			nu = 0.40;
			return;
		}

		var eMpa = TryGetDouble(entry, "E");
		var gMpa = TryGetDouble(entry, "G");
		var rhoVal = TryGetDouble(entry, "rho");

		if (!double.IsNaN(eMpa)) E = MPaTokN_m2(eMpa);
		if (!double.IsNaN(rhoVal)) rho = rhoVal;

		var nuComputed = ComputeNuFromEG(MPaTokN_m2(eMpa), MPaTokN_m2(gMpa));
		nu = !double.IsNaN(nuComputed) ? nuComputed : 0.40;
	}

	/// <summary>
	/// Searches for a material grade by name across all databases (Steel, Concrete, Timber, Plastic, Custom).
	/// Performs case-insensitive search and returns the material type and grade name if found.
	/// </summary>
	private (bool found, string type, string gradeName) FindGradeInDatabase(string searchName)
	{
		if (string.IsNullOrWhiteSpace(searchName))
			return (false, null, null);

		// First, try to find in the cached type-to-grades dictionary
		foreach (var kvp in typeToGrades)
		{
			var match = kvp.Value.FirstOrDefault(g => 
				g.Equals(searchName, StringComparison.OrdinalIgnoreCase));
			
			if (match != null)
			{
				return (true, kvp.Key, match);
			}
		}

		// If not found in cache, search directly in all JSON databases
		var databases = new[]
		{
			(db: LoadJsonResourceOnce(ref steelDb, "Alpaca4d.Resources.Material.steel_properties.json"), type: "Steel"),
			(db: LoadJsonResourceOnce(ref concreteDb, "Alpaca4d.Resources.Material.concrete_properties.json"), type: "Concrete"),
			(db: LoadJsonResourceOnce(ref timberDb, "Alpaca4d.Resources.Material.timber_properties.json"), type: "Timber"),
			(db: LoadJsonResourceOnce(ref plasticDb, "Alpaca4d.Resources.Material.plastic_properties.json"), type: "Plastic"),
			(db: customDb, type: GetMaterialTypeFromDb(customDb) ?? "Custom")
		};

		foreach (var (db, type) in databases)
		{
			if (db == null) continue;

			foreach (var prop in db.Properties())
			{
				if (prop.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				{
					return (true, type, prop.Name);
				}
			}
		}

		return (false, null, null);
	}

	/// <summary>
	/// Updates the dropdown menus to reflect a material grade that was typed by the user.
	/// This provides visual feedback that the typed input was recognized.
	/// </summary>
	private void UpdateDropdownsFromTypedInput(string type, string gradeName)
	{
		if (typeDrop == null || gradeDrop == null) return;

		// Update type dropdown
		int typeIdx = typeDrop.FindIndex(type);
		if (typeIdx >= 0)
		{
			typeDrop.Value = typeIdx;
		}

		// Rebuild grade dropdown for the selected type
		InitializeGradeOptions();

		// Update grade dropdown
		int gradeIdx = gradeDrop.FindIndex(gradeName);
		if (gradeIdx >= 0)
		{
			gradeDrop.Value = gradeIdx;
		}
	}

		#region GH persistence

		public override bool Write(GH_IWriter writer)
		{
			// Persist the last used selections so the UI can restore them.
			var chunk = writer.CreateChunk("MaterialPresetElastic");
			chunk.SetString("Type", storedType ?? "Steel");
			chunk.SetString("Grade", storedGrade ?? "S235");
			chunk.SetString("Model", storedModel ?? "nD");

			return base.Write(writer);
		}

		public override bool Read(GH_IReader reader)
		{
			// Load stored selections (if present) before the base logic triggers OnComponentLoaded.
			if (reader.ChunkExists("MaterialPresetElastic"))
			{
				var chunk = reader.FindChunk("MaterialPresetElastic");
				try { storedType = chunk.GetString("Type"); } catch { storedType = "Steel"; }
				try { storedGrade = chunk.GetString("Grade"); } catch { storedGrade = "S235"; }
				try { storedModel = chunk.GetString("Model"); } catch { storedModel = "nD"; }
			}

			return base.Read(reader);
		}

		#endregion
	}
}

