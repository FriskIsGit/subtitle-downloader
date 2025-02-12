namespace subtitle_downloader.downloader;

class Program {
    public const string VERSION = "1.7.3";
    public static void Main(string[] args) {
        switch (args.Length) {
            case 0:
            case 1 when args[0].Equals("-h") || args[0].Equals("-help") || args[0].Equals("--help"):
                Arguments.PrintHelp();
                return;
            case 1 when args[0].Equals("-v") || args[0].StartsWith("--ver"):
                Console.WriteLine(VERSION);
                return;
            case 1 when args[0].Equals("-languages"):
                Arguments.PrintLanguages();
                return;
        }

        var arguments = Arguments.Parse(args);
        if (!arguments.Validate()) {
            Console.WriteLine("Invalid arguments detected. Exiting.");
            return;
        }
        Console.WriteLine(arguments);

        if (arguments.devMode && arguments.devGenerationCount != 0) {
            generateCues(arguments);
            return;
        }

        var flow = new ProgramFlow(arguments);
        flow.execute();
    }

    private static void generateCues(Arguments arguments) {
        int count = arguments.devGenerationCount;
        Console.WriteLine("Generating " + count + " cues.");
        const int length = 2;
        const int delay = 1;
        int offsetSeconds = 0;
        var subtitles = new List<Subtitle>(count);
        for (int i = 0; i < count; i++) {
            var start = Timecode.fromSeconds(offsetSeconds);
            int endSeconds = offsetSeconds + length;
            var end = Timecode.fromSeconds(endSeconds);
            var subtitle = new Subtitle(start, end, 
                i + ". Auto-Generated chunk [" + start.toVtt() + "-->" + end.toVtt() + ']');
            subtitles.Add(subtitle);
            offsetSeconds = endSeconds + delay;
        }

        string extension = arguments.convert ? arguments.convertToExtension : "srt";
        if (arguments.convert) {
            Console.WriteLine("Serializing!");
            var file = new SubtitleFile("", subtitles);
            string path = Converter.serialize(file, "gen",  extension);
            Console.WriteLine($"Saved to {path}");
        }
    }
}