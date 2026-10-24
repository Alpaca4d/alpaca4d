using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Generic;

namespace Alpaca4d
{
    public partial class Settings : EntityBase
    {
        public Constraint Constraint { get; set; }
        public Numberer Numberer { get; set; }
        public SystemEquation SystemEquation { get; set; }
        public Test Test { get; set; }
        public Algorithm Algorithm { get; set; }
        public Integrator Integrator { get; set; }
        public Analysis Analysis { get; set; }
        public AnalysisStep AnalysisStep { get; set; }
        public Damping Damping { get; set; }

        /// <summary>
        /// True when nobody chose <see cref="Test"/>: Analysis Settings filled it in. Run Analysis may then
        /// pick a safer one for the model it solves - see <see cref="WithTestFor"/>.
        /// </summary>
        public bool TestIsDefault { get; set; }

        /// <summary>
        /// Convergence tests that look only at how far an iteration moved, or how much energy it
        /// moved, and never at the force left unbalanced.
        /// </summary>
        private static readonly Test.TestType[] Blind =
        {
            Test.TestType.EnergyIncr, Test.TestType.NormDispIncr, Test.TestType.RelativeEnergyIncr,
            Test.TestType.RelativeNormDispIncr, Test.TestType.RelativeTotalNormDispIncr,
        };

        /// <summary>True when <see cref="Test"/> can pass while a force is still left unbalanced.</summary>
        public bool TestIgnoresUnbalance => this.Test != null && Blind.Contains(this.Test.Type);

        /// <summary>
        /// These settings for a model with one-way bars or supports: the same, except that a default
        /// test that ignores the unbalanced force is swapped for one that does not. A rigid one-way
        /// spring starts stiff, so the first iteration barely moves the node; an energy or displacement
        /// test passes it while the spring is still pulling, and the analysis reports a support that
        /// never let go. Settings chosen by hand are left as they are; null when nothing changes.
        /// </summary>
        public Settings WithTestFor(Model model)
        {
            if (!this.TestIsDefault || !this.TestIgnoresUnbalance || model == null || !model.HasOneWay)
                return null;

            return new Settings(this.Constraint, this.Numberer, this.SystemEquation,
                                Test.NormDispAndUnbalance(1e-8, 1e-8, Math.Max(this.Test.Iter, 25)),
                                this.Algorithm, this.Integrator, this.Analysis, this.AnalysisStep, this.Damping);
        }

        public Settings()
        {

        }

        public Settings(Constraint constraint, Numberer numberer, SystemEquation systemEquation, Test test, Algorithm algorithm, Integrator integrator, Analysis analysis, AnalysisStep analysisSteps, Damping damping = null)
        {
            this.Constraint = constraint;
            this.Numberer = numberer;
            this.SystemEquation = systemEquation;
            this.Test = test;
            this.Algorithm = algorithm;
            this.Integrator = integrator;
            this.Analysis = analysis;
            this.AnalysisStep = analysisSteps;
            this.Damping = damping;
        }

        public override string WriteTcl()
        {
            string tclText = "";
            if(Damping != null)
                tclText += this.Damping.WriteTcl();
            tclText += this.Constraint.WriteTcl();
            tclText += this.Numberer.WriteTcl();
            tclText += this.SystemEquation.WriteTcl();
            tclText += this.Test.WriteTcl();
            tclText += this.Algorithm.WriteTcl();
            tclText += this.Integrator.WriteTcl();
            tclText += this.Analysis.WriteTcl();
            tclText += this.AnalysisStep.WriteTcl();
            return tclText;
        }
    }
    public class Constraint
    {
        public ConstraintType Type { get; set; } = ConstraintType.Transformation;
        public enum ConstraintType
        {
            Plain,
            Transformation
        }
        public Constraint(string type)
        {
            this.Type = (ConstraintType) Enum.Parse(typeof(ConstraintType), type, true);
        }
        public string WriteTcl()
        {
            return $"constraints {Type}\n";
        }
    }

