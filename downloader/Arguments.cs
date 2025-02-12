using System.Text;

namespace subtitle_downloader.downloader;

public struct Arguments {
    private static readonly string[] SEASON_FLAGS = { "-s", "-S", "--season" };
    private static readonly string[] EPISODE_FLAGS = { "-e", "-E", "--episode" };
    private static readonly string[] YEAR_FLAGS = { "-y", "--year" };
    private static readonly string[] PACK_FLAGS = { "-p", "--pack" };
    private static readonly string[] LANGUAGE_FLAGS = { "--lang" };
    private static readonly string[] EXTENSION_FILTER_FLAGS = { "--filter" };
    private static readonly string[] LIST_FLAGS = { "-ls", "--list" };
    private static readonly string[] AUTO_SELECT_FLAGS = { "--auto-select", "-auto" };
    private static readonly string[] RENAME_FLAGS = { "--rename" };
    private static readonly string[] EXTRACT_ARGS_FLAGS = { "--extract" };
    private static readonly string[] FROM_SUBTITLE_FLAGS = { "--from", "--subtitle" };
    private static readonly string[] SHIFT_FLAGS = { "--shift" };
    private static readonly string[] CONVERT_FLAGS = { "--to", "--convert-to" };
    private static readonly string[] OUTPUT_FLAGS = { "--dest", "--out" };
    private static readonly string[] HELP_FLAGS = { "-h", "-help", "--help" };
    private static readonly string[] DEV_GEN_FLAGS = { "--gen" };

    private static readonly string[] SUBTITLE_FORMATS = {
        "srt", "ssa", "vtt", "aqt", "gsub", "jss", "sub", "ttxt", "pjs",
        "psb", "rt", "smi", "stl", "ssf", "ass", "sbv", "usf", "idx"
    };

    private const int MIN_YEAR = 1900;
    private const int MAX_SEASONS = 50;
    private const int MAX_EPISODES = 25000;

    private const int MAX_DOWNLOADS = 20;

    // 12 hour limits
    private const int BACKWARD_SHIFT_CAP = -12 * 60 * 60 * 1000;
    private const int FORWARD_SHIFT_CAP = 12 * 60 * 60 * 1000;

    public string title = "";
    public string language = "all";
    public string extensionFilter = "";
    public string outputDirectory = ".";
    public string subtitlePath = "";
    public string convertToExtension = "";

    public uint season = 0;
    public List<uint> episodes = new();
    public uint year = 0;

    public int shiftMs = 0;

    public bool subtitleFromFile = false;
    public bool convert = false;
    public bool downloadPack = false;

    public bool isMovie = true;
    public bool listSeries = false;
    public bool autoSelect = false;

    private bool providedSeason = false;
    private bool providedEpisode = false;

    public bool devMode = false;
    public int devGenerationCount = 0;

    public Arguments() {
    }

