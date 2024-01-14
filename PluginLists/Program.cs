using System.Diagnostics;

Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

var pl = new PluginLists.PluginLists();
while (pl.IsScanning() || pl.IsGettingDetails())
{
    Thread.Sleep(100);
}

foreach (var url in pl.GetPluginUrls())
{
    Console.WriteLine(url);
    var desc = pl.GetPluginDescription(url);
    if (desc != null)
        Console.WriteLine($"{desc.Name} {desc.Version} {desc.Description}");
}

