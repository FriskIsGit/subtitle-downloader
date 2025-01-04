using System.Diagnostics;

namespace subtitle_downloader.downloader; 

class ProgramFlow {
    private const int MAX_PACK_SIZE = 50;
    private readonly OpenSubtitleAPI api = new();
    private readonly Arguments args;

    public ProgramFlow(Arguments arguments) {
        args = arguments;
    }

    public void execute() {
        if (args.subtitleFromFile) {
            ensureModificationsRequested();
            processSubtitle(args.subtitlePath);
            return;
        }

        List<Production> productions = fetchProductions();
        Production prod = chooseProduction(productions);
        List<string> paths = fetchSubtitle(prod);
        
        ensureModificationsRequested();
        Console.WriteLine($"Processing {paths.Count} path(s)");
        foreach(string path in paths) {
            processSubtitle(path);
        }
    }

    private void ensureModificationsRequested() {
        if (args.shiftMs == 0 && !args.convert) {
            Utils.OkExit("Finished");
        }
    }

    private void processSubtitle(string path) {
        string originalExtension = Utils.GetExtension(path);
        if (args.shiftMs == 0 && args.convertToExtension == originalExtension) {
            Console.WriteLine("Nothing to do, yet modifications were requested!");
            return;
        }
        
        // Read subtitle file and parse
        var (subtitleFile, exception) = Converter.parse(path, originalExtension);
        if (exception != null) {
            Console.WriteLine("Parsing failure: " + exception.Message);
            if (subtitleFile.count() == 0) {
                Environment.Exit(1);
            }
            // Continue if some subtitles were parsed before failure
        }
        
        if (args.shiftMs != 0) {
            Console.WriteLine("Shifting by " + args.shiftMs + "ms");
            subtitleFile.shiftBy(args.shiftMs);
        }

        string newExtension = args.convert ? args.convertToExtension : originalExtension;
        Console.WriteLine($"Serializing {subtitleFile.count()} subtitle chunks to {newExtension}");
        Converter.serialize(subtitleFile, path, newExtension);
    }

    private List<Production> fetchProductions() {
        List<Production> suggested = api.getSuggestedMovies(args.title);
        if (suggested.Count == 0) {
            Console.WriteLine("No suggested productions found, falling back to search");
            suggested = api.searchProductions(args);
            if (suggested.Count == 0) {
                Utils.FailExit("Fallback failed!");
            }
        }
        return suggested;
    }
    
    private List<string> fetchSubtitle(Production prod) {
        Console.WriteLine("Selected: " + prod);
        string pageUrl = createSubtitleUrl(args.language, prod.id);

        if (args.isMovie) {
            return downloadSubtitle(pageUrl);
        }
        
        // Series fetching logic
        SimpleResponse seasonsResponse = api.fetchHtml(pageUrl);
        if (seasonsResponse.isError()) {
            Utils.FailExit("Failed to fetch seasons. URL: " + seasonsResponse.lastLocation);
        }
        string seasonsHtml = seasonsResponse.content;
        List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
        if (args.listSeries) {
            prettyPrint(seasons);
        }

        Season season = getSeason(seasons, args.season);
        if (args.downloadPack) {
            if (!season.hasPack) {
                Utils.FailExit($"Season number {season.number} has no pack download");
            }

            var episodeCount = season.episodes.Count;
            if (episodeCount > MAX_PACK_SIZE) {
                Utils.FailExit($"This season has {episodeCount} episodes which is more than {MAX_PACK_SIZE}");
            }

            throw new Exception("Unimplemented pack download");
        }
        Episode episode = getEpisode(season, args.episode);
        Console.WriteLine($"\"{episode.name}\" S{args.season} E{episode.number}");
        if (episode.url.Length == 0) {
            Utils.FailExit("This episode has no subtitles for given language, try again with --lang all");
        }
        return downloadSubtitle(episode.getPageUrl(), episode.name);
    }

    // This needs to be separated for download, scrape, selection and unzipping
    // Returns the paths of the downloaded then unzipped files
    private List<string> downloadSubtitle(string pageURL, string? fileName = null) {
        var response = api.fetchHtml(pageURL);
        if (response.isError()) {
            Utils.FailExit("Failed to download subtitle");
        }
        string html = response.content;
        Console.WriteLine($"Scraping: {pageURL}");

        List<SubtitleRow> rows = SubtitleScraper.ScrapeSubtitleTable(html);
        if (rows.Count == 0) {
            Utils.FailExit("No subtitle elements were scraped");
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
            Utils.FailExit(e.Message);
        }

        if (!download.Result) {
            Utils.FailExit("The download failed!");
        }
        
        Console.WriteLine("Unzipping " + downloadedZip);
        List<string> extracted = Utils.unzip(downloadedZip, outputDir);
        if (extracted.Count == 0) {
            Utils.FailExit("No elements were extracted from the zip file! Is the zip empty?");
        }
        Console.WriteLine("Deleting zip.");
        File.Delete(downloadedZip);
        return extracted;
    }

    private static Season getSeason(List<Season> seasons, uint seasonNum) {
        foreach (var season in seasons) {
            if (season.number == seasonNum) {
                return season;
            }
        }
        Utils.FailExit($"Season {seasonNum} wasn't found in {seasons.Count} seasons scraped");
        throw new UnreachableException();
    }
    
