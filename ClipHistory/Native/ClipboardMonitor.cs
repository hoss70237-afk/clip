// File: ClipHistory/Native/ClipboardMonitor.cs
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipHistory.Native
{
    public sealed class ClipboardMonitor : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private HwndSource _source;
        private IntPtr _hwnd;
        private bool _attached;

        public event Action<string> TextCopied;

        public void Attach()
        {
            // メッセージ受信用に独立したダミーウィンドウ（HWND_MESSAGE）を生成
            var parameters = new HwndSourceParameters("ClipboardMonitorWindow")
            {
                Width = 0, Height = 0, WindowStyle = 0,
                ParentWindow = new IntPtr(-3)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _hwnd = _source.Handle;
            
            _attached = AddClipboardFormatListener(_hwnd);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnClipboardUpdate()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        TextCopied?.Invoke(text);
                    }
                }
            }
            catch (Exception)
            {
                // クリップボードが他プロセスにロックされている場合は無視
            }
        }

        public void Dispose()
        {
            if (_attached && _hwnd != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_hwnd);
                _attached = false;
            }
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }
    }
}
