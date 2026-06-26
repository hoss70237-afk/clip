// File: ClipHistory/Services/SettingsManager.cs
using System;
using System.IO;
using System.Text;

namespace ClipHistory.Services
{
    public static class SettingsManager
    {
        public static Settings Load(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;

            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                
                if (key == "WindowWidth" && double.TryParse(val, out double w)) s.WindowWidth = w;
                else if (key == "WindowHeight" && double.TryParse(val, out double h)) s.WindowHeight = h;
                else if (uint.TryParse(val, out uint v))
                {
                    switch (key)
                    {
                        case "ShowModifiers": s.ShowModifiers = v; break;
                        case "ShowKey": s.ShowKey = v; break;
                        case "CycleModifiers": s.CycleModifiers = v; break;
                        case "CycleKey": s.CycleKey = v; break;
                    }
                }
            }
            return s;
        }

        public static void Save(string path, Settings s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ShowModifiers=" + s.ShowModifiers);
            sb.AppendLine("ShowKey=" + s.ShowKey);
            sb.AppendLine("CycleModifiers=" + s.CycleModifiers);
            sb.AppendLine("CycleKey=" + s.CycleKey);
            sb.AppendLine("WindowWidth=" + s.WindowWidth);
            sb.AppendLine("WindowHeight=" + s.WindowHeight);
            File.WriteAllText(path, sb.ToString());
        }
    }
}
