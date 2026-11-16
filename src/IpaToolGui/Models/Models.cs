using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IpaToolGui.Services;

namespace IpaToolGui.Models
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Raise(name);
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Re-evaluates every binding on this object (after bulk updates or a language change).</summary>
        public void RefreshAll() => Raise(string.Empty);
    }

    public sealed class AppItem : Observable
    {
        public long Id { get; set; }
        public string BundleId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public double Price { get; set; }
        public string FormattedPrice { get; set; }
        public string Platform { get; set; } = "";
        public List<string> Platforms { get; set; } = new List<string>();
        public DateTime? PurchaseDate { get; set; }

        // Store metadata from the public iTunes Lookup API.
        public bool HasStoreInfo { get; set; }
        public string Seller { get; set; }
        public string Genre { get; set; }
        public long SizeBytes { get; set; }
        public double Rating { get; set; }
        public long RatingCount { get; set; }
        public string MinOs { get; set; }
        public string ContentRating { get; set; }
        public string StoreUrl { get; set; }
        public string Description { get; set; }
        public string ReleaseNotes { get; set; }
        public DateTime? CurrentVersionDate { get; set; }
        public List<string> Screenshots { get; set; } = new List<string>();
        public string IconUrl { get; private set; }
        public string LargeIconUrl { get; set; }

        private ImageSource _icon;
        public ImageSource IconImage { get => _icon; private set => Set(ref _icon, value); }

        public void SetIcon(string url)
        {
            if (string.IsNullOrEmpty(url) || url == IconUrl) return;
            IconUrl = url;
            IconImage = Images.FromUrl(url, 160);
        }

        private static string ShortDate(DateTime d) => d.ToLocalTime().ToString("d", Loc.Instance.Culture);

        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : (BundleId ?? Loc.F("app_id", Id));
        public string Initial => DisplayName.Length > 0 ? DisplayName.Substring(0, 1).ToUpperInvariant() : "?";
        public string Subtitle
        {
            get
            {
                var parts = new[] { Seller, Genre }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (parts.Count == 0) return Id > 0 ? Loc.F("app_id", Id) : Loc.T("app_no_info");
                return string.Join("  ·  ", parts);
            }
        }
        public bool HasBundle => !string.IsNullOrEmpty(BundleId);
        public string BundleText => HasBundle ? BundleId : Loc.F("app_id", Id);
        public bool HasVersion => !string.IsNullOrEmpty(Version);
        public string VersionText => "v" + Version;
        public bool HasSize => SizeBytes > 0;
        public string SizeText => Format.Size(SizeBytes);
        public bool IsFree => Price <= 0;
        public string PriceText => IsFree ? Loc.T("app_free") : (!string.IsNullOrEmpty(FormattedPrice) ? FormattedPrice : Price.ToString("0.##", CultureInfo.InvariantCulture));
        public bool HasRating => RatingCount > 0;
        public string RatingText => $"★ {Rating.ToString("0.0", CultureInfo.InvariantCulture)}  ({Format.Count(RatingCount)})";
        public bool HasPurchaseDate => PurchaseDate.HasValue;
        public string PurchaseDateText => PurchaseDate.HasValue ? Loc.F("app_acquired", ShortDate(PurchaseDate.Value)) : "";
        public bool HasMinOs => !string.IsNullOrEmpty(MinOs);
        public string MinOsText => (Platform == "macos" ? "macOS " : "iOS ") + MinOs + "+";
        public bool HasStoreUrl => !string.IsNullOrEmpty(StoreUrl);
        public bool HasScreenshots => Screenshots.Count > 0;
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
        public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);
        public string ReleaseNotesTitle => CurrentVersionDate.HasValue
            ? Loc.F("app_whats_new_ver", Version, ShortDate(CurrentVersionDate.Value))
            : Loc.T("app_whats_new");
    }

    public enum JobState { Queued, Running, Done, Failed, Cancelled }

    public sealed class DownloadJob : Observable
    {
        public DownloadJob(AppItem app, string versionId, string versionLabel)
        {
            App = app;
            VersionId = versionId;
            VersionLabel = versionLabel;
            _status = Loc.T("job_waiting");
        }

        public AppItem App { get; }
        public string VersionId { get; }
        public string VersionLabel { get; }
        public CancellationTokenSource Cts { get; set; }
        public string Detail => $"{App.BundleText}  ·  {VersionLabel}";

        private JobState _state;
        public JobState State
        {
            get => _state;
            set
            {
                Set(ref _state, value);
                Raise(nameof(StateLabel));
                Raise(nameof(CanCancel));
                Raise(nameof(CanRetry));
                Raise(nameof(CanOpen));
                Raise(nameof(CanRemove));
                Raise(nameof(ShowProgress));
            }
        }

        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case JobState.Queued: return Loc.T("job_state_queued");
                    case JobState.Running: return Loc.T("job_state_running");
                    case JobState.Done: return Loc.T("job_state_done");
                    case JobState.Failed: return Loc.T("job_state_failed");
                    default: return Loc.T("job_state_cancelled");
                }
            }
        }

        public bool CanCancel => State == JobState.Queued || State == JobState.Running;
        public bool CanRetry => State == JobState.Failed || State == JobState.Cancelled;
        public bool CanOpen => State == JobState.Done;
        public bool CanRemove => State != JobState.Running;
        public bool ShowProgress => State == JobState.Running || State == JobState.Queued;

        private double _progress;
        public double Progress { get => _progress; set => Set(ref _progress, value); }

        private bool _indeterminate = true;
        public bool Indeterminate { get => _indeterminate; set => Set(ref _indeterminate, value); }

        private string _status;
        public string StatusText { get => _status; set => Set(ref _status, value); }

        public string OutputPath { get; set; }
    }

    public sealed class VersionItem : Observable
    {
        public string Id { get; set; }
        public bool IsLatest { get; set; }

        private string _display;
        public string DisplayVersion { get => _display; set { Set(ref _display, value); Raise(nameof(Title)); } }

        private DateTime? _releaseDate;
        public DateTime? ReleaseDate { get => _releaseDate; set { Set(ref _releaseDate, value); Raise(nameof(DateText)); } }

        private bool _failed;
        public bool Failed { get => _failed; set { Set(ref _failed, value); Raise(nameof(DateText)); } }

        private bool _loading;
        public bool Loading { get => _loading; set { Set(ref _loading, value); Raise(nameof(Title)); Raise(nameof(CanLoad)); } }

        private bool _loaded;
        public bool Loaded { get => _loaded; set { Set(ref _loaded, value); Raise(nameof(CanLoad)); } }

        public bool CanLoad => !Loading && !Loaded;
        public string Title => Loading ? Loc.T("version_loading") : (string.IsNullOrEmpty(DisplayVersion) ? Loc.T("version_unknown") : "v" + DisplayVersion);
        public string DateText => Failed
            ? Loc.T("version_info_failed")
            : ReleaseDate.HasValue ? Loc.F("version_released", ReleaseDate.Value.ToLocalTime().ToString("d", Loc.Instance.Culture)) : "";
        public string IdText => "ID " + Id;
        public string Label => string.IsNullOrEmpty(DisplayVersion) ? Loc.F("version_label_id", Id) : "v" + DisplayVersion;
    }

    public static class Images
    {
        public static ImageSource FromUrl(string url, int decodeWidth = 0, int decodeHeight = 0)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(url);
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                if (decodeHeight > 0) bi.DecodePixelHeight = decodeHeight;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                return bi;
            }
            catch
            {
                return null;
            }
        }
    }
}
