using WebScrapper.scrapper;

namespace subtitle_downloader.downloader; 

public class SubtitleScraper {
    
    public static List<SubtitleRow> ScrapeTableMovie(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            Console.WriteLine("Table is not in the page?");
            return new List<SubtitleRow>();
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
                movieTitle = doc.ExtractText(strong)
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


    public static Season ScrapeTableSeries(string html, uint seasonNumber) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            throw new Exception("<tbody> not found in the page, how's the page structured?");
        }

        List<Tag> tableRows = doc.ExtractTags(tableBody, "tr");


        Season season = new Season();
        foreach (var tr in tableRows) {
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
            
            if (season.number == seasonNumber) {
                break;
            }
        }

        return season;
    }
}

public class SubtitleRow {
    private const string DOWNLOAD_URL = "https://dl.opensubtitles.org/en/download/sub/";
    // title is either movie name or episode name
    public string movieTitle = "";
    public string downloadURL = "";
    public string format = "";
    public string flag = "";

    public double rating;
    public int downloads;
    
    public void fixTitle() {
        movieTitle = movieTitle.Replace('\n', ' ');
    }

    public override string ToString() {
        
        return $"{movieTitle} {getFullURL()} format:{format} rating:{rating} downloads:{downloads}";
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
    public int number = 0;
    
    public bool wholeDownload = false;
    public string packageDownloadUrl = "";

    public override string ToString() {
        if (wholeDownload) {
            return $"S{number} {DOMAIN}{packageDownloadUrl}";
        }
        return $"S{number}";
    }
}
public class Episode {
    public int number = 0;
    public string url = "";
}