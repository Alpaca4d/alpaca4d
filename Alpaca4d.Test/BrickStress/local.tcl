# The same solid solved twice: as drawn, and turned 45 degrees about Z with its loads turned with
# it. Read in the global axes the two disagree, and the second is the first rotated. Read in the
# element's own axes - the frame Utils.SolidAxes takes off the node numbering - they have to agree,
# because turning a problem round does not change what the material is doing inside it.
#
# The brick is deliberately distorted: no face parallel to another, no edge axis aligned, and an
# asymmetric load set, so the answer is a general stress state rather than a special one.
set c 0.70710678118654752440
set s 0.70710678118654752440

set brickNodes {
  0.00  0.00  0.00
  1.20  0.10 -0.10
  0.90  1.10  0.05
 -0.10  0.95  0.00
  0.15 -0.05  1.10
  1.05  0.20  0.90
  1.15  1.25  1.20
  0.05  1.10  0.95
}
set brickLoads {
  5  1.0  2.0  3.0
  6  0.5 -1.0  2.0
  7 -1.0  0.5  1.5
  8  2.0  1.0 -0.5
}

set tetNodes {
  0.00  0.00  0.00
  1.10  0.15 -0.05
  0.20  0.95  0.10
  0.05  0.10  1.20
}
set tetLoads { 2  1.0  2.0  3.0 }

proc turnNodes {lst} {
  global c s
  set out {}
  foreach {x y z} $lst { lappend out [expr {$c*$x - $s*$y}] [expr {$s*$x + $c*$y}] $z }
  return $out
}

proc turnLoads {lst} {
  global c s
  set out {}
  foreach {t x y z} $lst { lappend out $t [expr {$c*$x - $s*$y}] [expr {$s*$x + $c*$y}] $z }
  return $out
}

proc run {out label kind nodes loads held} {
  wipe
  model basic -ndm 3 -ndf 3
  set i 1
  foreach {x y z} $nodes { node $i $x $y $z; incr i }
  nDMaterial ElasticIsotropic 1 1000.0 0.25 0.0
  if {$kind eq "brick"} {
    element SSPbrick 1 1 2 3 4 5 6 7 8 1
  } else {
    element FourNodeTetrahedron 1 1 2 3 4 1
  }
  foreach t $held { fix $t 1 1 1 }
  pattern Plain 1 Linear { foreach {t x y z} $loads { load $t $x $y $z } }
  constraints Transformation
  numberer RCM
  system FullGeneral
  test NormDispIncr 1e-12 100
  algorithm Newton
  integrator LoadControl 1
  analysis Static
  analyze 1
  puts $out "NODES $label [join $nodes { }]"
  puts $out "STRESS $label [eleResponse 1 stresses]"
  wipe
}

set out [open local.txt w]
run $out BRICK_PLAIN  brick $brickNodes                  $brickLoads                  {1 2 3 4}
run $out BRICK_TURNED brick [turnNodes $brickNodes]      [turnLoads $brickLoads]      {1 2 3 4}
run $out TET_PLAIN    tet   $tetNodes                    $tetLoads                    {1 3 4}
run $out TET_TURNED   tet   [turnNodes $tetNodes]        [turnLoads $tetLoads]        {1 3 4}
close $out
