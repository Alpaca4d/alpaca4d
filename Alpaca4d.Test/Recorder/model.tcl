# One element of every kind Alpaca4d writes, each held on its own, so that one MPCO recorder
# can be asked for every result it offers and the file shows which element answered to which.
# The recorders go in between this and analysis.tcl - Recorder.cs writes them.
wipe
model basic -ndm 3 -ndf 6
uniaxialMaterial Elastic 1 2.0e11
uniaxialMaterial Elastic 4 1.0e6
nDMaterial ElasticIsotropic 2 3.0e10 0.2 2500.0
section Fiber 1 -GJ 1.0e6 { patch rect 1 4 4 -0.05 -0.05 0.05 0.05 }
section PlateFiber 2 2 0.2
geomTransf Linear 1 0 0 1
beamIntegration Lobatto 1 1 5
# beam
node 1 0 0 0
node 2 1 0 0
fix 1 1 1 1 1 1 1
element forceBeamColumn 1 1 2 1 1
# ASDShellQ4
node 3 2 0 0
node 4 3 0 0
node 5 3 1 0
node 6 2 1 0
fix 3 1 1 1 1 1 1
fix 6 1 1 1 1 1 1
element ASDShellQ4 2 3 4 5 6 2
# ShellDKGT
node 7 4 0 0
node 8 5 0 0
node 9 4 1 0
fix 7 1 1 1 1 1 1
fix 9 1 1 1 1 1 1
element ShellDKGT 3 7 8 9 2
# ASDShellT3
node 10 6 0 0
node 11 7 0 0
node 12 6 1 0
fix 10 1 1 1 1 1 1
fix 12 1 1 1 1 1 1
element ASDShellT3 4 10 11 12 2
# twoNodeLink
node 13 8 0 0
node 14 9 0 0
fix 13 1 1 1 1 1 1
element twoNodeLink 5 13 14 -mat 4 4 4 4 4 4 -dir 1 2 3 4 5 6
# forceBeamColumn with hinges, written the way BeamWithHinges writes it
node 15 0 0 2
node 16 1 0 2
fix 15 1 1 1 1 1 1
element forceBeamColumn 8 15 16 1 HingeRadau 1 0.1 1 0.1 1
model basic -ndm 3 -ndf 3
nDMaterial ElasticIsotropic 5 3.0e7 0.2 2500.0
# SSPbrick
node 101 10 0 0
node 102 11 0 0
node 103 11 1 0
node 104 10 1 0
node 105 10 0 1
node 106 11 0 1
node 107 11 1 1
node 108 10 1 1
fix 101 1 1 1
fix 102 1 1 1
fix 103 1 1 1
fix 104 1 1 1
element SSPbrick 6 101 102 103 104 105 106 107 108 5 0 0 0
# FourNodeTetrahedron
node 109 12 0 0
node 110 13 0 0
node 111 12 1 0
node 112 12 0 1
fix 109 1 1 1
fix 110 1 1 1
fix 111 1 1 1
element FourNodeTetrahedron 7 109 110 111 112 5 0 0 0
