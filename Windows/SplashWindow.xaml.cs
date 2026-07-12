using System;
using System.Windows;

namespace StreamMesh.Windows
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string text, double progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = text;
                LoadingProgress.Value = progress;
            });
        }
    }
}
