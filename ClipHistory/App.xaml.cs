// File: ClipHistory/App.xaml.cs
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using ClipHistory.Data;
using ClipHistory.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ClipHistory
{
    public partial class App : Application
    {
        private NotifyIcon _tray;
        private MainWindow _window;
        private HistoryRepository _repo;
        private Settings _settings;
        private string _dataDir;
        private string _settingsPath;

        private void App_Startup(object sender, StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                MessageBox.Show(ev.ExceptionObject.ToString(), "致命的なエラー", MessageBoxButton.OK, MessageBoxImage.Error);

            DispatcherUnhandledException += (s, ev) =>
            {
                MessageBox.Show(ev.Exception.ToString(), "アプリケーションエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                ev.Handled = true;
            };

            _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipHistory");
            Directory.CreateDirectory(_dataDir);

            string dbPath = Path.Combine(_dataDir, "history.db");
            _settingsPath = Path.Combine(_dataDir, "settings.ini");

            _repo = new HistoryRepository(dbPath);
            _settings = SettingsManager.Load(_settingsPath);

            _window = new MainWindow(_repo, _settings, _settingsPath);
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
            try { return SystemIcons.Application; }
            catch { return null; }
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            // 終了時にウィンドウサイズを含めた設定を保存
            if (_settings != null && !string.IsNullOrEmpty(_settingsPath))
            {
                SettingsManager.Save(_settingsPath, _settings);
            }

            _tray?.Dispose();
            _window?.DisposeResources();
            _repo?.Dispose();
        }
    }
}
