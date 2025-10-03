using System.Text;
using WebScrapper.scrapper;

namespace subtitle_downloader.downloader; 

public class SubtitleScraper {
    
    public static List<SubtitleRow> ScrapeSubtitleTable(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            return ScrapeDownloadAnchor(doc);
        }

        var subtitles = new List<SubtitleRow>();
        List<Tag> tableRows = doc.ExtractTags(tableBody, "tr");
        foreach (var tr in tableRows) {
            var id = tr.GetAttribute("id");
            if (id is null || !id.StartsWith("name")) {
                continue;
            }
            
            var anchor = doc.FindFrom("a", tr.StartOffset + 100,
                Compare.Exact("class", "bnone"),
                Compare.Key("title"),
                Compare.Key("href"),
                Compare.KeyAndValuePrefix("onclick", "if")
                );
            if (anchor is null) {
                continue;
            }
            SubtitleRow subtitleRow = new SubtitleRow {
                broadcastTitle = doc.ExtractText(anchor)
            };
            subtitleRow.broadcastTitle = fixTitle(subtitleRow.broadcastTitle);

            // Extract base filename for filtering distributions HDTV, WEB-DL, Blu-Ray
            // This could either be within <br> or <span>'s title attribute
            subtitleRow.baseFilename = extractBaseFilename(doc, html, anchor.EndOffset);

            var downloadAnchor = doc.FindFrom("a", anchor.EndOffset + 100, 
                Compare.KeyAndValuePrefix("href", "/en/subtitleserve"), 
                Compare.Key("onclick"));
            if (downloadAnchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }

            string href = downloadAnchor.GetAttribute("href") ?? "";
            subtitleRow.setDownloadURL(href);
            string timesDownloaded = doc.ExtractText(downloadAnchor);
            try {
                int x = timesDownloaded.IndexOf('x');
                subtitleRow.downloads = ulong.Parse(timesDownloaded[..x]);
            } catch { }

            var extensionSpan = doc.FindFrom("span", downloadAnchor.EndOffset, Compare.Exact("class", "p"));
            if (extensionSpan is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.format = doc.ExtractText(extensionSpan);
            
            var ratingSpan = doc.FindFrom("span", extensionSpan.EndOffset, Compare.Key("title"));
            if (ratingSpan is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            string ratingExtracted = doc.ExtractText(ratingSpan);
            try {
                subtitleRow.rating = double.Parse(ratingExtracted);
            } catch { }
            
            subtitles.Add(subtitleRow);
        }

        return subtitles;
    }

    // returns the URL of the production
    public static List<Production> scrapeSearchResults(string html, bool isMovie) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            return new List<Production>();
        }

        var productions = new List<Production>();
        List<Tag> tags = doc.ExtractTags(tableBody, "tr");
        foreach (var tag in tags) {
            string? id = tag.GetAttribute("id");
            if (id == null || !id.StartsWith("name")) {
                continue;
            }
            var data = doc.FindFrom("td", tag.StartOffset, Compare.KeyAndValuePrefix("id", "main"));
            if (data is null) {
                continue;
            }
            var strong = doc.FindFrom("strong", data.StartOffset + 16);
            if (strong is null) {
                continue;
            }
            
            string productionName = doc.ExtractText(strong);
            
            var anchor = doc.FindFrom("a", strong.StartOffset + 6, 
                Compare.Exact("class", "bnone"), Compare.Key("title"));
            if (anchor is null) {
                continue;
            }

            string? href = anchor.GetAttribute("href");
            if (href is null) {
                continue;
            }
            
            // Since title was extracted from the strong tag (specifically the anchor) the strong's end offset is known
            // This logic is meant to skip episode entries which follow this format: [S1E1] 
            int episodeSt = shortIndexOf(html, '[', strong.EndOffset, strong.EndOffset + 10);
            if (episodeSt != -1) {
                continue;
            }

            var (title, year) = Production.ParseTitleYear(productionName);
            
            var production = new Production {
                name = title,
                year = year,
                id = extractUrlId(href),
                kind = isMovie ? "movie" : "tv",
            };
            productions.Add(production);
        }

