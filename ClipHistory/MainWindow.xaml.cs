// File: ClipHistory/MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClipHistory.Data;
using ClipHistory.Models;
using ClipHistory.Native;
using ClipHistory.Services;

namespace ClipHistory
{
    public partial class MainWindow : Window
    {
        private readonly HistoryRepository _repo;
        private readonly Settings _settings;
        private readonly string _settingsPath;

        private readonly ClipboardMonitor _clipboard = new ClipboardMonitor();
        private readonly HotkeyManager _hotkeys = new HotkeyManager();

        // 表示用コレクション（必要分のみ保持。UI非表示時はクリアする）
        private ObservableCollection<ClipItem> _items;

        // インクリメンタル検索のデバウンス用
        private DispatcherTimer _searchDebounce;
        private string _pendingSearch;

        // 順送り状態
        private int _cycleIndex = 0;
        private bool _suppressNextMonitor; // 自分でSetTextした際の自己反映抑制
        private bool _cycleInProgress;

        // D&D
        private Point _dragStart;
        private ClipItem _dragItem;

        private int _showHotkeyId = -1;
        private int _cycleHotkeyId = -1;

        private const int PageSize = 200;

        public MainWindow(HistoryRepository repo, Settings settings, string settingsPath)
        {
            _repo = repo;
            _settings = settings;
            _settingsPath = settingsPath;
            InitializeComponent();
        }

        /// <summary>
        /// 表示せずにハンドルだけ確保し、クリップボード監視・ホットキーを開始。
        /// 起動時は履歴を一切ロードしない。
        /// </summary>
        public void InitializeHidden()
        {
            // ハンドル生成（非表示）
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.EnsureHandle();

            _clipboard.Attach(this);
            _clipboard.TextCopied += OnTextCopied;

            _hotkeys.Attach(this);
            RegisterHotkeys();

            _searchDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _searchDebounce.Tick += SearchDebounce_Tick;
        }

        private void RegisterHotkeys()
        {
            if (_showHotkeyId != -1) _hotkeys.Unregister(_showHotkeyId);
            if (_cycleHotkeyId != -1) _hotkeys.Unregister(_cycleHotkeyId);

            _showHotkeyId = _hotkeys.Register(
                (HotkeyModifiers)_settings.ShowModifiers, _settings.ShowKey, ShowAndActivate);

            _cycleHotkeyId = _hotkeys.Register(
                (HotkeyModifiers)_settings.CycleModifiers, _settings.CycleKey, OnCycleHotkey);
        }

        // ---------------- クリップボード監視 ----------------

        private void OnTextCopied(string text)
        {
            // 自分でSetTextした場合は履歴追加せず、順送り状態も維持
            if (_suppressNextMonitor)
            {
                _suppressNextMonitor = false;
                return;
            }

            // ユーザーが通常コピーした → 順送り状態を破棄
            _cycleInProgress = false;
            _cycleIndex = 0;

            var added = _repo.Add(text);

            // UI表示中なら先頭へ反映
            if (IsVisible && _items != null && string.IsNullOrEmpty(SearchBox.Text))
            {
                _items.Insert(0, added);
                if (_items.Count > PageSize)
                    _items.RemoveAt(_items.Count - 1);
            }
        }

        // ---------------- 順送りホットキー ----------------

        private void OnCycleHotkey()
        {
            int total = _repo.Count();
            if (total == 0) return;

            if (!_cycleInProgress)
            {
                _cycleInProgress = true;
                _cycleIndex = 0;
            }

            var item = _repo.GetNextForCycle(_cycleIndex);
            if (item == null)
            {
                // 循環: 先頭へ
                _cycleIndex = 0;
                item = _repo.GetNextForCycle(_cycleIndex);
                if (item == null) return;
            }

            SetClipboardText(item.Text);

            _cycleIndex++;
            if (_cycleIndex >= total) _cycleIndex = 0; // 末尾→先頭循環
        }

        /// <summary>自分でクリップボードへ書き込む（監視の自己反映を抑制）</summary>
        private void SetClipboardText(string text)
        {
            _suppressNextMonitor = true;
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                _suppressNextMonitor = false;
            }
        }

        // ---------------- 表示制御 ----------------

