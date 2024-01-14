using System.Diagnostics;

Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

var pl = new PluginLists.PluginLists();
while (pl.IsScanning())
{
    Thread.Sleep(100);
    foreach (var url in pl.GetPluginUrls())
    {
        Console.WriteLine(url);
    }
}