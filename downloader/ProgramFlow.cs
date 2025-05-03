using System.Diagnostics;
using System.Text;

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
            convertLocally();
            return;
        }

        List<Production> productions = fetchProductions();
        Production production = chooseProduction(productions);
        Console.WriteLine("Selected - " + production);
        string pageUrl = production.getPageUrl(args.language);
        List<string> paths = fetchSubtitle(pageUrl);

        if (paths.Count == 0) {
            Console.WriteLine("No file paths to process! How?");
            return;
        }
        
        if (!args.requiresModifications()) {
            Utils.OkExit(paths.Count == 1 ? $"Saved to {paths[0]}" : $"Saved to: \n{formatPathsAsList(paths)}");
        }

        Console.WriteLine($"Processing {paths.Count} path(s)");
        foreach(string path in paths) {
            (string savedPath, _) = processSubtitle(path);
            Console.WriteLine($"Saved to {savedPath}");
        }
    }

    private static string formatPathsAsList(List<string> paths) {
        StringBuilder format = new StringBuilder();
        foreach (string path in paths) {
            format.Append("  ").Append(path).Append('\n');
        }
        return format.ToString();
    }

    private void convertLocally() {
        (string savedPath, bool modified) = processSubtitle(args.subtitlePath);
        string message;
        if (modified) {
            if (args.outputDirectory != ".") {
                savedPath = moveToDestinationDirectory(savedPath, args.outputDirectory);
            }
            message = $"Saved to {savedPath}";
        }
        else {
            message = "No modifications were done.";
        }
        Console.WriteLine(message);
    }

    private static string moveToDestinationDirectory(string filePath, string outputDir) {
        string destPath = Path.Combine(outputDir, Path.GetFileName(filePath));
        string? destinationDir = Path.GetDirectoryName(destPath);
        if (destinationDir != null) {
            Directory.CreateDirectory(destinationDir);
        }
        File.Move(filePath, destPath);
        return destPath;
    }
    
    // Returns path of the resulting subtitle and boolean - true if any modifications were performed, false otherwise
    private (string, bool) processSubtitle(string path) {
        string originalExtension = Utils.GetExtension(path);
        if (!args.requiresModifications(originalExtension)) {
            return (path, false);
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

        if (args.cleanup) {
            int empties = subtitleFile.removeEmptySubtitles();
            if (empties > 0) {
                Console.WriteLine($"Removed {empties} subtitle cue(s) during cleanup.");
            }
        }
        
        string newExtension = args.convert ? args.convertToExtension : originalExtension;
        // Handle 0 cues
        Console.WriteLine($"Serializing {subtitleFile.count()} subtitle chunks to {newExtension}");
        return (Converter.serialize(subtitleFile, path, newExtension), true);
    }

    private List<Production> fetchProductions() {
        List<Production> productions = api.getSuggestedMovies(args.title);
        if (productions.Count == 0) {
            Console.WriteLine("No suggested productions found, falling back to search");
            productions = api.searchProductions(args);
            if (productions.Count == 0) {
                Utils.FailExit("Fallback failed!");
            }
        }
        return productions;
    }

    private List<string> fetchSubtitle(string pageUrl) {
        List<SubtitleRow> subtitles;
        SubtitleRow bestSubtitle;
        if (args.isMovie) {
            subtitles = scrapeSubtitles(pageUrl);
            bestSubtitle = selectSubtitle(subtitles, args);
            return downloadSubtitle(bestSubtitle.getDownloadURL());
        }
        // Series fetching logic
        SimpleResponse seasonsResponse = api.fetchHtml(pageUrl);
        if (seasonsResponse.isError()) {
            Utils.FailExit("Failed to fetch seasons. URL: " + seasonsResponse.lastLocation);
        }
        string seasonsHtml = seasonsResponse.content;
        List<Season> seasons = SubtitleScraper.ScrapeSeriesTable(seasonsHtml);
        if (args.listSeries) {
            prettyPrintSeasons(seasons);
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
            Console.WriteLine($"Downloading pack from season {season.number} at {season.getPackUrl()}");
            return downloadSubtitle(season.getPackUrl());
        }

        List<string> paths = new();
        var epCount = season.episodes.Count;
        // Resolve it against the actual episodes, know the MAX episode number
        foreach (var ep in args.episodes) {
            Episode episode = getEpisode(season, ep);
            Console.WriteLine($"\"{episode.name}\" S{args.season} E{episode.number}");
            if (episode.url.Length == 0) {
                Utils.FailExit("This episode has no subtitles for given language, try again with --lang all");
            }
            subtitles = scrapeSubtitles(episode.getPageUrl());
            bestSubtitle = selectSubtitle(subtitles, args);
            var downloaded = downloadSubtitle(bestSubtitle.getDownloadURL());
            paths.AddRange(downloaded);
        }

        return paths;
    }

    private List<SubtitleRow> scrapeSubtitles(string pageURL) {
        var response = api.fetchHtml(pageURL);
        if (response.isError()) {
            Utils.FailExit("Failed to download subtitle");
        }
        string html = response.content;
        Console.WriteLine($"Scraping {pageURL}");

        List<SubtitleRow> rows = SubtitleScraper.ScrapeSubtitleTable(html);
        if (rows.Count == 0) {
            Utils.FailExit("No subtitle elements were scraped");
        }
        return rows;
    }

    // Unzipping can be extracted to a separate function potentially
    // Returns the paths of the downloaded then unzipped files
    private List<string> downloadSubtitle(string resourceUrl) {
        string outputDir = args.outputDirectory;
        // This internally checks if directory exists anyway
        Directory.CreateDirectory(outputDir);
        
        var download = api.downloadSubtitle(resourceUrl, outputDir);
        try {
            download.Wait();
        }
        catch (Exception e) {
            Utils.FailExit(e.Message);
        }

        SimpleDownloadResponse downloaded = download.Result;
        if (downloaded.isError()) {
            Utils.FailExit("The download failed!");
        }

        var zip = downloaded.filename;
        Console.WriteLine($"Unzipping {zip}");
        List<string> extracted = Utils.unzip(zip, outputDir);
        if (extracted.Count == 0) {
            Utils.FailExit("No elements were extracted from the zip file! Is the zip empty?");
        }
        Console.WriteLine("Deleting zip.");
        File.Delete(zip);
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
    private static void prettyPrintSeasons(List<Season> seasons) {
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

    private static SubtitleRow selectSubtitle(List<SubtitleRow> rows, Arguments args) {
        sortSubtitleByDownloads(rows);
        return args.autoSelect
            ? selectBestSubtitle(rows, args.extensionFilter, args.textToContain)
            : userSelectsSubtitle(rows, args.extensionFilter, args.textToContain);
    }

    private static void sortSubtitleByDownloads(List<SubtitleRow> rows) {
        rows.Sort((e1, e2) => e2.downloads.CompareTo(e1.downloads));
    }

    private static SubtitleRow userSelectsSubtitle(List<SubtitleRow> sortedRows, string extension, string mustContain) {
        if (sortedRows.Count == 1) {
            Console.WriteLine("Single result, proceeding.");
            return sortedRows[0];
        }

        List<SubtitleRow> filtered = new List<SubtitleRow>();
        foreach (var sub in sortedRows) {
            // Unknown subtitle formats could be matching
            bool extensionMatch = extension == "" || sub.format == "" || sub.format == extension;
            // If filename is empty it is discarded
            bool filenameMatch = mustContain == "" || sub.baseFilename.Contains(mustContain, StringComparison.InvariantCultureIgnoreCase);
                
            if (extensionMatch && filenameMatch) {
                filtered.Add(sub);
            }
        }
        if (filtered.Count == 0) {
            Utils.FailExit("No subtitles remained after filtering by:" +
                           "\n  extension=" + extension +
                           "\n  text=" + mustContain);
        }
        sortedRows = filtered;
        
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
    
    private static SubtitleRow selectBestSubtitle(List<SubtitleRow> subtitles, string extension, string mustContain) {
        foreach (var sub in subtitles) {
            if (extension != "" && sub.format != extension) {
                continue;
            }
            
            if (mustContain != "" && !sub.baseFilename.Contains(mustContain, StringComparison.InvariantCultureIgnoreCase)) {
                continue;
            }
            return sub;
        }
        Console.WriteLine("WARN: Defaulted to first subtitle.");
        return subtitles[0];
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
            if (string.Equals(production.name, args.title, StringComparison.InvariantCultureIgnoreCase)
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
            if (string.Equals(production.name, args.title, StringComparison.InvariantCultureIgnoreCase)) {
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