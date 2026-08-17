using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Rampastring.Tools;

namespace ClientAvalonia.Controls;

/// <summary>
/// The shared 3D solar-system backdrop. One persistent scene sampled by every
/// Tactical panel: Kepler-driven planets (SolarSystemScene supplies positions),
/// a ringed Saturn, a GIS-textured Earth and an emissive sun. Only uniforms
/// change per frame — geometry and textures upload once. Draw order: star
/// quad → orbit lines → sun → planets (depth sorted) → Saturn ring → Earth.
/// </summary>
public sealed class SolarSystemGlControl : OpenGlControlBase
{
    private const int OrbitSegments = 64;

    // Local GL constants (standard values) — GlConsts surface differs across versions.
    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_FLOAT = 0x1406;
    private const int GL_UNSIGNED_SHORT = 0x1403;
    private const int GL_TRIANGLES = 0x0004;
    private const int GL_LINE_STRIP = 0x0003;
    private const int GL_TEXTURE0 = 0x84C0;
    private const int GL_RGBA = 0x1908;
    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_LINEAR = 0x2601;
    private const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlGenerateMipmap(int target);

    private const int GL_DEPTH_TEST = 0x0B71;
    private const int GL_CULL_FACE = 0x0B44;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_COLOR_BUFFER_BIT = 0x4000;
    private const int GL_DEPTH_BUFFER_BIT = 0x0100;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;
    private const int GL_UNSIGNED_BYTE = 0x1401;

    private const string BodyVertexShader = @"
attribute vec3 aPos;
attribute vec2 aUv;
uniform mat4 uMvp;
uniform mat4 uModel;
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vWorldPos;
void main()
{
    vUv = aUv;
    vNormal = (uModel * vec4(aPos, 0.0)).xyz;
    vWorldPos = (uModel * vec4(aPos, 1.0)).xyz;
    gl_Position = uMvp * vec4(aPos, 1.0);
}
";

    private const string PlanetFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vWorldPos;

uniform float uColorAR;
uniform float uColorAG;
uniform float uColorAB;
uniform float uColorBR;
uniform float uColorBG;
uniform float uColorBB;
uniform float uSunX;
uniform float uSunY;
uniform float uSunZ;
uniform float uCamX;
uniform float uCamY;
uniform float uCamZ;
uniform float uBanding;
uniform float uTime;
uniform float uOpacity;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void main()
{
    vec3 n = normalize(vNormal);
    vec3 toSun = normalize(vec3(uSunX, uSunY, uSunZ) - vWorldPos);
    vec3 toCam = normalize(vec3(uCamX, uCamY, uCamZ) - vWorldPos);
    float ndl = max(dot(n, toSun), 0.0);
    float view = clamp(dot(n, toCam), 0.0, 1.0);

    vec3 colorA = vec3(uColorAR, uColorAG, uColorAB);
    vec3 colorB = vec3(uColorBR, uColorBG, uColorBB);
    vec3 albedo;
    if (uBanding > 0.5)
    {
        float band = sin(vUv.y * 3.14159 * 24.0 + hash(vec2(floor(vUv.y * 24.0), 3.0)) * 1.9);
        albedo = mix(colorB, colorA, 0.55 + 0.45 * band);
    }
    else
    {
        albedo = mix(colorB, colorA, 0.5 + 0.5 * sin(vUv.y * 3.14159 * 20.0));
        albedo += (hash(vUv * 247.0) - 0.5) * 0.07;
    }

    // Keep planet identity, then apply holographic projection filter.
    vec3 lit = albedo * (0.18 + 1.05 * ndl);
    float scan = 0.88 + 0.12 * sin(vUv.y * 72.0 + uTime * 2.4);
    float grid = 0.92 + 0.08 * step(0.92, fract(vUv.x * 28.0 + vUv.y * 18.0));
    vec3 holo = mix(lit, lit * vec3(0.55, 0.92, 1.15), 0.38);
    holo *= scan * grid;

    float rim = pow(1.0 - view, 2.2);
    holo += vec3(0.25, 0.85, 1.0) * rim * 0.85;
    holo += vec3(0.15, 0.55, 0.75) * pow(1.0 - view, 5.0) * 0.35;

    float alpha = smoothstep(0.0, 0.06, view) * clamp(uOpacity, 0.0, 1.0);
    gl_FragColor = vec4(holo * alpha, alpha);
}
";

    private const string SunFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vWorldPos;
uniform float uTime;
uniform float uGlow;
uniform float uCamX;
uniform float uCamY;
uniform float uCamZ;
uniform float uOpacity;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void main()
{
    vec3 n = normalize(vNormal);
    vec3 cam = vec3(uCamX, uCamY, uCamZ);
    if (dot(cam, cam) < 1e-4)
        cam = vec3(0.0, 4.0, 6.0);
    vec3 toCam = normalize(cam - vWorldPos);
    float view = clamp(dot(n, toCam), 0.0, 1.0);
    float pulse = 0.55 + 0.45 * sin(uTime * 1.6);

    // White-cyan holographic core (reference plate), not a warm yellow star.
    float noise = hash(vUv * 40.0 + uTime * 0.15);
    vec3 core = vec3(0.92, 0.98, 1.0);
    vec3 cyan = vec3(0.35, 0.82, 1.0);
    vec3 deep = vec3(0.08, 0.28, 0.55);
    vec3 col = mix(deep, cyan, pow(view, 0.55));
    col = mix(col, core, pow(view, 1.35) * (0.75 + 0.25 * pulse));
    col += cyan * noise * 0.12 * view;

    float alpha = uGlow > 0.5
        ? pow(view, 0.75) * 0.62
        : max(smoothstep(0.0, 0.03, view), 0.95 * step(0.01, view));
    alpha *= clamp(uOpacity, 0.0, 1.0);
    gl_FragColor = vec4(col * alpha, alpha);
}
";

