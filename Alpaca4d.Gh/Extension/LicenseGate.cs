using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What a component says and does when a missing license stops it.
    ///
    /// Two rules, both in <see cref="Alpaca4d.License.License"/>: an analysis is free up to
    /// <see cref="Alpaca4d.License.License.FreeElementLimit"/> elements, and a few components
    /// need a license whatever the model. This keeps the wording of the refusal in one place, and
    /// remembers who was refused so that a license added from the License window runs them again
    /// straight away, instead of leaving them red until something upstream changes.
    /// </summary>
    internal static class LicenseGate
    {
        private const string HowToLicense =
            "Add a license from Alpaca4d ▸ License ▸ Activate License, or buy one at https://alpaca4d.github.io/buy.html.";

        private static readonly List<WeakReference<GH_Component>> Refused = new List<WeakReference<GH_Component>>();

        /// <summary>For a component that needs a license whatever the model.</summary>
        public static bool Allows(GH_Component component, string name)
        {
            if (Alpaca4d.License.License.ValidateFeature(Alpaca4d.UI.LicenseManagementForm.ShowForm))
                return true;

            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{name} needs a license. {HowToLicense}");
            Remember(component);
            return false;
        }

        /// <summary>For an analysis, which is free up to the element limit.</summary>
        public static bool AllowsModel(GH_Component component, Alpaca4d.Model model)
        {
            if (Alpaca4d.License.License.ValidateLicense(model, false, Alpaca4d.UI.LicenseManagementForm.ShowForm))
                return true;

            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Without a license Alpaca4d analyses models of up to {Alpaca4d.License.License.FreeElementLimit} " +
                $"elements, and this one has {model.Elements.Count}. {HowToLicense}");
            Remember(component);
            return false;
        }

        /// <summary>
        /// Runs again every component a missing license stopped. Called once a license has been
        /// added. Scheduled rather than expired on the spot, because it is called from the License
        /// window and a solution cannot start in the middle of someone else's.
        /// </summary>
        public static void RerunRefused()
        {
            var components = Refused
                .Select(reference => reference.TryGetTarget(out var component) ? component : null)
                .Where(component => component != null)
                .Distinct()
                .ToList();
            Refused.Clear();

            foreach (var group in components.GroupBy(component => component.OnPingDocument()).Where(group => group.Key != null))
            {
                var waiting = group.ToList();
                group.Key.ScheduleSolution(5, document =>
                {
                    foreach (var component in waiting)
                        component.ExpireSolution(false);
                });
            }
        }

        private static void Remember(GH_Component component)
        {
            Refused.RemoveAll(reference => !reference.TryGetTarget(out var alive) || alive == component);
            Refused.Add(new WeakReference<GH_Component>(component));
        }
    }
}
