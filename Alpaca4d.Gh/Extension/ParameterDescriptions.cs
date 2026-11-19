using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The descriptions of an Alpaca4d component's inputs and outputs, as this version registers them.
    ///
    /// Grasshopper saves each parameter's description in the .gh file and reads it back, so a file
    /// saved by an older Alpaca4d goes on showing that version's texts - the long tooltips shortened
    /// on 2026-11-18, say - until the component is placed anew. When a document is opened, or objects
    /// are pasted or inserted into one, each Alpaca4d component's parameters take the description that
    /// a fresh one of the same type, on the same unit, registers, matched by name. A parameter whose
    /// name it does not register - one the user added or renamed - keeps its own.
    /// </summary>
    internal static class ParameterDescriptions
    {
        /// <summary>Each type's, and unit's, descriptions by "in:" or "out:" and the parameter's name; null for a type that cannot be made.</summary>
        private static readonly Dictionary<string, Dictionary<string, string>> Registered = new Dictionary<string, Dictionary<string, string>>();

        private static bool _watching;

        /// <summary>Refreshes every document opened from now on, and every object added to one.</summary>
        public static void Watch()
        {
            if (_watching || Instances.DocumentServer == null)
                return;
            _watching = true;
            Instances.DocumentServer.DocumentAdded += (server, document) =>
            {
                Refresh(document.Objects);
                document.ObjectsAdded += (sender, e) => Refresh(e.Objects);
            };
        }

        /// <summary>The Alpaca4d components among <paramref name="objects"/>, refreshed.</summary>
        public static void Refresh(IEnumerable<IGH_DocumentObject> objects)
        {
            foreach (var component in objects.OfType<GH_Component>())
                if (component.GetType().Assembly == typeof(ParameterDescriptions).Assembly)
                    Refresh(component);
        }

        /// <summary>Gives each parameter of <paramref name="component"/> the description its type registers for it now.</summary>
        public static void Refresh(GH_Component component)
        {
            try
            {
                var registered = Of(component);
                if (registered == null)
                    return;
                foreach (var param in component.Params.Input)
                    if (registered.TryGetValue("in:" + param.Name, out string text))
                        param.Description = text;
                foreach (var param in component.Params.Output)
                    if (registered.TryGetValue("out:" + param.Name, out string text))
                        param.Description = text;
            }
            catch (Exception)
            {
                // A description is not worth a document that fails to open.
            }
        }

        /// <summary>What a fresh component of <paramref name="component"/>'s type and unit registers, made once a type and unit.</summary>
        private static Dictionary<string, string> Of(GH_Component component)
        {
            string unit = (component as GH_SwitcherComponent)?.ActiveUnit?.Name;
            string key = component.GetType().FullName + "|" + unit;
            if (Registered.TryGetValue(key, out var known))
                return known;

            Dictionary<string, string> found = null;
            try
            {
                var fresh = (GH_Component)Activator.CreateInstance(component.GetType());
                if (unit != null && fresh is GH_SwitcherComponent switcher && switcher.ActiveUnit?.Name != unit)
                    switcher.SwitchUnit(unit, false, false);
                found = new Dictionary<string, string>();
                foreach (var param in fresh.Params.Input)
                    found["in:" + param.Name] = param.Description;
                foreach (var param in fresh.Params.Output)
                    found["out:" + param.Name] = param.Description;
            }
            catch (Exception)
            {
                found = null;
            }
            Registered[key] = found;
            return found;
        }
    }
}
