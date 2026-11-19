# Three 3 m cantilevers of timber written the way Alpaca4d writes a beam - an elastic section with
# shear, Newton-Cotes over five sections, an MPCO recorder asking for section.force - for the whole
# chain from recorder file to the EN 1995-1-1 utilisation. Each is statically determinate, so the
# forces at the fixed end are the tip loads times the lever arm whatever the stiffness. Units kN, m.
#
#   1  C24, 200 high x 100 wide:  N = -20 kN, Vy = 3 kN, so Mz = 9 kNm at the fixed end
#   2  GL24h, 400 high x 140 wide:  N = +30 kN, Vz = 4 kN, so My = 12 kNm; T = 0.5 kNm
#   3  C24, round 200:  Vy = 2 kN, so Mz = 6 kNm
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
section Elastic 1 11.0e6 0.02 6.6667e-5 1.6667e-5 0.69e6 4.58e-5 0.8333 0.8333
section Elastic 2 11.5e6 0.056 7.4667e-4 9.1467e-5 0.65e6 2.84e-4 0.8333 0.8333
section Elastic 3 11.0e6 0.031416 7.854e-5 7.854e-5 0.69e6 1.5708e-4 0.9 0.9

geomTransf Linear 1 0 0 1
geomTransf Linear 2 0 0 1
geomTransf Linear 3 0 0 1

element forceBeamColumn 1 1 2 1 NewtonCotes 1 5
element forceBeamColumn 2 3 4 2 NewtonCotes 2 5
element forceBeamColumn 3 5 6 3 NewtonCotes 3 5

timeSeries Linear 1
pattern Plain 1 1 {
    load 2 -20.0 3.0 0.0 0.0 0.0 0.0
    load 4  30.0 0.0 4.0 0.5 0.0 0.0
    load 6   0.0 2.0 0.0 0.0 0.0 0.0
}

recorder mpco timber.mpco -E section.force

system BandGeneral
numberer RCM
constraints Plain
test NormDispIncr 1.0e-12 20
algorithm Newton
integrator LoadControl 1.0
analysis Static
analyze 1
wipe
