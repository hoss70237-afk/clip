// File: ClipHistory/InputBoxDialog.cs
using System.Windows;
using System.Windows.Controls;

namespace ClipHistory
{
    /// <summary>
    /// 定型文セット名などを入力するためのシンプルな1行ダイアログ
    /// </summary>
    public sealed class InputBoxDialog : Window
    {
        private readonly TextBox _box;
        public string InputText => _box.Text;

        public InputBoxDialog(string title, string message)
        {
            Title = title; Width = 350; Height = 140;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(lbl, 0); grid.Children.Add(lbl);

            _box = new TextBox { Text = "" };
            Grid.SetRow(_box, 1); grid.Children.Add(_box);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "キャンセル", Width = 70, IsCancel = true };

            ok.Click += (s, e) => { DialogResult = true; };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 2); grid.Children.Add(panel);

            Content = grid;
            Loaded += (s, e) => _box.Focus();
        }
    }
}
