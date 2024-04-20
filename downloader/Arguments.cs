using System.Text;

namespace subtitle_downloader.downloader; 

public struct Arguments {
    private static readonly string[] SEASON_IDENTIFIERS   = {"-s", "-S", "--season"};
    private static readonly string[] EPISODE_IDENTIFIERS  = {"-e", "-E", "--episode"};
    private static readonly string[] YEAR_IDENTIFIERS     = {"-y", "--year"};
    private static readonly string[] LANGUAGE_IDENTIFIERS = {"--lang"};
    private static readonly string[] LIST_IDENTIFIERS = {"-ls", "--list"};
    private static readonly string[] PATH_IDENTIFIERS = {"--path"};

    private const int MIN_YEAR = 1900;
    private const int MAX_SEASONS = 50;
    private const int MAX_EPISODES = 25000;

    public string title = "";
    public string language = "all";
    public uint year = 0;

    public bool isMovie = true;
    
    public uint season = 0;
    public uint episode = 0;
    
    public bool listSeries = false;

    private bool providedSeason = false;
    private bool providedEpisode = false;

    public Arguments() {
    }
    
    public static Arguments Parse(string[] args) {
        var subtitle = new Arguments();
        bool isTitleSet = false;
        
        for (int i = 0; i < args.Length; i++) {
            string currentArg = args[i];
            int seasonIndex = StartsWith(currentArg, SEASON_IDENTIFIERS);
            if (seasonIndex != -1) {
                subtitle.isMovie = false;
                string key = SEASON_IDENTIFIERS[seasonIndex];
                bool isAdjacent = currentArg.Length > key.Length;
                
                uint value;
                switch (isAdjacent) {
                    case true:
                        string numerical = currentArg[key.Length..];
                        if (uint.TryParse(numerical, out value)) {
                            subtitle.season = value;
                            subtitle.providedSeason = true;
                        }
                        else {
                            FailExit("Failed to parse (adjacent) season number!");
                        }
                        break;
                    case false:
                        bool hasNext = i + 1 < args.Length;
                        if (hasNext && uint.TryParse(args[i + 1], out value)) {
                            subtitle.season = value;
                            subtitle.providedSeason = true;
                            i++;
                        }
                        else {
                            FailExit("Failed to parse (separate) season number!");
                        }
                        break;
                }
                continue;
            }
            
            int episodeIndex = StartsWith(currentArg, EPISODE_IDENTIFIERS);
            if (episodeIndex != -1) {
                subtitle.isMovie = false;
                string key = EPISODE_IDENTIFIERS[episodeIndex];
                bool isAdjacent = currentArg.Length > key.Length;
                
                uint value;
                switch (isAdjacent) {
                    case true:
                        string numerical = currentArg[key.Length..];
                        if (uint.TryParse(numerical, out value)) {
                            subtitle.episode = value;
                            subtitle.providedEpisode = true;
                        }
                        else {
                            FailExit("Failed to parse (adjacent) episode number!");
                        }
                        break;
                    case false:
                        bool hasNext = i + 1 < args.Length;
                        if (hasNext && uint.TryParse(args[i + 1], out value)) {
                            subtitle.episode = value;
                            subtitle.providedEpisode = true;
                            i++;
                        }
                        else {
                            FailExit("Failed to parse (separate) episode number!");
                        }
                        break;
                }
                continue;
            }
            
            int yearIndex = StartsWith(currentArg, YEAR_IDENTIFIERS);
            if (yearIndex != -1) {
                string key = YEAR_IDENTIFIERS[yearIndex];
                bool isAdjacent = currentArg.Length > key.Length;
                
                uint value;
                switch (isAdjacent) {
                    case true:
                        string numerical = currentArg[key.Length..];
                        if (uint.TryParse(numerical, out value)) {
                            subtitle.year = value;
                        }
                        else {
                            FailExit("Failed to parse (adjacent) year number!");
                        }
                        break;
                    case false:
                        bool hasNext = i + 1 < args.Length;
                        if (hasNext && uint.TryParse(args[i + 1], out value)) {
                            subtitle.year = value;
                            i++;
                        }
                        else {
                            FailExit("Failed to parse (separate) year number!");
                        }
                        break;
                }
                continue;
            }
            
            int languageIndex = StartsWith(currentArg, LANGUAGE_IDENTIFIERS);
            if (languageIndex != -1) {
                bool hasNext = i + 1 < args.Length;
                if (hasNext) {
                    subtitle.language = args[i + 1];
                    i++;
                }
                else {
                    Console.WriteLine("The language argument wasn't provided. Help: --lang <language>");
                }
                continue;
            }

            int listIndex = EqualsAny(currentArg, LIST_IDENTIFIERS);
            if (listIndex != -1) {
                subtitle.listSeries = true;
                continue;
            }

            int pathIndex = StartsWith(currentArg, PATH_IDENTIFIERS);
            if (pathIndex != -1) {
                bool hasNext = i + 1 < args.Length;
                if (hasNext) {
                    string path = args[i + 1];
                    i++;
                    parseFilename(Path.GetFileNameWithoutExtension(path), ref subtitle);
                }
                else {
                    Console.WriteLine("A path to file was expected. Help: --path <path>");
                }
                continue;
            }

            if (currentArg.StartsWith('-')) {
                Console.WriteLine($"Unrecognized argument identifier: {currentArg}");
                continue;
            }
            if (!isTitleSet) {
                subtitle.title = currentArg;
                isTitleSet = true;
            }
        }

        return subtitle;
    }

