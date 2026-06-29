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
/// OpenGL host for the RSM model preview (GLWpfControl + OpenTK 4 — pure WPF, composes with the
/// preview chrome). Uploads <see cref="ModelGeometry"/> baked by <c>RsmModelBuilder</c> and draws it
/// textured + shaded with an orbit/pan/zoom camera framed to the model.
/// </summary>
public partial class GlModelViewer : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ModelGeometry), typeof(GlModelViewer),
        new PropertyMetadata(null, OnModelChanged));

    public ModelGeometry? Model
    {
        get => (ModelGeometry?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private sealed record Sub(int Vao, int Vbo, int Count, int Tex, int MeshIndex);

    private bool _started, _ready, _dirty;
    private int _program, _vpLoc, _modelLoc, _lightLoc, _colorLoc, _texLoc, _hasTexLoc;
    private readonly List<Sub> _subs = new();
    private ModelGeometry? _pending;

    // animation: per-mesh world matrices recomputed each frame from the elapsed clock
    private IReadOnlyList<MeshTransform>? _anims;
    private double _version;
    private int _animLength;
    private double _elapsedMs;

    // camera (spherical orbit around _target)
    private float _yaw = 45f, _pitch = 25f, _distance = 100f;
    private Vector3 _target = Vector3.Zero;
    private Point _lastMouse;
    private MouseButton _dragButton;
    private bool _dragging;

    public GlModelViewer()
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

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (GlModelViewer)d;
        v._pending = e.NewValue as ModelGeometry;
        v._dirty = true;          // actual GL upload happens on the render thread (OnRender)
        v.FrameCamera(v._pending);
    }

    private void FrameCamera(ModelGeometry? m)
    {
        if (m is null) return;
        _target = new Vector3(m.Center[0], m.Center[1], m.Center[2]);
        // distance so the bounding sphere fits in a 45° vertical FOV, with margin
        float r = MathF.Max(m.Radius, 1f);
        _distance = r / MathF.Sin(MathHelper.DegreesToRadians(45f) * 0.5f) * 1.25f;
        _yaw = 45f; _pitch = 25f;
    }

    private void OnReady()
    {
        GL.ClearColor(0.10f, 0.10f, 0.13f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);   // RSM winding varies + two-sided faces; lighting is two-sided
        _program = BuildProgram(VertSrc, FragSrc);
        _vpLoc = GL.GetUniformLocation(_program, "vp");
        _modelLoc = GL.GetUniformLocation(_program, "m");
        _lightLoc = GL.GetUniformLocation(_program, "uLightDir");
        _colorLoc = GL.GetUniformLocation(_program, "uColor");
        _texLoc = GL.GetUniformLocation(_program, "uTex");
        _hasTexLoc = GL.GetUniformLocation(_program, "uHasTexture");
        _ready = true;
    }

    private void OnRender(TimeSpan delta)
    {
        _elapsedMs += delta.TotalMilliseconds;
        if (_ready && _dirty) Upload();

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (_program == 0 || _subs.Count == 0) return;

        float aspect = (float)Math.Max(1.0, Surface.ActualWidth) / (float)Math.Max(1.0, Surface.ActualHeight);
        Vector3 dir = SphericalDir(_yaw, _pitch);
        Vector3 eye = _target + dir * _distance;
        Matrix4 view = Matrix4.LookAt(eye, _target, Vector3.UnitY);
        float near = MathF.Max(_distance * 0.01f, 0.05f);
        float far = _distance * 4f + 5000f;
        Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), aspect, near, far);
        Matrix4 vp = view * proj;                 // row-vector: clip = pos * view * proj

        // light follows the camera a bit so the model is never fully dark from any angle
        Vector3 light = Vector3.Normalize(dir + new Vector3(0.3f, 0.6f, 0.2f));
        Vector3 color = new(0.78f, 0.80f, 0.86f);

        double frame = _elapsedMs;
        Matrix4[] mats = _anims is { Count: > 0 }
            ? RsmPose.ComputeWorldMatrices(_anims, _version, _animLength, frame)
            : Array.Empty<Matrix4>();

        GL.UseProgram(_program);
        GL.UniformMatrix4(_vpLoc, true, ref vp);   // transpose:true — OpenTK row-major -> GL column-major
        GL.Uniform3(_lightLoc, ref light);
        GL.Uniform3(_colorLoc, ref color);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(_texLoc, 0);
        int lastMesh = -1;
        foreach (var s in _subs)
        {
            if (s.MeshIndex != lastMesh)
            {
                Matrix4 m = s.MeshIndex < mats.Length ? mats[s.MeshIndex] : Matrix4.Identity;
                GL.UniformMatrix4(_modelLoc, true, ref m);
                lastMesh = s.MeshIndex;
            }
            GL.Uniform1(_hasTexLoc, s.Tex != 0 ? 1 : 0);
            if (s.Tex != 0) GL.BindTexture(TextureTarget.Texture2D, s.Tex);
            GL.BindVertexArray(s.Vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, s.Count);
        }
        GL.BindVertexArray(0);
    }

    private void Upload()
    {
        _dirty = false;
        DeleteBuffers();
        var m = _pending;
        _anims = m?.Meshes.Select(mm => mm.Transform).ToList();
        _version = m?.Version ?? 0;
        _animLength = m?.AnimationLength ?? 0;
        _elapsedMs = 0;
        if (m is null) return;

        const int stride = ModelSubmesh.Stride * sizeof(float);
        for (int mi = 0; mi < m.Meshes.Count; mi++)
        {
            foreach (var sm in m.Meshes[mi].Submeshes)
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
                _subs.Add(new Sub(vao, vbo, sm.VertexCount, CreateTexture(sm.Texture), mi));
            }
        }
        GL.BindVertexArray(0);
    }

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

        bool pan = _dragButton is MouseButton.Right or MouseButton.Middle
                   || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (pan)
        {
            // move the orbit target in the camera plane
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
        _distance = Math.Clamp(_distance * factor, 0.5f, 500000f);
    }

    // ---- gl resource management ----

    private void DeleteBuffers()
    {
        foreach (var s in _subs)
        {
            GL.DeleteVertexArray(s.Vao);
            GL.DeleteBuffer(s.Vbo);
            if (s.Tex != 0) GL.DeleteTexture(s.Tex);
        }
        _subs.Clear();
    }

    private void ReleaseGl()
    {
        // best-effort: the GL context is current right after the render loop on teardown
        try
        {
            DeleteBuffers();
            if (_program != 0) { GL.DeleteProgram(_program); _program = 0; }
        }
        catch { /* context already gone — driver reclaims on destruction */ }
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
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
uniform mat4 vp;
uniform mat4 m;
out vec3 vN;
out vec2 vUv;
void main(){
    vN = aNormal * mat3(m);
    vUv = aUv;
    gl_Position = vec4(aPos, 1.0) * m * vp;
}";

    private const string FragSrc = @"#version 330 core
in vec3 vN;
in vec2 vUv;
out vec4 FragColor;
uniform vec3 uLightDir;
uniform vec3 uColor;
uniform sampler2D uTex;
uniform int uHasTexture;
void main(){
    vec3 n = normalize(vN);
    float d = abs(dot(n, normalize(uLightDir)));   // two-sided: never depends on winding
    float lit = 0.28 + 0.72 * d;
    vec3 baseCol = uColor;
    if (uHasTexture == 1) {
        vec4 t = texture(uTex, vUv);
        if (t.a < 0.5) discard;                    // RO color-key cutout
        baseCol = t.rgb;
    }
    FragColor = vec4(baseCol * lit, 1.0);
}";
}
