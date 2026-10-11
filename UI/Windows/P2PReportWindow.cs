using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StreamMesh.UI.Windows
{
    public class P2PReportWindow : Window
    {
        public P2PReportWindow(string title, string content)
        {
            Title = title;
            Width = 500;
            Height = 400;
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0f172a"));
            Foreground = System.Windows.Media.Brushes.White;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var txtTitle = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) };
            Grid.SetRow(txtTitle, 0);
            grid.Children.Add(txtTitle);

            var box = new System.Windows.Controls.TextBox
            {
                Text = content,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e293b")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155")),
                Padding = new Thickness(10)
            };
            Grid.SetRow(box, 1);
            grid.Children.Add(box);

            var btnStack = new StackPanel {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(btnStack, 2);
            grid.Children.Add(btnStack);

            var btnClose = new System.Windows.Controls.Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 35,
                Style = System.Windows.Application.Current.Resources["PrimaryButtonStyle"] as Style,
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"))
            };
            btnClose.Click += (s, e) => Close();
            btnStack.Children.Add(btnClose);

            Content = grid;
        }
    }
}
