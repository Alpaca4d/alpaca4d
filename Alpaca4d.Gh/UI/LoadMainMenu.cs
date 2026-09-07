using System.Threading;
using System.Windows.Forms;

using Alpaca4d;
using Alpaca4d.UI;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System.Diagnostics;
using System.Security.Policy;
using System.IO;
using System.Reflection;
using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace Alpaca4d.Menu
{
    public class MenuLoad
    {
        private static ToolStripMenuItem alpacaMenu;

        internal static void OnStartup(GH_Canvas canvas)
        {
            alpacaMenu = new ToolStripMenuItem("Alpaca4d")
            {
                Name = "Alpaca4d",
            };

            PopulateSub(alpacaMenu);

            GH_DocumentEditor editor = null;

            while (editor == null)
            {
                editor = Grasshopper.Instances.DocumentEditor;
                Thread.Sleep(321);
            }

            if (!editor.MainMenuStrip.Items.ContainsKey("Alpaca4d"))
            {
                editor.MainMenuStrip.Items.Add(alpacaMenu);
            }
            else
            {
                alpacaMenu = (ToolStripMenuItem)editor.MainMenuStrip.Items["Alpaca4d"];
                lock (alpacaMenu)
                {
                    alpacaMenu.DropDown.Items.Add(new ToolStripSeparator());
                    PopulateSub(alpacaMenu);
                }
            }

            Grasshopper.Instances.CanvasCreated -= OnStartup;
        }

        private static void PopulateSub(ToolStripMenuItem menuItem)
        {
            // Add Documentation
            menuItem.DropDown.Items.Add("Documentation", Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_, 
                (sender, e) => OpenBrowser(sender, e, "https://alpaca4d.gitbook.io/docs"));

            menuItem.DropDown.Items.Add("Examples", Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_, 
                (sender, e) => OpenBrowser(sender, e, "https://alpaca4d.gitbook.io/docs/examples"));

            menuItem.DropDown.Items.Add(new ToolStripSeparator());
            //----------------------

            // Add Settings
            ToolStripMenuItem subMenuSettings = new ToolStripMenuItem("Settings");
            subMenuSettings.DropDown.Items.Add("Download OpenSees...", Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_,
                (sender, e) => OpenBrowser(sender, e, "https://opensees.berkeley.edu/OpenSees/user/download.php"));
            subMenuSettings.DropDown.Items.Add(new ToolStripSeparator());
            subMenuSettings.DropDown.Items.Add("Set OpenSees Executable...", null, SetOpenSeesPath);
            subMenuSettings.DropDown.Items.Add("Clear OpenSees Path", null, ClearOpenSeesPath);
            menuItem.DropDown.Items.Add(subMenuSettings);

            menuItem.DropDown.Items.Add(new ToolStripSeparator());
            //----------------------

            // Add License Management
            ToolStripMenuItem subMenuLicense = new ToolStripMenuItem("License");
            subMenuLicense.DropDown.Items.Add("Activate License", null, ShowLicenseManagement);
            subMenuLicense.DropDown.Items.Add("License Terms", null,
                (sender, e) => OpenBrowser(sender, e, "https://alpaca4d.gitbook.io/docs/references/license"));

            menuItem.DropDown.Items.Add(subMenuLicense);

            // Add Help
            menuItem.DropDown.Items.Add("Help", Alpaca4d.Gh.Properties.Resources.External_Link__Alpaca4d_,(sender, e) => OpenBrowser(sender, e, "https://github.com/Alpaca4d/Alpaca4d/issues/new"));
        }

        // create an event handler that opens up a sub-window
        private static void OpenForm(object sender, System.EventArgs e)
        {
            //new Form1().ShowDialog();
        }

        // create an event handler that opens up a browser window
        private static void OpenBrowser(object sender, System.EventArgs e, string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        // create an event handler that opens up a Grasshopper template document 
        private static void OpenGhFile(object sender, EventArgs e, string fileName = null)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string folderAssembly = System.IO.Path.GetDirectoryName(assemblyPath);
            

            string templateFile = System.IO.Path.Combine(folderAssembly, "Assets", fileName);

            PasteGrasshopperFile(templateFile);
        }

        private static void PasteGrasshopperFile(string filePath)
        {
            GH_Document currentDoc = Grasshopper.Instances.ActiveCanvas.Document;
            if (currentDoc == null)
            {
                MessageBox.Show("No active Grasshopper document found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GH_Archive archive = new GH_Archive();
            archive.ReadFromFile(filePath);
            
            GH_Document newDoc = new GH_Document();
            if (archive.ExtractObject(newDoc, "Definition"))
            {
                currentDoc.MergeDocument(newDoc);
                Grasshopper.Instances.ActiveCanvas.Refresh();
                Grasshopper.Instances.ActiveCanvas.Update();
            }
            else
            {
                MessageBox.Show("Failed to extract the Grasshopper document from the archive.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // create an event handler for showing the license management form
        private static void ShowLicenseManagement(object sender, EventArgs e)
        {
            try
            {
                Alpaca4d.UI.LicenseManagementForm.ShowForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening license management: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void SetOpenSeesPath(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select OpenSees Executable";
                dialog.Filter = "Executable files (*)|*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    AlpacaSettings.OpenSeesPath = dialog.FileName;

                    // Clear the execute bit and macOS quarantine now rather than at the
                    // first analysis. This is the one moment we can say something useful
                    // about a solver we cannot unblock: the user is here, they just chose
                    // the file, and the alternative is an analysis that dies with no
                    // output whenever they next hit Run.
                    if (!Alpaca4d.Application.PrepareSolver(dialog.FileName, out string problem))
                    {
                        MessageBox.Show(problem, "Alpaca4d - OpenSees is blocked",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    MessageBox.Show($"OpenSees path set to:\n{dialog.FileName}", "Alpaca4d Settings",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private static void ClearOpenSeesPath(object sender, EventArgs e)
        {
            AlpacaSettings.OpenSeesPath = string.Empty;
            MessageBox.Show("OpenSees path has been cleared.", "Alpaca4d Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}