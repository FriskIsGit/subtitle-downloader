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

        string language = args[0];
        var subtitle = ParsedSubtitle.Parse(args[1]);
        string html = HtmlDoc.fetchHtml("");
        Console.WriteLine(subtitle);
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