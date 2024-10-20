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

    public static (List<Subtitle>, Exception?) parse(string path, string extension) {
        if (!File.Exists(path)) {
            FailExit("Subtitle file does not exist! Ensure the path is correct.");
        }

        switch (extension) {
            case "srt":
                return parseSRT(path);
            case "vtt":
                return parseVTT(path);
        }
        FailExit("Unsupported extension: " + extension);
        throw new Exception("UNREACHABLE");
    }

    private static (List<Subtitle>, Exception?) parseVTT(string path) {
        using FileStream file = File.OpenRead(path);
        using var reader = new StreamReader(file, Encoding.UTF8, true);
        
        var subtitles = new List<Subtitle>(2048);
        
        string? vttMarker = reader.ReadLine();
        if (vttMarker != "WEBVTT") {
            return (subtitles, new SubtitleException("No VTT marker, aborting!"));
        }
        string? emptyLine = reader.ReadLine();
        while (!reader.EndOfStream) {
            string? timestamps = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(timestamps) || timestamps.Length == 1) {
                // since there's no marker for subtitle chunks we must quit on timestamps
                return (subtitles, null);
            }
            
            var (start, end, exception) = parseTimestamps(timestamps, false);
            if (exception != null) {
                return (subtitles, exception);
            }
            if (start == null || end == null) {
                throw new SubtitleException("Illegal State: One of the timestamps is null");
            }
            
            var content = new StringBuilder();
            // Parse subtitle text (may span over one or more lines)
            while (!reader.EndOfStream) {
                string? line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) {
                    break;
                }

                content.Append(line);
                content.Append('\n');
            }
            
            var sub = new Subtitle(start, end, content.ToString());
            subtitles.Add(sub);
        }

        return (subtitles, null);
    }

    public static (List<Subtitle>, Exception?) parseSRT(string path) {
        using FileStream file = File.OpenRead(path);
        using var reader = new StreamReader(file, Encoding.UTF8, true);
        
        var subtitles = new List<Subtitle>(2048);
        
        while (!reader.EndOfStream) {
            string? counter = reader.ReadLine();
            if (!int.TryParse(counter, out _)) {
                break;
            }
            string? timestamps = reader.ReadLine();
            if (timestamps == null) {
                return (subtitles, new SubtitleException("Expected SRT timestamps [start --> end]"));
            }
            
            var (start, end, exception) = parseTimestamps(timestamps, true);
            if (exception != null) {
                return (subtitles, exception);
            }
            if (start == null || end == null) {
                throw new SubtitleException("Illegal State: One of the timestamps is null");
            }
            
            var content = new StringBuilder();
            // Parse subtitle text (may span over one or more lines)
            while (!reader.EndOfStream) {
                string? line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) {
                    break;
                }

                content.Append(line);
                content.Append('\n');
            }
            
            var sub = new Subtitle(start, end, content.ToString());
            subtitles.Add(sub);
        }

        return (subtitles, null);
    }

    public static void serializeTo(List<Subtitle> subtitles, string path, string extension) {
        string newName = Path.GetFileNameWithoutExtension(path) + '.' + extension;
        Console.WriteLine("New name: " + newName);
        switch (extension) {
            case "srt":
                serializeToSRT(subtitles, newName);
                return;
            case "vtt":
                serializeToVTT(subtitles, newName);
                return;
        }
        FailExit("Unsupported extension: " + extension);
        throw new Exception("UNREACHABLE");
    }

    private static void serializeToSRT(List<Subtitle> subtitles, string path) {
        using FileStream file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.UTF8);
        
        int counter = 0;
        
        foreach (var sub in subtitles) {
            counter++;
            file.Write(Encoding.ASCII.GetBytes(counter + "\n"));
            string timestamps = sub.start.toSrt() + " --> " + sub.end.toSrt() + "\n";
            file.Write(Encoding.ASCII.GetBytes(timestamps));
            file.Write(Encoding.UTF8.GetBytes(sub.content));
            // content already contains a new line
            file.Write("\n"u8.ToArray());
        }
        file.Flush();
    }

    public static void serializeToVTT(List<Subtitle> subtitles, string path) {
        using FileStream file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.UTF8);

        file.Write("WEBVTT\n\n"u8.ToArray());

        foreach (var sub in subtitles) {
            string timestamps = sub.start.toVtt() + " --> " + sub.end.toVtt() + "\n";
            file.Write(Encoding.ASCII.GetBytes(timestamps));
            file.Write(Encoding.UTF8.GetBytes(sub.content));
            file.Write("\n"u8.ToArray());
        }
    }
    
    private static (Timecode?, Timecode?, Exception?) parseTimestamps(string timestamps, bool srt) {
        if (timestamps.Length < 23) {
            // minimal VTT timestamps length "01:11.111 --> 01:22.222".Length
            return (null, null, new SubtitleException("The timestamps are too short"));
        }

        int separator = timestamps.IndexOf(" --> ", StringComparison.Ordinal);
        if (separator == -1) {
            return (null, null, new SubtitleException("No timecode separator found"));
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

public class Subtitle {
    public Timecode start;
    public Timecode end;
    public string content;
    
    public Subtitle(Timecode start, Timecode end, string content) {
        this.start = start;
        this.end = end;
        this.content = content;
    }

    public void shiftForwardBy(int ms) { 
        start.shiftForwardBy(ms);
        end.shiftForwardBy(ms);
    }
    public void shiftBackwardBy(int ms) {
        Console.WriteLine("Unimplemented");
    }
}

public class Timecode {
    public int hours, minutes, seconds, milliseconds;

    public Timecode(int hr, int min, int s, int ms) {
        hours = hr;
        minutes = min;
        seconds = s;
        milliseconds = ms;
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