// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Velopack;

namespace DepotDL.GUI
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
