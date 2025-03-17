using System.Text;
using static subtitle_downloader.downloader.ParsingException;

namespace subtitle_downloader.downloader;

public class Converter {
    private const int SUBTITLE_CAPACITY = 1024;
    /*
     Each subtitle has four parts in the SRT file.
       1. A numeric counter indicating the number or position of the subtitle.
       2. Start and end time of the subtitle separated by –> characters
       3. Subtitle text in one or more lines.
       4. A blank line indicating the end of the subtitle.
     */

    public static (SubtitleFile, Exception?) parse(string path, string extension) {
        if (!File.Exists(path)) {
            Utils.FailExit("Subtitle file does not exist! Ensure the path is correct.");
        }
        
        using FileStream file = File.OpenRead(path);
        using var reader = new StreamReader(file, Encoding.UTF8, true);

        // This loop exists so that in case the format needs to be detected the switch statement will be reused
        while (true) {
            switch (extension) {
                case "srt":
                    return parseSRT(reader);
                case "vtt":
                    return parseVTT(reader);
                case "txt":
                case "sub":
                    string? detectedExt = detectSubtitleFormat(reader);
                    if (detectedExt == null) {
                        Utils.FailExit("Unable to detect extension, append the extension to file");
                        break;
                    }
                    extension = detectedExt;
                    continue;
                // MPL (MicroDVD): [1][2] Lorem ipsum
                case "mpl":
                    return parseMPL(reader);
                // MPL2 (Enhanced MicroDVD): {1}{2}{y:i} Lorem ipsum
                case "mpl2":
                    return parseMPL2(reader);
                // https://github.com/libass/libass/wiki/ASS-File-Format-Guide
                case "ssa":
                case "ass":
                    return parseSubStationAlpha(reader);
                case "tmp":
                    return parseTimeBased(reader);
            }

            Utils.FailExit("Unsupported extension: " + extension);
            throw new Exception("UNREACHABLE");
        }
    }

    private static string? detectSubtitleFormat(StreamReader reader) {
        string? firstLine = reader.ReadLine();
        reader.DiscardBufferedData();
        reader.BaseStream.Seek(0, SeekOrigin.Begin);

        if (firstLine == null) {
            return null;
        }

        if (firstLine.Length == 1 && firstLine[0] == '1') {
            return "srt";
        }

        if (firstLine.Length == 6 && firstLine == "WEBVTT") {
            return "vtt";
        }

        if (firstLine.Length < 6) {
            return null;
        }

        if (hasMPLStructure(firstLine, 1)) {
            return "mpl";
        }

        if (hasMPLStructure(firstLine, 2)) {
            return "mpl2";
        }

        if (hasSSAStructure(firstLine)) {
            return "ssa";
        }
        
        if (hasTMPStructure(firstLine)) {
            return "tmp";
        }
        
        return null;
    }
    
    private static bool hasTMPStructure(string line) {
        if (line.Length < 9) {
            return false;
        }
        // 00:00:04:
        return line[2] == ':' && line[5] == ':' && line[8] == ':';
    }
    
    private static bool hasSSAStructure(string line) {
        return line.StartsWith('[') && line.EndsWith(']');
    }

    private static bool hasMPLStructure(string line, int version) {
        char openBracket, closingBracket;
        switch (version) {
            case 1:
                openBracket = '[';
                closingBracket = ']';
                break;
            case 2:
                openBracket = '{';
                closingBracket = '}';
                break;
            default:
                Utils.FailExit("Invalid MPL version: " + version);
                return false;
        }

        if (line[0] != openBracket) {
            return false;
        }

        string separatorBrackets = "" + closingBracket + openBracket;
        int separator = line.IndexOf(separatorBrackets, StringComparison.Ordinal);
        if (separator == -1) {
            return false;
        }

        int timestampsEnd = line.IndexOf(closingBracket, separator + 3);    
        string startFrame = line[1..separator];
        string endFrame = line[(separator+2)..timestampsEnd];
        return int.TryParse(startFrame, out _) && int.TryParse(endFrame, out _);
    }
    
