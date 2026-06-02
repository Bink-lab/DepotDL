using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DepotDL.GUI.Helpers;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.ViewModels
{
    public partial class StoreGameViewModel : ObservableObject, ISteamGameViewModel
    {
        public StoreGame Game { get; }

        [ObservableProperty] private BitmapImage? _headerImage;
        [ObservableProperty] private bool _isImageLoading = true;

        public string OwnersShort
        {
            get
            {
                var s = Game.Owners;
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                var parts = s.Split("..", StringSplitOptions.TrimEntries);
                if (parts.Length == 0) return s;
                var lower = parts[0].Replace(",", "").Trim();
                if (!long.TryParse(lower, out long n)) return s;
                if (n >= 1_000_000_000) return $"{n / 1_000_000_000}B+ owners";
                if (n >= 1_000_000) return $"{n / 1_000_000}M+ owners";
                if (n >= 1_000) return $"{n / 1_000}K+ owners";
                return $"{n}+ owners";
            }
        }

        public string ReviewRatio
        {
            get
            {
                int total = Game.Positive + Game.Negative;
                if (total == 0) return "No reviews";
                double pct = Game.Positive * 100.0 / total;
                string label = pct >= 80 ? "Very Positive"
                             : pct >= 70 ? "Positive"
                             : pct >= 40 ? "Mixed"
                             : "Negative";
                return $"{pct:F0}% {label}  ({total:N0} reviews)";
            }
        }

        public string PriceDisplay => PriceFormatter.Format(Game.Price, "Free");

        public StoreGameViewModel(StoreGame game) => Game = game;

        public Task LoadImageAsync(CancellationToken ct = default)
        {
            return ImageLoader.LoadGameImageAsync(this, Game.AppId, Game.HeaderImageUrl, ct);
        }
    }
}
