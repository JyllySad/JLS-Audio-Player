using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JLS.Models
{
    public partial class SavedAlbum : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _artist = string.Empty;

        [ObservableProperty]
        private string _year = string.Empty;

        [ObservableProperty]
        private string _coverPath = string.Empty;

        [ObservableProperty]
        private string _primaryColorHex = "#1E1E1E";

        [ObservableProperty]
        private string _textColorHex = "#FFFFFF";

        [ObservableProperty]
        private string _lightColorHex = "#FFFFFF";

        [JsonIgnore]
        [ObservableProperty]
        private ObservableCollection<FileSystemItem> _items = new();

        public List<string> TrackPaths { get; set; } = new();

        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}