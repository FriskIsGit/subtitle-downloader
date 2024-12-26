# Subtitle downloader

## Usage
- `<production title> [options...]`
- `--from [file name] [options...]`

## Arguments
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

## Examples

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

## Building the project
Run in project's root directory
```bash
dotnet run
```

## Supported conversions

| Formats  | Parsing | Serializing to |
|----------|---------|----------------|
| SRT      | ✔️      | ️✔             |
| VTT      | ✔       | ✔              |
| MPL, SUB | ✔️      | ️              |
| MPL2     | ✔️      | ️              |
| SSA      | ✔️      | ️              |
| TMP      | ✔️      | ️              |


## Fixing bad encoding
Git comes with many preinstalled binaries among which is `iconv` <br>
On Windows it can be found at `Git/usr/bin/iconv.exe` where Git is git's root installation directory. <br>
On Linux it's most likely preinstalled.

```bash
iconv -f ISO-8859-1 -t UTF-8 subs.srt > subs_utf8.srt
```
```bash
iconv -f WINDOWS-1250 -t UTF-8 subs.srt > subs_utf8.srt
```

