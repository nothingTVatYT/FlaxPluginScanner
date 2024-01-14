using System.Diagnostics;
using System.Text.Json;

namespace PluginLists;

public class PluginLists
{
    private const string LocalListUrl = "pluginlist.json";
    private readonly HttpClient _httpClient = new();
    private readonly ISet<string> _plugins;
    private readonly HashSet<string> _lists;
    private int _tasksRunning;

    public PluginLists()
    {
        _plugins = new HashSet<string>();
        _lists = new HashSet<string>();
        _tasksRunning = 0;
        Init();
    }

    private void InitListTemplate()
    {
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
            lock (_plugins) _plugins.Add(url);
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
        Interlocked.Increment(ref _tasksRunning);
        lock (_lists) _lists.Add(url);
        var stream = await _httpClient.GetStreamAsync(url);
        var list = JsonSerializer.Deserialize<PluginLinkList>(stream);
        if (list == null)
        {
            Debug.WriteLine($"Not a plugin link list: {url}");
            Interlocked.Decrement(ref _tasksRunning);
            return;
        }

        // scan plugin URLs
        foreach (var pluginUrl in list.Plugins)
        {
            Debug.WriteLine($"Adding {pluginUrl}");
            lock (_plugins) _plugins.Add(pluginUrl);
        }

        // scan list links
        foreach (var listUrl in list.Lists)
        {
            Debug.WriteLine($"Download and scan plugin list: {listUrl}");
            AddList(listUrl);
        }

        Interlocked.Decrement(ref _tasksRunning);
    }

    /// <summary>
    /// Check if there are still scanning tasks running.
    /// </summary>
    /// <returns>true if at least one tasks is still active</returns>
    public bool IsScanning()
    {
        return _tasksRunning > 0;
    }

    /// <summary>
    /// Get a list of plugins, this list is filled asynchronously, so it may not be complete
    /// </summary>
    /// <returns>a list of URLs to plugins</returns>
    public List<string> GetPluginUrls()
    {
        List<string> result;
        lock (_plugins) result = _plugins.ToList();
        return result;
    }
}