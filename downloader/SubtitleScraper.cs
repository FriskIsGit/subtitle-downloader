using WebScrapper.scrapper;

namespace subtitle_downloader.downloader; 

public class SubtitleScraper {
    
    public static List<SubtitleRow> ScrapeSubtitleTable(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            Console.WriteLine("Table is not in the page?");
            return ScrapeDownloadButton(doc);
        }

        var subtitles = new List<SubtitleRow>();
        List<Tag> tableRows = doc.ExtractTags(tableBody, "tr");
        foreach (var tr in tableRows) {
            var id = tr.GetAttribute("id");
            if (id is null || !id.StartsWith("name")) {
                continue;
            }
            
            var strong = doc.FindFrom("strong", tr.StartOffset + 100);
            if (strong is null) {
                continue;
            }
            SubtitleRow subtitleRow = new SubtitleRow {
                broadcastTitle = doc.ExtractText(strong)
            };
            subtitleRow.fixTitle();
            
            var flagDiv = doc.FindFrom("div", strong.EndOffset, ("class", "flag", Compare.VALUE_STARTS_WITH));
            if (flagDiv != null) {
                subtitleRow.flag = flagDiv.GetAttribute("class") ?? "";
            } else {
                subtitles.Add(subtitleRow);
                continue;
            }
            
            var downloadAnchor = doc.FindFrom("a", flagDiv.StartOffset + 10, 
                ("href", "", Compare.KEY_ONLY), ("onclick", "", Compare.KEY_ONLY));
            if (downloadAnchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.downloadURL = downloadAnchor.GetAttribute("href") ?? "";
            string timesDownloaded = doc.ExtractText(downloadAnchor);
            int x = timesDownloaded.IndexOf('x');
            try {
                subtitleRow.downloads = int.Parse(timesDownloaded[..x]);
            }catch { }

            var extensionSpan = doc.FindFrom("span", downloadAnchor.EndOffset, ("class", "p", Compare.EXACT));
            if (extensionSpan is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.format = doc.ExtractText(extensionSpan);
            
            var ratingSpan = doc.FindFrom("span", extensionSpan.EndOffset, ("title", "", Compare.KEY_ONLY));
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

    private static List<SubtitleRow> ScrapeDownloadButton(HtmlDoc doc) {
        Tag? downloadAnchor = doc.Find("a", 
            ("download", "download", Compare.EXACT),
            ("href", "", Compare.KEY_ONLY));
        if (downloadAnchor is null) {
            Console.WriteLine("Download anchor not found");
            return new List<SubtitleRow>();
        }

        SubtitleRow subtitle = new SubtitleRow();
        subtitle.downloadURL = downloadAnchor.GetAttribute("href") ?? "";
        subtitle.broadcastTitle = downloadAnchor.GetAttribute("data-product-title") ?? "";
        
        return new List<SubtitleRow>(1) { subtitle };
    }


    public static List<Season> ScrapeSeriesTable(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            throw new Exception("<tbody> not found in the page, how's the page structured?");
        }

        List<Tag> tableRows = doc.ExtractTags(tableBody, "tr");

        List<Season> seasons = new();
        
        // 1. Scrape season packages
        foreach (var tr in tableRows) {
            Season season = new Season();
            if (tr.Attributes.Count != 0) {
                continue;
            }

            Tag? seasonTag = doc.FindFrom("span", tr.StartOffset + 2, ("id", "season", Compare.VALUE_STARTS_WITH));
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
            }
            catch { }

            Tag? seasonAnchor = doc.FindFrom("a", seasonTag.StartOffset + 10, ("href", "", Compare.KEY_ONLY));
            if (seasonAnchor is null) {
                continue;
            }

            if (seasonAnchor.Attributes.Count > 1) {
                seasonAnchor = doc.FindFrom("a", seasonAnchor.StartOffset + 10, ("href", "", Compare.KEY_ONLY));
                if (seasonAnchor is null) {
                    continue;
                }
            }
            
            string url = seasonAnchor.GetAttribute("href") ?? "";
            if (url.StartsWith("/download/")) {
                season.wholeDownload = true;
                season.packageDownloadUrl = url;
            }
            seasons.Add(season);
        }

        int seasonNumber = 0;
        // Scrape episodes
        foreach (var tr in tableRows) {
            if (tr.Attributes.Count == 0) {
                seasonNumber++;
                continue;
            }

            string? prop = tr.GetAttribute("itemprop");
            if (prop is not "episode") {
                continue;
            }

            Tag? spanEpisodeNumber = doc.FindFrom("span", tr.StartOffset + 10, ("itemprop", "episodeNumber", Compare.EXACT));
            if (spanEpisodeNumber is null) {
                continue;
            }

            Episode episode = new Episode();
            string episodeStr = doc.ExtractText(spanEpisodeNumber);
            try {
                episode.number = int.Parse(episodeStr);
            }
            catch { continue; }
            
            Tag? episodeInfo = doc.FindFrom("a", spanEpisodeNumber.StartOffset + 10, 
                ("itemprop", "url", Compare.EXACT),
                ("href", "", Compare.KEY_ONLY));
            if (episodeInfo is null) {
                continue;
            }
            episode.url = episodeInfo.GetAttribute("href") ?? "";

            Tag? episodeName = doc.FindFrom("span", episodeInfo.StartOffset + 10,
                ("itemprop", "name", Compare.EXACT));
            if (episodeName is null) {
                continue;
            }
            episode.name = doc.ExtractText(episodeName);

            int seasonIndex = seasonNumber - 1;
            Season season = seasons[seasonIndex];
            season.episodes.Add(episode);
        }

        return seasons;
    }
}

public class SubtitleRow {
    private const string DOWNLOAD_URL = "https://dl.opensubtitles.org/en/download/sub/";
    // title is either movie name or episode name
    public string broadcastTitle = "";
    public string downloadURL = "";
    public string format = "";
    public string flag = "";

    public double rating;
    public int downloads;
    
    public void fixTitle() {
        broadcastTitle = broadcastTitle.Replace('\n', ' ');
    }

    public override string ToString() {
        return $"{broadcastTitle} {getFullURL()} format:{format} rating:{rating} downloads:{downloads}";
    }

    public string getFullURL() {
        return $"{DOWNLOAD_URL}{getLastPart()}";
    }

    private string getLastPart() {
        int slash = downloadURL.LastIndexOf('/');
        if (slash == -1) {
            return downloadURL;
        }
        return downloadURL[(slash+1)..];
    }
}

public class Season {
    private const string DOMAIN = "https://opensubtitles.org";
    public int number;
    public List<Episode> episodes = new();

    public bool wholeDownload;
    public string packageDownloadUrl = "";

    public override string ToString() {
        if (wholeDownload) {
            return $"S{number} {DOMAIN}{packageDownloadUrl}";
        }
        return $"S{number}";
    }
}
public class Episode {
    private const string DOMAIN = "https://opensubtitles.org";
    public int number = 0;
    public string name = "";
    public string url = "";

    public string getPageUrl() {
        return $"{DOMAIN}{url}";
    }
}