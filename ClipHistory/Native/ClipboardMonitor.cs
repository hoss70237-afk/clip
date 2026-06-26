// File: ClipHistory/Native/ClipboardMonitor.cs
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipHistory.Native
{
    /// <summary>
    /// AddClipboardFormatListener による完全イベント駆動監視。
    /// ポーリング・タイマー・常駐スレッドを一切使用しない。
    /// </summary>
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

        /// <summary>テキストがコピーされた時に発火（テキスト内容を渡す）</summary>
        public event Action<string> TextCopied;

        public void Attach(Window window)
        {
            _hwnd = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);
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
                // テキスト形式のみを対象とする。画像/HTML/ファイルは無視。
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
        }
    }
}
