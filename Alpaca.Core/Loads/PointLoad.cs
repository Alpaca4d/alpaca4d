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
    public partial class PointLoad : ILoad
    {
        public Point3d Pos { get; set; }
        public Vector3d Force { get; set; }
        public Vector3d Moment { get; set; }
        public int Ndf { get; set; }
        public LoadType Type { get; set; } = LoadType.PointLoad;
        public int? Id { get; set; }
        public int PatternTag { get; set; }
        public ITimeSeries TimeSeries { get; set; }

        public PointLoad()
        {

        }
        public PointLoad(Point3d pos, Vector3d force, Vector3d moment, ITimeSeries timeSeries)
        {
            this.Pos = pos;
            this.Force = force;
            this.Moment = moment;
            this.TimeSeries = timeSeries;
        }

        public PointLoad(int? id, Vector3d force, Vector3d moment, ITimeSeries timeSeries)
        {
            this.Id = id;
            this.Force = force;
            this.Moment = moment;
            this.TimeSeries = timeSeries;
        }


        //public void SetTag(Model model)
        //{
        //    try
        //    {
        //        if (this.Pos.DistanceTo(Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsThreeNDF, this.Pos)) < model.Tollerance)
        //        {
        //            this.Id = model.CloudPointThreeNDF.ClosestPoint(this.Pos) + 1;
        //            this.Ndf = 3;
        //        }
        //        else if (this.Pos.DistanceTo(Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsSixNDF, this.Pos)) < model.Tollerance)
        //        {
        //            this.Id = model.CloudPointSixNDF.ClosestPoint(this.Pos) + 1 + model.UniquePointsThreeNDF.Count();
        //            this.Ndf = 6;

        //        }
        //    }
        //    catch
        //    {
        //        if (this.Pos.DistanceTo(Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsSixNDF, this.Pos)) < model.Tollerance)
        //        {
        //            this.Id = model.CloudPointSixNDF.ClosestPoint(this.Pos) + 1 + model.UniquePointsThreeNDF.Count();
        //            this.Ndf = 6;
        //        }
        //        else if (this.Pos.DistanceTo(Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsThreeNDF, this.Pos)) < model.Tollerance)
        //        {
        //            this.Id = model.CloudPointThreeNDF.ClosestPoint(this.Pos) + 1;
        //            this.Ndf = 3;
        //        }
        //    }
        //}

        public void SetTag(Model model)
        {

            if (model.UniquePointsThreeNDF.Count != 0)
                {
                    var closestPointInThreeNDF = Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsThreeNDF, this.Pos);
                    if (this.Pos.DistanceTo(closestPointInThreeNDF) <= model.Tollerance)
                    {
                        this.Id = model.CloudPointThreeNDF.ClosestPoint(this.Pos) + 1;
                        this.Ndf = 3;
                    }
                    else
                        throw new Exception($"Point load at location '{this.Pos}' is not part of the model!");
                }

                if (model.UniquePointsSixNDF.Count != 0)
                {
                    var closestPointInSixNDF = Rhino.Collections.Point3dList.ClosestPointInList(model.UniquePointsSixNDF, this.Pos);
                    if (this.Pos.DistanceTo(closestPointInSixNDF) <= model.Tollerance)
                    {
                        this.Id = model.CloudPointSixNDF.ClosestPoint(this.Pos) + 1 + model.UniquePointsThreeNDF.Count();
                        this.Ndf = 6;
                }
                    else
                        throw new Exception($"Point load at location '{this.Pos}' is not part of the model!");
                }
        }

        public string WriteTcl()
        {
            string tcl;
            if(this.Ndf == 3)
            {
                tcl = $"\tload {this.Id} {this.Force.X} {this.Force.Y} {this.Force.Z}\n";
            }
            else if(this.Ndf == 6)
            {
                tcl = $"\tload {this.Id} {this.Force.X} {this.Force.Y} {this.Force.Z} {this.Moment.X} {this.Moment.Y} {this.Moment.Z}\n";
            }
            else
            {
                throw new Exception($"Ndf is not defined. node with coordinate {this.Pos} does not belong to the model");
            }

            return tcl;

        }
    }
}
