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
            
            var anchor = doc.FindFrom("a", strong.StartOffset, true);
            if (anchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }

            var href = anchor.GetAttribute("href");
            if (href != null) {
                subtitleRow.href = href;
            }
            
            var spanTitle = doc.FindFrom("span", anchor.StartOffset, true);
            if (spanTitle is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            
            var title = spanTitle.GetAttribute("title");
            if (title != null) {
                subtitleRow.subtitleName = title;
            }

            var flagDiv = doc.FindFrom("div", spanTitle.StartOffset, true);
            if (flagDiv is null || flagDiv.Attributes.Count == 1) {
                subtitles.Add(subtitleRow);
                continue;
            }
            
            var flag = flagDiv.GetAttribute("class");
            if (flag != null) {
                subtitleRow.flag = flag;
            }

            var downloadAnchor = doc.FindFrom("a", flagDiv.StartOffset, true);
            if (downloadAnchor is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            
            var classHref = downloadAnchor.GetAttribute("class");
            if (classHref != null) {
                subtitleRow.downloadURL = classHref;
            }

            string times = doc.ExtractText(downloadAnchor);
            int x = times.IndexOf('x');
            if (x != -1) {
                subtitleRow.downloads = int.Parse(times[..x]);
            }
            
            var spanFormat = doc.FindFrom("span", downloadAnchor.StartOffset, false, ("class", "p"));
            if (spanFormat is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.format = doc.ExtractText(spanFormat);
            
            var spanVotes = doc.FindFrom("span", spanFormat.StartOffset + 4, true);
            if (spanVotes is null) {
                subtitles.Add(subtitleRow);
                continue;
            }
            subtitleRow.rating = double.Parse(doc.ExtractText(spanVotes));
            
            subtitles.Add(subtitleRow);
        }
        return subtitles;
    }
    
    
}

public class SubtitleRow {
    public string productionTitle = "";
    public string subtitleName = "";
    public string href = "";
    public string flag = "";
    public string downloadURL = "";
    public string format = "";

    public double rating;
    public int downloads;
}