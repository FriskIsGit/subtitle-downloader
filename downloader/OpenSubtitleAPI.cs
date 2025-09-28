using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public class OpenSubtitleAPI {
    private const string SUBTITLE_SUGGEST = "https://www.opensubtitles.org/libs/suggest.php?format=json3";
    private const string SUBTITLE_SEARCH = "https://www.opensubtitles.org/en/search2";

    private readonly ExtendedHttpClient client = new(new HttpClientHandler { AllowAutoRedirect = true }) {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public List<Production> getSuggestedMovies(string title) {
        var productions = new List<Production>();
        string url = $"{SUBTITLE_SUGGEST}&MovieName={title}";
        var response = client.fetchJson(url);
        if (response.statusCode != HttpStatusCode.OK) {
            Console.WriteLine($"Status code: {response.statusCode}");
            return productions;
        }

        JsonNode? arrayNode;
        try {
            arrayNode = JsonNode.Parse(response.content);
        }
        catch (System.Text.Json.JsonException e) {
            Console.WriteLine($"[Unusual exception occurred] Suggestions are not a valid JSON: {e.Message}");
            return productions;
        }
        
        if (arrayNode is null) {
            return productions;
        }

        foreach (var element in arrayNode.AsArray()) {
            if (element is null) {
                continue;
            }

            var prod = Production.parse(element);
            productions.Add(prod);
        }
        
        return productions;
    }
    
    public List<Production> searchProductions(Arguments args) {
        StringBuilder url = new StringBuilder($"{SUBTITLE_SEARCH}/MovieName-{args.title}");
        url.Append(args.isMovie ? "/SearchOnlyMovies=on" : "/SearchOnlyTVSeries=on");
        if (args.year != 0) {
            url.Append($"/MovieYear-{args.year}");
        }
        var simpleResponse = client.fetchHtml(url.ToString());
        if (simpleResponse.isError()) {
            Utils.FailExit("Failed to fetch seasons. Code:" + simpleResponse.statusCode);
        }
        
        // it's possible to be redirected to the target website after search if there's only 1 result
        string? location = simpleResponse.lastLocation;
        if (location != null && location.Contains("/imdbid-")) {
            Console.WriteLine("Detected direct redirect!");
        }
        return SubtitleScraper.scrapeSearchResults(simpleResponse.content, args.isMovie);
    }

    public SimpleResponse getHtml(string url) {
        return client.get(url);
    }

    public async Task<SimpleDownloadResponse> downloadSubtitle(string resourceUrl, string outputDir) {
        HttpResponseMessage response = await client.GetAsync(resourceUrl);
        string filename = "unknown.zip";
        ContentDispositionHeaderValue? contentDisposition = response.Content.Headers.ContentDisposition;
        if (contentDisposition != null && contentDisposition.FileName != null) {
            filename = Utils.sanitizeFileName(contentDisposition.FileName);
        }
        switch (response.StatusCode) {
            case HttpStatusCode.MovedPermanently:
                Console.WriteLine("Captcha? (301). Downgrading to HTTP");
                resourceUrl = downgradeUrl(resourceUrl);
                break;
            case HttpStatusCode.NotFound:
                Console.WriteLine("Received 404 - likely the pack has more than 50 subtitles inside.");
                return SimpleDownloadResponse.fail(HttpStatusCode.NotFound);
            case HttpStatusCode.TooManyRequests:
                Console.WriteLine("Too many requests (429)");
                Console.WriteLine(response.Content);
                return SimpleDownloadResponse.fail(HttpStatusCode.TooManyRequests);
        }

        var path = Path.Combine(outputDir, filename);
        await using var stream = await client.GetStreamAsync(resourceUrl);
        await using var fs = new FileStream(path, FileMode.Create);
        await stream.CopyToAsync(fs);
        return SimpleDownloadResponse.ok(path);
    }

    private static string downgradeUrl(string url) {
        return url.Replace("https", "http");
    }
}

// Retrieved as part of JSON response from suggest.php
public struct Production {
    public uint id;
    public uint year;
    public uint total;
    
    public string name;
    public string kind;
    public string rating;

    public static Production parse(JsonNode node) {
        var production = new Production();
        production.name = node["name"]?.ToString() ?? "";
        production.year = uint.Parse(node["year"]?.ToString() ?? "");
        production.total = uint.Parse(node["total"]?.ToString() ?? "");
        production.id = node["id"]?.GetValue<uint>() ?? 0;
        production.kind = node["kind"]?.ToString() ?? "";
        production.rating = node["rating"]?.ToString() ?? "";
        return production;
    }

    public override string ToString() {
        return $"{name} ({year}) id:{id} rating:{rating} kind:{kind}";
    }

    public string getPageUrl(string langId) {
        if (langId.Length > 3) {
            langId = langId[..3];
        }
        return $"https://www.opensubtitles.org/en/search/sublanguageid-{langId}/idmovie-{id}";
    }
    
    public static (string title, uint year) ParseTitleYear(string productionName) {
        int newLine = productionName.IndexOf('\n', StringComparison.InvariantCulture);
        if (newLine == -1) {
            return ("", 0);
        }
        string title = productionName[..newLine];

        int bracketOpen = productionName.LastIndexOf('(');
        if (bracketOpen != -1) {
            int close = productionName.LastIndexOf(')');
            string numericalYear = productionName[(bracketOpen+1)..close];
            if (uint.TryParse(numericalYear, out var year)) {
                return (title, year);
            }
        }

        return (title, 0);
    }
}