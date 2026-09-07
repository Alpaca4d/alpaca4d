using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using Rhino.Geometry;
using Grasshopper;
using Alpaca4d;
using Alpaca4d.Helper;
using PureHDF;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Result
{

    public enum ResultType
    {
        DISPLACEMENT,
        ROTATION,
        VELOCITY,
        ANGULAR_VELOCITY,
        ACCELERATION,
        ANGULAR_ACCELERATION,
        REACTION_FORCE,
        REACTION_MOMENT,
        MODES_OF_VIBRATION_U,
        MODES_OF_VIBRATION_R
    }

    public enum ResultLocation
    {
        NODES,
        ELEMENTS,
    }

    public partial class Read
    {
        private const string ON_NODES = "/MODEL_STAGE[1]/RESULTS/ON_NODES";
        private const string ON_ELEMENTS = "/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS";
        private const string STEP_PREFIX = "STEP_";

        /// <summary>
        /// How many steps the recorder actually wrote, which is the count a caller needs to
        /// walk a whole time history.
        ///
        /// Read off the file rather than off Settings.AnalysisStep.NumIncr: a model can be
        /// run with no Settings at all, an analysis that stops early writes fewer steps than
        /// it was asked for, and a deserialised model carries no Settings to ask.
        ///
        /// A modal run nests its results as STEP_0/MODE_n instead, so this returns 1 there -
        /// modes are counted by the eigenvalue analysis, not by this.
        /// </summary>
        public static int StepCount(Model alpacaModel)
        {
            if (alpacaModel == null || alpacaModel.Recorders == null || !alpacaModel.Recorders.Any())
                return 0;

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);
            if (!System.IO.File.Exists(recorderPath))
                return 0;

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            // Every recorded quantity is written for the same steps, so the first group that
            // holds any settles the count.
            foreach (var dataGroup in DataGroups(h5File))
            {
                int count = dataGroup.Children().Count(child => child.Name.StartsWith(STEP_PREFIX));
                if (count > 0)
                    return count;
            }

            return 0;
        }

        /// <summary>
        /// Every DATA group in the file: one per nodal quantity, and one per element class
        /// per element quantity. Laid out as ON_NODES/&lt;quantity&gt;/DATA and
        /// ON_ELEMENTS/&lt;quantity&gt;/&lt;class&gt;/DATA.
        /// </summary>
        private static IEnumerable<PureHDF.IH5Group> DataGroups(PureHDF.IH5Group h5File)
        {
            if (h5File.LinkExists(ON_NODES))
            {
                foreach (var quantity in h5File.Group(ON_NODES).Children().OfType<PureHDF.IH5Group>())
                {
                    if (quantity.LinkExists("DATA"))
                        yield return quantity.Group("DATA");
                }
            }

            if (h5File.LinkExists(ON_ELEMENTS))
            {
                foreach (var quantity in h5File.Group(ON_ELEMENTS).Children().OfType<PureHDF.IH5Group>())
                {
                    foreach (var elementClass in quantity.Children().OfType<PureHDF.IH5Group>())
                    {
                        if (elementClass.LinkExists("DATA"))
                            yield return elementClass.Group("DATA");
                    }
                }
            }
        }

        /// <summary>
        /// Methods to return nodal Displacement, Rotation, Velocity, Acceleration
        /// </summary>
        /// <param name="alpacaModel"></param>
        /// <param name="step"></param>
        /// <param name="resultType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static IEnumerable<Rhino.Geometry.Vector3d> NodalOutput(Model alpacaModel, int step, ResultType resultType, List<int?> nodeIndex = null)
        {
            var dataOutput = Enumerable.Empty<Rhino.Geometry.Vector3d>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);           
            double[,] values;

            var _resultType = Alpaca4d.Helper.EnumHelper.ResultTypeConvert(resultType);
            if (alpacaModel.IsModal == false)
            {
                var dataset = h5File.Dataset($"/MODEL_STAGE[1]/RESULTS/ON_NODES/{_resultType}/DATA/STEP_{step}");
                var dimX = (long)dataset.Space.Dimensions[0];
                var dimY = (long)dataset.Space.Dimensions[1];

                values = dataset.Read<double>().ToArray2D(dimX, dimY);
            }
            else
            {
                var dataset = h5File.Dataset($"MODEL_STAGE[1]/RESULTS/ON_NODES/{_resultType}/DATA/STEP_0/MODE_{step}");
                var dimX = (long)dataset.Space.Dimensions[0];
                var dimY = (long)dataset.Space.Dimensions[1];

                values = dataset.Read<double>().ToArray2D(dimX, dimY);
            }


            try
            {
                // read all data base
                if (nodeIndex == null)
                {
                    for (int i = 0; i < alpacaModel.Nodes.Count; i++)
                    {
                        double x = (double)values.GetValue(i, 0);
                        double y = (double)values.GetValue(i, 1);
                        double z = (double)values.GetValue(i, 2);
                        dataOutput = dataOutput.Append(new Rhino.Geometry.Vector3d(x, y, z));
                    }
                    h5File.Dispose();
                }
                // read value only for selected nodes
                else
                {
                    foreach (int i in nodeIndex)
                    {
                        double x = (double)values.GetValue(i - 1, 0);
                        double y = (double)values.GetValue(i - 1, 1);
                        double z = (double)values.GetValue(i - 1, 2);
                        dataOutput = dataOutput.Append(new Rhino.Geometry.Vector3d(x, y, z));
                    }
                    h5File.Dispose();
                }
            }
            catch
            {
                h5File.Dispose();

                throw new Exception($"STEP_{step} not defined!");
            }

            return dataOutput;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alpacaModel"></param>
        /// <param name="step"></param>
        /// <param name="resultType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static (List<List<double>> n, List<List<double>> mz, List<List<double>> vy, List<List<double>> my, List<List<double>> vz, List<List<double>> t) ForceBeamColumn(Model alpacaModel, int step, string resultType = null)
        {
            // MPCORecorder names every element result group
            //     <classTag>-<className>[<integrationRule>:<customRuleIndex>:<headerIndex>]
            // (MPCORecorder.cpp, "create a name for this dataset using the following format").
            // forceBeamColumn always lands on integrationRule 1000 (CustomIntegrationRule), and
            // customRuleIndex is handed out in order of discovery, one per DISTINCT set of
            // normalised Gauss point locations - not one per integration type. So the index is
            // not a stable label: with NewtonCotes and HingeRadau in the same model, whichever
            // element the domain reaches first takes index 1. Worse, HingeRadau locations depend
            // on lpI/L and lpJ/L, so every distinct hinge length ratio spawns another group
            // ([1000:3:0], [1000:4:0], ...). Hard-coding a list of keys silently drops the beams
            // that fall outside it, so enumerate whatever the file actually holds and key the
            // rows by element ID, which is unique across the whole model.
            const string BASE = "/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.force";
            const string BEAM_CLASS = "ForceBeamColumn";
            const int SECTIONFORCES = 6;

            var nNested  = new List<List<double>>();
            var mzNested = new List<List<double>>();
            var vyNested = new List<List<double>>();
            var myNested = new List<List<double>>();
            var vzNested = new List<List<double>>();
            var tNested  = new List<List<double>>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            if (!h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no section forces. Switch \"section.force\" on in the Recorder component.");

            // Map: element ID -> row data (all columns for that element)
            var rowById = new Dictionary<int, double[]>();

            var beamGroups = h5File.Group(BASE)
                                   .Children()
                                   .OfType<PureHDF.IH5Group>()
                                   .Where(group => group.Name.Contains(BEAM_CLASS))
                                   .ToList();

            foreach (var group in beamGroups)
            {
                var stepGroup = group.Group("DATA");
                if (!stepGroup.LinkExists($"STEP_{step}"))
                    throw new Exception($"STEP_{step} not defined!");

                var idDataset   = group.Dataset("ID");
                var dataDataset = stepGroup.Dataset($"STEP_{step}");

                long rows = (long)dataDataset.Space.Dimensions[0];
                long cols = (long)dataDataset.Space.Dimensions[1];
                long idRows = (long)idDataset.Space.Dimensions[0];

                double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);
                int[,]    ids  = idDataset.Read<int>().ToArray2D(idRows, 1L);

                for (int r = 0; r < rows; r++)
                {
                    int elemId = ids[r, 0];
                    var rowData = new double[cols];
                    for (int c = 0; c < cols; c++)
                        rowData[c] = data[r, c];
                    rowById[elemId] = rowData;
                }
            }

            try
            {
                foreach (var beam in alpacaModel.Beams)
                {
                    var n  = new List<double>();
                    var mz = new List<double>();
                    var vy = new List<double>();
                    var my = new List<double>();
                    var vz = new List<double>();
                    var t  = new List<double>();

                    if (rowById.TryGetValue(beam.Id.Value, out double[] row))
                    {
                        // Derive the number of integration points from the column count
                        int numIP = row.Length / SECTIONFORCES;
                        for (int j = 0; j < SECTIONFORCES * numIP; j += SECTIONFORCES)
                        {
                            n.Add(row[j + 0]);
                            mz.Add(row[j + 1]);
                            vy.Add(row[j + 2]);
                            my.Add(row[j + 3]);
                            vz.Add(row[j + 4]);
                            t.Add(row[j + 5]);
                        }
                    }

                    nNested.Add(n);
                    mzNested.Add(mz);
                    vyNested.Add(vy);
                    myNested.Add(my);
                    vzNested.Add(vz);
                    tNested.Add(t);
                }
                h5File.Dispose();
            }
            catch
            {
                h5File.Dispose();
                throw new Exception($"STEP_{step} not defined!");
            }

            return (nNested, mzNested, vyNested, myNested, vzNested, tNested);
        }

        public static (List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>) ASDQ4Forces(Model alpacaModel, int step, string resultType = null)
        {
            resultType = "203-ASDShellQ4[201:0:0]";
            var fxxNested = new List<List<double>>();
            var fyyNested = new List<List<double>>();
            var fxyNested = new List<List<double>>();
            var mxxNested = new List<List<double>>();
            var myyNested = new List<List<double>>();
            var mxyNested = new List<List<double>>();
            var vxzNested = new List<List<double>>();
            var vyzNested = new List<List<double>>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            double[,] values;

            var dataset = h5File.Dataset($"/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.force/{resultType}/DATA/STEP_{step}");
            var dimX = (long)dataset.Space.Dimensions[0];
            var dimY = (long)dataset.Space.Dimensions[1];

            values = dataset.Read<double>().ToArray2D(dimX, dimY);

            var asdq4ShellNumber = alpacaModel.Shells.Where(x => x.ElementClass == Element.ElementClass.ASDShellQ4).Count();

            try
            {
                for (int i = 0; i < asdq4ShellNumber; i++)
                {
                    var fxx = new List<double>();
                    var fyy = new List<double>();
                    var fxy = new List<double>();
                    var mxx = new List<double>();
                    var myy = new List<double>();
                    var mxy = new List<double>();
                    var vxz = new List<double>();
                    var vyz = new List<double>();

                    int NUMBER_COMPONENTS = 8;
                    int NUMBER_NODES = 4;
                    for (int j = 0; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 1; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fyy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 2; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 3; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 4; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        myy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 5; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 6; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vxz.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 7; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vyz.Add((double)values.GetValue(i, j));
                    }

                    fxxNested.Add(fxx);
                    fyyNested.Add(fyy);
                    fxyNested.Add(fxy);
                    mxxNested.Add(mxx);
                    myyNested.Add(myy);
                    mxyNested.Add(mxy);
                    vxzNested.Add(vxz);
                    vyzNested.Add(vyz);
                }

                h5File.Dispose();
            }
            catch
            {
                h5File.Dispose();
                throw new Exception($"STEP_{step} not defined!");
            }

            return (fxxNested, fyyNested, fxyNested, mxxNested, myyNested, mxyNested, vxzNested, vyzNested);
        }

        public static (List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>) DKGTForces(Model alpacaModel, int step, string resultType = null)
        {
            resultType = "167-ShellDKGT[103:0:0]"; // DKGT
            //resultType = "168-UnknownMovableObject[103:0:0]"; // NLDKGT
            var fxxNested = new List<List<double>>();
            var fyyNested = new List<List<double>>();
            var fxyNested = new List<List<double>>();
            var mxxNested = new List<List<double>>();
            var myyNested = new List<List<double>>();
            var mxyNested = new List<List<double>>();
            var vxzNested = new List<List<double>>();
            var vyzNested = new List<List<double>>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            double[,] values;

            var dataset = h5File.Dataset($"/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.force/{resultType}/DATA/STEP_{step}");
            var dimX = (long)dataset.Space.Dimensions[0];
            var dimY = (long)dataset.Space.Dimensions[1];

            values = dataset.Read<double>().ToArray2D(dimX, dimY);

            var dkgtShellNumber = alpacaModel.Shells.Where(x => x.ElementClass == Element.ElementClass.ShellDKGT).Count();

            try
            {
                for (int i = 0; i < dkgtShellNumber; i++)
                {
                    var fxx = new List<double>();
                    var fyy = new List<double>();
                    var fxy = new List<double>();
                    var mxx = new List<double>();
                    var myy = new List<double>();
                    var mxy = new List<double>();
                    var vxz = new List<double>();
                    var vyz = new List<double>();

                    int NUMBER_COMPONENTS = 8;
                    int NUMBER_NODES = 3;
                    for (int j = 0; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 1; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fyy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 2; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 3; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 4; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        myy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 5; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 6; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vxz.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 7; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vyz.Add((double)values.GetValue(i, j));
                    }

                    fxxNested.Add(fxx);
                    fyyNested.Add(fyy);
                    fxyNested.Add(fxy);
                    mxxNested.Add(mxx);
                    myyNested.Add(myy);
                    mxyNested.Add(mxy);
                    vxzNested.Add(vxz);
                    vyzNested.Add(vyz);
                }

                h5File.Dispose();
            }
            catch
            {
                h5File.Dispose();
                throw new Exception($"STEP_{step} not defined!");
            }

            return (fxxNested, fyyNested, fxyNested, mxxNested, myyNested, mxyNested, vxzNested, vyzNested);
        }

        public static (List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>, List<List<double>>) ASDT3Forces(Model alpacaModel, int step, string resultType = null)
        {
            resultType = "204-ASDShellT3[102:0:0]"; // ASDShellT3
            var fxxNested = new List<List<double>>();
            var fyyNested = new List<List<double>>();
            var fxyNested = new List<List<double>>();
            var mxxNested = new List<List<double>>();
            var myyNested = new List<List<double>>();
            var mxyNested = new List<List<double>>();
            var vxzNested = new List<List<double>>();
            var vyzNested = new List<List<double>>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            double[,] values;

            var dataset = h5File.Dataset($"/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.force/{resultType}/DATA/STEP_{step}");
            var dimX = (long)dataset.Space.Dimensions[0];
            var dimY = (long)dataset.Space.Dimensions[1];

            values = dataset.Read<double>().ToArray2D(dimX, dimY);

            var asdt3ShellNumber = alpacaModel.Shells.Where(x => x.ElementClass == Element.ElementClass.ASDShellT3).Count();

            try
            {
                for (int i = 0; i < asdt3ShellNumber; i++)
                {
                    var fxx = new List<double>();
                    var fyy = new List<double>();
                    var fxy = new List<double>();
                    var mxx = new List<double>();
                    var myy = new List<double>();
                    var mxy = new List<double>();
                    var vxz = new List<double>();
                    var vyz = new List<double>();

                    int NUMBER_COMPONENTS = 8;
                    int NUMBER_NODES = 3;
                    for (int j = 0; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 1; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fyy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 2; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        fxy.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 3; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxx.Add((double)values.GetValue(i, j));
                    }

                    for (int j = 4; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        myy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 5; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        mxy.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 6; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vxz.Add((double)values.GetValue(i, j));
                    }
                    for (int j = 7; j < NUMBER_COMPONENTS * NUMBER_NODES; j += NUMBER_COMPONENTS)
                    {
                        vyz.Add((double)values.GetValue(i, j));
                    }

                    fxxNested.Add(fxx);
                    fyyNested.Add(fyy);
                    fxyNested.Add(fxy);
                    mxxNested.Add(mxx);
                    myyNested.Add(myy);
                    mxyNested.Add(mxy);
                    vxzNested.Add(vxz);
                    vyzNested.Add(vyz);
                }

                h5File.Dispose();
            }
            catch
            {
                h5File.Dispose();
                throw new Exception($"STEP_{step} not defined!");
            }

            return (fxxNested, fyyNested, fxyNested, mxxNested, myyNested, mxyNested, vxzNested, vyzNested);
        }

        /// <summary>
        /// The stress at one through-thickness station of one shell, at one Gauss point.
        /// </summary>
        public struct ShellFibreStress
        {
            /// <summary>Element tag.</summary>
            public int ElementId;
            /// <summary>Gauss point, 0-based.</summary>
            public int GaussPoint;
            /// <summary>Station through the thickness, 0-based, counting from the bottom face up.</summary>
            public int Fibre;
            /// <summary>Direct stress along the section's local 1 axis.</summary>
            public double S11;
            /// <summary>Direct stress along the section's local 2 axis.</summary>
            public double S22;
            /// <summary>In-plane shear.</summary>
            public double S12;
            /// <summary>Transverse shear, local 2-3 plane.</summary>
            public double S23;
            /// <summary>Transverse shear, local 3-1 plane.</summary>
            public double S31;

            /// <summary>
            /// Von Mises equivalent stress. A shell fibre is in plane stress - the through-thickness
            /// direct stress is condensed out by PlateFiberMaterial - so s33 is zero and the general
            /// form collapses to this.
            /// </summary>
            public double VonMises
            {
                get
                {
                    return System.Math.Sqrt(
                        S11 * S11 - S11 * S22 + S22 * S22 +
                        3.0 * (S12 * S12 + S23 * S23 + S31 * S31));
                }
            }
        }

        /// <summary>
        /// Reads the true stresses through the thickness of every shell, from the recorder's
        /// "section.fiber.stress" group.
        ///
        /// Not to be confused with what Shell Forces reads. That one is "section.force", the stress
        /// resultants - forces and moments per unit width, the whole thickness collapsed into eight
        /// numbers. These are stresses proper, in force over area, at stations through the depth.
        /// OpenSees also offers an element level "stresses" response, but for a shell that is the
        /// same stress resultant again under a misleading name: ASDShellQ4::getResponse case 2 loops
        /// getStressResultant(), exactly what section.force returns. Checked against OpenSees 3.5 on
        /// a two element model: the two datasets came back bit for bit identical.
        ///
        /// The layout is read from the file's own META rather than assumed, because the number of
        /// stations depends on the section. A PlateFiber section has five, at the Lobatto points
        /// -1, -0.6547, 0, 0.6547, 1 of the thickness, so the first and last sit exactly on the
        /// bottom and top faces. A LayeredShell section has one per layer, at the layer centres,
        /// which do not reach the faces. Either way they are ordered bottom to top.
        ///
        /// Columns run gauss-major: gauss, then fibre, then component.
        /// </summary>
        public static List<ShellFibreStress> ShellFibreStresses(Model alpacaModel, int step)
        {
            const string BASE = "/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.fiber.stress";

            var output = new List<ShellFibreStress>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            if (!h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no through-thickness stresses. Switch \"section.fiber.stress\" " +
                    "on in the Recorder component and run the analysis again.");

            // One group per element class, named <classTag>-<className>[<rule>:<index>:<header>].
            // Enumerated rather than named: the index is handed out in order of discovery, so it is
            // not a stable label, and a model can hold quads and triangles at once.
            foreach (var group in h5File.Group(BASE).Children().OfType<PureHDF.IH5Group>())
            {
                if (!group.LinkExists("DATA") || !group.LinkExists("ID") || !group.LinkExists("META"))
                    continue;

                var dataGroup = group.Group("DATA");
                if (!dataGroup.LinkExists($"STEP_{step}"))
                    throw new Exception($"STEP_{step} not defined!");

                var meta = group.Group("META");

                // One entry per Gauss point. MULTIPLICITY is how many stations that Gauss point
                // holds, NUM_COMPONENTS how many numbers each station holds.
                int[,] multiplicity = ReadIntColumn(meta, "MULTIPLICITY");
                int[,] numComponents = ReadIntColumn(meta, "NUM_COMPONENTS");
                if (multiplicity == null || numComponents == null)
                    continue;

                int gaussCount = multiplicity.GetLength(0);

                var idDataset = group.Dataset("ID");
                var dataDataset = dataGroup.Dataset($"STEP_{step}");

                long rows = (long)dataDataset.Space.Dimensions[0];
                long cols = (long)dataDataset.Space.Dimensions[1];
                long idRows = (long)idDataset.Space.Dimensions[0];

                double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);
                int[,] ids = idDataset.Read<int>().ToArray2D(idRows, 1L);

                for (int r = 0; r < rows; r++)
                {
                    int elementId = ids[r, 0];
                    int column = 0;

                    for (int gauss = 0; gauss < gaussCount; gauss++)
                    {
                        int fibres = multiplicity[gauss, 0];
                        int components = numComponents[gauss, 0];

                        for (int fibre = 0; fibre < fibres; fibre++)
                        {
                            if (column + components > cols)
                                break;

                            // Five components for a plate fibre: PlateFiberMaterial condenses the
                            // through-thickness direct stress out and reports 11, 22, 12, 23, 31.
                            // Anything shorter is padded with zero rather than dropped, so an
                            // unusual material cannot cost the components that are there.
                            var entry = new ShellFibreStress
                            {
                                ElementId = elementId,
                                GaussPoint = gauss,
                                Fibre = fibre,
                                S11 = components > 0 ? data[r, column + 0] : 0.0,
                                S22 = components > 1 ? data[r, column + 1] : 0.0,
                                S12 = components > 2 ? data[r, column + 2] : 0.0,
                                S23 = components > 3 ? data[r, column + 3] : 0.0,
                                S31 = components > 4 ? data[r, column + 4] : 0.0,
                            };

                            output.Add(entry);
                            column += components;
                        }
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// Which of the stations through the thickness are the top, the middle and the bottom, in
        /// that order, given how many there are.
        ///
        /// Stations run bottom to top, so the top is the last and the bottom the first. Whether
        /// those sit on the faces depends on the section: a PlateFiber section integrates over five
        /// Lobatto points, whose first and last are the faces exactly and whose third is the
        /// mid-surface exactly; a LayeredShell section reports its layer centres, so its outermost
        /// stations are half a layer in from the faces. An even number of layers has no station at
        /// the mid-surface at all, and the one just above it is used.
        ///
        /// One copy, because the Shell Stresses component and its view have to agree about which
        /// station "Top" means.
        /// </summary>
        public static int[] LayerFibres(int fibreCount)
        {
            if (fibreCount <= 0)
                return new int[0];

            return new[] { fibreCount - 1, fibreCount / 2, 0 };
        }

        /// <summary>The layers <see cref="LayerFibres"/> hands back, in the same order.</summary>
        public static readonly string[] LayerNames = { "Top", "Middle", "Bottom" };

        /// <summary>A META column of ints, or null when the file does not hold it.</summary>
        private static int[,] ReadIntColumn(PureHDF.IH5Group meta, string name)
        {
            if (!meta.LinkExists(name))
                return null;

            var dataset = meta.Dataset(name);
            long rows = (long)dataset.Space.Dimensions[0];

            return dataset.Read<int>().ToArray2D(rows, 1L);
        }


        /// <summary>
        /// The six stress components of every solid of one class, one value per element, in the
        /// order the model lists those elements.
        ///
        /// Keyed by element tag rather than by row position. The recorder groups its rows by
        /// element class and writes an ID dataset alongside them saying which element each row
        /// belongs to; reading rows positionally only happens to work while the model's own order
        /// and the file's agree, which is a coincidence of how Alpaca4d hands out tags rather than
        /// anything the format promises.
        ///
        /// The group is found by class name rather than named outright. The recorder's directory
        /// name carries an integration rule and a header index - "121-SSPbrick[400:0:0]" - and the
        /// header index counts up when one class produces two different response layouts, so two
        /// materials answering "stresses" differently put half the elements in a group that a
        /// hard-coded name never opens. Every group for the class is read and merged instead.
        /// </summary>
        /// <param name="className">The element's OpenSees class name, as it appears in the group name.</param>
        /// <param name="elements">The elements to report on, in the order they are to be reported.</param>
        /// <param name="local">
        /// Report in each element's own axes rather than the global ones. The frame comes from the
        /// node numbering - see Utils.SolidAxes - and exists only for reading: the analysis itself
        /// is run in the global axes whatever this says.
        /// </param>
        private static (List<double>, List<double>, List<double>, List<double>, List<double>, List<double>)
            SolidStress(Model alpacaModel, int step, string className, IReadOnlyList<Generic.IBrick> elements, bool local)
        {
            const string BASE = ON_ELEMENTS + "/stresses";
            const int COMPONENTS = 6;

            var sigma = new List<double>[COMPONENTS];
            for (int c = 0; c < COMPONENTS; c++)
                sigma[c] = new List<double>();

            if (elements.Count == 0)
                return (sigma[0], sigma[1], sigma[2], sigma[3], sigma[4], sigma[5]);

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            if (!h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no stresses. Switch \"stresses\" on in the Recorder component.");

            var rowById = new Dictionary<int, double[]>();

            var groups = h5File.Group(BASE)
                               .Children()
                               .OfType<PureHDF.IH5Group>()
                               .Where(group => group.Name.Contains(className))
                               .ToList();

            foreach (var group in groups)
            {
                if (!group.LinkExists("DATA") || !group.Group("DATA").LinkExists($"{STEP_PREFIX}{step}"))
                    throw new Exception($"STEP_{step} not defined!");

                var idDataset = group.Dataset("ID");
                var dataDataset = group.Group("DATA").Dataset($"{STEP_PREFIX}{step}");

                long rows = (long)dataDataset.Space.Dimensions[0];
                long cols = (long)dataDataset.Space.Dimensions[1];

                double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);
                int[,] ids = idDataset.Read<int>().ToArray2D((long)idDataset.Space.Dimensions[0], 1L);

                for (int r = 0; r < rows; r++)
                {
                    var row = new double[cols];
                    for (int c = 0; c < cols; c++)
                        row[c] = data[r, c];

                    rowById[ids[r, 0]] = row;
                }
            }

            foreach (var element in elements)
            {
                if (element.Id == null || !rowById.TryGetValue(element.Id.Value, out double[] row) || row.Length < COMPONENTS)
                    throw new Exception(
                        $"The recorder file holds no stresses for {className} {element.Id}. The file was " +
                        "written by a different model from the one being read - re-run the analysis.");

                // The solver works in the global axes and so does the file. Turning the tensor into
                // the element's own frame is done here rather than by the caller because the six
                // numbers only mean anything together, and a caller holding six separate lists has
                // already lost the tensor.
                var values = row;
                if (local)
                {
                    var frame = Utils.SolidFrame(element.Mesh.Vertices.ToPoint3dArray());
                    values = Utils.StressInFrame(row, frame.X, frame.Y, frame.Z);
                }

                for (int c = 0; c < COMPONENTS; c++)
                    sigma[c].Add(values[c]);
            }

            return (sigma[0], sigma[1], sigma[2], sigma[3], sigma[4], sigma[5]);
        }

        /// <summary>
        /// The stress in every four node tetrahedron, one value per element, in the order the model
        /// lists them.
        ///
        /// The six components are sigma11, sigma22, sigma33, sigma12, sigma23 and sigma13, which is
        /// the order FourNodeTetrahedron::setResponse names them in. They come out of the solver in
        /// the global axes - the element builds its strain from global nodal displacements and hands
        /// it straight to the nD material, and neither the element nor the material has a frame of
        /// its own - and <paramref name="local"/> turns them into the element's own.
        /// </summary>
        public static (List<double>, List<double>, List<double>, List<double>, List<double>, List<double>) TetrahedronStress(Model alpacaModel, int step, string resultType = null, bool local = false)
        {
            var tetrahedra = alpacaModel.Bricks
                .Where(x => x.ElementClass == Element.ElementClass.FourNodeTetrahedron)
                .ToList();

            return SolidStress(alpacaModel, step, "FourNodeTetrahedron", tetrahedra, local);
        }

        /// <summary>
        /// The stress in every SSP brick, one value per element, in the order the model lists them.
        /// Same six components and same axes as <see cref="TetrahedronStress"/>; SSPbrick has no
        /// "stresses" response of its own and passes the request to its material, which answers in
        /// the three dimensional order sigma11, sigma22, sigma33, sigma12, sigma23, sigma31.
        /// </summary>
        public static (List<double>, List<double>, List<double>, List<double>, List<double>, List<double>) SSPBrickStress(Model alpacaModel, int step, string resultType = null, bool local = false)
        {
            var bricks = alpacaModel.Bricks
                .Where(x => x.ElementClass == Element.ElementClass.SSPBrick)
                .ToList();

            return SolidStress(alpacaModel, step, "SSPbrick", bricks, local);
        }


        /// <summary>
        /// Every fibre's stress and strain history, from a "section fiberData" recorder
        /// file. Each line is one analysis step and carries five numbers per fibre -
        /// y, z, area, stress, strain - in the section's own fibre order.
        /// </summary>
        /// <param name="fiberCount">
        /// How many fibres the section has. The file is read against this rather than
        /// inferred from it, so a step written short - the solver killed mid-write - is
        /// dropped instead of silently shifting every fibre after it.
        /// </param>
        public static List<(List<double> Stress, List<double> Strain)> FiberData(string filePath, int fiberCount)
        {
            const int ValuesPerFiber = 5;
            const int StressOffset = 3;
            const int StrainOffset = 4;

            var histories = new List<(List<double> Stress, List<double> Strain)>(fiberCount);
            for (var i = 0; i < fiberCount; i++)
                histories.Add((new List<double>(), new List<double>()));

            if (fiberCount <= 0 || !System.IO.File.Exists(filePath))
                return histories;

            foreach (var line in System.IO.File.ReadAllLines(filePath))
            {
                var values = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (values.Length < fiberCount * ValuesPerFiber)
                    continue;

                for (var i = 0; i < fiberCount; i++)
                {
                    var at = i * ValuesPerFiber;
                    histories[i].Stress.Add(TclNumber.Read(values[at + StressOffset]));
                    histories[i].Strain.Add(TclNumber.Read(values[at + StrainOffset]));
                }
            }

            return histories;
        }

    }

    // Class Created to wrap an object in a single output for Grasshopper
    public partial class PointFiberResult
    {
        public DataTree<double> Stress { get; set; } = new DataTree<double>();
        public DataTree<double> Strain { get; set; } = new DataTree<double>();
        public DataTree<Alpaca4d.Section.PointFiber> Fibers { get; set; } = new DataTree<Alpaca4d.Section.PointFiber>();

        public PointFiberResult()
        {
        }
    }
}
