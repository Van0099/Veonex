using NeoVeldrid;
using SDL;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Veonex.Core;
using Veonex.Mathematics;

namespace Veonex.Render;

public sealed class WindowParameters
{
	public int Width { get; init; } = 1280;
	public int Height { get; init; } = 720;
	public string Title { get; init; } = "Veonex";
	public bool VSync { get; init; } = false;
	public bool Resizable { get; init; } = true;
	public bool Fullscreen { get; init; } = false;
}

public sealed unsafe class RenderBackend : IDisposable
{
	private readonly WindowParameters _parameters;
	private readonly SDL_Window* _window;

	private readonly GraphicsDevice _graphicsDevice;
	private readonly CommandList _commandList;

	private readonly Shader[] _shaders;
	private readonly Pipeline _pipeline;
	private readonly Pipeline _transparentPipeline;

	private readonly Shader[] _skyShaders;
	private readonly Pipeline _skyPipeline;

	private readonly ResourceLayout _skyLayout;
	private readonly DeviceBuffer _skyBuffer;
	private readonly ResourceSet _skyResourceSet;

	private readonly Sampler _linearSampler;
	private readonly Texture _whiteTexture;

	private readonly ResourceLayout _cameraLayout;
	private readonly DeviceBuffer _cameraBuffer;
	private readonly ResourceSet _cameraResourceSet;
	private readonly ResourceLayout _materialLayout;

	private readonly Dictionary<Guid, MeshBuffer> _meshCache = [];
	private readonly Dictionary<Guid, MaterialResources> _materialCache = [];
	private readonly Dictionary<string, Texture> _textureCache =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, bool> _textureAlphaCache =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<Guid, MeshBounds> _meshBoundsCache = [];

	private readonly Dictionary<BatchKey, DrawBatch> _drawBatches = [];
	private readonly Dictionary<BatchKey, TransparentBatch> _transparentBatches = [];
	private readonly List<TransparentBatch> _sortedTransparentBatches = [];

	private DeviceBuffer? _instanceBuffer;
	private int _instanceBufferCapacity;
	private InstanceData[] _instanceUploadData = new InstanceData[1];
	private OcclusionInstanceData[] _occlusionUploadData = new OcclusionInstanceData[1];

	private bool _occlusionCullingSupported;
	private Shader[]? _occlusionDepthShaders;
	private Shader? _hiZCopyShader;
	private Shader? _hiZDownsampleShader;
	private Shader? _occlusionCullShader;
	private Pipeline? _occlusionDepthPipeline;
	private Pipeline? _hiZCopyPipeline;
	private Pipeline? _hiZDownsamplePipeline;
	private Pipeline? _occlusionCullPipeline;
	private ResourceLayout? _occlusionBuildLayout;
	private ResourceLayout? _occlusionCullLayout;
	private DeviceBuffer? _occlusionCullParametersBuffer;
	private DeviceBuffer? _occlusionMetadataBuffer;
	private DeviceBuffer? _occlusionVisibleInstanceBuffer;
	private DeviceBuffer? _occlusionIndirectBuffer;
	private int _occlusionBufferCapacity;
	private IndirectDrawIndexedArguments[] _occlusionIndirectUploadData = new IndirectDrawIndexedArguments[1];
	private ResourceSet? _occlusionCullResourceSet;

	private Texture? _occlusionDepthTexture;
	private Texture? _hiZTexture;
	private TextureView? _occlusionDepthView;
	private TextureView? _hiZFullView;
	private TextureView[] _hiZMipViews = [];
	private Framebuffer? _occlusionFramebuffer;
	private ResourceSet? _hiZCopyResourceSet;
	private ResourceSet[] _hiZDownsampleResourceSets = [];
	private int _occlusionTextureWidth;
	private int _occlusionTextureHeight;

	private int _renderWidth;
	private int _renderHeight;
	private bool _fullscreen;

	public bool MouseCaptured { get; private set; }

	private bool _disposed;
	private long _lastDrawCallLogTime;

	public ResourceFactory Factory =>
		_graphicsDevice.ResourceFactory;

	private readonly record struct BatchKey(
		Guid MeshId,
		Guid MaterialId);

	[StructLayout(LayoutKind.Sequential)]
	private struct InstanceData
	{
		public Matrix4x4 Model;
		public Vector4 NormalRow0;
		public Vector4 NormalRow1;
		public Vector4 NormalRow2;
	}

	private static readonly int InstanceDataSize =
		Marshal.SizeOf<InstanceData>();

	[StructLayout(LayoutKind.Sequential)]
	private struct OcclusionInstanceData
	{
		public Vector4 CenterRadius;
		public uint BatchIndex;
		public uint OutputStart;
		public uint Padding0;
		public uint Padding1;
	}

	private static readonly int OcclusionInstanceDataSize =
		Marshal.SizeOf<OcclusionInstanceData>();

	private static readonly int IndexedIndirectCommandSize =
		Marshal.SizeOf<IndirectDrawIndexedArguments>();

	[StructLayout(LayoutKind.Sequential)]
	private struct OcclusionCullParameters
	{
		public Matrix4x4 ViewProjection;
		public Vector4 HiZInfo;
		public Vector4 ClipInfo;

		public OcclusionCullParameters(
			Matrix4x4 viewProjection,
			int width,
			int height,
			int mipLevels,
			int instanceCount,
			bool clipSpaceYInverted)
		{
			ViewProjection = viewProjection;
			HiZInfo = new Vector4(width, height, mipLevels, instanceCount);
			ClipInfo = new Vector4(clipSpaceYInverted ? 1f : 0f, 0f, 0f, 0f);
		}
	}

	private sealed class DrawBatch
	{
		public Mesh Mesh { get; }
		public Material Material { get; }

		private InstanceData[] _instances = new InstanceData[4];
		private OcclusionInstanceData[] _occlusionInstances = new OcclusionInstanceData[4];

		public int InstanceCount { get; private set; }
		public int InstanceStart { get; set; }
		public int BatchIndex { get; set; }

		public DrawBatch(Mesh mesh, Material material)
		{
			Mesh = mesh;
			Material = material;
		}

		public void Reset()
		{
			InstanceCount = 0;
			InstanceStart = 0;
			BatchIndex = 0;
		}

		public void AddInstance(
			InstanceData instance,
			Vector3 worldCenter,
			float worldRadius)
		{
			if (InstanceCount == _instances.Length)
			{
				int newLength = checked(_instances.Length * 2);
				Array.Resize(ref _instances, newLength);
				Array.Resize(ref _occlusionInstances, newLength);
			}

			_instances[InstanceCount] = instance;
			_occlusionInstances[InstanceCount] = new OcclusionInstanceData
			{
				CenterRadius = new Vector4(worldCenter, worldRadius)
			};
			InstanceCount++;
		}

		public void CopyInstancesTo(
			InstanceData[] destination,
			int destinationStart)
		{
			Array.Copy(
				_instances,
				0,
				destination,
				destinationStart,
				InstanceCount);
		}

		public void CopyOcclusionInstancesTo(
			OcclusionInstanceData[] destination,
			int destinationStart)
		{
			for (int i = 0; i < InstanceCount; i++)
			{
				OcclusionInstanceData data = _occlusionInstances[i];
				data.BatchIndex = (uint)BatchIndex;
				data.OutputStart = (uint)InstanceStart;
				destination[destinationStart + i] = data;
			}
		}
	}

	private readonly struct TransparentInstance
	{
		public InstanceData Data { get; }
		public float DistanceSquared { get; }

		public TransparentInstance(InstanceData data, float distanceSquared)
		{
			Data = data;
			DistanceSquared = distanceSquared;
		}
	}

	private sealed class TransparentInstanceComparer : IComparer<TransparentInstance>
	{
		public static readonly TransparentInstanceComparer Instance = new();

		public int Compare(TransparentInstance left, TransparentInstance right) =>
			right.DistanceSquared.CompareTo(left.DistanceSquared);
	}

	private sealed class TransparentBatch
	{
		public Mesh Mesh { get; }
		public Material Material { get; }

