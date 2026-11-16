using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IpaToolGui.Models;
using IpaToolGui.Services;
using Microsoft.Win32;

namespace IpaToolGui
{
    public partial class MainWindow : Window
    {
        private enum ToastKind { Info, Success, Warning, Error }

        private static readonly Regex Percent = new Regex(@"(\d{1,3})\s*%", RegexOptions.Compiled);
        private static readonly Regex Paren = new Regex(@"\(([^()]*)\)", RegexOptions.Compiled);
        private static readonly Regex BundlePattern = new Regex(@"^[A-Za-z0-9\-_]+(\.[A-Za-z0-9\-_]+)+$", RegexOptions.Compiled);
        private const int PurchasePageSize = 50;

        private readonly AppSettings _settings = AppSettings.Load();
        private readonly IpaTool _tool;
        private readonly ObservableCollection<AppItem> _searchItems = new ObservableCollection<AppItem>();
        private readonly ObservableCollection<AppItem> _purchaseItems = new ObservableCollection<AppItem>();
        private readonly ObservableCollection<DownloadJob> _jobs = new ObservableCollection<DownloadJob>();
        private readonly ObservableCollection<VersionItem> _versions = new ObservableCollection<VersionItem>();
        private readonly DispatcherTimer _toastTimer;

        private bool _loggedIn;
        private string _accountName;
        private string _accountEmail;
        private bool _queueRunning;
        private bool _updating;
        private bool _awaiting2fa;
        private bool _uiReady;
        private bool _applyingSettings;
        private CancellationTokenSource _loginCts;
        private CancellationTokenSource _versionsCts;
        private AppItem _versionsApp;
        private AppItem _detailApp;
        private ReleaseInfo _guiRelease;
        private int _purchasePage = 1;
        private int _purchasePages = 1;
        private int _purchaseTotal = -1;

        // Texts built from a key + arguments, rebuilt when the language changes.
        private string _toolStatusKey = "tool_status_checking";
        private object _toolStatusArg;
        private Func<string> _searchInfo;
        private Func<string> _versionsInfo;

        public MainWindow()
        {
            Loc.Instance.SetLanguage(_settings.Language);
            ThemeManager.Apply(_settings.Theme);

            InitializeComponent();

            _tool = new IpaTool(() => _settings.GetPassphrase());
            _tool.Output += line => Dispatcher.BeginInvoke(new Action(() => AppendLog(line)));

            SearchResults.ItemsSource = _searchItems;
            PurchaseResults.ItemsSource = _purchaseItems;
            DownloadList.ItemsSource = _jobs;
            VersionsList.ItemsSource = _versions;
            _jobs.CollectionChanged += (s, e) => UpdateDownloadBadge();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };

            PreviewKeyDown += Window_PreviewKeyDown;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += (s, e) => SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            Bind(LatestVerText, "ipatool_not_checked");
            ApplySettingsToUi();
            SetAccount(null, null);
            AccountEmail.Text = Loc.T("account_checking");
            SetLoginTexts();
            ApplyLanguageTexts();
        }

        private static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

        // =====================================================================
        // Startup
        // =====================================================================

        private void Window_SourceInitialized(object sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog(Loc.F("log_started", AppVersion, AppPaths.Root));
            Updater.CleanupOldFiles();
            _uiReady = true;

            if (!_tool.IsInstalled)
            {
                await RunSetupAsync();
            }
            else
            {
                UpdateToolVersionUi();
                await RefreshAccountAsync();
                if (_settings.AutoUpdate) await CheckForUpdatesAsync(auto: true);
                else SetToolStatus("tool_status_autooff", "#6B7086");
            }

            if (_settings.AutoUpdate) await CheckGuiUpdateAsync(auto: true);
        }

        private void ApplySettingsToUi()
        {
            _applyingSettings = true;
            try
            {
                FolderBox.Text = _settings.DownloadFolder;
                AutoPurchaseCheck.IsChecked = _settings.AutoPurchase;
                AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
                CountryBox.Text = _settings.Country;
                SelectPill(LanguagePanel, string.IsNullOrEmpty(_settings.Language) ? Loc.Auto : _settings.Language);
                SelectPill(ThemePanel, ThemeManager.Mode);
            }
            finally
            {
                _applyingSettings = false;
            }
        }

        private static void SelectPill(Panel panel, string tag)
        {
            foreach (var rb in panel.Children.OfType<RadioButton>())
                rb.IsChecked = (rb.Tag as string) == tag;
        }

        /// <summary>Binds a TextBlock to a localized string so it follows language changes.</summary>
        private static void Bind(TextBlock tb, string key) =>
            tb.SetBinding(TextBlock.TextProperty, new Binding("[" + key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay });

        /// <summary>Re-renders texts that are built in code (with arguments) after a language change.</summary>
        private void ApplyLanguageTexts()
        {
            DownloadFolderInfo.Text = Loc.F("downloads_saving_to", _settings.DownloadFolder);
            AboutText.Text = Loc.F("about_text", AppVersion);
            if (_guiRelease != null) GuiUpdateText.Text = Loc.F("about_update_available", _guiRelease.Version, AppVersion);
            if (_uiReady) SetAccount(_accountName, _accountEmail);
            UpdateToolVersionUi();
            ToolStatusText.Text = Loc.F(_toolStatusKey, _toolStatusArg);
            SetLoginTexts();
            UpdatePurchasePageText();
            if (_searchInfo != null) SearchResultInfo.Text = _searchInfo();
            if (_versionsInfo != null) VersionsInfo.Text = _versionsInfo();

            foreach (var item in _searchItems.Concat(_purchaseItems)) item.RefreshAll();
            foreach (var job in _jobs) job.RefreshAll();
            foreach (var v in _versions) v.RefreshAll();
            if (_detailApp != null && DetailOverlay.Visibility == Visibility.Visible) _ = ShowDetailAsync(_detailApp);
        }

        // =====================================================================
        // Appearance
        // =====================================================================

