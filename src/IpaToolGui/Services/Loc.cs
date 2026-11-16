using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Markup;

namespace IpaToolGui.Services
{
    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string nativeName, string culture)
        {
            Code = code;
            NativeName = nativeName;
            Culture = culture;
        }

        public string Code { get; }
        public string NativeName { get; }
        public string Culture { get; }
    }

    /// <summary>
    /// String tables live in Languages/*.json (embedded). XAML binds through <see cref="Tr"/>,
    /// so switching language updates the whole UI without a restart.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public const string Auto = "auto";
        private const string Fallback = "en";

        public static readonly LanguageInfo[] Languages =
        {
            new LanguageInfo("vi", "Tiếng Việt", "vi-VN"),
            new LanguageInfo("en", "English", "en-US"),
            new LanguageInfo("zh-Hans", "简体中文", "zh-CN"),
            new LanguageInfo("ko", "한국어", "ko-KR"),
            new LanguageInfo("tr", "Türkçe", "tr-TR"),
        };

        public static Loc Instance { get; } = new Loc();

        private readonly Dictionary<string, Dictionary<string, string>> _cache = new Dictionary<string, Dictionary<string, string>>();
        private Dictionary<string, string> _current = new Dictionary<string, string>();
        private Dictionary<string, string> _fallback = new Dictionary<string, string>();

        public event PropertyChangedEventHandler PropertyChanged;

        public string Code { get; private set; } = Fallback;
        public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

        public string this[string key] => Get(key);

        public static string T(string key) => Instance.Get(key);

        public static string F(string key, params object[] args)
        {
            var format = Instance.Get(key);
            try { return string.Format(Instance.Culture, format, args); }
            catch (FormatException) { return format; }
        }

        /// <summary>Maps a setting value ("auto" or a code) to a supported language code.</summary>
        public static string Resolve(string setting)
        {
            if (!string.IsNullOrEmpty(setting) && setting != Auto && Languages.Any(l => l.Code == setting)) return setting;

            var ui = CultureInfo.CurrentUICulture;
            switch (ui.TwoLetterISOLanguageName)
            {
                case "vi": return "vi";
                case "zh": return "zh-Hans";
                case "ko": return "ko";
                case "tr": return "tr";
                default: return Fallback;
            }
        }

        public void SetLanguage(string setting)
        {
            var code = Resolve(setting);
            _fallback = Load(Fallback);
            _current = code == Fallback ? _fallback : Load(code);
            Code = code;
            try { Culture = new CultureInfo(Languages.First(l => l.Code == code).Culture); }
            catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        }

        private string Get(string key)
        {
            if (key == null) return "";
            if (_current.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (_fallback.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return key;
        }

        private Dictionary<string, string> Load(string code)
        {
            if (_cache.TryGetValue(code, out var cached)) return cached;
            var table = new Dictionary<string, string>();
            try
            {
                using (var s = typeof(Loc).Assembly.GetManifestResourceStream("lang." + code + ".json"))
                {
                    if (s != null)
                    {
                        using (var r = new StreamReader(s, Encoding.UTF8))
                            table = Json.Deserialize<Dictionary<string, string>>(r.ReadToEnd()) ?? table;
                    }
                }
            }
            catch
            {
                // broken table: fall back to English keys
            }

            _cache[code] = table;
            return table;
        }
    }

    /// <summary>XAML: Text="{l:Tr search_title}" — a live binding to the current language.</summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class Tr : MarkupExtension
    {
        public Tr() { }
        public Tr(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