    public static Arguments Parse(string[] args) {
        var arguments = new Arguments();
        bool isTitleSet = false;

        HashSet<uint> episodeSet = new();
        for (int i = 0; i < args.Length; i++) {
            string currentArg = args[i];
            string? flag = GetPrefixedFlag(currentArg, SEASON_FLAGS);
            if (flag != null) {
                arguments.isMovie = false;
                bool isAdjacent = currentArg.Length > flag.Length;

                uint value;
                if (isAdjacent) {
                    string numerical = currentArg[flag.Length..];
                    if (uint.TryParse(numerical, out value)) {
                        arguments.season = value;
                        arguments.providedSeason = true;
                    }
                    else {
                        Utils.FailExit("Failed to parse (adjacent) season number!");
                    }
                }
                else {
                    bool hasNext = i + 1 < args.Length;
                    if (hasNext && uint.TryParse(args[i + 1], out value)) {
                        arguments.season = value;
                        arguments.providedSeason = true;
                        i++;
                    }
                    else {
                        Utils.FailExit("Failed to parse (separate) season number!");
                    }
                }

                continue;
            }

            flag = GetPrefixedFlag(currentArg, EPISODE_FLAGS);
            if (flag != null) {
                arguments.isMovie = false;
                bool isAdjacent = currentArg.Length > flag.Length;

                string episodeArg;
                if (isAdjacent) {
                    episodeArg = currentArg[flag.Length..];
                }
                else {
                    bool hasNext = i + 1 < args.Length;
                    if (!hasNext) {
                        Utils.FailExit("Episode number expected. Usage: -e <episode or episodes>");
                    }

                    episodeArg = args[i + 1];
                    i++;
                }

                string[] episodes = episodeArg.Split(",");
                arguments.providedEpisode |= parseEpisodes(episodes, episodeSet);

                continue;
            }

            flag = GetPrefixedFlag(currentArg, YEAR_FLAGS);
            if (flag != null) {
                bool isAdjacent = currentArg.Length > flag.Length;

                uint value;
                if (isAdjacent) {
                    string numerical = currentArg[flag.Length..];
                    if (uint.TryParse(numerical, out value)) {
                        arguments.year = value;
                    }
                    else {
                        Utils.FailExit("Failed to parse (adjacent) year number!");
                    }
                }
                else {
                    bool hasNext = i + 1 < args.Length;
                    if (hasNext && uint.TryParse(args[i + 1], out value)) {
                        arguments.year = value;
                        i++;
                    }
                    else {
                        Utils.FailExit("Failed to parse (separate) year number!");
                    }
                }

                continue;
            }

            if (EqualsAny(currentArg, LANGUAGE_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("The language argument wasn't provided. Help: --lang <language>");
                }

                arguments.language = args[i + 1];
                i++;
                continue;
            }

            if (EqualsAny(currentArg, EXTENSION_FILTER_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("No extension provided. Usage: --filter <extension>");
                }

                string ext = args[i + 1].ToLower();
                if (ext.Length < 2) {
                    Console.WriteLine("Extension name is too short to match any known subtitle format, skipping!");
                    i++;
                    continue;
                }

                if (ext.StartsWith('.')) {
                    ext = ext[1..];
                }

                arguments.extensionFilter = ext;
                i++;
                continue;
            }

            if (EqualsAny(currentArg, PACK_FLAGS)) {
                arguments.downloadPack = true;
                continue;
            }

            if (EqualsAny(currentArg, LIST_FLAGS)) {
                arguments.listSeries = true;
                continue;
            }

            if (EqualsAny(currentArg, AUTO_SELECT_FLAGS)) {
                arguments.autoSelect = true;
                continue;
            }

            if (EqualsAny(currentArg, EXTRACT_ARGS_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("A path to file was expected. Help: --extract <path>");
                }

                string path = args[i + 1];
                i++;
                parseFilename(Path.GetFileNameWithoutExtension(path), ref arguments);
                continue;
            }

            if (EqualsAny(currentArg, FROM_SUBTITLE_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("A path to a subtitle file was expected. Help: --from <path>");
                }

                string path = args[i + 1];
                i++;
                arguments.subtitleFromFile = true;
                arguments.subtitlePath = path;
                continue;
            }

            if (EqualsAny(currentArg, SHIFT_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (hasNext && int.TryParse(args[i + 1], out int shiftMs)) {
                    arguments.shiftMs = shiftMs;
                    i++;
                }
                else {
                    Utils.FailExit("Milliseconds [+/-] were expected as the next argument. Help: --shift <ms>");
                }

                continue;
            }

            if (EqualsAny(currentArg, CONVERT_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("A path to a subtitle file was expected. Help: --convert-to <extension>");
                }

                string ext = args[i + 1].ToLower();
                if (ext.Length < 2) {
                    Console.WriteLine("Extension name is too short to match any known subtitle format, skipping!");
                    i++;
                    continue;
                }

                if (ext.StartsWith('.')) {
                    ext = ext[1..];
                }

                i++;
                arguments.convert = true;
                arguments.convertToExtension = ext;
                continue;
            }

            if (EqualsAny(currentArg, OUTPUT_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    Utils.FailExit("An argument was expected. Help: --out <directory_path>");
                }

                string outputPath = args[i + 1];
                i++;
                arguments.outputDirectory = outputPath;
                continue;
            }

            if (EqualsAny(currentArg, DEV_GEN_FLAGS)) {
                bool hasNext = i + 1 < args.Length;
                if (!hasNext) {
                    continue;
                }

                if (int.TryParse(args[i + 1], out int count)) {
                    arguments.devGenerationCount = count;
                    arguments.devMode = true;
                    i++;
                }

                continue;
            }

            if (EqualsAny(currentArg, HELP_FLAGS)) {
                PrintHelp();
                Environment.Exit(0);
            }

            if (currentArg.StartsWith('-')) {
                Console.WriteLine($"Unrecognized argument identifier: {currentArg}");
                continue;
            }

            if (!isTitleSet) {
                arguments.title = currentArg;
                isTitleSet = true;
            }
        }
        arguments.episodes = new List<uint>(episodeSet);
        arguments.episodes.Sort();
        return arguments;
    }

    // returns true if any episodes have been parsed, false otherwise
    private static bool parseEpisodes(string[] episodeParams, HashSet<uint> episodes) {
        bool parsedEpisode = false;
        foreach (var ep in episodeParams) {
            if (ep.Contains('-')) {
                string[] rangeSplit = ep.Split('-');
                uint[] range = Utils.toUIntArray(rangeSplit);
                if (range.Length != 2) {
                    Utils.FailExit("The episode range is not a valid double-split. Usage: -e 2-6");
                }

                // Investigate overflow behavior
                var rangeSize = range[1] - range[0] + 1;
                if (rangeSize > MAX_DOWNLOADS) {
                    Utils.FailExit($"The episode range size {rangeSize} exceeds {MAX_DOWNLOADS}.");
                }

                if (range[0] > range[1]) {
                    Utils.FailExit($"The episode range start value is larger than end value.");
                }
                parsedEpisode = true;
                for (uint e = range[0]; e <= range[1]; e++) {
                    episodes.Add(e);
                    if (episodes.Count > MAX_DOWNLOADS) {
                        Utils.FailExit($"The total number of episodes exceeds {MAX_DOWNLOADS}.");
                    }
                }
                continue;
            }

            if (uint.TryParse(ep, out uint value)) {
                parsedEpisode = true;
                episodes.Add(value);
                if (episodes.Count > MAX_DOWNLOADS) {
                    Utils.FailExit($"The total number of episodes exceeds {MAX_DOWNLOADS}.");
                }
            }
            else {
                Utils.FailExit($"Failed to parse {ep} to an episode number!");
            }
        }

        return parsedEpisode;
    }

    private static void parseFilename(string filename, ref Arguments args) {
        StringBuilder title = new StringBuilder();
        string[] parts;
        if (filename.Contains('.')) {
            // assume dot format
            parts = filename.Split('.');
        }
        else if (filename.Contains('-')) {
            parts = filename.Split('-');
        }
        else {
            parts = filename.Split(' ');
        }

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

            if (part.Length >= 4 && (part[0] == 'S' || part[0] == 's') && char.IsDigit(part[1]) &&
                char.IsDigit(part[^1])) {
                int episodeIndex = part.IndexOf('e', 2);
                if (episodeIndex == -1) {
                    episodeIndex = part.IndexOf('E', 2);
                }

                string seasonStr = part[1..episodeIndex];
                if (!uint.TryParse(seasonStr, out var season)) {
                    Console.WriteLine("Failed to parse season number");
                    continue;
                }

                args.season = season;
                args.providedSeason = true;
                string episodeStr = part[(episodeIndex + 1)..];
                if (!uint.TryParse(episodeStr, out var episode)) {
                    Console.WriteLine("Failed to parse episode number");
                    continue;
                }

                args.episodes.Add(episode);
                args.providedEpisode = true;

                args.isMovie = false;
                appendingTitle = false;
            }
            else if (part.Length == 4 && isNumerical(part)) {
                if (!uint.TryParse(part, out var year)) {
                    Console.WriteLine("Failed to parse year value");
                    continue;
                }

                args.year = year;
                appendingTitle = false;
            }
            else if (part.Length == 6 && part[0] == '(' && part[5] == '(' && isNumerical(part[1..5])) {
                if (!uint.TryParse(part[1..5], out var year)) {
                    Console.WriteLine("Failed to parse year value");
                    continue;
                }

                args.year = year;
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

        args.title = title.ToString();
    }

    private static int countChar(string str, char target) {
        int count = 0;
        foreach (var chr in str) {
            if (chr == target) {
                count++;
            }
        }

        return count;
    }

    public bool Validate() {
        if (devMode) {
            return true;
        }

        if (title.Length == 0 && !subtitleFromFile) {
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
            if (downloadPack) {
                if (!providedSeason) {
                    Console.WriteLine("Unspecified season number for the pack");
                    return false;
                }
            }
            else if (!providedSeason || !providedEpisode) {
                Console.WriteLine(
                    "For TV series season and episode arguments are required unless it's a pack download");
                return false;
            }

            if (season > MAX_SEASONS) {
                Console.WriteLine("Season number is too large!");
                return false;
            }

            
            if (episodes.Any(num => num > MAX_EPISODES)) {
                Console.WriteLine("Episode number is too large!");
                return false;
            }
        }

        if (extensionFilter != "" && !SUBTITLE_FORMATS.Contains(extensionFilter)) {
            Console.WriteLine("Subtitle extension doesn't match any existing subtitle formats!");
            return false;
        }

        if (shiftMs != 0) {
            if (shiftMs < BACKWARD_SHIFT_CAP) {
                Console.WriteLine("The shift goes too far back!");
                return false;
            }

            if (shiftMs > FORWARD_SHIFT_CAP) {
                Console.WriteLine("The shift goes too far forward!");
                return false;
            }
        }

        if (convert && !SUBTITLE_FORMATS.Contains(convertToExtension)) {
            Console.WriteLine("Subtitle extension doesn't match any existing subtitle formats!");
            return false;
        }

        if (!Directory.Exists(outputDirectory)) {
            Console.WriteLine($"Specified directory does not exist! {outputDirectory}");
            return false;
        }

        return true;
    }

    // returns the first parameter that the 'arg' starts with, otherwise null
    private static string? GetPrefixedFlag(string arg, params string[] parameters) {
        foreach (var param in parameters) {
            if (arg.StartsWith(param)) {
                return param;
            }
        }

        return null;
    }

    private static bool EqualsAny(string arg, params string[] parameters) {
        foreach (var param in parameters) {
            if (arg == param) {
                return true;
            }
        }

        return false;
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
        var args = new Arguments();
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
                args.year = uint.Parse(maybeYear);
                if (args.year < MIN_YEAR) {
                    Console.WriteLine($"The first sound film was projected in {MIN_YEAR}");
                    continue;
                }
                parsedProperty = true;
                continue;
            }

            if ((part.StartsWith('S') || part.StartsWith('s')) && part.Length > 1 && isNumerical(part[1])) {
                args.season = uint.Parse(part[1..]);
                args.isMovie = false;
                parsedProperty = true;
                continue;
            }
            if ((part.StartsWith('E') || part.StartsWith('e')) && part.Length > 1 && isNumerical(part[1])) {
                args.episodes.Add(uint.Parse(part[1..]));
                args.isMovie = false;
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

        args.title = title.ToString();
        return args;
    }

    public static void PrintHelp() {
        string programName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        Console.WriteLine($"Subtitle downloader-converter (OpenSubtitles) v{Program.VERSION}");
        Console.WriteLine();
        Console.WriteLine($"Usage: {programName} [movie/show title] [arguments...]");
        Console.WriteLine($"       {programName} --from [file path] [arguments...]");
        Console.WriteLine($"       {programName} --extract [filename] [arguments...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine(formatOption(SEASON_FLAGS, "Season number of a tv series"));
        Console.WriteLine(formatOption(EPISODE_FLAGS, "Episode numbers of a tv series"));
        Console.WriteLine(formatOption(LANGUAGE_FLAGS, "Subtitle language code (3 letters)"));
        Console.WriteLine(formatOption(YEAR_FLAGS, "[OPTIONAL] Year number of a movie or tv series"));
        Console.WriteLine(formatOption(LIST_FLAGS, "[OPTIONAL] Pretty print seasons and episodes"));
        Console.WriteLine(formatOption(EXTENSION_FILTER_FLAGS, "[OPTIONAL] Filter subtitles by extension"));
        Console.WriteLine(formatOption(AUTO_SELECT_FLAGS, "[OPTIONAL] Automatically selects subtitle to download"));
        Console.WriteLine(formatOption(PACK_FLAGS, "Download season as pack (<= 50 episodes) (faulty)"));
        Console.WriteLine(formatOption(FROM_SUBTITLE_FLAGS, "Parses a subtitle file (use with --shift and --convert-to)"));
        Console.WriteLine(formatOption(EXTRACT_ARGS_FLAGS, "Extracts production details from filename"));
        Console.WriteLine(formatOption(SHIFT_FLAGS, "Shifts subtitles in time by [+/- ms]"));
        Console.WriteLine(formatOption(CONVERT_FLAGS, "Subtitle format to convert to [srt/vtt]"));
        Console.WriteLine(formatOption(OUTPUT_FLAGS, "Destination directory where subtitles will be placed"));
        Console.WriteLine(formatOption(HELP_FLAGS, "Display this information (regardless of flag order)"));
        Console.WriteLine();
        Console.WriteLine("To display available subtitle languages and their codes use: -languages");
        Console.WriteLine("Season, episode and year arguments can be joined with a number (e.g. -S2)");
        Console.WriteLine("Episode numbers can be provided both as values and inclusive ranges (comma delimited e.g. -e 1,3-5,7)");
        Console.WriteLine("Files converted will have their names updated to match the output format");
        Console.WriteLine("File name provided with flag '--extract' should have an extension & follow any of the three formats: ");
        Console.WriteLine(" - dotted: Series.Name.Year.SxEy");
        Console.WriteLine(" - spaced: Production Name (Year) SxEy");
        Console.WriteLine(" - dashed: Production-Name-Year-SxEy");
        Console.WriteLine();
        Console.WriteLine("Usage example:");
        Console.WriteLine($"  {programName} \"The Godfather\" -y 1972");
        Console.WriteLine($"  {programName} \"Office\" -S9 -E19 -y2005");
        Console.WriteLine($"  {programName} \"Spongebob\" --year 1999 --season 2 --pack");
        Console.WriteLine($"  {programName} \"Game of Thrones\" -s8 -e 1-10");
        Console.WriteLine();
        Console.WriteLine("Subtitle conversion example:");
        Console.WriteLine($"  {programName} --from FastAndFurious.srt --shift +5000 --to vtt");
        Console.WriteLine();
    }

    private const int PAD_LENGTH = 32;
    private static string formatOption(string[] flags, string description) {
        StringBuilder str = new StringBuilder(100);
        str.Append("    ");
        
        bool first = true;
        foreach (string flag in flags) {
            if (first) {
                str.Append(flag);
                first = false;
                continue;
            }
            
            str.Append(", ");
            str.Append(flag);
        }

        while (str.Length < PAD_LENGTH) {
            str.Append(' ');
        }
        
        str.Append(description);
        return str.ToString();
    }

    public override string ToString() {
        StringBuilder str = new StringBuilder(32);
        str.Append($"{title} ");
        if (year != 0) {
            str.Append($"({year}) ");
        }
        if (!isMovie) {
            string ep = episodes.Count > 1 ? "[" + string.Join(",", episodes) + "]" : episodes[0].ToString();
            str.Append($"S{season} E{ep}");
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
    
    public static void PrintLanguages() {
        const string LANGUAGES = @"
ALL                    all
Abkhazian              abk
Afrikaans              afr
Albanian               alb
Amharic                amh
Arabic                 ara
Aragonese              arg
Armenian               arm
Assamese               asm
Asturian               ast
Azerbaijani            aze
Basque                 baq
Belarusian             bel
Bengali                ben
Bosnian                bos
Breton                 bre
Bulgarian              bul
Burmese                bur
Catalan                cat
Chinese (Cantonese)    zhc
Chinese (simplified)   chi
Chinese (traditional)  zht
Chinese bilingual      zhe
Croatian               hrv
Czech                  cze
Danish                 dan
Dari                   prs
Dutch                  dut
English                eng
Esperanto              epo
Estonian               est
Extremaduran           ext
Finnish                fin
French                 fre
Gaelic                 gla
Galician               glg
Georgian               geo
German                 ger
Greek                  ell
Hebrew                 heb
Hindi                  hin
Hungarian              hun
Icelandic              ice
Igbo                   ibo
Indonesian             ind
Interlingua            ina
Irish                  gle
Italian                ita
Japanese               jpn
Kannada                kan
Kazakh                 kaz
Khmer                  khm
Korean                 kor
Kurdish                kur
Latvian                lav
Lithuanian             lit
Luxembourgish          ltz
Macedonian             mac
Malay                  may
Malayalam              mal
Manipuri               mni
Marathi                mar
Mongolian              mon
Montenegrin            mne
Navajo                 nav
Nepali                 nep
Northern Sami          sme
Norwegian              nor
Occitan                oci
Odia                   ori
Persian                per
Polish                 pol
Portuguese             por
Portuguese (BR)        pob
Portuguese (MZ)        pom
Pushto                 pus
Romanian               rum
Russian                rus
Santali                sat
Serbian                scc
Sindhi                 snd
Sinhalese              sin
Slovak                 slo
Slovenian              slv
Somali                 som
Spanish                spa
Spanish (EU)           spn
Spanish (LA)           spl
Swahili                swa
Swedish                swe
Syriac                 syr
Tagalog                tgl
Tamil                  tam
Tatar                  tat
Telugu                 tel
Thai                   tha
Toki Pona              tok
Turkish                tur
Turkmen                tuk
Ukrainian              ukr
Urdu                   urd
Uzbek                  uzb
Vietnamese             vie
Welsh                  wel";
        Console.WriteLine(LANGUAGES);
    }
}
