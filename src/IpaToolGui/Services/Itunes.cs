using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IpaToolGui.Models;

namespace IpaToolGui.Services
{
    /// <summary>Public iTunes Lookup API: icons, screenshots, ratings and store links (no login needed).</summary>
    public static class Itunes
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("IpaToolGui/1.0");
            return c;
        }

        private static string Entity(string platform) => platform == "macos" ? "&entity=macSoftware" : "";

        private static async Task<List<Dictionary<string, object>>> Query(string query, string country)
        {
            var url = $"https://itunes.apple.com/lookup?{query}&country={Uri.EscapeDataString(country ?? "us")}";
            var json = await Http.GetStringAsync(url);
            return Json.ParseObject(json).Objects("results").Where(r => r.Long("trackId") > 0).ToList();
        }

        /// <summary>Fills store metadata for every item (store country first, then US as fallback).</summary>
        public static async Task EnrichAsync(IList<AppItem> items, string country)
        {
            try
            {
                foreach (var c in new[] { country, "us" }.Distinct())
                {
                    var pending = items.Where(i => i.Id > 0 && !i.HasStoreInfo).ToList();
                    if (pending.Count == 0) return;
                    foreach (var group in pending.GroupBy(i => i.Platform == "macos"))
                    {
                        foreach (var chunk in Chunk(group.ToList(), 150))
                        {
                            var ids = string.Join(",", chunk.Select(i => i.Id).Distinct());
                            var results = await Query("id=" + ids + Entity(group.Key ? "macos" : ""), c);
                            foreach (var r in results)
                                foreach (var item in chunk.Where(i => i.Id == r.Long("trackId")))
                                    Apply(item, r);
                        }
                    }
                }
            }
            catch
            {
                // metadata is a nice-to-have
            }
        }

        public static async Task<AppItem> LookupBundleAsync(string bundleId, string country, string platform)
        {
            foreach (var c in new[] { country, "us" }.Distinct())
            {
                try
                {
                    var r = (await Query("bundleId=" + Uri.EscapeDataString(bundleId) + Entity(platform), c)).FirstOrDefault();
                    if (r != null)
                    {
                        var item = new AppItem { Platform = platform };
                        Apply(item, r);
                        return item;
                    }
                }
                catch
                {
                    // try next country
                }
            }
            return null;
        }

        public static async Task<AppItem> LookupIdAsync(long id, string country, string platform)
        {
            var item = new AppItem { Id = id, Platform = platform };
            await EnrichAsync(new[] { item }, country);
            return item.HasStoreInfo ? item : null;
        }

        public static void Apply(AppItem item, Dictionary<string, object> r)
        {
            if (item.Id == 0) item.Id = r.Long("trackId");
            if (string.IsNullOrEmpty(item.BundleId)) item.BundleId = r.Str("bundleId");
            if (string.IsNullOrWhiteSpace(item.Name)) item.Name = r.Str("trackName");
            if (string.IsNullOrEmpty(item.Version)) item.Version = r.Str("version");
            if (item.Price <= 0) item.Price = r.Dbl("price");
            item.FormattedPrice = r.Str("formattedPrice");
            item.Seller = r.Str("sellerName") ?? r.Str("artistName");
            item.Genre = r.Str("primaryGenreName");
            item.SizeBytes = r.Long("fileSizeBytes");
            item.Rating = r.Dbl("averageUserRating");
            item.RatingCount = r.Long("userRatingCount");
            item.MinOs = r.Str("minimumOsVersion");
            item.ContentRating = r.Str("trackContentRating") ?? r.Str("contentAdvisoryRating");
            item.StoreUrl = r.Str("trackViewUrl");
            item.Description = r.Str("description");
            item.ReleaseNotes = r.Str("releaseNotes");
            item.CurrentVersionDate = r.Date("currentVersionReleaseDate");

            var shots = r.Strings("screenshotUrls").ToList();
            if (shots.Count == 0) shots = r.Strings("ipadScreenshotUrls").ToList();
            if (shots.Count == 0) shots = r.Strings("appletvScreenshotUrls").ToList();
            item.Screenshots = shots;

            item.LargeIconUrl = r.Str("artworkUrl512") ?? r.Str("artworkUrl100");
            item.SetIcon(r.Str("artworkUrl100") ?? r.Str("artworkUrl512") ?? r.Str("artworkUrl60"));
            item.HasStoreInfo = true;
            item.RefreshAll();
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> list, int size)
        {
            for (int i = 0; i < list.Count; i += size)
                yield return list.GetRange(i, Math.Min(size, list.Count - i));
        }
    }
}