        private void Language_Checked(object sender, RoutedEventArgs e)
        {
            if (_applyingSettings || !IsLoaded) return;
            var code = (sender as RadioButton)?.Tag as string ?? Loc.Auto;
            _settings.Language = code;
            _settings.Save();
            Loc.Instance.SetLanguage(code);
            ApplyLanguageTexts();
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_applyingSettings || !IsLoaded) return;
            var mode = (sender as RadioButton)?.Tag as string ?? ThemeManager.System;
            _settings.Theme = mode;
            _settings.Save();
            ThemeManager.Apply(mode);
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (ThemeManager.Mode != ThemeManager.System) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ThemeManager.SystemPrefersDark() != ThemeManager.IsDark) ThemeManager.Apply(ThemeManager.System);
            }));
        }

        // =====================================================================
        // First run
        // =====================================================================

        private async Task RunSetupAsync()
        {
            SetupOverlay.Visibility = Visibility.Visible;
            SetupErrorBox.Visibility = Visibility.Collapsed;
            SetupButtons.Visibility = Visibility.Collapsed;
            SetupProgress.IsIndeterminate = true;
            SetupText.Text = Loc.T("setup_finding");
            _tool.Updating = true;
            try
            {
                var rel = await Updater.GetLatestAsync();
                LatestVerText.Text = rel.Version;
                SetupText.Text = Loc.F("setup_downloading", rel.Version, Updater.Arch);
                await Updater.InstallAsync(rel, new Progress<double>(p =>
                {
                    SetupProgress.IsIndeterminate = p < 0;
                    if (p >= 0) SetupProgress.Value = p * 100;
                }));
                _tool.Updating = false;
                SetupOverlay.Visibility = Visibility.Collapsed;
                UpdateToolVersionUi();
                SetToolStatus("tool_status_latest", "#3DD68C");
                ShowToast(Loc.F("toast_installed", rel.Version), ToastKind.Success);
                AppendLog(Loc.F("log_installed", rel.Version, AppPaths.BinDir));
                await RefreshAccountAsync();
            }
            catch (Exception ex)
            {
                _tool.Updating = false;
                SetupProgress.IsIndeterminate = false;
                SetupProgress.Value = 0;
                SetupText.Text = Loc.T("setup_failed");
                SetupErrorText.Text = ex.Message;
                SetupErrorBox.Visibility = Visibility.Visible;
                SetupButtons.Visibility = Visibility.Visible;
                AppendLog(Loc.F("log_install_error", ex));
            }
        }

        private async void SetupRetry_Click(object sender, RoutedEventArgs e) => await RunSetupAsync();

        private async void SetupPickExe_Click(object sender, RoutedEventArgs e)
        {
            if (_tool.RunningCount > 0)
            {
                ShowToast(Loc.T("toast_busy"), ToastKind.Warning);
                return;
            }

            var dlg = new OpenFileDialog { Title = Loc.T("pick_dialog_title"), Filter = "ipatool (*.exe)|*.exe" };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var version = Updater.InstallFromFile(dlg.FileName);
                SetupOverlay.Visibility = Visibility.Collapsed;
                UpdateToolVersionUi();
                SetToolStatus("tool_status_custom", "#F5A524");
                ShowToast(Loc.F("toast_using", version), ToastKind.Success);
                await RefreshAccountAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Loc.T("pick_invalid_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // =====================================================================
        // ipatool updates
        // =====================================================================

        private void UpdateToolVersionUi()
        {
            var v = Updater.InstalledVersion();
            ToolVersionText.Text = v == null ? Loc.T("tool_not_installed") : "ipatool " + v;
            InstalledVerText.Text = v ?? Loc.T("tool_not_installed");
        }

        private void SetToolStatus(string key, string color, object arg = null)
        {
            _toolStatusKey = key;
            _toolStatusArg = arg;
            ToolStatusText.Text = Loc.F(key, arg);
            ToolStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(auto: false);

        private async Task CheckForUpdatesAsync(bool auto)
        {
            if (_updating) return;
            _updating = true;
            UpdateNowButton.IsEnabled = false;
            SetToolStatus("tool_status_checking", "#6B7086");
            try
            {
                var rel = await Updater.GetLatestAsync();
                LatestVerText.Text = rel.Version;
                var installed = Updater.InstalledVersion();

                if (!Updater.IsNewer(rel.Version, installed))
                {
                    SetToolStatus("tool_status_latest", "#3DD68C");
                    if (!auto) ShowToast(Loc.F("toast_latest", installed), ToastKind.Success);
                    return;
                }

                if (_tool.RunningCount > 0)
                {
                    SetToolStatus("tool_status_available", "#F5A524", rel.Version);
                    ShowToast(Loc.F("toast_update_later", rel.Version), ToastKind.Info);
                    return;
                }

                SetToolStatus("tool_status_updating", "#5B78FF", rel.Version);
                UpdateProgress.Visibility = Visibility.Visible;
                UpdateProgress.IsIndeterminate = true;
                _tool.Updating = true;
                try
                {
                    await Updater.InstallAsync(rel, new Progress<double>(p =>
                    {
                        UpdateProgress.IsIndeterminate = p < 0;
                        if (p >= 0) UpdateProgress.Value = p * 100;
                    }));
                }
                finally
                {
                    _tool.Updating = false;
                }

                UpdateToolVersionUi();
                SetToolStatus("tool_status_latest", "#3DD68C");
                ShowToast(Loc.F("toast_updated", installed, rel.Version), ToastKind.Success);
                AppendLog(Loc.F("toast_updated", installed, rel.Version));
            }
            catch (Exception ex)
            {
                SetToolStatus("tool_status_failed", "#E0484E");
                AppendLog(Loc.F("log_update_error", ex.Message));
                if (!auto) ShowToast(Loc.F("toast_check_failed", ex.Message), ToastKind.Error);
            }
            finally
            {
                _updating = false;
                UpdateNowButton.IsEnabled = true;
                UpdateProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenToolFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AppPaths.BinDir);
            OpenPath(AppPaths.BinDir);
        }

        private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenPath(Updater.ReleasesPage);

        // =====================================================================
        // GUI updates (this app's own GitHub releases; notify only)
        // =====================================================================

        private async Task CheckGuiUpdateAsync(bool auto)
        {
            try
            {
                var rel = await Updater.GetLatestGuiAsync();
                if (rel == null)
                {
                    if (!auto) ShowToast(Loc.T("toast_gui_none"), ToastKind.Info);
                    return;
                }

                if (Updater.IsNewer(rel.Version, AppVersion))
                {
                    _guiRelease = rel;
                    GuiUpdateText.Text = Loc.F("about_update_available", rel.Version, AppVersion);
                    GuiUpdateBanner.Visibility = Visibility.Visible;
                    ShowToast(Loc.F("toast_gui_new", rel.Version), ToastKind.Info);
                }
                else if (!auto)
                {
                    ShowToast(Loc.F("toast_gui_latest", AppVersion), ToastKind.Success);
                }
            }
            catch (Exception ex)
            {
                if (!auto) ShowToast(Loc.F("toast_gui_failed", ex.Message), ToastKind.Error);
            }
        }

        private async void CheckGuiUpdate_Click(object sender, RoutedEventArgs e) => await CheckGuiUpdateAsync(auto: false);
        private void GuiUpdate_Click(object sender, RoutedEventArgs e) => OpenPath(_guiRelease?.PageUrl ?? Updater.GuiRepoUrl + "/releases/latest");
        private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenPath(Updater.GuiRepoUrl);
        private void OpenIpaToolRepo_Click(object sender, RoutedEventArgs e) => OpenPath("https://github.com/" + Updater.Repo);

        // =====================================================================
        // Account
        // =====================================================================

        private async Task RefreshAccountAsync()
        {
            AccountEmail.Text = Loc.T("account_checking");
            var r = await _tool.AuthInfo();
            var acc = r.Last("email");
            if (r.Ok && acc != null) SetAccount(acc.Str("name"), acc.Str("email"));
            else SetAccount(null, null);
        }

        private void SetAccount(string name, string email)
        {
            _accountName = name;
            _accountEmail = email;
            _loggedIn = !string.IsNullOrEmpty(email);
            AccountName.Text = _loggedIn ? (string.IsNullOrWhiteSpace(name) ? email : name) : Loc.T("account_signed_out");
            AccountEmail.Text = _loggedIn ? email : Loc.T("account_signed_out_hint");
            AvatarText.Text = _loggedIn ? Initials(string.IsNullOrWhiteSpace(name) ? email : name) : "?";
            AvatarCircle.Fill = _loggedIn
                ? (Brush)new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#6C8BFF"), (Color)ColorConverter.ConvertFromString("#9257FF"), 45)
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#363A4C"));
            AccountButton.Content = Loc.T(_loggedIn ? "account_sign_out" : "account_sign_in");
            SearchLoginBanner.Visibility = _loggedIn ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string Initials(string text)
        {
            var words = (text ?? "").Split(new[] { ' ', '@', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            var s = words.Length >= 2 && !text.Contains("@") ? $"{words[0][0]}{words[words.Length - 1][0]}" : words[0].Substring(0, 1);
            return s.ToUpperInvariant();
        }

        private void HandleToolError(string error)
        {
            if (Errors.IsNotLoggedIn(error) && _loggedIn)
            {
                SetAccount(null, null);
                ShowToast(Loc.T("toast_session_expired"), ToastKind.Warning);
            }
        }

        private async void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_loggedIn)
            {
                ShowLogin();
                return;
            }

            if (MessageBox.Show(this, Loc.F("signout_confirm", _accountEmail), Loc.T("signout_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var r = await _tool.Revoke();
            SetAccount(null, null);
            ShowToast(r.Ok ? Loc.T("toast_signed_out") : Loc.F("toast_signed_out_warn", r.Error), ToastKind.Info);
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e) => ShowLogin();

        private void ShowLogin()
        {
            SetLogin2fa(false);
            LoginEmail.Text = _settings.LastEmail ?? "";
            LoginPassword.Clear();
            LoginErrorBox.Visibility = Visibility.Collapsed;
            LoginStatusText.Visibility = Visibility.Collapsed;
            LoginOverlay.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(LoginEmail.Text)) LoginEmail.Focus();
                else LoginPassword.Focus();
            }), DispatcherPriority.Input);
        }

        private void SetLogin2fa(bool on)
        {
            _awaiting2fa = on;
            LoginCredPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Login2faPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            LoginBackButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SetLoginTexts();
            Login2faCode.Text = "";
            if (on) Dispatcher.BeginInvoke(new Action(() => Login2faCode.Focus()), DispatcherPriority.Input);
        }

        private void SetLoginTexts()
        {
            LoginSubmitText.Text = Loc.T(_awaiting2fa ? "login_verify" : "login_submit");
            LoginTitle.Text = Loc.T(_awaiting2fa ? "login_2fa_title" : "login_title");
            LoginSubtitle.Text = _awaiting2fa ? Loc.F("login_2fa_subtitle", LoginEmail.Text.Trim()) : Loc.T("login_subtitle");
        }

        private void SetLoginBusy(bool busy, string status = null)
        {
            LoginSubmit.IsEnabled = !busy;
            LoginSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            LoginEmail.IsEnabled = LoginPassword.IsEnabled = Login2faCode.IsEnabled = !busy;
            LoginStatusText.Text = status ?? "";
            LoginStatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            if (busy) LoginErrorBox.Visibility = Visibility.Collapsed;
        }

        private void ShowLoginError(string text)
        {
            LoginErrorText.Text = text;
            LoginErrorBox.Visibility = Visibility.Visible;
        }

        private void LoginField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) LoginSubmit_Click(sender, e);
        }

        private void LoginBack_Click(object sender, RoutedEventArgs e)
        {
            SetLogin2fa(false);
            LoginErrorBox.Visibility = Visibility.Collapsed;
        }

        private void LoginClose_Click(object sender, RoutedEventArgs e)
        {
            _loginCts?.Cancel();
            SetLoginBusy(false);
            LoginPassword.Clear();
            LoginOverlay.Visibility = Visibility.Collapsed;
        }

        private async void LoginSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (!LoginSubmit.IsEnabled) return;
            var email = LoginEmail.Text.Trim();
            var password = LoginPassword.Password;
            var code = _awaiting2fa ? Login2faCode.Text.Trim() : null;

            if (email.Length == 0) { ShowLoginError(Loc.T("login_need_email")); LoginEmail.Focus(); return; }
            if (password.Length == 0) { SetLogin2fa(false); ShowLoginError(Loc.T("login_need_password")); LoginPassword.Focus(); return; }
            if (_awaiting2fa && !Regex.IsMatch(code, @"^\d{6}$")) { ShowLoginError(Loc.T("login_need_code")); Login2faCode.Focus(); return; }

            _loginCts?.Cancel();
            _loginCts = new CancellationTokenSource();
            SetLoginBusy(true, Loc.T(_awaiting2fa ? "login_busy_code" : "login_busy"));

            var r = await _tool.Login(email, password, code, _loginCts.Token);
            SetLoginBusy(false);
            if (r.Cancelled) return;

            if (r.HasMessage("2FA code is required"))
            {
                SetLogin2fa(true);
                return;
            }

            var ok = r.Events.LastOrDefault(ev => ev.Bool("success") == true && ev.ContainsKey("email"));
            if (r.Ok && ok != null)
            {
                _settings.LastEmail = email;
                _settings.Save();
                LoginPassword.Clear();
                LoginOverlay.Visibility = Visibility.Collapsed;
                SetAccount(ok.Str("name"), ok.Str("email"));
                ShowToast(Loc.F("toast_signed_in", ok.Str("email")), ToastKind.Success);
                return;
            }

            ShowLoginError(Errors.Friendly(r.Error ?? Loc.T("login_failed")));
            if (_awaiting2fa) Login2faCode.SelectAll();
        }

        // =====================================================================
        // Navigation
        // =====================================================================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageSearch == null) return; // during InitializeComponent
            PageSearch.Visibility = NavSearch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PagePurchases.Visibility = NavPurchases.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageDownloads.Visibility = NavDownloads.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (NavLog.IsChecked == true) LogBox.ScrollToEnd();
            if (NavPurchases.IsChecked == true && _uiReady && _loggedIn && _purchaseItems.Count == 0 && PurchaseLoadButton.IsEnabled)
                _ = LoadPurchasesAsync(1);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (DetailOverlay.Visibility == Visibility.Visible) { CloseDetail(); e.Handled = true; }
            else if (VersionsOverlay.Visibility == Visibility.Visible) { CloseVersions(); e.Handled = true; }
            else if (LoginOverlay.Visibility == Visibility.Visible) { LoginClose_Click(sender, e); e.Handled = true; }
        }

        private string SelectedPill(string group) =>
            FindChildren<RadioButton>(this).FirstOrDefault(r => r.GroupName == group && r.IsChecked == true)?.Tag as string ?? "";

        private string SearchPlatform => SelectedPill("SearchPlatform");
        private int SearchLimit => int.TryParse(SelectedPill("SearchLimit"), out var n) ? n : 10;
        private string PurchasePlatform => SelectedPill("PurchasePlatform");

        private static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T t) yield return t;
                foreach (var nested in FindChildren<T>(child)) yield return nested;
            }
        }

        // =====================================================================
        // Search
        // =====================================================================

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = SearchAsync();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

        private async Task SearchAsync()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0) { SearchBox.Focus(); return; }
            if (!SearchButton.IsEnabled) return;

            var platform = SearchPlatform;
            SearchErrorBanner.Visibility = Visibility.Collapsed;
            SearchResultInfo.Visibility = Visibility.Collapsed;

            bool isId = Regex.IsMatch(q, @"^(id)?\d{6,}$", RegexOptions.IgnoreCase);
            bool isBundle = !isId && BundlePattern.IsMatch(q);
            if (!isId && !isBundle && !_loggedIn)
            {
                ShowLogin();
                return;
            }

            SetSearchBusy(true);
            _searchItems.Clear();
            SearchEmpty.Visibility = Visibility.Collapsed;
            try
            {
                var items = new List<AppItem>();
                if (isId)
                {
                    var id = long.Parse(Regex.Replace(q, @"\D", ""));
                    items.Add(await Itunes.LookupIdAsync(id, _settings.Country, platform) ?? new AppItem { Id = id, Platform = platform });
                    _searchInfo = () => Loc.F("search_info_id", id);
                }
                else if (isBundle)
                {
                    items.Add(await Itunes.LookupBundleAsync(q, _settings.Country, platform) ?? new AppItem { BundleId = q, Platform = platform });
                    _searchInfo = () => Loc.F("search_info_bundle", q);
                }
                else
                {
                    var r = await _tool.Search(q, platform, SearchLimit);
                    if (!r.Ok)
                    {
                        ShowSearchError(Errors.Friendly(r.Error));
                        HandleToolError(r.Error);
                        return;
                    }

                    items = (r.Last("apps")?.Objects("apps") ?? Enumerable.Empty<Dictionary<string, object>>())
                        .Select(a => FromIpaTool(a, platform)).ToList();
                    var count = items.Count;
                    _searchInfo = () => Loc.F("search_info_results", count, q);
                }

                foreach (var it in items) _searchItems.Add(it);
                SearchResultInfo.Text = _searchInfo();
                SearchResultInfo.Visibility = Visibility.Visible;

                if (items.Count == 0)
                {
                    Bind(SearchEmptyTitle, "search_noresult_title");
                    Bind(SearchEmptyHint, "search_noresult_hint");
                    SearchEmpty.Visibility = Visibility.Visible;
                }

                await Itunes.EnrichAsync(items, _settings.Country);
            }
            catch (Exception ex)
            {
                ShowSearchError(ex.Message);
            }
            finally
            {
                SetSearchBusy(false);
            }
        }

        private static AppItem FromIpaTool(Dictionary<string, object> a, string platform) => new AppItem
        {
            Id = a.Long("id"),
            BundleId = a.Str("bundleID"),
            Name = a.Str("name"),
            Version = a.Str("version"),
            Price = a.Dbl("price"),
            Platforms = a.Strings("platforms").ToList(),
            PurchaseDate = a.Date("purchaseDate"),
            Platform = platform,
        };

        private void SetSearchBusy(bool busy)
        {
            SearchButton.IsEnabled = !busy;
            SearchSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowSearchError(string text)
        {
            SearchErrorText.Text = text;
            SearchErrorBanner.Visibility = Visibility.Visible;
            if (_searchItems.Count == 0)
            {
                Bind(SearchEmptyTitle, "search_error_title");
                Bind(SearchEmptyHint, "search_error_hint");
                SearchEmpty.Visibility = Visibility.Visible;
            }
        }

        // =====================================================================
        // Purchases
        // =====================================================================

        private async void PurchaseLoad_Click(object sender, RoutedEventArgs e) => await LoadPurchasesAsync(1);
        private async void PurchasePrev_Click(object sender, RoutedEventArgs e) => await LoadPurchasesAsync(_purchasePage - 1);
        private async void PurchaseNext_Click(object sender, RoutedEventArgs e) => await LoadPurchasesAsync(_purchasePage + 1);

        private void UpdatePurchasePageText() =>
            PurchasePageText.Text = _purchaseTotal < 0 ? "" : Loc.F("purchases_page", _purchasePage, _purchasePages, _purchaseTotal);

        private async Task LoadPurchasesAsync(int page)
        {
            if (!_loggedIn) { ShowLogin(); return; }
            if (page < 1) page = 1;

            PurchaseLoadButton.IsEnabled = PurchasePrev.IsEnabled = PurchaseNext.IsEnabled = false;
            PurchaseSpinner.Visibility = Visibility.Visible;
            PurchaseErrorBanner.Visibility = Visibility.Collapsed;
            try
            {
                var platform = PurchasePlatform;
                var r = await _tool.ListPurchases(page, PurchasePageSize, platform);
                if (!r.Ok)
                {
                    PurchaseErrorText.Text = Errors.Friendly(r.Error);
                    PurchaseErrorBanner.Visibility = Visibility.Visible;
                    HandleToolError(r.Error);
                    return;
                }

                var ev = r.Last("apps");
                var items = (ev?.Objects("apps") ?? Enumerable.Empty<Dictionary<string, object>>())
                    .Select(a => FromIpaTool(a, platform == "iphone" || platform == "ipad" ? "" : platform)).ToList();
                _purchaseTotal = (int)(ev?.Long("totalCount") ?? items.Count);
                _purchasePage = page;
                _purchasePages = Math.Max(1, (int)Math.Ceiling(_purchaseTotal / (double)PurchasePageSize));

                _purchaseItems.Clear();
                foreach (var it in items) _purchaseItems.Add(it);
                PurchaseScroll.ScrollToTop();
                UpdatePurchasePageText();
                PurchaseEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (items.Count == 0)
                {
                    Bind(PurchaseEmptyTitle, "purchases_none");
                    PurchaseEmptyHint.Visibility = Visibility.Collapsed;
                }

                await Itunes.EnrichAsync(items, _settings.Country);
            }
            finally
            {
                PurchaseSpinner.Visibility = Visibility.Collapsed;
                PurchaseLoadButton.IsEnabled = true;
                PurchasePrev.IsEnabled = _purchasePage > 1;
                PurchaseNext.IsEnabled = _purchasePage < _purchasePages;
            }
        }

        // =====================================================================
        // Card actions
        // =====================================================================

        private static AppItem ItemOf(object sender) => (sender as FrameworkElement)?.Tag as AppItem;

        private void CopyBundle_Click(object sender, RoutedEventArgs e) => CopyBundle(ItemOf(sender));

        private void CopyBundle(AppItem app)
        {
            if (app == null) return;
            var text = app.HasBundle ? app.BundleId : app.Id.ToString();
            try
            {
                Clipboard.SetText(text);
                ShowToast(Loc.F("toast_copied", text), ToastKind.Success);
            }
            catch
            {
                ShowToast(Loc.T("toast_clipboard"), ToastKind.Warning);
            }
        }

        private void OpenStore_Click(object sender, RoutedEventArgs e)
        {
            var app = ItemOf(sender);
            if (app?.HasStoreUrl == true) OpenPath(app.StoreUrl);
        }

        private static string LatestLabel(AppItem app) =>
            app.HasVersion ? Loc.F("latest_version_v", app.Version) : Loc.T("latest_version");

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            var app = ItemOf(sender);
            if (app != null) EnqueueDownload(app, null, LatestLabel(app));
        }

        private async void Versions_Click(object sender, RoutedEventArgs e)
        {
            var app = ItemOf(sender);
            if (app != null) await OpenVersionsAsync(app);
        }

        // =====================================================================
        // App detail
        // =====================================================================

        private void Detail_Click(object sender, RoutedEventArgs e)
        {
            var app = ItemOf(sender);
            if (app != null) _ = ShowDetailAsync(app);
        }

        private async Task ShowDetailAsync(AppItem app)
        {
            _detailApp = app;
            if (!app.HasStoreInfo && app.Id > 0) await Itunes.EnrichAsync(new[] { app }, _settings.Country);
            if (_detailApp != app) return;

            DetailName.Text = app.DisplayName;
            DetailSeller.Text = app.Subtitle;
            DetailInitial.Text = app.Initial;
            DetailIcon.ImageSource = !string.IsNullOrEmpty(app.LargeIconUrl) ? Images.FromUrl(app.LargeIconUrl, 256) : app.IconImage;

            DetailChips.Children.Clear();
            AddChip(app.BundleText, mono: true);
            if (app.HasVersion) AddChip(app.VersionText);
            if (app.HasSize) AddChip(app.SizeText);
            AddChip(app.PriceText, app.IsFree ? "SuccessSoftBrush" : "WarningSoftBrush", app.IsFree ? "SuccessBrush" : "WarningBrush");
            if (app.HasRating) AddChip(app.RatingText);
            if (app.HasMinOs) AddChip(app.MinOsText);
            if (!string.IsNullOrEmpty(app.ContentRating)) AddChip(app.ContentRating);
            if (!string.IsNullOrEmpty(app.Genre)) AddChip(app.Genre);

            DetailStoreButton.Visibility = app.HasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
            if (DetailOverlay.Visibility != Visibility.Visible)
            {
                DetailShots.ItemsSource = app.Screenshots.Select(u => Images.FromUrl(u, 0, 760)).Where(i => i != null).ToList();
                DetailScroll.ScrollToTop();
            }
            DetailShotsPanel.Visibility = app.HasScreenshots ? Visibility.Visible : Visibility.Collapsed;
            DetailNotesTitle.Text = app.ReleaseNotesTitle;
            DetailNotes.Text = app.ReleaseNotes ?? "";
            DetailNotesPanel.Visibility = app.HasReleaseNotes ? Visibility.Visible : Visibility.Collapsed;
            DetailDesc.Text = app.Description ?? "";
            DetailDescPanel.Visibility = app.HasDescription ? Visibility.Visible : Visibility.Collapsed;
            DetailNoInfo.Visibility = app.HasStoreInfo ? Visibility.Collapsed : Visibility.Visible;

            DetailOverlay.Visibility = Visibility.Visible;
        }

        private void AddChip(string text, string bgKey = null, string fgKey = null, bool mono = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 12 };
            if (mono) tb.FontFamily = (FontFamily)FindResource("MonoFont");
            if (fgKey != null) { tb.SetResourceReference(TextBlock.ForegroundProperty, fgKey); tb.FontWeight = FontWeights.SemiBold; }
            var chip = new Border { Style = (Style)FindResource("Chip"), Child = tb };
            if (bgKey != null) chip.SetResourceReference(Border.BackgroundProperty, bgKey);
            DetailChips.Children.Add(chip);
        }

        private void CloseDetail()
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
            DetailShots.ItemsSource = null;
            _detailApp = null;
        }

        private void DetailClose_Click(object sender, RoutedEventArgs e) => CloseDetail();

        private void DetailDownload_Click(object sender, RoutedEventArgs e)
        {
            var app = _detailApp;
            if (app != null) EnqueueDownload(app, null, LatestLabel(app));
        }

        private async void DetailVersions_Click(object sender, RoutedEventArgs e)
        {
            var app = _detailApp;
            if (app == null) return;
            CloseDetail();
            await OpenVersionsAsync(app);
        }

        private void DetailStore_Click(object sender, RoutedEventArgs e)
        {
            if (_detailApp?.HasStoreUrl == true) OpenPath(_detailApp.StoreUrl);
        }

        private void DetailCopy_Click(object sender, RoutedEventArgs e) => CopyBundle(_detailApp);

        private void Shots_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Wheel scrolls the detail page; Shift+wheel scrolls the screenshots sideways.
            if (sender is ScrollViewer sv && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
                return;
            }

            e.Handled = true;
            DetailScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
        }

        private void Overlay_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (sender == DetailOverlay) CloseDetail();
            else if (sender == VersionsOverlay) CloseVersions();
        }

        private void OverlayCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        // =====================================================================
        // Versions
        // =====================================================================

        private void SetVersionsInfo(Func<string> text)
        {
            _versionsInfo = text;
            VersionsInfo.Text = text();
        }

        private async Task OpenVersionsAsync(AppItem app)
        {
            if (!_loggedIn) { ShowLogin(); return; }

            _versionsCts?.Cancel();
            _versionsCts = new CancellationTokenSource();
            var ct = _versionsCts.Token;

            _versionsApp = app;
            _versions.Clear();
            VersionsAppName.Text = $"{app.DisplayName}  ·  {app.BundleText}";
            SetVersionsInfo(() => Loc.T("versions_loading"));
            VersionsSpinner.Visibility = Visibility.Visible;
            VersionsMore.Visibility = Visibility.Collapsed;
            VersionsOverlay.Visibility = Visibility.Visible;

            var r = await _tool.ListVersions(app, ct);
            if (ct.IsCancellationRequested) return;
            VersionsSpinner.Visibility = Visibility.Collapsed;

            if (!r.Ok)
            {
                var err = Errors.Friendly(r.Error);
                SetVersionsInfo(() => err);
                HandleToolError(r.Error);
                return;
            }

            var ids = r.Last("externalVersionIdentifiers")?.Strings("externalVersionIdentifiers").ToList() ?? new List<string>();
            ids.Reverse(); // Apple lists oldest first
            for (int i = 0; i < ids.Count; i++) _versions.Add(new VersionItem { Id = ids[i], IsLatest = i == 0 });

            if (ids.Count == 0)
            {
                SetVersionsInfo(() => Loc.T("versions_none"));
                return;
            }

            var total = ids.Count;
            SetVersionsInfo(() => Loc.F("versions_count_loading", total));
            VersionsMore.Visibility = total > 6 ? Visibility.Visible : Visibility.Collapsed;
            await LoadVersionDetailsAsync(6, ct);
        }

        private async Task LoadVersionDetailsAsync(int count, CancellationToken ct)
        {
            var app = _versionsApp;
            var todo = _versions.Where(v => v.CanLoad).Take(count).ToList();
            foreach (var v in todo)
            {
                if (ct.IsCancellationRequested) return;
                await LoadVersionDetailAsync(app, v, ct);
            }

            if (!ct.IsCancellationRequested && _versionsApp == app)
            {
                var total = _versions.Count;
                SetVersionsInfo(() => Loc.F("versions_count", total));
                VersionsMore.Visibility = _versions.Any(v => v.CanLoad) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task LoadVersionDetailAsync(AppItem app, VersionItem v, CancellationToken ct)
        {
            v.Loading = true;
            var r = await _tool.VersionMetadata(app, v.Id, ct);
            v.Loading = false;
            if (r.Cancelled) return;

            var ev = r.Last("displayVersion");
            if (r.Ok && ev != null)
            {
                v.DisplayVersion = ev.Str("displayVersion");
                v.ReleaseDate = ev.Date("releaseDate");
            }
            else
            {
                v.Failed = true;
            }
            v.Loaded = true;
        }

        private async void VersionsMore_Click(object sender, RoutedEventArgs e)
        {
            VersionsMore.IsEnabled = false;
            try { await LoadVersionDetailsAsync(10, _versionsCts?.Token ?? CancellationToken.None); }
            finally { VersionsMore.IsEnabled = true; }
        }

        private async void VersionInfo_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is VersionItem v && v.CanLoad && _versionsApp != null)
                await LoadVersionDetailAsync(_versionsApp, v, _versionsCts?.Token ?? CancellationToken.None);
        }

        private void VersionDownload_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is VersionItem v && _versionsApp != null)
                EnqueueDownload(_versionsApp, v.IsLatest ? null : v.Id, v.IsLatest ? Loc.F("version_latest_prefix", v.Label) : v.Label);
        }

        private void CloseVersions()
        {
            _versionsCts?.Cancel();
            VersionsOverlay.Visibility = Visibility.Collapsed;
        }

        private void VersionsClose_Click(object sender, RoutedEventArgs e) => CloseVersions();

        // =====================================================================
        // Download queue
        // =====================================================================

        private void EnqueueDownload(AppItem app, string versionId, string label)
        {
            if (!_loggedIn)
            {
                ShowLogin();
                ShowToast(Loc.T("toast_need_login"), ToastKind.Warning);
                return;
            }

            var dup = _jobs.FirstOrDefault(j => j.CanCancel && j.App.BundleText == app.BundleText && j.VersionId == versionId);
            if (dup != null)
            {
                ShowToast(Loc.F("toast_in_queue", app.DisplayName), ToastKind.Info);
                return;
            }

            _jobs.Insert(0, new DownloadJob(app, versionId, label) { State = JobState.Queued });
            ShowToast(Loc.F("toast_queued", app.DisplayName), ToastKind.Info);
            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            if (_queueRunning) return;
            _queueRunning = true;
            try
            {
                while (true)
                {
                    var job = _jobs.LastOrDefault(j => j.State == JobState.Queued); // oldest first
                    if (job == null) break;
                    await RunJobAsync(job);
                    UpdateDownloadBadge();
                }
            }
            finally
            {
                _queueRunning = false;
                UpdateDownloadBadge();
            }
        }

        private async Task RunJobAsync(DownloadJob job)
        {
            try
            {
                Directory.CreateDirectory(_settings.DownloadFolder);
            }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.StatusText = Loc.F("job_folder_error", ex.Message);
                return;
            }

            job.Cts = new CancellationTokenSource();
            job.State = JobState.Running;
            job.Indeterminate = true;
            job.Progress = 0;
            job.StatusText = Loc.T(_settings.AutoPurchase ? "job_connecting_license" : "job_connecting");
            UpdateDownloadBadge();

            var sw = Stopwatch.StartNew();
            var r = await _tool.Download(job.App, _settings.DownloadFolder, _settings.AutoPurchase, job.VersionId,
                line => Dispatcher.BeginInvoke(new Action(() => ApplyProgress(job, line))), job.Cts.Token);
            sw.Stop();

            if (r.Cancelled)
            {
                job.State = JobState.Cancelled;
                job.StatusText = Loc.T("job_cancelled");
                return;
            }

            var done = r.Last("output");
            if (r.Ok && done != null)
            {
                job.OutputPath = done.Str("output");
                job.Progress = 100;
                job.Indeterminate = false;
                job.State = JobState.Done;
                long size = 0;
                try { size = new FileInfo(job.OutputPath).Length; } catch { /* ignore */ }
                var purchased = done.Bool("purchased") == true ? "  ·  " + Loc.T("job_license_acquired") : "";
                job.StatusText = $"{Path.GetFileName(job.OutputPath)}  ·  {Format.Size(size)}  ·  {Format.Duration(sw.Elapsed)}{purchased}";
                ShowToast(Loc.F("toast_downloaded", job.App.DisplayName), ToastKind.Success);
            }
            else
            {
                job.State = JobState.Failed;
                job.Indeterminate = false;
                job.StatusText = Errors.Friendly(r.Error ?? Loc.T("job_failed_default"));
                HandleToolError(r.Error);
                ShowToast(Loc.F("toast_download_failed", job.App.DisplayName), ToastKind.Error);
            }
        }

        private static void ApplyProgress(DownloadJob job, string line)
        {
            if (job.State != JobState.Running) return;
            var m = Percent.Match(line);
            if (!m.Success) return;
            var pct = Math.Min(100, int.Parse(m.Groups[1].Value));
            job.Indeterminate = false;
            job.Progress = pct;
            var detail = Paren.Match(line);
            job.StatusText = detail.Success
                ? Loc.F("job_progress_detail", pct, detail.Groups[1].Value.Trim())
                : Loc.F("job_progress", pct);
        }

        private void UpdateDownloadBadge()
        {
            var active = _jobs.Count(j => j.CanCancel);
            DownloadBadge.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadBadgeText.Text = active.ToString();
            DownloadEmpty.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static DownloadJob JobOf(object sender) => (sender as FrameworkElement)?.Tag as DownloadJob;

        private void JobCancel_Click(object sender, RoutedEventArgs e)
        {
            var job = JobOf(sender);
            if (job == null) return;
            if (job.State == JobState.Queued)
            {
                job.State = JobState.Cancelled;
                job.StatusText = Loc.T("job_cancelled_short");
                UpdateDownloadBadge();
            }
            else job.Cts?.Cancel();
        }

        private void JobRetry_Click(object sender, RoutedEventArgs e)
        {
            var job = JobOf(sender);
            if (job == null) return;
            job.State = JobState.Queued;
            job.Indeterminate = true;
            job.StatusText = Loc.T("job_waiting");
            UpdateDownloadBadge();
            _ = ProcessQueueAsync();
        }

        private void JobOpen_Click(object sender, RoutedEventArgs e)
        {
            var job = JobOf(sender);
            if (job?.OutputPath == null) return;
            if (File.Exists(job.OutputPath)) Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\"");
            else OpenPath(Path.GetDirectoryName(job.OutputPath));
        }

        private void JobRemove_Click(object sender, RoutedEventArgs e)
        {
            var job = JobOf(sender);
            if (job != null && job.CanRemove) _jobs.Remove(job);
        }

        private void ClearFinished_Click(object sender, RoutedEventArgs e)
        {
            foreach (var j in _jobs.Where(j => !j.CanCancel).ToList()) _jobs.Remove(j);
        }

        private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_settings.DownloadFolder);
                OpenPath(_settings.DownloadFolder);
            }
            catch (Exception ex)
            {
                ShowToast(Loc.F("toast_open_failed", ex.Message), ToastKind.Error);
            }
        }

        // =====================================================================
        // Settings
        // =====================================================================

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FolderPicker.Pick(this, _settings.DownloadFolder, Loc.T("folder_dialog_title"));
            if (string.IsNullOrEmpty(path)) return;
            _settings.DownloadFolder = path;
            _settings.Save();
            ApplySettingsToUi();
            ApplyLanguageTexts();
            ShowToast(Loc.F("toast_folder_set", path), ToastKind.Success);
        }

        private void SettingsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_applyingSettings || !IsLoaded) return;
            _settings.AutoPurchase = AutoPurchaseCheck.IsChecked == true;
            _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _settings.Save();
        }

        private void CountryBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var c = (CountryBox.Text ?? "").Trim().ToLowerInvariant();
            if (!Regex.IsMatch(c, "^[a-z]{2}$"))
            {
                CountryBox.Text = _settings.Country;
                return;
            }

            if (c == _settings.Country) return;
            _settings.Country = c;
            _settings.Save();
            ShowToast(Loc.F("toast_country", c.ToUpperInvariant()), ToastKind.Success);
        }

        private void CopyPassphrase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_settings.GetPassphrase());
                ShowToast(Loc.T("toast_pass_copied"), ToastKind.Success);
            }
            catch
            {
                ShowToast(Loc.T("toast_clipboard"), ToastKind.Warning);
            }
        }

        private void ShowPassphraseEdit_Click(object sender, RoutedEventArgs e)
        {
            PassphraseEditPanel.Visibility = PassphraseEditPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (PassphraseEditPanel.Visibility == Visibility.Visible) PassphraseBox.Focus();
        }

        private async void SavePassphrase_Click(object sender, RoutedEventArgs e)
        {
            var p = PassphraseBox.Password;
            if (string.IsNullOrEmpty(p)) { PassphraseBox.Focus(); return; }
            _settings.SetPassphrase(p);
            PassphraseBox.Clear();
            PassphraseEditPanel.Visibility = Visibility.Collapsed;
            await RefreshAccountAsync();
            ShowToast(Loc.T(_loggedIn ? "toast_pass_ok" : "toast_pass_bad"), _loggedIn ? ToastKind.Success : ToastKind.Warning);
        }

        private async void ResetSession_Click(object sender, RoutedEventArgs e)
        {
            if (_tool.RunningCount > 0 || _jobs.Any(j => j.State == JobState.Running))
            {
                ShowToast(Loc.T("toast_running_wait"), ToastKind.Warning);
                return;
            }

            if (MessageBox.Show(this, Loc.F("reset_confirm", AppPaths.IpaToolStateDir), Loc.T("reset_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                if (Directory.Exists(AppPaths.IpaToolStateDir)) Directory.Delete(AppPaths.IpaToolStateDir, true);
                SetAccount(null, null);
                ShowToast(Loc.T("toast_reset"), ToastKind.Success);
                await RefreshAccountAsync();
            }
            catch (Exception ex)
            {
                ShowToast(Loc.F("toast_delete_failed", ex.Message), ToastKind.Error);
            }
        }

        // =====================================================================
        // Log
        // =====================================================================

        private void AppendLog(string line)
        {
            if (LogBox.Text.Length > 400_000) LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - 200_000);
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            if (PageLog.Visibility == Visibility.Visible) LogBox.ScrollToEnd();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogBox.Text);
                ShowToast(Loc.T("toast_log_copied"), ToastKind.Success);
            }
            catch
            {
                ShowToast(Loc.T("toast_clipboard"), ToastKind.Warning);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

        // =====================================================================
        // Helpers
        // =====================================================================

        private void ShowToast(string text, ToastKind kind = ToastKind.Info)
        {
            ToastText.Text = text;
            switch (kind)
            {
                case ToastKind.Success: ToastIcon.Text = ""; ToastIcon.Foreground = Brush("#7CE0A8"); break;
                case ToastKind.Warning: ToastIcon.Text = ""; ToastIcon.Foreground = Brush("#FFC85C"); break;
                case ToastKind.Error: ToastIcon.Text = ""; ToastIcon.Foreground = Brush("#FF8A8F"); break;
                default: ToastIcon.Text = ""; ToastIcon.Foreground = Brush("#9DB0FF"); break;
            }

            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 6 : 4);
            _toastTimer.Start();
        }

        private static SolidColorBrush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        private void OpenPath(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowToast(Loc.F("toast_open_failed", ex.Message), ToastKind.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var running = _jobs.Where(j => j.CanCancel).ToList();
            if (running.Count > 0 &&
                MessageBox.Show(this, Loc.F("close_confirm", running.Count), "IPA Tool GUI",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            foreach (var j in running) j.Cts?.Cancel();
            _loginCts?.Cancel();
            _versionsCts?.Cancel();
        }
    }
}
