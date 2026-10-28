using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d;
using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Loads
{
    public partial class MeshLoad : ILoad
    {
        public int? Id { get; set; }
        public IShell Element { get; set; }

        /// <summary>
        /// Text naming the shells the load goes on, read as the result filters read it: an ElementId,
        /// a wildcard over them, an element tag, or "regex:...". Looked at only when there is no
        /// <see cref="Element"/>; with neither, the load goes on every shell. A mesh given one
        /// ElementId on ASD Shell is loaded whole by that name, every face of it.
        /// </summary>
        public string Filter { get; set; }

        public Vector3d GlobalForce { get; set; }
        public Vector3d LocalForce { get; set; }
        public int Ndf { get; set; }
        public LoadType Type { get; set; } = LoadType.MeshLoad;
        public int PatternTag { get; set; }
        public ITimeSeries TimeSeries { get; set; }
        public bool IsLocal { get; set; }

        public List<PointLoad> PointLoads
        {
            get
            {
                var pointsLoads = new List<PointLoad>();
                var indexes = this.Element.IndexNodes;
                var numberOfPoints = indexes.Count;
                var meshArea = Alpaca4d.Utils.MeshArea(this.Element.Mesh);
                foreach (var nodeId in indexes)
                {
                    var forceX = this.GlobalForce.X * meshArea / numberOfPoints;
                    var forceY = this.GlobalForce.Y * meshArea / numberOfPoints;
                    var forceZ = this.GlobalForce.Z * meshArea / numberOfPoints;
                    var pointLoad = new PointLoad((int)nodeId, new Rhino.Geometry.Vector3d(forceX, forceY, forceZ), new Rhino.Geometry.Vector3d(0,0,0), this.TimeSeries);
                    pointLoad.Ndf = 6;
                    pointsLoads.Add(pointLoad);
                }
                return pointsLoads;
            }
        }



        public MeshLoad()
        {

        }
        public MeshLoad(Alpaca4d.Generic.IShell element, Vector3d force, ITimeSeries timeSeries, bool local = false)
        {
            this.Element = element;
            if (local)
            {
                this.LocalForce = force;
                this.GlobalForce = this.LocalToGlobal(force);
            }
            else
                this.GlobalForce = force;
            this.TimeSeries = timeSeries;
        }

        public Vector3d LocalToGlobal(Vector3d force)
        {
            return new Vector3d();
        }

        /// <summary>
        /// The shells out of <paramref name="shells"/> the load goes on: its own <see cref="Element"/>,
        /// the ones its <see cref="Filter"/> names, or with neither all of them.
        /// </summary>
        public List<IShell> AppliedTo(IEnumerable<IShell> shells)
        {
            if (this.Element != null)
                return new List<IShell> { this.Element };

            return Alpaca4d.Result.ElementFilter.Named(this.Filter, shells);
        }

        public void SetTag(Model model)
        {

        }

        public string WriteTcl()
        {
            var sb = new StringBuilder();
            foreach(var load in this.PointLoads)
            {
                sb.Append(load.WriteTcl());
            }
            return sb.ToString();
        }
    }
}