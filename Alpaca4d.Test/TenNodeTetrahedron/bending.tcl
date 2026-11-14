# Pure bending, which a ten node tetrahedron carries exactly: with nu = 0, the displacement
# u = -kappa x z, v = 0, w = kappa x^2 / 2 has eps_xx = -kappa z and no other strain, so
# sigma_xx = -E kappa z, linear, and every other component is zero. It is quadratic, so the
# element holds it exactly, and every node is put on it: the solve has nothing left to decide.
# What comes out is the stress at the four integration points, which is what the reader's
# extrapolation to the nodes has to turn back into -E kappa z at each node.
source element.tcl
nDMaterial ElasticIsotropic 1 1000.0 0.0 0.0
element TenNodeTetrahedronStd 1 1 2 3 4 5 6 7 8 9 10 1
set kappa 0.01
timeSeries Linear 1
pattern Plain 1 1 {
    for {set n 1} {$n <= 10} {incr n} {
        set x [nodeCoord $n 1]
        set z [nodeCoord $n 3]
        sp $n 1 [expr {-$kappa * $x * $z}]
        sp $n 2 0.0
        sp $n 3 [expr {$kappa * $x * $x / 2.0}]
    }
}
recorder mpco bending -N displacement -E stresses
# Every degree of freedom is prescribed, which leaves Transformation an empty system to solve.
constraints Penalty 1.0e14 1.0e14
numberer Plain
system BandGeneral
test NormDispIncr 1.0e-12 10
algorithm Linear
integrator LoadControl 1.0
analysis Static
analyze 1
wipe
