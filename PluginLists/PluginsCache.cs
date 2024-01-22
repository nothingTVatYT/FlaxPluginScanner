using System.Text.Json.Serialization;

namespace PluginLists;

public class PluginsCache
{
    [JsonInclude]
    public List<PluginDescription> Plugins = new();
}