    private const string EarthFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vWorldPos;

uniform sampler2D uAlbedo;
uniform float uSunX;
uniform float uSunY;
uniform float uSunZ;
uniform float uCamX;
uniform float uCamY;
uniform float uCamZ;
uniform float uAccentR;
uniform float uAccentG;
uniform float uAccentB;
uniform float uAtmosphere;
uniform float uGisAmount;
uniform float uGisRefine;
uniform float uGisWidth;
uniform float uGisHeight;

void main()
{
    vec3 n = normalize(vNormal);
    vec3 toSun = normalize(vec3(uSunX, uSunY, uSunZ) - vWorldPos);
    vec3 toCam = normalize(vec3(uCamX, uCamY, uCamZ) - vWorldPos);
    float ndl = max(dot(n, toSun), 0.0);
    float view = clamp(dot(n, toCam), 0.0, 1.0);
    float fill = max(dot(n, toCam), 0.0);

    if (uAtmosphere > 0.5)
    {
        float rim = pow(1.0 - view, 1.9);
        float day = 0.40 + 0.60 * ndl;
        vec3 accent = vec3(uAccentR, uAccentG, uAccentB);
        vec3 colAtm = mix(accent, vec3(0.45, 0.85, 1.0), 0.45) * rim * day;
        float alphaAtm = rim * 0.62 * day;
        gl_FragColor = vec4(colAtm * alphaAtm, alphaAtm);
        return;
    }

    // GIS fill with progressive refine: refine=0 → ~1:2 quantized sample,
    // refine=1 → full texel. Same atlas — no mid-zoom rebake.
    float refine = clamp(uGisRefine, 0.0, 1.0);
    float cellsU = max(uGisWidth * mix(0.5, 1.0, refine), 2.0);
    float cellsV = max(uGisHeight * mix(0.5, 1.0, refine), 1.0);
    vec2 uvQ = (floor(vUv * vec2(cellsU, cellsV)) + 0.5) / vec2(cellsU, cellsV);
    vec3 gisCoarse = texture2D(uAlbedo, uvQ).rgb;
    vec3 gisFine = texture2D(uAlbedo, vUv).rgb;
    vec3 gis = mix(gisCoarse, gisFine, refine);

    vec3 procedural = mix(vec3(0.08, 0.22, 0.48), vec3(0.16, 0.40, 0.26), 0.35 + 0.25 * sin(vUv.y * 6.2831));
    vec3 albedo = mix(procedural, gis, clamp(uGisAmount, 0.0, 1.0));
    vec3 lit = albedo * (0.28 + 0.95 * ndl + 0.18 * fill);

    float scan = 0.90 + 0.10 * sin(vUv.y * 80.0);
    float grid = 0.94 + 0.06 * step(0.93, fract(vUv.x * 32.0));
    vec3 holo = mix(lit, lit * vec3(0.50, 0.90, 1.20), 0.42);
    holo *= scan * grid;

    float rim = pow(1.0 - view, 2.15);
    holo += vec3(uAccentR, uAccentG, uAccentB) * rim * 0.95;

    float alpha = smoothstep(0.0, 0.05, view);
    gl_FragColor = vec4(holo * alpha, alpha);
}
";

    private const string RingVertexShader = @"
attribute vec3 aPos;
attribute vec2 aUv;
uniform mat4 uMvp;
varying vec2 vUv;
void main()
{
    vUv = aUv;
    gl_Position = uMvp * vec4(aPos, 1.0);
}
";

    private const string RingFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
varying vec2 vUv;
uniform float uColorR;
uniform float uColorG;
uniform float uColorB;
uniform float uSunSide;
uniform float uOpacity;

void main()
{
    float r = vUv.x;
    float band = 0.5 + 0.5 * sin(r * 44.0);
    float gaps = smoothstep(0.30, 0.36, r) * (1.0 - smoothstep(0.84, 0.90, r));
    float a = (0.28 + 0.48 * band) * gaps * clamp(uOpacity, 0.0, 1.0);

    // Holo cyan wash over ring identity colors.
    vec3 base = vec3(uColorR, uColorG, uColorB) * (0.55 + 0.45 * band) * uSunSide;
    vec3 col = mix(base, base * vec3(0.55, 0.90, 1.15), 0.45);
    gl_FragColor = vec4(col * a, a);
}
";

    private const string LineVertexShader = @"
attribute vec3 aPos;
uniform mat4 uMvp;
void main()
{
    gl_Position = uMvp * vec4(aPos, 1.0);
}
";

    private const string LineFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
uniform float uColorR;
uniform float uColorG;
uniform float uColorB;
uniform float uAlpha;
void main()
{
    gl_FragColor = vec4(uColorR * uAlpha, uColorG * uAlpha, uColorB * uAlpha, uAlpha);
}
";

    private const string StarVertexShader = @"
attribute vec2 aPos;
attribute vec2 aUv;
varying vec2 vUv;
void main()
{
    vUv = aUv;
    gl_Position = vec4(aPos, 0.9995, 1.0);
}
";

