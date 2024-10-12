# Subtitle downloader

### Usage
- `<production title> [options...]`
- `--from [file name] [options...]`

### Arguments
- `-s, -S, --season` - season number (required for tv series)
- `-e, -E, --episode` - episode number (required for tv series)
- `--lang` - specifies subtitle language (kinda required)
- `-y, --year` - the year a movie or a tv series was released (optional)
- `-ls, --list` - pretty prints episodes (for TV series)
- `--from` - extracts arguments from file name
- `--out` - directory where files are to be downloaded

### Examples

Fetching movie subtitles

```bash
`./subtitles "Pulp Fiction" --lang eng
```

```bash
`./subtitles Godfather -y1972 --lang spanish`
```

Fetching TV series subtitles
```bash
`./subtitles "The Gentlemen" -S1 -E3` --lang chinese
```

### Building the project
Run in root directory
```bash
dotnet run
```

