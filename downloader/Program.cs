using System.Text;
using WebScrapper.scrapper;

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
        Console.WriteLine(subtitle);

        var api = new API();
        List<Production> productions = api.getSuggestedMovies(subtitle.title);
        if (productions.Count == 0) {
            Console.WriteLine("No productions found, implement fallback?");
            return;
        }

        productions = filterByKind(productions, subtitle.isMovie);
        Production prod = selectProduction(productions, subtitle);
        Console.WriteLine("Selected: " + prod);
        string language = API.toSubLanguageID(args[0]);
        Console.WriteLine("Language id: " + prod);
        string searchURL = $"https://www.opensubtitles.org/en/search/sublanguageid-{language}/idmovie-{prod.id}";
        string html = HtmlDoc.fetchHtml(searchURL);
        Console.WriteLine($"SEARCH URL: {searchURL}");
        List<SubtitleRow> rows = SubtitleScraper.ScrapeTable(html);
        if (rows.Count == 0) {
            Console.WriteLine("No subtitle elements were scraped");
            return;
        }
        SubtitleRow bestSubtitle = selectSubtitle(rows);
        Console.WriteLine(bestSubtitle);
        var download = api.downloadSubtitle(bestSubtitle);
        download.Wait();
        string downloadedZip = bestSubtitle.productionTitle + ".zip";
        if (download.Result) {
            Console.WriteLine("Unzipping..");
            UnzipFile(downloadedZip);
        }
        Console.WriteLine("Cleaning up..");
        File.Delete(downloadedZip);
        cleanupNFOs();
        Console.WriteLine("Finished");
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

        if (max == 0) {
            return rows[0];
        }
        
        return bestSubtitle;
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
        if (productions.Count == 1) {
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

    private static List<Production> filterByKind(List<Production> productions, bool isMovie) {
        var filtered = new List<Production>();
        foreach (var prod in productions) {
            if (prod.kind == "movie" && isMovie) {
                filtered.Add(prod);
            }
            else if (prod.kind == "tv" && !isMovie) {
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