        return productions;
    }

    // Return base file name or empty
    private static string extractBaseFilename(HtmlDoc doc, string fullHtml, int offset) {
        var br1 = doc.FindFrom("br", offset);
        if (br1 == null) {
            return "";
        }
        var br2 = doc.FindFrom("br", br1.StartOffset + 6);
        if (br2 == null) {
            return "";
        }

        string spanSubString = fullHtml[(br1.StartOffset + 6)..br2.StartOffset];
        HtmlDoc spanDoc = new HtmlDoc(spanSubString);
        var titleSpan = spanDoc.Find("span", Compare.Key("title"));
        if (titleSpan != null) {
            string? title = titleSpan.GetAttribute("title");
            return title ?? "";
        }
        
        return fixTitle(spanSubString);
    }
    
    private static uint extractUrlId(string url) {
        var idMovie = url.IndexOf("idmovie-", StringComparison.InvariantCulture);
        if (idMovie == -1) {
            return 0;
        }
        int stIndex = idMovie + 8;
        string numerical = url[stIndex..];
        return uint.TryParse(numerical, out var id) ? id : 0;
    }
    
    private static int shortIndexOf(string text, char chr, int from, int to) {
        for (int i = from; i < text.Length && i < to; i++) {
            if (text[i] == chr) {
                return i;
            }
        }
        return -1;
    }
    
    private static List<SubtitleRow> ScrapeDownloadAnchor(HtmlDoc doc) {
        Tag? downloadAnchor = doc.Find("a", 
            Compare.Exact("itemprop", "url"),
            Compare.Exact("title", "Download"),
            Compare.Key("href"));
        if (downloadAnchor is null) {
            Console.WriteLine("Download anchor not found");
            return new List<SubtitleRow>();
        }

        SubtitleRow subtitle = new SubtitleRow();
        string href = downloadAnchor.GetAttribute("href") ?? "";
        subtitle.downloadURL = href;

        Tag? titleSpan = doc.FindFrom("span",
            downloadAnchor.StartOffset + 50,
            Compare.Exact("itemprop", "name"));
        
        if (titleSpan == null) {
            Console.WriteLine("No title found!");
            return new List<SubtitleRow>(1) { subtitle };
        }
        
        subtitle.broadcastTitle = doc.ExtractText(titleSpan);

        Tag? h2 = doc.FindFrom("h2", titleSpan.StartOffset + 50);
        if (h2 != null) {
            // The subtitle extension was put at the very end of this header tag for some reason
            string content = doc.ExtractText(h2);
            string[] spaceSplit = content.Split(' ');
            for (int i = spaceSplit.Length - 1; i >= 0; i--) {
                int length = spaceSplit[i].Length;
                if (length >= 2 && length <= 4) {
                    subtitle.format = spaceSplit[i].Trim().ToLower();
                    break;
                }
            }
        }

        if (h2 != null) {
            Tag? downloadedAnchor = doc.FindFrom("a", h2.EndOffset,
                Compare.Exact("class", "none"), Compare.Exact("title", "downloaded"));
            if (downloadedAnchor == null) {
                return new List<SubtitleRow>(1) { subtitle };
            }
            string times = doc.ExtractText(downloadedAnchor);
            int x = times.IndexOf('x');
            if (x != -1 && ulong.TryParse(times[..x], out ulong downloaded)) {
                subtitle.downloads = downloaded;
            }
        }
        
        return new List<SubtitleRow>(1) { subtitle };
    }

    public static List<Season> ScrapeSeriesTable(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            Console.WriteLine("ERROR: <tbody> not found in the page, dumping html!");
            Utils.FailExit(html);
            return new List<Season>();
        }

        List<Tag> tableRows = doc.ExtractTags(tableBody, "tr");

        List<Season> seasons = new();

        bool hasUnclassified = false;
        // 1. Scrape season packages
        foreach (var tr in tableRows) {
            Season season = new Season();
            if (tr.Attributes.Count != 0) {
                continue;
            }

            Tag? seasonTag = doc.FindFrom("span", tr.StartOffset + 2, Compare.KeyAndValuePrefix("id", "season"));
            if (seasonTag is null) {
                continue;
            }

            string seasonStr = seasonTag.GetAttribute("id") ?? "";
            int dash = seasonStr.IndexOf('-');
            if (dash == -1) {
                continue;
            }

            try {
                season.number = int.Parse(seasonStr[(dash + 1)..]);
                if (season.number == -1) {
                    hasUnclassified = true;
                }
            }
            catch { }

            Tag? seasonAnchor = doc.FindFrom("a", seasonTag.StartOffset + 10, Compare.Key("href"));
            if (seasonAnchor is null) {
                continue;
            }

            if (seasonAnchor.Attributes.Count > 1) {
                seasonAnchor = doc.FindFrom("a", seasonAnchor.StartOffset + 10, Compare.Key("href"));
                if (seasonAnchor is null) {
                    continue;
                }
            }
            
            string url = seasonAnchor.GetAttribute("href") ?? "";
            if (url.StartsWith("/download/")) {
                season.hasPack = true;
                season.packageDownloadUrl = url;
            }
            seasons.Add(season);
        }

        // Console.WriteLine($"Has unclassified episodes: {hasUnclassified}");
        int seasonIndex = -1;
        // Scrape episodes
        foreach (var tr in tableRows) {
            if (tr.Attributes.Count == 0) {
                seasonIndex++;
                continue;
            }

            string? prop = tr.GetAttribute("itemprop");
            if (prop is not "episode") {
                continue;
            }

            Tag? spanEpisodeNumber = doc.FindFrom("span", tr.StartOffset + 10, 
                Compare.Exact("itemprop", "episodeNumber"));
            if (spanEpisodeNumber is null) {
                continue;
            }

            var episode = new Episode();
            string episodeStr = doc.ExtractText(spanEpisodeNumber);
            try {
                episode.number = int.Parse(episodeStr);
            }
            catch { continue; }
            
            Tag? episodeInfo = doc.FindFrom("a", spanEpisodeNumber.StartOffset + 10, 
                Compare.Exact("itemprop", "url"),
                Compare.Key("href"));
            if (episodeInfo is null || spanEpisodeNumber.EndOffset + 10 < episodeInfo.StartOffset) {
                // Extract td after tr
                Tag? td = doc.FindFrom("td", tr.StartOffset + 10);
                var dirtyName = doc.ExtractText(td ?? tr);
                int dot = dirtyName.IndexOf('.');
                if (dot != 1) {
                    episode.name = dirtyName[(dot + 2)..].Trim();
                    seasons[seasonIndex].episodes.Add(episode);
                }
                continue;
            }
            episode.url = episodeInfo.GetAttribute("href") ?? "";

            Tag? episodeName = doc.FindFrom("span", episodeInfo.StartOffset + 10,
                Compare.Exact("itemprop", "name"));
            if (episodeName is null) {
                continue;
            }
            episode.name = doc.ExtractText(episodeName);
            
            Season season = seasons[seasonIndex];
            season.episodes.Add(episode);
        }

        return seasons;
    }
    
    private static int indexOf(StringBuilder str, char chr) {
        for (int i = 0; i < str.Length; i++) {
            if (str[i] == chr) {
                return i;
            }
        }

        return -1;
    }
    
    private static string fixTitle(string title) {
        StringBuilder fixedTitle = new(title);
        fixedTitle.Replace('\n', ' ');
        fixedTitle.Replace("\t", "");
        int quoteSt = indexOf(fixedTitle, '"');
        if (quoteSt != -1 && quoteSt + 1 < fixedTitle.Length && fixedTitle[quoteSt + 1] == ' ') {
            fixedTitle.Remove(quoteSt + 1, 1);
        }
        
        title = fixedTitle.ToString();
        return title.Trim();
    }

    public static List<Language> ScrapeSubtitleLanguages(string html) {
        HtmlDoc doc = new HtmlDoc(html);
        int selectIndex = html.IndexOf("<select", StringComparison.Ordinal);
        var ul = doc.FindFrom("select", selectIndex, 
            Compare.Exact("name", "SubLanguageID"), Compare.Exact("id", "SubLanguageID")
        );
        if (ul == null) {
            return new List<Language>();
        }
        List<Language> languages = new List<Language>(128);
        List<Tag> listElements = doc.ExtractTags(ul, "option");
        foreach (Tag option in listElements) {
            string name = doc.ExtractText(option);
            string code = option.GetAttribute("value") ?? "";
            var lang = new Language(name, code);
            languages.Add(lang);
        }
        return languages;
    }
}

