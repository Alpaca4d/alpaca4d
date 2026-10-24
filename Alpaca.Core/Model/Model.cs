using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;

using Rhino.Geometry;
using Alpaca4d.Element;
using Alpaca4d.Material;
using Alpaca4d.Generic;
using Alpaca4d;
using Alpaca4d.Loads;

namespace Alpaca4d
{
    public class Model
    {
        public int Ndm { get; set; }
        public int Ndf { get; set; }
        public List<Point3d> UniquePoints { get; set; }
        public List<Point3d> UniquePointsThreeNDF { get; set; } = new List<Point3d>();
        public List<Point3d> UniquePointsSixNDF { get; set; } = new List<Point3d>();

        /// <summary>
        /// The nodes of each pool by where they stand, which Assemble finds every node through:
        /// an element's ends, a support, a load. See <see cref="NodeIndex"/>.
        /// </summary>
        public NodeIndex NodeIndexThreeNDF { get; set; } = new NodeIndex(0.0);
        public NodeIndex NodeIndexSixNDF { get; set; } = new NodeIndex(0.0);

        public PointCloud PointCloud { get; set; }

        // The clouds and trees of the nodes, for whoever still asks for them; Assemble itself goes
        // through the node indexes. Made when first read, since building them costs as much as
        // the rest of a large model.
        private PointCloud _cloudPointThreeNDF, _cloudPointSixNDF;
        private RTree _rTreeCloudPointThreeNDF, _rTreeCloudPointSixNDF;

        public PointCloud CloudPointThreeNDF
        {
            get => _cloudPointThreeNDF ?? (_cloudPointThreeNDF = this.UniquePointsThreeNDF.Count > 0 ? new PointCloud(this.UniquePointsThreeNDF) : null);
            set => _cloudPointThreeNDF = value;
        }

        public PointCloud CloudPointSixNDF
        {
            get => _cloudPointSixNDF ?? (_cloudPointSixNDF = this.UniquePointsSixNDF.Count > 0 ? new PointCloud(this.UniquePointsSixNDF) : null);
            set => _cloudPointSixNDF = value;
        }

        public RTree RTreeCloudPoint { get; set; }

        public RTree RTreeCloudPointThreeNDF
        {
            get => _rTreeCloudPointThreeNDF ?? (_rTreeCloudPointThreeNDF = this.UniquePointsThreeNDF.Count > 0 ? RTree.CreateFromPointArray(this.UniquePointsThreeNDF) : null);
            set => _rTreeCloudPointThreeNDF = value;
        }

        public RTree RTreeCloudPointSixNDF
        {
            get => _rTreeCloudPointSixNDF ?? (_rTreeCloudPointSixNDF = this.UniquePointsSixNDF.Count > 0 ? RTree.CreateFromPointArray(this.UniquePointsSixNDF) : null);
            set => _rTreeCloudPointSixNDF = value;
        }
        public static int NumberOfBeam { get; set; }
        public List<Node> Nodes { get; set; } = new List<Node> { };
        public List<IBeam> Beams { get; set; } = new List<IBeam> { };
        public List<IShell> Shells { get; set; } = new List<IShell> { };
        public List<IBrick> Bricks { get; set; } = new List<IBrick> { };

        /// <summary>
        /// The links. Kept apart from the other three because they carry no geometry the model can
        /// weigh, mesh or draw a stress on: a link is a stiffness between two nodes and nothing else.
        /// </summary>
        public List<ILink> Links { get; set; } = new List<ILink> { };
        public List<IElement> Elements { get; set; } = new List<IElement> { };
        public List<ForceBeamColumn> ForceBeamColumns { get; set; }
        public List<LoadPattern> LoadPatterns { get; set; } = new List<LoadPattern> { };
        public List<ILoad> Loads { get; set; } = new List<ILoad> { };
        public List<Alpaca4d.Loads.PointLoad> PointLoad
        {
            get
            {
                var pointLoadType = this.Loads.OfType<Alpaca4d.Loads.PointLoad>();
                return pointLoadType.ToList();
            }
        }
        public List<Alpaca4d.Loads.PointLoad> GravityPointLoad { get; set; } = new List<Alpaca4d.Loads.PointLoad> { };
        public List<Alpaca4d.Loads.LineLoad> LineLoad
        {
            get
            {
                var lineLoadType = this.Loads.OfType<Alpaca4d.Loads.LineLoad>();
                return lineLoadType.ToList();
            }
        }
        public List<Alpaca4d.Loads.MeshLoad> MeshLoad
        {
            get
            {
                var meshLoadType = this.Loads.OfType<Alpaca4d.Loads.MeshLoad>();
                return meshLoadType.ToList();
            }
        }
        public List<Alpaca4d.Loads.MassLoad> Mass { get; set; } = new List<Alpaca4d.Loads.MassLoad> { };
        public List<Support> Supports { get; set; } = new List<Support> { };
        public List<IConstraint> Constraint { get; set; } = new List<IConstraint> { };
        public List<IRecorder> Recorders { get; set; } = new List<IRecorder> { };


        public List<UniaxialMaterialElastic> UniaxialMaterialElastics { get; set; }
        public List<IMaterial> Materials { get; set; } = new List<IMaterial> { };
        public List<IUniaxialSection> ElasticSections { get; set; } = new List<IUniaxialSection> { };
        public double Tollerance { get; set; }
        public double? TotalMass
        {
            get
            {
                double? mass = 0.00;
                foreach (var item in this.Elements)
                {
                    if (item.Type == ElementType.Beam)
                    {
                        var beam = (IBeam)item;
                        var length = beam.Curve.GetLength();
                        var massDens = beam.Section.Area * beam.Section.Material.Rho;
                        mass += (massDens * length);
                    }

                    else if (item.Type == ElementType.Shell)
                    {
                        var shell = (IShell)item;

                        double meshArea = Alpaca4d.Utils.MeshArea(shell.Mesh);
                        var areaDensity = shell.Section.Thickness * (shell.Section.Material.Rho ?? 0.0);
                        mass += areaDensity * meshArea;
                    }

                    else if (item.Type == ElementType.Brick)
                    {
                        var brick = (IBrick)item;
                        var meshVolume = Alpaca4d.Utils.MeshVolume(brick.Mesh);
                        var density = brick.Material.Rho ?? 0.0;
                        mass += density * meshVolume;
                    }

                    // A link weighs nothing. It is a stiffness between two nodes, with no material
                    // and no volume for this to read a density off; weight at a link belongs to
                    // the mass loads counted below.
                    else if (item.Type == ElementType.Link)
                    {
                    }

                    else
                    {
                        throw new NotImplementedException($"{item.Type} has not been considered!");
                    }
                }

                foreach (var pointMass in this.Mass)
                {
                    mass += pointMass.TransMass.Z;
                }
                return mass;
            }
        }

        public string FileName { get; set; }
        public string ModalAnalysisReportFile { get; set; }

