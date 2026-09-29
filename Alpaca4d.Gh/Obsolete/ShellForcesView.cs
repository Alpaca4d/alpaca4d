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
    /// <summary>
    /// Retired. View Results draws the same result - "Shell forces" in its Result dropdown -
    /// alongside every other one, on the deformed shape if asked, so it is not a component of
    /// its own any more. Kept hidden so definitions that already place it still open.
    /// </summary>
    [Obsolete]
    public class ShellForcesView : GH_Component
    {
        private Alpaca4d.Model _model = null;
        private List<Mesh> _coloredShellMeshes = new List<Mesh>();
        private int _forceType = 0;
        private int _step = 0;
        private List<System.Drawing.Color> _colors = new List<System.Drawing.Color>();
        private double _min = 0.0;
        private double _max = 0.0;

        public ShellForcesView()
          : base("Shell Forces View (Alpaca4d)", "Shell Forces View",
            "Visualize Shell Forces in the viewport with color gradient",
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
            pManager.AddIntegerParameter("ForceType", "ForceType", "Force type to display: 0=pxx, 1=pyy, 2=pxy, 3=mxx, 4=myy, 5=mxy, 6=vxz, 7=vyz", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Analysis step", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Color gradient for visualization", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Min/Max range for color mapping", GH_ParamAccess.item);
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
        /// We use it to populate the value list for ForceType.
        /// </summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            
            // Clear cached meshes
            _coloredShellMeshes.Clear();
            
            // Update value list for ForceType input
            var forceTypeNames = new List<string> 
            { 
                "pxx", "pyy", "pxy", "mxx", "myy", "mxy", "vxz", "vyz" 
            };
            var forceTypeValues = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            
            ValueList.UpdateValueLists(this, 1, forceTypeNames, forceTypeValues, GH_ValueListMode.DropDown, 0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _forceType = 0;
            _step = 0;

            if (!DA.GetData(0, ref _model)) return;
            DA.GetData(1, ref _forceType);
            DA.GetData(2, ref _step);

            // Validate force type
            if (_forceType < 0 || _forceType > 7)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ForceType must be between 0 and 7");
                return;
            }

            // Get colors
            _colors = new List<System.Drawing.Color>();
            if (!DA.GetDataList(3, _colors))
            {
                _colors = Alpaca4d.Colors.Gradient(0);
            }

            // Read force data
            var fxQuad = new List<List<double>>();
            var fyQuad = new List<List<double>>();
            var fxyQuad = new List<List<double>>();
            var mxQuad = new List<List<double>>();
            var myQuad = new List<List<double>>();
            var mxyQuad = new List<List<double>>();
            var vxzQuad = new List<List<double>>();
            var vyzQuad = new List<List<double>>();

            var fxTri = new List<List<double>>();
            var fyTri = new List<List<double>>();
            var fxyTri = new List<List<double>>();
            var mxTri = new List<List<double>>();
            var myTri = new List<List<double>>();
            var mxyTri = new List<List<double>>();
            var vxzTri = new List<List<double>>();
            var vyzTri = new List<List<double>>();

            if (_model.HasQuadShell)
                (fxQuad, fyQuad, fxyQuad, mxQuad, myQuad, mxyQuad, vxzQuad, vyzQuad) = Alpaca4d.Result.Read.ASDQ4Forces(_model, _step);
            if (_model.HasTriShell)
                (fxTri, fyTri, fxyTri, mxTri, myTri, mxyTri, vxzTri, vyzTri) = Alpaca4d.Result.Read.ASDT3Forces(_model, _step);

            // Merge quad and tri data
            var allForces = new List<List<double>>();
            
            // Select the appropriate force component based on _forceType
            var quadForces = GetForceComponent(_forceType, fxQuad, fyQuad, fxyQuad, mxQuad, myQuad, mxyQuad, vxzQuad, vyzQuad);
            var triForces = GetForceComponent(_forceType, fxTri, fyTri, fxyTri, mxTri, myTri, mxyTri, vxzTri, vyzTri);
            
            // Combine forces maintaining shell order
            int quadIndex = 0;
            int triIndex = 0;
            foreach (var shell in _model.Shells)
            {
                if (shell.ElementClass == Element.ElementClass.ASDShellQ4)
                {
                    if (quadIndex < quadForces.Count)
                    {
                        allForces.Add(quadForces[quadIndex]);
                        quadIndex++;
                    }
                }
                else if (shell.ElementClass == Element.ElementClass.ASDShellT3)
                {
                    if (triIndex < triForces.Count)
                    {
                        allForces.Add(triForces[triIndex]);
                        triIndex++;
                    }
                }
            }

            // Calculate min/max values
            Rhino.Geometry.Interval domain = new Rhino.Geometry.Interval();
            if (!DA.GetData(4, ref domain))
            {
                var allValues = allForces.SelectMany(x => x).ToList();
                if (allValues.Count > 0)
                {
                    _min = allValues.Min();
                    _max = allValues.Max();
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
            _coloredShellMeshes.Clear();
            for (int i = 0; i < _model.Shells.Count; i++)
            {
                var shell = _model.Shells[i];
                var mesh = shell.Mesh.DuplicateMesh();
                
                if (i < allForces.Count)
                {
                    var forceValues = allForces[i];
                    
                    // Apply colors to mesh vertices based on force values
                    var colors = new List<System.Drawing.Color>();
                    foreach (var value in forceValues)
                    {
                        colors.Add(Alpaca4d.Colors.GetColor(value, colorDict));
                    }
                    
                    // Set vertex colors
                    mesh.VertexColors.Clear();
                    for (int j = 0; j < mesh.Vertices.Count && j < colors.Count; j++)
                    {
                        mesh.VertexColors.Add(colors[j]);
                    }
                }
                
                _coloredShellMeshes.Add(mesh);
            }

            // Output info
            string[] forceNames = { "pxx", "pyy", "pxy", "mxx", "myy", "mxy", "vxz", "vyz" };
            string info = $"Force: {forceNames[_forceType]}, Min: {_min:F3}, Max: {_max:F3}, Step: {_step}";
            DA.SetData(0, info);

            // Ensure viewport updates
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>
        /// Helper method to select the appropriate force component
        /// </summary>
        private List<List<double>> GetForceComponent(int forceType, 
            List<List<double>> fx, List<List<double>> fy, List<List<double>> fxy,
            List<List<double>> mx, List<List<double>> my, List<List<double>> mxy,
            List<List<double>> vxz, List<List<double>> vyz)
        {
            switch (forceType)
            {
                case 0: return fx;
                case 1: return fy;
                case 2: return fxy;
                case 3: return mx;
                case 4: return my;
                case 5: return mxy;
                case 6: return vxz;
                case 7: return vyz;
                default: return new List<List<double>>();
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
            foreach (var mesh in _coloredShellMeshes)
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
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.shellStress;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{F1A8E3D7-6C49-4B8E-9F2A-7D3C8E4F5A6B}");
    }
}

