using System;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Platform;

namespace Alycia.Client.Avalonia;

public static class Brand
{
    public static StreamGeometry Mark { get; } = LoadMark();

    private static StreamGeometry LoadMark()
    {
        var uri = new Uri("avares://Alycia.Client.Avalonia/Assets/Brand/alycia-boreale.svg");
        using var stream = AssetLoader.Open(uri);
        var path = XDocument.Load(stream).Descendants(XName.Get("path", "http://www.w3.org/2000/svg")).Single();
        return StreamGeometry.Parse((string)path.Attribute("d")!);
    }
}
