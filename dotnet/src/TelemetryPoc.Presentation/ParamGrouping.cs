using TelemetryPoc.Domain;

namespace TelemetryPoc.Presentation;

public sealed record ParamGroup(string Name, IReadOnlyList<ChannelMeta> Channels);

public static class ParamGrouping
{
    private static readonly (string Group, string[] Cols)[] Groups =
    {
        ("INU Mode", new[] { "inu_mode1", "inu_mode2" }),
        ("Velocity", new[] { "vel_x", "vel_y", "vel_z", "plat_azim", "vclimb" }),
        ("Attitude", new[] { "roll", "pitch", "heading_t", "heading_m", "sky_pitch", "sky_roll", "sky_azim", "sky_heading", "prsnt_head" }),
        ("Acceleration", new[] { "acc_x", "acc_y", "acc_z" }),
        ("Body Rates", new[] { "roll_r", "pitch_r", "yaw_r" }),
        ("Position", new[] { "lat", "lon" }),
    };

    private static readonly Dictionary<string, string> GroupOfMap = BuildMap();
    private static readonly string[] Order = Groups.Select(g => g.Group).Append("System").ToArray();

    private static Dictionary<string, string> BuildMap()
    {
        var m = new Dictionary<string, string>();
        foreach (var (group, cols) in Groups)
        {
            foreach (var c in cols)
            {
                m[c] = group;
            }
        }

        return m;
    }

    public static string GroupOf(string columnName)
        => GroupOfMap.TryGetValue(columnName, out var g) ? g : "System";

    public static IReadOnlyList<ParamGroup> Group(IReadOnlyList<ChannelMeta> channels)
    {
        var buckets = new Dictionary<string, List<ChannelMeta>>();
        foreach (var ch in channels)
        {
            var g = GroupOf(ch.ColumnName);
            if (!buckets.TryGetValue(g, out var list))
            {
                buckets[g] = list = [];
            }

            list.Add(ch);
        }
        var result = new List<ParamGroup>();
        foreach (var group in Order)
        {
            if (!buckets.TryGetValue(group, out var list) || list.Count == 0)
            {
                continue;
            }

            list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
            result.Add(new ParamGroup(group, list));
        }
        return result;
    }
}
