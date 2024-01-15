using System.Diagnostics;

Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

var pl = new PluginLists.PluginLists();

while (pl.IsScanning() || pl.IsGettingDetails())
{
    // here you can use the pl.GetPluginUrls() and pl.GetPluginDescription(string url)
    // to progressively fill a GUI instead
    Thread.Sleep(100);
}

foreach (var url in pl.GetPluginUrls())
{
    Console.WriteLine(url);
    var desc = pl.GetPluginDescription(url);
    if (desc != null)
        Console.WriteLine($"{desc.Name} {desc.Version} {desc.Description}");
}
