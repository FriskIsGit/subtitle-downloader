using System.Net;
using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public class API {
    private const string SUBTITLE_SUGGEST = "https://www.opensubtitles.org/libs/suggest.php?format=json3";
    private const string SEARCH = "https://www.opensubtitles.org/en/search/sublanguageid-{}/idmovie-{}";

    private readonly HttpClient client = new() {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public List<Production> getSuggestedMovies(string title, string language) {
        var productions = new List<Production>();
        language = toSubLanguageID(language);
        string url = $"{SUBTITLE_SUGGEST}&MovieName={title}&SubLanguageID={language}";
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

    public static string toSubLanguageID(string language) {
        if (language.Length > 3) {
            return language[..3];
        }

        return language;
    }
    
    public SimpleResponse fetchJson(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get,
        };
        
        getRequest.Headers.Accept.ParseAdd("application/json");
        var response = client.Send(getRequest);
        HttpStatusCode code = response.StatusCode;
        string content = response.Content.ReadAsStringAsync().Result;
        return new SimpleResponse(code, content);
    }
    
    public async Task<DownloadInfo> downloadFile(string url, string dir) {
        HttpResponseMessage response = await client.GetAsync(url);
        if (response.RequestMessage?.RequestUri is null) {
            // Should never be here executed
            return DownloadInfo.Failed();
        }
        string fileName = extractName(response.RequestMessage.RequestUri);
        long contentLength = response.Content.Headers.ContentLength ?? 0;
        await using var stream = await client.GetStreamAsync(response.RequestMessage.RequestUri);
        await using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.OpenOrCreate);
        await stream.CopyToAsync(fs);
        return DownloadInfo.Ok(fileName, contentLength);
    }
    private static string extractName(Uri requestUri) {
        int lastSlash = requestUri.LocalPath.LastIndexOf('/');
        if (lastSlash == -1) {
            return requestUri.LocalPath;
        }
        return requestUri.LocalPath[(lastSlash+1)..];
    }
}

public struct DownloadInfo {
    public string fileName { get; set; }
    public long contentLength { get; set; }

    private DownloadInfo(string fileName, long contentLength) {
        this.fileName = fileName;
        this.contentLength = contentLength;
    }

    public static DownloadInfo Ok(string fileName, long contentLength) {
        return new DownloadInfo(fileName, contentLength);
    }
    public static DownloadInfo Failed() {
        return new DownloadInfo("", 0);
    }
}

public struct SimpleResponse {
    public readonly HttpStatusCode statusCode;
    public readonly string content;
    public SimpleResponse(HttpStatusCode code, string content) {
        statusCode = code;
        this.content = content;
    }
}