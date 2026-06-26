// File: ClipHistory/MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipHistory.Data;
using ClipHistory.Models;
using ClipHistory.Native;
using ClipHistory.Services;

namespace ClipHistory
{
    public partial class MainWindow : Window
    {
        private enum ViewMode { History, Template }

        private readonly HistoryRepository _repo;
        private readonly Settings _settings;
        private readonly string _settingsPath;

        private readonly ClipboardMonitor _clipboard = new ClipboardMonitor();
        private readonly HotkeyManager _hotkeys = new HotkeyManager();

        private ObservableCollection<DisplayItem> _items;
        private DispatcherTimer _searchDebounce;
        private string _pendingSearch;

        private int _cycleIndex = 0;
        private bool _suppressNextMonitor;
        private bool _cycleInProgress;

        private Point _dragStart;
        private DisplayItem _dragItem;

        private int _showHotkeyId = -1;
        private int _cycleHotkeyId = -1;

        private bool _isDialogOpen = false;
        private ViewMode _currentMode = ViewMode.History;
        private bool _showOnlyFavorites = false;

        private const int PageSize = 200;

        public MainWindow(HistoryRepository repo, Settings settings, string settingsPath)
        {
            _repo = repo;
            _settings = settings;
            _settingsPath = settingsPath;
            InitializeComponent();
        }

        public void InitializeHidden()
        {
            _clipboard.Attach();
            _clipboard.TextCopied += OnTextCopied;
            _hotkeys.Attach();
            RegisterHotkeys();

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _searchDebounce.Tick += SearchDebounce_Tick;
        }

        private void RegisterHotkeys()
        {
            if (_showHotkeyId != -1) _hotkeys.Unregister(_showHotkeyId);
            if (_cycleHotkeyId != -1) _hotkeys.Unregister(_cycleHotkeyId);

            _showHotkeyId = _hotkeys.Register((HotkeyModifiers)_settings.ShowModifiers, _settings.ShowKey, ShowAndActivate);
            _cycleHotkeyId = _hotkeys.Register((HotkeyModifiers)_settings.CycleModifiers, _settings.CycleKey, OnCycleHotkey);
        }

        // ================= フォーカス制御・座標計算 =================

        public void ShowAndActivate()
        {
            if (!IsVisible)
            {
                ReloadList();
                SetPositionToMouse();
                Show();
            }
            else
            {
                // 既に表示されている場合もマウス位置へ移動させる
                SetPositionToMouse();
            }
            WindowState = WindowState.Normal;
            Activate();
            SearchBox.Focus();
        }

        private void SetPositionToMouse()
        {
            var pt = System.Windows.Forms.Cursor.Position;
            var screen = System.Windows.Forms.Screen.FromPoint(pt);

            Matrix transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point ptDip = transform.Transform(new Point(pt.X, pt.Y));
            Point screenTopLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            Point screenBottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

            double left = ptDip.X;
            double top = ptDip.Y;

            if (left + Width > screenBottomRight.X) left = screenBottomRight.X - Width;
            if (top + Height > screenBottomRight.Y) top = screenBottomRight.Y - Height;
            if (left < screenTopLeft.X) left = screenTopLeft.X;
            if (top < screenTopLeft.Y) top = screenTopLeft.Y;

            Left = left;
            Top = top;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // ダイアログを開いていないのにフォーカスが外れた場合は非表示にする
            if (!_isDialogOpen && IsVisible)
            {
                HideAndRelease();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            HideAndRelease();
        }

        private void HideAndRelease()
        {
            Hide();
            HistoryList.ItemsSource = null;
            _items = null;
            SearchBox.Text = string.Empty;
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        // ================= タブ・データ読み込み =================

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _currentMode = TabTemplate.IsChecked == true ? ViewMode.Template : ViewMode.History;
            SearchBox.Text = "";
            ReloadList();
        }

        private void ReloadList()
        {
            List<DisplayItem> data;
            if (_currentMode == ViewMode.History)
            {
                data = string.IsNullOrEmpty(SearchBox.Text)
                    ? _repo.LoadHistoryPage(0, PageSize, _showOnlyFavorites)
                    : _repo.SearchHistory(SearchBox.Text, PageSize, _showOnlyFavorites);
            }
            else
            {
                data = _repo.LoadTemplates();
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    var kw = SearchBox.Text.ToLower();
                    data = data.Where(t => t.DisplayText.ToLower().Contains(kw) || t.FullText.ToLower().Contains(kw)).ToList();
                }
            }
            _items = new ObservableCollection<DisplayItem>(data);
            HistoryList.ItemsSource = _items;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pendingSearch = SearchBox.Text;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void SearchDebounce_Tick(object sender, EventArgs e)
        {
            _searchDebounce.Stop();
            if (IsVisible) ReloadList();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        // ================= クリップボード監視・順送り =================

        private void OnTextCopied(string text)
        {
            if (_suppressNextMonitor)
            {
                _suppressNextMonitor = false;
                return;
            }
            _cycleInProgress = false;
            _cycleIndex = 0;

            var added = _repo.AddHistory(text);

            if (IsVisible && _currentMode == ViewMode.History && _items != null && string.IsNullOrEmpty(SearchBox.Text))
            {
                // UI上の重複を削除してから先頭に追加
                var existing = _items.FirstOrDefault(i => i.FullText == text);
                if (existing != null) _items.Remove(existing);

                _items.Insert(0, added);
                if (_items.Count > PageSize) _items.RemoveAt(_items.Count - 1);
            }
        }

        private void OnCycleHotkey()
        {
            int total = _repo.CountHistory();
            if (total == 0) return;

            if (!_cycleInProgress) { _cycleInProgress = true; _cycleIndex = 0; }

            var item = _repo.GetNextForCycle(_cycleIndex);
            if (item == null)
            {
                _cycleIndex = 0;
                item = _repo.GetNextForCycle(_cycleIndex);
                if (item == null) return;
            }

            SetClipboardText(item.FullText);

            _cycleIndex++;
            if (_cycleIndex >= total) _cycleIndex = 0;
        }

        private void SetClipboardText(string text)
        {
            _suppressNextMonitor = true;
            try { Clipboard.SetText(text); } catch { _suppressNextMonitor = false; }
        }

        // ================= コピー・クリック処理 =================

        private void HistoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragItem = GetItemUnderMouse(e.OriginalSource as DependencyObject);
        }

        private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragItem == null) return;
            
