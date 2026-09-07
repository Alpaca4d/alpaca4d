using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Text.RegularExpressions;

using System.Drawing;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel.Data;


namespace Alpaca4d
{
    public class Utils
    {
        public static Plane AlignPlane(Plane plane, Vector3d vector)
        {
            double s;
            double t;
            plane.ClosestParameter(plane.Origin + vector, out s, out t);
            double num = Math.Atan2(s, t);
            plane.Rotate(-num + 1.5707963267948966, plane.ZAxis, plane.Origin);
            return plane;
        }
        /// <summary>
        /// The node nearest each search point, as an index into the cloud the tree was built from,
        /// counting from zero. Exactly one index per point, in the order the points were given.
        ///
        /// Nearest rather than first: a tolerance wide enough to catch two nodes used to hand back
        /// whichever the tree happened to reach first, which is not a property of the model. A
        /// point that catches nothing is an error rather than a gap in the list, because a caller
        /// reading the result positionally - which every caller does - cannot see a gap.
        /// </summary>
        /// <param name="owner">
        /// What is doing the searching, named in the message when a point finds no node. The
        /// alternative is "No node found within tolerance" with nothing saying where to look.
        /// </param>
        /// <param name="cloud">
        /// The points the tree was built from. Only needed to break a tie: without it the tree
        /// hands back whichever of several hits it reached first.
        /// </param>
        public static List<int> RTreeSearch(RTree tree, IList<Point3d> searchPoints, double tol,
                                            string owner = null, IList<Point3d> cloud = null)
        {
            var closestIndexes = new List<int>(searchPoints.Count);

            foreach (var pt in searchPoints)
            {
                int foundIndex = -1;
                double foundDistance = double.MaxValue;
                var point = pt;

                tree.Search(new Sphere(point, tol), (sender, e) =>
                {
                    // The tree hands back everything inside the sphere in no particular order, so
                    // when the cloud is to hand every hit is measured and the nearest kept.
                    if (cloud == null)
                    {
                        foundIndex = e.Id;
                        e.Cancel = true;
                        return;
                    }

                    double distance = point.DistanceTo(cloud[e.Id]);
                    if (foundIndex == -1 || distance < foundDistance)
                    {
                        foundIndex = e.Id;
                        foundDistance = distance;
                    }
                });

                if (foundIndex == -1)
                    throw new Exception(
                        $"{owner ?? "Something"} reaches {pt}, where the model has no node within the " +
                        $"tolerance of {tol}. Nodes are made by the elements that arrive at them, so a " +
                        "point no element reaches has nothing to attach to.");

                closestIndexes.Add(foundIndex);
            }

            return closestIndexes;
        }
        public static DataTree<object> DataTreeFromNestedList(List<List<double>> nestedList)
        {
            GH_Path path;
            var tree = new DataTree<object>();

            for (int i = 0; i < nestedList.Count; i++)
            {
                var array = (List<double>)nestedList[i];
                for (int j = 0; j < array.Count; j++)
                {
                    path = new GH_Path(i);
                    tree.Add(array[j], new GH_Path(path));
                }
            }

            return tree;
        }
        public static DataTree<object> DataTreeFromNestedList(List<List<double>> nestedList, List<int?> indexes)
        {
            GH_Path path;
            var tree = new DataTree<object>();

            for (int i = 0; i < nestedList.Count; i++)
            {
                var array = (List<double>)nestedList[i];
                for (int j = 0; j < array.Count; j++)
                {
                    path = new GH_Path((int)indexes[i]);
                    tree.Add(array[j], new GH_Path(path));
                }
            }

            return tree;
        }
        /// <summary>
        /// Copies every branch of one step's tree into <paramref name="history"/> under a path
        /// with the step number in front, so a history reads {step; element} where a single
        /// step reads {element}. The element index keeps its place, which is what lets a
        /// history branch be matched back to the element it came from.
        /// </summary>
        public static void AddStepToHistory(DataTree<object> history, DataTree<object> stepTree, int step)
        {
            for (int i = 0; i < stepTree.BranchCount; i++)
            {
                var indices = new List<int> { step };
                indices.AddRange(stepTree.Paths[i].Indices);
                history.AddRange(stepTree.Branches[i], new GH_Path(indices.ToArray()));
            }
        }