    // Parsing will not preserve REGION, NOTE or STYLE definition blocks
    private static (SubtitleFile, Exception?) parseVTT(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);

        string? vttMarker = reader.ReadLine();
        if (vttMarker == null || !vttMarker.StartsWith("WEBVTT")) {
            var file = new SubtitleFile("vtt", subtitles);
            return (file, new SubtitleException(VTT_NO_MARKER));
        }
        forwardReaderUntilEmptyLine(reader);
        while (!reader.EndOfStream) {
            // Skip any possible cue identifiers or definition blocks
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                // No WebVTT cue identifier
                var file = new SubtitleFile("vtt", subtitles);
                return (file, null);
            }

            if (line.StartsWith("NOTE") || line.StartsWith("REGION") || line.StartsWith("STYLE")) {
                forwardReaderUntilEmptyLine(reader);
                continue;
            }
            var (start, end, exception) = parseTimestamps(line, false);
            if (exception != null) {
                ParsingException type = exception.type;
                if (type == TIMESTAMPS_TOO_SHORT || type == NO_TIMECODE_SEPARATOR) {
                    // Likely there's some marker on top of the timestamps
                    continue;
                }
                var file = new SubtitleFile("vtt", subtitles);
                return (file, exception);
            }
            if (start == null || end == null) {
                throw new SubtitleException(NULL_TIMESTAMP);
            }

