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
    public partial class MassLoad : ILoad
    {
        /// <summary>A mass held in kg, in the unit the model is solved in.</summary>
        public static Vector3d ToModelUnits(Vector3d massInKg) => Alpaca4d.ModelMass.FromKg(massInKg);

        /// <summary>A mass read off a deck, back in kg.</summary>
        public static Vector3d FromModelUnits(Vector3d mass) => Alpaca4d.ModelMass.ToKg(mass);

        public Point3d Pos { get; set; }

        /// <summary>Translational mass, in kg.</summary>
        public Vector3d TransMass { get; set; }

        /// <summary>Rotational mass, in kg m^2.</summary>
        public Vector3d RotationMass { get; set; }
        public int? Ndf { get; set; }
        public LoadType Type { get; set; } = LoadType.Mass;
        public ITimeSeries TimeSeries { get; set; }
        public int? Id { get; set; }

        public MassLoad()
        {

        }
        public MassLoad(Point3d pos, Vector3d transMass, Vector3d rotationMass)
        {
            this.Pos = pos;
            this.TransMass = transMass;
            this.RotationMass = rotationMass;
        }

        public void SetTag(Model model)
        {
            // The three degree of freedom node first, as it always was here.
            int found = model.NodeIndexThreeNDF.Nearest(this.Pos);
            if (found >= 0)
            {
                this.Id = found + 1;
                this.Ndf = 3;
                return;
            }

            found = model.NodeIndexSixNDF.Nearest(this.Pos);
            if (found >= 0)
            {
                this.Id = found + 1 + model.UniquePointsThreeNDF.Count;
                this.Ndf = 6;
            }
        }

        public override string ToString()
        {
            return base.ToString();
        }
        public string WriteTcl()
        {
            string tclText;

            // Held in kg, written in the model's mass unit - see Alpaca4d.ModelMass.
            var trans = ToModelUnits(this.TransMass);
            var rotation = ToModelUnits(this.RotationMass);

            if (this.Ndf == 6)
            {
                tclText = $"mass {Id} {trans.X} {trans.Y} {trans.Z} {rotation.X} {rotation.Y} {rotation.Z}\n";
            }
            else if(this.Ndf == 3)
            {
                tclText = $"mass {Id} {trans.X} {trans.Y} {trans.Z}\n";
            }
            else
            {
                throw new Exception("No ndf has been assigned");
            }
            return tclText;
        }

    }
}

