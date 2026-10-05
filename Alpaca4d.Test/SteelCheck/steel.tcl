# Three 3 m cantilevers written the way Alpaca4d writes a beam - an elastic section with shear,
# Newton-Cotes over five sections, an MPCO recorder asking for section.force - for the whole chain
# from recorder file to utilisation. Each is statically determinate, so the forces at the fixed end
# are the tip loads times the lever arm whatever the section stiffness; the stiffness here is only
# roughly right. Units kN and m.
#
#   1  HE 300 B-like, S355:  N = -400 kN, Vy = 30 kN, so Mz = 90 kNm at the fixed end
#   2  RHS 200 x 100 x 10, S275:  N = +100 kN, Vz = 5 kN, so My = 15 kNm
#   3  a glulam rectangle, which has no steel grade and is left unchecked
wipe
model BasicBuilder -ndm 3 -ndf 6

node 1 0.0 0.0 0.0
node 2 3.0 0.0 0.0
node 3 0.0 1.0 0.0
node 4 3.0 1.0 0.0
node 5 0.0 2.0 0.0
node 6 3.0 2.0 0.0
fix 1 1 1 1 1 1 1
fix 3 1 1 1 1 1 1
fix 5 1 1 1 1 1 1

# section Elastic tag E A Iz Iy G J alphaY alphaZ
section Elastic 1 210.0e6 0.0143 2.47e-4 8.56e-5 80.77e6 1.4e-6 0.8 0.8
section Elastic 2 210.0e6 0.0056 2.86e-5 9.55e-6 80.77e6 2.4e-5 0.9 0.9
section Elastic 3 11.0e6 0.08 1.07e-3 2.67e-4 0.65e6 7.3e-4 0.83 0.83

geomTransf Linear 1 0 0 1
geomTransf Linear 2 0 0 1
geomTransf Linear 3 0 0 1

element forceBeamColumn 1 1 2 1 NewtonCotes 1 5
element forceBeamColumn 2 3 4 2 NewtonCotes 2 5
element forceBeamColumn 3 5 6 3 NewtonCotes 3 5

timeSeries Linear 1
pattern Plain 1 1 {
    load 2 -400.0 30.0 0.0 0.0 0.0 0.0
    load 4  100.0  0.0 5.0 0.0 0.0 0.0
    load 6    0.0  5.0 0.0 0.0 0.0 0.0
}

recorder mpco steel.mpco -E section.force

system BandGeneral
numberer RCM
constraints Plain
test NormDispIncr 1.0e-12 20
algorithm Newton
integrator LoadControl 1.0
analysis Static
analyze 1
wipe