            string content = parseSubtitleContent(reader);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }

        return (new SubtitleFile("vtt", subtitles), null);
    }


    public static (SubtitleFile, Exception?) parseMPL(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                break;
            }
            int separator = line.IndexOf("][", StringComparison.Ordinal);
            if (separator == -1) {
                Utils.FailExit("Invalid MPL timestamp - missing ][ in line: " + line);
            }

            int timestampsEnd = line.IndexOf(']', separator + 3);    
            string startFrameText = line[1..separator];
            string endFrameText = line[(separator+2)..timestampsEnd];
            
            int endFrame = 0;
            bool framesOk = int.TryParse(startFrameText, out var startFrame) &&
                            int.TryParse(endFrameText, out endFrame);
            if (!framesOk) {
                Utils.FailExit("Failed to parse frames in line: " + line);
            }

            string content = line[(timestampsEnd+1)..];
            var start = Timecode.fromFrames(startFrame, FPS);
            var end = Timecode.fromFrames(endFrame, FPS);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }

        var subtitleFile = new SubtitleFile("mpl", subtitles);
        return (subtitleFile, null);
    }
    
    public static int FPS = 25;
    public static (SubtitleFile, Exception?) parseMPL2(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                break;
            }
            int separator = line.IndexOf("}{", StringComparison.Ordinal);
            if (separator == -1) {
                Utils.FailExit("Invalid MPL2 timestamp! Expected: }{");
            }

            int timestampsEnd = line.IndexOf('}', separator + 3);    
            string startFrameText = line[1..separator];
            string endFrameText = line[(separator+2)..timestampsEnd];
            
            int endFrame = 0;
            bool framesOk = int.TryParse(startFrameText, out var startFrame) &&
                            int.TryParse(endFrameText, out endFrame);
            if (!framesOk) {
                Utils.FailExit("Failed to parse frames in line: " + line);
            }

            string content = line[(timestampsEnd+1)..];
            var start = Timecode.fromFrames(startFrame, FPS);
            var end = Timecode.fromFrames(endFrame, FPS);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }

        var subtitleFile = new SubtitleFile("mpl2", subtitles);
        return (subtitleFile, null);
    }

    public static (SubtitleFile, Exception?) parseSRT(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);

        SubtitleException? parsingException = null;
        while (!reader.EndOfStream) {
            string? counter = reader.ReadLine();
            if (Utils.isNullOrEmptyOrZWNBSP(counter)) {
                break;
            }
            if (!int.TryParse(counter, out _)) {
                parsingException = new SubtitleException(SRT_NO_NUMERICAL_COUNTER);
                break;
            }
            string? timestamps = reader.ReadLine();
            if (timestamps == null) {
                parsingException = new SubtitleException(NO_TIMESTAMPS);
                break;
            }

            var (start, end, exception) = parseTimestamps(timestamps, true);
            if (exception != null) {
                parsingException = exception;
                break;
            }
            if (start == null || end == null) {
                throw new SubtitleException(NULL_TIMESTAMP);
            }

            string content = parseSubtitleContent(reader);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }
        return (new SubtitleFile("srt", subtitles), parsingException);
    }

    public static (SubtitleFile, Exception?) parseTimeBased(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);

        SubtitleException? exception = null;
        bool firstCue = true;
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (line == null) {
                break;
            }
            if (line.Length < 9) {
                continue;
            }

            var timestamp = line[..8];
            var split = timestamp.Split(':');
            if (split.Length != 3) {
                continue;
            }
            if (!int.TryParse(split[0], out int hours)) {
                exception = new SubtitleException(INVALID_HOURS_TIMESTAMP);
                break;
            }
            
            if (!int.TryParse(split[1], out int minutes)) {
                exception = new SubtitleException(INVALID_MINUTES_TIMESTAMP);
                break;
            }
            if (!int.TryParse(split[2], out int seconds)) {
                exception = new SubtitleException(INVALID_SECONDS_TIMESTAMP);
                break;
            }

            Timecode start = new Timecode(hours, minutes, seconds, 0);
            string content = line[9..];
            var sub = new Subtitle(start, Timecode.END_CODE, content);
            subtitles.Add(sub);

            if (firstCue) {
                firstCue = false;
                continue;
            }

            Subtitle secondLast = subtitles[^2];
            secondLast.end = sub.start.copy();

        }
        return (new SubtitleFile("tmp", subtitles), exception);
    }

    public static (SubtitleFile, SubtitleException?) parseSubStationAlpha(StreamReader reader) {
        var subtitles = new List<Subtitle>(SUBTITLE_CAPACITY);
        // Skip until [Events] section
        bool foundEventsSection = false;
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (line == null) {
                break;
            }

            if (hasSSAStructure(line) && line.Length == 8) {
                foundEventsSection = true;
                break;
            }
        }

        if (!foundEventsSection) {
            return (new SubtitleFile("ssa", subtitles), new SubtitleException(SSA_NO_EVENTS_SECTION));
        }

        SubtitleException? parsingException = null;
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (line == null) {
                break;
            }

            if (!line.StartsWith("Dialogue")) {
                continue;
            }

            // Every line of Dialogue must consist of 10 parts
            // Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            int commas = 0;
            int textStart = -1;
            for (int i = 0; i < line.Length; i++) {
                if (line[i] == ',' && ++commas == 9) {
                    textStart = i + 1;
                    break;
                }
            }

            if (textStart < 0) {
                parsingException = new SubtitleException(SSA_INVALID_DIALOGUE, "9th comma is missing: " + line);
                break;
            }
            string[] parts = line[10..textStart].Split(",");
            (var start, parsingException) = fromSsaTimestamp(parts[1]);
            if (parsingException != null) {
                break;
            }
            (var end, parsingException) = fromSsaTimestamp(parts[2]);
            if (parsingException != null) {
                break;
            }
            string content = line[textStart..];
            // In SSA line breaks are represented as \N
            content = content.Replace("\\N", "\n");
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }
        return (new SubtitleFile("ssa", subtitles), parsingException);
    }

    private static string parseSubtitleContent(StreamReader reader) {
        var content = new StringBuilder();
        // Parse subtitle text (may span over one or more lines). Nonetheless empty subs may mistakenly appear in some files.
        string? firstContentLine = reader.ReadLine();
        if (!string.IsNullOrEmpty(firstContentLine)) {
            // This will not hurt but will enable parsing files that do not adhere strictly to the specification
            content.Append(firstContentLine);
        }

        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                break;
            }
            content.Append('\n');
            content.Append(line);
        }
        
        return content.ToString();
    }

    // Serializes a subtitle file to the given extension returning the path where it was stored
    public static string serialize(SubtitleFile subtitleFile, string path, string toExtension) {
        string newName = Path.GetFileNameWithoutExtension(path) + "_modified" + '.' + toExtension;
        string newPath = Path.Join(Path.GetDirectoryName(path), newName);

        string fromFormat = subtitleFile.format;
        if (fromFormat.StartsWith("mpl") || fromFormat.StartsWith("tmp")) {
            subtitleFile.replacePipes();
        }

        if (fromFormat == "mpl2" || fromFormat == "ssa" || fromFormat == "ass") {
            subtitleFile.removeCurlyStyling();
        }
        switch (toExtension) {
            case "srt":
                if (subtitleFile.format == "vtt") {
                    // maybe this is not even necessary
                    subtitleFile.stripHtmlTags();
                }
                serializeToSRT(subtitleFile.subtitles, newPath);
                return newPath;
            case "vtt":
                serializeToVTT(subtitleFile.subtitles, newPath);
                return newPath;
        }
        Utils.FailExit("Unsupported extension: " + toExtension);
        return "";
    }

    private static readonly byte[] DOUBLE_NEW_LINE = "\n\n"u8.ToArray();
    
    private static void serializeToSRT(List<Subtitle> subtitles, string path) {
        using FileStream file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.UTF8);

        int counter = 0;

        foreach (var sub in subtitles) {
            // could have been shifted to negative values
            if (sub.start.isNegative()) {
                if (sub.end.isNegative()) {
                    continue;
                }
                // it's possible to preserve the subtitle because the end is not negative
                sub.start = Timecode.ZERO_CODE;
            }
            counter++;
            
            file.Write(Encoding.ASCII.GetBytes(counter + "\n"));
            string timestamps = sub.start.toSrt() + " --> " + sub.end.toSrt() + "\n";
            file.Write(Encoding.ASCII.GetBytes(timestamps));
            file.Write(Encoding.UTF8.GetBytes(sub.content));
            file.Write(DOUBLE_NEW_LINE);
        }
        file.Flush();
    }

    public static void serializeToVTT(List<Subtitle> subtitles, string path) {
        using FileStream file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.UTF8);

        file.Write("WEBVTT\n\n"u8.ToArray());

        foreach (var sub in subtitles) {
            // could have been shifted to negative values
            if (sub.start.isNegative()) {
                if (sub.end.isNegative()) {
                    continue;
                }
                sub.start = Timecode.ZERO_CODE;
            }
            string timestamps = sub.start.toVtt() + " --> " + sub.end.toVtt() + "\n";
            file.Write(Encoding.ASCII.GetBytes(timestamps));
            file.Write(Encoding.UTF8.GetBytes(sub.content));
            file.Write(DOUBLE_NEW_LINE);
        }
    }

    private static void forwardReaderUntilEmptyLine(StreamReader reader) {
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                return;
            }
        }
    }

    private static (Timecode?, Timecode?, SubtitleException?) parseTimestamps(string timestamps, bool srt) {
        if (timestamps.Length < 23) {
            // minimal VTT timestamps length "01:11.111 --> 01:22.222".Length
            return (null, null, new SubtitleException(TIMESTAMPS_TOO_SHORT, timestamps));
        }

        int separator = timestamps.IndexOf(" --> ", StringComparison.Ordinal);
        if (separator == -1) {
            return (null, null, new SubtitleException(NO_TIMECODE_SEPARATOR, timestamps));
        }

        string startStamp = timestamps[..separator];
        string endStamp = timestamps[(separator+5)..];

        var (start, exception) = srt ? fromSrtTimestamp(startStamp) : fromVttTimestamp(startStamp);
        if (exception != null) {
            return (null, null, exception);
        }
        (var end, exception) = srt ? fromSrtTimestamp(endStamp) : fromVttTimestamp(endStamp);
        if (exception != null) {
            return (null, null, exception);
        }

        return (start, end, null);
    }

    //h:mm:ss.cs, hours:minute:seconds.hundredths
    private static (Timecode?, SubtitleException?) fromSsaTimestamp(string timestamp) {
        int dot = timestamp.IndexOf('.');
        if (dot == -1) {
            return (null, new SubtitleException(NO_CENTISECONDS_TIMESTAMP, "Expected a value in range [00, 99]"));
        }
        string[] split = timestamp[..dot].Split(':');
        
        if (!int.TryParse(split[0], out int hours)) {
            return (null, new SubtitleException(INVALID_HOURS_TIMESTAMP, timestamp));
        }

        if (!int.TryParse(split[1], out int minutes)) {
            return (null, new SubtitleException(INVALID_MINUTES_TIMESTAMP, timestamp));
        }
        
        if (!int.TryParse(split[2], out int seconds)) {
            return (null, new SubtitleException(INVALID_SECONDS_TIMESTAMP, timestamp));
        }
        
        if (!int.TryParse(timestamp[(dot+1)..], out int centiseconds) || centiseconds > 99) {
            return (null, new SubtitleException(INVALID_CENTISECONDS_TIMESTAMP, timestamp));
        }
        
        return (new Timecode(hours, minutes, seconds, centiseconds * 10), null);
    }

    private static (Timecode?, SubtitleException?) fromSrtTimestamp(string timestamp) {
        timestamp = timestamp.Trim();
        string[] split = timestamp.Split(':');
        if (split.Length != 3) {
            return (null, new SubtitleException(INVALID_TIMESTAMP, "Not a triple split: " + timestamp));
        }

        if (!int.TryParse(split[0], out int hours)) {
            return (null, new SubtitleException(INVALID_HOURS_TIMESTAMP, timestamp));
        }

        if (!int.TryParse(split[1], out int minutes)) {
            return (null, new SubtitleException(INVALID_MINUTES_TIMESTAMP, timestamp));
        }

        string[] subSplit = split[2].Split(',');
        if (subSplit.Length != 2) {
            return (null, new SubtitleException(INVALID_TIMESTAMP, "Not a double split on comma: " + timestamp));
        }

        if (!int.TryParse(subSplit[0], out int seconds)) {
            return (null, new SubtitleException(INVALID_SECONDS_TIMESTAMP, timestamp));
        }

        if (!int.TryParse(subSplit[1], out int milliseconds)) {
            return (null, new SubtitleException(INVALID_MILLISECONDS_TIMESTAMP, timestamp));
        }

        return (new Timecode(hours, minutes, seconds, milliseconds), null);
    }

    // Timecode hours are optional
    private static (Timecode?, SubtitleException?) fromVttTimestamp(string timestamp) {
        timestamp = timestamp.Trim();
        int dot = timestamp.IndexOf('.');
        if (dot == -1) {
            return (null, new SubtitleException(INVALID_TIMESTAMP, "Fractional values must be preceded by a dot: " + timestamp));
        }
        // Additional trailing details are cut off:    00:20.000 region:fred align:left
        if (dot+4 > timestamp.Length || !int.TryParse(timestamp[(dot+1)..(dot+4)], out int milliseconds)) {
            return (null, new SubtitleException(INVALID_MILLISECONDS_TIMESTAMP, timestamp));
        }

        string baseStamp = timestamp[..dot];
        string[] split = baseStamp.Split(':');

        int minutes, seconds;
        switch (split.Length) {
            case 2:
                if (!int.TryParse(split[0], out minutes)) {
                    return (null, new SubtitleException(INVALID_MINUTES_TIMESTAMP, timestamp));
                }
                if (!int.TryParse(split[1], out seconds)) {
                    return (null, new SubtitleException(INVALID_SECONDS_TIMESTAMP, timestamp));
                }
                return (new Timecode(0, minutes, seconds, milliseconds), null);
            case 3:
                if (!int.TryParse(split[0], out int hours)) {
                    return (null, new SubtitleException(INVALID_HOURS_TIMESTAMP, timestamp));
                }
                if (!int.TryParse(split[1], out minutes)) {
                    return (null, new SubtitleException(INVALID_MINUTES_TIMESTAMP, timestamp));
                }
                if (!int.TryParse(split[2], out seconds)) {
                    return (null, new SubtitleException(INVALID_SECONDS_TIMESTAMP, timestamp));
                }

                Timecode timecode = new Timecode(hours, minutes, seconds, milliseconds);
                return (timecode, null);
        }
        return (null, new SubtitleException(INVALID_TIMESTAMP, "Expected either a 2-split or a 3-split: " + timestamp));
    }
}

