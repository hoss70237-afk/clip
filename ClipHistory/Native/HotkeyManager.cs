// File: ClipHistory/Native/HotkeyManager.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipHistory.Native
{
    [Flags]
    public enum HotkeyModifiers : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008
    }

    /// <summary>
    /// RegisterHotKey によるグローバルホットキー。
    /// メッセージ駆動でCPUを消費しない。
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _hwnd;
        private HwndSource _source;
        private readonly Dictionary<int, Action> _handlers = new Dictionary<int, Action>();
        private int _nextId = 1;

        public void Attach(Window window)
        {
            _hwnd = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);
        }

        /// <returns>登録ID（変更時の再登録に使用）。失敗時は -1</returns>
        public int Register(HotkeyModifiers mods, uint virtualKey, Action handler)
        {
            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, (uint)mods, virtualKey))
            {
                _handlers[id] = handler;
                return id;
            }
            return -1;
        }

        public void Unregister(int id)
        {
            if (_handlers.Remove(id))
            {
                UnregisterHotKey(_hwnd, id);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_handlers.TryGetValue(id, out var act))
                {
                    act();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            foreach (var id in new List<int>(_handlers.Keys))
            {
                UnregisterHotKey(_hwnd, id);
            }
            _handlers.Clear();
            _source?.RemoveHook(WndProc);
        }
    }
}
