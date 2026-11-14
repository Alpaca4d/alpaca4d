# Checks `printA -file <f> -sparse <base> -binary` (OpenSees PR #1859) in a built OpenSees:
#
#   OpenSees printa-binary-check.tcl <a folder to write in>
#
# The file holds four int64 - rows, columns, entries, base - then the row and column indices as
# int32 and the values as doubles, in the machine's byte order: exactly what printA -sparse -ret
# returns. Checked for three systems and both bases; misuse must be refused, writing no file.
# Exits 0 when all is right, 1 when not.

set dir [lindex $argv 0]
proc frame {system} {
  wipe
  model basic -ndm 2 -ndf 3
  node 1 0 0; node 2 0 3; node 3 4 3; node 4 4 0
  fix 1 1 1 1; fix 4 1 1 0
  geomTransf Linear 1
  element elasticBeamColumn 1 1 2 0.01 2e11 1e-4 1
  element elasticBeamColumn 2 2 3 0.01 2e11 1e-4 1
  element elasticBeamColumn 3 3 4 0.01 2e11 1e-4 1
  timeSeries Constant 1; pattern Plain 1 1 { load 2 1000 0 0 }
  system $system; numberer RCM; constraints Plain; integrator LoadControl 1.0; algorithm Linear; analysis Static
  analyze 1
}

set ok 1
foreach system {SparseGeneral UmfPack FullGeneral} {
  foreach base {0 1} {
    frame $system
    set f [file join $dir A_${system}_$base.bin]
    set r [printA -file $f -sparse $base -binary]
    set a [printA -sparse $base -ret]
    set h [open $f rb]; set data [read $h]; close $h
    # An OpenSees without -binary ignores it, and writes the text into the file.
    if {[binary scan $data mmmm rows cols nnz b] != 4 || $nnz < 0 || 32 + 16 * $nnz != [string length $data]} {
      puts "$system, base $base: not a binary file - this OpenSees does not know printA -binary"
      set ok 0
      continue
    }
    set at 32
    binary scan $data @${at}n${nnz} ri; incr at [expr {4 * $nnz}]
    binary scan $data @${at}n${nnz} ci; incr at [expr {4 * $nnz}]
    binary scan $data @${at}d${nnz} vs; incr at [expr {8 * $nnz}]
    set same [expr {$r == 0 && $rows == [systemSize] && $cols == [systemSize] && $b == $base
      && $at == [string length $data] && $nnz == [llength [dict get $a values]]
      && $ri eq [dict get $a rowIndices] && $ci eq [dict get $a colIndices]}]
    foreach x $vs y [dict get $a values] { if {$x != $y} { set same 0 } }
    puts "$system, base $base: $nnz entries, the same as printA -sparse -ret: $same"
    if {!$same} { set ok 0 }
    file delete $f
  }
}

set no [file join $dir no.bin]
file delete $no
frame SparseGeneral
foreach args [list {-binary} [list -file $no -binary] [list -file $no -sparse 0 -binary -ret]] {
  set refused [catch {printA {*}$args}]
  puts "printA $args: refused $refused, file written [file exists $no]"
  if {!$refused || [file exists $no]} { set ok 0 }
}
wipe
model basic -ndm 1 -ndf 1
node 1 0
set refused [catch {printA -file $no -sparse 0 -binary}]
puts "printA -binary with no analysis: refused $refused, file written [file exists $no]"
if {!$refused || [file exists $no]} { set ok 0 }

puts [expr {$ok ? "printA -binary: all right" : "printA -binary: WRONG"}]
exit [expr {$ok ? 0 : 1}]
