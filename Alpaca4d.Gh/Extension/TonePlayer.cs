using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Plays short tones - beeps of a chosen pitch - on Windows and on macOS.
    ///
    /// Console.Beep(frequency, duration) would be the obvious way, and it works only on Windows: on macOS
    /// it throws. So the tones are made here, as a sine with a soft start and end, into a WAV, and played by
    /// the system: SoundPlayer on Windows, afplay on macOS. Asynchronous, and a new tone cuts off the one
    /// still playing, so dragging a slider fast plays where it is, not a queue of where it was. A tone that
    /// cannot be played is skipped without a word: no sound is no reason to fail a solution.
    /// </summary>
    internal static class TonePlayer
    {
        private const int Rate = 22050;
        private const double Volume = 0.3;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        private static Process _playing;

        public static void Play(IReadOnlyList<(double Frequency, int Milliseconds)> tones)
        {
            if (tones == null || tones.Count == 0)
                return;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlayOnWindows(Wav(tones));
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    PlayOnMac(tones);
            }
            catch (Exception)
            {
            }
        }

        // Not inlined, so that SoundPlayer is only looked for on Windows, where it is.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PlayOnWindows(byte[] wav)
        {
            // PlaySound plays one sound at a time for the process, so this stops the last one.
            new System.Media.SoundPlayer(new MemoryStream(wav)).Play();
        }

        private static void PlayOnMac(IReadOnlyList<(double Frequency, int Milliseconds)> tones)
        {
            string key = string.Join("_", tones.Select(t => $"{t.Frequency:0.0}-{t.Milliseconds}"));
            string file;
            lock (Gate)
            {
                if (!Files.TryGetValue(key, out file) || !File.Exists(file))
                {
                    string folder = Path.Combine(Path.GetTempPath(), "Alpaca4d", "Tones");
                    Directory.CreateDirectory(folder);
                    file = Path.Combine(folder, key + ".wav");
                    File.WriteAllBytes(file, Wav(tones));
                    Files[key] = file;
                }

                try
                {
                    if (_playing != null && !_playing.HasExited)
                        _playing.Kill();
                }
                catch (InvalidOperationException) { }
                _playing?.Dispose();

                _playing = Process.Start(new ProcessStartInfo("/usr/bin/afplay", "\"" + file + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
        }

        /// <summary>The tones one after the other, 16 bit mono, each with a 5 ms rise and a falling tail.</summary>
        internal static byte[] Wav(IReadOnlyList<(double Frequency, int Milliseconds)> tones)
        {
            var samples = new List<short>();
            foreach (var (frequency, milliseconds) in tones)
            {
                int count = Rate * milliseconds / 1000;
                int rise = Rate * 5 / 1000;
                for (int i = 0; i < count; i++)
                {
                    double envelope = Math.Min(1.0, i / (double)rise) * Math.Exp(-3.0 * i / count) * Math.Min(1.0, (count - i) / (double)rise);
                    samples.Add((short)(short.MaxValue * Volume * envelope * Math.Sin(2.0 * Math.PI * frequency * i / Rate)));
                }
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                int data = samples.Count * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + data);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);          // PCM
                writer.Write((short)1);          // mono
                writer.Write(Rate);
                writer.Write(Rate * 2);          // bytes a second
                writer.Write((short)2);          // bytes a sample
                writer.Write((short)16);         // bits a sample
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(data);
                foreach (var sample in samples)
                    writer.Write(sample);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
