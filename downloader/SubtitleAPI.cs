using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public class SubtitleAPI {
    private const string SUBTITLE_SUGGEST = "https://www.opensubtitles.org/libs/suggest.php?format=json3";

    private readonly HttpClient client = new() {
        Timeout = Timeout.InfiniteTimeSpan,
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
        
        getRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 Gecko/20100101");
        getRequest.Headers.Accept.ParseAdd("application/json");
        var response = client.Send(getRequest);
        HttpStatusCode code = response.StatusCode;
        string content = response.Content.ReadAsStringAsync().Result;
        return new SimpleResponse(code, content);
    }
    
    public string fetchHtml(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get,
        };
        getRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 Gecko/20100101");
        getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        getRequest.Headers.AcceptLanguage.ParseAdd("en-US;q=0.7");
        getRequest.Headers.Add("Set-GPC", "1");
        var response = client.Send(getRequest);
        if (response.StatusCode == HttpStatusCode.OK) {
            return response.Content.ReadAsStringAsync().Result;
        }

        if (response.StatusCode == HttpStatusCode.MovedPermanently && response.Headers.Location != null) {
            return fetchHtml(response.Headers.Location.ToString());
        }
            
        Console.WriteLine("Response Code: " + response.StatusCode);
        return response.Content.ReadAsStringAsync().Result;
    }
    
    public async Task<bool> downloadSubtitle(SubtitleRow subtitle, string fileName) {
        await using var stream = await client.GetStreamAsync(subtitle.getFullURL());
        await using var fs = new FileStream(fileName + ".zip", FileMode.Create);
        await stream.CopyToAsync(fs);
        return true;
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