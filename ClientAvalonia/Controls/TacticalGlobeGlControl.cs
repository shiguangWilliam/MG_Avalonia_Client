using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Rampastring.Tools;

namespace ClientAvalonia.Controls;

/// <summary>
/// OpenGL globe: a static UV sphere (uploaded once) textured with the baked
/// equirectangular world_map. Every frame only updates the yaw/pitch uniform
/// and re-issues the draw call — no CPU rasterization, no WriteableBitmap.
/// The projection replicates the legacy formula (screen = c ± xyz · radius ·
/// F/(F−z), camera on +z at F=3.4, model = Rx(pitch)·Ry(yaw)) so the Avalonia
/// anchor overlay stays registered with texture continents. The shared
/// composition FBO carries no MSAA, so silhouette smoothing is done in the
/// fragment shader (premultiplied edge feather) instead.
/// </summary>
public sealed class TacticalGlobeGlControl : OpenGlControlBase
{
    // Camera model must stay identical to TacticalGlobeView's overlay math.
    private const double FocalFactor = 3.4;
    private const double RadiusFactor = 0.44;
    private const double Near = 2.3;
    private const double Far = 4.5;

    // Local GL constants (standard values) — avoids depending on which
    // members Avalonia.OpenGL.GlConsts happens to expose.
    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_FLOAT = 0x1406;
    private const int GL_UNSIGNED_SHORT = 0x1403;
    private const int GL_UNSIGNED_BYTE = 0x1401;
    private const int GL_TRIANGLES = 0x0004;
    private const int GL_TEXTURE0 = 0x84C0;
    private const int GL_RGBA = 0x1908;
    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_REPEAT = 0x2901;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_LINEAR = 0x2601;
    private const int GL_DEPTH_TEST = 0x0B71;
    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_COLOR_BUFFER_BIT = 0x4000;
    private const int GL_DEPTH_BUFFER_BIT = 0x0100;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;

    private const string VertexShader = @"
attribute vec3 aPos;
attribute vec2 aUv;
uniform mat4 uMvp;
uniform mat4 uModel;
varying vec2 vUv;
varying vec3 vNormal;

void main()
{
    vUv = aUv;
    // Model has no translation, so w=0 extracts the rotation for normals.
    vNormal = (uModel * vec4(aPos, 0.0)).xyz;
    gl_Position = uMvp * vec4(aPos, 1.0);
}
";

    private const string FragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif

varying vec2 vUv;
varying vec3 vNormal;

uniform sampler2D uAlbedo;
uniform float uAccentR;
uniform float uAccentG;
uniform float uAccentB;

void main()
{
    vec3 n = normalize(vNormal);
    vec3 albedo = texture2D(uAlbedo, vUv).rgb;

    // Key light identical to the legacy CPU shader (camera space).
    vec3 key = vec3(0.45, 0.35, 0.82);
    float ndl = max(dot(n, key), 0.0);
    float view = clamp(n.z, 0.0, 1.0);
    float lum = (0.20 + 0.95 * ndl) * (0.35 + 0.65 * view);

    vec3 col = albedo * lum;

    // Atmosphere rim in the theme accent color.
    float rim = pow(1.0 - view, 3.0);
    col += vec3(uAccentR, uAccentG, uAccentB) * rim * 0.35;

    // Silhouette feather (premultiplied alpha; no MSAA on the shared FBO).
    float alpha = smoothstep(0.0, 0.22, view);
    gl_FragColor = vec4(col * alpha, alpha);
}
";

    // F2 border highlight: unlit emissive lines riding 0.2% above the surface.
    private const string BorderVertexShader = @"
attribute vec3 aPos;
uniform mat4 uMvp;
void main()
{
    gl_Position = uMvp * vec4(aPos, 1.0);
}
";

    private const string BorderFragmentShader = @"
#ifdef GL_ES
precision mediump float;
#endif
uniform float uBorderColorR;
uniform float uBorderColorG;
uniform float uBorderColorB;
uniform float uBorderAlpha;
void main()
{
    gl_FragColor = vec4(uBorderColorR * uBorderAlpha, uBorderColorG * uBorderAlpha, uBorderColorB * uBorderAlpha, uBorderAlpha);
}
";