    private static void parseFilename(string filename, ref Arguments subtitle) {
        StringBuilder title = new StringBuilder();
        if (filename.Contains('.')) {
            // assume dot format
            string[] parts = filename.Split('.');
            bool appendingTitle = true;
            foreach (var part in parts) {
                if (part.EndsWith("0p")) {
                    appendingTitle = false;
                    continue;
                }
                if (part.StartsWith("x264") || part.StartsWith("x265")) {
                    appendingTitle = false;
                    continue;
                }
                if (part.Length >= 4 && (part[0] == 'S' || part[0] == 's') && char.IsDigit(part[1]) && char.IsDigit(part[^1])) {
                    int episodeIndex = part.IndexOf('e', 2);
                    if (episodeIndex == -1) {
                        episodeIndex = part.IndexOf('E', 2);
                    }

                    string seasonStr = part[1..episodeIndex];
                    if (!uint.TryParse(seasonStr, out var season)) {
                        Console.WriteLine("Failed to parse season number");
                        continue;
                    }
                    subtitle.season = season;
                    subtitle.providedSeason = true;
                    string episodeStr = part[(episodeIndex + 1)..];
                    if (!uint.TryParse(episodeStr, out var episode)) {
                        Console.WriteLine("Failed to parse episode number");
                        continue;
                    }
                    subtitle.episode = episode;
                    subtitle.providedEpisode = true;

                    subtitle.isMovie = false;
                    appendingTitle = false;
                }
                else if (part.Length == 4 && isNumerical(part)) {
                    if (!uint.TryParse(part, out var year)) {
                        Console.WriteLine("Failed to parse year value");
                        continue;
                    }
                    subtitle.year = year;
                    appendingTitle = false;
                }
                else {
                    if (!appendingTitle) {
                        continue;
                    }
                    if (title.Length > 0) {
                        title.Append(' ');
                    }
                    title.Append(part);
                }
            }
            subtitle.title = title.ToString();
            return;
        }

        var pair = ParseTitleYear(filename);
        subtitle.title = pair.title;
        subtitle.year = pair.year;
    }

    public bool Validate() {
        if (title.Length == 0) {
            Console.WriteLine("The title cannot be empty");
            return false;
        }
        if (language.Length == 0) {
            Console.WriteLine("The language cannot be empty");
            return false;
        }
        if (year != 0 && year < MIN_YEAR) {
            Console.WriteLine($"The first sound film was projected in {MIN_YEAR}");
            return false;
        }

        if (!isMovie) {
            if (!providedSeason || !providedEpisode) {
                Console.WriteLine("For TV series season and episode arguments are required");
                return false;
            }

            if (season > MAX_SEASONS) {
                Console.WriteLine("Season number is too large!");
                return false;
            }
            if (episode > MAX_EPISODES) {
                Console.WriteLine("Episode number is too large!");
                return false;
            }
        }

        return true;
    }
    
