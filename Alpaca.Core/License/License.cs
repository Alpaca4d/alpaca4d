using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;


namespace Alpaca4d.License
{
    public static class License
    {
        public static string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        public static string GhAlpacaFolder = System.IO.Path.GetDirectoryName(assemblyLocation);
        public static string licenseLocation = System.IO.Path.Combine(GhAlpacaFolder, "data.bin");
        
        // Static variables for license validation timing
        private static int validationCounter = 0;
        private static DateTime? validationFirstTimeRun = DateTime.Now;
        private static DateTime? validationLastTimeRun = null;
        public static bool IsValid
        {
            get
            {
                var addresses = GetMacAddress();

                try
                {
                    // if data.bin does not exist, it raise exception
                    var users = License.DeserializeBinary(licenseLocation);

                    // check data.bin
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
        /// Validates license and shows license management form if needed
        /// </summary>
        /// <param name="model">The Alpaca model to check element count against</param>
        /// <param name="forceCheck">If true, bypasses the time-based check</param>
        /// <param name="showFormCallback">Callback to show the license management form</param>
        /// <param name="maxElements">Maximum number of elements allowed</param>
        /// <returns>True if license is valid or form was shown, false otherwise</returns>
        public static bool ValidateLicense(Alpaca4d.Model model, bool forceCheck = false, Action showFormCallback = null, int maxElements = 100)
        {
            // Update last run time to the current DateTime
            validationLastTimeRun = DateTime.Now;

            // Check if we should validate (first run or every 5 minutes)
            if (forceCheck || validationCounter == 0 || validationLastTimeRun - validationFirstTimeRun > TimeSpan.FromMinutes(5))
            {
                // License validation routine
                if (!IsValid)
                {
                    // Get element count directly from the Model
                    int elementCount = model.Elements.Count;

                    if (elementCount > maxElements)
                    {
                        // Restart the window here too, otherwise the counter stays at zero
                        // and the form reopens on every single solve.
                        if (!forceCheck)
                        {
                            validationFirstTimeRun = DateTime.Now;
                            validationCounter++;
                        }

                        showFormCallback?.Invoke();
                        return false; // Return false to indicate license issue
                    }
                }
                
                if (!forceCheck)
                {
                    validationFirstTimeRun = DateTime.Now;
                    validationCounter++;
                }
            }
            
            return true; // License is valid
        }
    }

    public class User
    {
        public string user_name { get; set; }
        public string mac_address { get; set; }
        public System.DateTime expiring_date { get; set; }
    }
}