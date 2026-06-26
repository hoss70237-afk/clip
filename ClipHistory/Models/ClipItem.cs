// File: ClipHistory/Models/ClipItem.cs
using System;
using System.ComponentModel;

namespace ClipHistory.Models
{
    /// <summary>
    /// 履歴1件を表すモデル。UI表示時のみ遅延ロードされる。
    /// メモリ削減のため不要なメタデータは保持しない。
    /// </summary>
    public sealed class ClipItem : INotifyPropertyChanged
    {
        private string _text;
        private int _sortOrder;
        private bool _isFavorite;

        public long Id { get; set; }

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(nameof(Text)); } }
        }

        public int SortOrder
        {
            get => _sortOrder;
            set { if (_sortOrder != value) { _sortOrder = value; OnPropertyChanged(nameof(SortOrder)); } }
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(nameof(IsFavorite)); } }
        }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
