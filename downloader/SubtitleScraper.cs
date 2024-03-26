using WebScrapper.scrapper;

namespace subtitle_downloader.downloader; 

public class SubtitleScraper {
    
    public static List<SubtitleRow> ScrapeTable(string html) {
        var doc = new HtmlDoc(html);
        Tag? tableBody = doc.Find("tbody");
        if (tableBody is null) {
            Console.WriteLine("Table is not in the page?");
            return new List<SubtitleRow>();
        }

        var subtitles = new List<SubtitleRow>();
        List<Tag> tableRows = doc.FindAllFrom("tr", tableBody.StartOffset, true);
        foreach (var tr in tableRows) {
            var strong = doc.FindFrom("strong", tr.StartOffset, true);
            if (strong is null) {
                continue;
            }
            SubtitleRow subtitleRow = new SubtitleRow {
                productionTitle = doc.ExtractText(strong)
            };
            subtitleRow.fixTitle();


            var time = doc.FindFrom("time", strong.EndOffset + 100, true);
            if (time is null) {
                continue;
            }
            // Perhaps store time uploaded

            var anchor = doc.FindFrom("a", time.StartOffset, true);
            if (anchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            
            anchor = doc.FindFrom("a", time.StartOffset + 100, true);
            if (anchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            var href = anchor.GetAttribute("href");
            if (href != null) {
                subtitleRow.downloadURL = href;
            }

            var formatSpan = doc.FindFrom("span", anchor.StartOffset, true);
            if (formatSpan is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.format = doc.ExtractText(formatSpan);
            
            var ratingSpan = doc.FindFrom("span", formatSpan.StartOffset + 20, true);
            if (ratingSpan is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.rating = double.Parse(doc.ExtractText(ratingSpan));
            
            /*var flagDiv = doc.FindFrom("div", formatSpan.StartOffset, true);
            if (flagDiv is null || flagDiv.Attributes.Count == 1) {
                subtitles.Add(subtitleRow);
                continue;
            }*/
            
            /*var flag = flagDiv.GetAttribute("class");
            if (flag != null) {
                subtitleRow.flag = flag;
            }

            var downloadAnchor = doc.FindFrom("a", flagDiv.StartOffset, true);
            if (downloadAnchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }

            string times = doc.ExtractText(downloadAnchor);
            int x = times.IndexOf('x');
            if (x != -1) {
                subtitleRow.downloads = int.Parse(times[..x]);
            }*/
            
            
            subtitles.Add(subtitleRow);
        }
        return subtitles;
    }
    
    
}

public class SubtitleRow {
    private const string DOWNLOAD_URL = "https://dl.opensubtitles.org/en/download/sub/";
    public string productionTitle = "";
    public string downloadURL = "";
    public string format = "";
    public string flag = "";

    public double rating;
    public int downloads;
    
    public void fixTitle() {
        productionTitle = productionTitle.Replace('\n', ' ');
    }

    public override string ToString() {
        
        return $"{productionTitle} {getFullURL()} format:{format} rating:{rating}";
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