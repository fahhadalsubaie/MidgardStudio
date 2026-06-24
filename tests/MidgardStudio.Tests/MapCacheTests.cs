using MidgardStudio.Core.MapCache;
using Xunit;

namespace MidgardStudio.Tests;

public class MapCacheTests
{
    [Fact]
    public void WriteRead_RoundTripsMapsAndDimensions()
    {
        var file = new MapCacheFile();
        file.Maps.Add(new MapCacheEntry { Name = "prontera", Xs = 200, Ys = 200, CompressedCells = MapCacheFile.CompressZlib(new byte[200 * 200]) });
        file.Maps.Add(new MapCacheEntry { Name = "custom_map", Xs = 50, Ys = 60, CompressedCells = MapCacheFile.CompressZlib(new byte[50 * 60]) });

        var read = MapCacheFile.Read(file.Write());

        Assert.Equal(2, read.Maps.Count);
        Assert.Equal("prontera", read.Maps[0].Name);
        Assert.Equal(200, read.Maps[0].Xs);
        Assert.Equal("custom_map", read.Maps[1].Name);
        Assert.Equal(60, read.Maps[1].Ys);
        Assert.Equal(file.Maps[0].CompressedCells, read.Maps[0].CompressedCells); // compressed bytes preserved
    }

    [Fact]
    public void Zlib_RoundTrips()
    {
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 7);
        Assert.Equal(data, MapCacheFile.DecompressZlib(MapCacheFile.CompressZlib(data)));
    }

    [Fact]
    public void FromGat_ParsesDimensionsAndCellTypes()
    {
        int w = 2, h = 2;
        var gat = new byte[14 + w * h * 20];
        gat[0] = (byte)'G'; gat[1] = (byte)'R'; gat[2] = (byte)'A'; gat[3] = (byte)'T';
        gat[4] = 1; gat[5] = 2;
        System.BitConverter.GetBytes(w).CopyTo(gat, 6);
        System.BitConverter.GetBytes(h).CopyTo(gat, 10);
        for (int i = 0; i < w * h; i++) gat[14 + i * 20 + 16] = (byte)i; // cell types 0,1,2,3

        var entry = MapCacheFile.FromGat("TestMap", gat);

        Assert.Equal("testmap", entry.Name);
        Assert.Equal(2, entry.Xs);
        Assert.Equal(2, entry.Ys);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, MapCacheFile.DecompressZlib(entry.CompressedCells));
    }
}
