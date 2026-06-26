// File: ClipHistory/Services/SettingsManager.cs
using System;
using System.IO;
using System.Text;

namespace ClipHistory.Services
{
    /// <summary>
    /// 設定の読み書き。外部JSONライブラリに依存せず、
    /// 単純な key=value テキストで軽量に永続化する。
    /// </summary>
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
                if (!uint.TryParse(val, out uint v)) continue;

                switch (key)
                {
                    case "ShowModifiers": s.ShowModifiers = v; break;
                    case "ShowKey": s.ShowKey = v; break;
                    case "CycleModifiers": s.CycleModifiers = v; break;
                    case "CycleKey": s.CycleKey = v; break;
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
            File.WriteAllText(path, sb.ToString());
        }
    }
}
