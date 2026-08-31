// Programmatic geographic sanity check on the vector bake preview.
// Samples known coordinates and asserts land/ocean classification.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: VerifyGeo <preview.png>");
    return 1;
}

using var img = Image.Load<Rgba32>(args[0]);
int w = img.Width, h = img.Height;

Rgba32 Sample(double lon, double lat)
{
    int x = (int)Math.Round((lon + 180.0) / 360.0 * w);
    int y = (int)Math.Round((90.0 - lat) / 180.0 * h);
    x = Math.Clamp(x, 0, w - 1);
    y = Math.Clamp(y, 0, h - 1);
    return img[x, y];
}

bool IsLand(Rgba32 c) => c.G > 0x60; // land teal G=0x87, ocean G<=0x36

var landChecks = new (string name, double lon, double lat)[]
{
    ("Sahara",          10.0,  23.0),
    ("Siberia",        100.0,  62.0),
    ("Amazon",         -60.0,  -5.0),
    ("Australia",      134.0, -25.0),
    ("Greenland",      -42.0,  72.0),
    ("India",           78.0,  21.0),
    ("Antarctica",      20.0, -80.0),
    ("US-Midwest",     -95.0,  40.0),
};

var oceanChecks = new (string name, double lon, double lat)[]
{
    ("Pacific-Central", -150.0,   0.0),
    ("Atlantic-Central", -30.0,  25.0),
    ("Indian-Central",    75.0, -20.0),
    ("South-Pacific",   -120.0, -45.0),
    ("Arctic-Ocean",      10.0,  85.0),
    ("Point-Nemo",       -110.0, -48.0),
};

int fails = 0;
foreach (var (name, lon, lat) in landChecks)
{
    var c = Sample(lon, lat);
    bool ok = IsLand(c);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} land   {name,-16} ({lon,7},{lat,6}) rgb=({c.R},{c.G},{c.B})");
    if (!ok) fails++;
}

foreach (var (name, lon, lat) in oceanChecks)
{
    var c = Sample(lon, lat);
    bool ok = !IsLand(c);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} ocean {name,-16} ({lon,7},{lat,6}) rgb=({c.R},{c.G},{c.B})");
    if (!ok) fails++;
}

// Border presence: sample along the US-Canada 49th parallel (~lon -95).
int borderHits = 0;
for (double lon = -124.0; lon <= -67.0; lon += 0.05)
{
    var c = Sample(lon, 49.0);
    if (c.R == 0x3E && c.G == 0x8E && c.B == 0x86)
        borderHits++;
}

Console.WriteLine($"US-Canada border pixel hits along 49N: {borderHits}");
if (borderHits < 100)
{
    Console.WriteLine("FAIL border coverage");
    fails++;
}

Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : $"{fails} CHECKS FAILED");
return fails == 0 ? 0 : 1;