    private static Episode getEpisode(Season season, uint episodeNum) {
        foreach (var episode in season.episodes) {
            if (episode.number == episodeNum) {
                return episode;
            }
        }
        Utils.FailExit($"Episode {episodeNum} wasn't found in {season.episodes.Count} episodes scraped");
        throw new UnreachableException();
    }

    private const string SEASON_SEPARATOR = "----------------------------------------------";
    private static void prettyPrint(List<Season> seasons) {
        foreach (var season in seasons) {
            int seasonNumber = season.number;
            int episodesCount = season.episodes.Count;
            
            if (seasonNumber == -1 && episodesCount == 0) {
                continue;
            }

            string packInfo = formatPackInfo(season.hasPack, episodesCount);
            Console.WriteLine($"Season [{seasonNumber}] Episodes: {episodesCount} {packInfo}");
            foreach (var episode in season.episodes) {
                Console.WriteLine($"  {episode.number}. {episode.name}");
            }
            Console.WriteLine(SEASON_SEPARATOR);
        }
    }

    private static string formatPackInfo(bool hasPack, int episodes) {
        if (!hasPack) {
            return "";
        }
        return episodes > MAX_PACK_SIZE ? "[PACK TOO BIG]" : "[PACK AVAILABLE]";
    }
    
    private static string createSubtitleUrl(string language, uint prodId) {
        string languageId = OpenSubtitleAPI.toSubLanguageID(language);
        // Console.WriteLine("Language id: " + languageId);
        return $"https://www.opensubtitles.org/en/search/sublanguageid-{languageId}/idmovie-{prodId}";
    }

    private static SubtitleRow selectSubtitle(List<SubtitleRow> rows, Arguments args) {
        sortSubtitleByDownloads(rows);
        return args.skipSelect
            ? selectBestSubtitle(rows, args.extensionFilter)
            : userSelectsSubtitle(rows, args.extensionFilter);
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
                Utils.FailExit("No subtitles remained after filtering by extension=" + extension + " :(");
            }
            sortedRows = filtered;
        }
        
        string table = Utils.prettyFormatSubtitlesInTable(sortedRows);
        Console.WriteLine(table);
        
        if (sortedRows.Count > 0) {
            string title = sortedRows[0].broadcastTitle;
            Console.WriteLine($"{title}");
        }
        Console.WriteLine($"Select subtitle to download [{1}-{sortedRows.Count}]:");
        while (true) {
            string? input = Console.ReadLine();
            if (input is null || input.Length == 0) {
                Console.WriteLine("Specify a number, try again.");
                continue;
            }

            if (!int.TryParse(input, out int num)) {
                Console.WriteLine("Not a number, try again.");
                continue;
            }

            if (num < 1 || num > sortedRows.Count) {
                Console.WriteLine("Out of bounds number, try again.");
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

    private Production chooseProduction(List<Production> productions) {
        keepKind(productions, args.isMovie ? "movie" : "tv");
        switch (productions.Count) {
            case 0:
                Utils.FailExit("ERROR: No productions remained after filtering");
                break;
            case 1:
                return productions[0];
        }

        return USER_SELECTS_PRODUCTION ? userSelectsProduction(productions) : selectBestProduction(productions);
    }

    private static Production userSelectsProduction(List<Production> productions) {
        for (int i = 0; i < productions.Count; i++) {
            Console.WriteLine($"#{i+1} {productions[i]}");
        }
        Console.WriteLine($"Select production [{1}-{productions.Count}]:");
        while (true) {
            string? input = Console.ReadLine();
            if (input is null || input.Length == 0) {
                Console.WriteLine("Specify a number, try again.");
                continue;
            }

            if (!int.TryParse(input, out int num)) {
                Console.WriteLine("Not a number, try again.");
                continue;
            }

            if (num < 1 || num > productions.Count) {
                Console.WriteLine("Out of bounds number, try again.");
                continue;
            }
            return productions[num - 1];
        }
    } 
    
    private Production selectBestProduction(List<Production> productions) {
        if (args.year != 0) {
            bool hasMatchingYear = false;
            foreach (var production in productions) {
                // If year is not given by the API ignore this constraint OR compare if present
                if (production.year == 0 || production.year == args.year) {
                    hasMatchingYear = true;
                    break;
                }
            }

            if (!hasMatchingYear) {
                Console.WriteLine($"No production found where year is matching, given: {args.year}");
                foreach (var prod in productions) {
                    Console.WriteLine(prod);
                }

                Environment.Exit(1);
            }
        }

        // Exact NAME&YEAR
        foreach (var production in productions) {
            if (production.name == args.title && production.year == args.year) {
                return production;
            }
        }

        // IgnoreCase NAME&YEAR
        foreach (var production in productions) {
            if (string.Equals(production.name, args.title, StringComparison.CurrentCultureIgnoreCase)
                && production.year == args.year) {
                return production;
            }
        }

        // just NAME
        foreach (var production in productions) {
            if (production.name == args.title) {
                return production;
            }
        }
        
        // just NAME (ignore case)
        foreach (var production in productions) {
            if (string.Equals(production.name, args.title, StringComparison.CurrentCultureIgnoreCase)) {
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