        public static DataTree<object> DataTreeFromNestedList(List<List<string>> nestedList)
        {
            GH_Path path;
            var tree = new DataTree<object>();

            for (int i = 0; i < nestedList.Count; i++)
            {
                var array = (List<string>)nestedList[i];
                for (int j = 0; j < array.Count; j++)
                {
                    path = new GH_Path(i);
                    tree.Add(array[j], new GH_Path(path));
                }
            }

            return tree;
        }
        public static List<Mesh> ExplodeMesh(Mesh mesh)
        {
            List<Mesh> list = new List<Mesh>(mesh.Faces.Count);
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                Mesh mesh2 = new Mesh();
                List<Color> list2 = new List<Color>();
                int a = mesh.Faces.GetFace(i).A;
                int b = mesh.Faces.GetFace(i).B;
                int c = mesh.Faces.GetFace(i).C;
                mesh2.Vertices.Add(mesh.Vertices.ElementAt(a));
                mesh2.Vertices.Add(mesh.Vertices.ElementAt(b));
                mesh2.Vertices.Add(mesh.Vertices.ElementAt(c));
                if (mesh.VertexColors.Count != 0)
                {
                    list2.Add(mesh.VertexColors.ElementAt(a));
                    list2.Add(mesh.VertexColors.ElementAt(b));
                    list2.Add(mesh.VertexColors.ElementAt(c));
                }
                if (mesh.Faces.GetFace(i).IsTriangle)
                {
                    mesh2.Faces.AddFace(0, 1, 2);
                }
                else
                {
                    int d = mesh.Faces.GetFace(i).D;
                    mesh2.Vertices.Add(mesh.Vertices.ElementAt(d));
                    if (mesh.VertexColors.Count != 0)
                    {
                        list2.Add(mesh.VertexColors.ElementAt(d));
                    }
                    mesh2.Faces.AddFace(0, 1, 2, 3);
                }
                if (mesh.VertexColors.Count != 0)
                {
                    mesh2.VertexColors.AppendColors(list2.ToArray());
                }
                list.Add(mesh2);
            }
            return list;
        }
        public static Plane PerpendicularFrame(Curve Curve)
        {
            double min = Curve.Domain.Min;
            double max = Curve.Domain.Max;
            double value = 0.5 * (min + max);

            double[] array = new double[2];
            if (value <= min + 1E-12)
            {
                array[0] = value;
                array[1] = 0.5 * (min + max);
            }
            else
            {
                array[0] = min;
                array[1] = value;
            }
            Plane[] perpendicularFrames = Curve.GetPerpendicularFrames(array);

            if (value <= min + 1E-12)
            {
                return perpendicularFrames[0];
            }
            return perpendicularFrames[1];
        }
        public static Mesh CreateLoft(IList<Polyline> polylines)
        {
            if (Enumerable.All(polylines, p => p.IsClosed))
                return CreateLoftClosed(polylines);
            else
                return CreateLoftOpen(polylines);
        }
        private static Mesh CreateLoftOpen(IList<Polyline> polylines)
        {
            Mesh result = new Mesh();
            var verts = result.Vertices;
            var faces = result.Faces;

            int ny = polylines.Count;
            int nx = Enumerable.Min(polylines, p => p.Count);
            int n;

            // add vertices
            for (int i = 0; i < ny; i++)
            {
                var poly = polylines[i];

                for (int j = 0; j < nx; j++)
                    verts.Add(poly[j]);
            }

            // add faces
            for (int i = 0; i < ny - 1; i++)
            {
                n = i * nx;

                for (int j = 0; j < nx - 1; j++)
                    faces.AddFace(n + j, n + j + 1, n + j + nx + 1, n + j + nx);
            }

            return result;
        }
        private static Mesh CreateLoftClosed(IList<Polyline> polylines)
        {
            Mesh result = new Mesh();
            var verts = result.Vertices;
            var faces = result.Faces;

            int ny = polylines.Count;
            int nx = Enumerable.Min(polylines, p => p.Count) - 1;
            int n;

            // add vertices
            for (int i = 0; i < ny; i++)
            {
                var poly = polylines[i];

                for (int j = 0; j < nx; j++)
                    verts.Add(poly[j]);
            }

            // add faces
            for (int i = 0; i < ny - 1; i++)
            {
                n = i * nx;

                for (int j0 = 0; j0 < nx; j0++)
                {
                    int j1 = (j0 + 1) % nx;
                    faces.AddFace(n + j0, n + j1, n + j1 + nx, n + j0 + nx);
                }
            }

            return result;
        }



        public static Mesh CreateLoft(IList<Polyline> polylines, List<double> deformation = null, List<Color> iColors = null, double? min = null, double? max = null)
        {
            if (Enumerable.All(polylines, p => p.IsClosed))
                return CreateLoftClosed(polylines, deformation, iColors, min, max);
            else
                return CreateLoftOpen(polylines, deformation, iColors, min, max);
        }
        private static Mesh CreateLoftClosed(IList<Polyline> polylines, List<double> deformation, List<Color> iColors, double? min, double? max)
        {
            // find the total range of displacement

            var d = new SortedDictionary<double, System.Drawing.Color>();

            var numberOfColors = iColors.Count;
            var diff = (max - min) / (numberOfColors - 1);

            var start = min;
            foreach (var color in iColors)
            {
                if (!d.ContainsKey((double)start))
                {
                    d.Add((double)start, color);
                    start += diff;
                }
            }


            Mesh result = new Mesh();
            var verts = result.Vertices;
            var faces = result.Faces;
            var clrs = result.VertexColors;

            int ny = polylines.Count;
            int nx = Enumerable.Min(polylines, p => p.Count) - 1;
            int n;

            // add vertices
            for (int i = 0; i < ny; i++)
            {
                var poly = polylines[i];

                for (int j = 0; j < nx; j++)
                {
                    verts.Add(poly[j]);
                    var clr = Alpaca4d.Colors.GetColor(deformation[i], d);
                    clrs.Add(clr);
                }
            }

            // add faces
            for (int i = 0; i < ny - 1; i++)
            {
                n = i * nx;

                for (int j0 = 0; j0 < nx; j0++)
                {
                    int j1 = (j0 + 1) % nx;
                    faces.AddFace(n + j0, n + j1, n + j1 + nx, n + j0 + nx);
                }
            }

            return result;
        }
        private static Mesh CreateLoftOpen(IList<Polyline> polylines, List<double> deformation, List<Color> iColors, double? min, double? max)
        {

            var d = new SortedDictionary<double, System.Drawing.Color>();

            var numberOfColors = iColors.Count;
            var range = max - max;
            var diff = (max - min) / (numberOfColors - 1);

            var start = min;
            foreach (var color in iColors)
            {
                d.Add((double)start, color);
                start += diff;
            }

            Mesh result = new Mesh();
            var verts = result.Vertices;
            var faces = result.Faces;
            var clrs = result.VertexColors;

            int ny = polylines.Count;
            int nx = Enumerable.Min(polylines, p => p.Count);
            int n;

            // add vertices
            for (int i = 0; i < ny; i++)
            {
                var poly = polylines[i];

                for (int j = 0; j < nx; j++)
                {
                    verts.Add(poly[j]);
                    var clr = Alpaca4d.Colors.GetColor(deformation[i], d);
                    clrs.Add(clr);
                }
            }

            // add faces
            for (int i = 0; i < ny - 1; i++)
            {
                n = i * nx;

                for (int j = 0; j < nx - 1; j++)
                    faces.AddFace(n + j, n + j + 1, n + j + nx + 1, n + j + nx);
            }

            return result;
        }

