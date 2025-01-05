using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public class OpenSubtitleAPI {
    private const string SUBTITLE_SUGGEST = "https://www.opensubtitles.org/libs/suggest.php?format=json3";
    private const string SUBTITLE_SEARCH = "https://www.opensubtitles.org/en/search2";
    private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130";

    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = true }) {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public List<Production> getSuggestedMovies(string title) {
        var productions = new List<Production>();
        string url = $"{SUBTITLE_SUGGEST}&MovieName={title}";
        var response = fetchJson(url);
        if (response.statusCode != HttpStatusCode.OK) {
            Console.WriteLine($"Status code: {response.statusCode}");
            return productions;
        }

        JsonNode? arrayNode = JsonNode.Parse(response.content);
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
    
    // OpenSubtitles API is quirky
    public List<Production> searchProductions(Arguments args) {
        StringBuilder url = new StringBuilder($"{SUBTITLE_SEARCH}/MovieName-{args.title}");
        url.Append(args.isMovie ? "/SearchOnlyMovies=on" : "/SearchOnlyTVSeries=on");
        if (args.year != 0) {
            url.Append($"/MovieYear-{args.year}");
        }
        var simpleResponse = fetchHtml(url.ToString());
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

    public SimpleResponse fetchJson(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get,
        };
        
        getRequest.Headers.UserAgent.ParseAdd(USER_AGENT);
        getRequest.Headers.Accept.ParseAdd("application/json");
        var response = client.Send(getRequest);
        HttpStatusCode code = response.StatusCode;
        string content = response.Content.ReadAsStringAsync().Result;
        return new SimpleResponse(code, content);
    }
    
    public SimpleResponse fetchHtml(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get,
        };
        getRequest.Headers.UserAgent.ParseAdd(USER_AGENT);
        getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        getRequest.Headers.AcceptLanguage.ParseAdd("en-US;q=0.7");
        getRequest.Headers.Add("Set-GPC", "1");
        HttpResponseMessage? response;
        try {
            response = client.Send(getRequest);
        }
        catch (Exception exception) {
            Utils.FailExit(exception.Message);
            return new SimpleResponse(HttpStatusCode.ServiceUnavailable, "", url);
        }
         
        string content = response.Content.ReadAsStringAsync().Result;
        if (response.StatusCode == HttpStatusCode.OK) {
            return new SimpleResponse(response.StatusCode, content, url);
        }

        var statusCode = response.StatusCode;
        if (response.Headers.Location != null && 
            (statusCode == HttpStatusCode.MovedPermanently || statusCode == HttpStatusCode.Redirect)) {
            return fetchHtml(response.Headers.Location.ToString());
        }
        
        Console.WriteLine("Response code: " + statusCode);
        return new SimpleResponse(response.StatusCode, content, url);
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

public readonly struct SimpleDownloadResponse {
    public readonly string filename;
    public readonly HttpStatusCode statusCode;

    public SimpleDownloadResponse(string filename, HttpStatusCode code) {
        this.filename = filename;
        statusCode = code;
    }
    
    public static SimpleDownloadResponse fail(HttpStatusCode code) {
        return new SimpleDownloadResponse("", code);
    }
    
    public static SimpleDownloadResponse ok(string filename) {
        return new SimpleDownloadResponse(filename, HttpStatusCode.OK);
    }

    public bool isError() {
        return (int)statusCode >= 400;
    }
}

public readonly struct SimpleResponse {
    public readonly HttpStatusCode statusCode;
    public readonly string content;
    public readonly string? lastLocation = null;
    public SimpleResponse(HttpStatusCode code, string content) {
        statusCode = code;
        this.content = content;
    }
    public SimpleResponse(HttpStatusCode code, string content, string lastLocation) {
        statusCode = code;
        this.content = content;
        this.lastLocation = lastLocation;
    }

    public bool isError() {
        return (int)statusCode >= 400;
    }
}