public class SubtitleFile {
    // format is the extension that applies to the subtitles
    public readonly string format;
    public List<Subtitle> subtitles;

    public SubtitleFile(string format, List<Subtitle> subtitles) {
        this.format = format;
        this.subtitles = subtitles;
    }

    public void shiftBy(int ms) {
        foreach (Subtitle sub in subtitles) {
            sub.shiftBy(ms);
        }
    }
    
    public void stripHtmlTags() {
        foreach (Subtitle sub in subtitles) {
            sub.content = sub.stripStyling('<', '>');
        }
    }
    
    public void replacePipes() {
        // In MPL pipes represent new lines
        foreach (Subtitle sub in subtitles) {
            sub.content = sub.content.Replace('|', '\n');
        }
    }
    
    public void removeCurlyStyling() {
        foreach (Subtitle sub in subtitles) {
            sub.content = sub.stripStyling('{', '}');
        }
    }

    public int count() {
        return subtitles.Count;
    }
}

public class Subtitle {
    public Timecode start;
    public Timecode end;
    public string content;

    public Subtitle(Timecode start, Timecode end, string content) {
        this.start = start;
        this.end = end;
        this.content = content;
    }

    // Styling examples
    // html tags: <b> <i> <u> <c> <v> <ruby> <rt>
    // mpl tags: {y:b} {y:i} {y:u}
    // ssa tags: {\i1} {\i0} {\an4\pos(750,869)}
    public string stripStyling(char stylingStart, char stylingEnd) {
        var builder = new StringBuilder();
        for (var i = 0; i < content.Length; i++) {
            char c = content[i];
            if (c == stylingStart) {
                int closingSign = content.IndexOf(stylingEnd, i + 1);
                if (closingSign == -1) {
                    // this shouldn't happen but it's a sign that it could be part of text
                    builder.Append(c);
                    break;
                }

                i = closingSign;
                continue;
            }
            builder.Append(c);
        }

        return builder.ToString();
    }

