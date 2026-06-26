// File: ClipHistory/SettingsDialog.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipHistory.Native;
using ClipHistory.Services;

namespace ClipHistory
{
    /// <summary>
    /// ホットキー変更用の軽量ダイアログ。
    /// キー入力をそのままキャプチャして設定に反映する。
    /// </summary>
    public sealed class SettingsDialog : Window
    {
        private readonly Settings _settings;
        private readonly TextBox _showBox;
        private readonly TextBox _cycleBox;

        private uint _showMod, _showKey, _cycleMod, _cycleKey;

        public SettingsDialog(Settings settings)
        {
            _settings = settings;
            _showMod = settings.ShowModifiers; _showKey = settings.ShowKey;
            _cycleMod = settings.CycleModifiers; _cycleKey = settings.CycleKey;

            Title = "設定";
            Width = 360; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var grid = new Grid { Margin = new Thickness(10) };
            for (int i = 0; i < 4; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddLabel(grid, "履歴表示ホットキー:", 0);
            _showBox = AddCaptureBox(grid, 0, _showMod, _showKey,
                (m, k) => { _showMod = m; _showKey = k; });

            AddLabel(grid, "順送りホットキー:", 1);
            _cycleBox = AddCaptureBox(grid, 1, _cycleMod, _cycleKey,
                (m, k) => { _cycleMod = m; _cycleKey = k; });

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "キャンセル", Width = 70, IsCancel = true };
            ok.Click += (s, e) =>
            {
                _settings.ShowModifiers = _showMod; _settings.ShowKey = _showKey;
                _settings.CycleModifiers = _cycleMod; _settings.CycleKey = _cycleKey;
                DialogResult = true;
            };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 3);
            Grid.SetColumnSpan(panel, 2);
            grid.Children.Add(panel);

            Content = grid;
        }

        private void AddLabel(Grid grid, string text, int row)
        {
            var lbl = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
        }

        private TextBox AddCaptureBox(Grid grid, int row, uint mod, uint key,
            System.Action<uint, uint> onChanged)
        {
            var box = new TextBox
            {
                IsReadOnly = true,
                Margin = new Thickness(0, 4, 0, 4),
                Text = Describe(mod, key)
            };
            box.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl ||
                    k == Key.LeftShift || k == Key.RightShift ||
                    k == Key.LeftAlt || k == Key.RightAlt ||
                    k == Key.LWin || k == Key.RWin) return;

                uint m = 0;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) m |= (uint)HotkeyModifiers.Control;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) m |= (uint)HotkeyModifiers.Shift;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) m |= (uint)HotkeyModifiers.Alt;
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) m |= (uint)HotkeyModifiers.Win;

                uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                onChanged(m, vk);
                box.Text = Describe(m, vk);
            };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return box;
        }

        private static string Describe(uint mod, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mod & (uint)HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((mod & (uint)HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((mod & (uint)HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((mod & (uint)HotkeyModifiers.Win) != 0) parts.Add("Win");
            var key = KeyInterop.KeyFromVirtualKey((int)vk);
            parts.Add(key.ToString());
            return string.Join(" + ", parts);
        }
    }
}
