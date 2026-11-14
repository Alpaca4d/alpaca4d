using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Alpaca4d
{
    /// <summary>One load of the lateral pattern a pushover scales: forces and moments on a node, at load factor 1.</summary>
    public sealed class PushoverLoad
    {
        public int Node { get; }

        /// <summary>The node's degrees of freedom: 3 for a solid's node, 6 for a beam's or a shell's.</summary>
        public int Ndf { get; }

        /// <summary>Fx, Fy, Fz in kN and Mx, My, Mz in kN m, global.</summary>
        public double[] Values { get; }

        public PushoverLoad(int node, int ndf, double fx, double fy, double fz, double mx = 0.0, double my = 0.0, double mz = 0.0)
        {
            if (ndf != 3 && ndf != 6)
                throw new ArgumentOutOfRangeException(nameof(ndf), "A node has 3 or 6 degrees of freedom.");
            Node = node;
            Ndf = ndf;
            Values = ndf == 3 ? new[] { fx, fy, fz } : new[] { fx, fy, fz, mx, my, mz };
        }

        public string WriteTcl() => $"load {Node} {string.Join(" ", Values.Select(v => TclNumber.Write(v)))}\n";
    }

    /// <summary>What a pushover gives back: the capacity curve, point by point, and how it ended.</summary>
    public sealed class PushoverCurve
    {
        /// <summary>The control node's displacement along the push, from where the model's own loads left it, m.</summary>
        public List<double> Displacement { get; } = new List<double>();

        /// <summary>The base shear: the reactions along the push, all of them, turned to resist it, kN.</summary>
        public List<double> BaseShear { get; } = new List<double>();

        /// <summary>The factor on the lateral loads.</summary>
        public List<double> LoadFactor { get; } = new List<double>();

        /// <summary>The recorded step of each point, for the result components and Model View to read.</summary>
        public List<int> Step { get; } = new List<int>();

        /// <summary>The push the analysis was asked for, m.</summary>
        public double Target { get; internal set; }

        /// <summary>The control node reached <see cref="Target"/>.</summary>
        public bool ReachedTarget { get; internal set; }

        /// <summary>A step did not converge however it was tried, down to 1/1024 of its size, and the push stopped there.</summary>
        public bool Stopped { get; internal set; }

        /// <summary>The model's own loads did not converge, so nothing was pushed.</summary>
        public bool GravityFailed { get; internal set; }

        /// <summary>
        /// A beam's section lost all its stiffness under the model's own loads (see
        /// <see cref="Pushover.FlexibilityLost"/>), so the state the push starts from is not to be trusted.
        /// </summary>
        public bool GravitySuspect { get; internal set; }

        /// <summary>
        /// The push went on past a step where a beam's section had lost all its stiffness, and the curve
        /// is cut before it: what came after rests on section states that are not the materials'.
        /// </summary>
        public bool Cut { get; internal set; }

        /// <summary>How many times OpenSees said a beam's section had no stiffness to invert, failed attempts included.</summary>
        public long FlexibilityLostMessages { get; internal set; }

        /// <summary>What OpenSees printed, with a line repeated written once and its count.</summary>
        public string Log { get; internal set; } = "";

        /// <summary>The largest base shear along the curve, by size, with its sign; NaN on an empty curve.</summary>
        public double PeakBaseShear => BaseShear.Count == 0 ? double.NaN : BaseShear.OrderByDescending(Math.Abs).First();

        /// <summary>The displacement at <see cref="PeakBaseShear"/>; NaN on an empty curve.</summary>
        public double PeakDisplacement => BaseShear.Count == 0 ? double.NaN : Displacement[BaseShear.IndexOf(PeakBaseShear)];

        /// <summary>How far the push got, m: the last point's displacement.</summary>
        public double LastDisplacement => Displacement.Count == 0 ? 0.0 : Displacement[Displacement.Count - 1];

        public override string ToString()
        {
            if (GravityFailed)
                return "Pushover: the model's own loads did not converge, so nothing was pushed.";
            var text = $"Pushover: {Displacement.Count} points to {LastDisplacement:0.####} m of {Target:0.####} m";
            if (!double.IsNaN(PeakBaseShear))
                text += $"; peak base shear {PeakBaseShear:0.##} kN at {PeakDisplacement:0.####} m";
            if (Cut)
                text += "; cut where a section lost all its stiffness";
            else if (!ReachedTarget)
                text += "; stopped short of the target";
            return text;
        }
    }

    /// <summary>
    /// A static pushover: the model's own loads applied in steps and held, then a lateral load pattern
    /// scaled up under displacement control until one node has moved a set distance along one direction.
    ///
    /// Displacement control rather than load control, so the push goes on past the peak: a load
    /// factor that can no longer grow - a mechanism, a frame softening under P-Delta - is a point of
    /// the curve, not a failure to converge. A step that does not converge with Newton is tried with
    /// ModifiedNewton on the initial stiffness, KrylovNewton, NewtonLineSearch and Newton on the
    /// initial stiffness; then at half the step, down to 1/1024 of it; and only then is the push
    /// stopped, with the curve as far as it got. Each converged step is a recorded step and a point of
    /// the curve, so a point can be looked at in Model View or read with the result components.
    ///
    /// BandGeneral solves: a tangent that softens past the peak is no longer positive definite, which
    /// BandSPD and Mumps on a symmetric positive definite matrix cannot factor.
    ///
    /// A force-based beam inverts its sections' stiffness. One whose fibres have all yielded with
    /// nothing to harden - Steel01 with b = 0 on a section of a few fibres - or broken has none to
    /// invert, and OpenSees says so and goes on: the step converges, on section states that are not
    /// the materials'. The push stops at the first such step, and the curve is cut before it; see
    /// <see cref="AnalysisTcl"/> and <see cref="Read"/>. With b = 0 it can go wrong a few steps before
    /// OpenSees says anything, which is why Pushover Analysis warns of it (<see cref="WithoutHardening"/>).
    /// </summary>
    public static class Pushover
    {
        public const string GravityFailedMarker = "ALPACA_PUSHOVER_GRAVITY_FAILED";
        public const string StoppedMarker = "ALPACA_PUSHOVER_STOPPED";
        public const string ReachedMarker = "ALPACA_PUSHOVER_REACHED";

        /// <summary>Printed before each attempt at a step, and after each step that converged.</summary>
        public const string TryMarker = "ALPACA_PUSHOVER_TRY";
        public const string PointMarker = "ALPACA_PUSHOVER_POINT";

        /// <summary>
        /// What a force-based beam prints when a section has no stiffness left to invert: a fibre
        /// section whose every fibre has yielded with nothing to harden, or broken. OpenSees goes on
        /// with what it has, and the step can converge on section states that are not the materials'.
        /// </summary>
        public const string FlexibilityLost = "could not invert flexibility";

        /// <summary>How small a step may get, as a share of the set one, before the push stops.</summary>
        public const int Halvings = 10;

        /// <summary>The convergence tolerance: on the norm of a displacement increment, m.</summary>
        public const double Tolerance = 1e-8;

        /// <summary>Printed when the push stops at a step that converged on a beam section with no stiffness left.</summary>
        public const string LostMarker = "ALPACA_PUSHOVER_LOST";

        /// <summary>
        /// The analysis that follows a model's deck: its load patterns in <paramref name="gravitySteps"/>
        /// steps, held, then <paramref name="loads"/> scaled until node <paramref name="controlNode"/>
        /// has moved <paramref name="target"/> along <paramref name="dof"/>, in about
        /// <paramref name="steps"/> steps. The curve goes to <paramref name="curvePath"/>, a line per
        /// recorded step from the end of the model's loads on: displacement, base shear, load factor.
        ///
        /// The deck has to start with <see cref="LogTcl"/> on <paramref name="logPath"/>: it reads back
        /// what each attempt at a step printed there. A step that converged while a beam said its
        /// section had no stiffness left to invert ends the push - the step rests on section states
        /// that are not the materials', and so does every step after it - and an attempt that failed
        /// that way goes straight to half the step, as no other algorithm gets on with such a section.
        /// </summary>
        /// <param name="dof">1, 2 or 3: X, Y or Z.</param>
        /// <param name="target">Signed: a negative target pushes the other way.</param>
        /// <param name="patternTag">A tag no pattern and no time series of the deck has; see <see cref="FreeTag"/>.</param>
        public static string AnalysisTcl(int controlNode, int dof, double target, int steps, int gravitySteps,
                                         IReadOnlyCollection<PushoverLoad> loads, int patternTag, string curvePath, string logPath)
        {
            if (dof < 1 || dof > 3)
                throw new ArgumentOutOfRangeException(nameof(dof), "A pushover goes along X, Y or Z: dof 1, 2 or 3.");
            if (!(Math.Abs(target) > 0.0) || double.IsNaN(target) || double.IsInfinity(target))
                throw new ArgumentOutOfRangeException(nameof(target), "The target displacement has to be a distance, not zero.");
            if (steps < 1)
                throw new ArgumentOutOfRangeException(nameof(steps), "A pushover needs at least one step.");
            if (gravitySteps < 1)
                throw new ArgumentOutOfRangeException(nameof(gravitySteps), "The model's own loads need at least one step.");
            if (loads == null || loads.Count == 0)
                throw new ArgumentException("A pushover needs a lateral load to scale.", nameof(loads));
            if (string.IsNullOrEmpty(logPath))
                throw new ArgumentException("The deck reads back what OpenSees printed, so it needs the log file LogTcl set.", nameof(logPath));

            var lateral = new StringBuilder();
            foreach (var load in loads)
                lateral.Append("    " + load.WriteTcl());

            var tcl = new StringBuilder(@"# Pushover (Alpaca4d): the model's loads in @GRAVITY@ steps, held; then the lateral loads, scaled
# until node @NODE@ has moved @TARGET@ along dof @DOF@, in steps of @STEP@.
constraints Transformation
numberer RCM
system BandGeneral
test NormDispIncr @TOLERANCE@ 50 0
algorithm Newton
integrator LoadControl @GRAVITYFACTOR@
analysis Static
set alpacaOk [analyze @GRAVITY@]
if {$alpacaOk != 0} {
    puts ""@GRAVITYFAILED@""
}
if {$alpacaOk == 0} {
loadConst -time 0.0
timeSeries Linear @PATTERN@
pattern Plain @PATTERN@ @PATTERN@ {
@LATERAL@}
set alpacaNode @NODE@
set alpacaDof @DOF@
set alpacaPattern @PATTERN@
set alpacaTarget @TARGET@
set alpacaDu @STEP@
set alpacaStep $alpacaDu
set alpacaMinStep [expr {abs($alpacaDu) / @MINSTEP@}]
set alpacaStart [nodeDisp $alpacaNode $alpacaDof]
set alpacaDone 0.0
set alpacaLog @LOGPATH@
set alpacaCurve [open @CURVEPATH@ w]

# The reactions, all of them: a free node's is what is left unbalanced, next to nothing once a step
# has converged. Turned round, so that the base shear resists the push.
proc alpacaBaseShear {dof} {
    reactions
    set total 0.0
    foreach node [getNodeTags] {
        set total [expr {$total - [nodeReaction $node $dof]}]
    }
    return $total
}

proc alpacaPoint {} {
    global alpacaCurve alpacaNode alpacaDof alpacaStart alpacaPattern
    set d [expr {[nodeDisp $alpacaNode $alpacaDof] - $alpacaStart}]
    puts $alpacaCurve ""$d [alpacaBaseShear $alpacaDof] [getLoadFactor $alpacaPattern]""
    flush $alpacaCurve
}

# Whether OpenSees said a beam's section had no stiffness left, in what it logged from byte $from on.
proc alpacaLost {from} {
    global alpacaLog
    if {[catch {open $alpacaLog r} file]} { return 0 }
    seek $file $from
    set text [read $file 1048576]
    close $file
    return [expr {[string first {@FLEXIBILITYLOST@} $text] >= 0}]
}

# One attempt at the step the integrator holds: whether it converged, and whether a section was lost on the way.
proc alpacaAttempt {algorithm} {
    global alpacaLog
    eval algorithm $algorithm
    puts ""@TRY@""
    set mark [file size $alpacaLog]
    set ok [analyze 1]
    return [list $ok [alpacaLost $mark]]
}

alpacaPoint
set alpacaEnd @REACHED@
while {abs($alpacaTarget - $alpacaDone) > 1e-9 * abs($alpacaTarget)} {
    set alpacaIncr $alpacaStep
    if {abs($alpacaIncr) > abs($alpacaTarget - $alpacaDone)} { set alpacaIncr [expr {$alpacaTarget - $alpacaDone}] }
    integrator DisplacementControl $alpacaNode $alpacaDof $alpacaIncr
    lassign [alpacaAttempt Newton] alpacaOk alpacaLostNow
    if {$alpacaOk != 0 && !$alpacaLostNow} {
        test NormDispIncr @TOLERANCE@ 200 0
        foreach alpacaAlgorithm {{ModifiedNewton -initial} KrylovNewton NewtonLineSearch {Newton -initial}} {
            lassign [alpacaAttempt $alpacaAlgorithm] alpacaOk alpacaLostNow
            if {$alpacaOk == 0 || $alpacaLostNow} break
        }
        test NormDispIncr @TOLERANCE@ 50 0
    }
    algorithm Newton
    if {$alpacaOk == 0} {
        set alpacaDone [expr {$alpacaDone + $alpacaIncr}]
        alpacaPoint
        puts ""@POINT@""
        if {$alpacaLostNow} {
            set alpacaEnd @LOST@
            break
        }
        if {abs($alpacaStep) < abs($alpacaDu)} {
            set alpacaStep [expr {2.0 * $alpacaStep}]
            if {abs($alpacaStep) > abs($alpacaDu)} { set alpacaStep $alpacaDu }
        }
    } else {
        set alpacaStep [expr {$alpacaStep / 2.0}]
        if {abs($alpacaStep) < $alpacaMinStep} {
            set alpacaEnd @STOPPED@
            break
        }
    }
}
close $alpacaCurve
puts ""$alpacaEnd $alpacaDone""
}
wipe
");
            var values = new Dictionary<string, string>
            {
                ["GRAVITYFAILED"] = GravityFailedMarker,
                ["GRAVITYFACTOR"] = TclNumber.Write(1.0 / gravitySteps),
                ["GRAVITY"] = gravitySteps.ToString(CultureInfo.InvariantCulture),
                ["TOLERANCE"] = TclNumber.Write(Tolerance),
                ["LATERAL"] = lateral.ToString(),
                ["PATTERN"] = patternTag.ToString(CultureInfo.InvariantCulture),
                ["NODE"] = controlNode.ToString(CultureInfo.InvariantCulture),
                ["DOF"] = dof.ToString(CultureInfo.InvariantCulture),
                ["TARGET"] = TclNumber.Write(target),
                ["MINSTEP"] = (1 << Halvings).ToString(CultureInfo.InvariantCulture) + ".0",
                ["STEP"] = TclNumber.Write(target / steps),
                ["FLEXIBILITYLOST"] = FlexibilityLost,
                ["REACHED"] = ReachedMarker,
                ["STOPPED"] = StoppedMarker,
                ["LOST"] = LostMarker,
                ["TRY"] = TryMarker,
                ["POINT"] = PointMarker,
                // Last, being the only values that are not Alpaca4d's own words.
                ["LOGPATH"] = TclPath.Write(logPath),
                ["CURVEPATH"] = TclPath.Write(curvePath),
            };
            foreach (var value in values)
                tcl.Replace("@" + value.Key + "@", value.Value);
            return tcl.ToString();
        }

        /// <summary>
        /// The fibre materials of <paramref name="materials"/> with nothing to harden once they have
        /// yielded: Steel01 with b = 0. A section of them whose fibres yield together - a flange, a
        /// ring of bars - has no stiffness left the moment it does, and a force-based beam goes wrong on
        /// it before OpenSees says anything (see Alpaca4d.Test/Pushover). A b of 0.001 to 0.01 is the
        /// usual cure.
        /// </summary>
        public static List<Alpaca4d.Generic.IMaterial> WithoutHardening(IEnumerable<Alpaca4d.Generic.IMaterial> materials)
        {
            return (materials ?? Enumerable.Empty<Alpaca4d.Generic.IMaterial>())
                .OfType<Alpaca4d.Material.Steel01>()
                .Where(steel => !(steel.b > 0.0))
                .Cast<Alpaca4d.Generic.IMaterial>()
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// <paramref name="tcl"/> with every Linear beam transformation made PDelta, so that the loads
        /// the columns carry push them further as the frame sways. Corotational ones are left as they
        /// are: they hold large displacements, P-Delta among them. <paramref name="changed"/> is how
        /// many were made PDelta.
        /// </summary>
        public static List<string> WithPDelta(IEnumerable<string> tcl, out int changed)
        {
            var linear = new Regex(@"^(\s*geomTransf\s+)Linear(\s)", RegexOptions.Multiline);
            int count = 0;
            var result = (tcl ?? Enumerable.Empty<string>())
                .Select(chunk => chunk == null ? null : linear.Replace(chunk, match =>
                {
                    count++;
                    return match.Groups[1].Value + "PDelta" + match.Groups[2].Value;
                }))
                .ToList();
            changed = count;
            return result;
        }

        /// <summary>
        /// A tag past every pattern and time series <paramref name="tcl"/> declares: the lateral
        /// pattern's, and its time series'. Read off the deck rather than handed out by a counter, as
        /// a deck read back from a file names its own.
        /// </summary>
        public static int FreeTag(IEnumerable<string> tcl)
        {
            var declaration = new Regex(@"^\s*(?:pattern\s+\w+|timeSeries\s+\w+)\s+(\d+)", RegexOptions.Multiline);
            int highest = 0;
            foreach (var chunk in tcl ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(chunk))
                    continue;
                foreach (Match match in declaration.Matches(chunk))
                    if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag))
                        highest = Math.Max(highest, tag);
            }
            return highest + 1;
        }

        /// <summary>
        /// The line that starts a pushover deck, before the model: everything OpenSees prints goes to
        /// <paramref name="logPath"/> and not to the screen. A beam whose section has lost its stiffness
        /// says so on every iteration of every element of every attempt at a step, and in a push that
        /// has gone that way that came to 1.7 million lines on one step, which no log held in memory
        /// should be asked to take. <see cref="Read"/> goes through the file a line at a time.
        /// </summary>
        public static string LogTcl(string logPath) => $"logFile {TclPath.Write(logPath)} -noEcho\n";

        /// <summary>How many lines of OpenSees' own the log handed back keeps, repeats run together.</summary>
        public const int LogLines = 400;

        /// <summary>
        /// The curve <see cref="AnalysisTcl"/> wrote, with each point's recorded step: the model's
        /// loads took steps 0 to <paramref name="gravitySteps"/> - 1, and the first point is the last
        /// of those. <paramref name="logPath"/> is the file <see cref="LogTcl"/> sent OpenSees'
        /// messages to; <paramref name="printed"/> what it printed besides, an error that stopped the
        /// deck among it.
        /// </summary>
        public static PushoverCurve Read(string curvePath, string logPath, string printed, int gravitySteps, double target)
        {
            var curve = new PushoverCurve { Target = target };

            // Which points rest on a step that converged with a section it could not invert. The
            // markers and the beams' messages both go through opserr, so they come in the order they
            // were printed: a message between the last attempt of a step and the point it made is
            // that step's, and a message before any attempt is the model's own loads'. Messages of
            // an attempt that failed do not count - the step was taken back.
            int points = 0, firstBad = -1;
            bool attemptLost = false, anyTry = false;
            var log = new LogDigest();
            if (!string.IsNullOrWhiteSpace(printed))
                foreach (var line in printed.Split('\n'))
                    log.Add(line.TrimEnd('\r'));

            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                using (var reader = new StreamReader(logPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Contains(TryMarker))
                        {
                            anyTry = true;
                            attemptLost = false;
                            continue;
                        }
                        if (line.Contains(PointMarker))
                        {
                            points++;
                            if (attemptLost && firstBad < 0)
                                firstBad = points;
                            continue;
                        }
                        if (line.Contains(FlexibilityLost))
                        {
                            curve.FlexibilityLostMessages++;
                            if (anyTry)
                                attemptLost = true;
                            else
                                curve.GravitySuspect = true;
                        }
                        if (line.Contains(GravityFailedMarker))
                            curve.GravityFailed = true;
                        if (line.Contains(ReachedMarker))
                            curve.ReachedTarget = true;
                        if (line.Contains(StoppedMarker))
                            curve.Stopped = true;
                        if (line.Contains(LostMarker))
                            curve.Cut = true;
                        log.Add(line);
                    }
                }
            }
            curve.Log = log.Text(logPath);

            if (curve.GravityFailed)
                return curve;

            if (File.Exists(curvePath))
            {
                foreach (var line in File.ReadAllLines(curvePath))
                {
                    var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length < 3)
                        continue;
                    curve.Step.Add(gravitySteps - 1 + curve.Displacement.Count);
                    curve.Displacement.Add(TclNumber.Read(values[0]));
                    curve.BaseShear.Add(TclNumber.Read(values[1]));
                    curve.LoadFactor.Add(TclNumber.Read(values[2]));
                }
            }

            if (firstBad > 0 && firstBad < curve.Displacement.Count)
            {
                int extra = curve.Displacement.Count - firstBad;
                curve.Displacement.RemoveRange(firstBad, extra);
                curve.BaseShear.RemoveRange(firstBad, extra);
                curve.LoadFactor.RemoveRange(firstBad, extra);
                curve.Step.RemoveRange(firstBad, extra);
                curve.Cut = true;
                curve.ReachedTarget = false;
            }
            return curve;
        }

        /// <summary>
        /// A log cut down to what can be read: a line repeated is written once, with how many times it
        /// came; past <see cref="LogLines"/> lines only the count of the rest.
        /// </summary>
        private sealed class LogDigest
        {
            private readonly StringBuilder _text = new StringBuilder();
            private string _last;
            private long _repeats, _kept, _dropped;

            public void Add(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                if (line == _last)
                {
                    _repeats++;
                    return;
                }
                Flush();
                _last = line;
                if (_kept < LogLines)
                {
                    _text.AppendLine(line);
                    _kept++;
                }
                else
                    _dropped++;
            }

            private void Flush()
            {
                if (_repeats > 0 && _kept <= LogLines && _dropped == 0)
                    _text.AppendLine($"    ... the line above {_repeats} more time{(_repeats == 1 ? "" : "s")}");
                else if (_repeats > 0)
                    _dropped += _repeats;
                _repeats = 0;
            }

            public string Text(string logPath)
            {
                Flush();
                if (_dropped > 0)
                    _text.AppendLine($"... and {_dropped} more lines: the whole log is {logPath}");
                return _text.ToString().TrimEnd();
            }
        }
    }
}
