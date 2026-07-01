using System.Text;

namespace TelemetryPoc.Tests;

/// <summary>Hand-rolled minimal protobuf encoder for a single-layer, single-point Mapbox
/// Vector Tile, used to exercise MbTilesTileSource.DecodeTile's success path without
/// pulling in an MVT-writer dependency (Mapbox.VectorTile only ships a reader).</summary>
internal static class MvtBytes
{
    public static byte[] MinimalTile(string layerName = "water", string keyName = "class", string valueStr = "water")
        => WrapLengthDelimited(3, BuildLayer(layerName, keyName, valueStr));

    private static byte[] BuildLayer(string name, string keyName, string valueStr)
    {
        var body = new List<byte>();
        WriteVarintField(body, 15, 2); // version
        AppendLengthDelimited(body, 1, Encoding.UTF8.GetBytes(name));       // name
        AppendLengthDelimited(body, 3, Encoding.UTF8.GetBytes(keyName));    // keys[0]
        AppendLengthDelimited(body, 4, BuildValue(valueStr));               // values[0]
        WriteVarintField(body, 5, 256);                                     // extent
        AppendLengthDelimited(body, 2, BuildFeature());                     // features[0]
        return body.ToArray();
    }

    private static byte[] BuildValue(string s)
    {
        var body = new List<byte>();
        AppendLengthDelimited(body, 1, Encoding.UTF8.GetBytes(s));
        return body.ToArray();
    }

    private static byte[] BuildFeature()
    {
        var body = new List<byte>();
        var tags = new List<byte>();
        WriteVarint(tags, 0); // key index 0
        WriteVarint(tags, 0); // value index 0
        AppendLengthDelimited(body, 2, tags.ToArray()); // tags (packed)
        WriteVarintField(body, 3, 1);                   // type = POINT
        var geom = new List<byte>();
        WriteVarint(geom, 9);           // MoveTo, count=1 → (1<<3)|1
        WriteVarint(geom, ZigZag(5));   // dx=5
        WriteVarint(geom, ZigZag(5));   // dy=5
        AppendLengthDelimited(body, 4, geom.ToArray());
        return body.ToArray();
    }

    private static uint ZigZag(int v) => (uint)((v << 1) ^ (v >> 31));

    private static void WriteVarint(List<byte> buf, ulong value)
    {
        while (value > 0x7F)
        {
            buf.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buf.Add((byte)value);
    }

    private static void WriteTag(List<byte> buf, int field, int wireType) => WriteVarint(buf, (ulong)((field << 3) | wireType));

    private static void WriteVarintField(List<byte> buf, int field, ulong value)
    {
        WriteTag(buf, field, 0);
        WriteVarint(buf, value);
    }

    private static void AppendLengthDelimited(List<byte> buf, int field, byte[] data)
    {
        WriteTag(buf, field, 2);
        WriteVarint(buf, (ulong)data.Length);
        buf.AddRange(data);
    }

    private static byte[] WrapLengthDelimited(int field, byte[] data)
    {
        var buf = new List<byte>();
        AppendLengthDelimited(buf, field, data);
        return buf.ToArray();
    }
}
