// File: ClipHistory/App.xaml.cs
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using ClipHistory.Data;
using ClipHistory.Services;
using Application = System.Windows.Application;

namespace ClipHistory
{
    public partial class App : Application
    {
        private NotifyIcon _tray;
        private MainWindow _window;
        private HistoryRepository _repo;
        private Settings _settings;
        private string _dataDir;

        private void App_Startup(object sender, StartupEventArgs e)
        {
            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipHistory");
            Directory.CreateDirectory(_dataDir);

            string dbPath = Path.Combine(_dataDir, "history.db");
            string settingsPath = Path.Combine(_dataDir, "settings.ini");

            _repo = new HistoryRepository(dbPath);
            _settings = SettingsManager.Load(settingsPath);

            // ウィンドウは生成のみ（Showはしない）= 起動時は履歴を読み込まない
            _window = new MainWindow(_repo, _settings, settingsPath);
            // ハンドル確保のため非表示で初期化（メッセージ受信に必要）
            _window.InitializeHidden();

            SetupTray();
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Visible = true,
                Text = "ClipHistory"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("履歴を開く(&O)", null, (s, e) => _window.ShowAndActivate());
            menu.Items.Add("設定(&S)", null, (s, e) => _window.OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了(&X)", null, (s, e) => Shutdown());
            _tray.ContextMenuStrip = menu;

            _tray.DoubleClick += (s, e) => _window.ShowAndActivate();
        }

        private Icon LoadIcon()
        {
            // 埋め込みアイコンが無い場合はシステムアイコンを利用
            try { return SystemIcons.Application; }
            catch { return null; }
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            _tray?.Dispose();
            _window?.DisposeResources();
            _repo?.Dispose();
        }
    }
}
