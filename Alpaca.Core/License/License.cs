using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Linq;
using System.Net.NetworkInformation;


namespace Alpaca4d.License
{
    public static class License
    {
        /// <summary>
        /// The license file, in <see cref="Application.UserFolder"/>. It used to be data.bin beside
        /// the plugin, and was left behind by every update; <see cref="AdoptEarlierLicense"/>
        /// brings it over.
        /// </summary>
        public static string licenseLocation = System.IO.Path.Combine(Application.UserFolder, "license.bin");

        /// <summary>What the license file was called when it sat beside the plugin.</summary>
        private const string PluginFolderLicense = "data.bin";

        /// <summary>The <see cref="licenseLocation"/> an earlier license was last looked for at.</summary>
        private static string adoptedFor = null;

        /// <summary>
        /// The most elements a model may have and still be analysed without a license. Past it,
        /// Run Analysis and Natural Vibration Analysis refuse to run.
        /// </summary>
        public const int FreeElementLimit = 50;

        /// <summary>
        /// How often a refusal opens the License window. The refusal itself happens on every
        /// solve; the window would reopen on every solve too, and a blocked component re-solves
        /// whenever anything upstream changes.
        /// </summary>
        private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(5);
        private static DateTime? reminderLastShown = null;
        public static bool IsValid
        {
            get
            {
                AdoptEarlierLicense();
                return IsLicense(licenseLocation);
            }
        }

        /// <summary>Whether the file at <paramref name="filePath"/> licenses this computer today.</summary>
        public static bool IsLicense(string filePath)
        {
            try
            {
                // if the file does not exist, it raise exception
                var users = License.DeserializeBinary(filePath);
                var addresses = GetMacAddress();

                foreach (var user in users)
                {
                    if (user.user_name == "FreeVersion")
                    {
                        if (DateTime.Now < user.expiring_date)
                            return true;
                    }

                    foreach (var macAdress in addresses)
                    {
                        if ((macAdress == user.mac_address) && (DateTime.Now < user.expiring_date))
                            return true;
                    }
                }
            }
            catch (Exception)
            {
                // No licence file, an unreadable one, or one written by something else:
                // all of them mean the same thing here, so there is nothing to inspect.
                return false;
            }
            return false;
        }

        /// <summary>
        /// Brings over the license an earlier version kept beside itself, when there is none at
        /// <see cref="licenseLocation"/>: the newest of those copies that is still a license here,
        /// so that the expired one the package used to ship is passed over. Looked for once a
        /// session, as the license is read on every solve.
        /// </summary>
        public static void AdoptEarlierLicense()
        {
            if (adoptedFor == licenseLocation)
                return;
            adoptedFor = licenseLocation;
            Application.AdoptFromPluginFolders(PluginFolderLicense, licenseLocation, IsLicense);
        }

        /// <summary>
        /// Makes the license file at <paramref name="filePath"/> this computer's license, in place of
        /// the one it had. Check it with <see cref="IsLicense"/> first: this replaces whatever is there.
        /// </summary>
        public static void Install(string filePath)
        {
            if (string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(licenseLocation), StringComparison.OrdinalIgnoreCase))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(licenseLocation));
            File.Copy(filePath, licenseLocation, true);
        }

        /// <summary>
        /// Removes the license from this computer: the file the License window adds, and the copies
        /// earlier versions kept beside themselves, which would otherwise be brought back the next
        /// time Alpaca4d starts.
        /// </summary>
        public static void Remove()
        {
            if (File.Exists(licenseLocation))
                File.Delete(licenseLocation);

            foreach (var copy in Application.PluginFolderCopies(PluginFolderLicense))
            {
                try
                {
                    File.Delete(copy);
                }
                catch
                {
                    // A folder that cannot be written to. A license left in it comes back at the
                    // next start, as it would have stayed in use before.
                }
            }
        }

        public static List<string> GetMacAddress()
        {
            var macAddress = NetworkInterface.GetAllNetworkInterfaces().Select(x => x.GetPhysicalAddress().ToString()).ToList();
            return macAddress;
        }


        // method for Alpaca4d to read back the results
        public static List<User> DeserializeBinary(string filePath)
        {
            byte[] binaryData = File.ReadAllBytes(filePath);
            var deserializedObject = MessagePack.MessagePackSerializer.Typeless.Deserialize(binaryData);
            var myobject = (List<User>)deserializedObject;
            return myobject;
        }

        public static byte[] SerializeJsonToBinary(string filePath)
        {
            byte[] binaryData;

            string jsonData = System.IO.File.ReadAllText(filePath);
            var myObject = Newtonsoft.Json.JsonConvert.DeserializeObject<List<User>>(jsonData);

            binaryData = MessagePack.MessagePackSerializer.Typeless.Serialize(myObject);
            return binaryData;
        }

        public static void SerializeBinaryToFile(string filePath, byte[] binaryData)
        {
            File.WriteAllBytes(filePath, binaryData);
        }

        public static List<User> DeserialiseJSON(string filePath)
        {
            string jsonData = System.IO.File.ReadAllText(filePath);
            var users = JsonConvert.DeserializeObject<List<User>>(jsonData);

            return users;
        }

        /// <summary>
        /// Whether a model this size may be analysed: always with a license, and without one only up
        /// to <paramref name="maxElements"/> elements. A refusal calls <paramref name="showFormCallback"/>,
        /// at most once every five minutes unless <paramref name="forceCheck"/> is set.
        ///
        /// Checked on every call. It used to look only once every five minutes and let everything
        /// through in between, which was enough for a reminder and is not enough for a limit.
        /// </summary>
        public static bool ValidateLicense(Alpaca4d.Model model, bool forceCheck = false, Action showFormCallback = null, int maxElements = FreeElementLimit)
        {
            // The size first: a small model never needs the license file read.
            if (model.Elements.Count <= maxElements || IsValid)
                return true;

            Remind(showFormCallback, forceCheck);
            return false;
        }

        /// <summary>
        /// Whether a component that needs a license whatever the model may run. A refusal calls
        /// <paramref name="showFormCallback"/>, on the same five-minute rule as <see cref="ValidateLicense"/>.
        /// </summary>
        public static bool ValidateFeature(Action showFormCallback = null, bool forceCheck = false)
        {
            if (IsValid)
                return true;

            Remind(showFormCallback, forceCheck);
            return false;
        }

        private static void Remind(Action showFormCallback, bool forceCheck)
        {
            if (showFormCallback == null)
                return;

            var now = DateTime.Now;
            if (!forceCheck && reminderLastShown.HasValue && now - reminderLastShown.Value < ReminderInterval)
                return;

            reminderLastShown = now;
            showFormCallback();
        }
    }

    public class User
    {
        public string user_name { get; set; }
        public string mac_address { get; set; }
        public System.DateTime expiring_date { get; set; }
    }
}