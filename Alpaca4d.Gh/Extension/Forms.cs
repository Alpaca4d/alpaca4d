using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Eto;
using Eto.Forms;
using Eto.Drawing;
using System.Reflection;
using System.Diagnostics;

namespace Alpaca4d.Gh.Forms
{
    public partial class Advertise
    {
        public Advertise()
        {
            var windows = new Eto.Forms.Form();
            windows.Size = new Eto.Drawing.Size(800, 450);
            windows.Maximizable = false;
            windows.Minimizable = false;
            windows.Resizable = false;
            windows.Padding = new Eto.Drawing.Padding(5, 5, 5, 5);
            windows.BackgroundColor = new Eto.Drawing.Color(1, 1, 1);

            var centerScreen = Eto.Forms.Screen.DisplayBounds.Center;
            windows.Location = new Eto.Drawing.Point((int)centerScreen.X - windows.Size.Width / 2, (int)centerScreen.Y - windows.Size.Height / 2);
            windows.Icon = Eto.Drawing.Icon.FromResource("Alpaca4d.Gh.Resources.Tab.png");
            
            var imageView = new Eto.Forms.ImageView();
            imageView.Image = Eto.Drawing.Bitmap.FromResource("Alpaca4d.Gh.Resources.Sponsors.become a sponsor.png");

            windows.Content = imageView;
            windows.Show();
        }

        public static Advertise Default()
        {
            return new Advertise();
        }
    }

}