    public void shiftBy(int ms) {
        if (ms > 0) {
            start.shiftForwardBy(ms);
            end.shiftForwardBy(ms);
        } else {
            ms = Math.Abs(ms);
            start.shiftBackBy(ms);
            end.shiftBackBy(ms);
        }
    }
}

public class Timecode {
    public static readonly Timecode ZERO_CODE = new (0, 0, 0, 0);
    public static readonly Timecode END_CODE = new (99, 59, 59, 999);
    public int hours, minutes, seconds, milliseconds;

    public Timecode(int hr, int min, int s, int ms) {
        hours = hr;
        minutes = min;
        seconds = s;
        milliseconds = ms;
    }

    public Timecode copy() {
        return new Timecode(hours, minutes, seconds, milliseconds);
    }

    public static Timecode fromSeconds(int seconds) {
        int hr = 0, m = 0, s = 0;
        if (seconds >= 3600) {
            hr = seconds / 3600;
            seconds %= 3600;
        }
        if (seconds >= 60) {
            m = (seconds / 60) | 0;
            seconds %= 60;
        }
        if (seconds > 0) {
            s = seconds;
        }
        return new Timecode(hr, m, s, 0);
    }

    public static Timecode fromFrames(int frames, double fps) {
        double secondsFraction = frames / fps;
        int hours = (int) secondsFraction / 3600;
        if (hours > 0) {
            secondsFraction %= 3600;
        }
        
        int minutes = (int) secondsFraction / 60;
        if (minutes > 0) {
            secondsFraction %= 60;
        }

        int seconds = (int)secondsFraction;
        secondsFraction -= seconds;

        int ms = (int)(secondsFraction * 1000);
        return new Timecode(hours, minutes, seconds, ms);
    }
    
