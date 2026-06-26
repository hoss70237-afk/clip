// File: ClipHistory/TemplateEditDialog.cs
using System.Windows;
using System.Windows.Controls;

namespace ClipHistory
{
    public sealed class TemplateEditDialog : Window
    {
        private readonly TextBox _titleBox;
        private readonly TextBox _textBox;

        public string TemplateTitle => _titleBox.Text;
        public string TemplateText => _textBox.Text;
        public bool CopyRequested { get; private set; }

        public TemplateEditDialog(string title, string text)
        {
            Title = string.IsNullOrEmpty(title) ? "定型文の追加" : "定型文の編集";
            Width = 450; Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var grid = new Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(CreateLabel("タイトル:", 0));
            _titleBox = new TextBox { Text = title, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(_titleBox, 1); grid.Children.Add(_titleBox);

            grid.Children.Add(CreateLabel("本文:", 2));
            _textBox = new TextBox
            {
                Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(_textBox, 3); grid.Children.Add(_textBox);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "保存", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var copyOk = new Button { Content = "コピーして保存", Width = 110, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "キャンセル", Width = 70, IsCancel = true };

            ok.Click += (s, e) => { DialogResult = true; };
            copyOk.Click += (s, e) => { CopyRequested = true; DialogResult = true; };

            panel.Children.Add(ok);
            panel.Children.Add(copyOk);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 4); grid.Children.Add(panel);

            Content = grid;
            Loaded += (s, e) => _titleBox.Focus();
        }

        private TextBlock CreateLabel(string text, int row)
        {
            var lbl = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(lbl, row);
            return lbl;
        }
    }
}
