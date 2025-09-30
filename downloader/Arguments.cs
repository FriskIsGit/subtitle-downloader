using System.Text;
using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader;

public struct Arguments {
    private static readonly string[] LANGUAGE_FLAGS = { "--lang" };
    private static readonly string[] YEAR_FLAGS = { "-y", "--year" };
    private static readonly string[] EXTENSION_FILTER_FLAGS = { "--filter" };
    private static readonly string[] AUTO_SELECT_FLAGS = { "--auto-select", "-auto" };
    private static readonly string[] JSON_OUTPUT_FLAGS = { "--json" };
    private static readonly string[] CONTAINS_FLAGS = { "--contains", "--has" };
    // private static readonly string[] RENAME_FLAGS = { "--rename" };
    private static readonly string[] EXTRACT_ARGS_FLAGS = { "--extract" };
    private static readonly string[] FROM_SUBTITLE_FLAGS = { "--from", "--subtitle" };
    private static readonly string[] SHIFT_FLAGS = { "--shift" };
    private static readonly string[] CONVERT_FLAGS = { "--to", "--convert-to" };
    private static readonly string[] OUTPUT_FLAGS = { "--dest", "--out", "-o" };
    private static readonly string[] CLEANUP_FLAGS = { "--clean", "--cleanup"};
    private static readonly string[] PROVIDER_FLAGS = { "--provider" };
    
    private static readonly string[] SEASON_FLAGS = { "-s", "-S", "--season" };
    private static readonly string[] EPISODE_FLAGS = { "-e", "-E", "--episode" };
    private static readonly string[] LIST_FLAGS = { "-ls", "--list" };
    private static readonly string[] PACK_FLAGS = { "-p", "--pack" };
    
    private static readonly string[] HELP_FLAGS = { "-h", "-help", "--help" };
    private static readonly string[] VERSION_FLAGS = { "-v", "-version", "--version" };
    private static readonly string[] AVAILABLE_LANGUAGES_FLAGS = { "-languages" };
    private static readonly string[] DEV_GEN_FLAGS = { "--gen" };

    private static readonly string[] SUBTITLE_FORMATS = {
        "srt", "ssa", "vtt", "aqt", "gsub", "jss", "sub", "ttxt", "pjs",
        "psb", "rt",  "smi", "stl", "ssf", "ass", "sbv", "usf", "idx", "lrc"
    };

    private const int MIN_YEAR = 1900;
    private const int MAX_SEASONS = 50;
    private const int MAX_EPISODES = 25000;

    private const int MAX_DOWNLOADS = 20;

    // 12 hour limits
    private const int BACKWARD_SHIFT_CAP = -12 * 60 * 60 * 1000;
    private const int FORWARD_SHIFT_CAP = 12 * 60 * 60 * 1000;

    public Provider provider = Provider.OpenSubtitles;
    public string title = "";
    public string language = "eng";
    public string extensionFilter = "";
    public string outputDirectory = ".";
    public string subtitlePath = "";
    public string convertToExtension = "";
    public string textToContain = "";

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
    public bool jsonOutput = false;
    public bool cleanup = false;

    private bool providedSeason = false;
    private bool providedEpisode = false;

    public bool devMode = false;
    public int devGenerationCount = 0;

    public Arguments() {
    }

