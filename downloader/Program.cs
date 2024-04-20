
using System.Diagnostics;
using System.Text;

namespace subtitle_downloader.downloader;

class Program {
    public const string VERSION = "1.2.2";
    public static void Main(string[] args) {
        switch (args.Length) {
            case 0:
            case 1 when args[0].StartsWith("-h"):
                Arguments.PrintHelp();
                return;
            case 1 when args[0].StartsWith("-v"):
                Console.WriteLine(VERSION);
                return;
        }

        var arguments = Arguments.Parse(args);
        Console.WriteLine(arguments);
        if (!arguments.Validate()) {
            Console.WriteLine("Invalid arguments detected. Exiting.");
            return;
        }
        
        var api = new SubtitleAPI();
        List<Production> productions = api.getSuggestedMovies(arguments.title);
        if (productions.Count == 0) {
            Console.WriteLine("No suggested productions found, falling back to search");
            productions = api.searchSubtitle(arguments);
            if (productions.Count == 0) {
                Console.WriteLine("Fallback failed!");
                return;
            }
        }
        
        Production prod = selectProduction(productions, arguments);
        Console.WriteLine("Selected: " + prod);
        string pageUrl = createSubtitleUrl(arguments.language, prod.id);

        if (arguments.isMovie) {
            downloadSubtitle(api, pageUrl);
        } else {
            string seasonsHtml = api.fetchHtml(pageUrl);
            List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
            if (arguments.listSeries) {
                prettyPrint(seasons);
            }
            Episode episode = getRequestedEpisode(seasons, arguments.season, arguments.episode);
            Console.WriteLine($"\"{episode.name}\" S{arguments.season} E{episode.number}");
            if (episode.url.Length == 0) {
                Console.WriteLine("This episode has no subtitles for given language, try again with --lang all");
                return;
            }
            downloadSubtitle(api, episode.getPageUrl(), episode.name);
        }
        
        Console.WriteLine("Finished");
    }

    private static void downloadSubtitle(SubtitleAPI api, string pageURL, string? fileName = null) {
        string html = api.fetchHtml(pageURL);
        Console.WriteLine($"Scraping: {pageURL}");

        List<SubtitleRow> rows = SubtitleScraper.ScrapeSubtitleTable(html);
        if (rows.Count == 0) {
            Console.WriteLine("No subtitle elements were scraped");
            return;
        }
        SubtitleRow bestSubtitle = selectSubtitle(rows);
        Console.WriteLine(bestSubtitle);
        if (fileName is null) {
            fileName = bestSubtitle.broadcastTitle;
        }

        fileName = sanitizeFileName(fileName);
        var download = api.downloadSubtitle(bestSubtitle, fileName);
        try {
            download.Wait();
        }
        catch (Exception e) {
            Console.WriteLine(e.Message);
            Environment.Exit(0);
        }
        
        string downloadedZip = fileName + ".zip";
         
        if (download.Result) {
            Console.WriteLine("Unzipping..");
            UnzipFile(downloadedZip);
        }
        Console.WriteLine("Cleaning up..");
        File.Delete(downloadedZip);
        cleanupNFOs();
    }

    private static string sanitizeFileName(string fileName) {
        StringBuilder str = new(fileName.Length);
        foreach (char chr in fileName) {
            switch (chr) {
                case '<':
                case '>':
                case ':':
                case '/':
                case '\\':
                case '"':
                case '|':
                case '?':
                case '*':
                    break;
                default:
                    str.Append(chr);
                    break;
            }
        }
        return str.ToString();
    }

    private static Episode getRequestedEpisode(List<Season> seasons, uint seasonNum, uint episodeNum) {
        int seasonIndex = -1;
        for (var i = 0; i < seasons.Count; i++) {
            if (seasons[i].number == seasonNum) {
                seasonIndex = i;
                break;
            }
        }
        
        if (seasonIndex == -1) {
            Console.WriteLine($"Season {seasonNum} wasn't found in {seasons.Count} seasons scraped");
            Environment.Exit(0);
        }

        Season season = seasons[seasonIndex];
        foreach (var episode in season.episodes) {
            if (episode.number == episodeNum) {
                return episode;
            }
        }
        Console.WriteLine($"Episode {episodeNum} wasn't found in {season.episodes.Count} episodes scraped");
        Environment.Exit(0);
        throw new UnreachableException();
    }

    private static void prettyPrint(List<Season> seasons) {
        foreach (var season in seasons) {
            Console.WriteLine($"Season [{season.number}] Episodes: {season.episodes.Count}");
            foreach (var episode in season.episodes) {
                Console.WriteLine($"  {episode.number}. {episode.name}");
            }
            Console.WriteLine("----------------------------------------------");
        }
    }
    
    private static string createSubtitleUrl(string language, uint prodId) {
        string languageId = SubtitleAPI.toSubLanguageID(language);
        // Console.WriteLine("Language id: " + languageId);
        return $"https://www.opensubtitles.org/en/search/sublanguageid-{languageId}/idmovie-{prodId}";
    }

    private static void cleanupNFOs() {
        string[] files = Directory.GetFiles(".");
        foreach (var file in files) {
            if (file.EndsWith(".nfo")) {
                File.Delete(file);
                break;
            }
        }
    }

    private static SubtitleRow selectSubtitle(List<SubtitleRow> rows) {
        sortSubtitleByDownloads(rows);
        if (USER_SELECT_SUBTITLE) {
            return userSelectsSubtitle(rows);
        }

        return selectBestSubtitle(rows);
    }

