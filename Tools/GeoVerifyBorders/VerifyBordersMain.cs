// Sanity checks on country_borders.bin ('GBCB' v1): structural integrity,
// per-country ring presence and geographic spot checks via point-in-ring
// (ray casting on lon/lat). Usage: VerifyBorders <country_borders.bin>
using System.Globalization;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: VerifyBorders <country_borders.bin>");
    return 1;
}

using var fs = File.OpenRead(args[0]);
using var br = new BinaryReader(fs);

if (br.ReadUInt32() != 0x42434247u || br.ReadUInt32() != 1u)
{
    Console.Error.WriteLine("bad magic/version");
    return 1;
}

int countryCount = br.ReadInt32();
var map = new Dictionary<string, List<double[][]>>();
for (int c = 0; c < countryCount; c++)
{
    string code = new string(br.ReadChars(3)).TrimEnd();
    int ringCount = br.ReadInt32();
    var rings = new List<double[][]>();
    for (int r = 0; r < ringCount; r++)
    {
        int n = br.ReadInt32();
        var ring = new double[n][];
        for (int i = 0; i < n; i++)
        {
            double lon = br.ReadUInt16() / 65535.0 * 360.0 - 180.0;
            double lat = br.ReadUInt16() / 65535.0 * 180.0 - 90.0;
            ring[i] = new[] { lon, lat };
        }

        rings.Add(ring);
    }

    map[code] = rings;
}

Console.WriteLine($"countries={map.Count} rings={map.Values.Sum(v => v.Count)}");

static bool PointInRing(double[][] ring, double lon, double lat)
{
    bool inside = false;
    int n = ring.Length;
    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        double xi = ring[i][0], yi = ring[i][1];
        double xj = ring[j][0], yj = ring[j][1];
        if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
            inside = !inside;
    }

    return inside;
}

bool InsideCountry(string code, double lon, double lat)
{
    if (!map.TryGetValue(code, out var rings))
        return false;

    // Even-odd across all rings: outer ring counts, lakes subtract.
    int hits = 0;
    foreach (var ring in rings)
        if (PointInRing(ring, lon, lat))
            hits++;

    return hits % 2 == 1;
}

var checks = new (string code, string name, double lon, double lat, bool expectInside)[]
{
    ("US", "Kansas",      -98.0,  38.5, true),
    ("US", "49N border",  -95.0,  48.9, true),
    ("US", "Toronto(→CA)", -79.4, 43.7, false),
    ("IT", "Rome",         12.5,  41.9, true),
    ("IT", "Milan",        9.19,  45.46, true),
    ("IT", "Corsica(→FR)",  9.0,  42.0, false),
    ("JP", "Tokyo",       139.7, 35.7, true),
    ("JP", "Osaka",       135.5, 34.7, true),
    ("JP", "Seoul(→KR)",  127.0, 37.5, false),
    ("DE", "Berlin",       13.4, 52.5, true),
    ("FR", "Paris",         2.35, 48.85, true),
    ("GB", "London",       -0.13, 51.5, true),
    ("BR", "Brasilia",     -47.9, -15.8, true),
    ("AU", "Canberra",     149.1, -35.3, true),
    ("EG", "Cairo",         31.2, 30.0, true),
    ("RU", "Moscow",        37.6, 55.75, true),
};

int fails = 0;
foreach (var (code, name, lon, lat, expectInside) in checks)
{
    bool inside = InsideCountry(code, lon, lat);
    bool ok = inside == expectInside;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {code,-3} {name,-16} ({lon.ToString(CultureInfo.InvariantCulture),8},{lat.ToString(CultureInfo.InvariantCulture),7}) inside={inside}");
    if (!ok) fails++;
}

// Ring sanity: every country has ≥1 ring with ≥3 vertices, lat/lon in range.
foreach (var (code, rings) in map)
{
    if (rings.Count < 1 || rings.Any(r => r.Length < 3))
    {
        Console.WriteLine($"FAIL {code}: bad ring structure");
        fails++;
    }

    foreach (var ring in rings)
        foreach (var p in ring)
        {
            if (p[0] < -180.5 || p[0] > 180.5 || p[1] < -90.5 || p[1] > 90.5)
            {
                Console.WriteLine($"FAIL {code}: coordinate out of range {p[0]},{p[1]}");
                fails++;
                goto nextCountry;
            }
        }

    nextCountry: ;
}

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILURES");
return fails == 0 ? 0 : 1;
