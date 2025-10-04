using System.Text;

namespace subtitle_downloader.downloader; 

public class NameParser {
    private const int MIN_YEAR = 1900;
    private const int MAX_SEASONS = 50;

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
            char separator = '.';
            uint maxCount = 0;
            for (var ci = 0; ci < counts.Length; ci++) {
                var c = counts[ci];
                if (c <= maxCount) {
                    continue;
                }
                maxCount = c;
                switch (ci) {
                    case 0: separator = '.'; break;
                    case 1: separator = '-'; break;
                    default: separator = ' '; break;
                }
            }

            string[] parts = text.Split(separator);
            return parseSeparated(parts);
        }

        return parseJoined();
    }

    private Metadata parseJoined() {
        var meta = new Metadata();
        StringBuilder title = new StringBuilder();

        bool appendingTitle = true;
        for (int i = 0; i < text.Length; i++) {
            if (i > 0 && text[i] == '(') {
                int closing = text.IndexOf(')', i + 1);
                if (closing == -1) {
                    if (appendingTitle) {
                        title.Append(text[i..]);
                        return meta;
                    }
                    continue;
                }
                
                string inside = text[(i+1)..closing];
                bool inferred = parseMetadata(inside, meta, true);
                if (inferred) {
                    appendingTitle = false;
                }

                i = closing;
                continue;
            }
            
            if (i > 0 && !meta.providedSeason && i < text.Length-1 && (text[i] == 'S' || text[i] == 's')) {
                int digitsEnd = Utils.IterateWhile(char.IsDigit, text, i+1);
                if (digitsEnd == i+2 || digitsEnd == i+3) {
                    uint season = uint.Parse(text[(i+1)..digitsEnd]);
                    if (season <= MAX_SEASONS) {
                        meta.season = season;
                        meta.providedSeason = true;
                        appendingTitle = false;
                        i = digitsEnd-1;
                        continue;
                    }
                }
            }
            
            if (appendingTitle) {
                title.Append(text[i]);
            }
        }

        meta.name = title.ToString().Trim();
        // Parse metadata to gather any missing data
        parseMetadata(text[meta.name.Length..], meta, true);
        return meta;
    }

    // Attempts to parse the given string, inferring metadata, accounting for joined data
    // Returns bool indicating whether anything was parsed.
    private static bool parseMetadata(string snippet, Metadata meta, bool joined) {
        var (isYear, year) = getYear(snippet);
        if (isYear && year >= MIN_YEAR && meta.year == 0) {
            meta.year = year;
            return true;
        }

        if (joined) {
            if (meta.releaseType == "") {
                var (matched, start, end) = Utils.LocationOfContained(snippet, Metadata.RELEASE_TYPES);
                if (matched) {
                    meta.releaseType = snippet[start..end];
                    return true;
                }
            }
        }
        else {
            if (Utils.EqualsAny(snippet, Metadata.RELEASE_TYPES)) {
                meta.releaseType = snippet;
                return true;
            }
            
            if (snippet.EndsWith("0p") || Utils.EqualsAny(snippet, Metadata.ENCODER_IDENTIFIERS)) {
                return true;
            }
            
            if (snippet.Length >= 2 && (snippet[0] == 'S' || snippet[0] == 's')) {
                int digitsEnd = Utils.IterateWhile(char.IsDigit, snippet, 1);
                if (digitsEnd == 2 || digitsEnd == 3) {
                    uint season = uint.Parse(snippet[1..digitsEnd]);
                    if (season <= MAX_SEASONS) {
                        meta.season = season;
                        meta.providedSeason = true;
                        return true;
                    }
                }
                // skip, it's either not followed by a digit or too long, or too high
            }
            
            // Unable to reliable extract this information if the string is joined
            if (Utils.EqualsAny(snippet, Metadata.NETFLIX_IDENTIFIERS)) {
                meta.netflix = true;
                return true;
            }
        }
        
        return false;
    }

    private Metadata parseSeparated(string[] parts) {
        var meta = new Metadata();
        bool appendingTitle = true;
        StringBuilder title = new StringBuilder();
        for (var index = 0; index < parts.Length; index++) {
            var part = parts[index];
            if (Utils.isEnclosedBy(part, '(', ')')) {
                part = part[1..^1];
                appendingTitle = false;
            }

            if (index > 0) {
                bool inferred = parseMetadata(part, meta, false);
                if (inferred) {
                    appendingTitle = false;
                }
            }

            if (part.Length >= 4 && (part[0] == 'S' || part[0] == 's') && char.IsDigit(part[1]) && 
                char.IsDigit(part[^1])) {
                // Could handle ranges such as S01E1118-1119
                int seasonEnd = Utils.IterateWhile(char.IsDigit, part, 2);
                if (seasonEnd != 2 && seasonEnd != 3) {
                    continue;
                }

                meta.season = uint.Parse(part[1..seasonEnd]);
                meta.providedSeason = true;
                appendingTitle = false;
                
                if (part[seasonEnd] == 'E' || part[seasonEnd] == 'e') {
                    int epEnd = Utils.IterateWhile(char.IsDigit, part, seasonEnd+1);
                    if (epEnd == seasonEnd+1) {
                        continue;
                    }
                    meta.episode = uint.Parse(part[(seasonEnd+1)..epEnd]);
                    meta.providedEpisode = true;
                }
                
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

        meta.name = title.ToString().Trim();
        return meta;
    }

    // getYear parses string year into uint
    // The bool returned determines whether parsing was successful
    public static (bool, uint) getYear(string maybeYear) {
        if (maybeYear.Length != 4 || !Utils.isNumerical(maybeYear)) {
            return (false, 0);
        }
        if (!uint.TryParse(maybeYear, out var year)) {
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
        "BluRay", "Blu-ray", "BDRip", "BrRip", "BRRip", "DVDRip", "DVDR",
        "WEB-DL", "WEBDL", "WEBRip", "WEB-Rip", "WEB",
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
    
    public bool providedSeason, providedEpisode;

    public bool Equals(Metadata other) {
        return name == other.name &&
               releaseType == other.releaseType &&
               season == other.season &&
               episode == other.episode &&
               year == other.year &&
               netflix == other.netflix &&
               providedSeason == other.providedSeason &&
               providedEpisode == other.providedEpisode;
    }

    public bool isMovie() {
        return !providedSeason;
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

        if (year != 0)
            parts.Add($"Year: {year}");

        parts.Add($"Netflix: {(netflix ? "Yes" : "No")}");
        // Join with commas and wrap in curly braces
        return $"{{ {string.Join(", ", parts)} }}";
    }
}