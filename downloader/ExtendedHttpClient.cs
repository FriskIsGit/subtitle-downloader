using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace subtitle_downloader.downloader;

public class ExtendedHttpClient : HttpClient {
    private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130";

    public ExtendedHttpClient(HttpMessageHandler handler, bool disposeHandler = true) : base(handler, disposeHandler) {
        
    }
    
    public SimpleResponse get(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get
        };
        getRequest.Headers.UserAgent.ParseAdd(USER_AGENT);
        
        var response = Send(getRequest);
        HttpStatusCode code = response.StatusCode;
        string content = response.Content.ReadAsStringAsync().Result;
        return new SimpleResponse(code, content);
    } 
    
    public SimpleResponse getJson(string url) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get
        };
        
        getRequest.Headers.UserAgent.ParseAdd(USER_AGENT);
        getRequest.Headers.Accept.ParseAdd("application/json");
        var response = Send(getRequest);
        HttpStatusCode code = response.StatusCode;
        string content = response.Content.ReadAsStringAsync().Result;
        return new SimpleResponse(code, content);
    }
    
    public SimpleResponse post(string url, string body) {
        var getRequest = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = HttpMethod.Post,
            Content = new StringContent(body)
        };
        
        getRequest.Headers.UserAgent.ParseAdd(USER_AGENT);
        getRequest.Headers.Accept.ParseAdd("application/json");
        var response = Send(getRequest);
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
            response = Send(getRequest);
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

public class Params {
    private readonly StringBuilder str = new ();
    private bool first = true;

    private Params() {
    }

    public static Params New() => new();

    public Params AddPair(string key, string value) {
        if (first) {
            first = false;
            str.Append('?');
        }
        else {
            str.Append('&');
        }

        str.Append(Encode(key)).Append('=').Append(Encode(value));
        return this;
    }

    public void AddPair<T>(string key, T val) => AddPair(key, val?.ToString() ?? "null");

    public string Get() => str.ToString();

    public void Clear() {
        first = true;
        str.Clear();
    }

    private static string Encode(string toEncode) {
        return WebUtility.UrlEncode(toEncode);
    }

    public override string ToString() => str.ToString();
}