# A two-element cantilever plate in pure bending, thin enough that the answer is a
# textbook one: sigma = -/+ 6*m/h^2 on the bottom and top faces, zero at mid-depth.
# Layer 1 of the stack is the bottom face.
wipe
model basic -ndm 3 -ndf 6
node 1 0 0 0
node 2 1 0 0
node 3 1 1 0
node 4 0 1 0
node 5 2 0 0
node 6 2 1 0
fix 1 1 1 1 1 1 1
fix 4 1 1 1 1 1 1
nDMaterial ElasticIsotropic 1 30000000.0 0.2 2500.0
nDMaterial ElasticIsotropic 2 10000000.0 0.2 800.0
section LayeredShell 1 5 2 0.02 1 0.05 1 0.06 1 0.05 2 0.02
element ASDShellQ4 1 1 2 3 4 1
element ASDShellQ4 2 2 5 6 3 1
recorder mpco layered -N displacement rotation -E stresses section.force section.fiber.stress
timeSeries Linear 1
pattern Plain 1 1 {
  load 5 0 0 -10 0 0 0
  load 6 0 0 -10 0 0 0
}
constraints Transformation
numberer RCM
system BandSPD
test NormDispIncr 1e-8 10
algorithm Newton
integrator LoadControl 1
analysis Static
analyze 1
