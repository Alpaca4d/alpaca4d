using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Eto.Forms;
using Eto.Drawing;
using Alpaca4d.License;

namespace Alpaca4d.UI
{
    public class LicenseManagementForm : Form
    {
        private ListBox licenseListBox;
        private Label statusLabel;
        private Button addLicenseButton;
        private Button removeLicenseButton;
        private Button buyLicenseButton;
        
        private string licenseFilePath;
        private List<User> currentLicenses;

        private static LicenseManagementForm openForm;

        public LicenseManagementForm()
        {
            InitializeComponent();
            LoadLicenses();
        }

        private void InitializeComponent()
        {
            // Form properties
            Title = "Alpaca4d - License Management";
            Maximizable = false;
            Minimizable = true;
            Resizable = true;
            Topmost = true;
            Padding = new Padding(20);
            BackgroundColor = Eto.Drawing.Colors.White;
            
            // Let the form auto-size based on content - this handles DPI and platform differences automatically
            AutoSize = true;
            
            // Set minimum size to ensure the form is never too small
            MinimumSize = new Size(480, 400);

            // Set icon if available
            try
            {
                Icon = Icon.FromResource("Alpaca4d.Gh.Resources.Tab.png");
            }
            catch
            {
                // Icon not found, continue without it
            }

            // Initialize license file path
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string alpacaFolder = Path.GetDirectoryName(assemblyLocation);
            licenseFilePath = Path.Combine(alpacaFolder, "data.bin");

            // Create main layout
            Content = CreateMainLayout();
            
            // Center the form on screen after content is set (so auto-size has calculated dimensions)
            Shown += (sender, e) =>
            {
                var screen = Screen.FromPoint(PointFromScreen(new PointF(0, 0))) ?? Screen.PrimaryScreen;
                var screenBounds = screen.WorkingArea;
                Location = new Point(
                    (int)(screenBounds.Center.X - Width / 2),
                    (int)(screenBounds.Center.Y - Height / 2));
            };
        }

        private Control CreateMainLayout()
        {
            var layout = new TableLayout
            {
                Spacing = new Size(10, 10)
            };

            // Header section
            layout.Rows.Add(CreateHeaderSection());

            // License list section - make it expandable horizontally only
            var listRow = new TableRow();
            listRow.Cells.Add(CreateLicenseListSection());
            layout.Rows.Add(listRow);

            // Status section
            layout.Rows.Add(CreateStatusSection());

            // Button section
            layout.Rows.Add(CreateButtonSection());

            return layout;
        }

        private Control CreateHeaderSection()
        {
            var headerLayout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            // Title
            var titleLabel = new Label
            {
                Text = "License Management",
                Font = SystemFonts.Bold(16),
                TextAlignment = TextAlignment.Center,
                TextColor = Eto.Drawing.Colors.DarkBlue
            };
            headerLayout.Items.Add(titleLabel);

            // Subtitle
            var subtitleLabel = new Label
            {
                Text = "Manage your Alpaca4d licenses",
                Font = new Font(SystemFont.Default, 10),
                TextAlignment = TextAlignment.Center,
                TextColor = Eto.Drawing.Colors.Gray
            };
            headerLayout.Items.Add(subtitleLabel);

            return headerLayout;
        }

        private Control CreateLicenseListSection()
        {
            var listLayout = new TableLayout
            {
                Spacing = new Size(10, 10)
            };

            // List label
            var labelRow = new TableRow();
            var listLabel = new Label
            {
                Text = "Current Licenses:",
                Font = SystemFonts.Bold(12)
            };
            labelRow.Cells.Add(listLabel);
            listLayout.Rows.Add(labelRow);

            // License list box - fixed size ensures consistent appearance
            var listRow = new TableRow();
            licenseListBox = new ListBox
            {
                Size = new Size(400, 150),
                BackgroundColor = Eto.Drawing.Colors.LightGrey,
            };
            licenseListBox.SelectedIndexChanged += OnLicenseSelectionChanged;
            var listCell = new TableCell(licenseListBox);
            listCell.ScaleWidth = true;
            listRow.Cells.Add(listCell);
            listLayout.Rows.Add(listRow);

            return listLayout;
        }

        private Control CreateStatusSection()
        {
            var statusLayout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 5
            };

            // Status label
            statusLabel = new Label
            {
                Text = "Ready",
                Font = new Font(SystemFont.Default, 10),
                TextColor = Eto.Drawing.Colors.DarkGreen
            };
            statusLayout.Items.Add(statusLabel);

            // License file path info
            var pathLabel = new Label
            {
                Text = $"License file: {licenseFilePath}",
                Font = new Font(SystemFont.Default, 8),
                TextColor = Eto.Drawing.Colors.Gray
            };
            statusLayout.Items.Add(pathLabel);

            return statusLayout;
        }

        private Control CreateButtonSection()
        {
            var buttonLayout = new TableLayout
            {
                Spacing = new Size(10, 10)
            };

            // Single row with Add, Remove, and Buy License buttons
            var row1 = new TableRow();
            
            // Create buttons and add them to cells with proper scaling
            var addButtonCell = new TableCell(CreateAddLicenseButton());
            addButtonCell.ScaleWidth = true;
            row1.Cells.Add(addButtonCell);
            
            var removeButtonCell = new TableCell(CreateRemoveLicenseButton());
            removeButtonCell.ScaleWidth = true;
            row1.Cells.Add(removeButtonCell);
            
            var buyButtonCell = new TableCell(CreateBuyLicenseButton());
            buyButtonCell.ScaleWidth = true;
            row1.Cells.Add(buyButtonCell);
            
            buttonLayout.Rows.Add(row1);

            return buttonLayout;
        }

