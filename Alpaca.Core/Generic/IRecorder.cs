using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alpaca4d.Generic
{
    public interface IRecorder
    {
        public string FileName { get; set; }

        /// <summary>The recorder command, written after the model and before the analysis.</summary>
        public string WriteTcl();
    }
}
