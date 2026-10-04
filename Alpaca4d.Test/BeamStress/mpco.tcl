# Two cantilevers written the way Alpaca4d writes a beam - an elastic section with shear,
# Newton-Cotes over five sections, an MPCO recorder asking for section.force - so the whole path
# from recorder file to stress can be checked, not only the arithmetic at the end of it.
#
# Element 1 has the section of a 0.2 x 0.4 rectangle (RectangleCS: width along local z, height
# along y). Element 2 has made-up properties, the way an Elastic Section does, with no shape behind
# them. Every load is different, so a force read into the wrong slot reads as the wrong stress.
# Units N and m.
wipe
model BasicBuilder -ndm 3 -ndf 6

node 1 0.0 0.0 0.0
node 2 3.0 0.0 0.0
node 3 0.0 2.0 0.0
node 4 3.0 2.0 0.0
fix 1 1 1 1 1 1 1
fix 3 1 1 1 1 1 1

# section Elastic tag E A Iz Iy G J alphaY alphaZ, as RectangleCS.WriteTcl has it.
section Elastic 1 3.0e10 0.08 0.00106666666666667 0.000266666666666667 1.25e10 0.000732421875 0.833333333333333 0.833333333333333
section Elastic 2 3.0e10 0.01 0.0001 0.00002 1.25e10 0.00001 0.8 0.8

geomTransf Linear 1 0 0 1
geomTransf Linear 2 0 0 1

element forceBeamColumn 1 1 2 1 NewtonCotes 1 5
element forceBeamColumn 2 3 4 2 NewtonCotes 2 5

timeSeries Linear 1
pattern Plain 1 1 {
    load 2  12000.0  3000.0 -2000.0  700.0 0.0 0.0
    load 4  -4000.0   500.0   250.0   20.0 0.0 0.0
}

recorder mpco beam.mpco -E section.force

system BandGeneral
numberer RCM
constraints Plain
test NormDispIncr 1.0e-12 20
algorithm Newton
integrator LoadControl 1.0
analysis Static
analyze 1
wipe
