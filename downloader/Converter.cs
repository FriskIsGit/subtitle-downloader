using System.Text;

namespace subtitle_downloader.downloader;

public class Converter {
    /*
     Each subtitle has four parts in the SRT file.
       1. A numeric counter indicating the number or position of the subtitle.
       2. Start and end time of the subtitle separated by –> characters
       3. Subtitle text in one or more lines.
       4. A blank line indicating the end of the subtitle.
     */

    public static (SubtitleFile, Exception?) parse(string path, string extension) {
        if (!File.Exists(path)) {
            FailExit("Subtitle file does not exist! Ensure the path is correct.");
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
                        FailExit("Unable to detect extension, append the extension to file");
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
            }

            FailExit("Unsupported extension: " + extension);
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
        return null;
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
                FailExit("Invalid MPL version: " + version);
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
    
    private static (SubtitleFile, Exception?) parseVTT(StreamReader reader) {
        var subtitles = new List<Subtitle>(2048);

        string? vttMarker = reader.ReadLine();
        if (vttMarker != "WEBVTT") {
            var file = new SubtitleFile("vtt", subtitles);
            return (file, new SubtitleException("No VTT marker, aborting!"));
        }
        string? emptyLine = reader.ReadLine();
        while (!reader.EndOfStream) {
            string? timestamps = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(timestamps) || timestamps.Length == 1) {
                // since there's no marker for subtitle chunks we must quit on timestamps
                var file = new SubtitleFile("vtt", subtitles);
                return (file, null);
            }

            var (start, end, exception) = parseTimestamps(timestamps, false);
            if (exception != null) {
                var file = new SubtitleFile("vtt", subtitles);
                return (file, exception);
            }
            if (start == null || end == null) {
                throw new SubtitleException("Illegal State: One of the timestamps is null");
            }

            string content = parseSubtitleContent(reader);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }

        return (new SubtitleFile("vtt", subtitles), null);
    }


    public static (SubtitleFile, Exception?) parseMPL(StreamReader reader) {
        var subtitles = new List<Subtitle>(2048);
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                break;
            }
            int separator = line.IndexOf("][", StringComparison.Ordinal);
            if (separator == -1) {
                FailExit("Invalid MPL timestamp - missing ][ in line: " + line);
            }

            int timestampsEnd = line.IndexOf(']', separator + 3);    
            string startFrameText = line[1..separator];
            string endFrameText = line[(separator+2)..timestampsEnd];
            
