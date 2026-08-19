using System.Numerics;
using System.Runtime.InteropServices;
using NeoVeldrid;
using NeoVeldrid.Vk;
using SDL;
using Veonex.Core;

namespace Veonex.Render;

public sealed class WindowParameters
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public string Title { get; init; } = "Veonex";
    public bool VSync { get; init; } = false;
    public bool Resizable { get; init; } = true;
}

public sealed unsafe class RenderBackend : IDisposable
{
    private readonly WindowParameters _parameters;

    private readonly SDL_Window* _window;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly CommandList _commandList;

    private readonly Shader[] _shaders;
    private readonly Pipeline _pipeline;

    private readonly ResourceLayout _transformLayout;

    private readonly Dictionary<Guid, MeshBuffer> _meshCache = [];
    private readonly Dictionary<Guid, TransformResources> _transformCache = [];

    private bool _disposed;

    public ResourceFactory Factory =>
        _graphicsDevice.ResourceFactory;

    public bool IsRunning { get; private set; } = true;

    private sealed class TransformResources : IDisposable
    {
        public DeviceBuffer Buffer { get; }
        public ResourceSet ResourceSet { get; }

        public TransformResources(
            DeviceBuffer buffer,
            ResourceSet resourceSet)
        {
            Buffer = buffer;
            ResourceSet = resourceSet;
        }

        public void Dispose()
        {
            ResourceSet.Dispose();
            Buffer.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MatrixBuffer
    {
        public Matrix4x4 MVP;

        public MatrixBuffer(
            Matrix4x4 mvp)
        {
            MVP = mvp;
        }
    }

    public RenderBackend(
        WindowParameters parameters)
    {
        _parameters = parameters;

        _window = CreateWindow();

        _graphicsDevice =
            CreateGraphicsDevice();

        _commandList =
            Factory.CreateCommandList();

        _transformLayout =
            Factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription(
                        "MVP",
                        ResourceKind.UniformBuffer,
                        ShaderStages.Vertex)));

        string shaderPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Shaders",
                "Basic.ves");

        _shaders =
            ShaderLoader.Load(
                Factory,
                shaderPath);

