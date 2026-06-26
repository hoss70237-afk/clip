// File: ClipHistory/EditDialog.cs
using System.Windows;
using System.Windows.Controls;

namespace ClipHistory
{
    /// <summary>
    /// 履歴編集用の軽量ダイアログ（XAML不要・コード生成で省メモリ）。
    /// </summary>
    public sealed class EditDialog : Window
    {
        private readonly TextBox _box;

        public string EditedText => _box.Text;

        public EditDialog(string text)
        {
            Title = "履歴の編集";
            Width = 400;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var grid = new Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _box = new TextBox
            {
                Text = text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(_box, 0);
            grid.Children.Add(_box);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var ok = new Button { Content = "保存", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "キャンセル", Width = 70, IsCancel = true };
            ok.Click += (s, e) => { DialogResult = true; };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);

            Content = grid;
        }
    }
}
