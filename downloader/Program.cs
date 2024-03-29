using System.Text;

namespace subtitle_downloader.downloader;

class Program {
    private const string VERSION = "1.0.0";
    public static void Main(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return;
        }

        if (args.Length == 1) {
            Console.WriteLine($"Specify production details in the following format: {ParsedSubtitle.FORMAT}");
            return;
        }

        string remaining = concatenateRemainingArgs(args, 1);
        var subtitle = ParsedSubtitle.Parse(remaining);

        var api = new SubtitleAPI();
        List<Production> productions = api.getSuggestedMovies(subtitle.title);
        if (productions.Count == 0) {
            Console.WriteLine("No productions found, implement fallback?");
            return;
        }
        
        Production prod = selectProduction(productions, subtitle);
        Console.WriteLine("Selected: " + prod);
        string pageUrl = createSubtitleUrl(args[0], prod.id);

        if (subtitle.isMovie) {
            downloadSubtitle(api, pageUrl);
        } else {
            string seasonsHtml = api.fetchHtml(pageUrl);
            List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
            Console.WriteLine($"Scraped {seasons.Count} seasons");
            Episode episode = getRequestedEpisode(seasons, subtitle.season, subtitle.episode);
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
        download.Wait();
        
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
        uint seasonIndex = seasonNum - 1;
        if (seasonIndex >= seasons.Count) {
            Console.WriteLine($"Season {seasonNum} was requested but only {seasons.Count} were found");
            prettyPrint(seasons);
            Environment.Exit(0);
        }

        Season season = seasons[(int)seasonIndex];
        uint episodeIndex = episodeNum - 1;
        if (episodeIndex >= seasons.Count) {
            Console.WriteLine($"Episode {episodeNum} was requested but only {season.episodes.Count} were found");
            prettyPrint(seasons);
            Environment.Exit(0);
        }

        return season.episodes[(int)episodeIndex];
    }

    private static void prettyPrint(List<Season> seasons) {
        
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
        // Choose based on total (popularity?)
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

        return max == 0 ? rows[0] : bestSubtitle;
    }

    private static string concatenateRemainingArgs(string[] args, int from) {
        var concatenated = new StringBuilder();
        for (int i = from; i < args.Length; i++) {
            if (args[i].Length == 0) {
                continue;
            }
            if (i != from) {
                concatenated.Append(' ');
            }
            concatenated.Append(args[i]);
        }
        return concatenated.ToString();
    }
    
    private static Production selectProduction(List<Production> productions, ParsedSubtitle desiredSubtitle) {
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

    private static void PrintHelp() {
        Console.WriteLine($"Subtitle downloader (OpenSubtitles) v{VERSION}");
        Console.WriteLine("Commands:");
        Console.WriteLine("    <language> <movie name> (<year>)");
        Console.WriteLine("    <language> <show name> (<year>) S<season> E<episode>");
        Console.WriteLine("    <language> <show name> S<season> E<episode>");

        Console.WriteLine("Usage example:");
        Console.WriteLine("  subtitles french The Godfather (1972)");
        Console.WriteLine("  subtitles french Office (2005) S9 E19");
        Console.WriteLine("  subtitles french fast and the furious");
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