        _pipeline =
            CreatePipeline();
    }

    private SDL_Window* CreateWindow()
    {
        if (!SDL3.SDL_Init(
                SDL_InitFlags.SDL_INIT_VIDEO))
        {
            throw new InvalidOperationException(
                $"Failed to initialize SDL3: {SDL3.SDL_GetError()}");
        }

        SDL_WindowFlags flags =
            SDL_WindowFlags.SDL_WINDOW_VULKAN;

        if (_parameters.Resizable)
        {
            flags |=
                SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        byte[] title =
            System.Text.Encoding.UTF8.GetBytes(
                _parameters.Title + '\0');

        fixed (byte* titlePtr = title)
        {
            SDL_Window* window =
                SDL3.SDL_CreateWindow(
                    titlePtr,
                    _parameters.Width,
                    _parameters.Height,
                    flags);

            if (window == null)
            {
                string error =
                    SDL3.SDL_GetError();

                SDL3.SDL_Quit();

                throw new InvalidOperationException(
                    $"Failed to create SDL3 window: {error}");
            }

            return window;
        }
    }

    private GraphicsDevice CreateGraphicsDevice()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The current Veonex SDL3/Vulkan backend targets Windows.");
        }

        SDL_PropertiesID properties =
            SDL3.SDL_GetWindowProperties(
                _window);

        void* hwnd;
        void* hinstance;

        fixed (
            byte* hwndProperty =
                SDL3.SDL_PROP_WINDOW_WIN32_HWND_POINTER)
        fixed (
            byte* hinstanceProperty =
                SDL3.SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER)
        {
            hwnd =
                (void*)SDL3.SDL_GetPointerProperty(
                    properties,
                    hwndProperty,
                    0);

            hinstance =
                (void*)SDL3.SDL_GetPointerProperty(
                    properties,
                    hinstanceProperty,
                    0);
        }

        if (hwnd == null)
        {
            throw new InvalidOperationException(
                "SDL3 did not provide a Win32 HWND.");
        }

        if (hinstance == null)
        {
            throw new InvalidOperationException(
                "SDL3 did not provide a Win32 HINSTANCE.");
        }

        VkSurfaceSource surfaceSource =
            VkSurfaceSource.CreateWin32(
                (nint)hinstance,
                (nint)hwnd);

        GraphicsDeviceOptions options =
            new(
                debug: true,
                swapchainDepthFormat:
                    PixelFormat.D24_UNorm_S8_UInt,
                syncToVerticalBlank:
                    _parameters.VSync,
                resourceBindingModel:
                    ResourceBindingModel.Improved,
                preferDepthRangeZeroToOne:
                    true,
                preferStandardClipSpaceYDirection:
                    true);

        return GraphicsDevice.CreateVulkan(
            options,
            surfaceSource,
            (uint)_parameters.Width,
            (uint)_parameters.Height);
    }

    private Pipeline CreatePipeline()
    {
        VertexLayoutDescription positionLayout =
            new(
                new VertexElementDescription(
                    "Position",
                    VertexElementSemantic.Position,
                    VertexElementFormat.Float3));

        VertexLayoutDescription normalLayout =
            new(
                new VertexElementDescription(
                    "Normal",
                    VertexElementSemantic.Normal,
                    VertexElementFormat.Float3));

        ShaderSetDescription shaderSet =
            new(
                [
                    positionLayout,
                    normalLayout
                ],
                _shaders);

        RasterizerStateDescription rasterizer =
            new(
                FaceCullMode.None,
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

        DepthStencilStateDescription depthState =
            new(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);

        GraphicsPipelineDescription description =
            new(
                BlendStateDescription.SingleOverrideBlend,
                depthState,
                rasterizer,
                PrimitiveTopology.TriangleList,
                shaderSet,
                [
                    _transformLayout
                ],
                _graphicsDevice
                    .SwapchainFramebuffer
                    .OutputDescription);

        return Factory.CreateGraphicsPipeline(
            description);
    }

    public void RenderFrame(
        Scene scene,
        Camera camera)
    {
        PumpEvents();

        if (!IsRunning)
            return;

        float aspect =
            (float)_parameters.Width /
            _parameters.Height;

        Matrix4x4 view =
            CreateViewMatrix(camera);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                (float)(
                    camera.FieldOfView *
                    Math.PI /
                    180.0),
                aspect,
                (float)camera.NearClip,
                (float)camera.FarClip);

        HashSet<Guid> activeTransforms = [];

        _commandList.Begin();

        _commandList.SetFramebuffer(
            _graphicsDevice.SwapchainFramebuffer);

        _commandList.SetFullViewports();

        _commandList.ClearColorTarget(
            0,
            RgbaFloat.Black);

        _commandList.ClearDepthStencil(
            1.0f);

        _commandList.SetPipeline(
            _pipeline);

        foreach (Entity entity in scene.Entities)
        {
            if (!entity.Has<MeshRenderer>())
                continue;

            MeshRenderer renderer =
                entity.Get<MeshRenderer>();

            if (!renderer.Visible)
                continue;

            if (renderer.Mesh == null)
                continue;

            if (!renderer.Mesh.Data.HasNormals)
                continue;

            if (!entity.Has<Transform>())
                continue;

            Transform transform =
                entity.Get<Transform>();

            MeshBuffer meshBuffer =
                GetOrCreateMeshBuffer(
                    renderer.Mesh);

            Matrix4x4 model =
                CreateModelMatrix(
                    transform);

            Matrix4x4 mvp =
                model *
                view *
                projection;

            TransformResources resources =
                GetOrCreateTransformResources(
                    entity.Id);

            _graphicsDevice.UpdateBuffer(
                resources.Buffer,
                0,
                new MatrixBuffer(mvp));

            activeTransforms.Add(
                entity.Id);

            _commandList.SetGraphicsResourceSet(
                0,
                resources.ResourceSet);

            _commandList.SetVertexBuffer(
                0,
                meshBuffer.PositionBuffer);

            if (meshBuffer.NormalBuffer != null)
            {
                _commandList.SetVertexBuffer(
                    1,
                    meshBuffer.NormalBuffer);
            }

            _commandList.SetIndexBuffer(
                meshBuffer.IndexBuffer,
                IndexFormat.UInt32);

            _commandList.DrawIndexed(
                meshBuffer.IndexCount,
                1,
                0,
                0,
                0);
        }

        _commandList.End();

        _graphicsDevice.SubmitCommands(
            _commandList);

        _graphicsDevice.SwapBuffers();

        CleanupUnusedTransformResources(
            activeTransforms);
    }

    private MeshBuffer GetOrCreateMeshBuffer(
        Mesh mesh)
    {
        if (_meshCache.TryGetValue(
            mesh.Id,
            out MeshBuffer? existing))
        {
            return existing;
        }

        MeshBuffer created =
            CreateMeshBuffer(mesh);

        _meshCache.Add(
            mesh.Id,
            created);

        return created;
    }

    private MeshBuffer CreateMeshBuffer(
        Mesh mesh)
    {
        MeshData data =
            mesh.Data;

        Vector3[] positions =
            new Vector3[data.VertexCount];

        for (int i = 0; i < data.VertexCount; i++)
        {
            positions[i] =
                new Vector3(
                    (float)data.Positions[i].X,
                    (float)data.Positions[i].Y,
                    (float)data.Positions[i].Z);
        }

        DeviceBuffer positionBuffer =
            Factory.CreateBuffer(
                new BufferDescription(
                    (uint)(
                        data.VertexCount *
                        sizeof(float) *
                        3),
                    BufferUsage.VertexBuffer));

        _graphicsDevice.UpdateBuffer(
            positionBuffer,
            0,
            positions);

        DeviceBuffer? normalBuffer = null;

        if (data.HasNormals)
        {
            Vector3[] normals =
                new Vector3[data.VertexCount];

            for (int i = 0; i < data.VertexCount; i++)
            {
                normals[i] =
                    new Vector3(
                        (float)data.Normals[i].X,
                        (float)data.Normals[i].Y,
                        (float)data.Normals[i].Z);
            }

            normalBuffer =
                Factory.CreateBuffer(
                    new BufferDescription(
                        (uint)(
                            data.VertexCount *
                            sizeof(float) *
                            3),
                        BufferUsage.VertexBuffer));

            _graphicsDevice.UpdateBuffer(
                normalBuffer,
                0,
                normals);
        }

        DeviceBuffer? uvBuffer = null;

        if (data.HasUVs)
        {
            Vector2[] uvs =
                new Vector2[data.VertexCount];

            for (int i = 0; i < data.VertexCount; i++)
            {
                uvs[i] =
                    new Vector2(
                        (float)data.UVs[i].X,
                        (float)data.UVs[i].Y);
            }

            uvBuffer =
                Factory.CreateBuffer(
                    new BufferDescription(
                        (uint)(
                            data.VertexCount *
                            sizeof(float) *
                            2),
                        BufferUsage.VertexBuffer));

            _graphicsDevice.UpdateBuffer(
                uvBuffer,
                0,
                uvs);
        }

        DeviceBuffer indexBuffer =
            Factory.CreateBuffer(
                new BufferDescription(
                    (uint)(
                        data.IndexCount *
                        sizeof(uint)),
                    BufferUsage.IndexBuffer));

        _graphicsDevice.UpdateBuffer(
            indexBuffer,
            0,
            data.Indices);

        return new MeshBuffer(
            positionBuffer,
            indexBuffer,
            (uint)data.IndexCount,
            normalBuffer,
            uvBuffer);
    }

    private TransformResources
        GetOrCreateTransformResources(
            Guid entityId)
    {
        if (_transformCache.TryGetValue(
            entityId,
            out TransformResources? existing))
        {
            return existing;
        }

        DeviceBuffer buffer =
            Factory.CreateBuffer(
                new BufferDescription(
                    (uint)Marshal.SizeOf<MatrixBuffer>(),
                    BufferUsage.UniformBuffer));

        ResourceSet resourceSet =
            Factory.CreateResourceSet(
                new ResourceSetDescription(
                    _transformLayout,
                    buffer));

        TransformResources resources =
            new(
                buffer,
                resourceSet);

        _transformCache.Add(
            entityId,
            resources);

        return resources;
    }

    private void CleanupUnusedTransformResources(
        HashSet<Guid> activeEntities)
    {
        List<Guid>? remove = null;

        foreach (Guid id in _transformCache.Keys)
        {
            if (activeEntities.Contains(id))
                continue;

            remove ??= [];

            remove.Add(id);
        }

        if (remove == null)
            return;

        foreach (Guid id in remove)
        {
            TransformResources resources =
                _transformCache[id];

            resources.Dispose();

            _transformCache.Remove(id);
        }
    }

    private static Matrix4x4 CreateModelMatrix(
        Transform transform)
    {
        Vector3 position =
            new(
                (float)transform.Position.X,
                (float)transform.Position.Y,
                (float)transform.Position.Z);

        Vector3 scale =
            new(
                (float)transform.Scale.X,
                (float)transform.Scale.Y,
                (float)transform.Scale.Z);

        Vector3 rotation =
            new(
                (float)transform.Rotation.X,
                (float)transform.Rotation.Y,
                (float)transform.Rotation.Z);

        float radiansX =
            rotation.X *
            (MathF.PI / 180.0f);

        float radiansY =
            rotation.Y *
            (MathF.PI / 180.0f);

        float radiansZ =
            rotation.Z *
            (MathF.PI / 180.0f);

        Quaternion quaternion =
            Quaternion.CreateFromYawPitchRoll(
                radiansY,
                radiansX,
                radiansZ);

        return
            Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(quaternion) *
            Matrix4x4.CreateTranslation(position);
    }

    private static Matrix4x4 CreateViewMatrix(
        Camera camera)
    {
        Transform transform =
            camera.Entity.Get<Transform>();

        Vector3 position =
            new(
                (float)transform.Position.X,
                (float)transform.Position.Y,
                (float)transform.Position.Z);

        Vector3 rotation =
            new(
                (float)transform.Rotation.X,
                (float)transform.Rotation.Y,
                (float)transform.Rotation.Z);

        Quaternion quaternion =
            Quaternion.CreateFromYawPitchRoll(
                rotation.Y *
                    (MathF.PI / 180.0f),
                rotation.X *
                    (MathF.PI / 180.0f),
                rotation.Z *
                    (MathF.PI / 180.0f));

        Vector3 forward =
            Vector3.Transform(
                Vector3.UnitZ,
                quaternion);

        Vector3 up =
            Vector3.Transform(
                Vector3.UnitY,
                quaternion);

        return Matrix4x4.CreateLookAt(
            position,
            position + forward,
            up);
    }

    private void PumpEvents()
    {
        SDL_Event ev;

        while (SDL3.SDL_PollEvent(&ev))
        {
            if (ev.Type ==
                SDL_EventType.SDL_EVENT_QUIT)
            {
                IsRunning = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (MeshBuffer meshBuffer
                 in _meshCache.Values)
        {
            meshBuffer.Dispose();
        }

        _meshCache.Clear();

        foreach (TransformResources resources
                 in _transformCache.Values)
        {
            resources.Dispose();
        }

        _transformCache.Clear();

        _pipeline.Dispose();

        foreach (Shader shader in _shaders)
        {
            shader.Dispose();
        }

        _transformLayout.Dispose();

        _commandList.Dispose();

        _graphicsDevice.Dispose();

        if (_window != null)
        {
            SDL3.SDL_DestroyWindow(
                _window);
        }

        SDL3.SDL_Quit();
    }
}