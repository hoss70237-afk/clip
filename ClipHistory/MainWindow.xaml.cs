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
        private bool _suppressComboEvent;

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

            // 起動時に設定からウィンドウサイズを復元
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;

            // サイズが変更されたら設定クラスに記録（終了時に保存される）
            SizeChanged += (s, e) =>
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            };
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
                TabHistory.IsChecked = true; 
                ReloadList();
                SetPositionToMouse();
                Show();
            }
            else
            {
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
            if (!_isDialogOpen && IsVisible) HideAndRelease();
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

            if (_currentMode == ViewMode.History)
            {
                SearchBox.Visibility = Visibility.Visible;
                BtnClearSearch.Visibility = Visibility.Visible;
                ComboTemplateSet.Visibility = Visibility.Collapsed;
                BtnDeleteSet.Visibility = Visibility.Collapsed;
                SearchBox.Text = "";
                ReloadList();
            }
            else
            {
                SearchBox.Visibility = Visibility.Collapsed;
                BtnClearSearch.Visibility = Visibility.Collapsed;
                ComboTemplateSet.Visibility = Visibility.Visible;
                BtnDeleteSet.Visibility = Visibility.Visible;
                LoadTemplateSets();
            }
        }

        private void LoadTemplateSets()
        {
            var sets = _repo.LoadTemplateSets();
            sets.Add(new TemplateSet { Id = -1, Name = "＋ 新規セット作成" });

            _suppressComboEvent = true;
            ComboTemplateSet.ItemsSource = sets;
            if (sets.Count > 1) ComboTemplateSet.SelectedIndex = 0;
            else ComboTemplateSet.SelectedIndex = -1;
            _suppressComboEvent = false;

            ReloadList();
        }

        private void ComboTemplateSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressComboEvent) return;

            if (ComboTemplateSet.SelectedItem is TemplateSet selected && selected.Id == -1)
            {
                OpenDialog(() =>
                {
                    var dlg = new InputBoxDialog("新規セット作成", "セット名を入力してください:") { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
                    {
                        var newSet = _repo.AddTemplateSet(dlg.InputText);
                        LoadTemplateSets();
                        _suppressComboEvent = true;
                        foreach (TemplateSet s in ComboTemplateSet.Items)
                            if (s.Id == newSet.Id) { ComboTemplateSet.SelectedItem = s; break; }
                        _suppressComboEvent = false;
                        ReloadList();
                    }
                    else
                    {
                        LoadTemplateSets();
                    }
                });
            }
            else
            {
                ReloadList();
            }
        }

        private void DeleteSet_Click(object sender, RoutedEventArgs e)
        {
            if (ComboTemplateSet.SelectedItem is TemplateSet selected && selected.Id != -1)
            {
                _repo.DeleteTemplateSet(selected.Id);
                LoadTemplateSets();
            }
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
                data = new List<DisplayItem>();
                if (ComboTemplateSet.SelectedItem is TemplateSet selected && selected.Id != -1)
                {
                    data = _repo.LoadTemplatesBySet(selected.Id);
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
            if (_suppressNextMonitor) { _suppressNextMonitor = false; return; }
            _cycleInProgress = false; _cycleIndex = 0;

            var added = _repo.AddHistory(text);

            if (IsVisible && _currentMode == ViewMode.History && _items != null && string.IsNullOrEmpty(SearchBox.Text))
            {
                var existing = _items.FirstOrDefault(i => i.FullText == text);
                if (existing != null) _items.Remove(existing);

                _items.Insert(0, added);
                if (_items.Count > PageSize) _items.RemoveAt(_items.Count - 1);
            }
        }

        private void OnCycleHotkey()
        {
            int total = _repo.CountHistory();
            if (total <= 1) return; // 1件以下なら順送りの意味がないため何もしない

            if (!_cycleInProgress) 
            { 
                _cycleInProgress = true; 
                _cycleIndex = 1; // コピー済みの最新(0番目)を飛ばして、2つ目の履歴からスタートする
            }

            var item = _repo.GetNextForCycle(_cycleIndex);
            if (item == null)
            {
                _cycleIndex = 0;
                item = _repo.GetNextForCycle(_cycleIndex);
                if (item == null) return;
            }

            SetClipboardText(item.FullText);
            _cycleIndex++;
            if (_cycleIndex >= total) _cycleIndex = 0; // 末尾まで行ったら最新(0)へ循環
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
            _suppressNextMonitor = false; 
            _cycleInProgress = false;
            _cycleIndex = 0;
            try { Clipboard.SetText(item.FullText); } catch { }
            HideAndRelease();
        }

        // ================= D&D並べ替え =================

        private void HistoryList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;

            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

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
                    var sets = _repo.LoadTemplateSets();
                    if (sets.Count > 0)
                    {
                        var addToTplMenu = new MenuItem { Header = "定型文に登録" };
                        foreach (var set in sets)
                        {
                            var setItem = new MenuItem { Header = set.Name };
                            setItem.Click += (s, args) => _repo.AddTemplateItem(set.Id, selectedItem.FullText);
                            addToTplMenu.Items.Add(setItem);
                        }
                        menu.Items.Add(addToTplMenu);
                        menu.Items.Add(new Separator());
                    }

                    menu.Items.Add(CreateMenu("編集", MenuEdit_Click));
                    menu.Items.Add(CreateMenu("お気に入り登録/解除", MenuFavorite_Click));
                    menu.Items.Add(CreateMenu("削除", MenuDelete_Click));
                }
            }
            else
            {
                if (ComboTemplateSet.SelectedItem is TemplateSet selected && selected.Id != -1)
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
            if (!(ComboTemplateSet.SelectedItem is TemplateSet selected && selected.Id != -1)) return;
            OpenDialog(() =>
            {
                var dlg = new EditDialog("") { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.EditedText))
                {
                    var added = _repo.AddTemplateItem(selected.Id, dlg.EditedText);
                    _items.Add(added);
                }
            });
        }

        private void MenuEditTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            OpenDialog(() =>
            {
                var dlg = new EditDialog(item.FullText) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    item.FullText = dlg.EditedText;
                    _repo.UpdateTemplateItem(item.Id, item.FullText);
                    if (dlg.CopyRequested) CopyItemAndHide(item);
                }
            });
        }

        private void MenuDeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryList.SelectedItem is DisplayItem item)) return;
            _repo.DeleteTemplateItem(item.Id);
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