    public bool isNegative() {
        return hours < 0 || minutes < 0 || seconds < 0 || milliseconds < 0;
    }
    public string toSrt() {
        return formatUnit(hours, 2) + ":" +
               formatUnit(minutes, 2) + ":" +
               formatUnit(seconds, 2) + "," +
               formatUnit(milliseconds, 3);
    }

    public string toVtt() {
        return (hours > 0 ? formatUnit(hours, 2) + ":" : "")
               + formatUnit(minutes, 2) + ":"
               + formatUnit(seconds, 2) + "."
               + formatUnit(milliseconds, 3);
    }

    public void shiftForwardBy(int ms) {
        milliseconds += ms;

        int additionalSeconds = milliseconds / 1000;
        if (milliseconds >= 1000) {
            milliseconds %= 1000;
        }
        seconds += additionalSeconds;

        int additionalMinutes = seconds / 60;
        if (seconds >= 60) {
            seconds %= 60;
        }
        minutes += additionalMinutes;

        int additionalHours = minutes / 60;
        if (minutes >= 60) {
            minutes %= 60;
        }
        hours += additionalHours;
        // cannot mod hours because it cannot be carried over to a higher unit
    }

    // expecting a positive ms value
    public void shiftBackBy(int ms) {
        milliseconds -= ms;

        int secondsOffset = milliseconds / 1000;
        if (milliseconds < 0) {
            milliseconds %= 1000;
            if (milliseconds < 0) {
                milliseconds += 1000;
                secondsOffset -= 1;
            }
        }
        seconds += secondsOffset;

        int minutesOffset = seconds / 60;
        if (seconds < 0) {
            seconds %= 60;
            if (seconds < 0) {
                seconds += 60;
                minutesOffset -= 1;
            }
        }
        minutes += minutesOffset;

        int hourOffset = minutes / 60;
        if (minutes < 0) {
            minutes %= 60;
            if (minutes < 0) {
                minutes += 60;
                hourOffset -= 1;
            }
        }
        hours += hourOffset;
        // negative hours are invalid and won't be serialized
    }