public struct Language {
    private const int LANGUAGE_NAME_LENGTH = 24;
    private readonly string name;
    private readonly string code;

    public Language(string name, string code) {
        this.name = name;
        this.code = code;
    }

    public override string ToString() {
        StringBuilder builder = new StringBuilder(name);
        int diff = LANGUAGE_NAME_LENGTH - name.Length;
        for (int i = 0; i < diff; i++) {
            builder.Append(' ');
        }
        builder.Append(code);
        return builder.ToString();
    }
}

public class SubtitleRow {
    private const string DOWNLOAD_URL = "https://dl.opensubtitles.org/en/download/sub/";
    // title is either movie name or episode name
    public string broadcastTitle = "";
    public string baseFilename = "";
    public string format = "";

    public string downloadURL = "";
    
    public ulong downloads;
    public double rating;

    public override string ToString() {
        return $"{broadcastTitle} {getDownloadURL()} format:{format} rating:{rating} downloads:{downloads}";
    }
    public string ToStringAsElement() {
        return $"{getDownloadURL()} format:{format} downloads:{downloads}";
    }

    public void setDownloadURL(string href) {
        int slash = href.LastIndexOf('/');
        if (slash == -1) {
            downloadURL = href;
        }
        string sub_id = href[(slash+1)..];
        downloadURL = $"{DOWNLOAD_URL}{sub_id}";
    }

    public string getDownloadURL() {
        return downloadURL;
    }
    
}

public class Season {
    private const string DOMAIN = "https://opensubtitles.org";
    public int number;
    public List<Episode> episodes = new();

    public bool hasPack;
    public string packageDownloadUrl = "";

    public string getPackUrl() {
        return $"{DOMAIN}{packageDownloadUrl}";
    }
    
    public override string ToString() {
        if (hasPack) {
            return $"S{number} {DOMAIN}{packageDownloadUrl}";
        }
        return $"S{number}";
    }
}
public class Episode {
    private const string DOMAIN = "https://dl.opensubtitles.org";
    public int number;
    public string name = "";
    public string url = "";

    public string getPageUrl() {
        return $"{DOMAIN}{url}";
    }
}