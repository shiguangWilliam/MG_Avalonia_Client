// Convert Natural Earth GeoJSON -> compact binary for ClientAvalonia embedding.
// Usage: GeoConvert <land.geojson> <border.geojson> <out.bin> [countries.geojson country_out.bin]
//
// Binary layout (little-endian), version 2 — uint16-quantized coordinates:
//   u32 magic 0x42474D47 ('G','M','G','B')
//   u32 version = 2
//   u32 ringCount; per ring: u32 vertexCount, then (u16 qLon, u16 qLat) pairs
//   u32 lineCount; per line: u32 vertexCount, then (u16 qLon, u16 qLat) pairs
//
// Quantization: qLon = round((lon+180)/360*65535), qLat = round((lat+90)/180*65535)
// → 0.0055° (~610 m) steps, ~30x finer than 1:10m data granularity.
//
// Rings include polygon holes; the baker fills with even-odd scanline so
// orientation does not matter. Lines are border strokes.
//
// Country file layout ('GBCB', version 1) — outline rings grouped per country:
//   u32 magic 0x42434247 ('G','B','C','B')
//   u32 version = 1
//   u32 countryCount; per country: char[3] code (ASCII, space padded:
//     ISO_A2 when valid ("IT "), else ISO_A3 ("KOS")), u32 ringCount,
//     per ring: u32 vertexCount, then (u16 qLon, u16 qLat) pairs

using System.Text.Json;

if (args.Length is not (3 or 5))
{
    Console.Error.WriteLine("usage: GeoConvert <land.geojson> <border.geojson> <out.bin> [countries.geojson country_out.bin]");
    return 1;
}

var rings = new List<List<ushort[]>>();
var lines = new List<List<ushort[]>>();

static ushort QuantizeLon(double lon) => (ushort)Math.Round((lon + 180.0) / 360.0 * 65535.0);
static ushort QuantizeLat(double lat) => (ushort)Math.Round((lat + 90.0) / 180.0 * 65535.0);

void AddRing(JsonElement ring)
{
    var pts = new List<ushort[]>();
    foreach (var p in ring.EnumerateArray())
        pts.Add(new[] { QuantizeLon(p[0].GetDouble()), QuantizeLat(p[1].GetDouble()) });

    // Drop the duplicated closing vertex.
    int n = pts.Count;
    if (n > 1 && pts[0][0] == pts[n - 1][0] && pts[0][1] == pts[n - 1][1])
        pts.RemoveAt(n - 1);
    if (pts.Count >= 3)
        rings.Add(pts);
}

void CollectPolygon(JsonElement geom)
{
    switch (geom.GetProperty("type").GetString())
    {
        case "Polygon":
            foreach (var ring in geom.GetProperty("coordinates").EnumerateArray())
                AddRing(ring);
            break;
        case "MultiPolygon":
            foreach (var poly in geom.GetProperty("coordinates").EnumerateArray())
                foreach (var ring in poly.EnumerateArray())
                    AddRing(ring);
            break;
    }
}

void AddLine(JsonElement coords)
{
    var pts = new List<ushort[]>();
    foreach (var p in coords.EnumerateArray())
        pts.Add(new[] { QuantizeLon(p[0].GetDouble()), QuantizeLat(p[1].GetDouble()) });
    if (pts.Count >= 2)
        lines.Add(pts);
}

void CollectLine(JsonElement geom)
{
    switch (geom.GetProperty("type").GetString())
    {
        case "LineString":
            AddLine(geom.GetProperty("coordinates"));
            break;
        case "MultiLineString":
            foreach (var line in geom.GetProperty("coordinates").EnumerateArray())
                AddLine(line);
            break;
    }
}

void ParseFile(string path, Action<JsonElement> collect)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
    });

    foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        collect(feature.GetProperty("geometry"));
}

ParseFile(args[0], CollectPolygon);
ParseFile(args[1], CollectLine);

Console.WriteLine($"rings={rings.Count} lines={lines.Count}");

using (var bw = new BinaryWriter(File.Create(args[2])))
{
    bw.Write(0x42474D47u);
    bw.Write(2u);

    bw.Write((uint)rings.Count);
    foreach (var ring in rings)
    {
        bw.Write((uint)ring.Count);
        foreach (var p in ring)
        {
            bw.Write(p[0]);
            bw.Write(p[1]);
        }
    }

    bw.Write((uint)lines.Count);
    foreach (var line in lines)
    {
        bw.Write((uint)line.Count);
        foreach (var p in line)
        {
            bw.Write(p[0]);
            bw.Write(p[1]);
        }
    }
}

