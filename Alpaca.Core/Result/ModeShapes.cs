using System;
using System.Collections.Generic;
using System.Linq;
using PureHDF;

namespace Alpaca4d.Result
{
    public partial class Read
    {
        /// <summary>
        /// The translations of every mode of a modal run as OpenSees recorded them: mass-normalised,
        /// φᵀMφ = 1, in 1/√t with the model's mass in tonnes. One array per mode, lowest first; in
        /// each, a row per node of <see cref="Model.Nodes"/>, in that order, and X, Y, Z.
        ///
        /// <see cref="NodalOutput"/> hands the same modes out scaled to a largest translation of 1,
        /// which is what a picture wants and loses the mass. A response to a force needs it: a force
        /// F at node e drives mode m by φₑF, and node r answers with φᵣ times that, so
        /// μₑ μᵣ / M of a unit-scaled mode is φₑ φᵣ of the mass-normalised one. The modal mass of
        /// the unit-scaled mode is 1 / (largest translation)².
        ///
        /// A node with no row in the file - a node with no free degree of freedom is still
        /// written, so this is only for safety - reads as zero.
        /// </summary>
        public static List<double[,]> MassNormalisedModes(Model alpacaModel)
        {
            if (alpacaModel == null || !alpacaModel.IsModal)
                throw new ArgumentException("The model has not been through Natural Vibration Analysis, so it holds no modes.");
            if (alpacaModel.Recorders == null || !alpacaModel.Recorders.Any())
                throw new ArgumentException("The model has no recorder file to read its modes from.");

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);
            string resultType = Alpaca4d.Helper.EnumHelper.ResultTypeConvert(ResultType.MODES_OF_VIBRATION_U);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);
            if (!h5File.LinkExists($"{ON_NODES}/{resultType}"))
                throw new Exception("The recorder file holds no mode shapes.");

            // The tag of each row, so a row lands on its node even if OpenSees ever writes them in
            // another order. Without the ID table, rows are taken in the order of Nodes, as
            // NodalOutput takes them.
            int[] rowTags = null;
            string idPath = $"{ON_NODES}/{resultType}/ID";
            if (h5File.LinkExists(idPath))
                rowTags = h5File.Dataset(idPath).Read<int>().ToArray();

            var rowOfTag = new Dictionary<int, int>();
            if (rowTags != null)
                for (int row = 0; row < rowTags.Length; row++)
                    rowOfTag[rowTags[row]] = row;

            // MODE_0 is the first mode, as Nodal Displacements' Step 0 is.
            var modes = new List<double[,]>();
            for (int mode = 0; mode < alpacaModel.NumberOfModes; mode++)
            {
                string path = $"{ON_NODES}/{resultType}/DATA/STEP_0/MODE_{mode}";
                if (!h5File.LinkExists(path))
                    break;

                double[,] data = ModeData(h5File, resultType, mode);
                int columns = Math.Min(3, data.GetLength(1));
                var shape = new double[alpacaModel.Nodes.Count, 3];
                for (int i = 0; i < alpacaModel.Nodes.Count; i++)
                {
                    int row = i;
                    if (rowTags != null)
                    {
                        int tag = alpacaModel.Nodes[i].Id ?? i + 1;
                        if (!rowOfTag.TryGetValue(tag, out row))
                            continue;
                    }
                    if (row >= data.GetLength(0))
                        continue;
                    for (int j = 0; j < columns; j++)
                        shape[i, j] = data[row, j];
                }
                modes.Add(shape);
            }

            return modes;
        }
    }
}
