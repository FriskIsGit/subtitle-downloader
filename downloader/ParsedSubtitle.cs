using System.Text;

namespace subtitle_downloader.downloader; 

public struct ParsedSubtitle {
    public const string FORMAT = "Movie name (year) S2 E5";
    
    public string title = "";
    public uint year = 0;

    public bool isMovie = true;
    
    public uint season = 0;
    public uint episode = 0;

    public ParsedSubtitle() {
    }

    
    public static ParsedSubtitle NewMovie(string title, uint year) {
        return new ParsedSubtitle {
            title = title,
            year = year
        };
    }
    
    public static ParsedSubtitle NewShow(string title, uint year, uint season, uint episode) {
        return new ParsedSubtitle {
            title = title,
            year = year,
            isMovie = false,
            season = season,
            episode = episode,
        };
    }

    // Expected format: Movie Name (year) S1 E5
    // The year must be in brackets because some titles are literally just a number
    public static ParsedSubtitle Parse(string input) {
        string[] parts = input.Split(' ');
        var subtitle = new ParsedSubtitle();
        bool parsedProperty = false;
        var title = new StringBuilder(32);
        foreach (var part in parts) {
            if (part.Length == 0) {
                // Skip additional empty spaces
                continue;
            }

            // Parse year in brackets
            if (part.StartsWith('(')) {
                int closing = part.LastIndexOf(')');
                if (closing == -1) {
                    closing = part.Length;
                }

                string maybeYear = part[1..closing];
                if (isNumerical(maybeYear)) {
                    Console.WriteLine("Year is not numerical, skipping!");
                    continue;
                }
                subtitle.year = uint.Parse(maybeYear);
                parsedProperty = true;
                continue;
            }

            if ((part.StartsWith('S') || part.StartsWith('s')) && part.Length > 1 && isNumerical(part[1])) {
                subtitle.season = uint.Parse(part[1..]);
                subtitle.isMovie = false;
                parsedProperty = true;
                continue;
            }
            if ((part.StartsWith('E') || part.StartsWith('s')) && part.Length > 1 && isNumerical(part[1])) {
                subtitle.episode = uint.Parse(part[1..]);
                subtitle.isMovie = false;
                parsedProperty = true;
                continue;
            }

            if (!parsedProperty) {
                if (title.Length > 0) {
                    title.Append(' ');
                }
                title.Append(part);
            }
        }

        subtitle.title = title.ToString();
        return subtitle;
    }

    public override string ToString() {
        return $"{title} ({year}) S{season} E{episode}";
    }

    private static bool isNumerical(string str) {
        foreach (var chr in str) {
            switch (chr) {
                case >= '0' and <= '9':
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
    
    private static bool isNumerical(char chr) {
        switch (chr) {
            case >= '0' and <= '9':
                return true;
            default:
                return false;
        }
    }
}