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
    /// Colours the shells by the true stress at one face of the section.
    ///
    /// The counterpart to Shell Forces View, and reading a different quantity: that one shows the
    /// stress resultants, the whole thickness collapsed into a force or a moment per unit width.
    /// This shows stress proper, in force over area, and so has to be told which way through the
    /// thickness to look - a plate in pure bending reads equal and opposite on its two faces and
    /// zero in the middle.
    ///
    /// Principal directions are not drawn here; the Principal Stress Lines component already does
    /// that. The principal magnitudes are offered as things to colour by.
    /// </summary>
    public class ShellStressesView : GH_Component
    {
        private Alpaca4d.Model _model = null;
        private List<Mesh> _coloredShellMeshes = new List<Mesh>();
        private int _layer = 0;
        private int _stressType = 5;
        private int _step = 0;
        private List<System.Drawing.Color> _colors = new List<System.Drawing.Color>();
        private double _min = 0.0;
        private double _max = 0.0;

        /// <summary>Shared with the Shell Stresses component, so the two agree on what Top means.</summary>
        private static string[] LayerNames => Alpaca4d.Result.Read.LayerNames;
        private static readonly string[] StressNames = { "σ11", "σ22", "σ12", "σ23", "σ31", "VonMises", "σ1", "σ2" };

        public ShellStressesView()
          : base("Shell Stresses View (Alpaca4d)", "Shell Stresses View",
            "Colour the shells by the true stress at the top, middle or bottom of the section.\n" +
            "Not the same quantity as Shell Forces View, which shows stress resultants. A plate in " +
            "pure bending reads equal and opposite on its two faces and zero in the middle, so the " +
            "Layer matters as much as the component.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from Run Analysis.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Layer", "Layer", "Where through the thickness to read: 0=Top, 1=Middle, 2=Bottom.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("StressType", "StressType",
                "What to colour by: 0=σ11, 1=σ22, 2=σ12, 3=σ23, 4=σ31, 5=VonMises, 6=σ1, 7=σ2.\n" +
                "σ1 and σ2 are the in-plane principal stresses, larger and smaller. For their " +
                "directions use Principal Stress Lines.", GH_ParamAccess.item, 5);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Colour gradient for the contour.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Min/Max for the colour mapping. Left empty the range of the step is used.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Info", "Info", "Which layer and component are drawn, and the range they span.");
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _coloredShellMeshes.Clear();

            ValueList.UpdateValueLists(this, 1, LayerNames.ToList(),
                Enumerable.Range(0, LayerNames.Length).ToList(), GH_ValueListMode.DropDown, 0);
            ValueList.UpdateValueLists(this, 2, StressNames.ToList(),
                Enumerable.Range(0, StressNames.Length).ToList(), GH_ValueListMode.DropDown, 5);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _layer = 0;
            _stressType = 5;
            _step = 0;

            if (!DA.GetData(0, ref _model)) return;
            DA.GetData(1, ref _layer);
            DA.GetData(2, ref _stressType);
            DA.GetData(3, ref _step);

            if (_layer < 0 || _layer >= LayerNames.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer must be 0 (Top), 1 (Middle) or 2 (Bottom).");
                return;
            }

            if (_stressType < 0 || _stressType >= StressNames.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"StressType must be between 0 and {StressNames.Length - 1}.");
                return;
            }

            if (_model.Shells == null || _model.Shells.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "This model has no shells.");
                return;
            }

            _colors = new List<System.Drawing.Color>();
            if (!DA.GetDataList(4, _colors) || _colors.Count < 2)
                _colors = Alpaca4d.Colors.Gradient(0);

            List<Alpaca4d.Result.Read.ShellFibreStress> all;
            try
            {
                all = Alpaca4d.Result.Read.ShellFibreStresses(_model, _step);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var byElement = all.GroupBy(x => x.ElementId).ToDictionary(g => g.Key, g => g.ToList());

            // One list of values per shell, in the model's own order, holding one value per
            // integration point - which is what the mesh vertices are coloured from.
            var perShell = new List<List<double>>();
            foreach (var shell in _model.Shells)
            {
                var values = new List<double>();

                if (shell.Id != null && byElement.TryGetValue(shell.Id.Value, out var entries))
                {
                    int fibreCount = entries.Select(x => x.Fibre).Distinct().Count();
                    if (fibreCount > 0)
                    {
                        int[] wanted = Alpaca4d.Result.Read.LayerFibres(fibreCount);

                        values = entries.Where(x => x.Fibre == wanted[_layer])
                                        .OrderBy(x => x.GaussPoint)
                                        .Select(Component)
                                        .ToList();
                    }
                }

                perShell.Add(values);
            }

            var domain = new Rhino.Geometry.Interval();
            if (!DA.GetData(5, ref domain))
            {
                var flat = perShell.SelectMany(x => x).ToList();
                _min = flat.Count > 0 ? flat.Min() : 0.0;
                _max = flat.Count > 0 ? flat.Max() : 0.0;
            }
            else
            {
                _min = domain.Min;
                _max = domain.Max;
            }

            var colorDict = new SortedDictionary<double, System.Drawing.Color>();
            // A flat field - every value the same, which a plate with no load in this component
            // gives - would otherwise divide by zero and colour nothing.
            double span = _max - _min;
            double diff = span > 0.0 ? span / (_colors.Count - 1) : 1.0;
            double start = _min;
            foreach (var color in _colors)
            {
                if (!colorDict.ContainsKey(start))
                {
                    colorDict.Add(start, color);
                    start += diff;
                }
            }

            _coloredShellMeshes.Clear();
            for (int i = 0; i < _model.Shells.Count; i++)
            {
                var mesh = _model.Shells[i].Mesh.DuplicateMesh();
                var values = perShell[i];

                if (values.Count > 0)
                {
                    mesh.VertexColors.Clear();
                    for (int j = 0; j < mesh.Vertices.Count; j++)
                    {
                        // A triangle has three integration points and four vertices in some meshes,
                        // so the last value is reused rather than left uncoloured.
                        double value = values[Math.Min(j, values.Count - 1)];
                        mesh.VertexColors.Add(Alpaca4d.Colors.GetColor(value, colorDict));
                    }
                }

                _coloredShellMeshes.Add(mesh);
            }

            DA.SetData(0, $"{StressNames[_stressType]} at {LayerNames[_layer]}, Min: {_min:F3}, Max: {_max:F3}, Step: {_step}");

            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>The component being drawn, out of one station's five stresses.</summary>
        private double Component(Alpaca4d.Result.Read.ShellFibreStress s)
        {
            switch (_stressType)
            {
                case 0: return s.S11;
                case 1: return s.S22;
                case 2: return s.S12;
                case 3: return s.S23;
                case 4: return s.S31;
                case 5: return s.VonMises;
                case 6: return Principal(s, true);
                case 7: return Principal(s, false);
                default: return 0.0;
            }
        }

        /// <summary>
        /// An in-plane principal stress, from Mohr's circle on the membrane components. A shell
        /// fibre is in plane stress - the through-thickness direct stress is condensed out - so the
        /// two in-plane principals are the whole story and the third is zero.
        /// </summary>
        private static double Principal(Alpaca4d.Result.Read.ShellFibreStress s, bool larger)
        {
            double average = 0.5 * (s.S11 + s.S22);
            double radius = Math.Sqrt(0.25 * (s.S11 - s.S22) * (s.S11 - s.S22) + s.S12 * s.S12);

            return larger ? average + radius : average - radius;
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

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
                    new Point3d(1e9, 1e9, 1e9));
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.shellStress;

        public override Guid ComponentGuid => new Guid("{6B2F9C81-4D53-4A07-B1E8-95C7D2064A3F}");
    }
}
