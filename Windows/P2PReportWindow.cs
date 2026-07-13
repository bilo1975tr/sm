using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StreamMesh.Windows
{
    public class P2PReportWindow : Window
    {
        public P2PReportWindow(string title, string reportText)
        {
            Title = title;
            Width = 600;
            Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var textBox = new TextBox
            {
                Text = reportText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8")),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(10),
                Padding = new Thickness(8),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(textBox, 0);
            grid.Children.Add(textBox);
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };
            
            var copyButton = new Button
            {
                Content = "Raporu Kopyala",
                Width = 130,
                Height = 32,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0ea5e9")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold
            };
            copyButton.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(reportText);
                    MessageBox.Show("Rapor başarıyla panoya kopyalandı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Panoya kopyalama başarısız: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            
            var closeButton = new Button
            {
                Content = "Kapat",
                Width = 90,
                Height = 32,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            closeButton.Click += (s, e) => Close();
            
            buttonPanel.Children.Add(copyButton);
            buttonPanel.Children.Add(closeButton);
            
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);
            
            Content = grid;
        }
    }
}
