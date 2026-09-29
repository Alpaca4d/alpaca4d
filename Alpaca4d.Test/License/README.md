# License

    ./run.sh

What a license allows, and what its absence stops.

| without a license | with one |
| --- | --- |
| Run Analysis and Natural Vibration Analysis run models of up to **50 elements**, and refuse larger ones with an error | any size |
| **View Results**, **Model View** and **Moment Curvature** refuse to run, whatever the model | they run |
| every other component runs | every component runs |

A refusal also opens the License window, but at most once every five minutes: a refused
component re-solves whenever anything upstream changes, and the window would reopen every time.

## What runs

`License.cs` points the license file at a folder of its own and writes real license files into
it, the way `CreateLicense` writes them, then checks:

| check | why |
| --- | --- |
| no file, 50 and 51 elements | the limit sits exactly at 50, and 51 is refused on every run, not once every five minutes |
| the window, twice in a row | a refusal opens it once; the next one within five minutes does not, unless forced |
| a `FreeVersion` license, in date and expired | in date it lifts both rules; expired it lifts neither |
| a license for this machine and for another | only a matching MAC address counts |
| a file that is not a license | it counts as none, and nothing throws |

## What is not checked here

The error each component shows, and the re-run of refused components once a license is added
from the License window, go through Grasshopper and have to be tried in Rhino.
