using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.Markup.Xaml;
using System;

namespace TVDGUI.Views
{
    public class MainWindow : Window
    {
        private TextBox MaxDownloadTextBox;
        private TextBox MaxChunkTextBox;
        private TextBox PathTextBox;

        private Button FindPathButton;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            MaxDownloadTextBox = this.FindControl<TextBox>("MaxDownloadTextBox");
            MaxChunkTextBox = this.FindControl<TextBox>("MaxChunkTextBox");
            PathTextBox = this.FindControl<TextBox>("PathTextBox");

            FindPathButton = this.FindControl<Button>("FindPathButton");
            FindPathButton.Click += FindPathButton_Click;

            /*
            MaxDownloadTextBox.GetObservable(TextBox.TextProperty).Subscribe(text =>
            {

            });
            */
        }

        private async void FindPathButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            var path = await dialog.ShowAsync(this);
            PathTextBox.Text = path;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