		private TransparentInstance[] _instances = new TransparentInstance[4];
		private double _distanceSum;

		public int InstanceCount { get; private set; }
		public int InstanceStart { get; set; }
		public float SortDistanceSquared { get; private set; }

		public TransparentBatch(Mesh mesh, Material material)
		{
			Mesh = mesh;
			Material = material;
		}

		public void Reset()
		{
			InstanceCount = 0;
			InstanceStart = 0;
			_distanceSum = 0.0;
			SortDistanceSquared = 0f;
		}

		public void AddInstance(InstanceData instance, float distanceSquared)
		{
			if (InstanceCount == _instances.Length)
			{
				Array.Resize(
					ref _instances,
					checked(_instances.Length * 2));
			}

			_instances[InstanceCount++] = new TransparentInstance(
				instance,
				distanceSquared);
			_distanceSum += distanceSquared;
		}

		public void SortInstances()
		{
			if (InstanceCount == 0)
				return;

			Array.Sort(
				_instances,
				0,
				InstanceCount,
				TransparentInstanceComparer.Instance);

			SortDistanceSquared = (float)(_distanceSum / InstanceCount);
		}

		public void CopyInstancesTo(
			InstanceData[] destination,
			int destinationStart)
		{
			for (int i = 0; i < InstanceCount; i++)
				destination[destinationStart + i] = _instances[i].Data;
		}
	}

	private sealed class MaterialResources : IDisposable
	{
		public DeviceBuffer Buffer { get; }
		public ResourceSet ResourceSet { get; }

		public MaterialResources(
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
	private struct CameraBuffer
	{
		public Matrix4x4 ViewProjection;

		public CameraBuffer(Matrix4x4 viewProjection)
		{
			ViewProjection = viewProjection;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MaterialBuffer
	{
		public Vector4 AlbedoColor;
		public Vector4 Parameters;

		public MaterialBuffer(Vector4 albedoColor, float opacity)
		{
			AlbedoColor = albedoColor;
			Parameters = new Vector4(opacity, 0f, 0f, 0f);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SkyBuffer
	{
		public Matrix4x4 InverseViewProjection;

		public SkyBuffer(Matrix4x4 inverseViewProjection)
		{
			InverseViewProjection = inverseViewProjection;
		}
	}

	private readonly struct MeshBounds
	{
		public Vector3 Center { get; }
		public float Radius { get; }

		public MeshBounds(Vector3 center, float radius)
		{
			Center = center;
			Radius = radius;
		}
	}

	private readonly struct FrustumPlane
	{
		public Vector3 Normal { get; }
		public float Distance { get; }

		public FrustumPlane(Vector3 normal, float distance)
		{
			float length = normal.Length();

			if (length > 0.000001f)
			{
				Normal = normal / length;
				Distance = distance / length;
			}
			else
			{
				Normal = normal;
				Distance = distance;
			}
		}
	}

	public RenderBackend(WindowParameters parameters)
	{
		_parameters = parameters;
		_window = CreateWindow();

		(_renderWidth, _renderHeight) = GetWindowPixelSize();
		_fullscreen = _parameters.Fullscreen;

		_graphicsDevice = CreateGraphicsDevice();

		_linearSampler = Factory.CreateSampler(
			new SamplerDescription(
				SamplerAddressMode.Wrap,
				SamplerAddressMode.Wrap,
				SamplerAddressMode.Wrap,
				SamplerFilter.MinLinear_MagLinear_MipLinear,
				null,
				16,
				0,
				16,
				0,
				SamplerBorderColor.TransparentBlack));

		_whiteTexture = Factory.CreateTexture(
			TextureDescription.Texture2D(
				1,
				1,
				1,
				1,
				PixelFormat.R8_G8_B8_A8_UNorm,
				TextureUsage.Sampled));

		byte[] whitePixel = [255, 255, 255, 255];

		_graphicsDevice.UpdateTexture(
			_whiteTexture,
			whitePixel,
			0,
			0,
			0,
			1,
			1,
			1,
			0,
			0);

		_commandList = Factory.CreateCommandList();

		_cameraLayout = Factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"ViewProjection",
					ResourceKind.UniformBuffer,
					ShaderStages.Vertex)));

		_cameraBuffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)Marshal.SizeOf<CameraBuffer>(),
				BufferUsage.UniformBuffer));

		_cameraResourceSet = Factory.CreateResourceSet(
			new ResourceSetDescription(
				_cameraLayout,
				_cameraBuffer));

