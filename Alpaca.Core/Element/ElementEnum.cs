using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alpaca4d.Element
{
    public enum ElementType
    {
        ZeroLength,
        Beam,
        Shell,
        Brick,
        Link
    }

    public enum ElementClass
    {
        ZeroLength,
        ForceBeamColumn,
        ASDShellQ4,
        ASDShellT3,
        ShellNLDKGT,
        ShellDKGT,
        FourNodeTetrahedron,
        SSPBrick
    }
}