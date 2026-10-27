using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>What Topology Optimisation plays for each step shown.</summary>
    public enum StepSound
    {
        Off,

        /// <summary>A pitch from the step's compliance: higher as the design gets stiffer.</summary>
        Compliance,

        /// <summary>Grieg, In the Hall of the Mountain King: the next note for every step.</summary>
        MountainKing,

        /// <summary>Beethoven, Ode to Joy: the next note for every step.</summary>
        OdeToJoy,
    }

    /// <summary>
    /// What a step of a topology optimisation sounds like.
    ///
    /// <see cref="StepSound.Compliance"/> tells how the run is going: a short tone whose pitch rises as
    /// the design gets stiffer - the compliance of the design after that step, between the first
    /// design's and the lowest of the run, on a log scale, as compliance falls by orders of magnitude at
    /// first and by fractions of a percent at the end - rounded to a major pentatonic scale over two
    /// octaves from A3, which has no two notes a semitone apart, so any run of steps is in tune with
    /// itself. The step a converged run ended at plays the top note and its octave.
    ///
    /// The melodies play their next note for every step, so stepping through a run plays the tune, as fast
    /// as the steps go: notes held long in the score ring a little longer. They are tunes whose notes,
    /// rather than their rhythm, make them, so they stay recognisable at any pace. The step a converged run
    /// ended at plays the opening of Beethoven's Fifth. All three are in the public domain; the notes are
    /// written from the scores.
    /// </summary>
    public static class StepTone
    {
        /// <summary>Semitones above A3 (220 Hz): A, B, C#, E, F# in two octaves, and the A on top.</summary>
        public static readonly IReadOnlyList<int> Scale = new[] { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };

        public const double Base = 220.0;
        public const int Milliseconds = 80;

        /// <summary>
        /// Grieg, In the Hall of the Mountain King (Peer Gynt, 1875): the theme in B minor, as note and length in
        /// eighths. In the piece it comes round again and again, faster each time.
        /// </summary>
        public static readonly IReadOnlyList<(string Note, int Eighths)> MountainKing = Score(
            "B3/1 C#4/1 D4/1 E4/1 F#4/1 D4/1 F#4/2 " +
            "E#4/1 C#4/1 E#4/2 E4/1 C4/1 E4/2 " +
            "B3/1 C#4/1 D4/1 E4/1 F#4/1 D4/1 F#4/1 B4/1 " +
            "A4/1 F#4/1 D4/1 F#4/1 A4/4");

        /// <summary>Beethoven, Symphony No. 9 (1824): Ode to Joy, in D major.</summary>
        public static readonly IReadOnlyList<(string Note, int Eighths)> OdeToJoy = Score(
            "F#4/2 F#4/2 G4/2 A4/2 A4/2 G4/2 F#4/2 E4/2 D4/2 D4/2 E4/2 F#4/2 F#4/3 E4/1 E4/4 " +
            "F#4/2 F#4/2 G4/2 A4/2 A4/2 G4/2 F#4/2 E4/2 D4/2 D4/2 E4/2 F#4/2 E4/3 D4/1 D4/4");

        /// <summary>Beethoven, Symphony No. 5 (1808): its opening, for the step a run converged at.</summary>
        public static readonly IReadOnlyList<(string Note, int Eighths)> Fifth = Score(
            "G4/1 G4/1 G4/1 Eb4/8 F4/1 F4/1 F4/1 D4/8");

        /// <summary>
        /// The tones of the design after <paramref name="step"/> iterations, as (frequency in Hz, milliseconds).
        /// </summary>
        /// <param name="history">The run's iterations; the design after step k was analysed in iteration k + 1.</param>
        public static IReadOnlyList<(double Frequency, int Milliseconds)> Tones(IReadOnlyList<TopologyIteration> history, int step,
                                                                               bool converged, StepSound sound)
        {
            switch (sound)
            {
                case StepSound.Off:
                    return new (double, int)[0];
                case StepSound.Compliance:
                    return Tones(history, step, converged);
            }

            int n = history?.Count ?? 0;
            if (converged && step >= n)
                return Fifth.Select(Tone).ToList();

            var tune = sound == StepSound.OdeToJoy ? OdeToJoy : MountainKing;
            return new[] { Tone(tune[Math.Max(0, step) % tune.Count]) };
        }

        /// <summary>
        /// The compliance tone of the design after <paramref name="step"/> iterations: one, or two for the step a
        /// converged run ended at. None for a run with no iterations yet.
        /// </summary>
        public static IReadOnlyList<(double Frequency, int Milliseconds)> Tones(IReadOnlyList<TopologyIteration> history, int step, bool converged)
        {
            if (history == null || history.Count == 0)
                return new (double, int)[0];

            int n = history.Count;
            step = Math.Max(0, Math.Min(step, n));

            // The design after the last iteration was never analysed; the one before it is a step away.
            double compliance = history[Math.Min(step, n - 1)].Compliance;
            double first = history[0].Compliance;
            double lowest = history.Min(it => it.Compliance);

            double t = 0.0;
            if (first > 0.0 && lowest > 0.0 && compliance > 0.0 && first > lowest)
                t = Math.Max(0.0, Math.Min(1.0, Math.Log(first / compliance) / Math.Log(first / lowest)));

            double frequency = Note((int)Math.Round(t * (Scale.Count - 1)));
            if (converged && step == n)
                return new[] { (Note(Scale.Count - 1), Milliseconds), (2.0 * Note(Scale.Count - 1), 2 * Milliseconds) };
            return new[] { (frequency, Milliseconds) };
        }

        /// <summary>The frequency of note <paramref name="index"/> of <see cref="Scale"/>.</summary>
        public static double Note(int index) => Base * Math.Pow(2.0, Scale[index] / 12.0);

        /// <summary>The frequency of a note by name and octave - A4, C#5, Eb4, E#4 - in equal temperament, A4 at 440 Hz.</summary>
        public static double Frequency(string note)
        {
            string name = note.Substring(0, note.Length - 1);
            int octave = note[note.Length - 1] - '0';
            int semitone = "C D EF G A B".IndexOf(name[0]);
            if (semitone < 0 || octave < 0 || octave > 9)
                throw new ArgumentException($"{note} is not a note.");
            semitone += name.Count(c => c == '#') - name.Count(c => c == 'b');
            int midi = 12 * (octave + 1) + semitone;
            return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
        }

        /// <summary>
        /// A note of a melody as a tone: an eighth 110 ms, longer notes a little longer each - a stepped-through
        /// tune keeps a hint of its rhythm, and the next step cuts a long note short anyway.
        /// </summary>
        private static (double Frequency, int Milliseconds) Tone((string Note, int Eighths) note) =>
            (Frequency(note.Note), (int)Math.Round(110.0 * Math.Pow(note.Eighths, 0.75)));

        private static IReadOnlyList<(string Note, int Eighths)> Score(string score) => score
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split('/'))
            .Select(parts => (parts[0], int.Parse(parts[1])))
            .ToArray();
    }
}
