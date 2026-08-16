// Standalone check of the vector bake: decodes the embedded blob directly
// (bypassing Avalonia's AssetLoader, which requires a runtime to be set up)
// and re-implements the exact GlobeVectorBaker passes to emit a PNG preview.
// Usage: dotnet run --project ExportPreview.csproj -- <world_geo.bin> <out.png>
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ExportPreview <world_geo.bin> <out.png>");
    return 1;
}

const int Width = 2048;
const int Height = 1024;
const byte LandR = 0x2E, LandG = 0x87, LandB = 0x77;
const byte LandEdgeR = 0x5A, LandEdgeG = 0xF0, LandEdgeB = 0xD8;
const byte OceanR = 0x0A, OceanG = 0x14, OceanB = 0x20;
const byte ShelfR = 0x10, ShelfG = 0x24, ShelfB = 0x36;
const byte BorderR = 0x3E, BorderG = 0x8E, BorderB = 0x86;

double DecodeLon(ushort q) => q / 65535.0 * 360.0 - 180.0;
double DecodeLat(ushort q) => q / 65535.0 * 180.0 - 90.0;

var rings = new List<ushort[]>();
var lines = new List<ushort[]>();
using (var br = new BinaryReader(File.OpenRead(args[0])))
{
    if (br.ReadUInt32() != 0x42474D47u || br.ReadUInt32() != 2u)
    {
        Console.Error.WriteLine("bad magic/version");
        return 1;
    }

    int ringTotal = br.ReadInt32();
    for (int i = 0; i < ringTotal; i++)
    {
        int n = br.ReadInt32();
        var flat = new ushort[n * 2];
        Buffer.BlockCopy(br.ReadBytes(n * 4), 0, flat, 0, n * 4);
        rings.Add(flat);
    }

    int lineTotal = br.ReadInt32();
    for (int i = 0; i < lineTotal; i++)
    {
        int n = br.ReadInt32();
        var flat = new ushort[n * 2];
        Buffer.BlockCopy(br.ReadBytes(n * 4), 0, flat, 0, n * 4);
        lines.Add(flat);
    }
}

Console.WriteLine($"rings={rings.Count} lines={lines.Count}");

using var img = new Image<Rgba32>(Width, Height);
var land = new bool[Width, Height];

// ocean base
for (int y = 0; y < Height; y++)
{
    double lat = 90.0 - (y + 0.5) * 180.0 / Height;
    double band = 0.85 + 0.15 * Math.Cos(lat * Math.PI / 180.0);
    for (int x = 0; x < Width; x++)
        img[x, y] = new Rgba32(
            (byte)(OceanR * band), (byte)(OceanG * band), (byte)(OceanB * band), 255);
}

// scanline fill
var xs = new int[1024];
for (int y = 0; y < Height; y++)
{
    double lat = 90.0 - (y + 0.5) * 180.0 / Height;
    int count = 0;
    foreach (var ring in rings)
    {
        int n = ring.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double lat0 = DecodeLat(ring[2 * j + 1]);
            double lat1 = DecodeLat(ring[2 * i + 1]);
            if (lat0 == lat1 || lat0 > lat == lat1 > lat)
                continue;

            double lon0 = DecodeLon(ring[2 * j]);
            double lon1 = DecodeLon(ring[2 * i]);
            double dlon = lon1 - lon0;
            if (dlon > 180.0) lon1 -= 360.0;
            else if (dlon < -180.0) lon1 += 360.0;
            double lonAt = (lon1 - lon0) * (lat - lat0) / (lat1 - lat0) + lon0;
            while (lonAt < -180.0) lonAt += 360.0;
            while (lonAt > 180.0) lonAt -= 360.0;
            int col = (int)Math.Floor((lonAt + 180.0) * Width / 360.0);
            if (col < 0) col = 0;
            else if (col >= Width) col = Width - 1;
            if (count == xs.Length)
                Array.Resize(ref xs, count * 2);
            xs[count++] = col;
        }
    }

    Array.Sort(xs, 0, count);
    for (int k = 0; k + 1 < count; k += 2)
        for (int x = xs[k]; x <= xs[k + 1]; x++)
            land[x, y] = true;
}

// land + edge
for (int y = 0; y < Height; y++)
{
    for (int x = 0; x < Width; x++)
    {
        if (!land[x, y])
            continue;

        bool edge = false;
        for (int dy = -1; dy <= 1 && !edge; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int xx = x + dx, yy = y + dy;
            if (xx < 0 || xx >= Width || yy < 0 || yy >= Height || !land[xx, yy])
            {
                edge = true;
                break;
            }
        }

        img[x, y] = edge
            ? new Rgba32(LandEdgeR, LandEdgeG, LandEdgeB, 255)
            : new Rgba32(LandR, LandG, LandB, 255);
    }
}

// shelf
for (int y = 0; y < Height; y++)
{
    for (int x = 0; x < Width; x++)
    {
        if (land[x, y])
            continue;

        bool near = false;
        for (int dy = -3; dy <= 3 && !near; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            int xx = x + dx, yy = y + dy;
            if (xx >= 0 && xx < Width && yy >= 0 && yy < Height && land[xx, yy])
            {
                near = true;
                break;
            }
        }

        if (near)
        {
            var c = img[x, y];
            img[x, y] = new Rgba32(
                Math.Max(c.R, ShelfR), Math.Max(c.G, ShelfG), Math.Max(c.B, ShelfB), 255);
        }
    }
}

// borders
foreach (var line in lines)
{
    int n = line.Length / 2;
    for (int i = 0; i + 1 < n; i++)
    {
        int x0 = (int)Math.Round((DecodeLon(line[2 * i]) + 180.0) * Width / 360.0);
        int y0 = (int)Math.Round((90.0 - DecodeLat(line[2 * i + 1])) * Height / 180.0);
        int x1 = (int)Math.Round((DecodeLon(line[2 * (i + 1)]) + 180.0) * Width / 360.0);
        int y1 = (int)Math.Round((90.0 - DecodeLat(line[2 * (i + 1) + 1])) * Height / 180.0);

        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < Height)
                img[x0, y0] = new Rgba32(BorderR, BorderG, BorderB, 255);
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}

await img.SaveAsPngAsync(args[1]);
Console.WriteLine($"wrote {args[1]}");
return 0;
