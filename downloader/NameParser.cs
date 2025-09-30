using System.Text;

namespace subtitle_downloader.downloader; 

public class NameParser {
    private readonly string text;
    private NameParser(string text) {
        this.text = text;
    }

    private Metadata parse() {
        uint dots = countSeparateOccurrences(text, '.');
        uint dashes = countSeparateOccurrences(text, '-');
        uint spaces = countSeparateOccurrences(text, ' ');
        if (dots > 1 || dashes > 1 || spaces > 1) {
            uint[] counts = { dots, dashes, spaces };
            char separator = ' ';
            uint maxCount = 0;
            for (var i = 0; i < counts.Length; i++) {
                var c = counts[i];
                if (c <= maxCount) {
                    continue;
                }
                maxCount = c;
                switch (i) {
                    case 0: separator = '.'; break;
                    case 1: separator = '-'; break;
                    default: separator = ' '; break;
                }
            }

            string[] parts = text.Split(separator);
            return parseSeparated(parts);
        }
        
        var meta = new Metadata();
        StringBuilder title = new StringBuilder();
        
        bool parsedTitle = false;
        for (int i = 0; i < text.Length; i++) {
            
        }
        return meta;
    }

    private static Metadata parseSeparated(string[] parts) {
        var meta = new Metadata();
        StringBuilder title = new StringBuilder();
        bool appendingTitle = true;
        for (var index = 0; index < parts.Length; index++) {
            var part = parts[index];
            if (index > 0 && Utils.EqualsAny(part, Metadata.NETFLIX_IDENTIFIERS)) {
                meta.netflix = true;
                appendingTitle = false;
                continue;
            }
            
            if (index > 0 && Utils.EqualsAny(part, Metadata.RELEASE_TYPES)) {
                meta.releaseType = part;
                appendingTitle = false;
                continue;
            }

            if (part.EndsWith("0p") || Utils.EqualsAny(part, Metadata.ENCODER_IDENTIFIERS)) {
                appendingTitle = false;
                continue;
            }

            if (part.Length >= 4 && (part[0] == 'S' || part[0] == 's') && char.IsDigit(part[1]) &&
                char.IsDigit(part[^1])) {
                int episodeIndex = part.IndexOf('e', 2);
                if (episodeIndex == -1) {
                    episodeIndex = part.IndexOf('E', 2);
                }

                string seasonStr = part[1..episodeIndex];
                if (!uint.TryParse(seasonStr, out var season)) {
                    Console.WriteLine("Failed to parse season number, given: " + seasonStr);
                    continue;
                }

                meta.season = season;
                meta.providedSeason = true;
                string episodeStr = part[(episodeIndex + 1)..];
                if (!uint.TryParse(episodeStr, out var episode)) {
                    Console.WriteLine("Failed to parse episode number, given: " + episodeStr);
                    continue;
                }

                meta.episode = episode;
                meta.providedEpisode = true;

                appendingTitle = false;
                continue;
            }

            var (success, year) = getYear(part);
            if (success) {
                meta.year = year;
                appendingTitle = false;
                continue;
            }
            
            if (!appendingTitle) {
                continue;
            }

            if (title.Length > 0) {
                title.Append(' ');
            }

            title.Append(part);
        }

        meta.name = title.ToString();
        return meta;
    }

    // isYear accounts for two cases (2005) 2005
    // returning bool determining success
    public static (bool, uint) getYear(string maybeYear) {
        string toParse;
        if (maybeYear.Length == 4 && Utils.isNumerical(maybeYear)) {
            toParse = maybeYear;
        } else if (maybeYear.Length == 6 && maybeYear[0] == '(' && maybeYear[5] == ')' && Utils.isNumerical(maybeYear[1..5])) {
            toParse = maybeYear[1..5];
        } else {
            return (false, 0);
        }

        if (!uint.TryParse(toParse, out var year)) {
            Console.WriteLine("Unexpected error - failed to parse year value, given:" + year);
            return (false, 0);
        }
        
        return (true, year);
    }
    
    public static uint countSeparateOccurrences(string text, char target) {
        bool lastMatched = false;
        uint count = 0;
        foreach (char c in text) {
            if (c != target) {
                lastMatched = false;
                continue;
            }
            if (lastMatched) {
                continue;
            }
            count++;
            lastMatched = true;
        }
        return count;
    }
    
    public static Metadata parse(string text) {
        return new NameParser(text).parse();
    }
}

public class Metadata {
    public static readonly string[] RELEASE_TYPES = {
        "BluRay", "Blu-ray", "BDRip", "BRRip", "DVDRip", "DVDR",
        "WEB-DL", "WEBDL", "WEB", "WEBRip", "WEB-Rip", 
        "HDTV", "DVBRip", "PPVRip"
    };
    public static readonly string[] NETFLIX_IDENTIFIERS = { "nf", "NF", "netflix", "Netflix" };
    public static readonly string[] ENCODER_IDENTIFIERS = { "x264", "H264", "x265", "HEVC", "x266" };
    public static readonly string[] QUALITY_IDENTIFIERS = {
        "144p", "288p", "360p", "480p", "576p", "720p", "1080p", "1440p", "2160p", 
        "2K", "4K", "5K", "8K",
        "HD", "FHD", "UHD"
    };

    public string name = "", releaseType = "";
    public uint season, episode, year;
    public bool netflix;
    
    public bool providedSeason, providedEpisode, providedYear;

    public bool Equals(Metadata other) {
        return name == other.name &&
               releaseType == other.releaseType &&
               season == other.season &&
               episode == other.episode &&
               year == other.year &&
               netflix == other.netflix &&
               providedSeason == other.providedSeason &&
               providedEpisode == other.providedEpisode &&
               providedYear == other.providedYear;
    }

    public override string ToString() {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(name))
            parts.Add($"Name: {name}");

        if (!string.IsNullOrEmpty(releaseType))
            parts.Add($"ReleaseType: {releaseType}");

        if (providedSeason)
            parts.Add($"Season: {season}");

        if (providedEpisode)
            parts.Add($"Episode: {episode}");

        if (providedYear)
            parts.Add($"Year: {year}");

        parts.Add($"Netflix: {(netflix ? "Yes" : "No")}");
        // Join with commas and wrap in curly braces
        return $"{{ {string.Join(", ", parts)} }}";
    }
}