using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Result;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    public class BrickStressesView : GH_Component
    {
        private Alpaca4d.Model _model = null;
        private List<Mesh> _coloredBrickMeshes = new List<Mesh>();
        private int _stressType = 0;
        private int _step = 0;
        private List<System.Drawing.Color> _colors = new List<System.Drawing.Color>();
        private double _min = 0.0;
        private double _max = 0.0;

        public BrickStressesView()
          : base("Brick Stresses View (Alpaca4d)", "Brick Stresses View",
            "Visualize Brick Stresses in the viewport with color gradient",
            "Alpaca4d", "09_Visualisation")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The Alpaca Model", GH_ParamAccess.item);
            pManager.AddIntegerParameter("StressType", "StressType", "Stress component to display: 0=σ₁₁, 1=σ₂₂, 2=σ₃₃, 3=σ₁₂, 4=σ₂₃, 5=σ₁₃, 6=Von Mises. Axes says what 1, 2 and 3 mean.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Analysis step", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Color gradient for visualization", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Min/Max range for color mapping", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Axes", "Axes",
                "Which frame to read the components in: 0 = global, the axes the solver works in; " +
                "1 = each element's own, from its node numbering. Reading only - it changes nothing " +
                "in the analysis.",
                GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Info", "Info", "Information about min/max values");
        }

        /// <summary>
        /// This is called before SolveInstance to update value lists.
        /// We use it to populate the value list for StressType.
        /// </summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            
            // Clear cached meshes
            _coloredBrickMeshes.Clear();
            
            // Update value list for StressType input
            var stressTypeNames = new List<string> 
            { 
                "σ₁₁", "σ₂₂", "σ₃₃", "σ₁₂", "σ₂₃", "σ₁₃", "Von Mises" 
            };
            var stressTypeValues = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
            
            ValueList.UpdateValueLists(this, 1, stressTypeNames, stressTypeValues, GH_ValueListMode.DropDown, 0);

            ValueList.UpdateValueLists(this, 5, new List<string> { "Global", "Local" },
                                       new List<int> { 0, 1 }, GH_ValueListMode.DropDown, 0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _stressType = 0;
            _step = 0;

            if (!DA.GetData(0, ref _model)) return;
            DA.GetData(1, ref _stressType);
            DA.GetData(2, ref _step);

            int axes = 0;
            DA.GetData(5, ref axes);
            bool local = axes == 1;

            // Validate stress type
            if (_stressType < 0 || _stressType > 6)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "StressType must be between 0 and 6");
                return;
            }

            // Get colors
            _colors = new List<System.Drawing.Color>();
            if (!DA.GetDataList(3, _colors))
            {
                _colors = Alpaca4d.Colors.Gradient(0);
            }

            // Read stress data
            List<double> tetraSigma11 = new List<double>();
            List<double> tetraSigma22 = new List<double>();
            List<double> tetraSigma33 = new List<double>();
            List<double> tetraSigma12 = new List<double>();
            List<double> tetraSigma23 = new List<double>();
            List<double> tetraSigma13 = new List<double>();

            List<double> sspSigma11 = new List<double>();
            List<double> sspSigma22 = new List<double>();
            List<double> sspSigma33 = new List<double>();
            List<double> sspSigma12 = new List<double>();
            List<double> sspSigma23 = new List<double>();
            List<double> sspSigma13 = new List<double>();

            if (_model.HasTetrahedron)
            {
                (tetraSigma11, tetraSigma22, tetraSigma33, tetraSigma12, tetraSigma23, tetraSigma13) = 
                    Alpaca4d.Result.Read.TetrahedronStress(_model, _step, local: local);
            }
            if (_model.HasSSpBrick)
            {
                (sspSigma11, sspSigma22, sspSigma33, sspSigma12, sspSigma23, sspSigma13) = 
                    Alpaca4d.Result.Read.SSPBrickStress(_model, _step, local: local);
            }

            // The elements in the order the two readers were just called in: every tetrahedron,
            // then every SSP brick. Not the order the model holds them in, which is what the
            // meshes below used to be coloured by - a model mixing the two kinds painted each
            // brick with another one's stress.
            var recordedOrder = _model.Bricks
                .Where(x => x.ElementClass == Alpaca4d.Element.ElementClass.FourNodeTetrahedron)
                .Concat(_model.Bricks.Where(x => x.ElementClass == Alpaca4d.Element.ElementClass.SSPBrick))
                .ToList();

            // Merge stress data
            var sigma11 = tetraSigma11.Concat(sspSigma11).ToList();
            var sigma22 = tetraSigma22.Concat(sspSigma22).ToList();
            var sigma33 = tetraSigma33.Concat(sspSigma33).ToList();
            var sigma12 = tetraSigma12.Concat(sspSigma12).ToList();
            var sigma23 = tetraSigma23.Concat(sspSigma23).ToList();
            var sigma13 = tetraSigma13.Concat(sspSigma13).ToList();

            // Calculate Von Mises stress
            List<double> vonMises = new List<double>();
            for (int i = 0; i < sigma11.Count(); i++)
            {
                double _vonMises = Math.Sqrt(
                    0.5 * ((sigma11[i] - sigma22[i]) * (sigma11[i] - sigma22[i]) +
                           (sigma22[i] - sigma33[i]) * (sigma22[i] - sigma33[i]) +
                           (sigma33[i] - sigma11[i]) * (sigma33[i] - sigma11[i]) +
                           6.0 * (sigma12[i] * sigma12[i] + sigma23[i] * sigma23[i] + sigma13[i] * sigma13[i]))
                );
                vonMises.Add(_vonMises);
            }

            // Select the appropriate stress component based on _stressType
            var allStresses = GetStressComponent(_stressType, sigma11, sigma22, sigma33, sigma12, sigma23, sigma13, vonMises);

            // Calculate min/max values
            Rhino.Geometry.Interval domain = new Rhino.Geometry.Interval();
            if (!DA.GetData(4, ref domain))
            {
                if (allStresses.Count > 0)
                {
                    _min = allStresses.Min();
                    _max = allStresses.Max();
                }
            }
            else
            {
                _min = domain.Min;
                _max = domain.Max;
            }

            // Create color dictionary
            var colorDict = new System.Collections.Generic.SortedDictionary<double, System.Drawing.Color>();
            var numberOfColors = _colors.Count;
            var diff = (_max - _min) / (numberOfColors - 1);
            var start = _min;
            foreach (var color in _colors)
            {
                if (!colorDict.ContainsKey(start))
                {
                    colorDict.Add(start, color);
                    start += diff;
                }
            }

            // Create colored meshes
            _coloredBrickMeshes.Clear();
            for (int i = 0; i < recordedOrder.Count; i++)
            {
                var brick = recordedOrder[i];
                var mesh = brick.Mesh.DuplicateMesh();
                
                if (i < allStresses.Count)
                {
                    var stressValue = allStresses[i];
                    var color = Alpaca4d.Colors.GetColor(stressValue, colorDict);
                    
                    // Apply uniform color to all vertices of this brick
                    mesh.VertexColors.Clear();
                    for (int j = 0; j < mesh.Vertices.Count; j++)
                    {
                        mesh.VertexColors.Add(color);
                    }
                }
                
                _coloredBrickMeshes.Add(mesh);
            }

            // Output info
            string[] stressNames = { "σ₁₁", "σ₂₂", "σ₃₃", "σ₁₂", "σ₂₃", "σ₁₃", "Von Mises" };
            string info = $"Stress: {stressNames[_stressType]} ({(local ? "local" : "global")} axes), " +
                          $"Min: {_min:F3}, Max: {_max:F3}, Step: {_step}";
            DA.SetData(0, info);

            // Ensure viewport updates
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>
        /// Helper method to select the appropriate stress component
        /// </summary>
        private List<double> GetStressComponent(int stressType, 
            List<double> sigma11, List<double> sigma22, List<double> sigma33,
            List<double> sigma12, List<double> sigma23, List<double> sigma13,
            List<double> vonMises)
        {
            switch (stressType)
            {
                case 0: return sigma11;
                case 1: return sigma22;
                case 2: return sigma33;
                case 3: return sigma12;
                case 4: return sigma23;
                case 5: return sigma13;
                case 6: return vonMises;
                default: return new List<double>();
            }
        }

        /// <summary>
        /// This method draws the colored meshes in the viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            // Draw colored meshes
            foreach (var mesh in _coloredBrickMeshes)
            {
                args.Display.DrawMeshFalseColors(mesh);
                args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 1);
            }
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox
        {
            get
            {
                return new BoundingBox(
                    new Point3d(-1e9, -1e9, -1e9),
                    new Point3d(1e9, 1e9, 1e9)
                );
            }
        }

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.View_Brick_Stresses__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{2B9F7E4A-8D6C-4F1E-9A3B-5E7D8C9F1A2B}");
    }
}

