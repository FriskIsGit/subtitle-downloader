using System.Diagnostics;

namespace subtitle_downloader.downloader;

class Program {
    public const string VERSION = "1.6.0";
    public static void Main(string[] args) {
        switch (args.Length) {
            case 0:
            case 1 when args[0].Equals("-h") || args[0].Equals("-help") || args[0].Equals("--help"):
                Arguments.PrintHelp();
                return;
            case 1 when args[0].Equals("-v") || args[0].StartsWith("--ver"):
                Console.WriteLine(VERSION);
                return;
            case 1 when args[0].Equals("-languages"):
                Arguments.PrintLanguages();
                return;
        }

        var arguments = Arguments.Parse(args);
        if (!arguments.Validate()) {
            Console.WriteLine("Invalid arguments detected. Exiting.");
            return;
        }

        // n.ext
        if (arguments.subtitlePath.Length > 0) {
            // Read subtitle file and parse
            // Shift if needed
            // Serialize (unless shift == 0 and the format is the same)
            var (subtitles, exception) = Converter.parseSRT(arguments.subtitlePath);
            if (exception != null) {
                Console.WriteLine("FAILED TO PARSE: " + exception);
                return;
            }

            if (arguments.shift != 0) {
                if (arguments.shift > 0) {
                    foreach (Subtitle sub in subtitles) {
                        sub.shiftForwardBy(arguments.shift);
                    }
                }
                else {
                    foreach (Subtitle sub in subtitles) {
                        sub.shiftBackwardBy(arguments.shift);
                    }
                }
            }
            
            return;
        }
        var api = new SubtitleAPI();
        List<Production> productions = api.getSuggestedMovies(arguments.title);
        if (productions.Count == 0) {
            Console.WriteLine("No suggested productions found, falling back to search");
            productions = api.searchProductions(arguments);
            if (productions.Count == 0) {
                Console.WriteLine("Fallback failed!");
                return;
            }
        }
        
        Production prod = selectProduction(productions, arguments);
        Console.WriteLine("Selected: " + prod);
        string pageUrl = createSubtitleUrl(arguments.language, prod.id);

        if (arguments.isMovie) {
            downloadSubtitle(api, pageUrl, arguments);
        } else {
            string seasonsHtml = api.fetchHtml(pageUrl).content;
            List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
            if (arguments.listSeries) {
                prettyPrint(seasons);
            }
            Episode episode = getRequestedEpisode(seasons, arguments.season, arguments.episode);
            Console.WriteLine($"\"{episode.name}\" S{arguments.season} E{episode.number}");
            if (episode.url.Length == 0) {
                FailExit("This episode has no subtitles for given language, try again with --lang all");
            }
            downloadSubtitle(api, episode.getPageUrl(), arguments, episode.name);
        }
        
        Console.WriteLine("Finished");
    }

    // Returns the full path to the downloaded subtitle file, if there is more than one - returns the first found
    private static string downloadSubtitle(SubtitleAPI api, string pageURL, Arguments args, string? fileName = null) {
        string html = api.fetchHtml(pageURL).content;
        Console.WriteLine($"Scraping: {pageURL}");

        List<SubtitleRow> rows = SubtitleScraper.ScrapeSubtitleTable(html);
        if (rows.Count == 0) {
            FailExit("No subtitle elements were scraped");
        }
        SubtitleRow bestSubtitle = selectSubtitle(rows, args);

        if (fileName is null) {
            fileName = bestSubtitle.broadcastTitle;
        }
        fileName = Utils.sanitizeFileName(fileName);
        string outputDir = Utils.correctOutputDirectory(args.outputDirectory);
        string downloadedZip = outputDir == "." ? fileName + ".zip" : Path.Combine(outputDir, fileName) + ".zip";
        var download = api.downloadSubtitle(bestSubtitle, downloadedZip);
        try {
            download.Wait();
        }
        catch (Exception e) {
            FailExit(e.Message);
        }

        if (!download.Result) {
            FailExit("The download failed!");
        }
        
        Console.WriteLine("Unzipping " + downloadedZip);
        List<string> extracted = Utils.unzip(downloadedZip, outputDir);
        if (extracted.Count == 0) {
            FailExit("THE ZIP IS EMPTY - No elements were extracted from the zip file!");
        }
        Console.WriteLine("Cleaning up..");
        File.Delete(downloadedZip);
        return extracted[0];
    }

    private static void FailExit(string message) {
        Console.WriteLine(message);
        Environment.Exit(1);
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
            FailExit($"Season {seasonNum} wasn't found in {seasons.Count} seasons scraped");
        }

