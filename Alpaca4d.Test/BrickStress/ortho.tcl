# The same cube of an orthotropic material pulled along global X, then along global Y.
# Ex is 1000 and Ey is 4000, so if the material had a frame of its own the two would read alike.
# They do not: nDMaterial ElasticOrthotropic takes no orientation, and neither SSPbrick nor
# FourNodeTetrahedron takes a local axis, so Ex, Ey and Ez are the world X, Y and Z.
set out [open ortho.txt w]

wipe
model basic -ndm 3 -ndf 3
node 1 0 0 0; node 2 1 0 0; node 3 1 1 0; node 4 0 1 0
node 5 0 0 1; node 6 1 0 1; node 7 1 1 1; node 8 0 1 1
nDMaterial ElasticOrthotropic 1  1000.0 4000.0 1000.0  0.0 0.0 0.0  500.0 500.0 500.0
element SSPbrick 1 1 2 3 4 5 6 7 8 1
fix 1 1 1 1; fix 4 1 0 1; fix 5 1 0 0; fix 8 1 0 0
pattern Plain 1 Linear { load 2 2.5 0 0; load 3 2.5 0 0; load 6 2.5 0 0; load 7 2.5 0 0 }
constraints Transformation
numberer RCM
system BandGeneral
test NormDispIncr 1e-8 100
algorithm Newton
integrator LoadControl 1
analysis Static
analyze 1
puts $out "X = [nodeDisp 2 1]"
wipe

model basic -ndm 3 -ndf 3
node 1 0 0 0; node 2 1 0 0; node 3 1 1 0; node 4 0 1 0
node 5 0 0 1; node 6 1 0 1; node 7 1 1 1; node 8 0 1 1
nDMaterial ElasticOrthotropic 1  1000.0 4000.0 1000.0  0.0 0.0 0.0  500.0 500.0 500.0
element SSPbrick 1 1 2 3 4 5 6 7 8 1
fix 1 1 1 1; fix 2 0 1 1; fix 5 0 1 0; fix 6 0 1 0
pattern Plain 1 Linear { load 4 0 2.5 0; load 3 0 2.5 0; load 8 0 2.5 0; load 7 0 2.5 0 }
constraints Transformation
numberer RCM
system BandGeneral
test NormDispIncr 1e-8 100
algorithm Newton
integrator LoadControl 1
analysis Static
analyze 1
puts $out "Y = [nodeDisp 4 2]"
wipe

close $out