    private static string formatUnit(int value, int length) {
        var format = new StringBuilder();
        string stringedValue = value.ToString();
        int pad = length - stringedValue.Length;
        for (int i = 0; i < pad; i++) {
            format.Append('0');
        }
        format.Append(stringedValue);
        return format.ToString();
    }
}

public class SubtitleException : Exception {
    public readonly ParsingException type;

    public SubtitleException() {
        type = UNKNOWN;
    }

    private static string formatMessage(ParsingException type, string msg) {
        if (string.IsNullOrEmpty(msg)) {
            return type.ToString();
        }

        return type + ": " + msg;
    }

    public SubtitleException(ParsingException exception, string msg = "") : base(formatMessage(exception, msg)) {
        type = exception;
    }
}

public enum ParsingException {
    VTT_NO_MARKER, 
    SRT_NO_NUMERICAL_COUNTER, 
    
    INVALID_TIMESTAMP,
    
    INVALID_HOURS_TIMESTAMP,
    INVALID_MINUTES_TIMESTAMP,
    INVALID_SECONDS_TIMESTAMP,
    INVALID_MILLISECONDS_TIMESTAMP,
    INVALID_CENTISECONDS_TIMESTAMP,
    NO_CENTISECONDS_TIMESTAMP,

    SSA_NO_EVENTS_SECTION,
    SSA_INVALID_DIALOGUE,
    
    TIMESTAMPS_TOO_SHORT, NO_TIMECODE_SEPARATOR, NO_TIMESTAMPS, NULL_TIMESTAMP, 
    UNKNOWN
}
