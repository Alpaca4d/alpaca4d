using System;

namespace Alpaca4d.Element
{
    public class Release
    {
        /// <summary>Axial translation along X. False = released (low stiffness).</summary>
        public bool Tx { get; set; } = true;
        /// <summary>Translation along Y. False = released (low stiffness).</summary>
        public bool Ty { get; set; } = true;
        /// <summary>Translation along Z. False = released (low stiffness).</summary>
        public bool Tz { get; set; } = true;
        /// <summary>Torsional rotation about X. False = released (low stiffness).</summary>
        public bool Rx { get; set; } = true;
        /// <summary>Rotation about Y (bending). False = released (low stiffness).</summary>
        public bool Ry { get; set; } = true;
        /// <summary>Rotation about Z (bending). False = released (low stiffness).</summary>
        public bool Rz { get; set; } = true;

        public static readonly Release FullFixed = new Release();

        public Release() { }

        public Release(bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            Tx = tx;
            Ty = ty;
            Tz = tz;
            Rx = rx;
            Ry = ry;
            Rz = rz;
        }
    }
}