    private int _program;
    private int _vbo;
    private int _ebo;
    private int _indexCount;
    private int _albedo;
    private int _uMvp;
    private int _uModel;
    private int _uAccentR;
    private int _uAccentG;
    private int _uAccentB;
    private int _aPos;
    private int _aUv;

    // F2 border highlight state.
    private int _borderProgram;
    private int _borderVbo;
    private int _borderVa;
    private int _borderUvp;
    private int _borderUColorR;
    private int _borderUColorG;
    private int _borderUColorB;
    private int _borderUAlpha;
    private int[]? _borderRingOffsets;
    private int[]? _borderRingVertexCounts;
    private string? _borderCountry;
    private float _borderTargetAlpha;
    private float _borderCurrentAlpha;
    private long _borderFadeStartTicks;
    private bool _borderFadeActive;

    // GlInterface does not wrap these entry points; bind manually via
    // GetProcAddress (Winapi matches ANGLE's WINAPI exports).
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]
    private delegate void GlBlendFunc(int sfactor, int dfactor);

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]
    private delegate void GlDisableVertexAttribArray(int index);

    private GlBlendFunc? _blendFunc;
    private GlDisableVertexAttribArray? _disableVertexAttribArray;

    private const double BorderElevation = 1.002;
    private const int GL_LINE_STRIP = 0x0003;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;

    private double _yawDeg = 20.0;
    private double _pitchDeg = -16.0;
    private readonly float[] _accent = { 0.18f, 0.90f, 0.77f };
    private bool _failed;
    private bool _hasRendered;

    /// <summary>True once at least one frame reached the GPU — lets the overlay drop its fallback disc.</summary>
    public bool HasRendered => _hasRendered;

    /// <summary>Shader/resource creation failed; the overlay fallback stays visible.</summary>
    public bool IsContentFailed => _failed;

    /// <summary>Current pose in degrees; pushing a value re-renders one frame.</summary>
    public (double Yaw, double Pitch) Pose
    {
        get => (_yawDeg, _pitchDeg);
        set
        {
            _yawDeg = value.Yaw;
            _pitchDeg = value.Pitch;
            RequestNextFrameRendering();
        }
    }

    /// <summary>Theme accent (RGB 0-1) for the atmosphere rim.</summary>
    public void SetAccent(float r, float g, float b)
    {
        _accent[0] = r;
        _accent[1] = g;
        _accent[2] = b;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// F2: sets the highlighted country (ISO A2/A3 or null to clear). Ring data
    /// is quantized (qLon, qLat) and decoded to Dir() unit vectors here so the
    /// lines share the sphere's exact geometry. Re-render is driven by the fade
    /// loop in the host view.
    /// </summary>
    public void SetHighlightedCountry(string? code)
    {
        // GlobeBorderLibrary.CountryBordersEnabled = false hides all country
        // borders this build (data is wrong); treat every code as "none".
        if (!GlobeBorderLibrary.CountryBordersEnabled)
            code = null;

        if (code == _borderCountry)
            return;

        _borderCountry = code;
        _borderRingsStaged = null; // rebuilt lazily inside the GL context
        _borderFadeActive = true;
        _borderFadeStartTicks = Environment.TickCount64;
    }

    // Ring data staged for upload inside the render callback (uploads must run
    // with a current GL context; the setter may be called from the UI thread).
    private ushort[][]? _borderRingsStaged;

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            // Drain stale errors from earlier platform code so the final
            // check only reflects this control's own commands.
            while (gl.GetError() != 0)
            {
            }
            int vs = gl.CreateShader(GL_VERTEX_SHADER);
            string? vsErr = gl.CompileShaderAndGetError(vs, VertexShader);
            if (vsErr != null)
                throw new InvalidOperationException("vertex shader: " + vsErr);

            int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
            string? fsErr = gl.CompileShaderAndGetError(fs, FragmentShader);
            if (fsErr != null)
                throw new InvalidOperationException("fragment shader: " + fsErr);

            _program = gl.CreateProgram();
            gl.AttachShader(_program, vs);
            gl.AttachShader(_program, fs);
            gl.BindAttribLocationString(_program, 0, "aPos");
            gl.BindAttribLocationString(_program, 1, "aUv");
            string? linkErr = gl.LinkProgramAndGetError(_program);
            if (linkErr != null)
                throw new InvalidOperationException("link: " + linkErr);

            gl.DeleteShader(vs);
            gl.DeleteShader(fs);

            _aPos = gl.GetAttribLocationString(_program, "aPos");
            _aUv = gl.GetAttribLocationString(_program, "aUv");
            _uMvp = gl.GetUniformLocationString(_program, "uMvp");
            _uModel = gl.GetUniformLocationString(_program, "uModel");
            _uAccentR = gl.GetUniformLocationString(_program, "uAccentR");
            _uAccentG = gl.GetUniformLocationString(_program, "uAccentG");
            _uAccentB = gl.GetUniformLocationString(_program, "uAccentB");

            // ---- UV sphere: 48 slices x 32 stacks, uploaded once ----
            // Vertex layout: pos.xyz + uv (5 floats). Orientation matches the
            // legacy Dir(): x = cos(lat)sin(lon), y = sin(lat), z = cos(lat)cos(lon).
            const int slices = 48;
            const int stacks = 32;
            var verts = new float[(slices + 1) * (stacks + 1) * 5];
            int vi = 0;
            for (int st = 0; st <= stacks; st++)
            {
                double v = st / (double)stacks;
                double lat = (90.0 - v * 180.0) * Math.PI / 180.0; // v=0 → north pole
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

            _indexCount = indices.Length;

            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (float* vp = verts)
            {
                gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(verts.Length * sizeof(float)), (IntPtr)vp, GL_STATIC_DRAW);
            }

            _ebo = gl.GenBuffer();
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);
            fixed (ushort* ip = indices)
            {
                gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)(indices.Length * sizeof(ushort)), (IntPtr)ip, GL_STATIC_DRAW);
            }

            // ---- Albedo: baked equirectangular map, uploaded once ----
            if (!GlobeTextureBaker.TryGetPixels(out byte[] pixels, out int tw, out int th))
            {
                // Defensive: the baker always yields something (procedural
                // fallback); keep a 2x1 swatch in case of a race.
                tw = 2;
                th = 1;
                pixels = new byte[] { 20, 34, 46, 255, 20, 34, 46, 255 };
            }

            _albedo = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, _albedo);
            fixed (byte* pp = pixels)
            {
                gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, tw, th, 0, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)pp);
            }

            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            // world_map is 1728x850 (NPOT); OpenGL ES only allows CLAMP_TO_EDGE
            // on NPOT textures — REPEAT would leave the texture incomplete
            // (black). Revisit with a power-of-two 2K asset.
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

            // Sampler uniforms must be set with glUniform1i (not exposed by
            // GlInterface); the default sampler value is already 0 and every
            // draw binds the albedo to GL_TEXTURE0, so no explicit set is done
            // — calling Uniform1f here raises GL_INVALID_OPERATION.

            gl.Enable(GL_DEPTH_TEST);

            // ---- F2 border program (own tiny shaders; the main program's
            // fragment samples the albedo, which borders must not) ----
            int bvs = gl.CreateShader(GL_VERTEX_SHADER);
            string? bvsErr = gl.CompileShaderAndGetError(bvs, BorderVertexShader);
            if (bvsErr != null)
                throw new InvalidOperationException("border vertex shader: " + bvsErr);

            int bfs = gl.CreateShader(GL_FRAGMENT_SHADER);
            string? bfsErr = gl.CompileShaderAndGetError(bfs, BorderFragmentShader);
            if (bfsErr != null)
                throw new InvalidOperationException("border fragment shader: " + bfsErr);

            _borderProgram = gl.CreateProgram();
            gl.AttachShader(_borderProgram, bvs);
            gl.AttachShader(_borderProgram, bfs);
            gl.BindAttribLocationString(_borderProgram, 0, "aPos");
            string? bLinkErr = gl.LinkProgramAndGetError(_borderProgram);
            if (bLinkErr != null)
                throw new InvalidOperationException("border link: " + bLinkErr);

            gl.DeleteShader(bvs);
            gl.DeleteShader(bfs);

            _borderVa = gl.GetAttribLocationString(_borderProgram, "aPos");
            _borderUvp = gl.GetUniformLocationString(_borderProgram, "uMvp");
            _borderUColorR = gl.GetUniformLocationString(_borderProgram, "uBorderColorR");
            _borderUColorG = gl.GetUniformLocationString(_borderProgram, "uBorderColorG");
            _borderUColorB = gl.GetUniformLocationString(_borderProgram, "uBorderColorB");
            _borderUAlpha = gl.GetUniformLocationString(_borderProgram, "uBorderAlpha");

            _borderVbo = gl.GenBuffer();

            // Bind the two entry points GlInterface does not wrap. Null on
            // ancient drivers — then borders draw without blending (still
            // readable) and attrib arrays stay enabled harmlessly.
            _blendFunc = Marshal.GetDelegateForFunctionPointer<GlBlendFunc>(gl.GetProcAddress("glBlendFunc"));
            _disableVertexAttribArray = Marshal.GetDelegateForFunctionPointer<GlDisableVertexAttribArray>(
                gl.GetProcAddress("glDisableVertexAttribArray"));

            gl.Enable(GL_BLEND);
            _blendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

            if (gl.GetError() != 0)
                throw new InvalidOperationException("GL error during init");

            _failed = false;
            Logger.Log($"TacticalGlobeGlControl: GL globe ready (albedo {tw}x{th}).");
        }
        catch (Exception ex)
        {
            _failed = true;
            Logger.Log($"TacticalGlobeGlControl: GL init failed — {ex.Message}");
            CleanupPartial(gl);
        }
    }

    /// <summary>Best-effort deletion of whatever was created before the failure.</summary>
    private void CleanupPartial(GlInterface gl)
    {
        try
        {
            if (_vbo != 0)
                gl.DeleteBuffer(_vbo);
            if (_ebo != 0)
                gl.DeleteBuffer(_ebo);
            if (_albedo != 0)
                gl.DeleteTexture(_albedo);
            if (_program != 0)
                gl.DeleteProgram(_program);
            if (_borderVbo != 0)
                gl.DeleteBuffer(_borderVbo);
            if (_borderProgram != 0)
                gl.DeleteProgram(_borderProgram);
        }
        catch
        {
            // Context is unusable; nothing more to free.
        }
        finally
        {
            _program = _vbo = _ebo = _albedo = 0;
            _borderProgram = _borderVbo = 0;
        }
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int w = Math.Max(1, (int)(Bounds.Width * scaling));
        int h = Math.Max(1, (int)(Bounds.Height * scaling));

        if (_failed)
        {
            // Leave the surface transparent so the host's fallback disc shows.
            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
            gl.Viewport(0, 0, w, h);
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
            return;
        }

        gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        gl.UseProgram(_program);

        float[] mvp = BuildMvp(w, h, out float[] model);
        fixed (float* mvpP = mvp, modelP = model)
        {
            gl.UniformMatrix4fv(_uMvp, 1, false, mvpP);
            gl.UniformMatrix4fv(_uModel, 1, false, modelP);
        }

        gl.Uniform1f(_uAccentR, _accent[0]);
        gl.Uniform1f(_uAccentG, _accent[1]);
        gl.Uniform1f(_uAccentB, _accent[2]);

        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);

        gl.VertexAttribPointer(_aPos, 3, GL_FLOAT, 0, 20, IntPtr.Zero);
        gl.EnableVertexAttribArray(_aPos);
        gl.VertexAttribPointer(_aUv, 2, GL_FLOAT, 0, 20, (IntPtr)12);
        gl.EnableVertexAttribArray(_aUv);

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _albedo);

        gl.DrawElements(GL_TRIANGLES, _indexCount, GL_UNSIGNED_SHORT, IntPtr.Zero);

        DrawBorderLayer(gl, mvp);

        _hasRendered = true;
    }

    /// <summary>
    /// F2 border pass. On country change the rings are fetched, decoded to
    /// Dir() vertices and uploaded once; afterwards only the alpha uniform
    /// animates toward the target (fade 220ms in, 260ms out).
    /// </summary>
    private unsafe void DrawBorderLayer(GlInterface gl, float[] mvp)
    {
        // Fetch new geometry lazily (SetHighlightedCountry may run off-thread).
        if (_borderRingsStaged is null && _borderCountry != null)
        {
            ushort[][]? rings = GlobeBorderLibrary.TryGetRings(_borderCountry);
            if (rings is null)
            {
                // Unknown country: drop it so we do not retry every frame.
                _borderCountry = null;
                _borderTargetAlpha = 0;
            }
            else
            {
                UploadBorderGeometry(gl, rings);
                _borderRingsStaged = rings;
                _borderTargetAlpha = 1;
                _borderFadeActive = true;
                _borderFadeStartTicks = Environment.TickCount64;
            }
        }

        if (_borderCountry is null && _borderRingOffsets != null && _borderTargetAlpha != 0)
        {
            _borderTargetAlpha = 0;
            _borderFadeActive = true;
            _borderFadeStartTicks = Environment.TickCount64;
        }

        if (_borderRingOffsets is null)
            return;

        // Ease the alpha toward the target; keep re-rendering until settled.
        if (_borderFadeActive)
        {
            double elapsed = (Environment.TickCount64 - _borderFadeStartTicks) / 1000.0;
            double duration = _borderTargetAlpha > _borderCurrentAlpha ? 0.22 : 0.26;
            double t = duration <= 0 ? 1.0 : Math.Clamp(elapsed / duration, 0.0, 1.0);
            double eased = t * t * (3.0 - 2.0 * t); // smoothstep
            _borderCurrentAlpha = (float)(_borderCurrentAlpha + (_borderTargetAlpha - _borderCurrentAlpha) * eased);

            if (t >= 1.0 || Math.Abs(_borderTargetAlpha - _borderCurrentAlpha) < 0.01f)
            {
                _borderCurrentAlpha = _borderTargetAlpha;
                _borderFadeActive = false;
            }

            RequestNextFrameRendering();
        }

        if (_borderCurrentAlpha <= 0.01f)
            return;

        gl.UseProgram(_borderProgram);
        fixed (float* mvpP = mvp)
        {
            gl.UniformMatrix4fv(_borderUvp, 1, false, mvpP);
        }

        gl.Uniform1f(_borderUColorR, _accent[0]);
        gl.Uniform1f(_borderUColorG, _accent[1]);
        gl.Uniform1f(_borderUColorB, _accent[2]);
        gl.Uniform1f(_borderUAlpha, _borderCurrentAlpha);

        gl.BindBuffer(GL_ARRAY_BUFFER, _borderVbo);
        gl.VertexAttribPointer(_borderVa, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(_borderVa);

        for (int i = 0; i < _borderRingVertexCounts!.Length; i++)
        {
            gl.DrawArrays(GL_LINE_STRIP, _borderRingOffsets![i], (IntPtr)_borderRingVertexCounts[i]);
        }

        _disableVertexAttribArray?.Invoke(_borderVa);
    }

    private unsafe void UploadBorderGeometry(GlInterface gl, ushort[][] rings)
    {
        var verts = new List<float>(rings.Length * 64);
        var offsets = new int[rings.Length];
        var counts = new int[rings.Length];

        for (int r = 0; r < rings.Length; r++)
        {
            ushort[] ring = rings[r];
            int n = ring.Length / 2;
            offsets[r] = verts.Count / 3;
            counts[r] = n;

            for (int i = 0; i < n; i++)
            {
                // Same quantization as GeoConvert/GlobeVectorBaker.
                double lon = ring[2 * i] / 65535.0 * 360.0 - 180.0;
                double lat = ring[2 * i + 1] / 65535.0 * 180.0 - 90.0;
                double latRad = lat * Math.PI / 180.0;
                double lonRad = lon * Math.PI / 180.0;
                double cl = Math.Cos(latRad);
                verts.Add((float)(cl * Math.Sin(lonRad) * BorderElevation));
                verts.Add((float)(Math.Sin(latRad) * BorderElevation));
                verts.Add((float)(cl * Math.Cos(lonRad) * BorderElevation));
            }
        }

        var arr = verts.ToArray();
        gl.BindBuffer(GL_ARRAY_BUFFER, _borderVbo);
        fixed (float* p = arr)
        {
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(arr.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);
        }

        _borderRingOffsets = offsets;
        _borderRingVertexCounts = counts;
    }

    protected override void OnOpenGlLost()
    {
        // Context is gone; forget handles so the next init rebuilds them.
        _program = _vbo = _ebo = _albedo = 0;
        _borderProgram = _borderVbo = 0;
        _borderRingOffsets = null;
        _borderRingVertexCounts = null;
        _borderRingsStaged = null; // force re-upload after context restore
        _hasRendered = false;
        _failed = false;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_failed || _program == 0)
            return;

        if (_vbo != 0)
            gl.DeleteBuffer(_vbo);
        if (_ebo != 0)
            gl.DeleteBuffer(_ebo);
        if (_albedo != 0)
            gl.DeleteTexture(_albedo);
        gl.DeleteProgram(_program);
        if (_borderVbo != 0)
            gl.DeleteBuffer(_borderVbo);
        if (_borderProgram != 0)
            gl.DeleteProgram(_borderProgram);

        _program = _vbo = _ebo = _albedo = 0;
        _borderProgram = _borderVbo = 0;
        _borderRingOffsets = null;
        _borderRingVertexCounts = null;
        _hasRendered = false;
    }

    /// <summary>
    /// MVP = P·V·M as a column-major float[16] (shader-side column vectors,
    /// transpose=false). P maps to ndc.x = (2RF/w)·x/(F−z), ndc.y mirrored on
    /// y — the exact legacy projection; V is the camera at (0,0,F).
    /// </summary>
    private float[] BuildMvp(int w, int h, out float[] model)
    {
        double radius = RadiusFactor * Math.Min(w, h);
        double p11 = 2.0 * radius * FocalFactor / w;
        double p22 = 2.0 * radius * FocalFactor / h;
        double p33 = -(Far + Near) / (Far - Near);
        double p34 = -2.0 * Far * Near / (Far - Near);

        double yaw = _yawDeg * Math.PI / 180.0;
        double pitch = _pitchDeg * Math.PI / 180.0;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);

        // Model M = Rx(pitch)·Ry(yaw) (math rows):
        // [ cy       0     sy    ]
        // [ sp·sy    cp    −sp·cy ]
        // [ −cp·sy   sp    cp·cy  ]
        model = new[]
        {
            (float)cy, (float)(sp * sy), (float)(-cp * sy), 0f,
            0f, (float)cp, (float)sp, 0f,
            (float)sy, (float)(-sp * cy), (float)(cp * cy), 0f,
            0f, 0f, 0f, 1f,
        };

        // MVP math rows:
        // R0 = (A·cy, 0, A·sy, 0)
        // R1 = (B·sp·sy, B·cp, −B·sp·cy, 0)
        // R2 = (−C·cp·sy, C·sp, C·cp·cy, −C·F + D)
        // R3 = (cp·sy, −sp, −cp·cy, F)
        return new[]
        {
            (float)(p11 * cy), (float)(p22 * sp * sy), (float)(-p33 * cp * sy), (float)(cp * sy),
            0f, (float)(p22 * cp), (float)(p33 * sp), (float)(-sp),
            (float)(p11 * sy), (float)(-p22 * sp * cy), (float)(p33 * cp * cy), (float)(-cp * cy),
            0f, 0f, (float)(p34 - p33 * FocalFactor), (float)FocalFactor,
        };
    }
}