        private Button CreateAddLicenseButton()
        {
            addLicenseButton = new Button
            {
                Text = "Add License",
                BackgroundColor = Eto.Drawing.Colors.LightGreen,
                TextColor = Eto.Drawing.Colors.Black,
                MinimumSize = new Size(100, 35)
            };
            
            addLicenseButton.Click += OnAddLicenseClicked;
            return addLicenseButton;
        }

        private Button CreateRemoveLicenseButton()
        {
            removeLicenseButton = new Button
            {
                Text = "Remove License",
                BackgroundColor = Eto.Drawing.Colors.LightCoral,
                TextColor = Eto.Drawing.Colors.Black,
                MinimumSize = new Size(100, 35)
            };
            
            removeLicenseButton.Click += OnRemoveLicenseClicked;
            return removeLicenseButton;
        }

        private Button CreateBuyLicenseButton()
        {
            buyLicenseButton = new Button
            {
                Text = "Buy License",
                BackgroundColor = Eto.Drawing.Color.FromArgb(Palette.AlpacaDarkBlue.R, Palette.AlpacaDarkBlue.G, Palette.AlpacaDarkBlue.B),
                TextColor = Eto.Drawing.Colors.Black,
                MinimumSize = new Size(100, 35)
            };
            
            buyLicenseButton.Click += OnBuyLicenseClicked;
            return buyLicenseButton;
        }

        private void LoadLicenses()
        {
            try
            {
                licenseListBox.Items.Clear();
                currentLicenses = new List<User>();

                if (File.Exists(licenseFilePath))
                {
                    currentLicenses = Alpaca4d.License.License.DeserializeBinary(licenseFilePath);
                    
                    foreach (var license in currentLicenses)
                    {
                        string displayText = $"{license.user_name} - Expires: {license.expiring_date:yyyy-MM-dd}";
                        if (license.expiring_date < DateTime.Now)
                        {
                            displayText += " (EXPIRED)";
                        }
                        licenseListBox.Items.Add(displayText);
                    }

                    UpdateStatus($"Loaded {currentLicenses.Count} license(s)", Eto.Drawing.Colors.DarkGreen);
                }
                else
                {
                    UpdateStatus("No license file found", Eto.Drawing.Colors.Orange);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading licenses: {ex.Message}", Eto.Drawing.Colors.Red);
            }
        }

        private void OnLicenseSelectionChanged(object sender, EventArgs e)
        {
            // Remove button is always enabled now, no need to disable it
            // The validation will happen when the button is clicked
        }

        private void OnAddLicenseClicked(object sender, EventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "Select License File";
                openFileDialog.Filters.Add(new FileFilter("License files", "*.bin"));
                openFileDialog.Filters.Add(new FileFilter("All files", "*.*"));

                if (openFileDialog.ShowDialog(this) == DialogResult.Ok)
                {
                    string selectedFilePath = openFileDialog.FileName;
                    string targetDirectory = Path.GetDirectoryName(licenseFilePath);

                    // Ensure the target directory exists
                    if (!Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    // Copy the file to the target location
                    File.Copy(selectedFilePath, licenseFilePath, true);

                    // Verify the license is valid
                    if (Alpaca4d.License.License.IsValid)
                    {
                        UpdateStatus("License added successfully!", Eto.Drawing.Colors.DarkGreen);
                        LoadLicenses(); // Refresh the list
                    }
                    else
                    {
                        UpdateStatus("License file added but appears to be invalid or expired", Eto.Drawing.Colors.Orange);
                        LoadLicenses(); // Refresh the list anyway
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding license: {ex.Message}", Eto.Drawing.Colors.Red);
            }
        }

        private void OnRemoveLicenseClicked(object sender, EventArgs e)
        {
            if (licenseListBox.SelectedIndex < 0)
            {
                MessageBox.Show(
                    this,
                    "Please select a license from the list to remove.",
                    "No License Selected",
                    MessageBoxType.Information);
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    this, // Set the owner to this form
                    "Are you sure you want to remove the selected license?",
                    "Confirm License Removal",
                    MessageBoxType.Question);

                if (result == DialogResult.Yes)
                {
                    // Remove the license file
                    if (File.Exists(licenseFilePath))
                    {
                        File.Delete(licenseFilePath);
                        UpdateStatus("License removed successfully", Eto.Drawing.Colors.DarkGreen);
                        LoadLicenses(); // Refresh the list
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error removing license: {ex.Message}", Eto.Drawing.Colors.Red);
            }
        }

        private void OnBuyLicenseClicked(object sender, EventArgs e)
        {
            try
            {
                // Open the license purchase URL in the default browser
                string licenseUrl = "https://alpaca4d.github.io/buy.html";
                Process.Start(new ProcessStartInfo
                {
                    FileName = licenseUrl,
                    UseShellExecute = true
                });
                
                UpdateStatus("Opening license purchase page...", Eto.Drawing.Colors.DarkBlue);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening license purchase page: {ex.Message}", Eto.Drawing.Colors.Red);
            }
        }



        private void UpdateStatus(string message, Color color)
        {
            statusLabel.Text = message;
            statusLabel.TextColor = color;
        }

        /// <summary>
        /// Static method to create and show the license management form
        /// </summary>
        public static void ShowForm()
        {
            if (openForm != null && !openForm.IsDisposed)
            {
                openForm.BringToFront();
                return;
            }

            openForm = new LicenseManagementForm();
            openForm.Closed += (sender, e) => openForm = null;
            openForm.Show();
        }
    }
}