            // マウスがあまり移動していなければシングルクリック（再コピー）と判定
            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                CopyItemAndHide(_dragItem);
            }
            _dragItem = null;
        }

        private void CopyItemAndHide(DisplayItem item)
        {
            _suppressNextMonitor = false; // 次回の監視を有効にしてトップへ移動させる
            _cycleInProgress = false;
            _cycleIndex = 0;
            try { Clipboard.SetText(item.FullText); } catch { }
            HideAndRelease();
        }

        // ================= D&D並べ替え =================

        private void HistoryList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
            if (!string.IsNullOrEmpty(SearchBox.Text)) return; // 検索中は並べ替え不可

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

            var ids = _items.Select(i => i.Id).ToList();
            if (_currentMode == ViewMode.History) _repo.ReorderHistory(ids);
            else _repo.ReorderTemplates(ids);

            for (int i = 0; i < _items.Count; i++) _items[i].SortOrder = i;
            _dragItem = null;
        }

        private DisplayItem GetItemUnderMouse(DependencyObject src)
        {
            while (src != null && !(src is ListBoxItem))
                src = VisualTreeHelper.GetParent(src);
            return (src as ListBoxItem)?.DataContext as DisplayItem;
        }

        // ================= コンテキストメニュー =================

        private void HistoryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = HistoryList.ContextMenu;
            menu.Items.Clear();
            var selectedItem = HistoryList.SelectedItem as DisplayItem;

            if (_currentMode == ViewMode.History)
            {
                var favItem = new MenuItem { Header = "お気に入りのみ表示", IsCheckable = true, IsChecked = _showOnlyFavorites };
                favItem.Click += (s, args) => { _showOnlyFavorites = favItem.IsChecked; ReloadList(); };
                menu.Items.Add(favItem);
                menu.Items.Add(new Separator());

                if (selectedItem != null)
                {
                    menu.Items.Add(CreateMenu("編集", MenuEdit_Click));
                    menu.Items.Add(CreateMenu("お気に入り登録/解除", MenuFavorite_Click));
                    menu.Items.Add(CreateMenu("削除", MenuDelete_Click));
                }
            }
            else
            {
                menu.Items.Add(CreateMenu("定型文を新規追加", MenuAddTemplate_Click));
                if (selectedItem != null)
                {
                    menu.Items.Add(new Separator());
                    menu.Items.Add(CreateMenu("編集", MenuEditTemplate_Click));
                    menu.Items.Add(CreateMenu("削除", MenuDeleteTemplate_Click));
                }
            }
        }

        private MenuItem CreateMenu(string header, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += handler;
            return item;
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            OpenDialog(() =>
            {
                var dlg = new EditDialog(item.FullText) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    item.FullText = dlg.EditedText;
                    item.DisplayText = dlg.EditedText;
                    _repo.UpdateHistoryText(item.Id, dlg.EditedText);
                    if (dlg.CopyRequested) CopyItemAndHide(item);
                }
            });
        }

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            item.IsFavorite = !item.IsFavorite;
            _repo.SetHistoryFavorite(item.Id, item.IsFavorite);
            if (_showOnlyFavorites && !item.IsFavorite) _items.Remove(item);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            _repo.DeleteHistory(item.Id);
            _items.Remove(item);
        }

        private void MenuAddTemplate_Click(object sender, RoutedEventArgs e)
        {
            OpenDialog(() =>
            {
                var dlg = new TemplateEditDialog("", "") { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    var added = _repo.AddTemplate(dlg.TemplateTitle, dlg.TemplateText);
                    if (string.IsNullOrEmpty(SearchBox.Text)) _items.Add(added);
                }
            });
        }

        private void MenuEditTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            OpenDialog(() =>
            {
                var dlg = new TemplateEditDialog(item.DisplayText, item.FullText) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    item.DisplayText = dlg.TemplateTitle;
                    item.FullText = dlg.TemplateText;
                    _repo.UpdateTemplate(item.Id, item.DisplayText, item.FullText);
                    if (dlg.CopyRequested) CopyItemAndHide(item);
                }
            });
        }

        private void MenuDeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            _repo.DeleteTemplate(item.Id);
            _items.Remove(item);
        }

        // ================= その他 =================

        public void OpenSettings()
        {
            OpenDialog(() =>
            {
                var dlg = new SettingsDialog(_settings) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    SettingsManager.Save(_settingsPath, _settings);
                    RegisterHotkeys();
                }
            });
        }

        private void OpenDialog(Action action)
        {
            _isDialogOpen = true;
            try { action(); }
            finally { _isDialogOpen = false; }
        }

        public void DisposeResources()
        {
            _clipboard.Dispose();
            _hotkeys.Dispose();
        }
    }
}
