using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PluginLists;

public partial class PluginLists
{
    [GeneratedRegex(@",\s*}")]
    private static partial Regex CommaBeforeClosingBracket();
    [GeneratedRegex(@"(\w+)\s*=\s*(null|false|true|\x22[^\x22]*\x22)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex VariableAssignmentRegex();
    [GeneratedRegex(@"Version = new Version\(\)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex NewEmptyVersionRegex();
    [GeneratedRegex(@"Version = new Version\((\d+),\s*(\d+),?\s*\d*\)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex NewVersionRegex();
    [GeneratedRegex(@"(https?://(\w+\.)?github.com)/([^/]+)/([^/]+)/?", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex GithubRepo();
    private string LocalListUrl;
    private readonly HttpClient _httpClient = new();
    private readonly ISet<string> _pluginUrls;
    private readonly HashSet<string> _lists;
    private readonly Dictionary<string, PluginDescription> _plugins;
    private readonly Dictionary<string, FlaxProject> _projects;
    private int _scannerTasksRunning;
    private int _getDetailsTasksRunning;
    private JsonSerializerOptions _jsonOptions = new(JsonSerializerOptions.Default);

    public PluginLists()
    {
        _jsonOptions.WriteIndented = true;
        _jsonOptions.IncludeFields = true;
        _pluginUrls = new HashSet<string>();
        _lists = new HashSet<string>();
        _plugins = new Dictionary<string, PluginDescription>();
        _projects = new Dictionary<string, FlaxProject>();
        _scannerTasksRunning = 0;
        _getDetailsTasksRunning = 0;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "FlaxPluginList (github.com/nothingTVatYT/FlaxPluginScanner)");
        var tokenFile = Environment.GetEnvironmentVariable("GH_TOKEN");

        if (!string.IsNullOrEmpty(tokenFile))
        {
            var token = File.ReadAllText(tokenFile);
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.ReplaceLineEndings(""));
        }
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
	    var projectDir = Environment.GetEnvironmentVariable("PROJECT_DIR");
	    if (!string.IsNullOrEmpty(projectDir))
		    LocalListUrl = projectDir + "/pluginlist.json";
	    else
		    LocalListUrl = "pluginlist.json";
        Init();
    }

    private void InitListTemplate()
    {
        // just serialize some nonsense URLs so that people can see how the structure is meant to be
        var list = new PluginLinkList();
        list.Plugins.Add("http://somewhere.net/plugin-abc");
        list.Plugins.Add("http://somewhere.net/plugin-xyz");
        list.Lists.Add("http://someother.org/a-list-like-this.json");
        try
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var initData = JsonSerializer.Serialize(list, jsonOptions);
            File.WriteAllText(LocalListUrl, initData);
        } catch (Exception exception)
        {
            Debug.WriteLine("Could not init local list: " + exception);
        }
    }

    private void Init()
    {
        // TODO: use some magic directory guessing so that it works on all supported platforms
        PluginLinkList? localList = null;
        if (File.Exists(LocalListUrl))
        {
            using var localListFile = File.OpenRead(LocalListUrl);
            localList = JsonSerializer.Deserialize<PluginLinkList>(localListFile);
        }

        if (localList == null)
        {
            Debug.WriteLine("No local plugin list found.");
            InitListTemplate();
            return;
        }

        // scan plugin URLs
        foreach (var url in localList.Plugins)
        {
            Debug.WriteLine($"Adding {url}");
            bool added;
            lock (_pluginUrls) added = _pluginUrls.Add(url);
            if (added)
                FindPluginDescription(url);
        }

        foreach (var url in localList.Lists)
        {
            AddList(url);
        }
    }

    public async void AddList(string url)
    {
        lock (_lists)
        {
            if (_lists.Contains(url))
                return;
        }
        Interlocked.Increment(ref _scannerTasksRunning);
        lock (_lists) _lists.Add(url);
        var stream = await _httpClient.GetStreamAsync(url);
        var list = JsonSerializer.Deserialize<PluginLinkList>(stream);
        if (list == null)
        {
            Debug.WriteLine($"Not a plugin link list: {url}");
            Interlocked.Decrement(ref _scannerTasksRunning);
            return;
        }

        // scan plugin URLs
        foreach (var pluginUrl in list.Plugins)
        {
            Debug.WriteLine($"Adding {pluginUrl}");
            lock (_pluginUrls)
            {
                if (_pluginUrls.Add(pluginUrl))
                    FindPluginDescription(pluginUrl);
            }
        }

        // scan list links
        foreach (var listUrl in list.Lists)
        {
            Debug.WriteLine($"Download and scan plugin list: {listUrl}");
            AddList(listUrl);
        }

        Interlocked.Decrement(ref _scannerTasksRunning);
    }

    private async void FindPluginDescription(string url)
    {
        var match = GithubRepo().Match(url);
        if (!match.Success)
        {
            Debug.WriteLine($"Not a github URL: {url}");
            return;
        }

        var owner = match.Groups[3].Value;
        var repository = match.Groups[4].Value;
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repository))
        {
            Debug.WriteLine($"not a github repository URL: {url}");
            for (var i = 0; i < match.Groups.Count; i++)
                Debug.WriteLine($" group {i} {match.Groups[i].Value}");
            return;
        }
        Interlocked.Increment(ref _getDetailsTasksRunning);
        // get default branch
        var requestUri = $"https://api.github.com/repos/{owner}/{repository}";
        var repositoryDescription = await GetObjectFromUrl<RepositoryDescription>(requestUri);
        var branch = repositoryDescription?.default_branch;
        if (string.IsNullOrEmpty(branch))
        {
            Debug.WriteLine($"Could not get default branch for {url}.");
            Interlocked.Decrement(ref _getDetailsTasksRunning);
            return;
        }

        // get FlaxProject and Source folder hash
        var treesResult =
            await GetObjectFromUrl<TreesResult>($"https://api.github.com/repos/{owner}/{repository}/git/trees/{branch}");
        var sourceFolderUrl = "";
        FlaxProject? flaxProject = null;
        if (treesResult is { tree: not null })
        {
            foreach (var tree in treesResult.tree)
            {
                if (tree.path != null && tree.path.EndsWith(".flaxproj") && "blob".Equals(tree.type) && tree.url != null)
                {
                    flaxProject = await GetObjectFromBlob<FlaxProject>(tree.url);
                    if (flaxProject != null)
                        lock (_projects)
                            _projects[url] = flaxProject;
                }

                if ("Source".Equals(tree.path) && "tree".Equals(tree.type))
                {
                    sourceFolderUrl = tree.url;
                }
            }
        }
        else
        {
            Debug.WriteLine($"Could not get directory for {url}");
            Interlocked.Decrement(ref _getDetailsTasksRunning);
            return;
        }

        if (string.IsNullOrEmpty(sourceFolderUrl))
        {
            Debug.WriteLine($"Could not get Source directory for {url}");
            Interlocked.Decrement(ref _getDetailsTasksRunning);
            return;
        }

        if (flaxProject is not { Name: not null })
        {
            Debug.WriteLine(
                $"Could not get a flax project file for {url}");
            Interlocked.Decrement(ref _getDetailsTasksRunning);
            return;
        }

        var sourceTreesResult = await GetObjectFromUrl<TreesResult>(sourceFolderUrl);
        if (sourceTreesResult is not { tree: not null })
        {
            Debug.WriteLine(
                $"Could not get directory for Source folder for {url}");
            Interlocked.Decrement(ref _getDetailsTasksRunning);
            return;
        }

        foreach (var sourceFileTree in sourceTreesResult.tree)
        {
            if (flaxProject.Name.Equals(sourceFileTree.path))
            {
                // this is the Source folder for the GamePlugin
                var gameSourceTreesResult = await GetObjectFromUrl<TreesResult>(sourceFileTree.url);
                if (gameSourceTreesResult is { tree: not null })
                {
                    // looking for the GamePlugin constructor
                    // the name could be anything
                    foreach (var gameSourceFilesResult in gameSourceTreesResult.tree)
                    {
                        if (gameSourceFilesResult.path != null && gameSourceFilesResult.path.EndsWith(".cs"))
                        {
                            var text = await GetTextFile(gameSourceFilesResult.url);
                            if (text.Contains("new PluginDescription"))
                            {
                                var pluginDescription = ParsePluginDescription(text);
                                if (pluginDescription != null)
                                    lock (_plugins)
                                        _plugins[url] = pluginDescription;
                                break;
                            }
                        }
                    }
                }
            }
        }

        Interlocked.Decrement(ref _getDetailsTasksRunning);
    }

    private async Task<T?> GetObjectFromUrl<T>(string? url)
    {
        if (url == null)
            return default;
        try
        {
            var stream = await _httpClient.GetStreamAsync(url);
            var result = JsonSerializer.Deserialize<T>(stream);
            if (result == null)
                Debug.WriteLine($"Could not get a {typeof(T)} from {url}.");
            return result;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Could not access {url}: {e}");
            return default;
        }
    }

    private async Task<T?> GetObjectFromBlob<T>(string url)
    {
        var text = await GetTextFile(url);
        if (string.IsNullOrEmpty(text))
            return default;
        try
        {
            var result = JsonSerializer.Deserialize<T>(text);
            return result;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Could not deserialize to a {typeof(T)}: {e}");
            Debug.WriteLine($" The json is: {text}");
            return default;
        }
    }

    private async Task<string> GetTextFile(string? url)
    {
        if (url == null)
            return "";
        try
        {
            var stream = await _httpClient.GetStreamAsync(url);
            var blobResult = JsonSerializer.Deserialize<BlobResult>(stream);
            if (blobResult is not { content: not null }) return "";
            var text = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(blobResult.content));
            var idx = text.IndexOf('{');
            if (idx > 0)
                text = text[idx..];
            return text;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Could not get text file from {url}: {e}");
            return "";
        }
    }

    private PluginDescription? ParsePluginDescription(string text)
    {
        var start = text.IndexOf("new PluginDescription", StringComparison.Ordinal);
        var openBracket = text.IndexOf("{", start, StringComparison.Ordinal);
        var closeBracket = text.IndexOf("}", openBracket, StringComparison.Ordinal);
        var initCode = text.Substring(openBracket, closeBracket - openBracket + 1);
        // replace Version constructor with a string literal
        var replacedV = NewEmptyVersionRegex().Replace(initCode, "\"Version\": \"1.0\"");
        var replaced1 = NewVersionRegex().Replace(replacedV, "\"Version\": \"$1.$2\"");
        // replace variable assignments with json node syntax
        var replaced2 = VariableAssignmentRegex().Replace(replaced1, "\"$1\" : $2");
        // remove comma before closing bracket
        var replaced = CommaBeforeClosingBracket().Replace(replaced2, "}");
        try
        {
            return JsonSerializer.Deserialize<PluginDescription>(replaced, _jsonOptions);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Caught an exception in plugin description parser: {e}");
            Debug.WriteLine($"The json text is {replaced}");
        }

        return null;
    }

    /// <summary>
    /// Check if there are still scanning tasks running.
    /// </summary>
    /// <returns>true if at least one tasks is still active</returns>
    public bool IsScanning()
    {
        return _scannerTasksRunning > 0;
    }

    public bool IsGettingDetails()
    {
        return _getDetailsTasksRunning > 0;
    }

    /// <summary>
    /// Get a list of plugins, this list is filled asynchronously, so it may not be complete
    /// </summary>
    /// <returns>a list of URLs to plugins</returns>
    public List<string> GetPluginUrls()
    {
        List<string> result;
        lock (_pluginUrls) result = _pluginUrls.ToList();
        return result;
    }

    public PluginDescription? GetPluginDescription(string url)
    {
        PluginDescription? p;
        lock (_plugins) _plugins.TryGetValue(url, out p);
        return p;
    }

    public FlaxProject? GetFlaxProject(string url)
    {
        FlaxProject? p;
        lock (_projects) _projects.TryGetValue(url, out p);
        return p;
    }

    public void SavePluginsCache()
    {
        var cache = new PluginsCache();
        foreach (var pl in _plugins.Values)
            cache.Plugins.Add(pl);
        using var stream = File.OpenWrite("plugins.cache");
        JsonSerializer.Serialize(stream, cache, _jsonOptions);
    }
}
