using System;
using System.Linq;
using System.Reflection;
using Alpaca4d.Interop;

namespace Alpaca4d.Karamba3D
{
    /// <summary>
    /// The way into Karamba3D for code that must not touch Karamba's types itself.
    ///
    /// Alpaca4d loads whether or not Karamba3D is installed, so nothing Grasshopper inspects when it
    /// loads the plug-in may name a Karamba type. Nothing here does: only <see cref="KarambaReader"/>
    /// does, and its methods are compiled the first time one is called - by which point
    /// <see cref="LoadedVersion"/> has said Karamba3D is there.
    /// </summary>
    public static class KarambaImport
    {
        /// <summary>The Karamba3D major version this reader is compiled against.</summary>
        public const int SupportedMajorVersion = 3;

        private const string KarambaCommon = "KarambaCommon";

        private static bool _resolverInstalled;

        /// <summary>The version of the KarambaCommon Karamba3D has loaded, or null when Karamba3D is not loaded.</summary>
        public static Version LoadedVersion() => LoadedKarambaCommon()?.GetName().Version;

        /// <summary>Whether <paramref name="value"/> is a Karamba3D model, judged by name so that Karamba need not be loadable.</summary>
        public static bool IsKarambaModel(object value) => value?.GetType().FullName == "Karamba.Models.Model";

        /// <summary>
        /// Reads a Karamba3D model. Throws <see cref="System.IO.FileNotFoundException"/>,
        /// <see cref="TypeLoadException"/> or <see cref="MissingMemberException"/> when the Karamba3D
        /// loaded is not one this reader was compiled for.
        /// </summary>
        public static ImportModel Read(object model)
        {
            InstallResolver();
            return KarambaReader.Read(model);
        }

        private static Assembly LoadedKarambaCommon() =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == KarambaCommon);

        /// <summary>
        /// Points requests for KarambaCommon at the copy Karamba3D loaded. The runtime looks for it
        /// next to Alpaca4d first, where it is not and must not be; Rhino's own resolver normally finds
        /// it after that, and this is the fallback for when it does not.
        /// </summary>
        private static void InstallResolver()
        {
            if (_resolverInstalled)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                new AssemblyName(args.Name).Name == KarambaCommon ? LoadedKarambaCommon() : null;
            _resolverInstalled = true;
        }
    }
}
