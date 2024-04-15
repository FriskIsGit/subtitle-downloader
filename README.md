﻿# Subtitle downloader

### Usage
- `<production title> [options...]`
### Arguments
- `-s, -S, --season` - season number (required for tv series)
- `-e, -E, --episode` - episode number (required for tv series)
- `--lang` - specifies subtitle language (kinda required)
- `-y, --year` - the year a movie or a tv series was released (optional)
- `--list` - pretty prints episodes if specified for TV series
- `--path` - extracts production information from file name

### Examples

Fetching movie subtitles
```bash
`./subtitles Godfather -y1972 --lang spanish`
```

Fetching TV show subtitles
```bash
`./subtitles "The Gentlemen" -S1 -E3` --lang chinese
```

