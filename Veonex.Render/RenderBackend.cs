using System.Numerics;
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

    private readonly DeviceBuffer _cubeVertexBuffer;
    private readonly DeviceBuffer _cubeIndexBuffer;

    private readonly Shader[] _cubeShaders;
    private readonly Pipeline _cubePipeline;

    private readonly CubeVertex[] _cubeVertices;

    private float _rotation;
    private bool _disposed;

    public ResourceFactory Factory =>
        _graphicsDevice.ResourceFactory;

    public bool IsRunning { get; private set; } = true;

    private struct CubeVertex
    {
        public Vector3 Position;
        public Vector3 Color;

        public CubeVertex(
            Vector3 position,
            Vector3 color)
        {
            Position = position;
            Color = color;
        }
    }

    private static readonly uint[] CubeIndices =
    [
        // Front
        0, 1, 2,
        2, 3, 0,

        // Back
        4, 5, 6,
        6, 7, 4,

        // Left
        8, 9, 10,
        10, 11, 8,

        // Right
        12, 13, 14,
        14, 15, 12,

        // Top
        16, 17, 18,
        18, 19, 16,

        // Bottom
        20, 21, 22,
        22, 23, 20
    ];

    public RenderBackend(
        WindowParameters parameters)
    {
        _parameters = parameters;

        _window = CreateWindow();

        _graphicsDevice =
            CreateGraphicsDevice();

        _commandList =
            Factory.CreateCommandList();

        _cubeVertices =
            CreateCubeVertices();

        _cubeVertexBuffer =
            Factory.CreateBuffer(
                new BufferDescription(
                    (uint)(
                        _cubeVertices.Length *
                        (sizeof(float) * 6)),
                    BufferUsage.VertexBuffer));

        _cubeIndexBuffer =
            Factory.CreateBuffer(
                new BufferDescription(
                    (uint)(
                        CubeIndices.Length *
                        sizeof(uint)),
                    BufferUsage.IndexBuffer));

        _graphicsDevice.UpdateBuffer(
            _cubeVertexBuffer,
            0,
            _cubeVertices);

        _graphicsDevice.UpdateBuffer(
            _cubeIndexBuffer,
            0,
            CubeIndices);

        string shaderPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Shaders",
                "Cube.ves");

        _cubeShaders =
            ShaderLoader.Load(
                Factory,
                shaderPath);

        _cubePipeline =
            CreateCubePipeline();
    }

    private static CubeVertex[] CreateCubeVertices()
    {
        Vector3 frontColor =
            new(1.0f, 0.15f, 0.15f);

        Vector3 backColor =
            new(0.8f, 0.05f, 0.05f);

        Vector3 leftColor =
            new(1.0f, 0.30f, 0.10f);

        Vector3 rightColor =
            new(0.65f, 0.0f, 0.0f);

        Vector3 topColor =
            new(1.0f, 0.45f, 0.45f);

        Vector3 bottomColor =
            new(0.45f, 0.02f, 0.02f);

        return
        [
            // Front
            new(
                new(-1, -1, -1),
                frontColor),

            new(
                new( 1, -1, -1),
                frontColor),

            new(
                new( 1,  1, -1),
                frontColor),

            new(
                new(-1,  1, -1),
                frontColor),

            // Back
            new(
                new( 1, -1,  1),
                backColor),

            new(
                new(-1, -1,  1),
                backColor),

            new(
                new(-1,  1,  1),
                backColor),

            new(
                new( 1,  1,  1),
                backColor),

            // Left
            new(
                new(-1, -1,  1),
                leftColor),

            new(
                new(-1, -1, -1),
                leftColor),

            new(
                new(-1,  1, -1),
                leftColor),

            new(
                new(-1,  1,  1),
                leftColor),

            // Right
            new(
                new(1, -1, -1),
                rightColor),

            new(
                new(1, -1,  1),
                rightColor),

            new(
                new(1,  1,  1),
                rightColor),

            new(
                new(1,  1, -1),
                rightColor),

            // Top
            new(
                new(-1, 1, -1),
                topColor),

            new(
                new(1, 1, -1),
                topColor),

            new(
                new(1, 1, 1),
                topColor),

            new(
                new(-1, 1, 1),
                topColor),

            // Bottom
            new(
                new(-1, -1, 1),
                bottomColor),

            new(
                new(1, -1, 1),
                bottomColor),

            new(
                new(1, -1, -1),
                bottomColor),

            new(
                new(-1, -1, -1),
                bottomColor)
        ];
    }

    private SDL_Window* CreateWindow()
    {
        if (!SDL3.SDL_Init(
                SDL_InitFlags.SDL_INIT_VIDEO))
        {
            string error =
                SDL3.SDL_GetError();

            throw new InvalidOperationException(
                $"Failed to initialize SDL3: {error}");
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

    private Pipeline CreateCubePipeline()
    {
        VertexLayoutDescription vertexLayout =
            new(
                new VertexElementDescription(
                    "Position",
                    VertexElementSemantic.Position,
                    VertexElementFormat.Float3),

                new VertexElementDescription(
                    "Color",
                    VertexElementSemantic.TextureCoordinate,
                    VertexElementFormat.Float3));

        ShaderSetDescription shaderSet =
            new(
                [
                    vertexLayout
                ],
                _cubeShaders);

        GraphicsPipelineDescription pipelineDescription =
            new(
                BlendStateDescription.SingleOverrideBlend,

                new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind:
                        ComparisonKind.LessEqual),

                RasterizerStateDescription.Default,

                PrimitiveTopology.TriangleList,

                shaderSet,

                [],

                _graphicsDevice
                    .SwapchainFramebuffer
                    .OutputDescription);

        return Factory.CreateGraphicsPipeline(
            pipelineDescription);
    }

    public void RenderFrame(
        Scene scene,
        Camera camera)
    {
        PumpEvents();

        if (!IsRunning)
            return;

        _rotation += 0.01f;

        UpdateCubeVertices();

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
            _cubePipeline);

        _commandList.SetVertexBuffer(
            0,
            _cubeVertexBuffer);

        _commandList.SetIndexBuffer(
            _cubeIndexBuffer,
            IndexFormat.UInt32);

        _commandList.DrawIndexed(
            (uint)CubeIndices.Length,
            1,
            0,
            0,
            0);

        _commandList.End();

        _graphicsDevice.SubmitCommands(
            _commandList);

        _graphicsDevice.SwapBuffers();
    }

    private void UpdateCubeVertices()
    {
        float aspect =
            (float)_parameters.Width /
            _parameters.Height;

        Matrix4x4 rotation =
            Matrix4x4.CreateRotationY(
                _rotation) *
            Matrix4x4.CreateRotationX(
                _rotation * 0.7f);

        Matrix4x4 view =
            Matrix4x4.CreateLookAt(
                new Vector3(0, 0, 5),
                Vector3.Zero,
                Vector3.UnitY);

        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3.0f,
                aspect,
                0.1f,
                100.0f);

        Matrix4x4 transform =
            rotation *
            view *
            projection;

        CubeVertex[] transformed =
            new CubeVertex[_cubeVertices.Length];

        for (int i = 0; i < _cubeVertices.Length; i++)
        {
            Vector4 clip =
                Vector4.Transform(
                    new Vector4(
                        _cubeVertices[i].Position,
                        1.0f),
                    transform);

            transformed[i] =
                new CubeVertex(
                    new Vector3(
                        clip.X / clip.W,
                        clip.Y / clip.W,
                        clip.Z / clip.W),
                    _cubeVertices[i].Color);
        }

        _graphicsDevice.UpdateBuffer(
            _cubeVertexBuffer,
            0,
            transformed);
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

        _cubePipeline.Dispose();

        foreach (Shader shader in _cubeShaders)
        {
            shader.Dispose();
        }

        _cubeVertexBuffer.Dispose();
        _cubeIndexBuffer.Dispose();

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