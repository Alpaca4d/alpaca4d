# One ten node tetrahedron, straight edged but not regular, its corners tilted off every axis, and
# its edge nodes at the midpoints in TenNodeTetrahedronStd order: 1-2, 2-3, 3-1, 1-4, 2-4, 3-4.
# Sourced by the decks next to it, which set what is done to it.
wipe
model BasicBuilder -ndm 3 -ndf 3
set corners {{0.0 0.0 0.0} {2.0 0.2 0.1} {0.3 1.5 -0.2} {0.4 0.3 1.8}}
for {set i 0} {$i < 4} {incr i} {
    eval node [expr {$i + 1}] [lindex $corners $i]
}
set tag 5
foreach edge {{1 2} {2 3} {3 1} {1 4} {2 4} {3 4}} {
    set a [lindex $corners [expr {[lindex $edge 0] - 1}]]
    set b [lindex $corners [expr {[lindex $edge 1] - 1}]]
    node $tag [expr {([lindex $a 0] + [lindex $b 0]) / 2.0}] \
              [expr {([lindex $a 1] + [lindex $b 1]) / 2.0}] \
              [expr {([lindex $a 2] + [lindex $b 2]) / 2.0}]
    incr tag
}