        public void ShowAndActivate()
        {
            if (!IsVisible)
            {
                LoadInitialPage();
                Show();
            }
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            SearchBox.Focus();
        }

        private void LoadInitialPage()
        {
            // 必要最小限（先頭ページ）のみ遅延ロード
            var page = _repo.LoadPage(0, PageSize);
            _items = new ObservableCollection<ClipItem>(page);
            HistoryList.ItemsSource = _items;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // ×ボタンでは終了せず非表示にしてメモリを解放
            e.Cancel = true;
            HideAndRelease();
        }

        private void HideAndRelease()
        {
            Hide();
            // UI非表示時に保持オブジェクトを解放
            HistoryList.ItemsSource = null;
            _items = null;
            SearchBox.Text = string.Empty;
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        // ---------------- 検索（インクリメンタル） ----------------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pendingSearch = SearchBox.Text;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void SearchDebounce_Tick(object sender, EventArgs e)
        {
            _searchDebounce.Stop();
            if (_items == null) return;

            List<ClipItem> result = string.IsNullOrEmpty(_pendingSearch)
                ? _repo.LoadPage(0, PageSize)
                : _repo.Search(_pendingSearch, PageSize);

            _items.Clear();
            foreach (var it in result) _items.Add(it);
        }

        // ---------------- 再コピー ----------------

        private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CopySelected();
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelected();

        private void CopySelected()
        {
            if (HistoryList.SelectedItem is ClipItem item)
            {
                // 通常コピー扱い: 順送りリセット + 最新履歴化したいので監視に任せる
                _suppressNextMonitor = false;
                _cycleInProgress = false;
                _cycleIndex = 0;
                try { Clipboard.SetText(item.Text); } catch { }
            }
        }

        // ---------------- 編集 ----------------

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is ClipItem item)) return;

            var dlg = new EditDialog(item.Text) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                item.Text = dlg.EditedText;
                _repo.UpdateText(item.Id, dlg.EditedText);
            }
        }

        // ---------------- お気に入り ----------------

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is ClipItem item)) return;
            item.IsFavorite = !item.IsFavorite;
            _repo.SetFavorite(item.Id, item.IsFavorite);
        }

        // ---------------- 削除 ----------------

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is ClipItem item)) return;
            _repo.Delete(item.Id);
            _items.Remove(item);
        }

        // ---------------- ドラッグ＆ドロップ並べ替え ----------------

        private void HistoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragItem = GetItemUnderMouse(e.OriginalSource as DependencyObject);
        }

        private void HistoryList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
            // 検索中は並べ替え不可（順序が不定になるため）
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;

            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DragDrop.DoDragDrop(HistoryList, _dragItem, DragDropEffects.Move);
        }

        private void HistoryList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void HistoryList_Drop(object sender, DragEventArgs e)
        {
            if (_dragItem == null || _items == null) return;

            var target = GetItemUnderMouse(e.OriginalSource as DependencyObject);
            if (target == null || ReferenceEquals(target, _dragItem)) { _dragItem = null; return; }

            int oldIndex = _items.IndexOf(_dragItem);
            int newIndex = _items.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) { _dragItem = null; return; }

            _items.Move(oldIndex, newIndex);

            // 並べ替え結果を永続化（現在ページ範囲の sort_order を再採番）
            PersistReorder();
            _dragItem = null;
        }

        private void PersistReorder()
        {
            // 先頭ページ全体を対象に再採番。検索無効時のみ実行。
            var ids = _items.Select(i => i.Id).ToList();
            _repo.Reorder(ids);
            // sort_order をUI側にも反映
            for (int i = 0; i < _items.Count; i++) _items[i].SortOrder = i;
        }

        private ClipItem GetItemUnderMouse(DependencyObject src)
        {
            while (src != null && !(src is ListBoxItem))
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            return (src as ListBoxItem)?.DataContext as ClipItem;
        }

        // ---------------- 設定 ----------------

        public void OpenSettings()
        {
            var dlg = new SettingsDialog(_settings) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                SettingsManager.Save(_settingsPath, _settings);
                RegisterHotkeys();
            }
        }

        public void DisposeResources()
        {
            _clipboard.Dispose();
            _hotkeys.Dispose();
        }
    }
}
