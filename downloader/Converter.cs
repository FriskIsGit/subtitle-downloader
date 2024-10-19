﻿using System.Text;

namespace subtitle_downloader.downloader; 

public class Converter {
    /*
     Each subtitle has four parts in the SRT file.
       1. A numeric counter indicating the number or position of the subtitle.
       2. Start and end time of the subtitle separated by –> characters
       3. Subtitle text in one or more lines.
       4. A blank line indicating the end of the subtitle.
     */

    public static (List<Subtitle>, Exception?) parseSRT(string path) {
        using FileStream file = File.OpenRead(path);
        using var reader = new StreamReader(file, Encoding.UTF8, true);
        
        var subtitles = new List<Subtitle>(2048);
        
        while (!reader.EndOfStream) {
            string? counter = reader.ReadLine();
            if (counter == null) {
                // expecting to quit here
                break;
            }
            string? timestamps = reader.ReadLine();
            if (timestamps == null) {
                return (subtitles, new SubtitleException("Expected timestamps [start --> end]"));
            }
            
            var (start, end, exception) = parseTimestamps(timestamps);
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
    
    public static Exception? serializeToVTT(List<Subtitle> subtitles, string path) {
        using FileStream file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.UTF8);

        file.Write("WEBVTT\n\n"u8.ToArray());

        foreach(var sub in subtitles) {

            string timestamps = sub.start.toVtt() + " --> " + sub.end.toVtt() + "\n";
            file.Write(Encoding.UTF8.GetBytes(timestamps));
            file.Write(Encoding.UTF8.GetBytes(sub.content));
            file.Write("\n\n"u8.ToArray());
        }
        return null;
    }
    
    private static (Timecode?, Timecode?, Exception?) parseTimestamps(string timestamps) {
        if (timestamps.Length < 29) {
            return (null, null, new SubtitleException("The timestamps are not full"));
        }

        if (!timestamps.Contains("-->", StringComparison.Ordinal)) {
            return (null, null, new SubtitleException("No timecode separator found"));
        }

        string startStamp = timestamps[..12];
        string endStamp = timestamps[17..29];

        var (start, exception) = fromSrtTimestamp(startStamp);
        if (exception != null) {
            return (null, null, exception);
        }
        (var end, exception) = fromSrtTimestamp(endStamp);
        if (exception != null) {
            return (null, null, exception);
        }

        return (start, end, null);
    }
    
    private static (Timecode?, SubtitleException?) fromSrtTimestamp(string timestamp) {
        string[] split = timestamp.Split(":");
        if (split.Length != 3) {
            return (null, new SubtitleException("Invalid timestamp, not a triple split"));
        }

        if (!int.TryParse(split[0], out int hours)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse hours"));
        }

        if (!int.TryParse(split[1], out int minutes)) {
            return (null, new SubtitleException("Invalid timestamp, failed to parse minutes"));
        }
        
        string[] subSplit = split[2].Split(",");
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
        return formatUnit(hours, 2) + ":" +
               formatUnit(minutes, 2) + ":" +
               formatUnit(seconds, 2) + "." +
               formatUnit(milliseconds, 3);
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