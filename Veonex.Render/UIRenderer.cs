using System.Numerics;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Veonex.Assets;
using Veonex.UI.Core;
using Veonex.UI.Layout;

namespace Veonex.Render;

public sealed class UIRenderer : IDisposable
{
	private const int InitialVertexCapacity = 256;

	private readonly GraphicsDevice _graphicsDevice;
	private readonly ResourceFactory _factory;
	private readonly Shader[] _shaders;
	private readonly Pipeline _pipeline;

	private DeviceBuffer _vertexBuffer;
	private UIVertex[] _vertices = new UIVertex[InitialVertexCapacity];
	private int _vertexCapacity = InitialVertexCapacity;
	private int _vertexCount;
	private bool _disposed;

	private static readonly int VertexSize =
		Marshal.SizeOf<UIVertex>();

	[StructLayout(LayoutKind.Sequential)]
	private struct UIVertex
	{
		public Vector2 Position;
		public Vector4 Color;

		public UIVertex(Vector2 position, Vector4 color)
		{
			Position = position;
			Color = color;
		}
	}

	public UIRenderer(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		OutputDescription outputDescription)
	{
		_factory = factory;
		_graphicsDevice = graphicsDevice;

		string shaderPath = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders",
			"UI.ves");

		_shaders = ShaderLoader.Load(factory, shaderPath);

		VertexLayoutDescription vertexLayout = new(
			new VertexElementDescription(
				"Position",
				VertexElementSemantic.Position,
				VertexElementFormat.Float2),
			new VertexElementDescription(
				"Color",
				VertexElementSemantic.Color,
				VertexElementFormat.Float4));

		ShaderSetDescription shaderSet = new(
			[vertexLayout],
			_shaders);

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
			BlendStateDescription.SingleAlphaBlend,
			depthState,
			rasterizer,
			PrimitiveTopology.TriangleList,
			shaderSet,
			[],
			outputDescription);

		_pipeline = factory.CreateGraphicsPipeline(description);

		_vertexBuffer = CreateVertexBuffer(_vertexCapacity);
	}

	public bool Prepare(
		UIDocument document,
		int width,
		int height)
	{
		ArgumentNullException.ThrowIfNull(document);

		_vertexCount = 0;

		if (width <= 0 || height <= 0)
			return false;

		CollectElement(document.Root, width, height);

		if (_vertexCount == 0)
			return false;

		EnsureVertexCapacity(_vertexCount);

		_graphicsDevice.UpdateBuffer(
			_vertexBuffer,
			0,
			ref _vertices[0],
			checked((uint)(_vertexCount * VertexSize)));

		return true;
	}

	public void Record(CommandList commandList)
	{
		ArgumentNullException.ThrowIfNull(commandList);

		if (_vertexCount == 0)
			return;

		commandList.SetPipeline(_pipeline);
		commandList.SetVertexBuffer(0, _vertexBuffer);
		commandList.Draw((uint)_vertexCount);
	}

	private void CollectElement(
		VisualElement element,
		int width,
		int height)
	{
		if (!element.Visible)
			return;

		UIRect rect = element.Rect;
		Vector4 color = element.BackgroundColor;

		bool validRect =
			float.IsFinite(rect.X) &&
			float.IsFinite(rect.Y) &&
			float.IsFinite(rect.Width) &&
			float.IsFinite(rect.Height) &&
			rect.Width > 0.0f &&
			rect.Height > 0.0f;

		bool validColor =
			float.IsFinite(color.X) &&
			float.IsFinite(color.Y) &&
			float.IsFinite(color.Z) &&
			float.IsFinite(color.W) &&
			color.W > 0.0f;

		if (validRect && validColor)
		{
			AddRectangle(rect, Vector4.Clamp(
				color,
				Vector4.Zero,
				Vector4.One), width, height);
		}

		foreach (VisualElement child in element.Children)
			CollectElement(child, width, height);
	}

	private void AddRectangle(
		UIRect rect,
		Vector4 color,
		int width,
		int height)
	{
		EnsureVertexCapacity(checked(_vertexCount + 6));

		float left = rect.X * (2.0f / width) - 1.0f;
		float right = (rect.X + rect.Width) * (2.0f / width) - 1.0f;

		float top = 1.0f - rect.Y * (2.0f / height);
		float bottom = 1.0f -
			(rect.Y + rect.Height) * (2.0f / height);

		Vector2 topLeft = new(left, top);
		Vector2 topRight = new(right, top);
		Vector2 bottomLeft = new(left, bottom);
		Vector2 bottomRight = new(right, bottom);

		_vertices[_vertexCount++] = new UIVertex(topLeft, color);
		_vertices[_vertexCount++] = new UIVertex(topRight, color);
		_vertices[_vertexCount++] = new UIVertex(bottomRight, color);

		_vertices[_vertexCount++] = new UIVertex(topLeft, color);
		_vertices[_vertexCount++] = new UIVertex(bottomRight, color);
		_vertices[_vertexCount++] = new UIVertex(bottomLeft, color);
	}

	private void EnsureVertexCapacity(int requiredCount)
	{
		if (requiredCount <= _vertexCapacity)
			return;

		int capacity = _vertexCapacity;

		while (capacity < requiredCount)
		{
			if (capacity > int.MaxValue / 2)
			{
				capacity = requiredCount;
				break;
			}

			capacity *= 2;
		}

		Array.Resize(ref _vertices, capacity);

		DeviceBuffer newBuffer = CreateVertexBuffer(capacity);
		_vertexBuffer.Dispose();

		_vertexBuffer = newBuffer;
		_vertexCapacity = capacity;
	}

	private DeviceBuffer CreateVertexBuffer(int capacity)
	{
		return _factory.CreateBuffer(
			new BufferDescription(
				checked((uint)(capacity * VertexSize)),
				BufferUsage.VertexBuffer));
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		_pipeline.Dispose();
		_vertexBuffer.Dispose();

		foreach (Shader shader in _shaders)
			shader.Dispose();
	}
}