
using System.Diagnostics;

namespace subtitle_downloader.downloader;

class Program {
    public const string VERSION = "1.0.2";
    public static void Main(string[] args) {
        if (args.Length == 0 || args is ["--help"] || args is ["-help"]) {
            Arguments.PrintHelp();
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
            Console.WriteLine("No productions found, implement fallback?");
            api.searchSubtitle(arguments);
            return;
        }
        
        Production prod = selectProduction(productions, arguments);
        Console.WriteLine("Selected: " + prod);
        string pageUrl = createSubtitleUrl(arguments.language, prod.id);

        if (arguments.isMovie) {
            downloadSubtitle(api, pageUrl);
        } else {
            string seasonsHtml = api.fetchHtml(pageUrl);
            List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
            prettyPrint(seasons);
            Episode episode = getRequestedEpisode(seasons, arguments.season, arguments.episode);
            Console.WriteLine($"Episode {episode.number} \"{episode.name}\" {episode.url}");
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

    private const int EPISODE_LIMIT = 20;
    private static void prettyPrint(List<Season> seasons) {
        bool truncated = false;
        foreach (var season in seasons) {
            Console.WriteLine($"Season [{season.number}] Episodes: {season.episodes.Count}");
            for (int e = 0; e < season.episodes.Count && e < EPISODE_LIMIT; e++) {
                Episode episode = season.episodes[e];
                Console.WriteLine($"  {episode.number}. {episode.name}");
            }

            Console.WriteLine("----------------------------------------------");
            if (season.episodes.Count > EPISODE_LIMIT) {
                truncated = true;
            }
        }

        if (truncated) {
            Console.WriteLine($"Episodes were truncated to {EPISODE_LIMIT}");
        }
    }
    
    private static string createSubtitleUrl(string language, uint prodId) {
        string languageId = SubtitleAPI.toSubLanguageID(language);
        Console.WriteLine("Language id: " + languageId);
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

    private static Production selectProduction(List<Production> productions, Arguments desiredSubtitle) {
        productions = filterByKind(productions, desiredSubtitle.isMovie);
        switch (productions.Count) {
            case 0:
                throw new Exception("No productions remained after filtering");
            case 1:
                return productions[0];
        }

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

    private static List<Production> filterByKind(List<Production> productions, bool keepMovies) {
        var filtered = new List<Production>();
        foreach (var prod in productions) {
            if (prod.kind == "movie" && keepMovies) {
                filtered.Add(prod);
            }
            else if (prod.kind == "tv" && !keepMovies) {
                filtered.Add(prod);
            }
        }

        return filtered;
    }

    // Extract .zip that contains the .srt files
    public static void UnzipFile(string zipPath) {
        try {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, ".");
        } catch (IOException io) {
            Console.WriteLine(io.Message);
        }
    }
}