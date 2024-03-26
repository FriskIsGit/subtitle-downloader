using System.Text.Json.Nodes;

namespace subtitle_downloader.downloader; 

public struct Production {
    public uint id;
    public uint year;
    public uint total;
    
    public string name;
    public string pic;
    public string kind;
    public string rating;

    public static Production parse(JsonNode node) {
        var production = new Production();
        production.name = node["name"]?.ToString() ?? "";
        production.year = uint.Parse(node["year"]?.ToString() ?? "");
        production.total = uint.Parse(node["total"]?.ToString() ?? "");
        production.id = node["id"]?.GetValue<uint>() ?? 0;
        production.pic = node["pic"]?.ToString() ?? "";
        production.kind = node["kind"]?.ToString() ?? "";
        production.rating = node["rating"]?.ToString() ?? "";
        return production;
    }

    public override string ToString() {
        return $"{name} ({year}) id:{id} rating:{rating} kind:{kind}";
    }
}