        public List<IStructure> ThreeNdfModel { get; set; } = new List<IStructure> { };
        public List<IStructure> SixNdfModel { get; set; } = new List<IStructure> { };

        public Analysis Analysis { get; set; }
        public Settings Settings { get; set; }
        public bool IsAnalysed { get; set; }
        public bool IsStatic { get; set; }
        public bool IsTransient { get; set; }
        public bool IsModal { get; set; }
        public int NumberOfModes { get; set; }

        /// <summary>
        /// Set by Linear Buckling Analysis on the model it hands on: its steps are buckling modes,
        /// step 0 the first, and its beams may be split.
        /// </summary>
        public bool IsBuckling { get; set; }

        /// <summary>The load factors of the buckling modes, lowest first, when <see cref="IsBuckling"/>.</summary>
        public List<double> BucklingFactors { get; set; } = new List<double>();

        public List<string> Tcl { get; set; } = new List<string> { };

        /// <summary>
        /// Where the load patterns begin in <see cref="Tcl"/>: Assemble writes them last, and a
        /// linear buckling analysis needs the model without them first. -1 when not assembled.
        /// </summary>
        public int LoadPatternTclStart { get; set; } = -1;

        public bool HasSSpBrick
        {
            get
            {
                bool hasSSpBrick = false;
                foreach(var brick in this.Bricks)
                {
                    if(brick.ElementClass == ElementClass.SSPBrick)
                    {
                        hasSSpBrick = true;
                        break;
                    }
                }
                return hasSSpBrick;
            }
        }

        public bool HasTetrahedron
        {
            get
            {
                bool hasTetrahedron = false;
                foreach (var brick in this.Bricks)
                {
                    if (brick.ElementClass == ElementClass.FourNodeTetrahedron)
                    {
                        hasTetrahedron = true;
                        break;
                    }
                }
                return hasTetrahedron;
            }
        }

        public bool HasTriShell
        {
            get
            {
                bool hasTriShell = false;
                foreach (var shell in this.Shells)
                {
                    if (shell.ElementClass == ElementClass.ShellDKGT || shell.ElementClass == ElementClass.ShellNLDKGT || shell.ElementClass == ElementClass.ASDShellT3)
                    {
                        hasTriShell = true;
                        break;
                    }
                }
                return hasTriShell;
            }
        }

        public bool HasQuadShell
        {
            get
            {
                bool hasQuadShell = false;
                foreach (var shell in this.Shells)
                {
                    if (shell.ElementClass == ElementClass.ASDShellQ4)
                    {
                        hasQuadShell = true;
                        break;
                    }
                }
                return hasQuadShell;
            }
        }

        public Dictionary<int?, Vector3d> NodalDisplacements(int step)
        {
            Dictionary<int?, Vector3d> result = new Dictionary<int?, Vector3d>();
            var _resultType = this.IsModal == false ? Result.ResultType.DISPLACEMENT : Result.ResultType.MODES_OF_VIBRATION_U;
            var dispVector = Alpaca4d.Result.Read.NodalOutput(this, step, _resultType).ToList();
            foreach(var node in this.Nodes)
            {
                result.Add(node.Id, dispVector[(int)node.Id - 1]);
            }
            return result;
        }