        Season season = seasons[seasonIndex];
        foreach (var episode in season.episodes) {
            if (episode.number == episodeNum) {
                return episode;
            }
        }

        FailExit($"Episode {episodeNum} wasn't found in {season.episodes.Count} episodes scraped");
        throw new UnreachableException();
    }

    private static void prettyPrint(List<Season> seasons) {
        foreach (var season in seasons) {
            int seasonNumber = season.number;
            int episodesCount = season.episodes.Count;
            
            if (seasonNumber == -1 && episodesCount == 0) {
                continue;
            }
            Console.WriteLine($"Season [{seasonNumber}] Episodes: {episodesCount}");
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

    private static SubtitleRow selectSubtitle(List<SubtitleRow> rows, Arguments args) {
        sortSubtitleByDownloads(rows);
        return args.skipSelect ? selectBestSubtitle(rows, args.extensionFilter) : userSelectsSubtitle(rows, args.extensionFilter);
    }

    private static void sortSubtitleByDownloads(List<SubtitleRow> rows) {
        rows.Sort((e1, e2) => e2.downloads.CompareTo(e1.downloads));
    }

    private static SubtitleRow userSelectsSubtitle(List<SubtitleRow> sortedRows, string extension) {
        if (sortedRows.Count == 1) {
            Console.WriteLine("Single result, proceeding.");
            return sortedRows[0];
        }

        if (extension != "") {
            List<SubtitleRow> filtered = new List<SubtitleRow>();
            foreach (var sub in sortedRows) {
                // Unknown subtitle formats could be matching
                if (sub.format == "" || sub.format == extension) {
                    filtered.Add(sub);
                }
            }
            if (filtered.Count == 0) {
                FailExit("No subtitles remained after filtering by extension(" + extension + ") :(");
            }
            sortedRows = filtered;
        }
        
        string table = Utils.prettyFormatSubtitlesInTable(sortedRows);
        Console.WriteLine(table);
        
        if (sortedRows.Count > 0) {
            string title = sortedRows[0].broadcastTitle;
            Console.WriteLine($"{title}");
        }
        Console.WriteLine($"Select subtitle to download ({1}-{sortedRows.Count}):");
        while (true) {
            string? input = Console.ReadLine();
            if (input is null || input.Length == 0) {
                Console.WriteLine("Specify a number");
                continue;
            }

            if (!int.TryParse(input, out var num)) {
                Console.WriteLine("Specify a number");
                continue;
            }

            if (num < 1 || num > sortedRows.Count) {
                Console.WriteLine("Out of bounds number");
                continue;
            }
            return sortedRows[num - 1];
        }
    }
    
    private static SubtitleRow selectBestSubtitle(List<SubtitleRow> rows, string extension) {
        bool applyFilter = extension != "";
        foreach (var subtitle in rows) {
            if (applyFilter) {
                if (subtitle.format == extension) {
                    return subtitle; 
                }
                continue;
            }
            if (subtitle.format != "" && subtitle.format != "srt") {
                continue;
            }
            return subtitle;
        }
        Console.WriteLine("WARN: Defaulted to first subtitle.");
        return rows[0];
    }

    private const bool USER_SELECTS_PRODUCTION = false;

    private static Production selectProduction(List<Production> productions, Arguments arguments) {
        keepKind(productions, arguments.isMovie ? "movie" : "tv");
        switch (productions.Count) {
            case 0:
                FailExit("ERROR: No productions remained after filtering");
                break;
            case 1:
                return productions[0];
        }

        return USER_SELECTS_PRODUCTION ? userSelectsProduction(productions) : selectBestProduction(productions, arguments);
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
                // If year is not given by the API ignore this constraint OR compare if present
                if (production.year == 0 || production.year == desiredSubtitle.year) {
                    hasMatchingYear = true;
                    break;
                }
            }

            if (!hasMatchingYear) {
                Console.WriteLine($"No production found where year is matching, given: {desiredSubtitle.year}");
                foreach (var prod in productions) {
                    Console.WriteLine(prod);
                }

                Environment.Exit(1);
            }
        }

        // Exact NAME&YEAR
        foreach (var production in productions) {
            if (production.name == desiredSubtitle.title && production.year == desiredSubtitle.year) {
                return production;
            }
        }

        // IgnoreCase NAME&YEAR
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
        
        // just NAME (ignore case)
        foreach (var production in productions) {
            if (string.Equals(production.name, desiredSubtitle.title, StringComparison.CurrentCultureIgnoreCase)) {
                return production;
            }
        }

        // Choose based on total (popularity?)
        Production bestProduction = productions[0];
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
}