        /// <summary>
        /// A mesh split into one mesh per face. Works on a copy: unwelding rewrites the mesh it is
        /// called on, and the caller's mesh is the one the user drew.
        /// </summary>
        private static List<Mesh> MeshToShell(Mesh mesh)
        {
            var working = mesh.DuplicateMesh();
            working.Unweld(0, true);

            return working.ExplodeAtUnweldedEdges().ToList();
        }
        /// <summary>
        /// A run of meshes swept into a layer of bricks between each neighbouring pair.
        ///
        /// Each brick takes its near face from one mesh and its far face from the next, pairing
        /// them corner by corner in the order the two faces list their vertices. That pairing is
        /// the whole method, and it is only right if every mesh in the series carries its faces and
        /// its vertices in the same order - so the bricks are checked here rather than left to come
        /// out twisted, because a twisted brick is one OpenSees solves rather than rejects.
        /// </summary>
        public static List<Mesh> MeshSeriesToBrick(List<Mesh> MeshList)
        {
            if (MeshList == null || MeshList.Count < 2)
                throw new Exception("A series needs at least two meshes to sweep a brick between.");

            var solid = new List<Mesh>();

            // Every mesh split into its own faces, so that a mesh of many faces sweeps into many
            // bricks rather than one. A single face mesh explodes to a list of one and takes the
            // same path.
            var meshExpl = MeshList.Select(MeshToShell).ToList();

            int faceCount = meshExpl[0].Count;
            for (int i = 1; i < meshExpl.Count; i++)
            {
                if (meshExpl[i].Count != faceCount)
                    throw new Exception(
                        $"Mesh {i + 1} of the series has {meshExpl[i].Count} faces where the first has " +
                        $"{faceCount}. Each pair of neighbours becomes a layer of bricks, so every mesh in " +
                        "the series has to carry the same faces in the same order.");
            }

            for (int index1 = 0; index1 < meshExpl.Count - 1; index1++)
            {
                for (int index2 = 0; index2 < faceCount; index2++)
                {
                    var near = meshExpl[index1][index2];
                    var far = meshExpl[index1 + 1][index2];

                    // A triangle would sweep into a wedge, which is not an element Alpaca4d has;
                    // saying so beats dropping it and returning fewer bricks than faces.
                    if (near.Vertices.Count != 4 || far.Vertices.Count != 4)
                        throw new Exception(
                            $"Face {index2 + 1} of mesh {index1 + 1} or {index1 + 2} in the series has " +
                            $"{Math.Min(near.Vertices.Count, far.Vertices.Count)} corners. A brick sweeps " +
                            "between quadrilaterals, so triangulated meshes have to be quadrangulated first.");

                    var nodes = near.Vertices.ToPoint3dArray()
                                    .Concat(far.Vertices.ToPoint3dArray())
                                    .ToList();

                    // Both faces reversed together flips the sign without disturbing the pairing,
                    // exactly as CleanHexahedron does it.
                    if (HexahedronJacobian(nodes, 0.0, 0.0, 0.0) < 0.0)
                    {
                        nodes = Enumerable.Range(0, 4).Reverse().Select(i => nodes[i])
                                .Concat(Enumerable.Range(4, 4).Reverse().Select(i => nodes[i]))
                                .ToList();
                    }

                    CheckHexahedron(nodes);

                    solid.Add(HexahedronMesh(nodes));
                }
            }

            return solid;
        }
        /// <summary>
        /// Reads a number out of a Tcl token. A .tcl file is written with a dot for the decimal
        /// separator whatever the machine's locale is, so it has to be read that way too.
        /// </summary>
        private static double ParseNumber(string token)
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                return value;

