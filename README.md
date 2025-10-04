# Subtitle downloader

## Usage
- `<production title> [options...]`
- `--from [file name] [options...]`

## Arguments

- `--lang` - specifies subtitle language (recommended
- `-y` `--year` - the year a movie or a tv series was released (optional)
- `--filter` - filter subtitles by extension
- `--contains` `--has` - filter subtitles by text contained in filename
- `--auto-select` `-auto` - automatically selects subtitle to download (use in scripts)
- `--from` - read a subtitle file from memory (for editing with **--shift**, **--to**)
- `--to` `--convert-to` - converts to the specified subtitle format
- `--shift` - shifts all timestamps by an offset (ms)
- `--extract` - extracts arguments from a file name
- `--provider` - select provider
- `--dest` `--out` `-o` - directory where downloaded files are to be placed
- `--clean` `--cleanup` - remove empty cues

- `-s` `-S` `--season` - season number (required for tv series)
- `-e` `-E` `--episode` - episode number (required for tv series)
- `-p` `--pack` - download a season as subtitle pack (not more than 30 episodes)
- `-ls` `--list` - pretty prints episodes (for TV series)
<details>
<summary>Dev flags</summary>

<code>--gen</code> - the number of cues to generate 

</details>

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
./subtitles --from "Starship Troopers (1997).vtt" --to srt --shift +4500
```

## Cloning & building the project
Clone with submodules
```bash
git clone --recurse-submodules https://github.com/FriskIsGit/subtitle-downloader
```
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
| LRC      | ✔️      | ️              |
| WHISPER  | ✔️      | ️              |


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

