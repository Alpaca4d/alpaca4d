using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The physical densities after every iteration of a run, to look back at any of them: frame 0 is the
    /// design the run starts from, frame k the design after k iterations.
    ///
    /// Kept small. A density is stored as a byte, 0 to 255 - a step of 0.004, far finer than any surface
    /// drawn from it at a threshold can show - so a frame is one byte per element instead of eight. Every
    /// <see cref="KeyEvery"/>th frame is stored whole; the ones between as their change from the frame
    /// before, which, as a run settles, is nearly all zeros. Each is deflated. A frame is read back by
    /// inflating its key frame and adding up to <see cref="KeyEvery"/> - 1 changes onto it.
    ///
    /// Safe to add to from the thread a run is on while another reads.
    /// </summary>
    public sealed class DensityHistory
    {
        /// <summary>How often a frame is stored whole.</summary>
        public const int KeyEvery = 16;

        private readonly object _gate = new object();
        private readonly List<byte[]> _frames;
        private byte[] _last;   // the last frame added, as bytes, to take the next one's change from
        private long _bytes;

        public DensityHistory()
        {
            _frames = new List<byte[]>();
        }

        private DensityHistory(List<byte[]> frames, byte[] last, long bytes)
        {
            _frames = frames;
            _last = last;
            _bytes = bytes;
        }

        /// <summary>How many frames: the iterations, and the starting design.</summary>
        public int Count
        {
            get { lock (_gate) return _frames.Count; }
        }

        /// <summary>How many bytes the frames take, deflated.</summary>
        public long Bytes
        {
            get { lock (_gate) return _bytes; }
        }

        /// <summary>The elements per frame; 0 before the first.</summary>
        public int Elements
        {
            get { lock (_gate) return _last?.Length ?? 0; }
        }

        /// <summary>The same frames, to go on adding to apart from this one: a run that goes on from another.</summary>
        public DensityHistory Copy()
        {
            lock (_gate)
                return new DensityHistory(new List<byte[]>(_frames), _last == null ? null : (byte[])_last.Clone(), _bytes);
        }

        public static byte Quantise(double density) => (byte)Math.Round(255.0 * Math.Max(0.0, Math.Min(1.0, density)));

        public void Add(IReadOnlyList<double> densities)
        {
            var bytes = new byte[densities.Count];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Quantise(densities[i]);

            lock (_gate)
            {
                if (_last != null && _last.Length != bytes.Length)
                    throw new ArgumentException($"A frame of {bytes.Length} densities after frames of {_last.Length}.");

                byte[] stored;
                if (_frames.Count % KeyEvery == 0)
                    stored = bytes;
                else
                {
                    stored = new byte[bytes.Length];
                    for (int i = 0; i < bytes.Length; i++)
                        stored[i] = unchecked((byte)(bytes[i] - _last[i]));
                }

                var packed = Deflate(stored);
                _frames.Add(packed);
                _bytes += packed.Length;
                _last = bytes;
            }
        }

        /// <summary>The densities of frame <paramref name="k"/>, to the byte they were stored as.</summary>
        public double[] this[int k]
        {
            get
            {
                List<byte[]> span;
                lock (_gate)
                {
                    if (k < 0 || k >= _frames.Count)
                        throw new ArgumentOutOfRangeException(nameof(k), $"There are frames 0 to {_frames.Count - 1}, not {k}.");
                    int key = k - k % KeyEvery;
                    span = _frames.GetRange(key, k - key + 1);
                }

                var bytes = Inflate(span[0]);
                for (int f = 1; f < span.Count; f++)
                {
                    var change = Inflate(span[f]);
                    for (int i = 0; i < bytes.Length; i++)
                        bytes[i] = unchecked((byte)(bytes[i] + change[i]));
                }

                var densities = new double[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                    densities[i] = bytes[i] / 255.0;
                return densities;
            }
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        private static byte[] Inflate(byte[] packed)
        {
            using (var input = new MemoryStream(packed))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                inflate.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