            return double.Parse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture);
        }

        private static int ParseTag(string token)
        {
            if (int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int tag))
                return tag;

            return (int)Math.Round(ParseNumber(token));
        }

        public static Vector3d PlaceCoordinates(Point3d point, Plane localPlane)
        {
            localPlane.ClosestParameter(point, out double s, out double t);
            double w = localPlane.DistanceTo(point);
            return new Vector3d(-1.0 * w, t, s);
        }
        [Obsolete("Use Alpaca4d.TclReader, which reads a .tcl file into a full Model rather than into loose geometry.")]
        public static (List<Point3d> points, List<Point3d> supports, List<Curve> beamCurves, List<Mesh> shellMeshes, List<Mesh> brickMeshes) TextToGeometry(string filepath)
        {
            var lines = System.IO.File.ReadAllLines(filepath);
            (var points, var supports, var lineGeometry, var meshShell, var meshBrick) = Utils.TextToGeometry(lines.ToList());

            return (points, supports, lineGeometry, meshShell, meshBrick);
        }

        [Obsolete("Use Alpaca4d.TclReader, which reads a .tcl file into a full Model rather than into loose geometry.")]
        public static (List<Point3d> points, List<Point3d> supports, List<Curve> beamCurves, List<Mesh> shellMeshes, List<Mesh> brickMeshes) TextToGeometry(List<string> lines)
        {
            Model model = new Model();

            // One entry of the incoming list can hold several commands: every WriteTcl() ends in "\n"
            // and some emit more than one line, so the newlines separate commands and must not be
            // treated as plain whitespace.
            var splittedLines = lines
                .Where(x => x != null)
                .SelectMany(x => x.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                .Select(x => x.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(x => x.Length != 0 && !x[0].StartsWith("#"))
                .ToList();


            var monoDimensionalObject = new List<string> { "truss", "corotTruss", "elasticBeamColumn", "ElasticTimoshenkoBeam", "dispBeamColumn", "forceBeamColumn", "twoNodeLink" };
            var biDimensionalObject = new List<string> { "ShellMITC4", "ASDShellQ4", "ShellDKGQ", "ShellNLDKGQ", "ShellDKGT", "ShellNLDKGT", "ASDShellT3" };
            var quadShellObject = new List<string> { "ShellMITC4", "ASDShellQ4", "ShellDKGQ", "ShellNLDKGQ" };
            var triDimensionalObject = new List<string> { "SSPbrick", "stdBrick", "FourNodeTetrahedron" };

            var nodeDictionary = new Dictionary<int, Alpaca4d.Element.Node>();
            var supportDictionary = new Dictionary<int, Alpaca4d.Element.Support>();
            var stickDictionary = new Dictionary<int, Curve>();
            var shellGeometryDictionary = new Dictionary<int, Mesh>();
            var brickGeometryDictionary = new Dictionary<int, Mesh>();

            foreach (var line in splittedLines)
            {
                // create only Node, Material, Section and Geometric Transformation
                if (line[0] == "node")
                {
                    int index = ParseTag(line[1]);
                    double x = ParseNumber(line[2]);
                    double y = ParseNumber(line[3]);
                    double z = ParseNumber(line[4]);

                    var node = new Alpaca4d.Element.Node(index, x, y, z);
                    nodeDictionary[index] = node;
                }
            }

            foreach (var line in splittedLines)
            {
                if (line[0] == "fix")
                {
                    var index = ParseTag(line[1]);

                    if (line.Length == 8)
                    {
                        var x = Convert.ToBoolean(ParseTag(line[2]));
                        var y = Convert.ToBoolean(ParseTag(line[3]));
                        var z = Convert.ToBoolean(ParseTag(line[4]));
                        var xx = Convert.ToBoolean(ParseTag(line[5]));
                        var yy = Convert.ToBoolean(ParseTag(line[6]));
                        var zz = Convert.ToBoolean(ParseTag(line[7]));
                        var supportNode = nodeDictionary[index];
                        var support = new Alpaca4d.Element.Support(supportNode.Pos, x, y, z, xx, yy, zz);
                        support.ndf = 6;

                        supportDictionary[index] = support;
                    }
                    else if (line.Length == 5)
                    {
                        var x = Convert.ToBoolean(ParseTag(line[2]));
                        var y = Convert.ToBoolean(ParseTag(line[3]));
                        var z = Convert.ToBoolean(ParseTag(line[4]));
                        var supportNode = nodeDictionary[index];
                        var support = new Alpaca4d.Element.Support(supportNode.Pos, x, y, z, false, false, false);
                        support.ndf = 3; // it is require because WriteTcl read the ndf to correctly serialise

                        supportDictionary[index] = support;
                    }
                    else
                    {
                        throw new Exception("Support element is not ndf 3 or 6!");
                    }
                }
                else if (line[0] == "element")
                {
                    if (monoDimensionalObject.Contains(line[1]))
                    {
                        var index = ParseTag(line[2]);
                        var startIndex = ParseTag(line[3]);
                        var endIndex = ParseTag(line[4]);

                        var startNode = nodeDictionary[startIndex];
                        var endNode = nodeDictionary[endIndex];
                        var stickElement = new Rhino.Geometry.LineCurve(startNode.Pos, endNode.Pos);
                        stickDictionary[index] = stickElement;
                    }
                    if (biDimensionalObject.Contains(line[1]))
                    {
                        // The quad shells take four corner nodes, the DKGT/T3 family three.
                        bool isQuad = quadShellObject.Contains(line[1]);
                        var index = ParseTag(line[2]);
                        var nodeId = new List<int>();
                        for (int i = 0; i < (isQuad ? 4 : 3); i++)
                            nodeId.Add(ParseTag(line[3 + i]));

                        var flatMesh = new Rhino.Geometry.Mesh();
                        foreach (var id in nodeId)
                            flatMesh.Vertices.Add(nodeDictionary[id].Pos);

                        if (isQuad)
                            flatMesh.Faces.AddFace(0, 1, 2, 3);
                        else
                            flatMesh.Faces.AddFace(0, 1, 2);

                        shellGeometryDictionary[index] = flatMesh;
                    }
                    if (triDimensionalObject.Contains(line[1]))
                    {
                        if (line[1] == "FourNodeTetrahedron")
                        {
                            var index = ParseTag(line[2]);
                            var nodeId_0 = ParseTag(line[3]);
                            var nodeId_1 = ParseTag(line[4]);
                            var nodeId_2 = ParseTag(line[5]);
                            var nodeId_3 = ParseTag(line[6]);
                            var nodeId = new List<int> { nodeId_0, nodeId_1, nodeId_2, nodeId_3 };
                            var solidMesh = new Rhino.Geometry.Mesh();
                            foreach (var id in nodeId)
                                solidMesh.Vertices.Add(nodeDictionary[id].Pos);
                            solidMesh.Faces.AddFace(0, 1, 2);
                            solidMesh.Faces.AddFace(0, 1, 3);
                            solidMesh.Faces.AddFace(1, 2, 3);
                            solidMesh.Faces.AddFace(0, 2, 3);
                            brickGeometryDictionary[index] = solidMesh;
                        }
                        else if (line[1] == "SSPbrick" || line[1] == "stdBrick")
                        {
                            var index = ParseTag(line[2]);
                            var nodeId_0 = ParseTag(line[3]);
                            var nodeId_1 = ParseTag(line[4]);
                            var nodeId_2 = ParseTag(line[5]);
                            var nodeId_3 = ParseTag(line[6]);
                            var nodeId_4 = ParseTag(line[7]);
                            var nodeId_5 = ParseTag(line[8]);
                            var nodeId_6 = ParseTag(line[9]);
                            var nodeId_7 = ParseTag(line[10]);
                            var nodeId = new List<int> { nodeId_0, nodeId_1, nodeId_2, nodeId_3, nodeId_4, nodeId_5, nodeId_6, nodeId_7 };
                            var solidMesh = new Rhino.Geometry.Mesh();
                            foreach (var id in nodeId)
                                solidMesh.Vertices.Add(nodeDictionary[id].Pos);
                            solidMesh.Faces.AddFace(0, 1, 2, 3);
                            solidMesh.Faces.AddFace(4, 5, 6, 7);
                            solidMesh.Faces.AddFace(1, 2, 6, 5);
                            solidMesh.Faces.AddFace(4, 7, 3, 0);
                            solidMesh.Faces.AddFace(0, 1, 5, 4);
                            solidMesh.Faces.AddFace(2, 3, 7, 6);
                            brickGeometryDictionary[index] = solidMesh;
                        }
                    }
                }
            }

            var points = nodeDictionary.Values.Select(x => x.Pos).ToList();
            var supports = supportDictionary.Values.Select(x => x.Pos).ToList();
            var lineGeometry = stickDictionary.Values.ToList();
            var meshShell = shellGeometryDictionary.Values.ToList();
            var meshBrick = brickGeometryDictionary.Values.ToList();

            return (points, supports, lineGeometry, meshShell, meshBrick);
        }


        /// <summary>
        /// The eight corners of the trilinear brick in its own local frame, in the order OpenSees
        /// numbers the nodes: SSPbrick::GetStab lays its shape function derivatives out as nodes
        /// one to four at zeta = -1 and nodes five to eight directly above them.
        /// </summary>
        private static readonly double[][] HexahedronCorners =
        {
            new[] { -1.0, -1.0, -1.0 }, new[] {  1.0, -1.0, -1.0 },
            new[] {  1.0,  1.0, -1.0 }, new[] { -1.0,  1.0, -1.0 },
            new[] { -1.0, -1.0,  1.0 }, new[] {  1.0, -1.0,  1.0 },
            new[] {  1.0,  1.0,  1.0 }, new[] { -1.0,  1.0,  1.0 },
        };

        /// <summary>
        /// The determinant of the Jacobian of a trilinear hexahedron at one point of its local
        /// frame, its nodes given in OpenSees order.
        ///
        /// This is the quantity that decides whether a solid is usable at all. OpenSees forms the
        /// volume element as the Gauss weight times this determinant and never looks at its sign,
        /// so an element wound the wrong way round is not rejected: it solves, with a negative
        /// stiffness, and hands back displacements pointing the wrong way and stresses of the
        /// wrong sign. Nothing downstream can tell that from a real answer, which is why the
        /// question is settled here.
        /// </summary>
        private static double HexahedronJacobian(IList<Point3d> nodes, double xi, double eta, double zeta)
        {
            var j = new double[3, 3];

            for (int a = 0; a < 8; a++)
            {
                var corner = HexahedronCorners[a];

                // d(N)/d(xi, eta, zeta) for N = (1 + xi_a xi)(1 + eta_a eta)(1 + zeta_a zeta) / 8
                var dN = new[]
                {
                    0.125 * corner[0] * (1.0 + corner[1] * eta) * (1.0 + corner[2] * zeta),
                    0.125 * corner[1] * (1.0 + corner[0] * xi)  * (1.0 + corner[2] * zeta),
                    0.125 * corner[2] * (1.0 + corner[0] * xi)  * (1.0 + corner[1] * eta),
                };

                var x = new[] { nodes[a].X, nodes[a].Y, nodes[a].Z };

                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        j[row, col] += x[row] * dN[col];
            }

            return j[0, 0] * (j[1, 1] * j[2, 2] - j[1, 2] * j[2, 1])
                 - j[0, 1] * (j[1, 0] * j[2, 2] - j[1, 2] * j[2, 0])
                 + j[0, 2] * (j[1, 0] * j[2, 1] - j[1, 1] * j[2, 0]);
        }

        /// <summary>
        /// The determinant of the Jacobian of a four node tetrahedron, which is constant over the
        /// element. Six times the signed volume, and the same number FourNodeTetrahedron::shp3d
        /// computes as its Jdet.
        /// </summary>
        private static double TetrahedronJacobian(IList<Point3d> nodes)
        {
            return (nodes[1] - nodes[0]) * Vector3d.CrossProduct(nodes[2] - nodes[0], nodes[3] - nodes[0]);
        }

        /// <summary>
        /// Throws unless a brick is the right way out everywhere, not just at its centre.
        ///
        /// A brick whose far face is twisted against its near one - which is what a mesh series
        /// whose meshes carry their faces in different orders produces - can read positive at the
        /// centre and still fold through itself at a corner, so every corner is checked.
        /// </summary>
        private static void CheckHexahedron(IList<Point3d> nodes)
        {
            double centre = HexahedronJacobian(nodes, 0.0, 0.0, 0.0);
            double worst = centre;

            foreach (var corner in HexahedronCorners)
                worst = Math.Min(worst, HexahedronJacobian(nodes, corner[0], corner[1], corner[2]));

            // Measured against the element's own size rather than an absolute figure, so that the
            // same brick passes or fails the same way whatever the model is drawn in. A Jacobian
            // scales as a volume, so the yardstick has to as well.
            if (worst <= 0.0 || worst < 1.0e-8 * Math.Abs(centre))
                throw new Exception(
                    "This brick folds through itself: its Jacobian is not positive at every corner " +
                    $"({worst:G4} at the worst corner against {centre:G4} at the centre). OpenSees would " +
                    "solve it anyway, with a negative stiffness, and hand back displacements and stresses " +
                    "of the wrong sign. Check that the two faces are drawn the same way round - a brick " +
                    "built from a mesh series needs every mesh in that series to carry its faces and its " +
                    "vertices in the same order.");
        }

        /// <summary>
        /// The closed surface of a hexahedron whose nodes are in OpenSees order, wound so that
        /// every face normal points out of the solid.
        ///
        /// Written out face by face rather than left to Mesh.UnifyNormals, which only makes the
        /// faces agree with one another - which of the two agreeing answers it settles on is its
        /// own business. Everything that asks this mesh for a volume, and so everything that turns
        /// a density into a weight, needs the outward one.
        /// </summary>
        private static Mesh HexahedronMesh(IList<Point3d> nodes)
        {
            var mesh = new Mesh();

            foreach (var node in nodes)
                mesh.Vertices.Add(node);

            mesh.Faces.AddFace(0, 3, 2, 1); // zeta = -1
            mesh.Faces.AddFace(4, 5, 6, 7); // zeta = +1
            mesh.Faces.AddFace(0, 1, 5, 4); // eta  = -1
            mesh.Faces.AddFace(3, 7, 6, 2); // eta  = +1
            mesh.Faces.AddFace(1, 2, 6, 5); // xi   = +1
            mesh.Faces.AddFace(0, 4, 7, 3); // xi   = -1

            mesh.FaceNormals.ComputeFaceNormals();
            mesh.Normals.ComputeNormals();

            return mesh;
        }

        /// <summary>
        /// The closed surface of a tetrahedron whose nodes are in OpenSees order, wound outward.
        /// The counterpart of <see cref="HexahedronMesh"/>, and outward for the same reason.
        /// </summary>
        private static Mesh TetrahedronMesh(IList<Point3d> nodes)
        {
            var mesh = new Mesh();

            foreach (var node in nodes)
                mesh.Vertices.Add(node);

            mesh.Faces.AddFace(0, 2, 1);
            mesh.Faces.AddFace(0, 3, 2);
            mesh.Faces.AddFace(0, 1, 3);
            mesh.Faces.AddFace(1, 2, 3);

            mesh.FaceNormals.ComputeFaceNormals();
            mesh.Normals.ComputeNormals();

            return mesh;
        }

        /// <summary>
        /// The local frame of a solid, read off the order its nodes are numbered in.
        ///
        /// Both solids are numbered so that the first node is the origin of the isoparametric
        /// frame and the next few walk its axes, so the frame is already there in the numbering
        /// and only needs squaring up:
        ///
        ///   hexahedron   1 to 2 is local x, 1 to 4 is roughly local y, and z comes out along 1 to 5
        ///   tetrahedron  1 to 2 is local x, 1 to 3 is roughly local y, and z comes out along 1 to 4
        ///
        /// Roughly, because the second edge is only a guide: y is the part of it square to x, and z
        /// is x cross y. So x lies exactly along an edge of the element, and the frame stays
        /// right-handed however distorted the element is.
        ///
        /// z landing near the third edge rather than away from it is not a convention, it is the
        /// positive Jacobian: 1 to 2, 1 to 4 and 1 to 5 are the +xi, +eta and +zeta directions, and
        /// xi cross eta is +zeta exactly when the determinant is positive, which
        /// <see cref="CleanHexahedron"/> and <see cref="CleanTetrahedron"/> guarantee. The same
        /// argument gives 1 to 4 for the tetrahedron, whose Jacobian is the triple product of its
        /// three edges from node 1.
        ///
        /// This is a frame to report in, and nothing more. It does not reach the solver: OpenSees
        /// gives neither solid an orientation argument, so the analysis is run in the global axes
        /// whatever this says, and an orthotropic material still lines up with the world.
        /// </summary>
        public static (Vector3d X, Vector3d Y, Vector3d Z) SolidFrame(IList<Point3d> nodes)
        {
            if (nodes.Count != 4 && nodes.Count != 8)
                throw new Exception($"A solid has four nodes or eight, not {nodes.Count}.");

            // Node 4 of a hexahedron and node 3 of a tetrahedron: in both cases the far end of the
            // second edge leaving node 1 within the first face.
            var guide = nodes[nodes.Count == 8 ? 3 : 2] - nodes[0];

            var x = Unit(nodes[1] - nodes[0],
                         "This solid has its first two nodes at the same point, so it has no local 1 axis.");

            var z = Unit(Vector3d.CrossProduct(x, guide),
                         "This solid has its first three nodes on one line, so it has no local plane.");

            // Square to x and in the x-guide plane, which is what makes local 1 exactly an edge and
            // local 2 only roughly one.
            var y = Unit(Vector3d.CrossProduct(z, x), "This solid has no local 2 axis.");

            return (x, y, z);
        }

        /// <summary>
        /// A vector scaled to unit length, or the given complaint if it has no length to scale.
        ///
        /// The arithmetic rather than Vector3d.Unitize because that one is a call into Rhino's
        /// native library, and a frame worked out from eight points has no business needing one.
        /// </summary>
        private static Vector3d Unit(Vector3d v, string ifDegenerate)
        {
            double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

            if (length <= Rhino.RhinoMath.ZeroTolerance)
                throw new Exception(ifDegenerate);

            return new Vector3d(v.X / length, v.Y / length, v.Z / length);
        }

        /// <summary>
        /// <see cref="SolidFrame"/> as a plane, for drawing and for the Plane output of the result
        /// components. Its origin is the centre of the element rather than node 1, because that is
        /// where a set of element axes is worth looking at.
        ///
        /// The three axes are written onto the plane rather than handed to Plane(origin, x, y),
        /// which squares up whatever it is given inside Rhino's native library. The frame is
        /// already square, so there is nothing for it to do - but this way the plane provably
        /// carries the same three vectors the stress is rotated by, rather than three that ought
        /// to be the same.
        /// </summary>
        public static Plane SolidAxes(IList<Point3d> nodes)
        {
            var frame = SolidFrame(nodes);

            var centre = Point3d.Origin;
            foreach (var node in nodes)
                centre += node;
            centre /= nodes.Count;

            var plane = default(Plane);
            plane.Origin = centre;
            plane.XAxis = frame.X;
            plane.YAxis = frame.Y;
            plane.ZAxis = frame.Z;

            return plane;
        }

        /// <summary>
        /// The local frame of a shell: the axes its section forces and stresses are reported in.
        ///
        /// Not simply the first edge, the way a solid's is. Every shell element in OpenSees builds
        /// a reference frame from its node coordinates and then turns the *section* within that
        /// plane by an angle of its own, and it is the section that answers a recorder - a
        /// "section.force" request returns getStressResultant() without rotating it back. So the
        /// frame worth drawing is the section's, and that is what this returns.
        ///
        ///   ShellDKGT, ShellNLDKGT  the reference frame, and no section angle: x along node 1 to 2,
        ///                           y the part of 1 to 3 square to it, z their cross product
        ///   ASDShellT3              the same, because its default section direction is 1 to 2 -
        ///                           the very vector the reference frame already uses, so the angle
        ///                           between them is zero
        ///   ASDShellQ4              not the same. Its reference x is side 1-2 flattened onto the
        ///                           element plane, but its default section x is the line joining
        ///                           the midpoints of sides 2-3 and 4-1 - so on any quad that is not
        ///                           a parallelogram the two differ, and the section is turned
        ///
        /// <paramref name="localX"/> is the "-local" vector when one was given. OpenSees does not
        /// take it as the axis outright: it flattens it onto the element plane first, so a vector
        /// that only roughly points the right way still lands square with the shell.
        /// </summary>
        public static (Vector3d X, Vector3d Y, Vector3d Z) ShellFrame(IList<Point3d> nodes, Vector3d localX)
        {
            if (nodes.Count != 3 && nodes.Count != 4)
                throw new Exception($"A shell face has three corners or four, not {nodes.Count}.");

            Vector3d normal, referenceX, target;

            if (nodes.Count == 3)
            {
                // ASDShellT3LocalCoordinateSystem, and ShellNLDKGT::updateBasis, which agree.
                var side12 = nodes[1] - nodes[0];
                normal = Unit(Vector3d.CrossProduct(side12, nodes[2] - nodes[0]),
                              "This shell face has its three corners on one line, so it has no normal.");
                referenceX = Unit(side12, "This shell face has its first two corners at the same point.");
                target = referenceX;
            }
            else
            {
                // ASDShellQ4LocalCoordinateSystem: the normal from the two diagonals, and x from
                // side 1-2 with whatever part of it stands out of the plane taken off.
                normal = Unit(Vector3d.CrossProduct(nodes[2] - nodes[0], nodes[3] - nodes[1]),
                              "This shell face is degenerate, so it has no normal.");

                var side12 = nodes[1] - nodes[0];
                referenceX = Unit(side12 - (side12 * normal) * normal,
                                  "This shell face has side 1-2 along its own normal.");

                // ASDShellQ4::setDomain, the branch with no -local given.
                target = Unit((nodes[1] + (nodes[2] - Point3d.Origin)) / 2.0
                              - (nodes[0] + (nodes[3] - Point3d.Origin)) / 2.0,
                              "This shell face has both pairs of opposite sides meeting at a point.");
            }

            var referenceY = Unit(Vector3d.CrossProduct(normal, referenceX), "This shell face has no local 2 axis.");

            if (localX.IsValid && localX.SquareLength > 0.0)
            {
                // Flattened onto the element plane exactly the way OpenSees flattens it: square to
                // the normal first, then square to that.
                var acrossX = Unit(Vector3d.CrossProduct(normal, localX),
                                   "The local X given for this shell lies along its normal, so it cannot be an axis in it.");
                target = Unit(Vector3d.CrossProduct(acrossX, normal), "The local X given for this shell has no direction.");
            }

            // The section angle, formed the way ASDShellQ4::setDomain forms it: the angle from the
            // reference x to the target, signed by which side of the reference x the target is on.
            // Written as a cosine and a sine rather than an angle, because turning the reference x
            // about the normal by that angle is just cos, sin against the two reference axes - and
            // for a target already in the plane it hands back the target itself.
            double cos = target * referenceX;
            double sin = target * referenceY;

            double norm = Math.Sqrt(cos * cos + sin * sin);
            if (norm <= Rhino.RhinoMath.ZeroTolerance)
                throw new Exception("This shell's section direction stands square to its own plane.");

            cos /= norm;
            sin /= norm;

            var x = Unit(cos * referenceX + sin * referenceY, "This shell has no local 1 axis.");
            var y = Unit(Vector3d.CrossProduct(normal, x), "This shell has no local 2 axis.");

            return (x, y, normal);
        }

        /// <summary>
        /// <see cref="ShellFrame"/> as a plane at the centre of the face. Built the same way
        /// <see cref="SolidAxes"/> is, and for the same reason.
        /// </summary>
        public static Plane ShellAxes(IList<Point3d> nodes, Vector3d localX)
        {
            var frame = ShellFrame(nodes, localX);

            var centre = Point3d.Origin;
            foreach (var node in nodes)
                centre += node;
            centre /= nodes.Count;

            var plane = default(Plane);
            plane.Origin = centre;
            plane.XAxis = frame.X;
            plane.YAxis = frame.Y;
            plane.ZAxis = frame.Z;

            return plane;
        }

        /// <summary>
        /// The six components of a stress tensor read in a different frame.
        ///
        /// In and out both run sigma11, sigma22, sigma33, sigma12, sigma23, sigma13, which is the
        /// order OpenSees names them in and the order the solid readers hand them back. The
        /// arithmetic is the ordinary sigma' = R^T sigma R, written out rather than assembled into
        /// matrices because six numbers do not justify a matrix class.
        /// </summary>
        public static double[] StressInFrame(IList<double> global, Vector3d x, Vector3d y, Vector3d z)
        {
            var s = new[,]
            {
                { global[0], global[3], global[5] },
                { global[3], global[1], global[4] },
                { global[5], global[4], global[2] },
            };

            var axes = new[] { x, y, z };

            double Component(int a, int b)
            {
                var u = new[] { axes[a].X, axes[a].Y, axes[a].Z };
                var v = new[] { axes[b].X, axes[b].Y, axes[b].Z };

                double total = 0.0;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        total += u[i] * s[i, j] * v[j];

                return total;
            }

            return new[]
            {
                Component(0, 0), Component(1, 1), Component(2, 2),
                Component(0, 1), Component(1, 2), Component(0, 2),
            };
        }

        /// <summary>
        /// A closed solid mesh built straight from nodes already in OpenSees order - four for a
        /// tetrahedron, eight for a brick - without reordering them.
        ///
        /// For reading a deck back, where the element line already carries the order the solver
        /// was given and the only thing left to do is draw it.
        /// </summary>
        public static Mesh SolidMesh(IList<Point3d> nodes)
        {
            if (nodes.Count == 4)
                return TetrahedronMesh(nodes);

            if (nodes.Count == 8)
                return HexahedronMesh(nodes);

            throw new Exception($"A solid has four nodes or eight, not {nodes.Count}.");
        }

        /// <summary>
        /// The mesh with any vertices sitting on top of one another merged into one, but only if
        /// that is what it takes to reach <paramref name="wanted"/>.
        ///
        /// A box drawn in Rhino and exploded, or one that has been through a boolean, arrives with
        /// its corners split - twenty four vertices where a brick has eight - and every corner then
        /// looks like a separate node. Welding it is what the user would have done by hand; doing it
        /// on a copy leaves the mesh they drew alone.
        /// </summary>
        private static Mesh Welded(Mesh mesh, int wanted)
        {
            if (mesh.Vertices.Count == wanted)
                return mesh;

            var working = mesh.DuplicateMesh();
            working.Vertices.CombineIdentical(true, true);

            return working;
        }

        /// <summary>
        /// An eight vertex mesh reordered into the node order OpenSees wants for a brick, and
        /// rebuilt as a closed outward facing mesh.
        ///
        /// The element carries its nodes in whatever order this hands back, so this is the one
        /// place that decides whether a brick is the right way out. It used to decide by firing a
        /// ray of a fixed 1000 units along the first face normal and counting crossings, which
        /// asks a question about the model's units rather than about the element - a solid deeper
        /// than that in the direction of the ray, or thinner than the 0.001 the ray was nudged by,
        /// answered it backwards. It now asks the solver's own question, whether the Jacobian is
        /// positive, and answers it exactly.
        /// </summary>
        public static Mesh CleanHexahedron(Mesh brick)
        {
            brick = Welded(brick, 8);

            if (brick.Vertices.Count != 8)
                throw new Exception(
                    $"A brick is a mesh of eight vertices. This one has {brick.Vertices.Count} once " +
                    "coincident vertices are merged.");

            if (brick.Faces.Count == 0 || !brick.Faces[0].IsQuad)
                throw new Exception(
                    "A brick is a mesh of six quadrilateral faces. This one's first face is not a quad.");

            // The first face, and facing it the four vertices that pair with its corners: for each
            // corner, the one vertex sharing an edge with it that is not itself on that face.
            var face = brick.Faces[0];
            var firstFace = new List<int> { face.A, face.B, face.C, face.D };
            var secondFace = new List<int>();

            foreach (int corner in firstFace)
            {
                var offFace = brick.Vertices.GetConnectedVertices(corner)
                                            .Where(i => i != corner && !firstFace.Contains(i))
                                            .ToList();

                if (offFace.Count != 1)
                    throw new Exception(
                        $"Vertex {corner} of this brick joins {offFace.Count} vertices off its first face, " +
                        "where a hexahedron joins exactly one. The mesh is not a closed eight vertex brick - " +
                        "look for a missing face, a split vertex, or two corners closer together than the " +
                        "model tolerance.");

                secondFace.Add(offFace[0]);
            }

            List<Point3d> Nodes() => firstFace.Concat(secondFace)
                                              .Select(i => (Point3d)brick.Vertices[i])
                                              .ToList();

            var nodes = Nodes();

            // Reversing the winding of both faces together turns the local frame inside out, and so
            // flips the sign of the Jacobian, while leaving each far vertex facing the same near one.
            if (HexahedronJacobian(nodes, 0.0, 0.0, 0.0) < 0.0)
            {
                firstFace.Reverse();
                secondFace.Reverse();
                nodes = Nodes();
            }

            CheckHexahedron(nodes);

            return HexahedronMesh(nodes);
        }

        /// <summary>
        /// A four vertex mesh reordered into the node order OpenSees wants for a tetrahedron, and
        /// rebuilt as a closed outward facing mesh. The counterpart of
        /// <see cref="CleanHexahedron"/>, and it decides the same question the same way.
        /// </summary>
        public static Mesh CleanTetrahedron(Mesh iMesh)
        {
            var nodes = Welded(iMesh, 4).Vertices.ToPoint3dArray().ToList();

            if (nodes.Count != 4)
                throw new Exception(
                    $"A tetrahedron is a mesh of four vertices. This one has {nodes.Count} once " +
                    "coincident vertices are merged.");

            // Swapping any two nodes turns the local frame inside out, and so flips the sign of the
            // Jacobian.
            if (TetrahedronJacobian(nodes) < 0.0)
            {
                var held = nodes[1];
                nodes[1] = nodes[2];
                nodes[2] = held;
            }

            if (TetrahedronJacobian(nodes) <= 0.0)
                throw new Exception(
                    "This tetrahedron encloses no volume: its four vertices lie on one plane, or two of " +
                    "them are the same point. OpenSees divides the shape function derivatives by the " +
                    "Jacobian, so there is nothing here it can solve.");

            return TetrahedronMesh(nodes);
        }

        public static List<Curve> Explode(Curve curve, bool recursive = false)
        {
            List<Curve> list = new List<Curve>();
            CurveSegments(list, curve, recursive);
            return list;
        }

        internal static bool CurveSegments(List<Curve> list, Curve curve, bool recursive)
        {
            if (curve == null)
            {
                return false;
            }
            PolyCurve polyCurve = curve as PolyCurve;
            if (polyCurve != null)
            {
                if (recursive)
                {
                    polyCurve.RemoveNesting();
                }
                Curve[] array = polyCurve.Explode();
                if (array == null)
                {
                    return false;
                }
                if (array.Length == 0)
                {
                    return false;
                }
                if (recursive)
                {
                    Curve[] array2 = array;
                    for (int i = 0; i < array2.Length; i++)
                    {
                        Curve curve2 = array2[i];
                        CurveSegments(list, curve2, recursive);
                    }
                }
                else
                {
                    Curve[] array3 = array;
                    for (int j = 0; j < array3.Length; j++)
                    {
                        Curve item = array3[j];
                        list.Add(item);
                    }
                }
                return true;
            }
            else
            {
                PolylineCurve polylineCurve = curve as PolylineCurve;
                if (polylineCurve != null)
                {
                    for (int k = 0; k < polylineCurve.PointCount - 1; k++)
                    {
                        list.Add(new LineCurve(polylineCurve.Point(k), polylineCurve.Point(k + 1)));
                    }
                    return true;
                }
                Polyline polyline;
                if (curve.TryGetPolyline(out polyline))
                {
                    for (int l = 0; l < polyline.Count - 1; l++)
                    {
                        list.Add(new LineCurve(polyline[l], polyline[l + 1]));
                    }
                    return true;
                }
                LineCurve lineCurve = curve as LineCurve;
                if (lineCurve != null)
                {
                    list.Add(lineCurve.DuplicateCurve());
                    return true;
                }
                ArcCurve arcCurve = curve as ArcCurve;
                if (arcCurve != null)
                {
                    list.Add(arcCurve.DuplicateCurve());
                    return true;
                }
                return CurveSegments(list, curve.ToNurbsCurve());
            }
        }
        private static bool CurveSegments(List<Curve> list, NurbsCurve nurbs)
        {
            int count = list.Count;
            if (nurbs == null)
            {
                return false;
            }
            double num = nurbs.Domain.Min;
            double max = nurbs.Domain.Max;
            //double num2;
            while (nurbs.GetNextDiscontinuity(Continuity.C1_locus_continuous, num, max, out double num2))
            {
                Interval interval = new Interval(num, num2);
                num = num2;
                if (interval.Length >= 1E-16)
                {
                    Curve curve = nurbs.Trim(interval);
                    if (curve.IsValid)
                    {
                        list.Add(curve);
                    }
                }
            }
            Interval interval2 = new Interval(num, max);
            if (interval2.Length > 1E-16)
            {
                Curve curve2 = nurbs.Trim(interval2);
                if (curve2.IsValid)
                {
                    list.Add(curve2);
                }
            }
            if (list.Count == count)
            {
                list.Add(nurbs);
            }
            return true;
        }


    }
}