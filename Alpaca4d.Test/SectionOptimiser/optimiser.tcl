# Two 3 m cantilevers with a load across their tips, solved twice - heavy and light, named on the
# command line - for the section optimiser's checks. Twice, not once with two analyses in it: MPCO
# numbers its steps on from one analysis to the next in the same OpenSees, and the second file would
# start at STEP_1. They are statically determinate, so the forces do not depend on the sections:
# the test reads one file or the other to stand for a design whose forces it decides itself. The
# deck's section is only roughly an IPE 200, and nothing reads its stiffness. Units kN and m.
#
#   heavy.mpco  beam 1: Vy = 60 kN, Mz = 180 kNm at the fixed end; beam 2: Vy = 30 kN, Mz = 90 kNm
#   light.mpco  beam 1: Vy = 20 kN, Mz =  60 kNm;                   beam 2: Vy = 10 kN, Mz = 30 kNm
set name [lindex $argv 0]
array set tip {heavy {60.0 30.0} light {20.0 10.0}}
foreach {p1 p2} $tip($name) {
    wipe
    model BasicBuilder -ndm 3 -ndf 6

    node 1 0.0 0.0 0.0
    node 2 3.0 0.0 0.0
    node 3 0.0 1.0 0.0
    node 4 3.0 1.0 0.0
    fix 1 1 1 1 1 1 1
    fix 3 1 1 1 1 1 1

    # section Elastic tag E A Iz Iy G J alphaY alphaZ
    section Elastic 1 210.0e6 0.00285 1.94e-5 1.42e-6 80.77e6 7.0e-8 0.5 0.5

    geomTransf Linear 1 0 0 1
    geomTransf Linear 2 0 0 1

    element forceBeamColumn 1 1 2 1 NewtonCotes 1 5
    element forceBeamColumn 2 3 4 2 NewtonCotes 1 5

    timeSeries Linear 1
    pattern Plain 1 1 "
        load 2 0.0 -$p1 0.0 0.0 0.0 0.0
        load 4 0.0 -$p2 0.0 0.0 0.0 0.0
    "

    recorder mpco $name.mpco -E section.force

    system BandGeneral
    numberer RCM
    constraints Plain
    test NormDispIncr 1.0e-12 20
    algorithm Newton
    integrator LoadControl 1.0
    analysis Static
    analyze 1
    wipe
}
