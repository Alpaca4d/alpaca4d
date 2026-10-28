using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel.Geometry.Delaunay;

namespace Alpaca4d.Loads
{
    /// <summary>
    /// pattern Plain $patternTag $tsTag <-fact $cFactor> {
    /// load...
    /// eleLoad...
    /// sp...
    /// }
    ///...
    /// </summary>
    public partial class LoadPattern
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public List<ILoad> Load;
        public ITimeSeries TimeSeries;
        public PatternType PatternType;
        public double Factor;

        /// <summary>An optional name - the load case an imported pattern holds, say. OpenSees never sees it.</summary>
        public string Name { get; set; }

        // for UniformExcitation
        public Direction Dof { get; set; } = Direction.X;
        public double Velocity { get; set; } = 0.0;

        public LoadPattern()
        {
        }

        public LoadPattern(PatternType patternType, ITimeSeries timeSeries, List<ILoad> load, double factor = 1)
        {
            this.PatternType = patternType;
            this.TimeSeries = timeSeries;
            this.TimeSeries.Id = (int)Id;
            this.Load = load;
            this.Factor = factor;
        }

        public static LoadPattern CreateUniformExcitation(Direction direction, ITimeSeries timeSeries, double velocity = 0.0, double factor = 1.0)
        {
            var load = new LoadPattern();
            load.PatternType = PatternType.UniformExcitation;
            load.Dof = direction;
            load.Velocity = velocity;
            load.Factor = factor;
            load.TimeSeries = timeSeries;
            load.TimeSeries.Id = (int)load.Id;


            return load;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.Name) ? base.ToString() : $"LoadPattern {this.Name}";
        }

        public string WriteTcl()
        {
            var sb = new StringBuilder();
            sb.Append(this.TimeSeries.WriteTcl());
            
            if (PatternType == PatternType.Plain)
            {
                sb.Append($"pattern {PatternType} {Id} {TimeSeries.Id} -fact {Factor} {{\n");
                foreach (var load in Load)
                {
                    sb.Append(load.WriteTcl());
                }
                sb.Append("}\n");
            }

            if (PatternType == PatternType.UniformExcitation)
            {
                sb.Append($"pattern {PatternType} {Id} {(int)Dof} -accel {TimeSeries.Id} -vel0 {Velocity} -fact {Factor}");
            }

            return sb.ToString();
        }
    }

    public enum PatternType
    {
        Plain,
        UniformExcitation,
        //MultipleSupport
    }

    public enum Direction
    {
        X = 1,
        Y = 2,
        Z = 3,
        XX = 4,
        YY = 5,
        ZZ = 6
    }
}