            int endFrame = 0;
            bool framesOk = int.TryParse(startFrameText, out var startFrame) &&
                            int.TryParse(endFrameText, out endFrame);
            if (!framesOk) {
                FailExit("Failed to parse frames in line: " + line);
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
        var subtitles = new List<Subtitle>(2048);
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line)) {
                break;
            }
            int separator = line.IndexOf("}{", StringComparison.Ordinal);
            if (separator == -1) {
                FailExit("Invalid MPL2 timestamp! Expected: }{");
            }

            int timestampsEnd = line.IndexOf('}', separator + 3);    
            string startFrameText = line[1..separator];
            string endFrameText = line[(separator+2)..timestampsEnd];
            
            int endFrame = 0;
            bool framesOk = int.TryParse(startFrameText, out var startFrame) &&
                            int.TryParse(endFrameText, out endFrame);
            if (!framesOk) {
                FailExit("Failed to parse frames in line: " + line);
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
        var subtitles = new List<Subtitle>(2048);

        while (!reader.EndOfStream) {
            string? counter = reader.ReadLine();
            if (!int.TryParse(counter, out _)) {
                break;
            }
            string? timestamps = reader.ReadLine();
            if (timestamps == null) {
                var file = new SubtitleFile("srt", subtitles);
                return (file, new SubtitleException("Expected SRT timestamps [start --> end]"));
            }

            var (start, end, exception) = parseTimestamps(timestamps, true);
            if (exception != null) {
                var file = new SubtitleFile("srt", subtitles);
                return (file, exception);
            }
            if (start == null || end == null) {
                throw new SubtitleException("Illegal State: One of the timestamps is null");
            }

            string content = parseSubtitleContent(reader);
            var sub = new Subtitle(start, end, content);
            subtitles.Add(sub);
        }
        return (new SubtitleFile("srt", subtitles), null);
    }

    private static string parseSubtitleContent(StreamReader reader) {
        var content = new StringBuilder();
        // Parse subtitle text (may span over one or more lines). Nonetheless empty subs may mistakenly appear in some files.
        string? firstContentLine = reader.ReadLine();
        if (!string.IsNullOrEmpty(firstContentLine)) {
            // This will not hurt but will enable parsing files that don't stick to the specification
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

    public static void serialize(SubtitleFile subtitleFile, string path, string toExtension) {
        string newName = Path.GetFileNameWithoutExtension(path) + "_modified" + '.' + toExtension;
        Console.WriteLine("New name: " + newName);

        if (subtitleFile.format.StartsWith("mpl")) {
            subtitleFile.replacePipes();
        }

        if (subtitleFile.format == "mpl2") {
            subtitleFile.removeMPLStyling();
        }
        switch (toExtension) {
            case "srt":
                if (subtitleFile.format == "vtt") {
                    // maybe this is not even necessary
                    subtitleFile.stripHtmlTags();
                }
                serializeToSRT(subtitleFile.subtitles, newName);
                return;
            case "vtt":
                serializeToVTT(subtitleFile.subtitles, newName);
                return;
        }
        FailExit("Unsupported extension: " + toExtension);
        throw new Exception("UNREACHABLE");
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

    private static (Timecode?, Timecode?, Exception?) parseTimestamps(string timestamps, bool srt) {
        if (timestamps.Length < 23) {
            // minimal VTT timestamps length "01:11.111 --> 01:22.222".Length
            return (null, null, new SubtitleException("The timestamps are too short: " + timestamps));
        }

        int separator = timestamps.IndexOf(" --> ", StringComparison.Ordinal);
        if (separator == -1) {
            return (null, null, new SubtitleException("No timecode separator found: " + timestamps));
        }

        string startStamp = timestamps[..separator];
        string endStamp = timestamps[(separator+5)..];

        var (start, exception) = srt ? fromSrtTimestamp(startStamp) : fromVTTTimestamp(startStamp);
        if (exception != null) {
            return (null, null, exception);
        }
        (var end, exception) = srt ? fromSrtTimestamp(endStamp) : fromVTTTimestamp(endStamp);
        if (exception != null) {
            return (null, null, exception);
        }

        return (start, end, null);
    }

    private static (Timecode?, SubtitleException?) fromSrtTimestamp(string timestamp) {
        string[] split = timestamp.Split(':');
        if (split.Length != 3) {
            return (null, new SubtitleException("Invalid timestamp, not a triple split"));
        }

        if (!int.TryParse(split[0], out int hours)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse hours"));
        }

        if (!int.TryParse(split[1], out int minutes)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse minutes"));
        }

        string[] subSplit = split[2].Split(',');
        if (subSplit.Length != 2) {
            return (null, new SubtitleException("Invalid sub stamp, not a double split"));
        }

        if (!int.TryParse(subSplit[0], out int seconds)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse seconds"));
        }

        if (!int.TryParse(subSplit[1], out int milliseconds)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse milliseconds"));
        }

        return (new Timecode(hours, minutes, seconds, milliseconds), null);
    }

    // Timecode hours are optional
    private static (Timecode?, SubtitleException?) fromVTTTimestamp(string timestamp) {
        int dot = timestamp.IndexOf('.');
        if (dot == -1) {
            return (null, new SubtitleException("Invalid timestamp, fractional values must be preceded by a dot"));
        }
        if (!int.TryParse(timestamp[(dot+1)..], out int milliseconds)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse milliseconds"));
        }

        string baseStamp = timestamp[..dot];
        string[] split = baseStamp.Split(':');

        int minutes, seconds;
        switch (split.Length) {
            case 2:
                if (!int.TryParse(split[0], out minutes)) {
                    return (null, new SubtitleException("Invalid timestamp, failed to parse minutes"));
                }
                if (!int.TryParse(split[1], out seconds)) {
                    return (null, new SubtitleException("Invalid timestamp, failed to parse seconds"));
                }
                return (new Timecode(0, minutes, seconds, milliseconds), null);
            case 3:
                if (!int.TryParse(split[0], out int hours)) {
                    return (null, new SubtitleException("Invalid timestamp, failed to parse hours"));
                }
                if (!int.TryParse(split[1], out minutes)) {
                    return (null, new SubtitleException("Invalid timestamp, failed to parse minutes"));
                }
                if (!int.TryParse(split[2], out seconds)) {
                    return (null, new SubtitleException("Invalid timestamp, failed to parse seconds"));
                }

                Timecode timecode = new Timecode(hours, minutes, seconds, milliseconds);
                return (timecode, null);
        }
        return (null, new SubtitleException("Invalid timestamp, expected either a 2-split or a 3-split"));
    }

    private static void FailExit(string message) {
        Console.WriteLine(message);
        Environment.Exit(1);
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
        foreach (Subtitle sub in subtitles) {
            sub.content = sub.replacePipes();
        }
    }
    
    public void removeMPLStyling() {
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

    // In MPL pipes represent new lines
    public string replacePipes() {
        return content.Replace('|', '\n');
    }

    // Styling examples
    // html tags: <b> <i> <u> <c> <v> <ruby> <rt>
    // mpl tags: {y:b} {y:i} {y:u}
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
    public int hours, minutes, seconds, milliseconds;

    public Timecode(int hr, int min, int s, int ms) {
        hours = hr;
        minutes = min;
        seconds = s;
        milliseconds = ms;
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
    public SubtitleException() {}
    public SubtitleException(string message) : base(message) {}
}
