# Update

    ./run.sh

What an update carries over: the license, and `settings.json` with the OpenSees path and the
display colours.

The Package Manager installs every version in a folder of its own, `packages/8.0/Alpaca4d/<version>`.
Both files used to be kept beside the plugin, so every update started with no license and no solver.
They are kept in `Application.UserFolder` now - `%APPDATA%\Alpaca4d` on Windows,
`~/Library/Application Support/Alpaca4d` on a Mac - and the first time either is looked for and not
found there, it is brought over from the folders of the versions installed before.

## What runs

`Update.cs` lays out package folders, a user folder and the files in them in a folder of its own,
so the real ones are never read or written, then checks:

| check | why |
| --- | --- |
| an update from the version the license was added to | the license counts after it, copied to the user folder, and the copy that works is taken over the expired `FreeVersion` the package used to ship |
| removing it | it goes from every version's folder too, or the next start would bring it back |
| an update with no license before it | nothing is brought over, expired copies included |
| a license already in the user folder | it is the one read, and an earlier version's never replaces it |
| installed by hand into Grasshopper's Libraries | the folders of other plugins beside it are not looked in; its own is |
| adding a license | the user folder is made when there is none, and picking the file already in use changes nothing |
| settings after an update | the newest `settings.json` is brought over, and a path set afterwards is saved in the user folder |

## What is not checked here

The License window itself - that it refuses a file that is not a license before replacing the one
in use - goes through Eto and has to be tried in Rhino.
