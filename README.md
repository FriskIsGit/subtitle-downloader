# Subtitle downloader

### Usage
- `<production title> [options...]`
- `--from [file name] [options...]`

### Arguments
- `-s` `-S` `--season` - season number (required for tv series)
- `-e` `-E` `--episode` - episode number (required for tv series)
- `--lang` - specifies subtitle language (kinda required)
- `-y` `--year` - the year a movie or a tv series was released (optional)
- `-ls` `--list` - pretty prints episodes (for TV series)
- `--filter` - filter subtitles by extension
- `--skip-select` - automatically select subtitle to download
- `--subtitle` - parse a subtitle file to edit (with --shift, --to)
- `--shift` - shifts all timestamps by an offset (ms)
- `--to` `--convert-to` - converts to the specified subtitle format
- `--from` - extracts arguments from file name
- `--dest` `--out` - directory where files are to be placed

### Examples

Fetching movie subtitles

```bash
./subtitles "Pulp Fiction" --lang eng
```

```bash
./subtitles Godfather -y1972 --lang spanish
```

Fetching TV series subtitles
```bash
./subtitles "The Gentlemen" -S1 -E3 --lang chinese
```

Converting a file from disk
```bash
./subtitles --subtitle "Starship Troopers (1997).vtt" --to srt --shift +4500
```

### Building the project
Run in project's root directory
```bash
dotnet run
```

### Supported conversions
_Parsing_: SRT, VTT, MPL, MPL2, SUB, TXT, SSA, ASS

_Serializing to_: SRT, VTT