    // Parses arguments and returns an instance unless any of the following flags are specified:
    // 'help', 'version', 'available languages', or if an error occurs during parsing.
    public static Arguments Parse(string[] args) {
        var arguments = new Arguments();
        bool isTitleSet = false;

        HashSet<uint> episodeSet = new();
        for (int i = 0; i < args.Length; i++) {
            string currentArg = args[i];
            
            if (EqualsAny(currentArg, HELP_FLAGS)) {
                PrintHelp();
                Environment.Exit(0);
            }
            
            if (EqualsAny(currentArg, VERSION_FLAGS)) {
                Utils.OkExit(Program.VERSION);
            }
            
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
                    EnsureNextArgument("Episode number expected. Usage: -e <episode or episodes>", i, args.Length);

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
                EnsureNextArgument("The language argument wasn't provided. Help: --lang <language>", i, args.Length);

                arguments.language = args[i + 1];
                i++;
                continue;
            }

            if (EqualsAny(currentArg, EXTENSION_FILTER_FLAGS)) {
                EnsureNextArgument("No extension provided. Usage: --filter <extension>", i, args.Length);

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

            if (EqualsAny(currentArg, CONTAINS_FLAGS)) {
                EnsureNextArgument("No text was provided. Help: --has <text>", i, args.Length);

                arguments.textToContain = args[i + 1];
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
            
            if (EqualsAny(currentArg, JSON_OUTPUT_FLAGS)) {
                arguments.jsonOutput = true;
                continue;
            }
            
            if (EqualsAny(currentArg, CLEANUP_FLAGS)) {
                arguments.cleanup = true;
                continue;
            }
            
            if (EqualsAny(currentArg, PROVIDER_FLAGS)) {
                EnsureNextArgument("A provider was expected. Help: --provider <provider>", i, args.Length);
                string providerName = args[i + 1];
                i++;
                switch (providerName.ToLower()) {
                    case "opensubtitles":
                        arguments.provider = Provider.OpenSubtitles;
                        break;
                    case "subdl":
                        arguments.provider = Provider.SubDL;
                        break;
                    default:
                        Utils.FailExit("Unknown provider: " + providerName);
                        break;
                }
                continue;
            }

            if (EqualsAny(currentArg, EXTRACT_ARGS_FLAGS)) {
                EnsureNextArgument("A path to file was expected. Help: --extract <path>", i, args.Length);

                string path = args[i + 1];
                i++;
                Metadata meta = NameParser.parse(Path.GetFileNameWithoutExtension(path));
                applyMetadataToArguments(meta, ref arguments);
                continue;
            }

            if (EqualsAny(currentArg, FROM_SUBTITLE_FLAGS)) {
                EnsureNextArgument("A path to a subtitle file was expected. Help: --from <path>", i, args.Length);

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
                EnsureNextArgument("Subtitle extension was expected. Help: --convert-to <extension>", i, args.Length);

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
                EnsureNextArgument("An argument was expected. Help: --out <directory_path>", i, args.Length);

                string outputPath = args[i + 1];
                i++;
                arguments.outputDirectory = Utils.correctDirectoryPath(outputPath);
                continue;
            }

            if (EqualsAny(currentArg, AVAILABLE_LANGUAGES_FLAGS)) {
                PrintLanguages();
                Environment.Exit(0);
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

    private static void applyMetadataToArguments(Metadata meta, ref Arguments args) {
        args.title = meta.name;
        args.year = meta.year;
        if (meta.providedSeason || meta.providedEpisode) {
            if (meta.providedSeason) {
                args.providedSeason = true;
                args.season = meta.season;
            }
            if (meta.providedEpisode) {
                args.providedEpisode = true;
                args.episodes.Add(meta.episode);
            }
        } else {
            args.isMovie = true;
        }
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

    private static void EnsureNextArgument(string errorMsg, int currentIndex, int length) {
        if (currentIndex + 1 < length) {
            return;
        }
        Utils.FailExit(errorMsg);
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
            Console.WriteLine($"Extension '{convertToExtension}' doesn't match any recognized subtitle formats!");
            return false;
        }
        
        if (jsonOutput && !autoSelect) {
            Console.WriteLine("Json output cannot be used without --auto-select");
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
        return Utils.EqualsAny(arg, parameters);
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

    public readonly bool requiresModifications(string originalExt = "") {
        bool mayRequireConverting = convert && (originalExt == "" || convertToExtension != originalExt);
        return shiftMs != 0 || mayRequireConverting || cleanup;
    }

    public static void PrintHelp() {
        StringBuilder help = new StringBuilder();
        string programName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        help.AppendLine($"Subtitle downloader-converter v{Program.VERSION}");
        help.AppendLine();
        help.AppendLine($"Usage: {programName} [movie/show title] [arguments...]");
        help.AppendLine($"       {programName} --from [file path] [arguments...]");
        help.AppendLine($"       {programName} --extract [filename] [arguments...]");
        help.AppendLine();
        help.AppendLine("Options:");
        help.Append(formatOption(LANGUAGE_FLAGS, "Subtitle language code (3 letters)"));
        help.Append(formatOption(YEAR_FLAGS, "Release year of the movie or tv series"));
        help.Append(formatOption(EXTENSION_FILTER_FLAGS, "Filter subtitles by extension"));
        help.Append(formatOption(CONTAINS_FLAGS, "Filter subtitles by text contained in filename"));
        help.Append(formatOption(AUTO_SELECT_FLAGS, "Automatically selects subtitle to download"));
        help.Append(formatOption(FROM_SUBTITLE_FLAGS, "Parses a subtitle file (use with --shift and --convert-to)"));
        help.Append(formatOption(EXTRACT_ARGS_FLAGS, "Extracts production details from filename"));
        help.Append(formatOption(SHIFT_FLAGS, "Shifts subtitles in time by [+/- ms]"));
        help.Append(formatOption(CONVERT_FLAGS, "Subtitle format to convert to [srt/vtt]"));
        help.Append(formatOption(OUTPUT_FLAGS, "Destination directory where subtitles will be placed"));
        help.Append(formatOption(CLEANUP_FLAGS, "Removes empty subtitles (cues)"));
        help.Append(formatOption(PROVIDER_FLAGS, "Force subtitle provider, one of: OpenSubtitles, SubDL"));
        help.Append(formatOption(HELP_FLAGS, "Display this information (regardless of flag order)"));
        help.AppendLine();
        help.AppendLine("TV series options:");
        help.Append(formatOption(SEASON_FLAGS, "Season number of the tv series"));
        help.Append(formatOption(EPISODE_FLAGS, "Episode numbers of the tv series"));
        help.Append(formatOption(LIST_FLAGS, "Pretty print seasons and episodes"));
        help.Append(formatOption(PACK_FLAGS, "Download season as pack (<= 50 episodes) (faulty)"));
        help.AppendLine();
        help.AppendLine("To display available subtitle languages and their codes use: -languages");
        help.AppendLine("Season, episode and year arguments can be joined with numbers (e.g. -S2).");
        help.AppendLine("Episode numbers can be provided both as values and inclusive ranges (comma delimited e.g. -e 1,3-5,7).");
        help.AppendLine("File name provided with the '--extract' flag should have an extension & follow any of the three formats:");
        help.AppendLine(" - dotted: Series.Name.Year.SxEy");
        help.AppendLine(" - spaced: Production Name (Year) SxEy");
        help.AppendLine(" - dashed: Production-Name-Year-SxEy");
        help.AppendLine();
        help.AppendLine("Usage example:");
        help.AppendLine($"  {programName} \"The Godfather\" -y 1972");
        help.AppendLine($"  {programName} \"Office\" -S9 -E19 -y2005");
        help.AppendLine($"  {programName} \"Spongebob\" --year 1999 --season 2 --pack");
        help.AppendLine($"  {programName} \"Game of Thrones\" -s8 -e 1-10");
        help.AppendLine();
        help.AppendLine("Subtitle conversion example:");
        help.AppendLine($"  {programName} --from FastAndFurious.srt --shift +5000 --to vtt");
        help.AppendLine();
        Console.Write(help);
    }

    private const int PAD_LENGTH = 32;
    private static StringBuilder formatOption(string[] flags, string description) {
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
        str.AppendLine();
        return str;
    }

    public override string ToString() {
        StringBuilder str = new StringBuilder(32);
        str.Append($"{title} ");
        if (year != 0) {
            str.Append($"({year}) ");
        }
        if (!isMovie) {
            string ep;
            if (downloadPack) {
                ep = "[PACK]";
            }
            else if (episodes.Count > 1) {
                ep = "[" + string.Join(",", episodes) + "]";
            }
            else {
                ep = episodes[0].ToString();
            }
            str.Append($"S{season} E{ep} ");
        }

        if (!subtitleFromFile) {
            str.Append($"{language}");
        }
        return str.ToString();
    }

    private static JsonNode? SUB_DL_LANGUAGES = JsonNode.Parse(@"{
  ""AR"": ""Arabic"",
  ""BR_PT"": ""Brazillian Portuguese"",
  ""DA"": ""Danish"",
  ""NL"": ""Dutch"",
  ""EN"": ""English"",
  ""FA"": ""Farsi_Persian"",
  ""FI"": ""Finnish"",
  ""FR"": ""French"",
  ""ID"": ""Indonesian"",
  ""IT"": ""Italian"",
  ""NO"": ""Norwegian"",
  ""RO"": ""Romanian"",
  ""ES"": ""Spanish"",
  ""SV"": ""Swedish"",
  ""VI"": ""Vietnamese"",
  ""SQ"": ""Albanian"",
  ""AZ"": ""Azerbaijani"",
  ""BE"": ""Belarusian"",
  ""BN"": ""Bengali"",
  ""ZH_BG"": ""Big 5 code"",
  ""BS"": ""Bosnian"",
  ""BG"": ""Bulgarian"",
  ""BG_EN"": ""Bulgarian_English"",
  ""MY"": ""Burmese"",
  ""CA"": ""Catalan"",
  ""ZH"": ""Chinese BG code"",
  ""HR"": ""Croatian"",
  ""CS"": ""Czech"",
  ""NL_EN"": ""Dutch_English"",
  ""EN_DE"": ""English_German"",
  ""EO"": ""Esperanto"",
  ""ET"": ""Estonian"",
  ""KA"": ""Georgian"",
  ""DE"": ""German"",
  ""EL"": ""Greek"",
  ""KL"": ""Greenlandic"",
  ""HE"": ""Hebrew"",
  ""HI"": ""Hindi"",
  ""HU"": ""Hungarian"",
  ""HU_EN"": ""Hungarian_English"",
  ""IS"": ""Icelandic"",
  ""JA"": ""Japanese"",
  ""KO"": ""Korean"",
  ""KU"": ""Kurdish"",
  ""LV"": ""Latvian"",
  ""LT"": ""Lithuanian"",
  ""MK"": ""Macedonian"",
  ""MS"": ""Malay"",
  ""ML"": ""Malayalam"",
  ""MNI"": ""Manipuri"",
  ""PL"": ""Polish"",
  ""PT"": ""Portuguese"",
  ""RU"": ""Russian"",
  ""SR"": ""Serbian"",
  ""SI"": ""Sinhala"",
  ""SK"": ""Slovak"",
  ""SL"": ""Slovenian"",
  ""TL"": ""Tagalog"",
  ""TA"": ""Tamil"",
  ""TE"": ""Telugu"",
  ""TH"": ""Thai"",
  ""TR"": ""Turkish"",
  ""UK"": ""Ukranian"",
  ""UR"": ""Urdu""
}");
    
    public static void PrintLanguages() {
        const string OPEN_SUBTITLES_LANGUAGES = @"
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
        Console.WriteLine(OPEN_SUBTITLES_LANGUAGES);
    }
}

public enum Provider {
    OpenSubtitles, SubDL
}