    public class Numberer
    {
        public NumbererType Type { get; set; }
        public enum NumbererType
        {
            RCM,
            AMD,
            Plain,
            ParallelPlain,
            ParallelRCM
        }
        public Numberer(string type)
        {
            this.Type = (NumbererType) Enum.Parse(typeof(NumbererType), type, true);
        }

        public string WriteTcl()
        {
            return $"numberer {Type}\n";
        }
    }

    public class SystemEquation
    {
        public SystemType Type { get; set; }
        public enum SystemType
        {
            BandGen,
            BandSPD,
            ProfileSPD,
            SuperLU,
            UmfPack,
            SparseSYM,
            SparseSPD,
            SparseGeneral,
            FullGeneral,
        }

        public SystemEquation(string type)
        {
            this.Type = (SystemType) Enum.Parse(typeof(SystemType), type, true);
        }

        public string WriteTcl()
        {
            return $"system {Type}\n";
        }

    }

    public class Test
    {
        public TestType Type { get; set; }
        public double Tol { get; set; }
        public double? TolR { get; set; }
        public int Iter { get; set; }
        public NormType Norm { get; set; } = NormType.TwoNorm;
        public FlagType Flag { get; set; } = FlagType.Nothing;
        public int? MaxIncr { get; set; }

        public enum TestType
        {
            NormUnbalance,
            NormDispIncr,
            EnergyIncr,
            NormDispAndUnbalance,
            NormDispOrUnbalance,
            RelativeNormUnbalance,
            RelativeNormDispIncr,
            RelativeTotalNormDispIncr,
            RelativeEnergyIncr,
            FixedNumIter,
        }
        public enum NormType
        {
            MaxNorm,
            OneNorm,
            TwoNorm
        }
        public enum FlagType
        {
            Nothing = 0,
            EachTime = 1,
            Successful = 2,
            EachStep = 4,
            ErrorMessage = 5
        }

        public Test()
        {
        }
        public Test(TestType testType , double tol = 1e-8, int iter = 10, FlagType flag = FlagType.Nothing, NormType norm = NormType.TwoNorm, int? maxIncr = null)
        {
            this.Type = testType;
            this.Tol = tol;
            this.Iter = iter;
            this.Flag = flag;
            this.Norm = norm;
            this.MaxIncr = maxIncr;
        }
        public static Test EnergyIncr(double tol = 1e-8, int iter = 10, FlagType flag = FlagType.Nothing, NormType norm = NormType.TwoNorm, int? maxIncr = null)
        {
            var test = new Test();
            test.Type = TestType.EnergyIncr;
            test.Tol = tol;
            test.Iter = iter;
            test.Flag = flag;
            test.Norm = norm;
            test.MaxIncr = maxIncr;
            return test;
        }

        public static Test NormUnbalance(double tol = 1e-8, int iter = 10, FlagType flag = FlagType.Nothing, NormType norm = NormType.TwoNorm, int? maxIncr = null)
        {
            var test = new Test();
            test.Type = TestType.NormUnbalance;
            test.Tol = tol;
            test.Iter = iter;
            test.Flag = flag;
            test.Norm = norm;
            test.MaxIncr = maxIncr;
            return test;
        }
        public static Test NormDispAndUnbalance(double tolIncr = 1e-8, double tolR = 1e-8, int iter = 10, FlagType flag = FlagType.Nothing, NormType norm = NormType.TwoNorm, int? maxIncr = null)
        {
            var test = new Test();
            test.Type = TestType.NormDispAndUnbalance;
            test.Tol = tolIncr;
            test.TolR = tolR;
            test.Iter = iter;
            test.Flag = flag;
            test.Norm = norm;
            test.MaxIncr = maxIncr;
            return test;
        }
        public static Test NormDispOrUnbalance(double tolIncr = 1e-8, double tolR = 1e-8, int iter = 10, FlagType flag = FlagType.Nothing, NormType norm = NormType.TwoNorm, int? maxIncr = null)
        {
            var test = new Test();
            test.Type = TestType.NormDispOrUnbalance;
            test.Tol = tolIncr;
            test.TolR = tolR;
            test.Iter = iter;
            test.Flag = flag;
            test.Norm = norm;
            test.MaxIncr = maxIncr;
            return test;
        }