    private const string StarFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
varying vec2 vUv;
uniform float uTime;
uniform float uAspect;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float starLayer(vec2 uv, float density, float seed)
{
    vec2 cell = floor(uv * density);
    vec2 f = fract(uv * density);
    float h = hash(cell + seed);
    if (h < 0.42)
        return 0.0;

    vec2 center = vec2(hash(cell + seed + 1.7), hash(cell + seed + 9.1));
    float d = length(f - center);
    float twinkle = 0.55 + 0.45 * sin(uTime * (0.45 + h) + h * 6.2831);
    return smoothstep(0.20, 0.0, d) * twinkle * (h - 0.42) * 2.8;
}

void main()
{
    vec2 uv = vUv * vec2(uAspect, 1.0);
    float stars = starLayer(uv, 16.0, 0.0)
                + starLayer(uv, 34.0, 13.0) * 0.8
                + starLayer(uv, 68.0, 29.0) * 0.45
                + starLayer(uv, 105.0, 47.0) * 0.25;

    float n1 = sin(uv.x * 1.6 + uTime * 0.04) * sin(uv.y * 1.2 + 0.6);
    float n2 = sin(uv.x * 3.1 - uTime * 0.03 + 1.2) * sin(uv.y * 2.4);
    float neb = 0.55 + 0.45 * n1 * n2;
    vec3 nebCol = vec3(0.010, 0.030, 0.070) + vec3(0.035, 0.075, 0.140) * neb;
    float flare = smoothstep(1.2, 0.0, length((vUv - vec2(0.52, 0.48)) * vec2(1.4, 1.0)));
    nebCol += vec3(0.08, 0.22, 0.38) * flare * 0.35;

    vec3 col = nebCol + vec3(0.75, 0.90, 1.0) * stars * 1.25;
    gl_FragColor = vec4(col, 1.0);
}
";

    // Entry points GlInterface does not wrap.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlBlendFunc(int sfactor, int dfactor);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDisableVertexAttribArray(int index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlUniform3f(int location, float x, float y, float z);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDepthMask(bool flag);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GlDisable(int cap);

    private GlBlendFunc? _blendFunc;
    private GlDisableVertexAttribArray? _disableVertexAttribArray;
    private GlUniform3f? _uniform3f;
    private GlDepthMask? _depthMask;
    private GlDisable? _disable;

    private int _bodyProgram;
    private int _sunProgram;
    private int _earthProgram;
    private int _ringProgram;
    private int _lineProgram;
    private int _starProgram;

    private int _sphereVbo;
    private int _sphereEbo;
    private int _sphereIndexCount;

    private int _ringVbo;
    private int _ringEbo;
    private int _ringIndexCount;

    private int _orbitVbo;
    private int _orbitBodyCount;

    private int _starVbo;

    private int _earthAlbedo;
    private int _earthAlbedoWidth = 2;
    private int _earthAlbedoHeight = 1;

    private readonly SolarSystemScene _scene = new();
    private bool _failed;
    private bool _hasRendered;
    private double _cameraX;
    private double _cameraY;
    private double _cameraZ;

    /// <summary>True once a frame reached the GPU.</summary>
    public bool HasRendered => _hasRendered;

    /// <summary>Shader/resource creation failed; the backdrop stays transparent.</summary>
    public bool IsContentFailed => _failed;

    /// <summary>Scene accessor (pose navigation, earth bridge queries).</summary>
    internal SolarSystemScene Scene => _scene;

    /// <summary>Advances the simulation and requests a frame.</summary>
    public void Tick(double dt)
    {
        _scene.Advance(dt);
        RequestNextFrameRendering();
    }

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            while (gl.GetError() != 0)
            {
            }

            _bodyProgram = BuildProgram(gl, BodyVertexShader, PlanetFragmentShader);
            _sunProgram = BuildProgram(gl, BodyVertexShader, SunFragmentShader);
            _earthProgram = BuildProgram(gl, BodyVertexShader, EarthFragmentShader);
            _ringProgram = BuildProgram(gl, RingVertexShader, RingFragmentShader);
            _lineProgram = BuildProgram(gl, LineVertexShader, LineFragmentShader);
            _starProgram = BuildProgram(gl, StarVertexShader, StarFragmentShader);

            BuildSphere(gl, 48, 32, out _sphereVbo, out _sphereEbo, out _sphereIndexCount);
            BuildRing(gl, out _ringVbo, out _ringEbo, out _ringIndexCount);
            BuildOrbitLines(gl, out _orbitVbo, out _orbitBodyCount);
            BuildStarQuad(gl, out _starVbo);

            if (!GlobeTextureBaker.TryGetPixels(out byte[] pixels, out int tw, out int th))
            {
                tw = 2;
                th = 1;
                pixels = new byte[] { 18, 34, 52, 255, 18, 34, 52, 255 };
            }