    // returns index of the parameter that it starts with, -1 if does not start with any
    private static int StartsWith(string arg, params string[] parameters) {
        for (var i = 0; i < parameters.Length; i++) {
            if (arg.StartsWith(parameters[i])) {
                return i;
            }
        }
        return -1;
    }
    private static int EqualsAny(string arg, params string[] parameters) {
        for (var i = 0; i < parameters.Length; i++) {
            if (arg == parameters[i]) {
                return i;
            }
        }
        return -1;
    }

    private static void FailExit(string message) {
        Console.WriteLine(message);
        Environment.Exit(0);
    }
    
    public static (string title, uint year) ParseTitleYear(string name) {
        int bracketOpen = name.LastIndexOf('(');
        if (bracketOpen == -1) {
            return (name, 0);
        }

        string title = name[..(bracketOpen - 1)];
        int close = name.LastIndexOf(')');
        string numericalYear = name[(bracketOpen+1)..close];
        if (uint.TryParse(numericalYear, out var year)) {
            return (title, year);
        }
        return (title, 0);
    }
    
    // Expected format: Movie Name (year) S1 E5
    // The year must be in brackets because some titles are literally just a number
    public static Arguments FancyParse(string input) {
        string[] parts = input.Split(' ');
        var subtitle = new Arguments();
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
                if (!isNumerical(maybeYear)) {
                    Console.WriteLine("The year is not numerical, skipping!");
                    continue;
                }
                if (maybeYear.Length < 4) {
                    Console.WriteLine("The year is too short, skipping!");
                    continue;
                }
                subtitle.year = uint.Parse(maybeYear);
                if (subtitle.year < MIN_YEAR) {
                    Console.WriteLine($"The first sound film was projected in {MIN_YEAR}");
                    continue;
                }
                parsedProperty = true;
                continue;
            }

            if ((part.StartsWith('S') || part.StartsWith('s')) && part.Length > 1 && isNumerical(part[1])) {
                subtitle.season = uint.Parse(part[1..]);
                subtitle.isMovie = false;
                parsedProperty = true;
                continue;
            }
            if ((part.StartsWith('E') || part.StartsWith('e')) && part.Length > 1 && isNumerical(part[1])) {
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

    public static void PrintHelp() {
        string programName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        Console.WriteLine();
        Console.WriteLine($"Subtitle downloader (OpenSubtitles) v{Program.VERSION}");
        Console.WriteLine();
        Console.WriteLine($"Usage: {programName} [movie/show title] [arguments...]");
        Console.WriteLine($"       {programName} --path [file path] [arguments...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("    -s, -S, --season              Season number of a tv series (season > 0)");
        Console.WriteLine("    -e, -E, --episode             Episode number of a tv series (episode > 0)");
        Console.WriteLine("    --lang                        Subtitle language written in English (at least 3 characters)");
        Console.WriteLine("    -y, --year                    [OPTIONAL] Year number of a movie or tv series");
        Console.WriteLine("    -ls, --list                   [OPTIONAL] Pretty print seasons and episodes");
        Console.WriteLine("    --path                        Extracts production details from filename in dotted/spaced format");
        Console.WriteLine();
        Console.WriteLine("Season, episode and year arguments can be concatenated with a number (e.g. -S2)");
        Console.WriteLine("File name provided with --path should follow two formats: ");
        Console.WriteLine(" - dotted: Series.Name.Year.SxEy");
        Console.WriteLine(" - spaced: Production Name (Year)");
        Console.WriteLine();
        Console.WriteLine("Usage example:");
        Console.WriteLine($"  {programName} \"The Godfather\" -y 1972");
        Console.WriteLine($"  {programName} \"Office\" -y2005 -S9 -E19");
        Console.WriteLine($"  {programName} \"fast and the furious\"");
    }
    
    public override string ToString() {
        StringBuilder str = new StringBuilder(32);
        str.Append($"{title} ");
        if (year != 0) {
            str.Append($"({year}) ");
        }
        if (!isMovie) {
            str.Append($"S{season} E{episode} ");
        }
        str.Append($"Language:{language}");
        return str.ToString();
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