    private static void sortSubtitleByDownloads(List<SubtitleRow> rows) {
        rows.Sort((e1, e2) => e2.downloads.CompareTo(e1.downloads));
    }

    private static SubtitleRow userSelectsSubtitle(List<SubtitleRow> rows) {
        for (int i = 0; i < rows.Count; i++) {
            var prod = rows[i];
            Console.WriteLine($"#{i+1} " + prod.ToStringNoTitle());
        }

        
        if (rows.Count > 0) {
            string title = rows[0].broadcastTitle;
            Console.WriteLine($"{title}");
        }
        Console.WriteLine($"Select subtitle to download ({1}-{rows.Count}):");
        while (true) {
            string? input = Console.ReadLine();
            if (input is null || input.Length == 0) {
                Console.WriteLine("Specify a number");
                continue;
            }

            int num;
            if (!int.TryParse(input, out num)) {
                Console.WriteLine("Specify a number");
                continue;
            }

            if (num < 1 || num > rows.Count) {
                Console.WriteLine("Out of bounds number");
                continue;
            }
            return rows[num - 1];
        }
    }

    private static SubtitleRow selectBestSubtitle(List<SubtitleRow> rows) {
        SubtitleRow bestSubtitle = new SubtitleRow();
        double max = 0;
        foreach (var subtitle in rows) {
            if (subtitle.format != "" && subtitle.format != "srt" ) {
                continue;
            }
            if (subtitle.rating > max) {
                max = subtitle.rating;
                bestSubtitle = subtitle;
            }
        }

        if (max == 0) {
            // Choose based on most downloads (popularity)
            foreach (var subtitle in rows) {
                if (subtitle.format != "" && subtitle.format != "srt" ) {
                    continue;
                }
                if (subtitle.downloads > max) {
                    max = subtitle.downloads;
                    bestSubtitle = subtitle;
                }
            }
        }
        return max == 0 ? rows[0] : bestSubtitle;
    }

    private const bool USER_SELECT_SUBTITLE = true;
    private const bool USER_SELECT_PRODUCTION = false;
    

    private static Production selectProduction(List<Production> productions, Arguments desiredSubtitle) {
        keepKind(productions, desiredSubtitle.isMovie ? "movie" : "tv");
        switch (productions.Count) {
            case 0:
                throw new Exception("No productions remained after filtering");
            case 1:
                return productions[0];
        }

        if (USER_SELECT_PRODUCTION) {
            return userSelectsProduction(productions);
        }
        return selectBestProduction(productions, desiredSubtitle);
    }

    private static Production userSelectsProduction(List<Production> productions) {
        Console.WriteLine($"Select production ({1}-{productions.Count}):");
        for (int i = 0; i < productions.Count; i++) {
            var prod = productions[i];
            Console.WriteLine($"#{i+1} " + prod);
        }
        while (true) {
            string? input = Console.ReadLine();
            if (input is null || input.Length == 0) {
                Console.WriteLine("Specify a number");
                continue;
            }

            int num;
            if (!int.TryParse(input, out num)) {
                Console.WriteLine("Specify a number");
                continue;
            }

            if (num < 1 || num > productions.Count) {
                Console.WriteLine("Out of bounds number");
                continue;
            }
            return productions[num - 1];
        }
    } 
    
    private static Production selectBestProduction(List<Production> productions, Arguments desiredSubtitle) {
        if (desiredSubtitle.year != 0) {
            bool hasMatchingYear = false;
            foreach (var production in productions) {
                if (production.year == 0) {
                    // If year is not given by the API ignore this constraint
                    hasMatchingYear = true;
                    break;
                }

                if (production.year == desiredSubtitle.year) {
                    hasMatchingYear = true;
                    break;
                }
            }

            if (!hasMatchingYear) {
                Console.WriteLine($"No production found where year is matching, given: {desiredSubtitle.year}");
                foreach (var prod in productions) {
                    Console.WriteLine(prod);
                }

                Environment.Exit(0);
            }
        }

        // NAME&YEAR
        foreach (var production in productions) {
            if (production.name == desiredSubtitle.title && production.year == desiredSubtitle.year) {
                return production;
            }
        }

        // LOWERCASE NAME&YEAR
        foreach (var production in productions) {
            if (string.Equals(production.name, desiredSubtitle.title, StringComparison.CurrentCultureIgnoreCase)
                && production.year == desiredSubtitle.year) {
                return production;
            }
        }

        // just NAME
        foreach (var production in productions) {
            if (production.name == desiredSubtitle.title) {
                return production;
            }
        }

        // Choose based on total (popularity?)
        Production bestProduction = new Production();
        uint max = 0;
        foreach (var production in productions) {
            if (production.total > max) {
                max = production.total;
                bestProduction = production;
            }
        }
        return bestProduction;
    }

    private static void keepKind(List<Production> productions, string kindToKeep) {
        int i = 0;
        while (i < productions.Count) {
            var prod = productions[i];
            if (prod.kind != kindToKeep) {
                productions.RemoveAt(i);
                continue;
            }
            i++;
        }
    }

    // Extract .zip that contains the .srt files
    private static void UnzipFile(string zipPath) {
        try {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, ".");
        } catch (IOException io) {
            Console.WriteLine(io.Message);
        }
    }
}