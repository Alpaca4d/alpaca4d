# Two cantilevers with elastic fibre sections, loaded every way at once, so the stress OpenSees
# integrates in a fibre can be set against the stress Alpaca4d recovers from the section forces.
#
# The first two sections are lopsided on purpose. An I with unequal flanges and a pair of angles
# both have their centroid off the middle of the drawing, and a recovery that measured from the
# wrong point, or bent the wrong way about either axis, would still look right on anything
# symmetric. The last three - a round bar, a tube and an IPE 300 - are the common shapes, and are
# checked fibre by fibre over the whole section rather than at a few corners.
#
# Coordinates are local (y, z), laid out as Alpaca4d draws the two sections: height along y,
# width along z. Units N and m.
wipe
model BasicBuilder -ndm 3 -ndf 6

node 1 0.0 0.0 0.0
node 2 2.0 0.0 0.0
node 3 0.0 1.0 0.0
node 4 2.0 1.0 0.0
node 5 0.0 2.0 0.0
node 6 2.0 2.0 0.0
node 7 0.0 3.0 0.0
node 8 2.0 3.0 0.0
node 9 0.0 4.0 0.0
node 10 2.0 4.0 0.0
fix 1 1 1 1 1 1 1
fix 3 1 1 1 1 1 1
fix 5 1 1 1 1 1 1
fix 7 1 1 1 1 1 1
fix 9 1 1 1 1 1 1

uniaxialMaterial Elastic 1 210.0e9

# I, height 0.30: top flange 0.20 x 0.015, bottom flange 0.12 x 0.010, web 0.008.
section Fiber 1 -GJ 1.0e5 {
    patch rect 1   8 80   0.135 -0.100   0.150  0.100
    patch rect 1 110  4  -0.140 -0.004   0.135  0.004
    patch rect 1   8 48  -0.150 -0.060  -0.140  0.060
}

# Two angles 100 x 75 x 8 either side of a 10 gap, long legs upright, short legs along the bottom.
section Fiber 2 -GJ 1.0e5 {
    patch rect 1 100  8  -0.050  0.005   0.050  0.013
    patch rect 1   8 67  -0.050  0.013  -0.042  0.080
    patch rect 1 100  8  -0.050 -0.013   0.050 -0.005
    patch rect 1   8 67  -0.050 -0.080  -0.042 -0.013
}

# Round bar, 100 diameter.
section Fiber 3 -GJ 1.0e5 {
    patch circ 1 72 24  0.0 0.0  0.0 0.050  0.0 360.0
}

# Tube, 200 outside, 10 wall.
section Fiber 4 -GJ 1.0e5 {
    patch circ 1 72 6  0.0 0.0  0.090 0.100  0.0 360.0
}

# IPE 300 without its root radii: flanges 150 x 10.7, web 7.1, depth 300.
section Fiber 5 -GJ 1.0e5 {
    patch rect 1   8 60   0.1393 -0.075   0.150  0.075
    patch rect 1 120  4  -0.1393 -0.00355 0.1393 0.00355
    patch rect 1   8 60  -0.150  -0.075  -0.1393 0.075
}

# Local z along global Z, so local y is global Y.
geomTransf Linear 1 0 0 1

element forceBeamColumn 1 1 2 1 Lobatto 1 5
element forceBeamColumn 2 3 4 1 Lobatto 2 5
element forceBeamColumn 3 5 6 1 Lobatto 3 5
element forceBeamColumn 4 7 8 1 Lobatto 4 5
element forceBeamColumn 5 9 10 1 Lobatto 5 5

timeSeries Linear 1
pattern Plain 1 1 {
    load 2  10000.0  2000.0 -1500.0  300.0 0.0 0.0
    load 4  -5000.0  -800.0   600.0   50.0 0.0 0.0
    load 6  20000.0  1500.0  2500.0  100.0 0.0 0.0
    load 8 -30000.0 -4000.0  2000.0  400.0 0.0 0.0
    load 10 50000.0  8000.0 -1200.0   60.0 0.0 0.0
}

# Section 1 is the fixed end, where the moments are largest. A fibre section reports
# P, Mz, My and T, in that order.
recorder Element -file force1.txt -ele 1 section 1 force
recorder Element -file force2.txt -ele 2 section 1 force
recorder Element -file force3.txt -ele 3 section 1 force
recorder Element -file force4.txt -ele 4 section 1 force
recorder Element -file force5.txt -ele 5 section 1 force

# Every fibre of the last three: y, z, area, stress, strain, fibre after fibre.
recorder Element -file bar_fibres.txt  -ele 3 section 1 fiberData
recorder Element -file tube_fibres.txt -ele 4 section 1 fiberData
recorder Element -file ipe_fibres.txt  -ele 5 section 1 fiberData

# The fibre nearest each corner of the I. Each point is that fibre's own centre.
recorder Element -file i_top_pos.txt -ele 1 section 1 fiber  0.1490625  0.09875 stress
recorder Element -file i_top_neg.txt -ele 1 section 1 fiber  0.1490625 -0.09875 stress
recorder Element -file i_bot_pos.txt -ele 1 section 1 fiber -0.149375   0.05875 stress
recorder Element -file i_bot_neg.txt -ele 1 section 1 fiber -0.149375  -0.05875 stress

# And of the angles: the top of each long leg and the tip of each short one.
recorder Element -file l_top_pos.txt -ele 2 section 1 fiber  0.0495  0.0125 stress
recorder Element -file l_tip_pos.txt -ele 2 section 1 fiber -0.0495  0.0795 stress
recorder Element -file l_top_neg.txt -ele 2 section 1 fiber  0.0495 -0.0125 stress
recorder Element -file l_tip_neg.txt -ele 2 section 1 fiber -0.0495 -0.0795 stress

system BandGeneral
numberer RCM
constraints Plain
test NormDispIncr 1.0e-12 20
algorithm Newton
integrator LoadControl 1.0
analysis Static
analyze 1
wipe
