// Ported from NLopt, src/util/mt19937ar.c.
//
// A C-program for MT19937, with initialization improved 2002/1/26.
// Coded by Takuji Nishimura and Makoto Matsumoto.
//
// Copyright (C) 1997 - 2002, Makoto Matsumoto and Takuji Nishimura,
// All rights reserved.
//
// Modified 2007 by Steven G. Johnson for use with NLopt (to avoid
// namespace pollution, use uint32_t instead of unsigned long,
// and add the urand function).  Modified 2009 to add normal-distributed
// random numbers.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
//   1. Redistributions of source code must retain the above copyright
//      notice, this list of conditions and the following disclaimer.
//
//   2. Redistributions in binary form must reproduce the above copyright
//      notice, this list of conditions and the following disclaimer in the
//      documentation and/or other materials provided with the distribution.
//
//   3. The names of its contributors may not be used to endorse or promote
//      products derived from this software without specific prior written
//      permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
// FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE
// COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
// HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
// STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
// OF THE POSSIBILITY OF SUCH DAMAGE.

using System;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// NLopt's random numbers: nlopt_init_genrand, nlopt_urand, nlopt_iurand and nlopt_nrand.
    ///
    /// NLopt keeps one generator per thread; here each optimisation owns one, so two running at
    /// once cannot draw each other's numbers. Seeded the same, it gives the same numbers as NLopt,
    /// which is what lets CRS2 and ISRES be checked against NLopt step by step.
    /// </summary>
    internal sealed class MersenneTwister
    {
        private const int N = 624;
        private const int M = 397;
        private const uint MatrixA = 0x9908b0dfU;   // constant vector a
        private const uint UpperMask = 0x80000000U; // most significant w-r bits
        private const uint LowerMask = 0x7fffffffU; // least significant r bits

        private static readonly uint[] Mag01 = { 0x0U, MatrixA };

        private readonly uint[] _mt = new uint[N];
        private int _mti = N + 1; // N + 1: not initialised

        public MersenneTwister() { }

        public MersenneTwister(uint seed)
        {
            InitGenrand(seed);
        }

        /// <summary>nlopt_init_genrand: initialises the state with a seed.</summary>
        public void InitGenrand(uint s)
        {
            _mt[0] = s;
            for (_mti = 1; _mti < N; _mti++)
                _mt[_mti] = unchecked(1812433253U * (_mt[_mti - 1] ^ (_mt[_mti - 1] >> 30)) + (uint)_mti);
        }

        /// <summary>A random number on [0, 0xffffffff].</summary>
        public uint GenrandInt32()
        {
            uint y;

            if (_mti >= N)
            {
                // generate N words at one time
                int kk;

                if (_mti == N + 1)
                    InitGenrand(5489U); // a default initial seed is used

                for (kk = 0; kk < N - M; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ Mag01[y & 0x1U];
                }
                for (; kk < N - 1; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ Mag01[y & 0x1U];
                }
                y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
                _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ Mag01[y & 0x1U];

                _mti = 0;
            }

            y = _mt[_mti++];

            // Tempering
            y ^= y >> 11;
            y ^= (y << 7) & 0x9d2c5680U;
            y ^= (y << 15) & 0xefc60000U;
            y ^= y >> 18;

            return y;
        }

        /// <summary>A random number on [0, 1) with 53-bit resolution.</summary>
        private double GenrandRes53()
        {
            uint a = GenrandInt32() >> 5, b = GenrandInt32() >> 6;
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }

        /// <summary>nlopt_urand: uniform on [a, b).</summary>
        public double Urand(double a, double b) => a + (b - a) * GenrandRes53();

        /// <summary>nlopt_iurand: uniform integer on [0, n).</summary>
        public int Iurand(int n) => (int)(GenrandInt32() % (uint)n);

        /// <summary>nlopt_nrand: normally distributed, by the Box-Muller method (Knuth vol II, 3.4.1, algorithm P).</summary>
        public double Nrand(double mean, double stddev)
        {
            double v1, v2, s;
            do
            {
                v1 = Urand(-1, 1);
                v2 = Urand(-1, 1);
                s = v1 * v1 + v2 * v2;
            } while (s >= 1.0);
            if (s == 0)
                return mean;
            return mean + v1 * Math.Sqrt(-2 * Math.Log(s) / s) * stddev;
        }
    }
}
