# One SSP brick, a unit cube of E = 1000 and nu = 0, pulled to a uniform 10 along global X.
# The element line carries no body force arguments, which is exactly what Alpaca4d writes when
# BodyForce is null - it parses, and the body force defaults to zero.
wipe
model basic -ndm 3 -ndf 3
# unit cube, OpenSees ordering: 1-4 at z=0 ccw in xy, 5-8 above them
node 1 0 0 0
node 2 1 0 0
node 3 1 1 0
node 4 0 1 0
node 5 0 0 1
node 6 1 0 1
node 7 1 1 1
node 8 0 1 1
nDMaterial ElasticIsotropic 1 1000.0 0.0 0.0
# no body force arguments, exactly as Alpaca writes it when BodyForce is null
element SSPbrick 1 1 2 3 4 5 6 7 8 1   
fix 1 1 1 1
fix 4 1 1 0
fix 5 1 0 1
fix 8 1 0 0
pattern Plain 1 Linear {
  load 2 2.5 0 0
  load 3 2.5 0 0
  load 6 2.5 0 0
  load 7 2.5 0 0
}
recorder mpco one -N displacement -E stresses
constraints Transformation
numberer RCM
system BandGeneral
test NormDispIncr 1e-8 100
algorithm Newton
integrator LoadControl 1
analysis Static
analyze 1
puts "ux node2 = [nodeDisp 2 1]"
puts "stress = [eleResponse 1 stresses]"
wipe
