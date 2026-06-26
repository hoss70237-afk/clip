// File: ClipHistory/Models/DisplayItem.cs
using System;
using System.ComponentModel;

namespace ClipHistory.Models
{
    /// <summary>
    /// 履歴・定型文を統合して扱うモデル。
    /// </summary>
    public sealed class DisplayItem : INotifyPropertyChanged
    {
        private string _displayText;
        private string _fullText;
        private int _sortOrder;
        private bool _isFavorite;

        public long Id { get; set; }
        public bool IsTemplate { get; set; }

        public string DisplayText
        {
            get => _displayText;
            set { if (_displayText != value) { _displayText = value; OnPropertyChanged(nameof(DisplayText)); } }
        }

        public string FullText
        {
            get => _fullText;
            set { if (_fullText != value) { _fullText = value; OnPropertyChanged(nameof(FullText)); } }
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

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
