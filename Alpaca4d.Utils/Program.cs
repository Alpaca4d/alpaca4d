using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Alpaca4d.License;

namespace CreateLicense
{
    internal class Program
    {
        static void Main()
        {
            var jsonFilePath = "data.json";
            
            // Get the project root directory (Alpaca4d.Utils folder).
            // The exe runs from bin/<Configuration>/net48, so it is three levels down.
            var currentDir = Directory.GetCurrentDirectory();
            var projectRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));
            var binaryFilePath = Path.Combine(projectRoot, "data.bin");

            Console.WriteLine($"Current directory: {currentDir}");
            Console.WriteLine($"Project root: {projectRoot}");
            Console.WriteLine($"Binary file path: {binaryFilePath}");

            try
            {
                var binary = License.SerializeJsonToBinary(jsonFilePath);
                License.SerializeBinaryToFile(binaryFilePath, binary);
                Console.WriteLine("data.bin created successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating data.bin: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}