# Every node held, and a body force of 10 downward handed to the element itself, which forms the
# nodal loads from its own shape functions. The reaction at each node is then the share of the
# weight that node takes, which is what TenNodeTetrahedron.BodyForceShares has to match.
source element.tcl
nDMaterial ElasticIsotropic 1 1000.0 0.25 0.0
element TenNodeTetrahedronStd 1 1 2 3 4 5 6 7 8 9 10 1 0.0 0.0 -10.0
for {set n 1} {$n <= 10} {incr n} {
    fix $n 1 1 1
}
constraints Penalty 1.0e14 1.0e14
numberer Plain
system BandGeneral
test NormDispIncr 1.0e-12 10
algorithm Linear
integrator LoadControl 1.0
analysis Static
analyze 1
reactions
set out [open bodyforce.txt w]
for {set n 1} {$n <= 10} {incr n} {
    puts $out "REACTION $n [nodeReaction $n 1] [nodeReaction $n 2] [nodeReaction $n 3]"
}
close $out
wipe