            _earthAlbedo = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, _earthAlbedo);
            _earthAlbedoWidth = tw;
            _earthAlbedoHeight = th;
            fixed (byte* pp = pixels)
            {
                gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, tw, th, 0, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)pp);
            }

            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

            // Mipmaps damp GIS coastline shimmer when Earth is mid-distance.
            var genMip = TryBind<GlGenerateMipmap>(gl, "glGenerateMipmap");
            if (genMip != null)
            {
                genMip(GL_TEXTURE_2D);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
            }

            _blendFunc = TryBind<GlBlendFunc>(gl, "glBlendFunc");
            _disableVertexAttribArray = TryBind<GlDisableVertexAttribArray>(gl, "glDisableVertexAttribArray");
        _uniform3f = null; // colors use Uniform1f (same path as TacticalGlobeGlControl)
        _depthMask = TryBind<GlDepthMask>(gl, "glDepthMask");
        _disable = TryBind<GlDisable>(gl, "glDisable");

        gl.Enable(GL_DEPTH_TEST);
        // Match TacticalGlobeGlControl: do NOT enable GL_CULL_FACE. The UV
        // sphere winding + our view handedness culls every triangle if back
        // faces are discarded — orbits draw, planets vanish.
        _disable?.Invoke(GL_CULL_FACE);
        gl.Enable(GL_BLEND);
        _blendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

            if (gl.GetError() != 0)
                throw new InvalidOperationException("GL error during solar system init");

            _failed = false;
            Logger.Log($"SolarSystemGlControl: scene ready (earth albedo {tw}x{th}).");
        }
        catch (Exception ex)
        {
            _failed = true;
            Logger.Log($"SolarSystemGlControl: GL init failed — {ex.Message}");
            CleanupPartial(gl);
        }
    }

    private static TDelegate? TryBind<TDelegate>(GlInterface gl, string name)
        where TDelegate : class
    {
        IntPtr ptr = gl.GetProcAddress(name);
        if (ptr == IntPtr.Zero)
            return null;

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
    }

    private static int BuildProgram(GlInterface gl, string vertexSource, string fragmentSource)
    {
        int vs = gl.CreateShader(GL_VERTEX_SHADER);
        string? vsErr = gl.CompileShaderAndGetError(vs, vertexSource);
        if (vsErr != null)
            throw new InvalidOperationException("vertex shader: " + vsErr);

        int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? fsErr = gl.CompileShaderAndGetError(fs, fragmentSource);
        if (fsErr != null)
            throw new InvalidOperationException("fragment shader: " + fsErr);

        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.BindAttribLocationString(program, 0, "aPos");
        gl.BindAttribLocationString(program, 1, "aUv");
        string? linkErr = gl.LinkProgramAndGetError(program);
        if (linkErr != null)
            throw new InvalidOperationException("link: " + linkErr);

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }

    private static unsafe void BuildSphere(GlInterface gl, int slices, int stacks, out int vbo, out int ebo, out int indexCount)
    {
        var verts = new float[(slices + 1) * (stacks + 1) * 5];
        int vi = 0;
        for (int st = 0; st <= stacks; st++)
        {
            double v = st / (double)stacks;
            double lat = (90.0 - v * 180.0) * Math.PI / 180.0;
            double cl = Math.Cos(lat);
            double sl = Math.Sin(lat);
            for (int s = 0; s <= slices; s++)
            {
                double u = s / (double)slices;
                double lon = (u * 360.0 - 180.0) * Math.PI / 180.0;
                verts[vi++] = (float)(cl * Math.Sin(lon));
                verts[vi++] = (float)sl;
                verts[vi++] = (float)(cl * Math.Cos(lon));
                verts[vi++] = (float)u;
                verts[vi++] = (float)v;
            }
        }

        var indices = new ushort[slices * stacks * 6];
        int ii = 0;
        for (int st = 0; st < stacks; st++)
        {
            for (int s = 0; s < slices; s++)
            {
                int cur = st * (slices + 1) + s;
                int nxt = cur + slices + 1;
                indices[ii++] = (ushort)cur;
                indices[ii++] = (ushort)nxt;
                indices[ii++] = (ushort)(cur + 1);
                indices[ii++] = (ushort)(cur + 1);
                indices[ii++] = (ushort)nxt;
                indices[ii++] = (ushort)(nxt + 1);
            }
        }

        indexCount = indices.Length;
        vbo = UploadBuffer(gl, verts);
        ebo = gl.GenBuffer();
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, ebo);
        fixed (ushort* ip = indices)
        {
            gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(indices.Length * sizeof(ushort)), (IntPtr)ip, GL_STATIC_DRAW);
        }
    }

    private static unsafe void BuildRing(GlInterface gl, out int vbo, out int ebo, out int indexCount)
    {
        const int segments = 96;
        const double inner = 1.35;
        const double outer = 2.25;

        var verts = new float[(segments + 1) * 2 * 5];
        int vi = 0;
        for (int s = 0; s <= segments; s++)
        {
            double a = s / (double)segments * 2.0 * Math.PI;
            float ca = (float)Math.Cos(a);
            float sa = (float)Math.Sin(a);
            verts[vi++] = (float)(inner * ca);
            verts[vi++] = 0f;
            verts[vi++] = (float)(inner * sa);
            verts[vi++] = 0f;
            verts[vi++] = 0.5f;
            verts[vi++] = (float)(outer * ca);
            verts[vi++] = 0f;
            verts[vi++] = (float)(outer * sa);
            verts[vi++] = 1f;
            verts[vi++] = 0.5f;
        }

        var indices = new ushort[segments * 6];
        int ii = 0;
        for (int s = 0; s < segments; s++)
        {
            int inner0 = s * 2;
            int outer0 = inner0 + 1;
            int inner1 = (s + 1) * 2;
            int outer1 = inner1 + 1;
            indices[ii++] = (ushort)inner0;
            indices[ii++] = (ushort)outer0;
            indices[ii++] = (ushort)outer1;
            indices[ii++] = (ushort)inner0;
            indices[ii++] = (ushort)outer1;
            indices[ii++] = (ushort)inner1;
        }

        indexCount = indices.Length;
        vbo = UploadBuffer(gl, verts);
        ebo = gl.GenBuffer();
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, ebo);
        fixed (ushort* ip = indices)
        {
            gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(indices.Length * sizeof(ushort)), (IntPtr)ip, GL_STATIC_DRAW);
        }
    }

    private static unsafe void BuildOrbitLines(GlInterface gl, out int vbo, out int bodyCount)
    {
        var bodies = SolarSystemScene.CreateBodies();
        var verts = new System.Collections.Generic.List<float>(bodies.Length * (OrbitSegments + 1) * 3);
        foreach (ref readonly var body in bodies.AsSpan())
        {
            for (int step = 0; step <= OrbitSegments; step++)
            {
                double e = step / (double)OrbitSegments * 2.0 * Math.PI;
                SolarSystemScene.OrbitPoint(in body, e, out double x, out double y, out double z);
                verts.Add((float)x);
                verts.Add((float)y);
                verts.Add((float)z);
            }
        }

        bodyCount = bodies.Length;
        vbo = UploadBuffer(gl, verts.ToArray());
    }

    private static unsafe void BuildStarQuad(GlInterface gl, out int vbo)
    {
        float[] verts =
        {
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f,
        };

        vbo = UploadBuffer(gl, verts);
    }

    private static unsafe int UploadBuffer(GlInterface gl, float[] data)
    {
        int vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (float* vp = data)
        {
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(data.Length * sizeof(float)), (IntPtr)vp, GL_STATIC_DRAW);
        }

        return vbo;
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int w = Math.Max(1, (int)(Bounds.Width * scaling));
        int h = Math.Max(1, (int)(Bounds.Height * scaling));

        gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.004f, 0.012f, 0.030f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        if (_failed)
            return;

        float aspect = (float)w / Math.Max(1, h);

        // ---- Star backdrop (depth test off; pushed to the far plane) ----
        _disable?.Invoke(GL_DEPTH_TEST);
        gl.UseProgram(_starProgram);
        gl.Uniform1f(gl.GetUniformLocationString(_starProgram, "uTime"), (float)_scene.Time);
        gl.Uniform1f(gl.GetUniformLocationString(_starProgram, "uAspect"), aspect);
        BindVec2(gl, _starVbo, _starProgram, "aPos", 0, (IntPtr)0);
        BindVec2(gl, _starVbo, _starProgram, "aUv", 0, (IntPtr)8);
        gl.DrawArrays(GL_TRIANGLES, 0, (IntPtr)6);
        gl.Enable(GL_DEPTH_TEST);

        float[] view = BuildCameraMatrix(aspect);
        float outerOpacity = (float)Math.Clamp(SolarSystemDirector.OuterSystemOpacity, 0.0, 1.0);

        if (outerOpacity > 0.02f)
        {
            // ---- Orbit lines: inner rings bright, outer rings faint (less empty disc) ----
            gl.UseProgram(_lineProgram);
            int lineMvp = gl.GetUniformLocationString(_lineProgram, "uMvp");
            fixed (float* vp = view)
            {
                gl.UniformMatrix4fv(lineMvp, 1, false, vp);
            }

            gl.Uniform1f(gl.GetUniformLocationString(_lineProgram, "uColorR"), 0.28f);
            gl.Uniform1f(gl.GetUniformLocationString(_lineProgram, "uColorG"), 0.88f);
            gl.Uniform1f(gl.GetUniformLocationString(_lineProgram, "uColorB"), 1.00f);

            bool earthCam = _scene.Camera.Focus == SolarSystemScene.CameraFocus.Earth;
            _depthMask?.Invoke(false);
            gl.BindBuffer(GL_ARRAY_BUFFER, _orbitVbo);
            int linePos = gl.GetAttribLocationString(_lineProgram, "aPos");
            if (linePos >= 0)
            {
                gl.VertexAttribPointer(linePos, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
                gl.EnableVertexAttribArray(linePos);
                for (int i = 0; i < _orbitBodyCount; i++)
                {
                    float orbitAlpha = OrbitLineAlpha(i, earthCam) * outerOpacity;
                    if (orbitAlpha < 0.02f)
                        continue;

                    gl.Uniform1f(gl.GetUniformLocationString(_lineProgram, "uAlpha"), orbitAlpha);
                    gl.DrawArrays(GL_LINE_STRIP, i * (OrbitSegments + 1), (IntPtr)(OrbitSegments + 1));
                }

                _disableVertexAttribArray?.Invoke(linePos);
            }

            _depthMask?.Invoke(true);

            // ---- Planets (far first for correct blending edges) ----
            var bodies = _scene.Bodies;
            int[] order = new int[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => DistanceSquaredToCamera(a).CompareTo(DistanceSquaredToCamera(b)));

            foreach (int i in order)
            {
                if (i == _scene.EarthIndex)
                    continue;

                ref readonly var body = ref bodies[i];
                (double px, double py, double pz) = _scene.GetPosition(i);
                float radius = (float)SolarSystemScene.BodyRadius(in body);
                // Outer planets: silhouette-scale so they don't steal Earth's focus.
                if (body.A > 4.0)
                    radius *= 0.78f;

                DrawPlanet(gl, view, i, px, py, pz, radius, outerOpacity);

                if (body.Kind == SolarSystemScene.PlanetKind.Ringed)
                    DrawSaturnRing(gl, view, px, py, pz, radius, in body, outerOpacity);
            }
        }

        // ---- Earth (single marble authority; GIS refine by camera distance) ----
        {
            ref readonly var earth = ref _scene.Bodies[_scene.EarthIndex];
            (double ex, double ey, double ez) = _scene.EarthPosition;
            DrawEarth(gl, view, ex, ey, ez, (float)SolarSystemScene.BodyRadius(in earth), earth.AxialTiltDeg);
        }

        // ---- Sun last as secondary light anchor (faded with outer system) ----
        if (outerOpacity > 0.02f)
        {
            _disable?.Invoke(GL_DEPTH_TEST);
            DrawSun(gl, view, outerOpacity);
            gl.Enable(GL_DEPTH_TEST);
        }

        _hasRendered = true;
    }

    private static float OrbitLineAlpha(int bodyIndex, bool earthCamera)
    {
        // Mercury..Mars = 0..3, Jupiter+ = 4..
        if (earthCamera)
        {
            return bodyIndex switch
            {
                <= 3 => 0.55f,
                <= 5 => 0.22f,
                _ => 0.10f,
            };
        }

        return bodyIndex <= 5 ? 0.70f : 0.28f;
    }

    private double DistanceSquaredToCamera(int bodyIndex)
    {
        (double x, double y, double z) = _scene.GetPosition(bodyIndex);
        double dx = x - _cameraX;
        double dy = y - _cameraY;
        double dz = z - _cameraZ;
        return dx * dx + dy * dy + dz * dz;
    }

    private static double PlanetSpinPhase(in SolarSystemScene.KeplerBody body, double time)
    {
        // One visual rotation per 1/14 of the body's own year — lively but calm.
        return time / (body.PeriodSeconds / 14.0) * 2.0 * Math.PI;
    }

    /// <summary>Builds the view-projection from the scene camera; caches the camera position.</summary>
    private float[] BuildCameraMatrix(float aspect)
    {
        float[] vp = _scene.BuildViewProjection(aspect, out _cameraX, out _cameraY, out _cameraZ);
        return vp;
    }

    private unsafe void DrawSun(GlInterface gl, float[] view, float opacity)
    {
        gl.UseProgram(_sunProgram);
        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uTime"), (float)_scene.Time);
        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uCamX"), (float)_cameraX);
        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uCamY"), (float)_cameraY);
        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uCamZ"), (float)_cameraZ);
        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uOpacity"), opacity);

        // Soft corona first (larger, additive-ish via premultiplied alpha).
        {
            var glow = MultiplyAffine(view, 0, 0, 0, (float)(SolarSystemScene.SunRadius * 1.70), 0, 0);
            int uMvp = gl.GetUniformLocationString(_sunProgram, "uMvp");
            int uModel = gl.GetUniformLocationString(_sunProgram, "uModel");
            fixed (float* mp = glow.Item1, dp = glow.Item2)
            {
                gl.UniformMatrix4fv(uMvp, 1, false, mp);
                gl.UniformMatrix4fv(uModel, 1, false, dp);
            }

            gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uGlow"), 1f);
            _depthMask?.Invoke(false);
            DrawSphereGeometry(gl, _sunProgram);
            _depthMask?.Invoke(true);
        }

        var core = MultiplyAffine(view, 0, 0, 0, (float)SolarSystemScene.SunRadius, 0, 0);
        int uMvp2 = gl.GetUniformLocationString(_sunProgram, "uMvp");
        int uModel2 = gl.GetUniformLocationString(_sunProgram, "uModel");
        fixed (float* mp = core.Item1, dp = core.Item2)
        {
            gl.UniformMatrix4fv(uMvp2, 1, false, mp);
            gl.UniformMatrix4fv(uModel2, 1, false, dp);
        }

        gl.Uniform1f(gl.GetUniformLocationString(_sunProgram, "uGlow"), 0f);
        DrawSphereGeometry(gl, _sunProgram);
    }

    private unsafe void DrawPlanet(GlInterface gl, float[] view, int bodyIndex, double px, double py, double pz, float radius, float opacity)
    {
        ref readonly var body = ref _scene.Bodies[bodyIndex];
        double spin = PlanetSpinPhase(in body, _scene.Time);

        gl.UseProgram(_bodyProgram);
        var (mvp, model) = MultiplyAffine(view, px, py, pz, radius, body.AxialTiltDeg, spin);
        int uMvp = gl.GetUniformLocationString(_bodyProgram, "uMvp");
        int uModel = gl.GetUniformLocationString(_bodyProgram, "uModel");
        fixed (float* mp = mvp, dp = model)
        {
            gl.UniformMatrix4fv(uMvp, 1, false, mp);
            gl.UniformMatrix4fv(uModel, 1, false, dp);
        }

        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorAR"), body.R);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorAG"), body.G);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorAB"), body.B);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorBR"), body.R * 0.55f + 0.08f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorBG"), body.G * 0.55f + 0.08f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uColorBB"), body.B * 0.55f + 0.08f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uSunX"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uSunY"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uSunZ"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uCamX"), (float)_cameraX);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uCamY"), (float)_cameraY);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uCamZ"), (float)_cameraZ);
        gl.Uniform1f(
            gl.GetUniformLocationString(_bodyProgram, "uBanding"),
            body.Kind == SolarSystemScene.PlanetKind.Banded || body.Kind == SolarSystemScene.PlanetKind.Ringed ? 1f : 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uTime"), (float)_scene.Time);
        gl.Uniform1f(gl.GetUniformLocationString(_bodyProgram, "uOpacity"), opacity);

        DrawSphereGeometry(gl, _bodyProgram);
    }

    private unsafe void DrawEarth(GlInterface gl, float[] view, double px, double py, double pz, float radius, double tiltDeg)
    {
        gl.UseProgram(_earthProgram);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uSunX"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uSunY"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uSunZ"), 0f);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uCamX"), (float)_cameraX);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uCamY"), (float)_cameraY);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uCamZ"), (float)_cameraZ);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uAccentR"), 0.23f);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uAccentG"), 0.82f);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uAccentB"), 0.91f);

        double dx = _cameraX - px;
        double dy = _cameraY - py;
        double dz = _cameraZ - pz;
        double camDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        SolarSystemScene.ComputeGisLod(camDist, radius, out float gisAmount, out float gisRefine);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uGisAmount"), gisAmount);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uGisRefine"), gisRefine);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uGisWidth"), _earthAlbedoWidth);
        gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uGisHeight"), _earthAlbedoHeight);

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _earthAlbedo);

        // Surface marble.
        {
            var (mvp, model) = MultiplyAffine(view, px, py, pz, radius, tiltDeg, SolarSystemDirector.EffectiveEarthSpinPhase);
            int uMvp = gl.GetUniformLocationString(_earthProgram, "uMvp");
            int uModel = gl.GetUniformLocationString(_earthProgram, "uModel");
            fixed (float* mp = mvp, dp = model)
            {
                gl.UniformMatrix4fv(uMvp, 1, false, mp);
                gl.UniformMatrix4fv(uModel, 1, false, dp);
            }

            gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uAtmosphere"), 0f);
            DrawSphereGeometry(gl, _earthProgram);
        }

        // Atmosphere when close enough that the limb reads (refine mid→high).
        if (gisRefine > 0.25f)
        {
            var (mvp, model) = MultiplyAffine(view, px, py, pz, radius * 1.045f, tiltDeg, SolarSystemDirector.EffectiveEarthSpinPhase);
            int uMvp = gl.GetUniformLocationString(_earthProgram, "uMvp");
            int uModel = gl.GetUniformLocationString(_earthProgram, "uModel");
            fixed (float* mp = mvp, dp = model)
            {
                gl.UniformMatrix4fv(uMvp, 1, false, mp);
                gl.UniformMatrix4fv(uModel, 1, false, dp);
            }

            gl.Uniform1f(gl.GetUniformLocationString(_earthProgram, "uAtmosphere"), 1f);
            _depthMask?.Invoke(false);
            DrawSphereGeometry(gl, _earthProgram);
            _depthMask?.Invoke(true);
        }
    }

    private unsafe void DrawSphereGeometry(GlInterface gl, int program)
    {
        int aPos = gl.GetAttribLocationString(program, "aPos");
        int aUv = gl.GetAttribLocationString(program, "aUv");
        gl.BindBuffer(GL_ARRAY_BUFFER, _sphereVbo);
        if (aPos >= 0)
        {
            gl.VertexAttribPointer(aPos, 3, GL_FLOAT, 0, 20, IntPtr.Zero);
            gl.EnableVertexAttribArray(aPos);
        }

        if (aUv >= 0)
        {
            gl.VertexAttribPointer(aUv, 2, GL_FLOAT, 0, 20, (IntPtr)12);
            gl.EnableVertexAttribArray(aUv);
        }

        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _sphereEbo);
        gl.DrawElements(GL_TRIANGLES, _sphereIndexCount, GL_UNSIGNED_SHORT, IntPtr.Zero);

        if (aUv >= 0)
            _disableVertexAttribArray?.Invoke(aUv);
        if (aPos >= 0)
            _disableVertexAttribArray?.Invoke(aPos);
    }

    private unsafe void DrawSaturnRing(
        GlInterface gl,
        float[] view,
        double px, double py, double pz,
        float radius,
        in SolarSystemScene.KeplerBody body,
        float opacity)
    {
        gl.UseProgram(_ringProgram);

        // Ring plane = planet equator (axial tilt), scaled by planet radius.
        double tilt = body.AxialTiltDeg * Math.PI / 180.0;
        double ct = Math.Cos(tilt);
        double st = Math.Sin(tilt);

        var model = new float[16];
        model[0] = radius; model[1] = 0; model[2] = 0; model[3] = 0;
        model[4] = 0; model[5] = (float)(radius * ct); model[6] = (float)(radius * st); model[7] = 0;
        model[8] = 0; model[9] = (float)(-radius * st); model[10] = (float)(radius * ct); model[11] = 0;
        model[12] = (float)px; model[13] = (float)py; model[14] = (float)pz; model[15] = 1;

        var mvp = new float[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += view[k * 4 + r] * model[c * 4 + k];
                mvp[c * 4 + r] = (float)sum;
            }
        }

        int uMvp = gl.GetUniformLocationString(_ringProgram, "uMvp");
        fixed (float* mp = mvp)
        {
            gl.UniformMatrix4fv(uMvp, 1, false, mp);
        }

        gl.Uniform1f(gl.GetUniformLocationString(_ringProgram, "uColorR"), body.R);
        gl.Uniform1f(gl.GetUniformLocationString(_ringProgram, "uColorG"), body.G);
        gl.Uniform1f(gl.GetUniformLocationString(_ringProgram, "uColorB"), body.B);
        gl.Uniform1f(gl.GetUniformLocationString(_ringProgram, "uSunSide"), 1f);
        gl.Uniform1f(gl.GetUniformLocationString(_ringProgram, "uOpacity"), opacity);

        _disable?.Invoke(GL_CULL_FACE);
        gl.Enable(GL_BLEND);
        _blendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        _depthMask?.Invoke(false);

        int aPos = gl.GetAttribLocationString(_ringProgram, "aPos");
        int aUv = gl.GetAttribLocationString(_ringProgram, "aUv");
        gl.BindBuffer(GL_ARRAY_BUFFER, _ringVbo);
        if (aPos >= 0)
        {
            gl.VertexAttribPointer(aPos, 3, GL_FLOAT, 0, 20, IntPtr.Zero);
            gl.EnableVertexAttribArray(aPos);
        }

        if (aUv >= 0)
        {
            gl.VertexAttribPointer(aUv, 2, GL_FLOAT, 0, 20, (IntPtr)12);
            gl.EnableVertexAttribArray(aUv);
        }

        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ringEbo);
        gl.DrawElements(GL_TRIANGLES, _ringIndexCount, GL_UNSIGNED_SHORT, IntPtr.Zero);

        if (aUv >= 0)
            _disableVertexAttribArray?.Invoke(aUv);
        if (aPos >= 0)
            _disableVertexAttribArray?.Invoke(aPos);

        _depthMask?.Invoke(true);
    }

    private static unsafe void BindVec2(GlInterface gl, int vbo, int program, string name, int stride, IntPtr offset)
    {
        int location = gl.GetAttribLocationString(program, name);
        if (location < 0)
            return;

        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        gl.VertexAttribPointer(location, 2, GL_FLOAT, 0, stride, offset);
        gl.EnableVertexAttribArray(location);
    }

    /// <summary>
    /// MVP = view · (T · Rx(tilt) · Ry(spin) · S(radius)); returns (mvp, model)
    /// both column-major float[16].
    /// </summary>
    private static (float[] Mvp, float[] Model) MultiplyAffine(
        float[] view,
        double px, double py, double pz,
        float radius,
        double tiltDeg,
        double spinPhase)
    {
        double tilt = tiltDeg * Math.PI / 180.0;
        double ct = Math.Cos(tilt);
        double st = Math.Sin(tilt);
        double cs = Math.Cos(spinPhase);
        double ss = Math.Sin(spinPhase);

        // Rotation rows for Rx(tilt)·Ry(spin):
        // [ cs     0    ss  ]
        // [ st·ss  ct  −st·cs ]
        // [ −ct·ss st   ct·cs ]
        double m00 = cs, m01 = 0, m02 = ss;
        double m10 = st * ss, m11 = ct, m12 = -st * cs;
        double m20 = -ct * ss, m21 = st, m22 = ct * cs;

        var model = new float[16];
        model[0] = (float)(m00 * radius); model[1] = (float)(m10 * radius); model[2] = (float)(m20 * radius); model[3] = 0;
        model[4] = (float)(m01 * radius); model[5] = (float)(m11 * radius); model[6] = (float)(m21 * radius); model[7] = 0;
        model[8] = (float)(m02 * radius); model[9] = (float)(m12 * radius); model[10] = (float)(m22 * radius); model[11] = 0;
        model[12] = (float)px; model[13] = (float)py; model[14] = (float)pz; model[15] = 1;

        var mvp = new float[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += view[k * 4 + r] * model[c * 4 + k];
                mvp[c * 4 + r] = (float)sum;
            }
        }

        return (mvp, model);
    }

    private void CleanupPartial(GlInterface gl)
    {
        try
        {
            if (_sphereVbo != 0) gl.DeleteBuffer(_sphereVbo);
            if (_sphereEbo != 0) gl.DeleteBuffer(_sphereEbo);
            if (_ringVbo != 0) gl.DeleteBuffer(_ringVbo);
            if (_ringEbo != 0) gl.DeleteBuffer(_ringEbo);
            if (_orbitVbo != 0) gl.DeleteBuffer(_orbitVbo);
            if (_starVbo != 0) gl.DeleteBuffer(_starVbo);
            if (_earthAlbedo != 0) gl.DeleteTexture(_earthAlbedo);
            foreach (int p in new[] { _bodyProgram, _sunProgram, _earthProgram, _ringProgram, _lineProgram, _starProgram })
                if (p != 0) gl.DeleteProgram(p);
        }
        catch
        {
        }
        finally
        {
            _sphereVbo = _sphereEbo = _ringVbo = _ringEbo = _orbitVbo = _starVbo = 0;
            _earthAlbedo = 0;
            _bodyProgram = _sunProgram = _earthProgram = _ringProgram = _lineProgram = _starProgram = 0;
        }
    }

    protected override void OnOpenGlLost()
    {
        _sphereVbo = _sphereEbo = _ringVbo = _ringEbo = _orbitVbo = _starVbo = 0;
        _earthAlbedo = 0;
        _bodyProgram = _sunProgram = _earthProgram = _ringProgram = _lineProgram = _starProgram = 0;
        _hasRendered = false;
        _failed = false;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_failed)
            return;

        CleanupPartial(gl);
        _hasRendered = false;
    }
}