		_materialLayout = Factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"MaterialColor",
					ResourceKind.UniformBuffer,
					ShaderStages.Fragment),
				new ResourceLayoutElementDescription(
					"AlbedoTexture",
					ResourceKind.TextureReadOnly,
					ShaderStages.Fragment),
				new ResourceLayoutElementDescription(
					"AlbedoSampler",
					ResourceKind.Sampler,
					ShaderStages.Fragment)));

		_skyLayout = Factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"SkyBuffer",
					ResourceKind.UniformBuffer,
					ShaderStages.Fragment)));

		_skyBuffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)Marshal.SizeOf<SkyBuffer>(),
				BufferUsage.UniformBuffer));

		_skyResourceSet = Factory.CreateResourceSet(
			new ResourceSetDescription(
				_skyLayout,
				_skyBuffer));

		string shaderPath = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders",
			"Basic.ves");

		_shaders = ShaderLoader.Load(Factory, shaderPath);

		string skyShaderPath = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders",
			"Sky.ves");

		_skyShaders = ShaderLoader.Load(Factory, skyShaderPath);

		_pipeline = CreatePipeline(transparent: false);
		_transparentPipeline = CreatePipeline(transparent: true);
		_skyPipeline = CreateSkyPipeline();

		_occlusionCullingSupported =
			_graphicsDevice.Features.ComputeShader &&
			_graphicsDevice.Features.StructuredBuffer &&
			_graphicsDevice.Features.DrawIndirect &&
			_graphicsDevice.Features.SubsetTextureView &&
			_graphicsDevice.IsDepthRangeZeroToOne &&
			_graphicsDevice.GetPixelFormatSupport(
				PixelFormat.R32_Float,
				TextureType.Texture2D,
				TextureUsage.Sampled | TextureUsage.Storage) &&
			_graphicsDevice.GetPixelFormatSupport(
				PixelFormat.D32_Float_S8_UInt,
				TextureType.Texture2D,
				TextureUsage.DepthStencil | TextureUsage.Sampled);

		if (_occlusionCullingSupported)
		{
			try
			{
				InitializeOcclusionCulling();
				Console.WriteLine(
					"[Veonex] GPU Hi-Z occlusion culling enabled (64+ opaque instances).");
			}
			catch (Exception ex)
			{
				DisposeOcclusionResources();
				_occlusionCullingSupported = false;
				Console.Error.WriteLine(
					$"[Veonex] Hi-Z occlusion culling disabled: {ex.Message}");
			}
		}
	}

	private void InitializeOcclusionCulling()
	{
		_occlusionBuildLayout = Factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"SourceSampler", ResourceKind.Sampler, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"DestinationImage", ResourceKind.TextureReadWrite, ShaderStages.Compute)));

		_occlusionCullLayout = Factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"SourceInstances", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"OcclusionInstances", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"VisibleInstances", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"IndirectCommands", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"HiZTexture", ResourceKind.TextureReadOnly, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"HiZSampler", ResourceKind.Sampler, ShaderStages.Compute),
				new ResourceLayoutElementDescription(
					"CullParameters", ResourceKind.UniformBuffer, ShaderStages.Compute)));

		_occlusionCullParametersBuffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)Marshal.SizeOf<OcclusionCullParameters>(),
				BufferUsage.UniformBuffer));

		string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "Shaders");
		_occlusionDepthShaders = ShaderLoader.Load(
			Factory, Path.Combine(shaderDirectory, "OcclusionDepth.ves"));
		_hiZCopyShader = ShaderLoader.LoadCompute(
			Factory, Path.Combine(shaderDirectory, "HiZCopy.comp"));
		_hiZDownsampleShader = ShaderLoader.LoadCompute(
			Factory, Path.Combine(shaderDirectory, "HiZDownsample.comp"));
		_occlusionCullShader = ShaderLoader.LoadCompute(
			Factory, Path.Combine(shaderDirectory, "OcclusionCull.comp"));

		CreateOcclusionTextures(_renderWidth, _renderHeight);
		_occlusionDepthPipeline = CreateOcclusionDepthPipeline();
		_hiZCopyPipeline = Factory.CreateComputePipeline(
			new ComputePipelineDescription(_hiZCopyShader, _occlusionBuildLayout, 8, 8, 1));
		_hiZDownsamplePipeline = Factory.CreateComputePipeline(
			new ComputePipelineDescription(_hiZDownsampleShader, _occlusionBuildLayout, 8, 8, 1));
		_occlusionCullPipeline = Factory.CreateComputePipeline(
			new ComputePipelineDescription(_occlusionCullShader, _occlusionCullLayout, 64, 1, 1));
	}

	private Pipeline CreateOcclusionDepthPipeline()
	{
		VertexLayoutDescription positionLayout = new(
			new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3));
		VertexLayoutDescription normalLayout = new(
			new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3));
		VertexLayoutDescription uvLayout = new(
			new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
		VertexLayoutDescription instanceLayout = new(
			(uint)InstanceDataSize, 1,
			new VertexElementDescription("InstanceRow0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 0),
			new VertexElementDescription("InstanceRow1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 16),
			new VertexElementDescription("InstanceRow2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 32),
			new VertexElementDescription("InstanceRow3", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 48),
			new VertexElementDescription("InstanceNormalRow0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 64),
			new VertexElementDescription("InstanceNormalRow1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 80),
			new VertexElementDescription("InstanceNormalRow2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, 96));

		ShaderSetDescription shaderSet = new(
			[positionLayout, normalLayout, uvLayout, instanceLayout],
			_occlusionDepthShaders!);

		RasterizerStateDescription rasterizer = new(
			FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false);
		DepthStencilStateDescription depthState = new(true, true, ComparisonKind.LessEqual);

		GraphicsPipelineDescription description = new(
			BlendStateDescription.Empty,
			depthState,
			rasterizer,
			PrimitiveTopology.TriangleList,
			shaderSet,
			[_cameraLayout],
			_occlusionFramebuffer!.OutputDescription);

		return Factory.CreateGraphicsPipeline(description);
	}

	private void CreateOcclusionTextures(int width, int height)
	{
		if (_occlusionDepthTexture != null || _hiZTexture != null)
			_graphicsDevice.WaitForIdle();

		DisposeOcclusionTextures();
		if (width <= 0 || height <= 0)
			return;

		_occlusionTextureWidth = width;
		_occlusionTextureHeight = height;
		_occlusionDepthTexture = Factory.CreateTexture(
			TextureDescription.Texture2D(
				(uint)width, (uint)height, 1, 1, PixelFormat.D32_Float_S8_UInt,
				TextureUsage.DepthStencil | TextureUsage.Sampled));
		_occlusionFramebuffer = Factory.CreateFramebuffer(
			new FramebufferDescription(_occlusionDepthTexture));
		_occlusionDepthView = Factory.CreateTextureView(_occlusionDepthTexture);

		int mipLevels = 1 + (int)MathF.Floor(MathF.Log2(Math.Max(width, height)));
		_hiZTexture = Factory.CreateTexture(
			TextureDescription.Texture2D(
				(uint)width, (uint)height, (uint)mipLevels, 1, PixelFormat.R32_Float,
				TextureUsage.Sampled | TextureUsage.Storage));
		_hiZFullView = Factory.CreateTextureView(
			new TextureViewDescription(
				_hiZTexture, PixelFormat.R32_Float, 0, (uint)mipLevels, 0, 1));
		_hiZMipViews = new TextureView[mipLevels];
		for (int mip = 0; mip < mipLevels; mip++)
		{
			_hiZMipViews[mip] = Factory.CreateTextureView(
				new TextureViewDescription(
					_hiZTexture, PixelFormat.R32_Float, (uint)mip, 1, 0, 1));
		}

		_hiZCopyResourceSet = Factory.CreateResourceSet(
			new ResourceSetDescription(
				_occlusionBuildLayout!, _occlusionDepthView, _linearSampler, _hiZMipViews[0]));

		_hiZDownsampleResourceSets = new ResourceSet[Math.Max(0, mipLevels - 1)];
		for (int mip = 1; mip < mipLevels; mip++)
		{
			_hiZDownsampleResourceSets[mip - 1] = Factory.CreateResourceSet(
				new ResourceSetDescription(
					_occlusionBuildLayout!, _hiZMipViews[mip - 1], _linearSampler, _hiZMipViews[mip]));
		}

		RecreateOcclusionCullResourceSet();
	}

	private void DisposeOcclusionTextures()
	{
		_hiZCopyResourceSet?.Dispose();
		_hiZCopyResourceSet = null;
		foreach (ResourceSet set in _hiZDownsampleResourceSets)
			set.Dispose();
		_hiZDownsampleResourceSets = [];
		_occlusionCullResourceSet?.Dispose();
		_occlusionCullResourceSet = null;
		_occlusionFramebuffer?.Dispose();
		_occlusionFramebuffer = null;
		_occlusionDepthView?.Dispose();
		_occlusionDepthView = null;
		_hiZFullView?.Dispose();
		_hiZFullView = null;
		foreach (TextureView view in _hiZMipViews)
			view.Dispose();
		_hiZMipViews = [];
		_occlusionDepthTexture?.Dispose();
		_occlusionDepthTexture = null;
		_hiZTexture?.Dispose();
		_hiZTexture = null;
		_occlusionTextureWidth = 0;
		_occlusionTextureHeight = 0;
	}

	private void EnsureOcclusionTextures()
	{
		if (!_occlusionCullingSupported ||
			(_occlusionTextureWidth == _renderWidth && _occlusionTextureHeight == _renderHeight))
			return;

		CreateOcclusionTextures(_renderWidth, _renderHeight);
	}

	private void EnsureOcclusionBuffersCapacity(int requiredCapacity)
	{
		if (_occlusionMetadataBuffer != null && _occlusionVisibleInstanceBuffer != null &&
			_occlusionIndirectBuffer != null && _occlusionBufferCapacity >= requiredCapacity)
			return;

		int capacity = Math.Max(4096, _occlusionBufferCapacity);
		while (capacity < requiredCapacity)
		{
			if (capacity > int.MaxValue / 2)
			{
				capacity = requiredCapacity;
				break;
			}
			capacity *= 2;
		}

		_occlusionCullResourceSet?.Dispose();
		_occlusionCullResourceSet = null;
		_occlusionMetadataBuffer?.Dispose();
		_occlusionVisibleInstanceBuffer?.Dispose();
		_occlusionIndirectBuffer?.Dispose();

		_occlusionMetadataBuffer = Factory.CreateBuffer(
			new BufferDescription(
				checked((uint)(capacity * OcclusionInstanceDataSize)),
				BufferUsage.StructuredBufferReadOnly,
				(uint)OcclusionInstanceDataSize));
		_occlusionVisibleInstanceBuffer = Factory.CreateBuffer(
			new BufferDescription(
				checked((uint)(capacity * InstanceDataSize)),
				BufferUsage.VertexBuffer | BufferUsage.StructuredBufferReadWrite,
				(uint)InstanceDataSize));
		_occlusionIndirectBuffer = Factory.CreateBuffer(
			new BufferDescription(
				checked((uint)(capacity * IndexedIndirectCommandSize)),
				BufferUsage.StructuredBufferReadWrite | BufferUsage.IndirectBuffer,
				(uint)IndexedIndirectCommandSize));
		_occlusionBufferCapacity = capacity;
		RecreateOcclusionCullResourceSet();
	}

	private void RecreateOcclusionCullResourceSet()
	{
		_occlusionCullResourceSet?.Dispose();
		_occlusionCullResourceSet = null;
		if (_occlusionCullLayout == null ||
			_instanceBuffer == null ||
			_occlusionMetadataBuffer == null ||
			_occlusionVisibleInstanceBuffer == null ||
			_occlusionIndirectBuffer == null ||
			_hiZFullView == null ||
			_occlusionCullParametersBuffer == null)
			return;

		_occlusionCullResourceSet = Factory.CreateResourceSet(
			new ResourceSetDescription(
				_occlusionCullLayout,
				_instanceBuffer!,
				_occlusionMetadataBuffer,
				_occlusionVisibleInstanceBuffer,
				_occlusionIndirectBuffer,
				_hiZFullView,
				_linearSampler,
				_occlusionCullParametersBuffer));
	}

	private SDL_Window* CreateWindow()
	{
		SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_VULKAN;

		if (_parameters.Fullscreen)
			flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;

		if (_parameters.Resizable)
			flags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

		byte[] title = System.Text.Encoding.UTF8.GetBytes(
			_parameters.Title + '\0');

		fixed (byte* titlePtr = title)
		{
			SDL_Window* window = SDL3.SDL_CreateWindow(
				titlePtr,
				_parameters.Width,
				_parameters.Height,
				flags);

			if (window == null)
			{
				string error = SDL3.SDL_GetError();
				throw new InvalidOperationException(
					$"Failed to create SDL3 window: {error}");
			}

			return window;
		}
	}

	private (int Width, int Height) GetWindowPixelSize()
	{
		int width = 0;
		int height = 0;

		if (!SDL3.SDL_GetWindowSizeInPixels(
			_window,
			&width,
			&height))
		{
			throw new InvalidOperationException(
				$"Failed to get window pixel size: {SDL3.SDL_GetError()}");
		}

		return (width, height);
	}

	private GraphicsDevice CreateGraphicsDevice()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException(
				"The current Veonex SDL3/Vulkan backend targets Windows.");
		}

		SDL_PropertiesID properties =
			SDL3.SDL_GetWindowProperties(_window);

		void* hwnd;
		void* hinstance;

		fixed (byte* hwndProperty =
			SDL3.SDL_PROP_WINDOW_WIN32_HWND_POINTER)
		fixed (byte* hinstanceProperty =
			SDL3.SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER)
		{
			hwnd = (void*)SDL3.SDL_GetPointerProperty(
				properties,
				hwndProperty,
				0);

			hinstance = (void*)SDL3.SDL_GetPointerProperty(
				properties,
				hinstanceProperty,
				0);
		}

		if (hwnd == null)
			throw new InvalidOperationException(
				"SDL3 did not provide a Win32 HWND.");

		if (hinstance == null)
			throw new InvalidOperationException(
				"SDL3 did not provide a Win32 HINSTANCE.");

		(int width, int height) = GetWindowPixelSize();

		GraphicsDeviceOptions options = new(
			debug: true,
			swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
			syncToVerticalBlank: _parameters.VSync,
			resourceBindingModel: ResourceBindingModel.Improved,
			preferDepthRangeZeroToOne: true,
			preferStandardClipSpaceYDirection: true);

		_renderWidth = width;
		_renderHeight = height;

		SwapchainSource swapchainSource = SwapchainSource.CreateWin32(
			(nint)hwnd,
			(nint)hinstance);

		SwapchainDescription swapchainDescription = new(
			swapchainSource,
			(uint)width,
			(uint)height,
			PixelFormat.D24_UNorm_S8_UInt,
			_parameters.VSync);

		return GraphicsDevice.CreateVulkan(
			options,
			swapchainDescription);
	}

	private Pipeline CreatePipeline(bool transparent)
	{
		VertexLayoutDescription positionLayout = new(
			new VertexElementDescription(
				"Position",
				VertexElementSemantic.Position,
				VertexElementFormat.Float3));

		VertexLayoutDescription normalLayout = new(
			new VertexElementDescription(
				"Normal",
				VertexElementSemantic.Normal,
				VertexElementFormat.Float3));

		VertexLayoutDescription uvLayout = new(
			new VertexElementDescription(
				"UV",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float2));

		VertexLayoutDescription instanceLayout = new(
			(uint)InstanceDataSize,
			1,
			new VertexElementDescription(
				"InstanceRow0",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				0),
			new VertexElementDescription(
				"InstanceRow1",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				16),
			new VertexElementDescription(
				"InstanceRow2",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				32),
			new VertexElementDescription(
				"InstanceRow3",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				48),
			new VertexElementDescription(
				"InstanceNormalRow0",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				64),
			new VertexElementDescription(
				"InstanceNormalRow1",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				80),
			new VertexElementDescription(
				"InstanceNormalRow2",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float4,
				96));

		ShaderSetDescription shaderSet = new(
			[
				positionLayout,
				normalLayout,
				uvLayout,
				instanceLayout
			],
			_shaders);

		RasterizerStateDescription rasterizer = new(
			FaceCullMode.Back,
			PolygonFillMode.Solid,
			FrontFace.Clockwise,
			depthClipEnabled: true,
			scissorTestEnabled: false);

		DepthStencilStateDescription depthState = new(
			depthTestEnabled: true,
			depthWriteEnabled: !transparent,
			comparisonKind: ComparisonKind.LessEqual);

		BlendStateDescription blendState = transparent
			? BlendStateDescription.SingleAlphaBlend
			: BlendStateDescription.SingleOverrideBlend;

		GraphicsPipelineDescription description = new(
			blendState,
			depthState,
			rasterizer,
			PrimitiveTopology.TriangleList,
			shaderSet,
			[
				_cameraLayout,
				_materialLayout
			],
			_graphicsDevice.SwapchainFramebuffer.OutputDescription);

		return Factory.CreateGraphicsPipeline(description);
	}

	private Pipeline CreateSkyPipeline()
	{
		ShaderSetDescription shaderSet = new([], _skyShaders);

		RasterizerStateDescription rasterizer = new(
			FaceCullMode.None,
			PolygonFillMode.Solid,
			FrontFace.Clockwise,
			depthClipEnabled: false,
			scissorTestEnabled: false);

		DepthStencilStateDescription depthState = new(
			depthTestEnabled: false,
			depthWriteEnabled: false,
			comparisonKind: ComparisonKind.Always);

		GraphicsPipelineDescription description = new(
			BlendStateDescription.SingleOverrideBlend,
			depthState,
			rasterizer,
			PrimitiveTopology.TriangleList,
			shaderSet,
			[_skyLayout],
			_graphicsDevice.SwapchainFramebuffer.OutputDescription);

		return Factory.CreateGraphicsPipeline(description);
	}

	public void RenderFrame(Scene scene, Camera camera)
	{
		if (_renderWidth <= 0 || _renderHeight <= 0)
			return;

		float aspect = (float)_renderWidth / _renderHeight;
		camera.AspectRatio = aspect;

		Matrix4x4 view = CreateViewMatrix(camera);

		Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
			(float)(camera.FieldOfView * Math.PI / 180.0),
			aspect,
			(float)camera.NearClip,
			(float)camera.FarClip);

		Matrix4x4 viewProjection = view * projection;

		if (!Matrix4x4.Invert(
			viewProjection,
			out Matrix4x4 inverseViewProjection))
		{
			return;
		}

		FrustumPlane[] frustumPlanes =
			CreateFrustumPlanes(viewProjection);

		foreach (DrawBatch batch in _drawBatches.Values)
			batch.Reset();

		foreach (TransparentBatch batch in _transparentBatches.Values)
			batch.Reset();

		_sortedTransparentBatches.Clear();

		Transform cameraTransform = camera.Entity.Get<Transform>();
		Vector3 cameraPosition = new(
			(float)cameraTransform.Position.X,
			(float)cameraTransform.Position.Y,
			(float)cameraTransform.Position.Z);

		foreach (Entity entity in scene.Entities)
		{
			if (!entity.Has<MeshRenderer>())
				continue;

			MeshRenderer renderer = entity.Get<MeshRenderer>();

			if (!renderer.Visible ||
				renderer.Mesh == null ||
				renderer.Material == null ||
				!renderer.Mesh.Data.HasNormals ||
				!entity.Has<Transform>())
			{
				continue;
			}

			Mesh mesh = renderer.Mesh;
			Material material = renderer.Material;

			// Полностью прозрачные материалы не отправляем на отрисовку.
			if (material.Opacity <= 0.0 || material.AlbedoColor.W <= 0.0)
				continue;

			BatchKey key = new(mesh.Id, material.Id);
			Transform transform = entity.Get<Transform>();
			InstanceData instance = CreateInstanceData(transform);

			MeshBounds bounds = GetOrCreateMeshBounds(mesh);
			Vector3 worldCenter = Vector3.Transform(
				bounds.Center,
				instance.Model);

			Vector3 scale = new(
				(float)transform.Scale.X,
				(float)transform.Scale.Y,
				(float)transform.Scale.Z);

			float maxScale = MathF.Max(
				MathF.Abs(scale.X),
				MathF.Max(
					MathF.Abs(scale.Y),
					MathF.Abs(scale.Z)));

			float worldRadius = bounds.Radius * maxScale;

			if (!IsSphereVisible(
				worldCenter,
				worldRadius,
				frustumPlanes))
			{
				continue;
			}

			if (IsTransparent(material))
			{
				float distanceSquared = Vector3.DistanceSquared(
					worldCenter,
					cameraPosition);

				if (!_transparentBatches.TryGetValue(
					key,
					out TransparentBatch? transparentBatch))
				{
					transparentBatch = new TransparentBatch(mesh, material);
					_transparentBatches.Add(key, transparentBatch);
				}

				transparentBatch.AddInstance(instance, distanceSquared);
			}
			else
			{
				if (!_drawBatches.TryGetValue(key, out DrawBatch? batch))
				{
					batch = new DrawBatch(mesh, material);
					_drawBatches.Add(key, batch);
				}

				batch.AddInstance(instance, worldCenter, worldRadius);
			}
		}

		int totalInstanceCount = 0;

		foreach (DrawBatch batch in _drawBatches.Values)
			totalInstanceCount = checked(totalInstanceCount + batch.InstanceCount);

		foreach (TransparentBatch batch in _transparentBatches.Values)
		{
			if (batch.InstanceCount == 0)
				continue;

			batch.SortInstances();
			_sortedTransparentBatches.Add(batch);
			totalInstanceCount = checked(totalInstanceCount + batch.InstanceCount);
		}

		_sortedTransparentBatches.Sort(static (left, right) =>
			right.SortDistanceSquared.CompareTo(left.SortDistanceSquared));

		int opaqueInstanceCount = 0;

		if (totalInstanceCount > 0)
		{
			EnsureInstanceUploadCapacity(totalInstanceCount);

			int instanceOffset = 0;
			int batchIndex = 0;

			foreach (DrawBatch batch in _drawBatches.Values)
			{
				if (batch.InstanceCount == 0)
					continue;

				batch.InstanceStart = instanceOffset;
				batch.BatchIndex = batchIndex;
				batch.CopyInstancesTo(_instanceUploadData, instanceOffset);
				batch.CopyOcclusionInstancesTo(_occlusionUploadData, instanceOffset);
				_occlusionIndirectUploadData[batchIndex] = new IndirectDrawIndexedArguments
				{
					IndexCount = checked((uint)batch.Mesh.Data.IndexCount),
					InstanceCount = 0,
					FirstIndex = 0,
					VertexOffset = 0,
					FirstInstance = 0
				};
				batchIndex++;
				instanceOffset += batch.InstanceCount;
			}

			opaqueInstanceCount = instanceOffset;

			foreach (TransparentBatch batch in _sortedTransparentBatches)
			{
				batch.InstanceStart = instanceOffset;
				batch.CopyInstancesTo(_instanceUploadData, instanceOffset);
				instanceOffset += batch.InstanceCount;
			}

			EnsureInstanceBufferCapacity(_instanceUploadData.Length);

			_graphicsDevice.UpdateBuffer(
				_instanceBuffer!,
				0,
				ref _instanceUploadData[0],
				checked((uint)(totalInstanceCount * InstanceDataSize)));

			if (_occlusionCullingSupported && opaqueInstanceCount >= 64)
			{
				EnsureOcclusionBuffersCapacity(totalInstanceCount);
				_graphicsDevice.UpdateBuffer(
					_occlusionMetadataBuffer!, 0, ref _occlusionUploadData[0],
					checked((uint)(opaqueInstanceCount * OcclusionInstanceDataSize)));
				int opaqueBatchCount = 0;
				foreach (DrawBatch batch in _drawBatches.Values)
				{
					if (batch.InstanceCount > 0)
						opaqueBatchCount++;
				}
				if (opaqueBatchCount > 0)
				{
					_graphicsDevice.UpdateBuffer(
						_occlusionIndirectBuffer!, 0, ref _occlusionIndirectUploadData[0],
						checked((uint)(opaqueBatchCount * IndexedIndirectCommandSize)));
				}
			}
		}

		bool useOcclusionCulling =
			_occlusionCullingSupported && opaqueInstanceCount >= 64;

		if (useOcclusionCulling)
		{
			EnsureOcclusionTextures();
			_graphicsDevice.UpdateBuffer(
				_occlusionCullParametersBuffer!,
				0,
				new OcclusionCullParameters(
					viewProjection,
					_renderWidth,
					_renderHeight,
					_hiZMipViews.Length,
					opaqueInstanceCount,
					_graphicsDevice.IsClipSpaceYInverted));
		}

		_graphicsDevice.UpdateBuffer(
			_cameraBuffer,
			0,
			new CameraBuffer(viewProjection));

		_graphicsDevice.UpdateBuffer(
			_skyBuffer,
			0,
			new SkyBuffer(inverseViewProjection));

		int skyDrawCalls = 0;
		int opaqueDrawCalls = 0;
		int transparentDrawCalls = 0;

		_commandList.Begin();

		if (useOcclusionCulling)
		{
			_commandList.SetFramebuffer(_occlusionFramebuffer!);
			_commandList.SetFullViewports();
			_commandList.ClearDepthStencil(1.0f);
			_commandList.SetPipeline(_occlusionDepthPipeline!);
			_commandList.SetGraphicsResourceSet(0, _cameraResourceSet);

			foreach (DrawBatch batch in _drawBatches.Values)
			{
				if (batch.InstanceCount == 0)
					continue;

				MeshBuffer meshBuffer = GetOrCreateMeshBuffer(batch.Mesh);
				_commandList.SetVertexBuffer(0, meshBuffer.PositionBuffer);
				if (meshBuffer.NormalBuffer != null)
					_commandList.SetVertexBuffer(1, meshBuffer.NormalBuffer);
				if (meshBuffer.UVBuffer != null)
					_commandList.SetVertexBuffer(2, meshBuffer.UVBuffer);
				_commandList.SetVertexBuffer(3, _instanceBuffer!, checked((uint)(batch.InstanceStart * InstanceDataSize)));
				_commandList.SetIndexBuffer(meshBuffer.IndexBuffer, IndexFormat.UInt32);
				_commandList.DrawIndexed(meshBuffer.IndexCount, (uint)batch.InstanceCount, 0, 0, 0);
			}

			_commandList.SetPipeline(_hiZCopyPipeline!);
			_commandList.SetComputeResourceSet(0, _hiZCopyResourceSet!);
			_commandList.Dispatch((uint)((_renderWidth + 7) / 8), (uint)((_renderHeight + 7) / 8), 1);

			_commandList.SetPipeline(_hiZDownsamplePipeline!);
			for (int mip = 1; mip < _hiZMipViews.Length; mip++)
			{
				_commandList.SetComputeResourceSet(0, _hiZDownsampleResourceSets[mip - 1]);
				int mipWidth = Math.Max(1, _renderWidth >> mip);
				int mipHeight = Math.Max(1, _renderHeight >> mip);
				_commandList.Dispatch((uint)((mipWidth + 7) / 8), (uint)((mipHeight + 7) / 8), 1);
			}

			_commandList.SetPipeline(_occlusionCullPipeline!);
			_commandList.SetComputeResourceSet(0, _occlusionCullResourceSet!);
			_commandList.Dispatch((uint)((opaqueInstanceCount + 63) / 64), 1, 1);
		}

		_commandList.SetFramebuffer(
			_graphicsDevice.SwapchainFramebuffer);

		_commandList.SetFullViewports();
		_commandList.ClearColorTarget(0, RgbaFloat.Black);
		_commandList.ClearDepthStencil(1.0f);

		_commandList.SetPipeline(_skyPipeline);
		_commandList.SetGraphicsResourceSet(0, _skyResourceSet);
		_commandList.Draw(3);
		skyDrawCalls++;

		_commandList.SetPipeline(_pipeline);
		_commandList.SetGraphicsResourceSet(0, _cameraResourceSet);

		foreach (KeyValuePair<BatchKey, DrawBatch> pair in _drawBatches)
		{
			DrawBatch batch = pair.Value;

			if (batch.InstanceCount == 0)
				continue;

			Material material = batch.Material;
			MaterialResources materialResources =
				GetOrCreateMaterialResources(material);

			Vector4 albedoColor = new(
				(float)material.AlbedoColor.X,
				(float)material.AlbedoColor.Y,
				(float)material.AlbedoColor.Z,
				(float)material.AlbedoColor.W);

			_graphicsDevice.UpdateBuffer(
				materialResources.Buffer,
				0,
				new MaterialBuffer(albedoColor, (float)material.Opacity));

			MeshBuffer meshBuffer = GetOrCreateMeshBuffer(batch.Mesh);

			_commandList.SetGraphicsResourceSet(
				1,
				materialResources.ResourceSet);

			_commandList.SetVertexBuffer(
				0,
				meshBuffer.PositionBuffer);

			if (meshBuffer.NormalBuffer != null)
			{
				_commandList.SetVertexBuffer(
					1,
					meshBuffer.NormalBuffer);
			}

			if (meshBuffer.UVBuffer != null)
			{
				_commandList.SetVertexBuffer(
					2,
					meshBuffer.UVBuffer);
			}

			uint instanceBufferOffset = checked(
				(uint)(batch.InstanceStart * InstanceDataSize));

			_commandList.SetVertexBuffer(
				3,
				useOcclusionCulling
					? _occlusionVisibleInstanceBuffer!
					: _instanceBuffer!,
				instanceBufferOffset);

			_commandList.SetIndexBuffer(
				meshBuffer.IndexBuffer,
				IndexFormat.UInt32);

			if (useOcclusionCulling)
			{
				_commandList.DrawIndexedIndirect(
					_occlusionIndirectBuffer!,
					checked((uint)(batch.BatchIndex * IndexedIndirectCommandSize)),
					1,
					(uint)IndexedIndirectCommandSize);
			}
			else
			{
				_commandList.DrawIndexed(
					meshBuffer.IndexCount,
					(uint)batch.InstanceCount,
					0,
					0,
					0);
			}
			opaqueDrawCalls++;
		}

		if (_sortedTransparentBatches.Count > 0)
		{
			_commandList.SetPipeline(_transparentPipeline);
			_commandList.SetGraphicsResourceSet(0, _cameraResourceSet);

			foreach (TransparentBatch batch in _sortedTransparentBatches)
			{
				Material material = batch.Material;
				MaterialResources materialResources =
					GetOrCreateMaterialResources(material);

				Vector4 albedoColor = new(
					(float)material.AlbedoColor.X,
					(float)material.AlbedoColor.Y,
					(float)material.AlbedoColor.Z,
					(float)material.AlbedoColor.W);

				_graphicsDevice.UpdateBuffer(
					materialResources.Buffer,
					0,
					new MaterialBuffer(albedoColor, (float)material.Opacity));

				MeshBuffer meshBuffer = GetOrCreateMeshBuffer(batch.Mesh);

				_commandList.SetGraphicsResourceSet(
					1,
					materialResources.ResourceSet);

				_commandList.SetVertexBuffer(0, meshBuffer.PositionBuffer);

				if (meshBuffer.NormalBuffer != null)
					_commandList.SetVertexBuffer(1, meshBuffer.NormalBuffer);

				if (meshBuffer.UVBuffer != null)
					_commandList.SetVertexBuffer(2, meshBuffer.UVBuffer);

				uint instanceBufferOffset = checked(
					(uint)(batch.InstanceStart * InstanceDataSize));

				_commandList.SetVertexBuffer(
					3,
					_instanceBuffer!,
					instanceBufferOffset);

				_commandList.SetIndexBuffer(
					meshBuffer.IndexBuffer,
					IndexFormat.UInt32);

				_commandList.DrawIndexed(
					meshBuffer.IndexCount,
					(uint)batch.InstanceCount,
					0,
					0,
					0);
				transparentDrawCalls++;
			}
		}

		_commandList.End();

		_graphicsDevice.SubmitCommands(_commandList);
		_graphicsDevice.SwapBuffers();

		long currentTime = Environment.TickCount64;

		if (currentTime - _lastDrawCallLogTime >= 1000)
		{
			_lastDrawCallLogTime = currentTime;

			int totalDrawCalls =
				skyDrawCalls +
				opaqueDrawCalls +
				transparentDrawCalls;

			System.Console.WriteLine(
				$"Draw calls: {totalDrawCalls} " +
				$"(Sky: {skyDrawCalls}, Opaque: {opaqueDrawCalls}, " +
				$"Transparent: {transparentDrawCalls}) | " +
				$"Instances: {totalInstanceCount}");
		}
	}

	private bool IsTransparent(Material material) =>
		material.Opacity < 1.0 ||
		material.AlbedoColor.W < 1.0 ||
		material.UseAlphaBlending ||
		TextureHasAlpha(material.Albedo);

	private bool TextureHasAlpha(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;

		string fullPath = Path.GetFullPath(path);

		if (_textureAlphaCache.TryGetValue(fullPath, out bool cached))
			return cached;

		bool hasAlpha = false;

		try
		{
			using FileStream stream = File.OpenRead(fullPath);
			byte[] signature = new byte[8];

			if (stream.Read(signature, 0, signature.Length) == signature.Length &&
				signature.AsSpan().SequenceEqual(new byte[]
				{ 137, 80, 78, 71, 13, 10, 26, 10 }))
			{
				byte[] lengthBytes = new byte[4];
				byte[] typeBytes = new byte[4];

				while (stream.Position + 8 <= stream.Length)
				{
					if (stream.Read(lengthBytes, 0, 4) != 4 ||
						stream.Read(typeBytes, 0, 4) != 4)
						break;

					uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
					string chunkType = System.Text.Encoding.ASCII.GetString(typeBytes);

					if (chunkLength > stream.Length - stream.Position - 4)
						break;

					if (chunkType == "IHDR")
					{
						if (chunkLength != 13)
							break;

						byte[] header = new byte[13];

						if (stream.Read(header, 0, header.Length) != header.Length)
							break;

						hasAlpha = header[9] is 4 or 6;
						stream.Seek(4, SeekOrigin.Current);

						if (hasAlpha)
							break;
					}
					else if (chunkType == "tRNS")
					{
						hasAlpha = true;
						break;
					}
					else
					{
						stream.Seek(chunkLength + 4L, SeekOrigin.Current);
					}

					if (chunkType == "IEND")
						break;
				}
			}
		}
		catch (IOException)
		{
			hasAlpha = false;
		}
		catch (UnauthorizedAccessException)
		{
			hasAlpha = false;
		}

		_textureAlphaCache[fullPath] = hasAlpha;
		return hasAlpha;
	}

	private void EnsureInstanceUploadCapacity(int requiredCount)
	{
		if (_instanceUploadData.Length >= requiredCount &&
			_occlusionUploadData.Length >= requiredCount &&
			_occlusionIndirectUploadData.Length >= requiredCount)
			return;

		int capacity = Math.Max(1, Math.Max(
			Math.Max(_instanceUploadData.Length, _occlusionUploadData.Length),
			_occlusionIndirectUploadData.Length));

		while (capacity < requiredCount)
		{
			if (capacity > int.MaxValue / 2)
			{
				capacity = requiredCount;
				break;
			}

			capacity *= 2;
		}

		Array.Resize(ref _instanceUploadData, capacity);
		Array.Resize(ref _occlusionUploadData, capacity);
		Array.Resize(ref _occlusionIndirectUploadData, capacity);
	}

	private void EnsureInstanceBufferCapacity(int requiredCapacity)
	{
		if (_instanceBuffer != null &&
			_instanceBufferCapacity >= requiredCapacity)
		{
			return;
		}

		int capacity = _instanceBufferCapacity == 0
			? 4096
			: _instanceBufferCapacity;

		while (capacity < requiredCapacity)
		{
			if (capacity > int.MaxValue / 2)
			{
				capacity = requiredCapacity;
				break;
			}

			capacity *= 2;
		}

		uint bufferSize = checked((uint)(capacity * InstanceDataSize));

		BufferUsage instanceBufferUsage = BufferUsage.VertexBuffer;
		uint instanceStride = 0;
		if (_occlusionCullingSupported)
		{
			instanceBufferUsage |= BufferUsage.StructuredBufferReadOnly;
			instanceStride = (uint)InstanceDataSize;
		}

		DeviceBuffer newBuffer = Factory.CreateBuffer(
			new BufferDescription(bufferSize, instanceBufferUsage, instanceStride));

		if (_occlusionCullingSupported)
		{
			_occlusionCullResourceSet?.Dispose();
			_occlusionCullResourceSet = null;
		}

		_instanceBuffer?.Dispose();
		_instanceBuffer = newBuffer;
		_instanceBufferCapacity = capacity;
		if (_occlusionCullingSupported)
			RecreateOcclusionCullResourceSet();
	}

	private Texture GetOrCreateTexture(string path)
	{
		string fullPath = Path.GetFullPath(path);

		if (_textureCache.TryGetValue(fullPath, out Texture? existing))
			return existing;

		Texture texture = TextureLoader.Load(
			Factory,
			_graphicsDevice,
			fullPath);

		_textureCache.Add(fullPath, texture);
		return texture;
	}

	private MaterialResources GetOrCreateMaterialResources(Material material)
	{
		if (_materialCache.TryGetValue(
			material.Id,
			out MaterialResources? existing))
		{
			return existing;
		}

		DeviceBuffer buffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)Marshal.SizeOf<MaterialBuffer>(),
				BufferUsage.UniformBuffer));

		Texture texture = string.IsNullOrWhiteSpace(material.Albedo)
			? _whiteTexture
			: GetOrCreateTexture(material.Albedo);

		ResourceSet resourceSet = Factory.CreateResourceSet(
			new ResourceSetDescription(
				_materialLayout,
				buffer,
				texture,
				_linearSampler));

		MaterialResources resources = new(buffer, resourceSet);
		_materialCache.Add(material.Id, resources);

		return resources;
	}

	private MeshBuffer GetOrCreateMeshBuffer(Mesh mesh)
	{
		if (_meshCache.TryGetValue(mesh.Id, out MeshBuffer? existing))
			return existing;

		MeshBuffer created = CreateMeshBuffer(mesh);
		_meshCache.Add(mesh.Id, created);

		return created;
	}

	private MeshBounds GetOrCreateMeshBounds(Mesh mesh)
	{
		if (_meshBoundsCache.TryGetValue(mesh.Id, out MeshBounds existing))
			return existing;

		MeshData data = mesh.Data;

		if (data.VertexCount == 0)
		{
			MeshBounds emptyBounds = new(Vector3.Zero, 0f);
			_meshBoundsCache.Add(mesh.Id, emptyBounds);
			return emptyBounds;
		}

		Vector3 firstPosition = new(
			(float)data.Positions[0].X,
			(float)data.Positions[0].Y,
			(float)data.Positions[0].Z);

		Vector3 min = firstPosition;
		Vector3 max = firstPosition;

		for (int i = 1; i < data.VertexCount; i++)
		{
			Vector3 position = new(
				(float)data.Positions[i].X,
				(float)data.Positions[i].Y,
				(float)data.Positions[i].Z);

			min = Vector3.Min(min, position);
			max = Vector3.Max(max, position);
		}

		Vector3 center = (min + max) * 0.5f;
		float radiusSquared = 0f;

		for (int i = 0; i < data.VertexCount; i++)
		{
			Vector3 position = new(
				(float)data.Positions[i].X,
				(float)data.Positions[i].Y,
				(float)data.Positions[i].Z);

			radiusSquared = MathF.Max(
				radiusSquared,
				Vector3.DistanceSquared(center, position));
		}

		MeshBounds bounds = new(center, MathF.Sqrt(radiusSquared));
		_meshBoundsCache.Add(mesh.Id, bounds);

		return bounds;
	}

	private static FrustumPlane[] CreateFrustumPlanes(Matrix4x4 matrix)
	{
		return
		[
			new FrustumPlane(
				new Vector3(
					matrix.M11 + matrix.M14,
					matrix.M21 + matrix.M24,
					matrix.M31 + matrix.M34),
				matrix.M41 + matrix.M44),

			new FrustumPlane(
				new Vector3(
					matrix.M14 - matrix.M11,
					matrix.M24 - matrix.M21,
					matrix.M34 - matrix.M31),
				matrix.M44 - matrix.M41),

			new FrustumPlane(
				new Vector3(
					matrix.M12 + matrix.M14,
					matrix.M22 + matrix.M24,
					matrix.M32 + matrix.M34),
				matrix.M42 + matrix.M44),

			new FrustumPlane(
				new Vector3(
					matrix.M14 - matrix.M12,
					matrix.M24 - matrix.M22,
					matrix.M34 - matrix.M32),
				matrix.M44 - matrix.M42),

			new FrustumPlane(
				new Vector3(
					matrix.M13,
					matrix.M23,
					matrix.M33),
				matrix.M43),

			new FrustumPlane(
				new Vector3(
					matrix.M14 - matrix.M13,
					matrix.M24 - matrix.M23,
					matrix.M34 - matrix.M33),
				matrix.M44 - matrix.M43)
		];
	}

	private static bool IsSphereVisible(
		Vector3 center,
		float radius,
		FrustumPlane[] planes)
	{
		foreach (FrustumPlane plane in planes)
		{
			float distance =
				Vector3.Dot(plane.Normal, center) +
				plane.Distance;

			if (distance < -radius)
				return false;
		}

		return true;
	}

	private MeshBuffer CreateMeshBuffer(Mesh mesh)
	{
		MeshData data = mesh.Data;

		Vector3[] positions = new Vector3[data.VertexCount];

		for (int i = 0; i < data.VertexCount; i++)
		{
			positions[i] = new Vector3(
				(float)data.Positions[i].X,
				(float)data.Positions[i].Y,
				(float)data.Positions[i].Z);
		}

		DeviceBuffer positionBuffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)(data.VertexCount * sizeof(float) * 3),
				BufferUsage.VertexBuffer));

		_graphicsDevice.UpdateBuffer(positionBuffer, 0, positions);

		DeviceBuffer? normalBuffer = null;

		if (data.HasNormals)
		{
			Vector3[] normals = new Vector3[data.VertexCount];

			for (int i = 0; i < data.VertexCount; i++)
			{
				normals[i] = new Vector3(
					(float)data.Normals[i].X,
					(float)data.Normals[i].Y,
					(float)data.Normals[i].Z);
			}

			normalBuffer = Factory.CreateBuffer(
				new BufferDescription(
					(uint)(data.VertexCount * sizeof(float) * 3),
					BufferUsage.VertexBuffer));

			_graphicsDevice.UpdateBuffer(normalBuffer, 0, normals);
		}

		DeviceBuffer? uvBuffer = null;

		if (data.HasUVs)
		{
			Vector2[] uvs = new Vector2[data.VertexCount];

			for (int i = 0; i < data.VertexCount; i++)
			{
				uvs[i] = new Vector2(
					(float)data.UVs[i].X,
					(float)data.UVs[i].Y);
			}

			uvBuffer = Factory.CreateBuffer(
				new BufferDescription(
					(uint)(data.VertexCount * sizeof(float) * 2),
					BufferUsage.VertexBuffer));

			_graphicsDevice.UpdateBuffer(uvBuffer, 0, uvs);
		}

		DeviceBuffer indexBuffer = Factory.CreateBuffer(
			new BufferDescription(
				(uint)(data.IndexCount * sizeof(uint)),
				BufferUsage.IndexBuffer));

		_graphicsDevice.UpdateBuffer(indexBuffer, 0, data.Indices);

		return new MeshBuffer(
			positionBuffer,
			indexBuffer,
			(uint)data.IndexCount,
			normalBuffer,
			uvBuffer);
	}

	private static InstanceData CreateInstanceData(Transform transform)
	{
		Vector3 position = new(
			(float)transform.Position.X,
			(float)transform.Position.Y,
			(float)transform.Position.Z);

		Vector3 scale = new(
			(float)transform.Scale.X,
			(float)transform.Scale.Y,
			(float)transform.Scale.Z);

		Vector3 rotation = new(
			(float)transform.Rotation.X,
			(float)transform.Rotation.Y,
			(float)transform.Rotation.Z);

		float radiansX = rotation.X * (MathF.PI / 180.0f);
		float radiansY = rotation.Y * (MathF.PI / 180.0f);
		float radiansZ = rotation.Z * (MathF.PI / 180.0f);

		Quaternion quaternion = Quaternion.CreateFromYawPitchRoll(
			radiansY,
			radiansX,
			radiansZ);

		Matrix4x4 model = Matrix4x4.CreateScale(scale) *
						  Matrix4x4.CreateFromQuaternion(quaternion) *
						  Matrix4x4.CreateTranslation(position);

		Matrix4x4 normalMatrix = Matrix4x4.Identity;

		if (Matrix4x4.Invert(model, out Matrix4x4 inverseModel))
			normalMatrix = Matrix4x4.Transpose(inverseModel);

		return new InstanceData
		{
			Model = model,
			NormalRow0 = new Vector4(
				normalMatrix.M11,
				normalMatrix.M12,
				normalMatrix.M13,
				normalMatrix.M14),
			NormalRow1 = new Vector4(
				normalMatrix.M21,
				normalMatrix.M22,
				normalMatrix.M23,
				normalMatrix.M24),
			NormalRow2 = new Vector4(
				normalMatrix.M31,
				normalMatrix.M32,
				normalMatrix.M33,
				normalMatrix.M34)
		};
	}

	private static Matrix4x4 CreateViewMatrix(Camera camera)
	{
		Transform transform = camera.Entity.Get<Transform>();

		Vector3 position = new(
			(float)transform.Position.X,
			(float)transform.Position.Y,
			(float)transform.Position.Z);

		DQuaternion rotation = transform.Quaternion;

		DVector3 forwardD =
			rotation * new DVector3(0.0, 0.0, 1.0);

		DVector3 upD =
			rotation * new DVector3(0.0, 1.0, 0.0);

		Vector3 forward = new(
			(float)forwardD.X,
			(float)forwardD.Y,
			(float)forwardD.Z);

		Vector3 up = new(
			(float)upD.X,
			(float)upD.Y,
			(float)upD.Z);

		return Matrix4x4.CreateLookAt(
			position,
			position + forward,
			up);
	}

	public void ResizeRenderTarget(int width, int height)
	{
		if (width <= 0 || height <= 0)
			return;

		if (_renderWidth == width && _renderHeight == height)
			return;

		_graphicsDevice.ResizeMainWindow((uint)width, (uint)height);

		_renderWidth = width;
		_renderHeight = height;
	}

	public void ToggleFullscreen()
	{
		_fullscreen = !_fullscreen;
		SDL3.SDL_SetWindowFullscreen(_window, _fullscreen);
	}

	public void SetFullscreen(bool fullscreen)
	{
		if (_fullscreen == fullscreen)
			return;

		_fullscreen = fullscreen;
		SDL3.SDL_SetWindowFullscreen(_window, fullscreen);
	}

	public void SetMouseCapture(bool captured)
	{
		if (MouseCaptured == captured)
			return;

		if (!SDL3.SDL_SetWindowRelativeMouseMode(_window, captured))
		{
			throw new InvalidOperationException(
				$"Failed to change mouse capture mode: {SDL3.SDL_GetError()}");
		}

		MouseCaptured = captured;
	}

	private void DisposeOcclusionResources()
	{
		_occlusionCullResourceSet?.Dispose();
		_occlusionCullResourceSet = null;
		DisposeOcclusionTextures();

		_occlusionDepthPipeline?.Dispose();
		_occlusionDepthPipeline = null;
		_hiZCopyPipeline?.Dispose();
		_hiZCopyPipeline = null;
		_hiZDownsamplePipeline?.Dispose();
		_hiZDownsamplePipeline = null;
		_occlusionCullPipeline?.Dispose();
		_occlusionCullPipeline = null;

		if (_occlusionDepthShaders != null)
		{
			foreach (Shader shader in _occlusionDepthShaders)
				shader.Dispose();
			_occlusionDepthShaders = null;
		}
		_hiZCopyShader?.Dispose();
		_hiZCopyShader = null;
		_hiZDownsampleShader?.Dispose();
		_hiZDownsampleShader = null;
		_occlusionCullShader?.Dispose();
		_occlusionCullShader = null;

		_occlusionMetadataBuffer?.Dispose();
		_occlusionMetadataBuffer = null;
		_occlusionVisibleInstanceBuffer?.Dispose();
		_occlusionVisibleInstanceBuffer = null;
		_occlusionIndirectBuffer?.Dispose();
		_occlusionIndirectBuffer = null;
		_occlusionBufferCapacity = 0;
		_occlusionCullParametersBuffer?.Dispose();
		_occlusionCullParametersBuffer = null;

		_occlusionBuildLayout?.Dispose();
		_occlusionBuildLayout = null;
		_occlusionCullLayout?.Dispose();
		_occlusionCullLayout = null;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		_commandList.Dispose();

		_pipeline.Dispose();
		_transparentPipeline.Dispose();
		_skyPipeline.Dispose();

		DisposeOcclusionResources();

		foreach (Shader shader in _shaders)
			shader.Dispose();

		foreach (Shader shader in _skyShaders)
			shader.Dispose();

		foreach (MaterialResources resources in _materialCache.Values)
			resources.Dispose();

		_materialCache.Clear();

		foreach (Texture texture in _textureCache.Values)
			texture.Dispose();

		_textureCache.Clear();
		_textureAlphaCache.Clear();

		foreach (MeshBuffer meshBuffer in _meshCache.Values)
			meshBuffer.Dispose();

		_meshCache.Clear();
		_meshBoundsCache.Clear();
		_drawBatches.Clear();
		_transparentBatches.Clear();
		_sortedTransparentBatches.Clear();

		_instanceBuffer?.Dispose();
		_instanceBuffer = null;
		_instanceBufferCapacity = 0;
		_instanceUploadData = Array.Empty<InstanceData>();
		_occlusionUploadData = Array.Empty<OcclusionInstanceData>();
		_occlusionIndirectUploadData = Array.Empty<IndirectDrawIndexedArguments>();

		_cameraResourceSet.Dispose();
		_cameraBuffer.Dispose();
		_cameraLayout.Dispose();

		_materialLayout.Dispose();

		_whiteTexture.Dispose();
		_linearSampler.Dispose();

		_skyResourceSet.Dispose();
		_skyBuffer.Dispose();
		_skyLayout.Dispose();

		_graphicsDevice.Dispose();

		if (_window != null)
			SDL3.SDL_DestroyWindow(_window);
	}
}
