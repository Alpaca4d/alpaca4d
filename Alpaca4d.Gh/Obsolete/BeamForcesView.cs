using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using GH_IO.Serialization;
using Rhino.Geometry;
using Rhino.Display;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Alpaca4d.Result;
using Alpaca4d.UIWidgets;
using Alpaca4d.UI;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Retired. View Results draws the same result - "Beam forces" in its Result dropdown -
    /// alongside every other one, on the deformed shape if asked, so it is not a component of
    /// its own any more. Kept hidden so definitions that already place it still open.
    /// </summary>
    [Obsolete]
    public class BeamForcesView : GH_Component
    {
        private Alpaca4d.Model _model = null;
        private List<Mesh> _forceDiagramMeshes = new List<Mesh>();
        private List<int> _forceTypes = new List<int>();
        private bool _showText = false;
        
        // Data structure to store label information for drawing in the viewport
        private class ForceLabel
        {
            public Point3d Position;
            public string Text;
            public System.Drawing.Color Color;
        }

        private readonly List<ForceLabel> _forceLabels = new List<ForceLabel>();
        private int _step = 0;
        private double _scale = 1.0;
        
        // Text height for force labels (can be changed via right-click menu)
        public double TextHeight { get; set; } = 0.5;

        public BeamForcesView()
          : base("Beam Forces View (Alpaca4d)", "Beam Forces View",
            "Visualize Beam Force Diagrams in the viewport",
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
            pManager.AddIntegerParameter("ForceType", "ForceType", "Force type to display: 0=N, 1=Vy, 2=Vz, 3=Torsion, 4=My, 5=Mz", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Analysis step", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Scale", "Scale", "Diagram scale factor", GH_ParamAccess.item, 1.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("ShowText", "ShowText", "Show numeric force values near diagram points", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Info", "Info", "Information about force diagrams");
        }

        /// <summary>
        /// This is called before SolveInstance to update value lists.
        /// </summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            
            // Clear cached meshes and labels
            _forceDiagramMeshes.Clear();
            _forceLabels.Clear();
            
            // Update value list for ForceType input
            var forceTypeNames = new List<string> 
            { 
                "N", "Vy", "Vz", "Torsion", "My", "Mz" 
            };
            var forceTypeValues = new List<int> { 0, 1, 2, 3, 4, 5 };
            
            ValueList.UpdateValueLists(this, 1, forceTypeNames, forceTypeValues, GH_ValueListMode.DropDown, 0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _forceTypes.Clear();
            _forceLabels.Clear();
            _step = 0;
            _scale = 1.0;

            if (!DA.GetData(0, ref _model)) return;
            if (!DA.GetDataList(1, _forceTypes))
            {
                // If no list provided, default to single force type 0
                _forceTypes.Add(0);
            }
            DA.GetData(2, ref _step);
            DA.GetData(3, ref _scale);
            DA.GetData(4, ref _showText);

            // Validate force types
            foreach (var forceType in _forceTypes)
            {
                if (forceType < 0 || forceType > 5)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"ForceType must be between 0 and 5, got {forceType}");
                    return;
                }
            }

            // Read force data
            (var n, var mz, var vy, var my, var vz, var t) = Alpaca4d.Result.Read.ForceBeamColumn(_model, _step);

            // Create force diagrams for each force type
            _forceDiagramMeshes.Clear();
            string[] forceNames = { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
            
            foreach (var forceType in _forceTypes)
            {
                // Select the appropriate force component based on forceType
                var forceData = GetForceComponent(forceType, n, vy, vz, t, my, mz);

                // Create force diagrams for each beam
                for (int i = 0; i < _model.Beams.Count; i++)
                {
                    if (i < forceData.Count)
                    {
                        var beam = _model.Beams[i];
                        var forces = forceData[i];
                        
                        // Create diagram mesh for this beam
                        var diagramMesh = CreateBeamForceDiagram(beam, forces, forceType, _scale);
                        if (diagramMesh != null)
                        {
                            _forceDiagramMeshes.Add(diagramMesh);
                        }
                    }
                }
            }

            // Output info
            var forceTypeNames = _forceTypes.Select(ft => forceNames[ft]).ToList();
            string info = $"Forces: {string.Join(", ", forceTypeNames)}, Beams: {_model.Beams.Count}, Step: {_step}, Scale: {_scale:F2}";
            DA.SetData(0, info);

            // Ensure viewport updates
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>
        /// Helper method to select the appropriate force component
        /// </summary>
        private List<List<double>> GetForceComponent(int forceType,
            List<List<double>> n, List<List<double>> vy, List<List<double>> vz,
            List<List<double>> t, List<List<double>> my, List<List<double>> mz)
        {
            switch (forceType)
            {
                case 0: return n;    // Normal force
                case 1: return vy;   // Shear Y
                case 2: return vz;   // Shear Z
                case 3: return t;    // Torsion (Mx)
                case 4: return my;   // Moment Y
                case 5: return mz;   // Moment Z
                default: return new List<List<double>>();
            }
        }

        /// <summary>
        /// Gets the positive and negative colors for a specific force type from the Palette
        /// </summary>
        private (System.Drawing.Color positive, System.Drawing.Color negative) GetForceTypeColors(int forceType)
        {
            switch (forceType)
            {
                case 0: // Normal force (N)
                    return (Palette.N_Positive, Palette.N_Negative);
                case 1: // Shear Y (Vy)
                    return (Palette.Vy_Positive, Palette.Vy_Negative);
                case 2: // Shear Z (Vz)
                    return (Palette.Vz_Positive, Palette.Vz_Negative);
                case 3: // Torsion (Mx)
                    return (Palette.Torsion_Positive, Palette.Torsion_Negative);
                case 4: // Moment Y (My)
                    return (Palette.My_Positive, Palette.My_Negative);
                case 5: // Moment Z (Mz)
                    return (Palette.Mz_Positive, Palette.Mz_Negative);
                default:
                    return (Palette.N_Positive, Palette.N_Negative);
            }
        }

        /// <summary>
        /// Creates a force diagram mesh for a single beam
        /// </summary>
        private Mesh CreateBeamForceDiagram(Alpaca4d.Generic.IBeam beam, List<double> forces, int forceType, double scale)
        {
            if (forces == null || forces.Count == 0) return null;

            var curve = beam.Curve;
            var integrationPoints = forces.Count;

            // Get reference direction based on force type
            Vector3d referenceDirection = GetReferenceDirection(beam, forceType);

            // Create points along the beam at integration point locations
            var beamPoints = new List<Point3d>();
            var diagramPoints = new List<Point3d>();
            var colors = new List<System.Drawing.Color>();

            // Get colors for this force type from the Palette
            var (positiveColor, negativeColor) = GetForceTypeColors(forceType);

            // The recorder writes one value per section, and the sections are NOT evenly spaced:
            // HingeRadau puts two of its six points a short 8/3*lp inside the ends. Ask the
            // integration rule where they actually sit rather than spreading them out.
            var stations = SectionStations(beam, integrationPoints);

            for (int i = 0; i < integrationPoints; i++)
            {
                double t = stations[i];
                Point3d pointOnBeam = curve.PointAtNormalizedLength(t);
                
                // Offset point perpendicular to beam in reference direction
                Vector3d offset = referenceDirection * forces[i] * scale;
                Point3d diagramPoint = pointOnBeam + offset;
                
                beamPoints.Add(pointOnBeam);
                diagramPoints.Add(diagramPoint);
                
                // Determine color based on force value (positive or negative) using Palette colors
                System.Drawing.Color color = forces[i] >= 0 ? positiveColor : negativeColor;
                colors.Add(color);
            }

            // Optionally create labels at diagram points
            if (_showText)
            {
                for (int i = 0; i < integrationPoints; i++)
                {
                    _forceLabels.Add(new ForceLabel
                    {
                        Position = diagramPoints[i],
                        Text = forces[i].ToString("0.00"),
                        Color = colors[i]
                    });
                }
            }

            // Create closed mesh from points with colors
            Mesh mesh = CreateClosedDiagramMesh(beamPoints, diagramPoints, colors);
            
            return mesh;
        }

        /// <summary>
        /// Normalised positions of the section results along a beam, from the element's own
        /// integration rule. Falls back to an even spread when the recorder holds a number of
        /// sections the rule does not account for, so an unexpected file still draws something.
        /// </summary>
        private static IReadOnlyList<double> SectionStations(Alpaca4d.Generic.IBeam beam, int count)
        {
            if (beam.BeamIntegration != null)
            {
                double length = beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd);
                var xi = beam.BeamIntegration.SectionLocations(length);

                if (xi != null && xi.Count == count)
                    return xi;
            }

            var uniform = new double[count];
            for (int i = 0; i < count; i++)
                uniform[i] = count == 1 ? 0.5 : (double)i / (count - 1);

            return uniform;
        }

        /// <summary>
        /// Creates a closed mesh from beam points and diagram points with vertex colors
        /// Creates a closed polygon: beam points forward, then diagram points backward
        /// </summary>
        private Mesh CreateClosedDiagramMesh(List<Point3d> beamPoints, List<Point3d> diagramPoints, List<System.Drawing.Color> colors)
        {
            if (beamPoints.Count < 2 || diagramPoints.Count < 2) return null;

            int n = beamPoints.Count;

            // Create a closed polyline: beam points -> last diagram point -> diagram points reversed -> first beam point
            var boundaryPoints = new List<Point3d>();
            var boundaryColors = new List<System.Drawing.Color>();
            
            // Add beam points forward (0 to n-1) with corresponding colors
            for (int i = 0; i < n; i++)
            {
                boundaryPoints.Add(beamPoints[i]);
                boundaryColors.Add(colors[i]);
            }
            
            // Add diagram points backward (n-1 to 0) with corresponding colors
            for (int i = n - 1; i >= 0; i--)
            {
                boundaryPoints.Add(diagramPoints[i]);
                boundaryColors.Add(colors[i]);
            }
            
            // Create closed polyline
            var polyline = new Polyline(boundaryPoints);
            polyline.Add(boundaryPoints[0]); // Close the polyline
            
            // Create mesh from closed polyline using Delaunay or simple triangulation
            var mesh = new Mesh();
            
            // Add all vertices with colors
            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                mesh.Vertices.Add(boundaryPoints[i]);
                mesh.VertexColors.Add(boundaryColors[i]);
            }
            
            // Create quad faces connecting beam edge to diagram edge
            for (int i = 0; i < n - 1; i++)
            {
                // Vertices on beam edge: i, i+1
                // Corresponding vertices on diagram edge (reversed): 2n-1-i, 2n-2-i
                int b0 = i;
                int b1 = i + 1;
                int d0 = 2 * n - 1 - i;
                int d1 = 2 * n - 2 - i;
                
                // Create quad face connecting beam edge to diagram edge
                mesh.Faces.AddFace(b0, b1, d1, d0);
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();

            return mesh;
        }

        /// <summary>
        /// Gets the reference direction for plotting based on force type
        /// </summary>
        private Vector3d GetReferenceDirection(Alpaca4d.Generic.IBeam beam, int forceType)
        {
            var curve = beam.Curve;
            var localZ = beam.GeomTransf.LocalZ;
            var localY = beam.GeomTransf.LocalY;

            // LocalX is the beam direction
            Vector3d localX = curve.PointAtEnd - curve.PointAtStart;
            localX.Unitize();

            switch (forceType)
            {
                case 0: // Normal force (N)
                    // Plot on plane from cross product of LocalX and GlobalZ
                    Vector3d globalZ = new Vector3d(0, 0, 1);
                    Vector3d nDirection = Vector3d.CrossProduct(localX, globalZ);
                    nDirection.Unitize();
                    if (nDirection.Length < 0.01) // If beam is vertical
                    {
                        nDirection = Vector3d.CrossProduct(localX, new Vector3d(1, 0, 0));
                        nDirection.Unitize();
                    }
                    return nDirection;

                case 1: // Shear Y (Vy)
                    // Plot along local Y
                    return localY;

                case 2: // Shear Z (Vz)
                    // Plot along local Z
                    return localZ;

                case 3: // Torsion (Mx)
                    // Plot on plane from cross product of LocalX and GlobalZ
                    Vector3d globalZ2 = new Vector3d(0, 0, 1);
                    Vector3d tDirection = Vector3d.CrossProduct(localX, globalZ2);
                    tDirection.Unitize();
                    if (tDirection.Length < 0.01) // If beam is vertical
                    {
                        tDirection = Vector3d.CrossProduct(localX, new Vector3d(1, 0, 0));
                        tDirection.Unitize();
                    }
                    return tDirection;

                case 4: // Moment Y (My)
                    // Plot perpendicular to local Y (along local Z)
                    return localZ;

                case 5: // Moment Z (Mz)
                    // Plot perpendicular to local Z (along local Y)
                    return localY;

                default:
                    return Vector3d.ZAxis;
            }
        }

        /// <summary>
        /// This method draws the force diagrams in the viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            // Draw beam reference lines
            foreach (var beam in _model.Beams)
            {
                args.Display.DrawCurve(beam.Curve, System.Drawing.Color.Gray, 1);
            }

            // Draw force diagram meshes with vertex colors
            foreach (var mesh in _forceDiagramMeshes)
            {
                args.Display.DrawMeshFalseColors(mesh);
                args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 1);
            }

            // Draw numeric labels as dots near diagram points if requested
            if (_showText && _forceLabels.Count > 0)
            {
                foreach (var label in _forceLabels)
                {
                    // Plane whose X/Y match the current camera, so text is screen‑aligned
                    var plane = new Plane(
                        label.Position,
                        args.Viewport.CameraX,
                        args.Viewport.CameraY
                    );

                    args.Display.Draw3dText(
                        label.Text,
                        System.Drawing.Color.Black,
                        plane,
                        TextHeight,   // text height in model units (adjustable via right-click menu)
                        "Arial",
                        false,
                        false
                    );
                }
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

        #region Serialization (Persistence)
        
        /// <summary>
        /// Writes the text height value to the document for persistence
        /// </summary>
        public override bool Write(GH_IWriter writer)
        {
            writer.SetDouble("TextHeight", TextHeight);
            return base.Write(writer);
        }

        /// <summary>
        /// Reads the text height value from the document
        /// </summary>
        public override bool Read(GH_IReader reader)
        {
            try
            {
                TextHeight = reader.GetDouble("TextHeight");
            }
            catch
            {
                TextHeight = 0.5; // Default value if not found
            }
            return base.Read(reader);
        }
        
        #endregion

        #region Custom Menu Items
        
        /// <summary>
        /// Appends custom menu items to the component's right-click menu
        /// </summary>
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            
            // Add separator
            Menu_AppendSeparator(menu);
            
            // ========== SLIDER OPTION ==========
            // Add label showing current value (will be updated by slider)
            var sliderLabel = new ToolStripLabel($"Text Height: {TextHeight:0.00}");
            sliderLabel.Font = new System.Drawing.Font(sliderLabel.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(sliderLabel);
            
            // Add TrackBar (slider) using ToolStripControlHost
            var slider = new TrackBar();
            slider.Minimum = 1;       // Represents 0.1
            slider.Maximum = 10;      // Represents 1.0
            slider.Value = Math.Min(Math.Max((int)(TextHeight * 10), 1), 10); // Clamp to valid range
            slider.Width = 200;
            slider.Height = 45;
            slider.TickFrequency = 1;
            slider.SmallChange = 1;
            slider.LargeChange = 2;
            slider.TickStyle = TickStyle.BottomRight;

            // Host the TrackBar inside the Grasshopper menu so it becomes visible
            var sliderHost = new ToolStripControlHost(slider);
            menu.Items.Add(sliderHost);
            
            // Update label and apply value immediately when slider changes
            slider.ValueChanged += (s, e) =>
            {
                double newHeight = slider.Value / 10.0;
                sliderLabel.Text = $"Text Height: {newHeight:0.00}";
                
                // Apply the value immediately
                if (Math.Abs(TextHeight - newHeight) > 0.001)
                {
                    TextHeight = newHeight;
                    ExpireSolution(true);
                }
            };
        }
        
        #endregion

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
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.BeamForcesDiagram__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{3C8E5F2D-7A4B-4E9F-8B1C-6D9E7F8A3C4D}");
    }
}

