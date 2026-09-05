using System;
using System.Collections.Generic;
using Alpaca4d.Generic;
using Alpaca4d.Template;

namespace Alpaca4d
{
    public class Recorder : IRecorder
    {
        public string FileName { get; set; }
        public bool Displacement { get; set; }
        public bool Rotation { get; set; }
        public bool Velocity { get; set; }

        /// <summary>
        /// Rotational velocity, the other half of Velocity. Recorded separately by OpenSees -
        /// "velocity" is the translational dofs only - so a model that asks for one and not the
        /// other gets half a transient answer.
        /// </summary>
        public bool AngularVelocity { get; set; }
        public bool Acceleration { get; set; }

        /// <summary>Rotational acceleration. See <see cref="AngularVelocity"/>.</summary>
        public bool AngularAcceleration { get; set; }
        public bool ReactionForce { get; set; }
        public bool ReactionMoment { get; set; }
        public bool ModesOfVibration { get; set; }
        public bool ModesOfVibrationRotational { get; set; }
        public bool Force { get; set; }
        public bool Stress { get; set; }
        public bool SectionForce { get; set; }
        public bool SectionFiberStress { get; set; }

        public Recorder()
        {
        }

        public Recorder(string filePath, bool displacements = false, bool rotations = true, bool velocity = false, bool accelerations = false, bool reactionForces = false, bool reactionMoments = false, bool modesOfVibrations = false, bool modesOfVibrationsRotational = false, bool forces = false, bool stresses = false, bool sectionForces = false, bool sectionFiberStresses = false, bool angularVelocity = false, bool angularAccelerations = false)
        {
            this.FileName = filePath;
            this.Displacement = displacements;
            this.Rotation = rotations;
            this.Velocity = velocity;
            this.AngularVelocity = angularVelocity;
            this.Acceleration = accelerations;
            this.AngularAcceleration = angularAccelerations;
            this.ReactionForce = reactionForces;
            this.ReactionMoment = reactionMoments;
            this.ModesOfVibration = modesOfVibrations;
            this.ModesOfVibrationRotational = modesOfVibrationsRotational;
            this.Force = forces;
            this.Stress = stresses;
            this.SectionForce = sectionForces;
            this.SectionFiberStress = sectionFiberStresses;
        }

        public string WriteTcl()
        {
            string nodeRespType = "";

            if (this.Displacement)
                nodeRespType += " displacement";

            if (this.Rotation)
                nodeRespType += " rotation";

            if (this.Velocity)
                nodeRespType += " velocity";

            if (this.AngularVelocity)
                nodeRespType += " angularVelocity";

            if (this.Acceleration)
                nodeRespType += " acceleration";

            if (this.AngularAcceleration)
                nodeRespType += " angularAcceleration";

            if (this.ReactionForce)
                nodeRespType += " reactionForce";

            if (this.ReactionMoment)
                nodeRespType += " reactionMoment";

            if (this.ModesOfVibration)
                nodeRespType += " modesOfVibration";

            if (this.ModesOfVibrationRotational)
                nodeRespType += " modesOfVibrationRotational";

            string elementRespType = "";

            if (this.Force)
                elementRespType += " force";

            if (this.Stress)
                elementRespType += " stresses";

            if (this.SectionForce)
                elementRespType += " section.force";

            if (this.SectionFiberStress)
                elementRespType += " section.fiber.stress";

            return $"recorder mpco {this.FileName} -N {nodeRespType} -E {elementRespType}\n";
        }

        public static Recorder MpcoStatic(string filePath,
                                  bool displacements = true,
                                  bool rotations = true,
                                  bool velocity = false,
                                  bool accelerations = false,
                                  bool reactionForces = true,
                                  bool reactionMoments = true,
                                  bool modesOfVibrations = false,
                                  bool modesOfVibrationsRotational = false,
                                  bool forces = false,
                                  bool stresses = true,
                                  bool sectionForces = true,
                                  bool sectionFiberStresses = true,
                                  bool angularVelocity = false,
                                  bool angularAccelerations = false)
        {
            return new Recorder(filePath, displacements, rotations, velocity, accelerations, reactionForces, reactionMoments, modesOfVibrations, modesOfVibrationsRotational, forces, stresses, sectionForces, sectionFiberStresses, angularVelocity, angularAccelerations);
        }

        public static Recorder MpcoTransient(string filePath,
                          bool displacements = true,
                          bool rotations = true,
                          bool velocity = true,
                          bool accelerations = true,
                          bool reactionForces = true,
                          bool reactionMoments = true,
                          bool modesOfVibrations = false,
                          bool modesOfVibrationsRotational = false,
                          bool forces = false,
                          bool stresses = true,
                          bool sectionForces = true,
                          bool sectionFiberStresses = true,
                          bool angularVelocity = true,
                          bool angularAccelerations = true)
        {
            return new Recorder(filePath, displacements, rotations, velocity, accelerations, reactionForces, reactionMoments, modesOfVibrations, modesOfVibrationsRotational, forces, stresses, sectionForces, sectionFiberStresses, angularVelocity, angularAccelerations);
        }

        public static Recorder MpcoEigen(string filePath,
                  bool displacements = false,
                  bool rotations = false,
                  bool velocity = false,
                  bool accelerations = false,
                  bool reactionForces = false,
                  bool reactionMoments = false,
                  bool modesOfVibrations = true,
                  bool modesOfVibrationsRotational = true,
                  bool forces = false,
                  bool stresses = false,
                  bool sectionForces = false,
                  bool sectionFiberStresses = false)
        {
            return new Recorder(filePath, displacements, rotations, velocity, accelerations, reactionForces, reactionMoments, modesOfVibrations, modesOfVibrationsRotational, forces, stresses, sectionForces, sectionFiberStresses);
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
