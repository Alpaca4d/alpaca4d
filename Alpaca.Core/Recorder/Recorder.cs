using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Generic;
using Alpaca4d.Template;

namespace Alpaca4d
{
    /// <summary>
    /// An MPCO recorder: the chosen results of every node and every element, in one HDF5 file.
    /// It is the file the 08_NumericalOutput components read back, and the one STKO opens.
    ///
    /// Results are held by the names the "recorder mpco" command takes, so what is asked for here
    /// is word for word what goes on the line.
    /// </summary>
    public class Recorder : IRecorder
    {
        public string FileName { get; set; }

        /// <summary>
        /// Nodal results, written after -N. Each one out of <see cref="NodeResultTypes"/> or the
        /// mode shapes: MPCO refuses the whole recorder over a nodal name it does not know.
        /// </summary>
        public List<string> NodeResults { get; set; } = new List<string>();

        /// <summary>
        /// Element results, written after -E, with the words of a response joined by dots -
        /// "section.force" for "section force". An element that does not answer to one is left out
        /// of that result without a word from MPCO, so beams and bricks can share one recorder.
        /// </summary>
        public List<string> ElementResults { get; set; } = new List<string>();

        public Recorder()
        {
        }

        public Recorder(string fileName, IEnumerable<string> nodeResults, IEnumerable<string> elementResults)
        {
            this.FileName = fileName;
            this.NodeResults = nodeResults.ToList();
            this.ElementResults = elementResults.ToList();
        }

        /// <summary>
        /// The nodal results the MPCO Recorder component offers: those a result component reads
        /// back. MPCO records more - Rayleigh and unbalanced forces, reactions with inertia - but
        /// nothing in Alpaca4d would read them, and a box that fills the file with results no
        /// component can show is one to leave out until a reader for them exists.
        ///
        /// Append only. The MPCO Recorder component saves its ticks by position in this list, so
        /// moving or removing a name would tick a different result in every file already saved.
        /// </summary>
        public static readonly IReadOnlyList<string> NodeResultTypes = new[]
        {
            "displacement",         // Nodal Displacements, View Results
            "rotation",             // Nodal Displacements, Principal Stress Lines
            "velocity",             // Nodal Displacements, after a transient analysis
            "angularVelocity",
            "acceleration",
            "angularAcceleration",
            "reactionForce",        // Reaction Forces, View Results
            "reactionMoment",
        };

        /// <summary>
        /// The element results the MPCO Recorder component offers, for the same reason as
        /// <see cref="NodeResultTypes"/>:
        ///
        ///   stresses               Brick Stresses - bricks and tetrahedra
        ///   section.force          Beam Forces and Shell Forces - every forceBeamColumn, hinged
        ///                          or not, and every shell
        ///   section.fiber.stress   Shell Stresses - through the thickness of plate fibre and
        ///                          layered sections
        ///
        /// Append only, for the same reason as <see cref="NodeResultTypes"/>.
        /// </summary>
        public static readonly IReadOnlyList<string> ElementResultTypes = new[]
        {
            "stresses",
            "section.force",
            "section.fiber.stress",
        };

        public static readonly IReadOnlyList<string> StaticNodeResults = new[]
        {
            "displacement", "rotation", "reactionForce", "reactionMoment",
        };

        /// <summary>
        /// The static set and the motion that a static analysis has none of. Rotational velocity
        /// and acceleration are recorded apart from the translational ones - "velocity" is the
        /// translational dofs only - so asking for one and not the other gets half an answer.
        /// </summary>
        public static readonly IReadOnlyList<string> TransientNodeResults = new[]
        {
            "displacement", "rotation", "velocity", "angularVelocity",
            "acceleration", "angularAcceleration", "reactionForce", "reactionMoment",
        };

        /// <summary>What Brick Stresses, Beam Forces, Shell Forces and Shell Stresses read.</summary>
        public static readonly IReadOnlyList<string> DefaultElementResults = new[]
        {
            "stresses", "section.force", "section.fiber.stress",
        };

        public string WriteTcl()
        {
            return $"recorder mpco {this.FileName} -N {string.Join(" ", this.NodeResults)} -E {string.Join(" ", this.ElementResults)}\n";
        }

        public static Recorder MpcoStatic(string filePath)
        {
            return new Recorder(filePath, StaticNodeResults, DefaultElementResults);
        }

        public static Recorder MpcoTransient(string filePath)
        {
            return new Recorder(filePath, TransientNodeResults, DefaultElementResults);
        }

        public static Recorder MpcoEigen(string filePath)
        {
            return new Recorder(filePath, new[] { "modesOfVibration", "modesOfVibrationRotational" }, new string[0]);
        }


        /// <summary>
        /// Every fibre of a section, in one recorder.
        ///
        /// "section fiberData" writes five numbers per fibre per step - y, z, area,
        /// stress, strain - in the order the fibres were added to the section, which is
        /// the order <see cref="Alpaca4d.Section.FiberSection.WriteTcl(int?)"/> writes
        /// them and therefore the order <see cref="Alpaca4d.Section.FiberSection.Fibers"/>
        /// returns them.
        ///
        /// This used to be one "section fiber $y $z" recorder per fibre. Every recorder
        /// holds its file open, and the C runtime OpenSees is built against will not have
        /// more than 512 files open at once - so a section of 2052 fibres got 507 result
        /// files and 1545 that were never created, with no error from the solver, which
        /// exits 0 regardless. The missing ones came back as empty fibre results.
        /// </summary>
        public static string FiberData()
        {
            return $"recorder Element -file {MomentCurvature.FiberDataFilePath} -ele 1 section fiberData\n";
        }

    }
}
