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
        /// Whether the recorder file holds a nodal result at all. An MPCO Recorder writes only what
        /// is ticked in it, and a file from an older Alpaca4d has no angular velocity or angular
        /// acceleration, so a component with an output per result asks before it reads rather than
        /// failing every output over the one that is not there.
        /// </summary>
        public static bool Holds(Model alpacaModel, ResultType resultType)
        {
            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            return h5File.LinkExists($"{ON_NODES}/{Alpaca4d.Helper.EnumHelper.ResultTypeConvert(resultType)}");
        }

        /// <summary>
        /// A nodal result as the recorder command names it - reactionForce - where the file
        /// calls it REACTION_FORCE. It is the name on the tick box that records it.
        /// </summary>
        public static string RecorderName(ResultType resultType)
        {
            if (resultType == ResultType.MODES_OF_VIBRATION_U)
                return "modesOfVibration";
            if (resultType == ResultType.MODES_OF_VIBRATION_R)
                return "modesOfVibrationRotational";

            var words = resultType.ToString().ToLowerInvariant().Split('_');
            return words[0] + string.Concat(words.Skip(1).Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
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
            // A list, not Enumerable.Append in a loop: that chains one iterator per node, and a
            // caller reading node i walks the chain from the start, so reading every node of a
            // large model took time growing as the square of its size.
            var dataOutput = new List<Rhino.Geometry.Vector3d>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);           
            double[,] values;

            var _resultType = Alpaca4d.Helper.EnumHelper.ResultTypeConvert(resultType);
            if (!h5File.LinkExists($"{ON_NODES}/{_resultType}"))
                throw new Exception($"The recorder file holds no {RecorderName(resultType)}. " +
                    $"Switch \"{RecorderName(resultType)}\" on in the MPCO Recorder component.");

            if (alpacaModel.IsModal == false)
            {
                var dataset = h5File.Dataset($"/MODEL_STAGE[1]/RESULTS/ON_NODES/{_resultType}/DATA/STEP_{step}");
                var dimX = (long)dataset.Space.Dimensions[0];
                var dimY = (long)dataset.Space.Dimensions[1];

                values = dataset.Read<double>().ToArray2D(dimX, dimY);
            }
            else
            {
                values = ModeData(h5File, _resultType, step);

                // Rotations by the factor of the translations of the same mode, so the two still
                // make one shape.
                var translations = resultType == ResultType.MODES_OF_VIBRATION_R
                    ? ModeData(h5File, Alpaca4d.Helper.EnumHelper.ResultTypeConvert(ResultType.MODES_OF_VIBRATION_U), step)
                    : values;
                double scale = ModeScale(translations);
                for (int i = 0; i < values.GetLength(0); i++)
                    for (int j = 0; j < values.GetLength(1); j++)
                        values[i, j] *= scale;
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
                        dataOutput.Add(new Rhino.Geometry.Vector3d(x, y, z));
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
                        dataOutput.Add(new Rhino.Geometry.Vector3d(x, y, z));
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

        /// <summary>Mode <paramref name="mode"/> of a modal run, a node per row, as the file holds it.</summary>
        private static double[,] ModeData(PureHDF.IH5Group h5File, string resultType, int mode)
        {
            var dataset = h5File.Dataset($"MODEL_STAGE[1]/RESULTS/ON_NODES/{resultType}/DATA/STEP_0/MODE_{mode}");
            var dimX = (long)dataset.Space.Dimensions[0];
            var dimY = (long)dataset.Space.Dimensions[1];

            return dataset.Read<double>().ToArray2D(dimX, dimY);
        }

        /// <summary>
        /// What a mode's translations, a node per row, are multiplied by to make its largest
        /// translation 1, in model length units, pointing the positive way - the size
        /// <see cref="Buckling.Normalise"/> gives a buckling mode.
        ///
        /// OpenSees hands the modes out mass-normalised, φᵀMφ = 1, so their size went with the mass
        /// of the model: the tip of one cantilever moved 3.2 m at 0.39 t and 0.03 m at 3925 t, and a
        /// heavy building needed a deformation scale of hundreds to show anything. The -unorm that
        /// Natural Vibration passes to modalProperties scales the copy its report is worked out
        /// from, not the modes in the domain the recorder writes.
        /// </summary>
        public static double ModeScale(double[,] translations)
        {
            double largest = 0.0, signed = 0.0;
            for (int i = 0; i < translations.GetLength(0); i++)
                for (int j = 0; j < Math.Min(3, translations.GetLength(1)); j++)
                    if (Math.Abs(translations[i, j]) > largest)
                    {
                        largest = Math.Abs(translations[i, j]);
                        signed = translations[i, j];
                    }

            return largest == 0.0 ? 1.0 : 1.0 / signed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alpacaModel"></param>
        /// <param name="step"></param>
        /// <param name="resultType"></param>
        /// <param name="only">The places in Model.Beams to read, or null for every beam. The others
        /// come back as empty lists, so the lists still run in the order of Model.Beams. A handful of
        /// beams out of a large model are read row by row, which skips most of the file.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static (List<List<double>> n, List<List<double>> mz, List<List<double>> vy, List<List<double>> my, List<List<double>> vz, List<List<double>> t) ForceBeamColumn(Model alpacaModel, int step, string resultType = null, ICollection<int> only = null)
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
            const string TRUSS_BASE = "/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/axialForce";
            const string BEAM_CLASS = "ForceBeamColumn";

            var nNested  = new List<List<double>>();
            var mzNested = new List<List<double>>();
            var vyNested = new List<List<double>>();
            var myNested = new List<List<double>>();
            var vzNested = new List<List<double>>();
            var tNested  = new List<List<double>>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            // Trusses answer "axialForce" and nothing else does; beams answer "section.force". A file
            // needs only what the model has.
            bool hasTrusses = alpacaModel.Beams.Any(beam => beam is Alpaca4d.Element.Truss);
            bool hasFrames = alpacaModel.Beams.Any(beam => !(beam is Alpaca4d.Element.Truss));

            if (hasFrames && !h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no section forces. Switch \"section.force\" on in the MPCO Recorder component.");
            if (hasTrusses && !h5File.LinkExists(TRUSS_BASE))
                throw new Exception(
                    "The recorder file holds no truss forces. Switch \"axialForce\" on in the MPCO Recorder component.");

            // Map: element ID -> row data (all columns for that element), and where each section
            // force sits in it
            var rowById = new Dictionary<int, (double[] Row, int[][] Columns)>();

            // The element tags of the beams asked for; null keeps every row.
            var wanted = only == null ? null : new HashSet<int>(only.Select(b => alpacaModel.Beams[b].Id.Value));

            var beamGroups = !h5File.LinkExists(BASE) ? new List<PureHDF.IH5Group>() :
                             h5File.Group(BASE)
                                   .Children()
                                   .OfType<PureHDF.IH5Group>()
                                   .Where(group => group.Name.Contains(BEAM_CLASS))
                                   .ToList();

            // A truss writes one number, its axial force, under axialForce/<tag>-CorotTruss[...].
            var axialById = new Dictionary<int, double>();
            if (hasTrusses)
            {
                foreach (var group in h5File.Group(TRUSS_BASE).Children().OfType<PureHDF.IH5Group>())
                {
                    var stepGroup = group.Group("DATA");
                    if (!stepGroup.LinkExists($"STEP_{step}"))
                        throw new Exception($"STEP_{step} not defined!");

                    var dataDataset = stepGroup.Dataset($"STEP_{step}");
                    long rows = (long)dataDataset.Space.Dimensions[0];
                    long cols = (long)dataDataset.Space.Dimensions[1];
                    double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);
                    int[] ids = ElementIds(recorderPath, TRUSS_BASE, group);

                    for (int r = 0; r < rows; r++)
                        if (wanted == null || wanted.Contains(ids[r]))
                            axialById[ids[r]] = data[r, 0];
                }
            }

            foreach (var group in beamGroups)
            {
                var stepGroup = group.Group("DATA");
                if (!stepGroup.LinkExists($"STEP_{step}"))
                    throw new Exception($"STEP_{step} not defined!");

                var dataDataset = stepGroup.Dataset($"STEP_{step}");

                long rows = (long)dataDataset.Space.Dimensions[0];
                long cols = (long)dataDataset.Space.Dimensions[1];

                int[] ids = ElementIds(recorderPath, BASE, group);
                int[][] columns = SectionColumns(recorderPath, BASE, group, (int)cols);

                // A few rows asked for are read one by one, which skips reading the rest of the
                // file; past an eighth of the group one read of the whole of it is quicker.
                if (wanted != null)
                {
                    var picked = Enumerable.Range(0, (int)rows).Where(r => wanted.Contains(ids[r])).ToList();
                    if (picked.Count * 8 < rows)
                    {
                        foreach (int r in picked)
                            rowById[ids[r]] = (dataDataset.Read<double>(new PureHDF.HyperslabSelection(2, new[] { (ulong)r, 0UL }, new[] { 1UL, (ulong)cols })), columns);
                        continue;
                    }
                }

                double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);

                for (int r = 0; r < rows; r++)
                {
                    int elemId = ids[r];
                    if (wanted != null && !wanted.Contains(elemId))
                        continue;
                    var rowData = new double[cols];
                    for (int c = 0; c < cols; c++)
                        rowData[c] = data[r, c];
                    rowById[elemId] = (rowData, columns);
                }
            }

            // Node positions by tag, for the length of a beam whose shears have to be worked out.
            Dictionary<int, Point3d> positions = null;

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

                    // A truss reports its axial force at both ends, which is where its two stations are
                    // (Truss.BeamIntegration), and no shear, moment or torsion.
                    if (beam is Alpaca4d.Element.Truss && axialById.TryGetValue(beam.Id.Value, out double axial))
                    {
                        n.AddRange(new[] { axial, axial });
                        mz.AddRange(new[] { 0.0, 0.0 });
                        vy.AddRange(new[] { 0.0, 0.0 });
                        my.AddRange(new[] { 0.0, 0.0 });
                        vz.AddRange(new[] { 0.0, 0.0 });
                        t.AddRange(new[] { 0.0, 0.0 });
                    }
                    else if (rowById.TryGetValue(beam.Id.Value, out var entry))
                    {
                        Func<int, double> at = column => column < 0 ? double.NaN : entry.Row[column];
                        foreach (var station in entry.Columns)
                        {
                            n.Add(at(station[0]));
                            mz.Add(at(station[1]));
                            vy.Add(at(station[2]));
                            my.Add(at(station[3]));
                            vz.Add(at(station[4]));
                            t.Add(at(station[5]));
                        }

                        // A section without shear - an Elastic section given no shear factors - records
                        // P, Mz, My and T only. Its shears are the slopes of its moments, Vy = dMz/dx and
                        // Vz = dMy/dx as OpenSees has them, and exactly so: a force-based beam's moments
                        // follow from equilibrium, and under the uniform loads that are the only ones
                        // Alpaca4d puts on a beam they are at most quadratic along it.
                        if (vy.Any(double.IsNaN) || vz.Any(double.IsNaN))
                        {
                            positions ??= alpacaModel.Nodes.Where(node => node.Id.HasValue)
                                                           .GroupBy(node => node.Id.Value)
                                                           .ToDictionary(group => group.Key, group => group.First().Pos);
                            double length = positions.TryGetValue(beam.INode ?? 0, out var i) && positions.TryGetValue(beam.JNode ?? 0, out var j)
                                ? i.DistanceTo(j)
                                : beam.Curve != null ? beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd) : 0.0;
                            var xi = beam.BeamIntegration?.SectionLocations(length);
                            if (length > 0.0 && xi != null && xi.Count == n.Count)
                            {
                                Slopes(xi, mz, vy, length);
                                Slopes(xi, my, vz, length);
                            }
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

        /// <summary>The names OpenSees gives a beam section's deformations that a fibre section's strains follow from.</summary>
        private static readonly string[] SectionDeformationNames = { "eps", "kappaZ", "kappaY" };

        /// <summary>
        /// Every beam's section deformations at <paramref name="step"/>, by element tag: for each
        /// station, from the I end, the axial strain at the section's centroid and its curvatures about
        /// local z and y - eps, kappaZ and kappaY as OpenSees names them, from which a fibre section's
        /// every strain follows, eps - y kappaZ + z kappaY. Read from the recorder's
        /// "section.deformation" group, which Pushover Analysis records, and Run Analysis for a model
        /// with fibre sections; an exception names what to tick when the file has none.
        /// </summary>
        public static Dictionary<int, double[][]> SectionDeformations(Model alpacaModel, int step) =>
            SectionDeformations(alpacaModel, new[] { step })[0];

        /// <summary>As <see cref="SectionDeformations(Model, int)"/>, at each of <paramref name="steps"/>, the file opened once.</summary>
        public static List<Dictionary<int, double[][]>> SectionDeformations(Model alpacaModel, IReadOnlyList<int> steps)
        {
            const string BASE = "/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/section.deformation";
            var results = steps.Select(_ => new Dictionary<int, double[][]>()).ToList();
            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            if (!h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no section deformations. Pushover Analysis records them; for Run Analysis, " +
                    "tick \"section.deformation\" in an MPCO Recorder.");

            foreach (var group in h5File.Group(BASE).Children().OfType<PureHDF.IH5Group>().Where(g => g.Name.Contains("ForceBeamColumn")))
            {
                var data = group.Group("DATA");
                int[] ids = ElementIds(recorderPath, BASE, group);
                for (int k = 0; k < steps.Count; k++)
                {
                    if (!data.LinkExists($"STEP_{steps[k]}"))
                        throw new Exception($"STEP_{steps[k]} not defined!");
                    var dataset = data.Dataset($"STEP_{steps[k]}");
                    long rows = (long)dataset.Space.Dimensions[0];
                    long cols = (long)dataset.Space.Dimensions[1];
                    double[,] values = dataset.Read<double>().ToArray2D(rows, cols);
                    int[][] columns = StationColumns(recorderPath, BASE, group, (int)cols, SectionDeformationNames, 4);

                    for (int r = 0; r < rows; r++)
                    {
                        var stations = new double[columns.Length][];
                        for (int s = 0; s < columns.Length; s++)
                            stations[s] = columns[s].Select(c => c < 0 ? 0.0 : values[r, c]).ToArray();
                        results[k][ids[r]] = stations;
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Where each of <paramref name="names"/> sits in a row of a beam's section result group,
        /// station by station, read off META/COMPONENTS as <see cref="SectionColumns"/> reads it; -1 for
        /// one the section does not record. A file without the names is read <paramref name="perStation"/>
        /// to a station, in the order of the names.
        /// </summary>
        private static int[][] StationColumns(string recorderPath, string basePath, PureHDF.IH5Group group, int rowLength,
                                              string[] names, int perStation) =>
            GroupInfo(recorderPath, basePath + "/" + group.Name + "#COMPONENTS", () =>
            {
                if (!group.LinkExists("META/COMPONENTS"))
                    return Enumerable.Range(0, rowLength / perStation)
                                     .Select(s => Enumerable.Range(s * perStation, names.Length).ToArray())
                                     .ToArray();

                var stations = string.Concat(group.Dataset("META/COMPONENTS").ReadString())
                                     .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var columns = new int[stations.Length][];
                int offset = 0;
                for (int s = 0; s < stations.Length; s++)
                {
                    var recorded = stations[s].Substring(stations[s].LastIndexOf('.') + 1).Split(',');
                    columns[s] = names.Select(name => Array.IndexOf(recorded, name)).Select(k => k < 0 ? -1 : offset + k).ToArray();
                    offset += recorded.Length;
                }
                return columns;
            });

        /// <summary>The recorder file <see cref="_groupInfo"/> holds what it holds of, as path, size and time written.</summary>
        private static string _groupInfoFile;

        /// <summary>What is the same at every step of a result group of that file, by the group's path and what it is.</summary>
        private static readonly Dictionary<string, object> _groupInfo = new Dictionary<string, object>();

        /// <summary>
        /// What <paramref name="read"/> reads of a result group, read once per file and kept: the
        /// element tags of its rows and where its columns sit are the same at every step. A file
        /// written again, by another run of the analysis, has another size or time and starts afresh.
        /// </summary>
        private static T GroupInfo<T>(string recorderPath, string key, Func<T> read)
        {
            var file = new System.IO.FileInfo(recorderPath);
            string stamp = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";

            lock (_groupInfo)
            {
                if (_groupInfoFile != stamp)
                {
                    _groupInfo.Clear();
                    _groupInfoFile = stamp;
                }

                if (!_groupInfo.TryGetValue(key, out var info))
                    _groupInfo[key] = info = read();
                return (T)info;
            }
        }

        /// <summary>
        /// The element tag of each row of a result group, from its ID dataset. Kept per file, as
        /// PureHDF takes as long over a group's tags as over a step's whole data - 80 ms for the
        /// 45,300 tags of a grillage, against 85 ms for its 1.4 million forces - so reading a few
        /// beams, or every step of a history, would otherwise spend most of its time reading the same
        /// tags again.
        /// </summary>
        private static int[] ElementIds(string recorderPath, string basePath, PureHDF.IH5Group group) =>
            GroupInfo(recorderPath, basePath + "/" + group.Name + "#ID", () => group.Dataset("ID").Read<int>());

        /// <summary>The names OpenSees gives a beam section's forces, in the order Read.ForceBeamColumn returns them.</summary>
        private static readonly string[] SectionForceNames = { "P", "Mz", "Vy", "My", "Vz", "T" };

        /// <summary>
        /// Where each section force sits in a row of a section.force group, station by station: the
        /// column of P, Mz, Vy, My, Vz and T, or -1 for one the section does not record.
        ///
        /// Read off the group's META/COMPONENTS, which names the columns of every station - for
        /// instance "0.1.2.P,Mz,My,T" - rather than assumed. A section with shear records six forces
        /// a station, an Elastic section given no shear factors four, and taking six from a row of
        /// fours read a cantilever's five stations as three, with the wrong forces in them. A file
        /// without the names is read six to a station, as it always was.
        /// </summary>
        private static int[][] SectionColumns(string recorderPath, string basePath, PureHDF.IH5Group group, int rowLength) =>
            GroupInfo(recorderPath, basePath + "/" + group.Name + "#COMPONENTS", () =>
            {
                if (!group.LinkExists("META/COMPONENTS"))
                {
                    return Enumerable.Range(0, rowLength / SectionForceNames.Length)
                                     .Select(s => Enumerable.Range(s * SectionForceNames.Length, SectionForceNames.Length).ToArray())
                                     .ToArray();
                }

                var stations = string.Concat(group.Dataset("META/COMPONENTS").ReadString())
                                     .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var columns = new int[stations.Length][];
                int offset = 0;
                for (int s = 0; s < stations.Length; s++)
                {
                    var names = stations[s].Substring(stations[s].LastIndexOf('.') + 1).Split(',');
                    columns[s] = SectionForceNames.Select(name => Array.IndexOf(names, name)).Select(k => k < 0 ? -1 : offset + k).ToArray();
                    offset += names.Length;
                }
                return columns;
            });

        /// <summary>
        /// Fills the gaps in <paramref name="shear"/> with the slope of <paramref name="moment"/>
        /// along the beam: a quadratic in the stations' abscissae <paramref name="xi"/>, fitted by
        /// least squares, differentiated and divided by <paramref name="length"/>. A quadratic holds
        /// the moments of a beam under uniform load exactly; two stations give a straight line.
        /// </summary>
        private static void Slopes(IReadOnlyList<double> xi, List<double> moment, List<double> shear, double length)
        {
            int degree = Math.Min(2, xi.Count - 1);
            if (degree < 1)
                return;

            // The normal equations of the fit, (degree + 1) square.
            int size = degree + 1;
            var a = new double[size, size + 1];
            for (int k = 0; k < xi.Count; k++)
            {
                for (int r = 0; r < size; r++)
                {
                    for (int c = 0; c < size; c++)
                        a[r, c] += Math.Pow(xi[k], r + c);
                    a[r, size] += Math.Pow(xi[k], r) * moment[k];
                }
            }

            // Gaussian elimination with partial pivoting.
            for (int p = 0; p < size; p++)
            {
                int best = p;
                for (int r = p + 1; r < size; r++)
                    if (Math.Abs(a[r, p]) > Math.Abs(a[best, p])) best = r;
                for (int c = 0; c <= size; c++)
                    (a[p, c], a[best, c]) = (a[best, c], a[p, c]);
                for (int r = 0; r < size; r++)
                {
                    if (r == p || a[p, p] == 0.0) continue;
                    double factor = a[r, p] / a[p, p];
                    for (int c = p; c <= size; c++)
                        a[r, c] -= factor * a[p, c];
                }
            }
            var coefficient = Enumerable.Range(0, size).Select(r => a[r, r] == 0.0 ? 0.0 : a[r, size] / a[r, r]).ToArray();

            for (int k = 0; k < xi.Count; k++)
            {
                if (!double.IsNaN(shear[k]))
                    continue;
                double slope = coefficient[1] + (degree == 2 ? 2.0 * coefficient[2] * xi[k] : 0.0);
                shear[k] = slope / length;
            }
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
                    "on in the MPCO Recorder component and run the analysis again.");

            // One group per element class, named <classTag>-<className>[<rule>:<index>:<header>].
            // Enumerated rather than named: the index is handed out in order of discovery, so it is
            // not a stable label, and a model can hold quads and triangles at once. Shells only: a
            // beam on a fibre section answers section.fiber.stress too, with its own layout.
            foreach (var group in h5File.Group(BASE).Children().OfType<PureHDF.IH5Group>()
                                        .Where(group => group.Name.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0))
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
            SolidStress(Model alpacaModel, int step, string className, IReadOnlyList<Generic.IBrick> elements, bool local, int points = 1)
        {
            const int COMPONENTS = 6;

            var sigma = new List<double>[COMPONENTS];
            for (int c = 0; c < COMPONENTS; c++)
                sigma[c] = new List<double>();

            // One value per element: the mean over its integration points, which for a single
            // point is that point, and for the four of a ten node tetrahedron is the stress at its
            // centre - the points sit symmetrically about it and the stress there is linear.
            foreach (var atPoints in SolidStressAtPoints(alpacaModel, step, className, elements, local, points))
                for (int c = 0; c < COMPONENTS; c++)
                    sigma[c].Add(atPoints.Average(point => point[c]));

            return (sigma[0], sigma[1], sigma[2], sigma[3], sigma[4], sigma[5]);
        }

        /// <summary>
        /// What <see cref="SolidStress"/> reads, before it is averaged: for each element, the six
        /// components at each of its <paramref name="points"/> integration points.
        ///
        /// A row of the file holds one block per point, one after the other, and each block is
        /// whatever the material answers to "stresses" - six numbers for every material Alpaca4d
        /// writes, but more for some, so only the first six of each block are taken.
        /// </summary>
        private static List<double[][]> SolidStressAtPoints(Model alpacaModel, int step, string className,
            IReadOnlyList<Generic.IBrick> elements, bool local, int points)
        {
            const string BASE = ON_ELEMENTS + "/stresses";
            const int COMPONENTS = 6;

            var stresses = new List<double[][]>();

            if (elements.Count == 0)
                return stresses;

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            if (!h5File.LinkExists(BASE))
                throw new Exception(
                    "The recorder file holds no stresses. Switch \"stresses\" on in the MPCO Recorder component.");

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
                if (element.Id == null || !rowById.TryGetValue(element.Id.Value, out double[] row) || row.Length < COMPONENTS * points)
                    throw new Exception(
                        $"The recorder file holds no stresses for {className} {element.Id}. The file was " +
                        "written by a different model from the one being read - re-run the analysis.");

                // The solver works in the global axes and so does the file. Turning the tensor into
                // the element's own frame is done here rather than by the caller because the six
                // numbers only mean anything together, and a caller holding six separate lists has
                // already lost the tensor.
                var frame = local ? Utils.SolidFrame(element.Mesh.Vertices.ToPoint3dArray()) : default;
                int block = row.Length / points;

                var atPoints = new double[points][];
                for (int p = 0; p < points; p++)
                {
                    var values = row.Skip(p * block).Take(COMPONENTS).ToArray();
                    atPoints[p] = local ? Utils.StressInFrame(values, frame.X, frame.Y, frame.Z) : values;
                }

                stresses.Add(atPoints);
            }

            return stresses;
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
        /// The stress in every ten node tetrahedron, one value per element, in the order the model
        /// lists them: the mean of its four integration points, which is the stress at its centre.
        /// Same six components and same axes as <see cref="TetrahedronStress"/>. The recorder
        /// files them under TenNodeTetrahedron, the element's class, whichever of its two commands
        /// built it.
        /// </summary>
        public static (List<double>, List<double>, List<double>, List<double>, List<double>, List<double>) TenNodeTetrahedronStress(Model alpacaModel, int step, string resultType = null, bool local = false)
        {
            return SolidStress(alpacaModel, step, "TenNodeTetrahedron", TenNodeTetrahedra(alpacaModel),
                               local, Element.TenNodeTetrahedron.GaussPoints);
        }

        /// <summary>
        /// The stress in every ten node tetrahedron at each of its ten nodes, in the order the model
        /// lists the elements and each element its nodes: six components per node, the field of the
        /// element alone, since a stress jumps from one element to the next. See
        /// Element.TenNodeTetrahedron.AtNodes for how four integration points make ten nodes.
        /// </summary>
        public static List<double[][]> TenNodeTetrahedronStressAtNodes(Model alpacaModel, int step, bool local = false)
        {
            var atNodes = new List<double[][]>();

            foreach (var atPoints in SolidStressAtPoints(alpacaModel, step, "TenNodeTetrahedron", TenNodeTetrahedra(alpacaModel),
                                                         local, Element.TenNodeTetrahedron.GaussPoints))
            {
                var nodes = new double[10][];
                for (int n = 0; n < nodes.Length; n++)
                    nodes[n] = new double[6];

                for (int c = 0; c < 6; c++)
                {
                    var component = Element.TenNodeTetrahedron.AtNodes(atPoints.Select(point => point[c]).ToList());
                    for (int n = 0; n < nodes.Length; n++)
                        nodes[n][c] = component[n];
                }

                atNodes.Add(nodes);
            }

            return atNodes;
        }

        private static List<Generic.IBrick> TenNodeTetrahedra(Model alpacaModel)
        {
            return alpacaModel.Bricks
                .Where(x => x.ElementClass == Element.ElementClass.TenNodeTetrahedron)
                .ToList();
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
