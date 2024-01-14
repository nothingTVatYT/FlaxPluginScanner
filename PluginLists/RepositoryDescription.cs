// ReSharper disable InconsistentNaming
// ReSharper disable UnassignedField.Global

using System.Text.Json.Serialization;

namespace PluginLists;

public class RepositoryDescription
{
    [JsonInclude]
    public string? name;
    [JsonInclude]
    public string? full_name;
    [JsonInclude]
    public string? description;
    [JsonInclude]
    public bool fork;
    [JsonInclude]
    public string? git_url;
    [JsonInclude]
    public string? ssh_url;
    [JsonInclude]
    public string? clone_url;
    [JsonInclude]
    public string? default_branch;
}

public class TreeResult
{
    [JsonInclude]
    public string? path;
    [JsonInclude]
    public string? type;
    [JsonInclude]
    public string? sha;
    [JsonInclude]
    public string? url;
}

public class TreesResult
{
    [JsonInclude]
    public string? sha;
    [JsonInclude]
    public string? url;
    [JsonInclude]
    public List<TreeResult>? tree;
}

public class BlobResult
{
    [JsonInclude]
    public string? content;
    [JsonInclude]
    public string? encoding;
}