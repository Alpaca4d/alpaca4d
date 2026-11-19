using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// A mesh gmsh wrote, read from its MSH 4.1 text format: the nodes, the elements of each
    /// geometric entity, and which surfaces bound each volume.
    ///
    /// Only what the meshers use is kept. Elements of other types than the ones listed in
    /// <see cref="NodesPerElement"/> are skipped, whatever their dimension.
    /// </summary>
    public sealed class MshFile
    {
        /// <summary>The nodes by gmsh's tag.</summary>
        public Dictionary<long, Point3d> Nodes { get; } = new Dictionary<long, Point3d>();

        /// <summary>The elements, in the order gmsh wrote them.</summary>
        public List<MshElement> Elements { get; } = new List<MshElement>();

        /// <summary>The surfaces each volume is bounded by, by the volume's tag. Unsigned.</summary>
        public Dictionary<int, int[]> VolumeSurfaces { get; } = new Dictionary<int, int[]>();

        /// <summary>The curves each surface is bounded by, by the surface's tag. Unsigned.</summary>
        public Dictionary<int, int[]> SurfaceCurves { get; } = new Dictionary<int, int[]>();

        /// <summary>gmsh's element types the meshers read, and how many nodes each has.</summary>
        public static readonly IReadOnlyDictionary<int, int> NodesPerElement = new Dictionary<int, int>
        {
            { 1, 2 },   // two node line
            { 2, 3 },   // three node triangle
            { 3, 4 },   // four node quadrangle
            { 4, 4 },   // four node tetrahedron
            { 15, 1 },  // one node point
        };

        public const int Line = 1, Triangle = 2, Quadrangle = 3, Tetrahedron = 4, Point = 15;

        public static MshFile Read(string path)
        {
            using (var reader = new StreamReader(path))
                return Read(reader);
        }

        public static MshFile Read(TextReader reader)
        {
            var file = new MshFile();
            var lines = new Lines(reader);
            string line;
            while ((line = lines.Next()) != null)
            {
                switch (line.Trim())
                {
                    case "$MeshFormat":
                        var version = lines.Words();
                        if (version.Length < 2 || !version[0].StartsWith("4", StringComparison.Ordinal))
                            throw new FormatException($"Alpaca4d reads gmsh's MSH 4 format, and this file is version {version[0]}.");
                        if (version[1] != "0")
                            throw new FormatException("Alpaca4d reads gmsh's MSH files as text, and this one is binary.");
                        break;
                    case "$Entities":
                        file.ReadEntities(lines);
                        break;
                    case "$Nodes":
                        file.ReadNodes(lines);
                        break;
                    case "$Elements":
                        file.ReadElements(lines);
                        break;
                }
            }
            return file;
        }

        private void ReadEntities(Lines lines)
        {
            var counts = lines.Longs();
            long points = counts[0], curves = counts[1], surfaces = counts[2], volumes = counts[3];

            for (long i = 0; i < points; i++)
                lines.Next();

            // curveTag minX minY minZ maxX maxY maxZ numPhysicalTags physicalTag... numBoundingPoints pointTag...
            for (long i = 0; i < curves; i++)
                lines.Next();

            for (long i = 0; i < surfaces; i++)
            {
                var w = lines.Longs();
                SurfaceCurves[(int)w[0]] = Bounding(w);
            }

            for (long i = 0; i < volumes; i++)
            {
                var w = lines.Longs();
                VolumeSurfaces[(int)w[0]] = Bounding(w);
            }
        }

        /// <summary>The bounding entities at the end of a curve, surface or volume line.</summary>
        private static int[] Bounding(long[] w)
        {
            // tag, then six numbers of the bounding box, then the physical tags, then the bounding entities.
            int at = 7;
            long physical = w[at];
            at += 1 + (int)physical;
            int count = (int)w[at];
            var tags = new int[count];
            for (int i = 0; i < count; i++)
                tags[i] = (int)Math.Abs(w[at + 1 + i]);
            return tags;
        }

        private void ReadNodes(Lines lines)
        {
            var header = lines.Longs();
            long blocks = header[0];
            for (long b = 0; b < blocks; b++)
            {
                // entityDim entityTag parametric numNodes; a parametric node has its u, v after x y z.
                var block = lines.Longs();
                long count = block[3];
                var tags = new long[count];
                for (long i = 0; i < count; i++)
                    tags[i] = long.Parse(lines.Next().Trim(), CultureInfo.InvariantCulture);
                for (long i = 0; i < count; i++)
                {
                    var xyz = lines.Doubles();
                    Nodes[tags[i]] = new Point3d(xyz[0], xyz[1], xyz[2]);
                }
            }
        }

        private void ReadElements(Lines lines)
        {
            var header = lines.Longs();
            long blocks = header[0];
            for (long b = 0; b < blocks; b++)
            {
                var block = lines.Longs();
                int dimension = (int)block[0];
                int entity = (int)block[1];
                int type = (int)block[2];
                long count = block[3];
                bool known = NodesPerElement.TryGetValue(type, out int nodes);
                for (long i = 0; i < count; i++)
                {
                    var line = lines.Next();
                    if (!known)
                        continue;
                    var w = Split(line);
                    var element = new long[nodes];
                    for (int n = 0; n < nodes; n++)
                        element[n] = long.Parse(w[1 + n], CultureInfo.InvariantCulture);
                    Elements.Add(new MshElement(dimension, entity, type, element));
                }
            }
        }

        private static string[] Split(string line) =>
            line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The lines of the file, read one at a time.</summary>
        private sealed class Lines
        {
            private readonly TextReader reader;
            public Lines(TextReader reader) { this.reader = reader; }

            public string Next() => reader.ReadLine();

            public string[] Words()
            {
                var line = Next() ?? throw new FormatException("The gmsh mesh file ends in the middle of a section.");
                return Split(line);
            }

            public long[] Longs()
            {
                var words = Words();
                var values = new long[words.Length];
                for (int i = 0; i < words.Length; i++)
                    values[i] = (long)double.Parse(words[i], CultureInfo.InvariantCulture);
                return values;
            }

            public double[] Doubles()
            {
                var words = Words();
                var values = new double[words.Length];
                for (int i = 0; i < words.Length; i++)
                    values[i] = double.Parse(words[i], CultureInfo.InvariantCulture);
                return values;
            }
        }
    }

    /// <summary>One element of a gmsh mesh: its dimension, the entity it is on, its type and its nodes' tags.</summary>
    public sealed class MshElement
    {
        public int Dimension { get; }
        public int Entity { get; }
        public int Type { get; }
        public long[] Nodes { get; }

        public MshElement(int dimension, int entity, int type, long[] nodes)
        {
            Dimension = dimension;
            Entity = entity;
            Type = type;
            Nodes = nodes;
        }
    }
}
