using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public class SubDlAPI {
    public const string API_KEY = "";
    private const string API_ENDPOINT = "https://api.subdl.com/api/v1/subtitles";
    public const string DOWNLOAD_ENDPOINT = "https://dl.subdl.com";
    public const string DOMAIN = "https://subdl.com";

    private readonly ExtendedHttpClient client = new(new HttpClientHandler { AllowAutoRedirect = true }) {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public SimpleResponse sendQuery(Query query) {
        return client.getJson(API_ENDPOINT + query.toPathParams());
    }
    
    public async Task<SimpleDownloadResponse> downloadSubtitle(string resourceUrl, string outputDir) {
        // This internally checks if directory exists anyway
        Directory.CreateDirectory(outputDir);
        HttpResponseMessage response = await client.GetAsync(resourceUrl);
        string filename = Path.GetFileName(resourceUrl);
        if (!response.IsSuccessStatusCode) {
            Console.WriteLine("Received status code: " + response.StatusCode);
            Console.WriteLine(response.Content);
            return SimpleDownloadResponse.fail(response.StatusCode);
        }

        var path = Path.Combine(outputDir, filename);
        await using var stream = await client.GetStreamAsync(resourceUrl);
        await using var fs = new FileStream(path, FileMode.Create);
        await stream.CopyToAsync(fs);
        return SimpleDownloadResponse.ok(path);
    }
}

public class Query {
    private readonly string apiKey;
    private string filmName;
    private string type; // movie/tv
    private string languages;
    private string imdbId, tmbdId;
    private string seasonNumber, episodeNumber;
    private string year;
    private bool fullSeason, comment;

    public Query(string apiKey) {
        this.apiKey = apiKey;
    }

    public void applyArguments(Arguments args) {
        filmName = args.title;
        if (args.year != 0) {
            year = args.year.ToString();
        }
        type = args.isMovie ? "movie" : "tv";
        if (!args.isMovie) {
            seasonNumber = args.season.ToString();
            if (args.episodes.Count == 1) {
                // Otherwise we don't specify it
                episodeNumber = args.episodes[0].ToString();
            }
        }

        // If fullSeason is applied no results are generated
        /*if (args.downloadPack) {
            fullSeason = true;
        }*/

        languages = args.language.Length > 2 ? args.language[..2] : args.language;
        languages = languages.ToUpper();
    }

    public string toPathParams() {
        Params pathParams = Params.New();
        pathParams.AddPair("api_key", apiKey);
        if (!string.IsNullOrEmpty(filmName)) pathParams.AddPair("film_name", filmName);
        if (!string.IsNullOrEmpty(year)) pathParams.AddPair("year", year);
        if (!string.IsNullOrEmpty(type)) pathParams.AddPair("type", type);
        if (!string.IsNullOrEmpty(languages)) pathParams.AddPair("languages", languages);
        if (!string.IsNullOrEmpty(seasonNumber)) pathParams.AddPair("season_number", seasonNumber);
        if (!string.IsNullOrEmpty(episodeNumber)) pathParams.AddPair("episode_number", episodeNumber);
        if (!string.IsNullOrEmpty(imdbId)) pathParams.AddPair("imdb_id ", imdbId);
        if (!string.IsNullOrEmpty(tmbdId)) pathParams.AddPair("tmdb_id ", tmbdId);
        
        if (fullSeason) pathParams.AddPair("full_season", 1);
        if (comment) pathParams.AddPair("comment", 1);

        return pathParams.Get();
    }

    // SubDL responds to JSON body with UnprocessableEntity undefined is not an object (evaluating 'error2.schema')
    public string toRequestBody() {
        var queryObj = new JsonObject {
            ["api_key"] = apiKey,
            ["film_name"] = filmName,
            ["type"] = type,
            ["languages"] = languages
        };

        var root = new JsonObject {
            ["query"] = queryObj
        };

        return root.ToJsonString();
    }
}

public class SubtitleResponse {
    public bool status;
    public List<MovieResult> movieResults;
    public List<SubtitleResult> subtitles;
    
    public static SubtitleResponse fromJson(JsonNode json) {
        var response = new SubtitleResponse();
        response.status = json["status"]?.GetValue<bool>() ?? false;
        
        var results = json["results"];
        if (results != null) {
            JsonArray resultArray = results.AsArray();
            response.movieResults = new List<MovieResult>(resultArray.Count);
            foreach (var jsonObj in resultArray) {
                if (jsonObj == null) {
                    continue;
                }

                var movieResult = MovieResult.fromJson(jsonObj);
                response.movieResults.Add(movieResult);
            }
        }
        else {
            response.movieResults = new List<MovieResult>();
        }
        
        var subtitles = json["subtitles"];
        if (subtitles != null) {
            JsonArray subtitlesArray = subtitles.AsArray();
            response.subtitles = new List<SubtitleResult>(subtitlesArray.Count);
            foreach (var jsonObj in subtitlesArray) {
                if (jsonObj == null) {
                    continue;
                }

                var subtitle = SubtitleResult.fromJson(jsonObj);
                response.subtitles.Add(subtitle);
            }
        }
        else {
            response.subtitles = new List<SubtitleResult>();
        }

        
        return response;
    }
}

public class MovieResult {
    public long sdId;
    public string name, type, imdbId, tmdbId, slug;
    // For some reason it's null
    public long? year;

    public static MovieResult fromJson(JsonNode json) {
        MovieResult result = new MovieResult();
        var maybeYear = json["year"];
        if (maybeYear != null) {
            result.year = uint.Parse(maybeYear.ToString());
        }
        result.sdId = json["sd_id"]?.GetValue<long>() ?? 0;
        result.name = json["name"]?.ToString() ?? "";
        result.type = json["type"]?.ToString() ?? "";
        result.imdbId = json["imdb_id"]?.ToString() ?? "";
        result.tmdbId = json["tmdb_id"]?.ToString() ?? "";
        result.slug = json["slug"]?.ToString() ?? "";
        return result;
    }
}

public class SubtitleResult {
    public string releaseName, name, lang, author, language;
    private string url, subtitlePage;
    public int season;
    public Metadata metadata;
    
    public static SubtitleResult fromJson(JsonNode json) {
        SubtitleResult result = new SubtitleResult();
        result.season = json["season"]?.GetValue<int>() ?? 0;
        
        result.releaseName = json["release_name"]?.ToString() ?? "";
        result.name = json["name"]?.ToString() ?? "";
        
        result.subtitlePage = json["subtitlePage"]?.ToString() ?? "";
        result.lang = json["lang"]?.ToString() ?? "";
        result.author = json["author"]?.ToString() ?? "";
        result.url = json["url"]?.ToString() ?? "";
        result.language = json["language"]?.ToString() ?? "";
        return result;
    }

    public string getUrl() {
        if (url.StartsWith("https://")) {
            return url;
        }

        url = SubDlAPI.DOWNLOAD_ENDPOINT + url;
        return url;
    }
    
    public string getSubtitlePage() {
        if (subtitlePage.StartsWith("https://")) {
            return subtitlePage;
        }

        subtitlePage = SubDlAPI.DOMAIN + subtitlePage;
        return subtitlePage;
    }
}