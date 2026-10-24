// What an update carries over: the license, and settings.json with the OpenSees path.
//
// The package manager installs every version of Alpaca4d in a folder of its own -
// packages/8.0/Alpaca4d/<version> - and both files used to be kept beside the plugin, so every
// update started with no license and no solver. They are kept in Application.UserFolder now, and
// the first time either is looked for and not found there, it is brought over from the folders of
// the versions installed before.
//
// Everything happens in a folder of the test's own: the package folders, the user folder and the
// files in them are made up here, so the real ones are never read or written.
using System;
using System.IO;
using System.Runtime.CompilerServices;

using Alpaca4d;
using LicenseRules = Alpaca4d.License.License;

class UpdateTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static string root;

    /// <summary>A folder of its own for one case: its package folders and its user folder.</summary>
    static string Case(string name)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        LicenseRules.licenseLocation = Path.Combine(folder, "user", "license.bin");
        return folder;
    }

    /// <summary>The folder the package manager installs <paramref name="version"/> in.</summary>
    static string Version(string caseFolder, string version)
    {
        string folder = Path.Combine(caseFolder, "packages", "8.0", "Alpaca4d", version);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A license file, the way CreateLicense writes it.</summary>
    static string WriteLicense(string path, DateTime expiring)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string json = Path.Combine(root, "license.json");
        File.WriteAllText(json,
            $"[{{\"user_name\": \"FreeVersion\", \"mac_address\": \"\", \"expiring_date\": \"{expiring:yyyy-MM-dd}\"}}]");
        LicenseRules.SerializeBinaryToFile(path, LicenseRules.SerializeJsonToBinary(json));
        File.Delete(json);
        return path;
    }

    static readonly DateTime InDate = DateTime.Today.AddMonths(1);

    /// <summary>What the package shipped: a FreeVersion long expired.</summary>
    static readonly DateTime Shipped = new DateTime(2026, 1, 1);

    static void Licenses()
    {
        Console.WriteLine("\nAn update from the version the license was added to");
        {
            string c = Case("update");
            string old = WriteLicense(Path.Combine(Version(c, "0.12.0-beta.1"), "data.bin"), InDate);
            string current = Version(c, "0.12.0-beta.2");
            WriteLicense(Path.Combine(current, "data.bin"), Shipped);
            Application.GhAlpacaFolder = current;

            Check(LicenseRules.IsValid, "the license still counts");
            Check(File.Exists(LicenseRules.licenseLocation), "it was brought over to the user folder");
            Check(LicenseRules.IsLicense(LicenseRules.licenseLocation), "the one that works, not the expired one the package shipped");
            Check(File.Exists(old), "and the earlier version's copy was left where it was");

            Console.WriteLine("\nRemoving it");

            LicenseRules.Remove();
            Check(!File.Exists(LicenseRules.licenseLocation), "takes it out of the user folder");
            Check(Application.PluginFolderCopies("data.bin").Count == 0, "and out of every version's folder, so nothing brings it back");
            Check(!LicenseRules.IsValid, "leaving no license");
        }

        Console.WriteLine("\nAn update with no license before it");
        {
            string c = Case("none");
            WriteLicense(Path.Combine(Version(c, "0.12.0-beta.1"), "data.bin"), Shipped);
            string current = Version(c, "0.12.0-beta.2");
            WriteLicense(Path.Combine(current, "data.bin"), Shipped);
            Application.GhAlpacaFolder = current;

            Check(!LicenseRules.IsValid, "there is none after it either");
            Check(!File.Exists(LicenseRules.licenseLocation), "and no expired copy is brought over");
        }

        Console.WriteLine("\nA license already in the user folder");
        {
            string c = Case("kept");
            WriteLicense(Path.Combine(Version(c, "0.12.0-beta.1"), "data.bin"), InDate);
            Application.GhAlpacaFolder = Version(c, "0.12.0-beta.2");
            byte[] mine = File.ReadAllBytes(WriteLicense(LicenseRules.licenseLocation, Shipped));

            Check(!LicenseRules.IsValid, "is the one read, even expired");
            Check(Equal(File.ReadAllBytes(LicenseRules.licenseLocation), mine), "and is not replaced by an earlier version's");
        }

        Console.WriteLine("\nInstalled by hand into Grasshopper's Libraries");
        {
            string c = Case("by-hand");
            string libraries = Path.Combine(c, "Libraries");
            WriteLicense(Path.Combine(libraries, "SomeOtherPlugin", "data.bin"), InDate);
            Application.GhAlpacaFolder = Path.Combine(libraries, "Alpaca4d");
            Directory.CreateDirectory(Application.GhAlpacaFolder);

            Check(!LicenseRules.IsValid, "the folders of other plugins beside it are not looked in");

            Case("by-hand-own");
            WriteLicense(Path.Combine(Application.GhAlpacaFolder, "data.bin"), InDate);
            Check(LicenseRules.IsValid, "its own folder is");
        }

        Console.WriteLine("\nAdding a license");
        {
            string c = Case("install");
            Application.GhAlpacaFolder = Version(c, "0.12.0-beta.2");
            string downloaded = WriteLicense(Path.Combine(c, "Downloads", "data.bin"), InDate);

            LicenseRules.Install(downloaded);
            Check(LicenseRules.IsValid, "puts it in the user folder, which did not exist yet");

            bool threw = false;
            try { LicenseRules.Install(LicenseRules.licenseLocation); }
            catch (Exception) { threw = true; }
            Check(!threw && LicenseRules.IsValid, "picking the file already in use changes nothing");
        }
    }

    // Kept out of Main: settings.json's path is read from Application.UserFolder once, when
    // AlpacaSettings is first touched, and it has to be the test's folder by then.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Settings()
    {
        Console.WriteLine("\nSettings, after an update");

        string c = Path.Combine(root, "settings");
        string current = Version(c, "0.12.0-beta.2");
        Application.GhAlpacaFolder = current;

        // An older file still, from when it held the path alone.
        string oldest = Path.Combine(Version(c, "0.11.0"), "settings.json");
        File.WriteAllText(oldest, "\"/Applications/OpenSees3.5.0/bin/OpenSees\"");
        File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddDays(-30));

        string previous = Path.Combine(Version(c, "0.12.0-beta.1"), "settings.json");
        File.WriteAllText(previous,
            "{ \"OpenSeesPath\": \"/Applications/OpenSees3.7.1/bin/OpenSees\", \"Colours\": { \"NodeIds\": \"#FF0000\" } }");
        File.SetLastWriteTimeUtc(previous, DateTime.UtcNow.AddDays(-1));

        Check(AlpacaSettings.OpenSeesPath == "/Applications/OpenSees3.7.1/bin/OpenSees", "the OpenSees path is the one set last");
        Check(AlpacaSettings.Colour(DisplayItem.NodeIds).R == 255 && AlpacaSettings.Colour(DisplayItem.NodeIds).G == 0, "and so are the display colours");

        string mine = Path.Combine(Application.UserFolder, "settings.json");
        Check(File.Exists(mine), "brought over to the user folder");

        AlpacaSettings.OpenSeesPath = "/opt/OpenSees/bin/OpenSees";
        Check(File.ReadAllText(mine).Contains("/opt/OpenSees/bin/OpenSees"), "where a path set afterwards is saved");
        Check(File.ReadAllText(previous).Contains("OpenSees3.7.1"), "leaving the earlier version's file as it was");
    }

    static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "alpaca_update_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Application.UserFolder = Path.Combine(root, "settings", "user");

        try
        {
            Licenses();
            Settings();
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all update checks passed" : $"{fails} update check(s) failed");
        return fails == 0 ? 0 : 1;
    }
}
