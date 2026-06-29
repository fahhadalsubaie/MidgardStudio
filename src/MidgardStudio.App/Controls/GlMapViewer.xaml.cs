using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MidgardStudio.Grf;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Wpf;

namespace MidgardStudio.App.Controls;

/// <summary>
/// OpenGL host for the 3D map preview (GND terrain; RSW models + water come in later slices). Renders the
/// baked <see cref="MapGeometry"/> terrain — textured + lightmap-shaded — with an orbit/pan/zoom camera.
/// </summary>
public partial class GlMapViewer : UserControl
{
    public static readonly DependencyProperty MapProperty = DependencyProperty.Register(
        nameof(Map), typeof(MapGeometry), typeof(GlMapViewer), new PropertyMetadata(null, OnMapChanged));

    public MapGeometry? Map
    {
        get => (MapGeometry?)GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    private sealed record Sub(int Vao, int Vbo, int Count, int Tex);
    private sealed record ModelSub(int Vao, int Vbo, int Count, int Tex, int MeshIndex);
    private sealed record ModelInst(List<ModelSub> Subs, List<Matrix4[]> MeshMatrices); // MeshMatrices[instance][meshIndex]

    private bool _started, _ready, _dirty;
    private int _program, _vpLoc, _texLoc, _hasTexLoc, _wireLoc, _wireColLoc, _ambLoc, _difLoc, _ldirLoc;
    private int _mProgram, _mVpLoc, _mModelLoc, _mAmbLoc, _mDifLoc, _mLdirLoc, _mTexLoc, _mHasTexLoc;
    private readonly List<Sub> _subs = new();
    private readonly List<ModelInst> _models = new();
    private MapGeometry? _pending;

    // lighting (from RSW)
    private Vector3 _lightDir = new(0, 1, 0), _ambient = Vector3.One, _diffuse = Vector3.One;

    // water
    private int _wProgram, _wVpLoc, _wTimeLoc, _wAmpLoc, _wSpeedLoc, _wPitchLoc, _wTexLoc;
    private int _waterVao, _waterVbo, _waterCount, _waterCycling = 3;
    private float _waterAmp = 1f, _waterSpeed = 2f, _waterPitch = 50f;
    private int[] _waterFrames = Array.Empty<int>();
    private double _elapsedMs;

    private float _yaw = 45f, _pitch = 40f, _distance = 500f;
    private Vector3 _target = Vector3.Zero;
    private Point _lastMouse;
    private MouseButton _dragButton;
    private bool _dragging;

    private Vector3 _bg = new(0.10f, 0.10f, 0.13f);
    private bool _wireframe;

    public GlMapViewer()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_started) return;
            _started = true;
            Surface.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3, RenderContinuously = true });
        };
        Unloaded += (_, _) => ReleaseGl();
        Surface.MouseDown += OnMouseDown;
        Surface.MouseUp += OnMouseUp;
        Surface.MouseMove += OnMouseMove;
        Surface.MouseWheel += OnMouseWheel;
    }

    private static void OnMapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (GlMapViewer)d;
        v._pending = e.NewValue as MapGeometry;
        v._dirty = true;
        if (v._pending is { } m)
        {
            v._lightDir = new Vector3(m.LightDir[0], m.LightDir[1], m.LightDir[2]);
            v._ambient = new Vector3(m.Ambient[0], m.Ambient[1], m.Ambient[2]);
            v._diffuse = new Vector3(m.Diffuse[0], m.Diffuse[1], m.Diffuse[2]);
        }
        v.FrameCamera(v._pending);
    }

    private void FrameCamera(MapGeometry? m)
    {
        if (m is null) return;
        _target = new Vector3(m.Center[0], m.Center[1], m.Center[2]);
        float r = MathF.Max(m.Radius, 1f);
        _distance = r / MathF.Sin(MathHelper.DegreesToRadians(45f) * 0.5f) * 1.4f;
        _yaw = 45f; _pitch = 40f;
    }

    private void OnReady()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        _program = BuildProgram(VertSrc, FragSrc);
        _vpLoc = GL.GetUniformLocation(_program, "vp");
        _texLoc = GL.GetUniformLocation(_program, "uTex");
        _hasTexLoc = GL.GetUniformLocation(_program, "uHasTexture");
        _wireLoc = GL.GetUniformLocation(_program, "uWire");
        _wireColLoc = GL.GetUniformLocation(_program, "uWireColor");
        _ambLoc = GL.GetUniformLocation(_program, "uAmbient");
        _difLoc = GL.GetUniformLocation(_program, "uDiffuse");
        _ldirLoc = GL.GetUniformLocation(_program, "uLightDir");

        _mProgram = BuildProgram(ModelVertSrc, ModelFragSrc);
        _mVpLoc = GL.GetUniformLocation(_mProgram, "vp");
        _mModelLoc = GL.GetUniformLocation(_mProgram, "m");
        _mAmbLoc = GL.GetUniformLocation(_mProgram, "uAmbient");
        _mDifLoc = GL.GetUniformLocation(_mProgram, "uDiffuse");
        _mLdirLoc = GL.GetUniformLocation(_mProgram, "uLightDir");
        _mTexLoc = GL.GetUniformLocation(_mProgram, "uTex");
        _mHasTexLoc = GL.GetUniformLocation(_mProgram, "uHasTexture");

        _wProgram = BuildProgram(WaterVertSrc, WaterFragSrc);
        _wVpLoc = GL.GetUniformLocation(_wProgram, "vp");
        _wTimeLoc = GL.GetUniformLocation(_wProgram, "uTime");
        _wAmpLoc = GL.GetUniformLocation(_wProgram, "uAmp");
        _wSpeedLoc = GL.GetUniformLocation(_wProgram, "uSpeed");
        _wPitchLoc = GL.GetUniformLocation(_wProgram, "uPitch");
        _wTexLoc = GL.GetUniformLocation(_wProgram, "uTex");
        _ready = true;
    }

    private void OnRender(TimeSpan delta)
    {
        _elapsedMs += delta.TotalMilliseconds;
        if (_ready && _dirty) Upload();

        GL.ClearColor(_bg.X, _bg.Y, _bg.Z, 1f);
        GL.PolygonMode(MaterialFace.FrontAndBack, _wireframe ? PolygonMode.Line : PolygonMode.Fill);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (_program != 0 && (_subs.Count > 0 || _models.Count > 0 || _waterCount > 0))
        {
            float aspect = (float)Math.Max(1.0, Surface.ActualWidth) / (float)Math.Max(1.0, Surface.ActualHeight);
            Vector3 dir = SphericalDir(_yaw, _pitch);
            Vector3 eye = _target + dir * _distance;
            Matrix4 view = Matrix4.LookAt(eye, _target, Vector3.UnitY);
            float near = MathF.Max(_distance * 0.01f, 0.1f);
            float far = _distance * 4f + MathF.Max(_pending?.Radius ?? 0, 1f) * 6f + 5000f;
            Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), aspect, near, far);
            Matrix4 vp = view * proj;

            // terrain
            if (_subs.Count > 0)
            {
                GL.UseProgram(_program);
                GL.UniformMatrix4(_vpLoc, true, ref vp);
                GL.Uniform1(_wireLoc, _wireframe ? 1 : 0);
                GL.Uniform3(_wireColLoc, new Vector3(0.8f, 0.85f, 0.9f));
                GL.Uniform3(_ldirLoc, ref _lightDir);
                GL.Uniform3(_ambLoc, ref _ambient);
                GL.Uniform3(_difLoc, ref _diffuse);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Uniform1(_texLoc, 0);
                foreach (var s in _subs)
                {
                    int hasTex = !_wireframe && s.Tex != 0 ? 1 : 0;
                    GL.Uniform1(_hasTexLoc, hasTex);
                    if (hasTex == 1) GL.BindTexture(TextureTarget.Texture2D, s.Tex);
                    GL.BindVertexArray(s.Vao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, s.Count);
                }
            }

            // placed models
            if (_models.Count > 0)
            {
                GL.UseProgram(_mProgram);
                GL.UniformMatrix4(_mVpLoc, true, ref vp);
                GL.Uniform3(_mLdirLoc, ref _lightDir);
                GL.Uniform3(_mAmbLoc, ref _ambient);
                GL.Uniform3(_mDifLoc, ref _diffuse);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Uniform1(_mTexLoc, 0);
                foreach (var inst in _models)
                {
                    foreach (var mats in inst.MeshMatrices)
                    {
                        int lastMesh = -1;
                        foreach (var s in inst.Subs)
                        {
                            if (s.MeshIndex != lastMesh)
                            {
                                Matrix4 mm = s.MeshIndex < mats.Length ? mats[s.MeshIndex] : Matrix4.Identity;
                                GL.UniformMatrix4(_mModelLoc, true, ref mm);
                                lastMesh = s.MeshIndex;
                            }
                            int hasTex = !_wireframe && s.Tex != 0 ? 1 : 0;
                            GL.Uniform1(_mHasTexLoc, hasTex);
                            if (hasTex == 1) GL.BindTexture(TextureTarget.Texture2D, s.Tex);
                            GL.BindVertexArray(s.Vao);
                            GL.DrawArrays(PrimitiveType.Triangles, 0, s.Count);
                        }
                    }
                }
            }

            // animated water (transparent, drawn last; depth-tested so terrain occludes it)
            if (_waterCount > 0 && _waterFrames.Length > 0 && !_wireframe)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);
                GL.UseProgram(_wProgram);
                GL.UniformMatrix4(_wVpLoc, true, ref vp);
                float t = (float)(_elapsedMs / 1000.0);
                GL.Uniform1(_wTimeLoc, t);
                GL.Uniform1(_wAmpLoc, _waterAmp);
                GL.Uniform1(_wSpeedLoc, _waterSpeed);
                GL.Uniform1(_wPitchLoc, _waterPitch);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.Uniform1(_wTexLoc, 0);
                int frame = (int)(t * 60.0 / Math.Max(1, _waterCycling)) % _waterFrames.Length;
                GL.BindTexture(TextureTarget.Texture2D, _waterFrames[frame]);
                GL.BindVertexArray(_waterVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, _waterCount);
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
            }
            GL.BindVertexArray(0);
        }
    }

    private void Upload()
    {
        _dirty = false;
        DeleteBuffers();
        var m = _pending;
        if (m is null) return;

        const int stride = MapSubmesh.Stride * sizeof(float);
        foreach (var sm in m.Terrain)
        {
            if (sm.Vertices.Length == 0) continue;
            int vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            int vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sm.Vertices.Length * sizeof(float), sm.Vertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            _subs.Add(new Sub(vao, vbo, sm.VertexCount, CreateTexture(sm.Texture)));
        }
        foreach (var inst in m.Models) BuildModelInst(inst);
        if (m.Water is { } water && water.VertexCount > 0) BuildWater(water);
        GL.BindVertexArray(0);
    }

    private void BuildWater(MapWater w)
    {
        const int stride = MapWater.Stride * sizeof(float);
        _waterVao = GL.GenVertexArray();
        GL.BindVertexArray(_waterVao);
        _waterVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _waterVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, w.Vertices.Length * sizeof(float), w.Vertices, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        _waterCount = w.VertexCount;
        _waterCycling = w.TextureCycling;
        _waterAmp = w.WaveHeight; _waterSpeed = w.WaveSpeed; _waterPitch = w.WavePitch;
        _waterFrames = new int[w.Frames.Count];
        for (int i = 0; i < w.Frames.Count; i++) _waterFrames[i] = CreateTexture(w.Frames[i]);
    }

    private void BuildModelInst(MapModelInstance inst)
    {
        var model = inst.Model;
        if (model.Meshes.Count == 0 || inst.Transforms.Count == 0) return;

        var subs = new List<ModelSub>();
        const int stride = ModelSubmesh.Stride * sizeof(float);
        for (int mi = 0; mi < model.Meshes.Count; mi++)
        {
            foreach (var sm in model.Meshes[mi].Submeshes)
            {
                if (sm.Vertices.Length == 0) continue;
                int vao = GL.GenVertexArray();
                GL.BindVertexArray(vao);
                int vbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, sm.Vertices.Length * sizeof(float), sm.Vertices, BufferUsageHint.StaticDraw);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
                GL.EnableVertexAttribArray(2);
                subs.Add(new ModelSub(vao, vbo, sm.VertexCount, CreateTexture(sm.Texture), mi));
            }
        }

        // rest pose without the GL flip — the instance matrix carries the flip + world placement
        var rest = RsmPose.ComputeWorldMatrices(
            model.Meshes.Select(mm => mm.Transform).ToList(), model.Version, model.AnimationLength, 0.0, applyFlip: false);

        var meshMats = new List<Matrix4[]>(inst.Transforms.Count);
        foreach (var t in inst.Transforms)
        {
            Matrix4 instM = ToMatrix(t);
            var arr = new Matrix4[rest.Length];
            for (int i = 0; i < rest.Length; i++) arr[i] = rest[i] * instM;
            meshMats.Add(arr);
        }
        _models.Add(new ModelInst(subs, meshMats));
    }

    private static Matrix4 ToMatrix(float[] a) => new(
        a[0], a[1], a[2], a[3],
        a[4], a[5], a[6], a[7],
        a[8], a[9], a[10], a[11],
        a[12], a[13], a[14], a[15]);

    private static int CreateTexture(ModelTexture? t)
    {
        if (t is null || t.Bgra.Length < t.Width * t.Height * 4 || t.Width <= 0 || t.Height <= 0) return 0;
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, t.Width, t.Height, 0,
            PixelFormat.Bgra, PixelType.UnsignedByte, t.Bgra);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        return tex;
    }

    private static Vector3 SphericalDir(float yawDeg, float pitchDeg)
    {
        float y = MathHelper.DegreesToRadians(yawDeg);
        float p = MathHelper.DegreesToRadians(pitchDeg);
        return new Vector3(MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p), MathF.Cos(p) * MathF.Cos(y));
    }

    // ---- input ----

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) { ResetView(); return; }
        _dragging = true;
        _dragButton = e.ChangedButton;
        _lastMouse = e.GetPosition(Surface);
        Surface.CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Surface.ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(Surface);
        double dx = pos.X - _lastMouse.X, dy = pos.Y - _lastMouse.Y;
        _lastMouse = pos;
        bool pan = _dragButton is MouseButton.Right or MouseButton.Middle || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (pan)
        {
            Vector3 fwd = -SphericalDir(_yaw, _pitch);
            Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, fwd));
            float scale = _distance * 0.0015f;
            _target += (-right * (float)dx + up * (float)dy) * scale;
        }
        else
        {
            _yaw -= (float)dx * 0.4f;
            _pitch = Math.Clamp(_pitch + (float)dy * 0.4f, -89f, 89f);
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float factor = e.Delta > 0 ? 0.9f : 1.0f / 0.9f;
        _distance = Math.Clamp(_distance * factor, 1f, 5000000f);
    }

    // ---- toolbar ----

    private void ResetView() => FrameCamera(Map);
    private void OnResetViewClick(object sender, RoutedEventArgs e) => ResetView();
    private void OnWireframeToggled(object sender, RoutedEventArgs e) => _wireframe = WireToggle.IsChecked == true;
    private void OnBackgroundClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _bg = HexToRgb(hex);
    }

    private static Vector3 HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
        float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
        float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
        return new Vector3(r, g, b);
    }

    // ---- gl lifecycle ----

    private void DeleteBuffers()
    {
        foreach (var s in _subs)
        {
            GL.DeleteVertexArray(s.Vao);
            GL.DeleteBuffer(s.Vbo);
            if (s.Tex != 0) GL.DeleteTexture(s.Tex);
        }
        _subs.Clear();
        foreach (var inst in _models)
            foreach (var s in inst.Subs)
            {
                GL.DeleteVertexArray(s.Vao);
                GL.DeleteBuffer(s.Vbo);
                if (s.Tex != 0) GL.DeleteTexture(s.Tex);
            }
        _models.Clear();

        if (_waterVao != 0) { GL.DeleteVertexArray(_waterVao); _waterVao = 0; }
        if (_waterVbo != 0) { GL.DeleteBuffer(_waterVbo); _waterVbo = 0; }
        foreach (var t in _waterFrames) if (t != 0) GL.DeleteTexture(t);
        _waterFrames = Array.Empty<int>();
        _waterCount = 0;
    }

    private void ReleaseGl()
    {
        try
        {
            DeleteBuffers();
            if (_program != 0) { GL.DeleteProgram(_program); _program = 0; }
            if (_mProgram != 0) { GL.DeleteProgram(_mProgram); _mProgram = 0; }
            if (_wProgram != 0) { GL.DeleteProgram(_wProgram); _wProgram = 0; }
        }
        catch { /* context gone — driver reclaims */ }
    }

    private static int BuildProgram(string vs, string fs)
    {
        int v = Compile(ShaderType.VertexShader, vs);
        int f = Compile(ShaderType.FragmentShader, fs);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v);
        GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.DetachShader(p, v);
        GL.DetachShader(p, f);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        return p;
    }

    private static int Compile(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        return s;
    }

    private const string VertSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
