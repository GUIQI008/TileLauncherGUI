using System.Windows;

namespace TileLauncherGUI
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow()
        {
            InitializeComponent();
        }

        public void UpdateProgress(double percentage)
        {
            pbDownload.Value = percentage;
            txtProgress.Text = $"{percentage:F0}%";
        }
    }
}