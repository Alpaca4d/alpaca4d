# Two tetrahedra and two SSP bricks, each under a different tension, with the element tags
# interleaved across the two classes: 10 and 30 are tetrahedra, 20 and 40 are bricks. The
# recorder writes one dataset per class, so the file's row order and a model's element order
# have no reason to agree, and the reader has to go by the ID dataset rather than by position.
wipe
model basic -ndm 3 -ndf 3
node 1 0 0 0; node 2 1 0 0; node 3 1 1 0; node 4 0 1 0
node 5 0 0 1; node 6 1 0 1; node 7 1 1 1; node 8 0 1 1
node 11 3 0 0; node 12 4 0 0; node 13 4 1 0; node 14 3 1 0
node 15 3 0 1; node 16 4 0 1; node 17 4 1 1; node 18 3 1 1
node 21 0 5 0; node 22 1 5 0; node 23 0 6 0; node 24 0 5 1
node 31 3 5 0; node 32 4 5 0; node 33 3 6 0; node 34 3 5 1
nDMaterial ElasticIsotropic 1 1000.0 0.0 0.0
element FourNodeTetrahedron 10 21 22 23 24 1
element SSPbrick 20 1 2 3 4 5 6 7 8 1
element FourNodeTetrahedron 30 31 32 33 34 1
element SSPbrick 40 11 12 13 14 15 16 17 18 1
fix 1 1 1 1; fix 4 1 0 1; fix 5 1 0 0; fix 8 1 0 0
fix 11 1 1 1; fix 14 1 0 1; fix 15 1 0 0; fix 18 1 0 0
fix 21 1 1 1; fix 23 1 1 1; fix 24 1 1 1
fix 31 1 1 1; fix 33 1 1 1; fix 34 1 1 1
pattern Plain 1 Linear {
  load 2 2.5 0 0; load 3 2.5 0 0; load 6 2.5 0 0; load 7 2.5 0 0
  load 12 10.0 0 0; load 13 10.0 0 0; load 16 10.0 0 0; load 17 10.0 0 0
  load 22 100.0 0 0
  load 32 400.0 0 0
}
recorder mpco mixed -N displacement -E stresses
constraints Transformation
numberer RCM
system BandGeneral
test NormDispIncr 1e-8 100
algorithm Newton
integrator LoadControl 1
analysis Static
analyze 1
foreach e {10 20 30 40} { puts "ele $e s11 = [lindex [eleResponse $e stresses] 0]" }
wipe