layout(location=2) in vec3 aColor;
uniform mat4 vp;
out vec2 vUv;
out vec3 vColor;
void main(){ vUv = aUv; vColor = aColor; gl_Position = vec4(aPos, 1.0) * vp; }";

    private const string FragSrc = @"#version 330 core
in vec2 vUv;
in vec3 vColor;
out vec4 FragColor;
uniform sampler2D uTex;
uniform int uHasTexture;
uniform int uWire;
uniform vec3 uWireColor;
uniform vec3 uLightDir;
uniform vec3 uAmbient;
uniform vec3 uDiffuse;
void main(){
    if (uWire == 1) { FragColor = vec4(uWireColor, 1.0); return; }
    vec3 col = vColor;
    if (uHasTexture == 1) {
        vec4 t = texture(uTex, vUv);
        if (t.a < 0.1) discard;
        col = t.rgb * vColor;
    }
    // flat ground sun term (lightmap already baked into vColor)
    vec3 lit = clamp(uAmbient + uDiffuse * max(uLightDir.y, 0.0), 0.0, 1.0);
    FragColor = vec4(col * lit, 1.0);
}";

    private const string ModelVertSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
uniform mat4 vp;
uniform mat4 m;
out vec3 vN;
out vec2 vUv;
void main(){ vN = aNormal * mat3(m); vUv = aUv; gl_Position = vec4(aPos, 1.0) * m * vp; }";

    private const string ModelFragSrc = @"#version 330 core