Console.WriteLine($"wrote {new FileInfo(args[2]).Length} bytes");

if (args.Length == 5)
    return WriteCountries(args[3], args[4]) ? 0 : 1;
return 0;

// Groups admin_0 polygons per country keyed by ISO_A2 (fallback ISO_A3) and
// writes the GBCB blob used by the runtime border highlight layer.
static bool WriteCountries(string countriesPath, string outPath)
{
    var byCountry = new SortedDictionary<string, List<List<ushort[]>>>(StringComparer.Ordinal);

    using var doc = JsonDocument.Parse(File.ReadAllText(countriesPath), new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
    });

    foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
    {
        if (!feature.TryGetProperty("properties", out var props))
            continue;

        // Preference chain: ISO_A2 → ISO_A2_EH → ISO_A3 → ISO_A3_EH →
        // ADM0_A3. Natural Earth marks disputed/overseas-composite entries as
        // "-99" in the plain ISO fields (e.g. France with overseas
        // departments), so the *_EH / ADM0_A3 fallbacks are required.
        string code = NormalizeCode(
            Props(props, "ISO_A2"),
            Props(props, "ISO_A2_EH"),
            Props(props, "ISO_A3"),
            Props(props, "ISO_A3_EH"),
            Props(props, "ADM0_A3"));
        if (code.Length == 0)
            continue;

        if (!byCountry.TryGetValue(code, out var ringList))
        {
            ringList = new List<List<ushort[]>>();
            byCountry[code] = ringList;
        }

        void AddRing(JsonElement ring)
        {
            var pts = new List<ushort[]>();
            foreach (var p in ring.EnumerateArray())
                pts.Add(new[] { QuantizeLon(p[0].GetDouble()), QuantizeLat(p[1].GetDouble()) });

            int n = pts.Count;
            if (n > 1 && pts[0][0] == pts[n - 1][0] && pts[0][1] == pts[n - 1][1])
                pts.RemoveAt(n - 1);
            if (pts.Count >= 3)
                ringList.Add(pts);
        }

        var geom = feature.GetProperty("geometry");
        switch (geom.GetProperty("type").GetString())
        {
            case "Polygon":
                foreach (var ring in geom.GetProperty("coordinates").EnumerateArray())
                    AddRing(ring);
                break;
            case "MultiPolygon":
                foreach (var poly in geom.GetProperty("coordinates").EnumerateArray())
                    foreach (var ring in poly.EnumerateArray())
                        AddRing(ring);
                break;
        }
    }

    int totalRings = byCountry.Values.Sum(v => v.Count);
    Console.WriteLine($"countries={byCountry.Count} rings={totalRings}");

    using (var bw = new BinaryWriter(File.Create(outPath)))
    {
        bw.Write(0x42434247u); // 'G','B','C','B'
        bw.Write(1u);
        bw.Write((uint)byCountry.Count);
        foreach (var (code, ringList) in byCountry)
        {
            bw.Write(code[0]);
            bw.Write(code[1]);
            bw.Write(code[2]);
            bw.Write((uint)ringList.Count);
            foreach (var ring in ringList)
            {
                bw.Write((uint)ring.Count);
                foreach (var p in ring)
                {
                    bw.Write(p[0]);
                    bw.Write(p[1]);
                }
            }
        }
    }

    Console.WriteLine($"wrote {new FileInfo(outPath).Length} bytes");
    return byCountry.Count > 0;
}

// Natural Earth uses "-99" for missing/disputed ISO codes; walk the fallback
// chain and keep the first 2- or 3-letter alphabetic code. Codes are stored
// space-padded to 3 chars ("IT ", "KOS", "FRA").
static string NormalizeCode(params string[] candidates)
{
    foreach (string raw in candidates)
    {
        string s = (raw ?? "").Trim().ToUpperInvariant();
        if (s.Length is 2 or 3 && s.All(char.IsLetter))
            return s.PadRight(3);
    }

    return "";
}

static string Props(JsonElement props, string name)
    => props.TryGetProperty(name, out var el) ? el.GetString() ?? "" : "";