        /// <summary>
        /// Every test used to be written as
        ///     test &lt;Type&gt; &lt;Tol&gt; [&lt;TolR&gt;] &lt;Iter&gt; &lt;Flag&gt; &lt;Norm&gt; &lt;MaxIncr&gt;
        /// but no OpenSees test takes that. Each one has its own argument list, and a value
        /// past the end of it is not ignored - it lands on whatever the parser reads next,
        /// silently. Three ways that went wrong:
        ///
        /// Most tests take three ints (iter, flag, norm) and then read one more double as
        /// maxTol, a divergence guard whose real default is 1.7e307. A trailing MaxIncr of 2
        /// set maxTol to 2, and the test fails the step as soon as the norm passes it - so an
        /// analysis that needed more than one iteration reported "failed to converge" for no
        /// reason. Measured against the bundled solver, the cut-off sits exactly at MaxIncr:
        /// on a model whose first-iteration norm is 128.7, a trailing 128 fails and 129
        /// converges. That holds for NormUnbalance too, whatever a newer OpenSees source may
        /// say about a maxIncr slot, so MaxIncr is written for no type at all.
        ///
        /// FixedNumIter reads only ints. Leading with a tolerance made OpenSees stop with
        /// "no numIter specified in test command", so the test never existed at all.
        ///
        /// NormDispAndUnbalance reads its trailing pair back over iter and flag (into
        /// idata[0] rather than idata[2], unlike NormDispOrUnbalance beside it), so writing
        /// norm quietly replaced the iteration limit with the norm type.
        ///
        /// So write what each type actually accepts, and nothing more.
        /// </summary>
        public string WriteTcl()
        {
            switch (this.Type)
            {
                // Ints only - no tolerance.
                case TestType.FixedNumIter:
                    return $"test {Type} {Iter} {(int)Flag} {(int)Norm}\n";

                // Two tolerances. TolR is nullable on this class, and a null one would
                // interpolate to nothing and shift every argument after it along by one, so
                // fall back to Tol rather than emit a line that parses as something else.
                case TestType.NormDispAndUnbalance:
                    return $"test {Type} {Tol} {TolR ?? Tol} {Iter} {(int)Flag}\n";

                case TestType.NormDispOrUnbalance:
                    return $"test {Type} {Tol} {TolR ?? Tol} {Iter} {(int)Flag} {(int)Norm}\n";

                // Everything else: tolerance and three ints, full stop.
                default:
                    return $"test {Type} {Tol} {Iter} {(int)Flag} {(int)Norm}\n";
            }
        }

    }

    public class Algorithm
    {
        public AlgorithmType Type { get; set; }
        public enum AlgorithmType
        {
            Linear,
            Newton,
            NewtonLineSearch,
            ModifiedNewton,
            KrylovNewton,
            SecantNewton,
            BFGS,
            Broyden
        }

        public Algorithm(string type)
        {
            this.Type = (AlgorithmType) Enum.Parse(typeof(AlgorithmType), type, true);
        }
        public string WriteTcl()
        {
            return $"algorithm {Type}\n";
        }

    }

    public class Integrator
    {
        public IntegratorType Type { get; set; }
        public double? Lambda { get; set; }
        public int? NumIter { get; set; }
        public double? MinLambda { get; set; }
        public double? MaxLambda { get; set; }
        public double? Gamma { get; set; }
        public double? Beta { get; set; }

        public enum IntegratorType
        {
            LoadControl,
            Newmark,
            CentralDifference
        }
        public Integrator()
        {

        }

        public Integrator(double lambda, int numIter, double minLambda, double maxLambda)
        {
            this.Lambda = lambda;
            this.NumIter = numIter;
            this.MinLambda = minLambda;
            this.MaxLambda = maxLambda;
        }

