// File: ClipHistory/Services/Settings.cs
using ClipHistory.Native;

namespace ClipHistory.Services
{
    /// <summary>ユーザー設定。ホットキーは変更可能。</summary>
    public sealed class Settings
    {
        // 表示用ホットキー（既定: Ctrl+Shift+V）
        public uint ShowModifiers { get; set; } = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        public uint ShowKey { get; set; } = 0x56; // V

        // 順送りホットキー（既定: Ctrl+Shift+B）
        public uint CycleModifiers { get; set; } = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        public uint CycleKey { get; set; } = 0x42; // B
    }
}
