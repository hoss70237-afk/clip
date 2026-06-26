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

        // 連続発火防止用のキャッシュ
        private string _lastText;
        private DateTime _lastCopiedTime;

        public event Action<string> TextCopied;

        public void Attach()
        {
            var parameters = new HwndSourceParameters("ClipboardMonitorWindow")
            {
                Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3)
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
                        // 同じテキストが500ms以内に連続で来た場合は弾く（コピー時2重発火の抑制）
                        if (text == _lastText && (DateTime.Now - _lastCopiedTime).TotalMilliseconds < 500)
                            return;

                        _lastText = text;
                        _lastCopiedTime = DateTime.Now;
                        TextCopied?.Invoke(text);
                    }
                }
            }
            catch (Exception)
            {
                // ロック時無視
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
