# Which way a shell's axis 1 actually points, measured rather than assumed.
#
# A uniform strain is imposed on every node, so the stress state is known exactly - f along global
# X and nothing else. Whatever mix of fxx, fyy and fxy the element then reports gives away the
# direction of its own axis 1, and 0.5*atan2(-2 fxy, fxx - fyy) reads it back out.
#
# The two candidates for a quad are side 1-2 and the line joining the midpoints of sides 2-3 and
# 4-1. They agree on a rectangle and differ on anything else, which is what the second case is for.
# eps_xx = a in the GLOBAL frame, everything else zero -> f = E*t*a along global X.
proc probe {out label nodes} {
  set a 0.001
  wipe
  model basic -ndm 3 -ndf 6
  set i 1
  foreach {x y z} $nodes { node $i $x $y $z; incr i }
  nDMaterial ElasticIsotropic 1 1000.0 0.0
  section PlateFiber 1 1 0.1
  element ASDShellQ4 1 1 2 3 4 1
  pattern Plain 1 Linear {
    set i 1
    foreach {x y z} $nodes {
      sp $i 1 [expr {$a*$x}]
      sp $i 2 0.0
      sp $i 3 0.0
      sp $i 4 0.0
      sp $i 5 0.0
      sp $i 6 0.0
      incr i
    }
  }
  constraints Penalty 1.0e14 1.0e14
  numberer RCM
  system FullGeneral
  test NormDispIncr 1e-12 100
  algorithm Newton
  integrator LoadControl 1
  analysis Static
  analyze 1
  set r [eleResponse 1 stresses]
  set fxx [lindex $r 0]; set fyy [lindex $r 1]; set fxy [lindex $r 2]
  set theta [expr {0.5*atan2(-2.0*$fxy, $fxx-$fyy) * 180.0/3.14159265358979}]
  puts $out "$label fxx=[format %9.6f $fxx] fyy=[format %9.6f $fyy] fxy=[format %9.6f $fxy]  axis1 at [format %7.3f $theta] deg from global X"
  wipe
}
set out [open frame.txt w]
probe $out "RECT" {0 0 0   2 0 0   2 1 0   0 1 0}
probe $out "SKEW" {0 0 0   2 0 0   2.5 1 0   1.0 1.2 0}
close $out
