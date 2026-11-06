# The patch test: every node of one element put on u = 1e-3 x, v = w = 0. With E = 1000 and
# nu = 0.25 (lambda = mu = 400) that is sigma_xx = 1.2, sigma_yy = sigma_zz = 0.4 and no shear,
# the same at all four integration points. The unit corner tetrahedron, so the reactions can be
# checked by hand: the face x = 0 carries -1.2 over its area of 1/2, less the 0.2 its edge node
# on the slanted face takes back from that face's traction, so -0.4 in all over the nodes at x = 0.
# An OpenSees whose TenNodeTetrahedron integrates over a sixth of the volume (3.8.0) gets the
# right stresses and a sixth of the reactions; one that does not know TenNodeTetrahedronStd at all
# stops at the element line, and its log is what Run Analysis explains.
wipe
model BasicBuilder -ndm 3 -ndf 3
node 1 0.0 0.0 0.0
node 2 1.0 0.0 0.0
node 3 0.0 1.0 0.0
node 4 0.0 0.0 1.0
node 5 0.5 0.0 0.0
node 6 0.5 0.5 0.0
node 7 0.0 0.5 0.0
node 8 0.0 0.0 0.5
node 9 0.5 0.0 0.5
node 10 0.0 0.5 0.5
nDMaterial ElasticIsotropic 1 1000.0 0.25 0.0
element TenNodeTetrahedronStd 1 1 2 3 4 5 6 7 8 9 10 1
timeSeries Linear 1
pattern Plain 1 1 {
    for {set n 1} {$n <= 10} {incr n} {
        sp $n 1 [expr {1.0e-3 * [nodeCoord $n 1]}]
        sp $n 2 0.0
        sp $n 3 0.0
    }
}
recorder mpco patch -N displacement -E stresses
constraints Penalty 1.0e14 1.0e14
numberer Plain
system BandGeneral
test NormDispIncr 1.0e-12 10
algorithm Linear
integrator LoadControl 1.0
analysis Static
analyze 1
reactions
set fx 0.0
for {set n 1} {$n <= 10} {incr n} {
    if {[nodeCoord $n 1] == 0.0} {
        set fx [expr {$fx + [nodeReaction $n 1]}]
    }
}
set out [open patch.txt w]
puts $out "REACTION_X0 $fx"
close $out
wipe
