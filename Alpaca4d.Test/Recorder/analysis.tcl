# Two static steps. Nothing here is checked for its value, only for where it lands in the file.
timeSeries Linear 1
pattern Plain 1 1 {
  load 2 0 0 -1000 0 0 0
  load 16 0 0 -1000 0 0 0
  load 4 0 0 -10 0 0 0
  load 5 0 0 -10 0 0 0
  load 8 0 0 -10 0 0 0
  load 11 0 0 -10 0 0 0
  load 14 0 0 -10 0 0 0
  load 105 0 0 -10
  load 112 0 0 -10
}
constraints Plain
numberer RCM
system UmfPack
test NormDispIncr 1e-8 10
algorithm Newton
integrator LoadControl 0.5
analysis Static
analyze 2
wipe
