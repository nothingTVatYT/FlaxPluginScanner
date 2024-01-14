using System.Text.Json.Serialization;

namespace PluginLists;

public class PluginLinkList
{
    [JsonInclude]
    public List<string> Plugins = new();
    [JsonInclude]
    public List<string> Lists = new();
}