        public List<Curve> DeformedBeam(int step, double scale)
        {
            var dispDictionary = this.NodalDisplacements(step);

            var beamDefModel = new List<Curve>();

            foreach(var beam in this.Beams)
            {
                var beamCurve = beam.Curve;

                var startVector = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.INode]) * scale;
                var endvector = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.JNode]) * scale;

               beamDefModel.Add( new Rhino.Geometry.LineCurve(beamCurve.PointAtStart + startVector, beamCurve.PointAtEnd + endvector) );
            }
            return beamDefModel;
        }


        // 3d visualisation
        // WIP
        public List<Mesh> DeformedBeam(int step, double scale, List<System.Drawing.Color> colors, double min, double max)
        {
            var dispDictionary = this.NodalDisplacements(step);
            return DeformedBeam(dispDictionary, scale, colors, min, max);
        }

        public List<Mesh> DeformedBeam(Dictionary<int?, Vector3d> dispDictionary, double scale, List<System.Drawing.Color> colors, double min, double max)
        {

            var beamDefModel = new List<Mesh>();

            foreach (var beam in this.Beams)
            {
                var beamCurve = beam.Curve;

                var startVctr = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.INode]);
                var endVctr = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.JNode]);

                var startVector = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.INode]) * scale;
                var endvector = new Rhino.Geometry.Vector3d(dispDictionary[(int)beam.JNode]) * scale;
                

                var beamDeformed = new Rhino.Geometry.LineCurve(beamCurve.PointAtStart + startVector, beamCurve.PointAtEnd + endvector);

                var curve = beamDeformed;
                
                // Handle sections with multiple curves (e.g., double L-sections)
                var sectionCurves = beam.Section.Curves;

                var localY = beam.GeomTransf.LocalY;
                var localZ = beam.GeomTransf.LocalZ;
                var planeStart = new Rhino.Geometry.Plane(curve.PointAtStart, localZ, localY);
                var planeEnd = new Rhino.Geometry.Plane(curve.PointAtEnd, localZ, localY);

                var transfStart = Rhino.Geometry.Transform.PlaneToPlane(Plane.WorldXY, planeStart);
                var transfEnd = Rhino.Geometry.Transform.PlaneToPlane(Plane.WorldXY, planeEnd);

                // Create a mesh for each curve in the section
                foreach (var section in sectionCurves)
                {
                    var sectionStart = section.DuplicateCurve();
                    var sectionEnd = section.DuplicateCurve();

                    sectionStart.Transform(transfStart);
                    sectionEnd.Transform(transfEnd);

                    var polyStart = sectionStart.ToPolyline(0, 0, 0, 0).ToPolyline();
                    var polyEnd = sectionEnd.ToPolyline(0, 0, 0, 0).ToPolyline();
                    var sections = new List<Rhino.Geometry.Polyline> { polyStart, polyEnd };

                    var beamMesh = Alpaca4d.Utils.CreateLoft(sections, new List<double> { startVctr.Length, endVctr.Length }, colors, min, max);

                    beamDefModel.Add(beamMesh);
                }
            }


            return beamDefModel;
        }

        public List<Mesh> DeformedShell(int step, double scale, List<System.Drawing.Color> colors, double min, double max)
        {
            var dispDictionary = this.NodalDisplacements(step);
            return DeformedShell(dispDictionary, scale, colors, min, max);
        }

        public List<Mesh> DeformedShell(Dictionary<int?, Vector3d> dispDictionary, double scale, List<System.Drawing.Color> colors, double min, double max)
        {

            // find the total range of displacement
            var disp = dispDictionary.Values.Select(x => x.Length);

            var d = new SortedDictionary<double, System.Drawing.Color>();

            var numberOfColors = colors.Count;
            var diff = (max - min) / (numberOfColors-1);

            var start = min;
            foreach (var color in colors)
			{
                if(!d.ContainsKey(start))
                {
                    d.Add(start, color);
                    start += diff;
                }
			}

            var shellDefModel = new List<Mesh>();

            foreach (var item in this.Shells)
            {
                var points = item.Mesh.Vertices.Select(x => new Rhino.Geometry.Point3d(x));
                var pointDef = new List<Point3d>();
                var defValues = new List<double>();
                 
                int i = 0;
                foreach(var point in points)
                {
                    var indexNode = item.IndexNodes[i];
                    pointDef.Add(point + dispDictionary[indexNode] * scale);
                    defValues.Add(dispDictionary[indexNode].Length);
                    i++;
                }

                var shellDef = new Rhino.Geometry.Mesh();
                shellDef.Vertices.AddVertices(pointDef);

                var myColors = defValues.Select(x => (Colors.GetColor(x, d)));

                shellDef.VertexColors.SetColors(myColors.ToArray());


                if (item.IndexNodes.Count == 4)
                    shellDef.Faces.AddFace(0, 1, 2, 3);
                else
                    shellDef.Faces.AddFace(0, 1, 2);

                shellDefModel.Add(shellDef);
            }

            return shellDefModel;
        }

        public List<Mesh> DeformedBrick(int step, double scale, List<System.Drawing.Color> colors, double min, double max)
        {
            var dispDictionary = this.NodalDisplacements(step);
            return DeformedBrick(dispDictionary, scale, colors, min, max);
        }

        public List<Mesh> DeformedBrick(Dictionary<int?, Vector3d> dispDictionary, double scale, List<System.Drawing.Color> colors, double min, double max)
        {

            // find the total range of displacement
            var disp = dispDictionary.Values.Select(x => x.Length);

            var d = new SortedDictionary<double, System.Drawing.Color>();

            var numberOfColors = colors.Count;
            var diff = (max - min) / (numberOfColors - 1);

            var start = min;
            foreach (var color in colors)
            {
                if (!d.ContainsKey(start))
                {
                    d.Add(start, color);
                    start += diff;
                }
            }


            var brickDefModel = new List<Mesh>();

            foreach (var item in this.Bricks)
            {
                var points = item.Mesh.Vertices.Select(x => new Rhino.Geometry.Point3d(x));
                var pointDef = new List<Point3d>();
                var defValues = new List<double>();

                int i = 0;
                foreach (var point in points)
                {
                    var indexNode = item.IndexNodes[i];
                    pointDef.Add(point + dispDictionary[(int)indexNode] * scale);
                    defValues.Add(dispDictionary[(int)indexNode].Length);
                    i++;
                }

                var brickDef = new Rhino.Geometry.Mesh();
                brickDef.Vertices.AddVertices(pointDef);

                var myColors = defValues.Select(x => (Colors.GetColor(x, d)));

                brickDef.VertexColors.SetColors(myColors.ToArray());

                if (item.IndexNodes.Count == 8)
                {
                    brickDef.Faces.AddFace(0, 3, 2, 1);
                    brickDef.Faces.AddFace(4, 5, 6, 7);
                    brickDef.Faces.AddFace(0, 1, 5, 4);
                    brickDef.Faces.AddFace(1, 2, 6, 5);
                    brickDef.Faces.AddFace(2, 3, 7, 6);
                    brickDef.Faces.AddFace(3, 0, 4, 7);
                }
                else
                {
                    brickDef.Faces.AddFace(0, 1, 2);
                    brickDef.Faces.AddFace(0, 1, 3);
                    brickDef.Faces.AddFace(1, 2, 3);
                    brickDef.Faces.AddFace(0, 2, 3);
                }

                brickDefModel.Add(brickDef);
            }

            return brickDefModel;
        }


        //Constructor
        public Model()
        {

        }

        public Model(List<IElement> elements, List<Support> supports, List<LoadPattern> loads, List<IConstraint> constraints, List<IRecorder> recorders)
        {
            this.Elements = elements;
            this.Supports = supports;
            this.LoadPatterns = loads;
            this.Constraint = constraints;
            this.Recorders = recorders;
        }

        public Model ShallowCopy()
        {
            return (Model)this.MemberwiseClone();
        }

        /// <summary>
        /// This model again, assembled, with other sections on its beams: one section per beam, in the
        /// order of <see cref="Beams"/>. A beam that keeps its section is the same object; the others are
        /// new beams (<see cref="IBeam.WithSection"/>), so nothing upstream changes. Supports,
        /// constraints, masses and loads are this model's own, except that a line load on a beam that
        /// changed is moved onto the new one. Self-weight follows the new sections.
        /// </summary>
        public Model WithBeamSections(IReadOnlyList<IUniaxialSection> sections)
        {
            if (sections == null || sections.Count != this.Beams.Count)
                throw new ArgumentException($"{sections?.Count ?? 0} sections for {this.Beams.Count} beams.");

            var replaced = new Dictionary<IBeam, IBeam>();
            for (int i = 0; i < this.Beams.Count; i++)
            {
                var beam = this.Beams[i];
                if (!ReferenceEquals(sections[i], beam.Section))
                    replaced[beam] = beam.WithSection(sections[i]);
            }

            var elements = this.Elements
                .Select(item => item is IBeam beam && replaced.TryGetValue(beam, out var other) ? other : item)
                .ToList();

            var model = new Model(elements, this.Supports, this.LoadPatterns.Select(pattern => Repointed(pattern, replaced)).ToList(),
                                  this.Constraint, this.Recorders)
            {
                Mass = this.Mass,
                Tollerance = this.Tollerance,
            };
            model.Assemble();
            return model;
        }

        /// <summary>The pattern with its line loads on <paramref name="replaced"/> beams moved onto the new ones; itself when none are.</summary>
        private static LoadPattern Repointed(LoadPattern pattern, Dictionary<IBeam, IBeam> replaced)
        {
            if (pattern.PatternType == PatternType.UniformExcitation || pattern.Load == null ||
                !pattern.Load.Any(load => load is Alpaca4d.Loads.LineLoad line && line.Element != null && replaced.ContainsKey(line.Element)))
                return pattern;

            var loads = pattern.Load.Select(load =>
            {
                if (!(load is Alpaca4d.Loads.LineLoad line) || line.Element == null || !replaced.TryGetValue(line.Element, out var beam))
                    return load;

                return new Alpaca4d.Loads.LineLoad
                {
                    Id = line.Id,
                    Range = line.Range,
                    AllElements = line.AllElements,
                    Element = beam,
                    Filter = line.Filter,
                    GlobalForce = line.GlobalForce,
                    LocalForce = line.LocalForce,
                    Ndf = line.Ndf,
                    Type = line.Type,
                    PatternTag = line.PatternTag,
                    TimeSeries = line.TimeSeries,
                    IsLocal = line.IsLocal,
                };
            }).ToList();

            return new LoadPattern(pattern.PatternType, pattern.TimeSeries, loads, pattern.Factor) { Name = pattern.Name };
        }

        /// <summary>
        /// The nodes of the model: every point an element or a constraint reaches, welded within
        /// <see cref="Tollerance"/>, in the order they are reached - beams, shells, constraints for
        /// the six degree of freedom nodes, solids for the three. Each point joins the node already
        /// within tolerance of it, or becomes one, which is what Point3d.CullDuplicates kept, in the
        /// same order, at a fraction of the cost.
        /// </summary>
        public void GetUniquePoints(List<IBeam> beams, List<IShell> shells, List<IBrick> bricks, List<IConstraint> constraints)
        {
            var three = new NodeIndex(this.Tollerance);
            var six = new NodeIndex(this.Tollerance);

            foreach (var beam in beams)
            {
                six.Weld(beam.Curve.PointAtStart);
                six.Weld(beam.Curve.PointAtEnd);
            }

            foreach (var shell in shells)
                foreach (var point in shell.Mesh.Vertices.ToPoint3dArray())
                    six.Weld(point);

            foreach (var brick in bricks)
                foreach (var point in brick.Mesh.Vertices.ToPoint3dArray())
                    three.Weld(point);

            foreach (var constraint in constraints)
            {
                if(constraint.ConstraintType == Constraints.ConstraintType.RigidDiaphgram)
                {
                    var rigid = (Alpaca4d.Constraints.RigidDiaphragm)constraint;
                    six.Weld((Point3d)rigid.MasterNode);
                    foreach (var point in rigid.SlaveNodes)
                        six.Weld(point);
                }
                else if (constraint.ConstraintType == Constraints.ConstraintType.EqualDof)
                {
                    var equalDOF = (Alpaca4d.Constraints.EqualDOF)constraint;
                    six.Weld((Point3d)equalDOF.MasterNode);
                    six.Weld(equalDOF.SlaveNode);
                }
                else if (constraint.ConstraintType == Constraints.ConstraintType.RigidLink)
                {
                    var rigidLink = (Alpaca4d.Constraints.RigidLink)constraint;
                    six.Weld(rigidLink.RetainedNode);
                    six.Weld(rigidLink.ConstrainedNode);
                }
            }

            this.NodeIndexThreeNDF = three;
            this.NodeIndexSixNDF = six;
            this.UniquePointsThreeNDF = three.Points;
            this.UniquePointsSixNDF = six.Points;
            this._cloudPointThreeNDF = this._cloudPointSixNDF = null;
            this._rTreeCloudPointThreeNDF = this._rTreeCloudPointSixNDF = null;

            if (!this.UniquePointsThreeNDF.Any())
                this.UniquePoints = this.UniquePointsSixNDF;
            else if (!this.UniquePointsSixNDF.Any())
            {
                this.UniquePoints = this.UniquePointsThreeNDF;
            }
            else
            {
                this.UniquePoints = this.UniquePointsThreeNDF.Concat(this.UniquePointsSixNDF).ToList();
            }
        }

        /// <summary>
        /// The tag of the node already standing at <paramref name="point"/>, and through
        /// <paramref name="ndf"/> how many degrees of freedom it has.
        ///
        /// Everything that makes nodes - beams, shells, solids - has been through
        /// <see cref="GetUniquePoints"/> by the time this is called, so a point that finds nothing
        /// is a point no element reaches. That is worth saying plainly: the alternative is a
        /// spring quietly attached to the nearest node it could find, which looks right and is not.
        ///
        /// Six degree of freedom nodes are searched first because that is where a spring nearly
        /// always lands, and because a point shared by a shell and a solid exists in both pools -
        /// the six degree of freedom one being the one that can carry a moment.
        /// </summary>
        public int NodeTagAt(Point3d point, out int ndf, string owner)
        {
            int tag = this.NodeTagNear(point, out ndf);
            if (tag > 0)
                return tag;

            throw new Exception($"{owner} sits at {point}, where the model has no node. A spring joins " +
                                $"nodes that are already there; it does not make them. Move the end onto a " +
                                $"beam end, a mesh vertex or another node, or raise the model tolerance " +
                                $"(currently {this.Tollerance}).");
        }

        /// <summary>
        /// The tag of the node within tolerance of <paramref name="point"/> - six degree of freedom
        /// nodes first, as <see cref="NodeTagAt"/> explains - and through <paramref name="ndf"/> its
        /// degrees of freedom; 0 when there is none.
        /// </summary>
        public int NodeTagNear(Point3d point, out int ndf)
        {
            int found = this.NodeIndexSixNDF.Nearest(point);
            if (found >= 0)
            {
                ndf = 6;
                return found + 1 + this.UniquePointsThreeNDF.Count;
            }

            found = this.NodeIndexThreeNDF.Nearest(point);
            if (found >= 0)
            {
                ndf = 3;
                return found + 1;
            }

            ndf = 0;
            return 0;
        }

        /// <summary>
        /// Where the running node tag has reached. Zero until the first auxiliary node is asked
        /// for, so that the count of real nodes is read after the point cloud has been built.
        /// </summary>
        private int _auxiliaryNodeTag = 0;

        /// <summary>
        /// The next free node tag past the ones the point cloud handed out.
        ///
        /// A skewed support needs a node the geometry never asked for, to react against through the
        /// spring that carries its restraint. Anything else of that sort draws from here too, so
        /// that no two of them pick the same tag.
        /// </summary>
        public int NextNodeTag()
        {
            if (this._auxiliaryNodeTag == 0)
                this._auxiliaryNodeTag = this.UniquePointsThreeNDF.Count + this.UniquePointsSixNDF.Count;

            return ++this._auxiliaryNodeTag;
        }

        public override string ToString()
        {
            return "<Class Model>";
        }

        public static string Initialise3ndf()
        {
            return "model BasicBuilder -ndm 3 -ndf 3\n";
        }

        public static string Initialise6ndf()
        {
            return "model BasicBuilder -ndm 3 -ndf 6\n";
        }

        public void WriteTcl()
        {
            // write a method to store the text file
            // in a tcl property
        }

        public (string output, string error, int exitCode) RunOpenSees()
        {
            string openSeesPath = Application.OpenSees;

            if (string.IsNullOrWhiteSpace(openSeesPath))
                throw new InvalidOperationException(
                    "OpenSees executable path is not configured. " +
                    "Use Alpaca4d \u2192 Settings \u2192 Set OpenSees Executable in the Grasshopper menu.");

            if (!System.IO.File.Exists(openSeesPath))
                throw new System.IO.FileNotFoundException(
                    $"OpenSees executable not found at: \"{openSeesPath}\". " +
                    "Update it via Alpaca4d \u2192 Settings in the Grasshopper menu.");

            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            // Read once: the wait and the message it may produce have to agree, whatever
            // else does to the setting meanwhile.
            TimeSpan? timeout = Application.OpenSeesTimeout;

            // The Process owns a process handle and two pipe handles; without the using
            // they are only released whenever the finalizer happens to run.
            using (Process process = new Process())
            {
                // Configure the process using the StartInfo properties.
                process.StartInfo.FileName = openSeesPath;
                process.StartInfo.Arguments = "\"" + this.FileName + "\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                // Read stdout/stderr via events (not Task/async) so we never block on a full
                // pipe while the other stream is being written (deadlock risk with ReadToEnd
                // on both streams sequentially). This stays fully synchronous, which is
                // required for Grasshopper components.
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!WaitForOpenSees(process, timeout))
                {
                    // Nothing is going to end this wait on its own, and a caller with no
                    // way out is worse than a failed analysis: in Grasshopper it freezes
                    // the canvas, and in a test run it leaves a host process behind that
                    // outlives the run that started it.
                    TryKill(process);

                    throw new TimeoutException(
                        $"OpenSees did not finish within {timeout.Value} and was stopped. " +
                        "Raise or clear Alpaca4d.Application.OpenSeesTimeout if the model genuinely needs longer." +
                        Environment.NewLine + errorBuilder.ToString().Trim());
                }

                // A Gatekeeper kill looks like nothing at all from here. macOS SIGKILLs the
                // solver before it runs, so there is no output, nothing on stderr, and no
                // hint that the model was never read - and "the analysis produced nothing"
                // sends people looking at their model instead of at the executable. The
                // quarantine check is what separates this from an ordinary SIGKILL.
                if (process.ExitCode == Application.GatekeeperKillExitCode &&
                    outputBuilder.Length == 0 &&
                    Application.IsQuarantined(openSeesPath))
                {
                    throw new InvalidOperationException(Application.QuarantineHelp(openSeesPath));
                }

                this.IsAnalysed = true;
                return (outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim(), process.ExitCode);
            }
        }

        /// <summary>
        /// Waits for the solver, up to <paramref name="timeout"/> - null waits forever.
        /// False if the timeout ran out. The parameterless overload is the one that also
        /// drains the asynchronous stdout/stderr readers, so both paths end with it.
        /// </summary>
        private static bool WaitForOpenSees(Process process, TimeSpan? timeout)
        {
            if (!timeout.HasValue)
            {
                process.WaitForExit();
                return true;
            }

            // Clamped both ends: negative would throw, and anything past int.MaxValue
            // milliseconds is 24 days, which is the same thing as waiting forever.
            double milliseconds = Math.Max(0, Math.Min(timeout.Value.TotalMilliseconds, int.MaxValue));

            if (!process.WaitForExit((int)milliseconds))
                return false;

            // The timed overload returns as soon as the process is gone, without waiting
            // for the readers, so the last lines can still be in flight. This one blocks
            // until they are drained - and the process has already exited, so it returns.
            process.WaitForExit();
            return true;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException) { }   // exited between the check and the kill
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Serialise()
        {
            string filePath = System.IO.Path.GetFullPath(this.FileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                if(this.Recorders.Count != 0)
                    File.Delete(System.IO.Path.GetFullPath( this.Recorders.First().FileName));
            }

            using (var file = new StreamWriter(File.Create(filePath)))
            {
                foreach(var line in this.Tcl)
                {
                    file.WriteLine(line);
                }
            }
        }

        private void GetElements()
        {
            foreach (var element in this.Elements)
            {
                if (element.Type == ElementType.Beam)
                {
                    this.Beams.Add((IBeam)element);
                }
                else if (element.Type == ElementType.Shell)
                {
                    this.Shells.Add((IShell)element);
                }
                else if (element.Type == ElementType.Brick)
                    this.Bricks.Add((IBrick)element);
                else if (element.Type == ElementType.Link)
                    this.Links.Add((ILink)element);
            }
        }

        private void CreateNodes()
        {
            // A node's tag is its place in its pool, from one, the three degree of freedom pool first.
            if (this.UniquePointsThreeNDF.Count != 0)
            {
                this.Tcl.Add(Model.Initialise3ndf());
                for (int i = 0; i < this.UniquePointsThreeNDF.Count; i++)
                {
                    var node = new Alpaca4d.Element.Node(this.UniquePointsThreeNDF[i]) { Ndf = 3, Id = i + 1 };
                    this.Nodes.Add(node);

                    this.Tcl.Add(node.WriteTcl());
                }
            }

            if (this.UniquePointsSixNDF.Count != 0)
            {
                this.Tcl.Add(Model.Initialise6ndf());
                for (int i = 0; i < this.UniquePointsSixNDF.Count; i++)
                {
                    var node = new Alpaca4d.Element.Node(this.UniquePointsSixNDF[i]) { Ndf = 6, Id = i + 1 + this.UniquePointsThreeNDF.Count };
                    this.Nodes.Add(node);

                    this.Tcl.Add(node.WriteTcl());
                }
            }
        }

        private void CreateSupports()
        {
            // A support lands on the node within tolerance of it, the six degree of freedom one when
            // a point has one of each, as NodeTagAt has it. It used to have to be in both pools of
            // a model with both, so no support of a mixed model could sit on a node of only one.
            foreach (var supportNode in this.Supports)
            {
                int tag = this.NodeTagNear(supportNode.Pos, out int ndf);
                if (tag == 0)
                    throw new Exception($"Support node at location '{supportNode.Pos}' is not part of the model!");

                supportNode.ndf = ndf;
                supportNode.Id = tag;
                (ndf == 3 ? this.ThreeNdfModel : this.SixNdfModel).Add(supportNode);
            }

            this.CreateSupportSprings();

            // A skewed support writes a node and a fix of its own, and both of those read
            // the number of degrees of freedom from whichever model builder is current.
            // The node block leaves that at 6 whenever the model has any beams or shells,
            // so a support sitting on a 3 ndf brick node needs the builder moved back
            // before it is written - and moved again for the next one.
            var activeNdf = this.UniquePointsSixNDF.Count != 0 ? 6 : 3;
            var deckNdf = activeNdf;

            foreach (var supportNode in this.Supports)
            {
                if (supportNode.ndf != activeNdf)
                {
                    this.Tcl.Add(supportNode.ndf == 3 ? Model.Initialise3ndf() : Model.Initialise6ndf());
                    activeNdf = supportNode.ndf;
                }

                this.Tcl.Add(supportNode.WriteTcl());
            }

            // Everything after this - the elements above all - expects the builder the
            // node block left behind.
            if (activeNdf != deckNdf)
                this.Tcl.Add(deckNdf == 3 ? Model.Initialise3ndf() : Model.Initialise6ndf());

            this.WarnOfSpringsHeldRigidly();

            // Trusses make six degree of freedom nodes, so the builder is at six here.
            this.RestrainTrussOnlyNodes();
        }

        /// <summary>
        /// How much stiffer than the structure itself a support spring is made.
        ///
        /// The restrained direction then gives way by about one part in a million of what
        /// the structure around it moves - orders below anything a user reads off a
        /// result - while spending only six of the roughly sixteen decimal digits a
        /// double carries, which leaves the solver about ten digits of conditioning to
        /// work with. Scaling by the model rather than hard-coding a stiffness is what
        /// keeps this unit-independent: a model in kN/m and the same model in N/mm come
        /// out equally fixed.
        /// </summary>
        private const double PenaltyFactor = 1.0e6;

        /// <summary>
        /// Gives every skewed support the auxiliary node, the element tag and the penalty
        /// materials its zeroLength needs.
        ///
        /// Auxiliary nodes are numbered above every real node and spring elements above
        /// every real element, on purpose. Node results in the recorder file are read by
        /// row, and the rows are ordered by node tag, so keeping the extra nodes last
        /// leaves the row of every node the user actually drew exactly where it was.
        /// </summary>
        private void CreateSupportSprings()
        {
            var skewed = this.Supports.Where(support => support.NeedsSpring).ToList();

            if (skewed.Count == 0)
                return;

            var stiffness = this.PenaltyStiffness();

            var springElementTag = this.Elements.Count;

            foreach (var support in skewed)
            {
                support.AuxiliaryNodeId = this.NextNodeTag();
                support.SpringElementId = ++springElementTag;
                support.TranslationSpring = SupportSpring(stiffness.translation);
                support.RotationSpring = SupportSpring(stiffness.rotation);

                // A spring support's own springs, a rigid one standing for the same penalty stiffness
                // a skewed support is held with.
                if (support.IsSpring)
                    support.SpringMaterials = support.RestrainedDirections
                        .Select(direction => support.SpringAlong(direction).Material(direction <= 3 ? stiffness.translation : stiffness.rotation))
                        .ToList();
            }
        }

        /// <summary>
        /// What Assemble found worth saying: things the model goes ahead with but that are probably not
        /// what was meant. Assemble Model shows them as warnings.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>
        /// Says so when a spring support and an ordinary one on the same node act along the same axis:
        /// the rigid one then takes the whole load in that direction, and the spring does nothing.
        /// Two springs on one axis are not a mistake - they act side by side.
        /// </summary>
        private void WarnOfSpringsHeldRigidly()
        {
            foreach (var node in this.Supports.Where(support => support.Id.HasValue).GroupBy(support => support.Id.Value))
            {
                var springs = node.Where(support => support.IsSpring).ToList();
                var rigid = node.Where(support => !support.IsSpring).ToList();
                if (springs.Count == 0 || rigid.Count == 0)
                    continue;

                foreach (var spring in springs)
                foreach (int along in spring.RestrainedDirections)
                {
                    var springAxis = AxisOf(spring.Plane, along);
                    bool held = rigid.Any(support => support.RestrainedDirections.Any(direction =>
                        (direction <= 3) == (along <= 3) && Math.Abs(AxisOf(support.Plane, direction) * springAxis) > 1e-6));

                    if (held)
                        this.Warnings.Add($"The spring support at {spring.Pos} acts along an axis an ordinary support on the same node " +
                                          "already holds, so the rigid support takes the whole load there and the spring does nothing. " +
                                          "Free that direction on the ordinary support.");
                }
            }
        }

        /// <summary>The axis of <paramref name="plane"/> a support direction 1-6 runs along or turns about.</summary>
        private static Vector3d AxisOf(Plane plane, int direction)
        {
            switch ((direction - 1) % 3)
            {
                case 0: return plane.XAxis;
                case 1: return plane.YAxis;
                default: return plane.ZAxis;
            }
        }

        /// <summary>
        /// A truss carries no moment, so a node only trusses reach has three rotations that nothing
        /// holds, and the stiffness matrix is singular. They are fixed here - all three, less any a
        /// support on the node already fixes, since OpenSees refuses a second fix of the same degree
        /// of freedom. A node with a beam, a shell, a link that turns, or any constraint on it is left
        /// alone: something there either holds the rotations or decides them.
        /// </summary>
        private void RestrainTrussOnlyNodes()
        {
            var trussNodes = new HashSet<int>();
            var heldNodes = new HashSet<int>();

            int Tag(Point3d point)
            {
                int found = this.NodeIndexSixNDF.Nearest(point);
                return found < 0 ? 0 : found + 1 + this.UniquePointsThreeNDF.Count;
            }

            foreach (var beam in this.Beams)
            {
                var into = beam is Truss ? trussNodes : heldNodes;
                into.Add(Tag(beam.Curve.PointAtStart));
                into.Add(Tag(beam.Curve.PointAtEnd));
            }

            if (trussNodes.Count == 0)
                return;

            foreach (var shell in this.Shells)
                foreach (var point in shell.Mesh.Vertices.ToPoint3dArray())
                    heldNodes.Add(Tag(point));

            foreach (var link in this.Links.OfType<Link>().Where(link => link.Directions.Any(direction => direction > 3)))
            {
                heldNodes.Add(Tag(link.Line.From));
                heldNodes.Add(Tag(link.Line.To));
            }

            foreach (var constraint in this.Constraint)
            {
                if (constraint is Alpaca4d.Constraints.RigidDiaphragm diaphragm)
                {
                    heldNodes.Add(Tag((Point3d)diaphragm.MasterNode));
                    foreach (var point in diaphragm.SlaveNodes)
                        heldNodes.Add(Tag(point));
                }
                else if (constraint is Alpaca4d.Constraints.EqualDOF tie)
                {
                    heldNodes.Add(Tag((Point3d)tie.MasterNode));
                    heldNodes.Add(Tag(tie.SlaveNode));
                }
                else if (constraint is Alpaca4d.Constraints.RigidLink rigidLink)
                {
                    heldNodes.Add(Tag(rigidLink.RetainedNode));
                    heldNodes.Add(Tag(rigidLink.ConstrainedNode));
                }
            }

            foreach (int node in trussNodes.Where(node => node > 0 && !heldNodes.Contains(node)).OrderBy(node => node))
            {
                // What a support on the node already fixes: only an axis-aligned rigid one writes a fix there.
                var fixedHere = this.Supports.Where(support => support.Id == node && support.ndf == 6 && !support.NeedsSpring).ToList();
                bool rx = !fixedHere.Any(support => support.Rx);
                bool ry = !fixedHere.Any(support => support.Ry);
                bool rz = !fixedHere.Any(support => support.Rz);

                if (rx || ry || rz)
                    this.Tcl.Add($"fix {node} 0 0 0 {Convert.ToInt16(rx)} {Convert.ToInt16(ry)} {Convert.ToInt16(rz)}\n");
            }
        }

        private static Alpaca4d.Material.UniaxialMaterialElastic SupportSpring(double stiffness)
        {
            return new Alpaca4d.Material.UniaxialMaterialElastic(
                "Alpaca4d support spring", stiffness, stiffness, 0.0, 0.0, 0.0);
        }

        /// <summary>
        /// The stiffness a support spring is given, translational and rotational, taken
        /// as <see cref="PenaltyFactor"/> times the stiffest thing in the model. Each
        /// element contributes the terms that actually appear on its stiffness diagonal -
        /// EA/L and 12EI/L^3 for a beam, 4EI/L and GJ/L for its rotations, membrane Et
        /// and plate Et^3/12 for a shell, E times a size for a brick - so the reference
        /// tracks the model whatever it is made of.
        /// </summary>
        private (double translation, double rotation) PenaltyStiffness()
        {
            double translation = 0.0;
            double rotation = 0.0;

            foreach (var beam in this.Beams)
            {
                var length = beam.Curve.GetLength();
                if (length <= 0.0)
                    continue;

                var section = beam.Section;
                var e = section.Material.E;
                var g = section.Material.G;
                var inertia = Math.Max(section.Iyy, section.Izz);

                translation = Math.Max(translation, e * section.Area / length);
                translation = Math.Max(translation, 12.0 * e * inertia / (length * length * length));
                rotation = Math.Max(rotation, 4.0 * e * inertia / length);
                rotation = Math.Max(rotation, g * section.J / length);
            }

            foreach (var shell in this.Shells)
            {
                var e = YoungsModulusOf(shell.Section.Material);
                var thickness = shell.Section.Thickness;

                translation = Math.Max(translation, e * thickness);
                rotation = Math.Max(rotation, e * thickness * thickness * thickness / 12.0);
            }

            foreach (var brick in this.Bricks)
            {
                var e = YoungsModulusOf(brick.Material);
                var size = CharacteristicSize(brick.Mesh);

                if (size > 0.0)
                    translation = Math.Max(translation, e * size);
            }

            // Nothing to scale against - a model of nothing but supports. Anything
            // positive will do; there is no structure for it to be stiff relative to.
            if (translation <= 0.0)
                translation = 1.0;
            if (rotation <= 0.0)
                rotation = translation;

            return (PenaltyFactor * translation, PenaltyFactor * rotation);
        }

        private static double YoungsModulusOf(IMultiDimensionMaterial material)
        {
            if (material is Alpaca4d.Material.ElasticIsotropicMaterial isotropic)
                return isotropic.E;

            if (material is Alpaca4d.Material.ElasticOrthotropicMaterial orthotropic)
                return Math.Max(orthotropic.Ex, Math.Max(orthotropic.Ey, orthotropic.Ez));

            return 0.0;
        }

        private static double CharacteristicSize(Mesh mesh)
        {
            if (mesh == null)
                return 0.0;

            var diagonal = mesh.GetBoundingBox(true).Diagonal;

            return (Math.Abs(diagonal.X) + Math.Abs(diagonal.Y) + Math.Abs(diagonal.Z)) / 3.0;
        }

        private List<PointLoad> CreateGravityLoad(Alpaca4d.Loads.Gravity gravityLoad = null)
        {
            var gravityPointLoad = new List<Alpaca4d.Loads.PointLoad>();

            double? mass = 0.00;
            foreach (var item in this.Elements)
            {
                if (item.Type == ElementType.Beam)
                {
                    var beam = (IBeam)item;
                    var length = beam.Curve.GetLength();
                    var massDens = beam.Section.Area * beam.Section.Material.Rho * 9.81 / 1000.0;
                    mass += (massDens * length);
                    if (gravityLoad != null)
                    {
                        var gravityPointLoadStart = new Alpaca4d.Loads.PointLoad((int)beam.INode, gravityLoad.Factor * new Rhino.Geometry.Vector3d(0, 0, (double)-(massDens * length / 2)), new Rhino.Geometry.Vector3d(0, 0, 0), gravityLoad.TimeSeries);
                        var gravityPointLoadEnd = new Alpaca4d.Loads.PointLoad((int)beam.JNode, gravityLoad.Factor * new Rhino.Geometry.Vector3d(0, 0, (double)-(massDens * length / 2)), new Rhino.Geometry.Vector3d(0, 0, 0), gravityLoad.TimeSeries);

                        gravityPointLoadStart.Ndf = 6;
                        gravityPointLoadEnd.Ndf = 6;
                        gravityPointLoadStart.Pos = this.UniquePoints[(int)beam.INode-1];
                        gravityPointLoadEnd.Pos = this.UniquePoints[(int)beam.JNode-1];
                        gravityPointLoad.Add(gravityPointLoadStart);
                        gravityPointLoad.Add(gravityPointLoadEnd);
                    }
                }

                if (item.Type == ElementType.Shell)
                {
                    var shell = (IShell)item;

                    double meshArea = Alpaca4d.Utils.MeshArea(shell.Mesh);
                    var areaDensity = shell.Section.Thickness * (shell.Section.Material.Rho ?? 0.0) * 9.81 / 1000.0;
                    mass += areaDensity * meshArea;
                    if (gravityLoad != null)
                    {
                        foreach (var index in shell.IndexNodes)
                        {
                            var gravityShellPointLoad = new Alpaca4d.Loads.PointLoad(index, gravityLoad.Factor * new Rhino.Geometry.Vector3d(0, 0, -(areaDensity * meshArea / (shell.Mesh.Vertices.Count))), new Rhino.Geometry.Vector3d(0, 0, 0), gravityLoad.TimeSeries);
                            gravityShellPointLoad.Ndf = 6;
                            gravityShellPointLoad.Pos = this.UniquePoints[(int)index -1];
                            gravityPointLoad.Add(gravityShellPointLoad);

                        }
                    }
                    //if item.CrossSection.__class__.__name__ != "LayeredShell":
                    //    areaDensity = item.CrossSection.thickness * item.CrossSection.material.rho
                    //    Mass += areaDensity * meshArea
                    //    if IsGravity:
                    //        for vertex in item.Mesh.Vertices.ToPoint3dArray():
                    //            gravityPointLoad = alpaca.PointLoad(vertex, g_factor * rg.Vector3d(0, 0, -(areaDensity * meshArea / len(item.Mesh.Vertices))) * GravityLoad.GravityFactor, rg.Vector3d(0, 0, 0), GravityLoad.TimeSeries)
                    //            AlpacaModel.loads.append(gravityPointLoad)
                    //else:
                    //    for thickness, material in zip(item.CrossSection.thickness, item.CrossSection.material):
                    //        areaDensity = thickness * material.rho
                    //        Mass += areaDensity * meshArea
                    //        if IsGravity:
                    //            for vertex in item.Mesh.Vertices.ToPoint3dArray():
                    //                gravityPointLoad = alpaca.PointLoad(vertex, g_factor * rg.Vector3d(0, 0, -(areaDensity * meshArea / len(item.Mesh.Vertices))) * GravityLoad.GravityFactor, rg.Vector3d(0, 0, 0), GravityLoad.TimeSeries)
                    //                AlpacaModel.loads.append(gravityPointLoad)
                }

                if (item.Type == ElementType.Brick)
                {
                    var brick = (IBrick)item;
                    var meshVolume = Alpaca4d.Utils.MeshVolume(brick.Mesh);
                    // A material given no density weighs nothing, which is a model, rather than a
                    // reason to stop: the cast this replaces threw on every nD material left at
                    // its default.
                    var density = (brick.Material.Rho ?? 0.0) * 9.81 / 1000.0;
                    mass += density * meshVolume;
                    if (gravityLoad != null)
                    {
                        foreach (var index in brick.IndexNodes)
                        {
                            var gravityBrickPointLoad = new Alpaca4d.Loads.PointLoad(index, gravityLoad.Factor * new Rhino.Geometry.Vector3d(0, 0, -(density * meshVolume / (brick.Mesh.Vertices.Count))), new Rhino.Geometry.Vector3d(0, 0, 0), gravityLoad.TimeSeries);
                            gravityBrickPointLoad.Ndf = 3;
                            gravityBrickPointLoad.Pos = this.UniquePoints[(int)index -1];
                            gravityPointLoad.Add(gravityBrickPointLoad);

                        }
                    }
                }
            }

            return gravityPointLoad;
        }

        private void addLoadPattern(List<LoadPattern> loadPatterns)
        {
            foreach (var loadPattern in loadPatterns)
            {
                if(loadPattern.PatternType == PatternType.UniformExcitation)
                    this.Tcl.Add(loadPattern.WriteTcl());
                else
                {
                    var myLoads = new List<ILoad>();

                    foreach (var item in loadPattern.Load)
                    {
                        if (item.Type == Alpaca4d.Loads.LoadType.PointLoad)
                        {
                            item.SetTag(this);
                            myLoads.Add((Loads.PointLoad)item);
                        }
                        else if (item.Type == Alpaca4d.Loads.LoadType.DistributedLoad)
                        {
                            item.SetTag(this);
                            var lineLoad = (Alpaca4d.Loads.LineLoad)item;
                            // Without a beam of its own, the load goes on every beam its filter names, or on every beam.
                            if (lineLoad.Element == null)
                            {
                                foreach (var beam in lineLoad.AppliedTo(this.Beams))
                                    myLoads.Add(new Alpaca4d.Loads.LineLoad(beam, lineLoad.GlobalForce, lineLoad.TimeSeries));
                            }
                            else
                                myLoads.Add(lineLoad);
                        }
                        else if (item.Type == Alpaca4d.Loads.LoadType.MeshLoad)
                        {
                            item.SetTag(this);
                            var meshLoad = (Alpaca4d.Loads.MeshLoad)item;
                            // Without a shell of its own, the load goes on every shell its filter names, or on every shell.
                            if (meshLoad.Element == null)
                            {
                                foreach (var shell in meshLoad.AppliedTo(this.Shells))
                                    myLoads.Add(new Alpaca4d.Loads.MeshLoad(shell, meshLoad.GlobalForce, meshLoad.TimeSeries));
                            }
                            else
                                myLoads.Add(meshLoad);
                        }
                        else if (item.Type == Alpaca4d.Loads.LoadType.Gravity)
                        {
                            var gravityLoad = this.CreateGravityLoad((Alpaca4d.Loads.Gravity)item);
                            myLoads.AddRange(gravityLoad);
                        }
                    }

                    var newLoadPattern = new LoadPattern(loadPattern.PatternType, loadPattern.TimeSeries, myLoads, loadPattern.Factor);
                    this.Tcl.Add(newLoadPattern.WriteTcl());
                }

            }
        }

        public void Assemble()
        {
            this.Warnings.Clear();
            this.GetElements();
            
            this.GetUniquePoints(this.Beams, this.Shells, this.Bricks, this.Constraint);

            this.CreateNodes();

            this.CreateSupports();

            //////////////////
            // Create Constraint
            //////////////////
            foreach (var constraint in this.Constraint)
            {
                constraint.SetTopologyRTree(this);
            }

            // create equalDOF when the model has 3df and 6df together
            // TODO

            // Where a solid meets a beam or a shell, a three and a six degree of freedom node stand
            // together: their translations are tied, every pair within tolerance, in the order of
            // the solid's nodes.
            if (this.UniquePointsThreeNDF.Count != 0 && this.UniquePointsSixNDF.Count != 0)
            {
                for (int i = 0; i < this.UniquePointsThreeNDF.Count; i++)
                {
                    var node_i = this.UniquePointsThreeNDF[i];
                    foreach (int j in this.NodeIndexSixNDF.Within(node_i))
                    {
                        var node_j = this.UniquePointsSixNDF[j];
                        var equalConstraint = new Alpaca4d.Constraints.EqualDOF(node_i, node_j, true, true, true, false, false, false);
                        equalConstraint.MasterNodeId = i + 1;
                        equalConstraint.SlaveNodeId = j + 1 + this.UniquePointsThreeNDF.Count;
                        this.Tcl.Add(equalConstraint.WriteTcl());
                    }
                }
            }



            var crossSections = new List<ISection>();
            var materials = new List<IMaterial>();


            foreach (var item in this.Elements)
            {
                if (item.Type == ElementType.Beam)
                {
                    crossSections.Add(((IBeam)item).Section);
                    materials.Add(((IBeam)item).Section.Material);
                }
                else if (item.Type == ElementType.Shell)
                {
                    crossSections.Add(((IShell)item).Section);
                    // Every layer's material, not just the first: a LayeredShell section stacks
                    // materials, and any left out here would be referenced by the section line
                    // without ever being declared.
                    materials.AddRange(((IShell)item).Section.Materials);
                }

                // A link carries one material per direction, and every one of them has to be
                // declared before the element line that names it.
                else if (item.Type == ElementType.Link)
                {
                    materials.AddRange(((ILink)item).Materials);
                }

                else
                    materials.Add(((IBrick)item).Material);
            }


            var uniqueSections = crossSections.Distinct().ToList();
            var uniqueMaterials = materials.Distinct().ToList();

            foreach (var material in uniqueMaterials)
            {
                this.Tcl.Add(material.WriteTcl());
            }

            foreach (var section in uniqueSections)
            {
                this.Tcl.Add(section.WriteTcl());
            }

            // assign tag and node Index to element and shell
            int index = 1;
            foreach(var element in this.Elements)
            {
                // The tag first, so that an element failing to find one of its nodes can name
                // itself in the message rather than reporting as element null.
                element.Id = index;
                element.SetTopologyRTree(this);
                element.SetTags();
                index++;
            }

            foreach(var item in this.Elements)
            {
                // The identifier goes out as a comment because OpenSees has no command for one:
                // the solver ignores the line, and Deserialise reads it back, so it survives a
                // Serialise/Deserialise round trip and can still be filtered on afterwards. The
                // element's own tag is written alongside because that is the only thing tying the
                // line to an element - the identifier itself need not be unique.
                if (!string.IsNullOrWhiteSpace(item.ElementId))
                    this.Tcl.Add($"# alpaca:elementid {item.Id} {item.ElementId.Trim()}\n");

                this.Tcl.Add(item.WriteTcl());
            }


            foreach(var item in this.Constraint)
            {
                this.Tcl.Add(item.WriteTcl());
            }


            foreach (var item in this.Mass)
            {
                item.SetTag(this);
                this.Tcl.Add(item.WriteTcl());
            }

            this.LoadPatternTclStart = this.Tcl.Count;
            this.addLoadPattern(this.LoadPatterns);
        }

    }
}