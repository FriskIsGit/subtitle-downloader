using WebScrapper.scrapper;

namespace subtitle_downloader.downloader; 

class Program {
    public static void Main(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return;
        }

        if (args.Length == 1) {
            Console.WriteLine($"Specify production details in the following format: {ParsedSubtitle.FORMAT}");
            return;
        }
        
        var subtitle = ParsedSubtitle.Parse(args[1]);
        Console.WriteLine(subtitle);
        
        var api = new API();
        List<Production> productions = api.getSuggestedMovies(subtitle.title, args[0]);
        productions = filterByKind(productions, subtitle.isMovie);
        Production prod = selectProduction(productions, subtitle);
        Console.WriteLine(prod);
    }

    private static Production selectProduction(List<Production> productions, ParsedSubtitle desiredSubtitle) {
        if (productions.Count == 1) {
            return productions[0];
        }

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
            throw new ArgumentException($"No production found where year is matching, given: {desiredSubtitle.year}");
        }
        
        foreach (var production in productions) {
            if (production.name == desiredSubtitle.title && production.year == desiredSubtitle.year) {
                return production;
            }
        }
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
            } else if (prod.kind == "tv" && !isMovie) {
                filtered.Add(prod);
            }
        }

        return filtered;
    }

    private static void PrintHelp() {
        Console.WriteLine("Subtitle downloader!");
        Console.WriteLine("Commands:");
        Console.WriteLine("<language> <movie name> (<year>)");
        Console.WriteLine("<language> <show name> (<year>) S<season> E<episode>");
        
        Console.WriteLine("Usage example:");
        Console.WriteLine("subtitles fr \"The Godfather (1972)\"");
        Console.WriteLine("subtitles fr \"The Office (2001)\" S9 E19");
    }
    
    
    // Extract .zip that contains the .srt files
    public static void UnzipFile(string zipPath) {
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, ".");
    }
}