        public static Integrator LoadControl(double lambda, int? numIter = null, double? minLambda = null, double? maxLambda = null)
        {
            var integrator = new Integrator();
            integrator.Type = IntegratorType.LoadControl;
            integrator.Lambda = lambda;
            integrator.MaxLambda = maxLambda;
            integrator.NumIter = numIter;
            integrator.MinLambda = minLambda;
            return integrator;
        }
        public static Integrator Newmark(double gamma, double beta)
        {
            var integrator = new Integrator();
            integrator.Type = IntegratorType.Newmark;
            integrator.Gamma = gamma;
            integrator.Beta = beta;
            return integrator;
        }

        public static Integrator CentralDifference()
        {
            var integrator = new Integrator();
            integrator.Type = IntegratorType.CentralDifference;
            return integrator;
        }

        public string WriteTcl()
        {
            if (this.Type == IntegratorType.LoadControl)
                return $"integrator {Type} {Lambda} {NumIter} {MinLambda} {MaxLambda}\n";
            else if (this.Type == IntegratorType.Newmark)
                return $"integrator {Type} {Gamma} {Beta}\n";
            else if (this.Type == IntegratorType.CentralDifference)
                return $"integrator {Type}\n";
            else
                throw new Exception($"Integrator {Type} does not exist");
        }

    }

    public partial class Analysis
    {
        public AnalysisType Type { get; set; }
        public enum AnalysisType
        {
            Static,
            Transient
        }

        public enum Solver
        {
            genBandArpack,
            symmBandLapack,
            fullGenLapack,
		}

        public Analysis()
        {
        }

        public Analysis(string type)
        {
            this.Type = (AnalysisType) Enum.Parse(typeof(AnalysisType), type, true);
        }

        public string WriteTcl()
        {
            return $"analysis {Type}\n";
        }
    }

    public partial class AnalysisStep
    {
        public int NumIncr { get; set; }
        public double? Dt { get; set; }
        public double? DtMin { get; set; }
        public double? DtMax { get; set; }
        public int? Jd { get; set; }

        public AnalysisStep()
        {

        }

        public AnalysisStep(int numIncr, double? dt = null, double? dtMin = null, double? dtMax = null, int? jD = null)
        {
            this.NumIncr = numIncr;
            this.Dt = dt;
            this.DtMin = dtMin;
            this.DtMax = dtMax;
            this.Jd = jD;
        }

        public AnalysisStep(string text)
        {
            string[] values = text.Split(' ');
            try
            {
                this.NumIncr = int.Parse(values[0]);
                this.Dt = double.Parse(values[1]);
                this.DtMin = double.Parse(values[2]);
                this.DtMax = double.Parse(values[3]);
                this.Jd = int.Parse(values[4]);
            }
            catch
            {

            }
        }

        public AnalysisStep(int numIncr)
        {
            this.NumIncr = numIncr;
        }

        public string WriteTcl()
        {
            // Capture the analyze() return code explicitly: OpenSees' "analyze" command
            // always returns TCL_OK (no process/exit-code signal on non-convergence);
            // failure is only reported via a negative return value from analyze() itself.
            return $"set alpacaAnalyzeOk [analyze {NumIncr} {Dt} {DtMin} {DtMax} {Jd}]\n"
                 + "puts \"ALPACA_ANALYZE_RESULT $alpacaAnalyzeOk\"\n";
        }
    }

    public partial class Damping
    {
        public double AlphaM { get; set; }
        public double BetaKCurr { get; set; }
        public double BetaKInit { get; set; }
        public double BetaKComm { get; set; }

        public Damping(double alphaM, double betaKCurr, double betaKInit, double betaKcomm)
        {
            this.AlphaM = alphaM;
            this.BetaKCurr = betaKCurr;
            this.BetaKInit = betaKInit;
            this.BetaKComm = betaKcomm;
        }

        public string WriteTcl()
        {
            return $"rayleigh {this.AlphaM} {this.BetaKCurr} {this.BetaKInit} {this.BetaKComm}\n";
        }
    }
}