in vec3 vN;
in vec2 vUv;
out vec4 FragColor;
uniform vec3 uLightDir;
uniform vec3 uAmbient;
uniform vec3 uDiffuse;
uniform sampler2D uTex;
uniform int uHasTexture;
void main(){
    float nl = abs(dot(normalize(vN), normalize(uLightDir)));   // two-sided
    vec3 lit = clamp(uAmbient + uDiffuse * nl, 0.0, 1.2);
    vec3 base = vec3(0.85);
    if (uHasTexture == 1) {
        vec4 t = texture(uTex, vUv);
        if (t.a < 0.5) discard;
        base = t.rgb;
    }
    FragColor = vec4(base * lit, 1.0);
}";

    private const string WaterVertSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
uniform mat4 vp;
uniform float uTime;
uniform float uAmp;
uniform float uSpeed;
uniform float uPitch;
out vec2 vUv;
void main(){
    vUv = aUv;
    float phase = radians(uSpeed * 50.0 * uTime + (aPos.x - aPos.z) * 0.1 * uPitch);
    vec3 p = vec3(aPos.x, aPos.y + uAmp * cos(phase), aPos.z);
    gl_Position = vec4(p, 1.0) * vp;
}";

    private const string WaterFragSrc = @"#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uTex;
void main(){
    vec4 c = texture(uTex, vUv);
    FragColor = vec4(c.rgb, 0.56);
}";
}
