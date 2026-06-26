// File: ClipHistory/Services/Settings.cs
using ClipHistory.Native;

namespace ClipHistory.Services
{
    public sealed class Settings
    {
        public uint ShowModifiers { get; set; } = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        public uint ShowKey { get; set; } = 0x56; // V

        public uint CycleModifiers { get; set; } = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift);
        public uint CycleKey { get; set; } = 0x42; // B

        // ウィンドウサイズの記録用
        public double WindowWidth { get; set; } = 400;
        public double WindowHeight { get; set; } = 500;
    }
}
