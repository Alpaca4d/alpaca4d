# Third-party notices

## Colibri

The Explorer components (`Alpaca4d.Gh/12_Optimisation`, `Alpaca.Core/Explorer`) do what Colibri's
Iterator, Outputs, Aggregator, Image Setting and Iteration Subdomain components did, and write
the same study format, so that studies open in Design Explorer and in the Alpaca4d Explorer
alike. They were written for Alpaca4d rather than copied, but follow Colibri's design closely.

<https://github.com/tt-acm/Colibri.Grasshopper>

```
MIT License

Those portions created by authors during the 2016 AEC Technology Hackathon are provided with
the following copyright:

Copyright (c) 2016 Colibri.Grasshopper

Those portions created by Thornton Tomasetti employees are provided with the following
copyright:

Copyright (c) 2016 Thornton Tomasetti

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NLopt

Non-Linear Optimisation (`Alpaca4d.Gh/12_Optimisation`) runs NLopt's derivative-free algorithms -
DIRECT-L, CRS2-LM, ISRES, COBYLA, BOBYQA, Nelder-Mead and Sbplx - ported to C# in
`Alpaca.Core/Optimisation`, together with the parts of NLopt they rely on. Each ported file keeps
the notice of the file it was ported from. None of NLopt's LGPL code (the `luksan` directory) is
used, so all of it is under the MIT license below, with the additional copyright holders that
follow it.

<https://github.com/stevengj/nlopt>

```
Copyright (c) 2007-2024 Massachusetts Institute of Technology

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

COBYLA, under the same terms:

```
Copyright (c) 1992, Michael J. D. Powell (M.J.D.Powell@damtp.cam.ac.uk)
Copyright (c) 2004, Jean-Sebastien Roy (js@jeannot.org)
Copyright (c) 2008, Steven G. Johnson (stevenj@alum.mit.edu)
```

BOBYQA, under the same terms:

```
Copyright (c) 2009 M. J. D. Powell (mjdp@cam.ac.uk)
Modifications Copyright (c) 2010 Massachusetts Institute of Technology
```

The random numbers CRS2-LM and ISRES draw come from NLopt's Mersenne Twister
(`Alpaca.Core/Optimisation/NLopt/MersenneTwister.cs`), under its own BSD license:

```
Copyright (C) 1997 - 2002, Makoto Matsumoto and Takuji Nishimura,
All rights reserved.

Modified 2007 by Steven G. Johnson for use with NLopt (to avoid
namespace pollution, use uint32_t instead of unsigned long,
and add the urand function).  Modified 2009 to add normal-distributed
random numbers.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

  1. Redistributions of source code must retain the above copyright
     notice, this list of conditions and the following disclaimer.

  2. Redistributions in binary form must reproduce the above copyright
     notice, this list of conditions and the following disclaimer in the
     documentation and/or other materials provided with the distribution.

  3. The names of its contributors may not be used to endorse or promote
     products derived from this software without specific prior written
     permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE
COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
OF THE POSSIBILITY OF SUCH DAMAGE.
```
