// What a license allows, and what its absence stops.
//
// Two rules, both in Alpaca4d.License.License:
//
//   1. An analysis is free up to FreeElementLimit elements. Past it, Run Analysis and Natural
//      Vibration Analysis refuse without a license - checked on every run, where it used to be
//      checked once every five minutes and let everything through in between.
//   2. Some components need a license whatever the model: View Results, Model View and Moment
//      Curvature. They ask ValidateFeature.
//
// Either refusal opens the License window, but no more than once every five minutes, because a
// refused component re-solves whenever anything upstream changes.
//
// The license file is pointed at a folder of this test's own, and written the way CreateLicense
// writes it, so the rules are checked against real files rather than a stand-in for them. What
// the components then say, and the re-run after a license is added, need Grasshopper and are not
// checked here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using LicenseRules = Alpaca4d.License.License;

class LicenseTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    /// <summary>Only counted: the rules look at how many elements there are and nothing else.</summary>
    class Stub : IElement
    {
        public ElementType Type => ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Model model) { }
        public string WriteTcl() => "";
    }

    static Model Of(int elements) =>
        new Model { Elements = Enumerable.Range(0, elements).Select(i => (IElement)new Stub()).ToList() };

    static void Write(string userName, string mac, DateTime expiring)
    {
        string json = Path.Combine(Path.GetTempPath(), "alpaca_license_test.json");
        File.WriteAllText(json,
            $"[{{\"user_name\": \"{userName}\", \"mac_address\": \"{mac}\", \"expiring_date\": \"{expiring:yyyy-MM-dd}\"}}]");
        LicenseRules.SerializeBinaryToFile(LicenseRules.licenseLocation, LicenseRules.SerializeJsonToBinary(json));
        File.Delete(json);
    }

    static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "alpaca_license_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        LicenseRules.licenseLocation = Path.Combine(folder, "data.bin");

        int shown = 0;
        Action window = () => shown++;

        try
        {
            Console.WriteLine($"\nNo license file: free up to {LicenseRules.FreeElementLimit} elements");

            Check(LicenseRules.FreeElementLimit == 50, "the limit is 50");
            Check(!LicenseRules.IsValid, "no file is no license");
            Check(LicenseRules.ValidateLicense(Of(50), false, window), "50 elements run");
            Check(shown == 0, "and open no window");
            Check(!LicenseRules.ValidateLicense(Of(51), false, window), "51 elements are refused");
            Check(shown == 1, "and open the License window");
            Check(!LicenseRules.ValidateLicense(Of(51), false, window), "51 elements are refused again at once");
            Check(shown == 1, "without opening the window a second time");
            Check(!LicenseRules.ValidateFeature(window), "a licensed component is refused");
            Check(shown == 1, "and the five minutes are shared with the element limit");
            Check(!LicenseRules.ValidateFeature(window, forceCheck: true), "forced, it is refused");
            Check(shown == 2, "and opens the window regardless");
            Check(!LicenseRules.ValidateFeature(null), "refused with no window to open");

            Console.WriteLine("\nA FreeVersion license that has not expired");

            Write("FreeVersion", "", DateTime.Today.AddMonths(1));
            Check(LicenseRules.IsValid, "is a license");
            Check(LicenseRules.ValidateLicense(Of(5000), false, window), "5000 elements run");
            Check(LicenseRules.ValidateFeature(window), "a licensed component runs");

            Console.WriteLine("\nA FreeVersion license that has expired");

            Write("FreeVersion", "", DateTime.Today.AddDays(-1));
            Check(!LicenseRules.IsValid, "is no license");
            Check(!LicenseRules.ValidateLicense(Of(51), false, null), "51 elements are refused");
            Check(LicenseRules.ValidateLicense(Of(50), false, null), "50 still run");
            Check(!LicenseRules.ValidateFeature(null), "a licensed component is refused");

            Console.WriteLine("\nA license for one machine");

            string mine = LicenseRules.GetMacAddress().FirstOrDefault(mac => !string.IsNullOrEmpty(mac));
            if (mine == null)
            {
                Console.WriteLine("  this machine reports no MAC address, so these were skipped");
            }
            else
            {
                Write("Someone", mine, DateTime.Today.AddYears(1));
                Check(LicenseRules.IsValid, "this machine's address is a license here");
                Check(LicenseRules.ValidateFeature(null), "and a licensed component runs");

                Write("Someone", "000000000000", DateTime.Today.AddYears(1));
                Check(!LicenseRules.IsValid, "another machine's address is not");
                Check(!LicenseRules.ValidateLicense(Of(51), false, null), "and 51 elements are refused");
            }

            Console.WriteLine("\nA file that is not a license");

            File.WriteAllText(LicenseRules.licenseLocation, "not a license");
            Check(!LicenseRules.IsValid, "is no license, and does not throw");
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all license checks passed" : $"{fails} license check(s) failed");
        return fails == 0 ? 0 : 1;
    }
}
