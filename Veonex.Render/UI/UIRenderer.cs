using System.Numerics;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Veonex.Assets;
using Veonex.UI.Controls;
using Veonex.UI.Core;
using Veonex.UI.Layout;

namespace Veonex.Render;

public sealed class UIRenderer : IDisposable
{
	private const int InitialVertexCapacity = 256;

	private readonly ResourceFactory _factory;
	private readonly GraphicsDevice _graphicsDevice;

	private readonly ResourceLayout _textureLayout;
	private readonly Sampler _sampler;
	private readonly Texture _whiteTexture;
	private readonly ResourceSet _whiteResourceSet;

	private readonly Shader[] _shaders;
	private readonly Pipeline _pipeline;

	private readonly Dictionary<string, UIFontAtlas> _fontAtlases =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly List<DrawBatch> _batches = [];

	private DeviceBuffer _vertexBuffer;
	private UIVertex[] _vertices = new UIVertex[InitialVertexCapacity];
	private int _vertexCapacity = InitialVertexCapacity;
	private int _vertexCount;

	private DrawBatch? _currentBatch;
	private bool _disposed;

	private static readonly int VertexSize =
		Marshal.SizeOf<UIVertex>();

	[StructLayout(LayoutKind.Sequential)]
	private struct UIVertex
	{
		public Vector2 Position;
		public Vector3 UV;
		public Vector4 Color;

		public UIVertex(
			Vector2 position,
			Vector3 uv,
			Vector4 color)
		{
			Position = position;
			UV = uv;
			Color = color;
		}
	}

	private sealed class DrawBatch
	{
		public ResourceSet Resources { get; }

		public int StartVertex { get; }

		public int VertexCount { get; set; }

		public DrawBatch(ResourceSet resources, int startVertex)
		{
			Resources = resources;
			StartVertex = startVertex;
		}
	}

	public UIRenderer(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		OutputDescription outputDescription)
	{
		_factory = factory;
		_graphicsDevice = graphicsDevice;

		_sampler = factory.CreateSampler(
			new SamplerDescription(
				SamplerAddressMode.Clamp,
				SamplerAddressMode.Clamp,
				SamplerAddressMode.Clamp,
				SamplerFilter.MinLinear_MagLinear_MipLinear,
				null,
				1,
				0,
				0,
				0,
				SamplerBorderColor.TransparentBlack));

		_textureLayout = factory.CreateResourceLayout(
			new ResourceLayoutDescription(
				new ResourceLayoutElementDescription(
					"GlyphTexture",
					ResourceKind.TextureReadOnly,
					ShaderStages.Fragment),
				new ResourceLayoutElementDescription(
					"GlyphSampler",
					ResourceKind.Sampler,
					ShaderStages.Fragment)));

		_whiteTexture = factory.CreateTexture(
			TextureDescription.Texture2D(
				1,
				1,
				1,
				1,
				PixelFormat.R8_UNorm,
				TextureUsage.Sampled));

		byte[] whitePixel = [255];

		graphicsDevice.UpdateTexture(
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

		_whiteResourceSet = factory.CreateResourceSet(
			new ResourceSetDescription(
				_textureLayout,
				_whiteTexture,
				_sampler));

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
				"UV",
				VertexElementSemantic.TextureCoordinate,
				VertexElementFormat.Float3),
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
			[_textureLayout],
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
		_batches.Clear();
		_currentBatch = null;

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

		foreach (DrawBatch batch in _batches)
		{
			if (batch.VertexCount == 0)
				continue;

			commandList.SetGraphicsResourceSet(
				0,
				batch.Resources);

			commandList.Draw(
				(uint)batch.VertexCount,
				1,
				(uint)batch.StartVertex,
				0);
		}
	}

	private void CollectElement(
		VisualElement element,
		int width,
		int height)
	{
		if (!element.Visible)
			return;

		UIRect rect = element.Rect;
		Vector4 backgroundColor = element.BackgroundColor;

		bool validRect =
			float.IsFinite(rect.X) &&
			float.IsFinite(rect.Y) &&
			float.IsFinite(rect.Width) &&
			float.IsFinite(rect.Height) &&
			rect.Width > 0.0f &&
			rect.Height > 0.0f;

		bool validBackground =
			float.IsFinite(backgroundColor.X) &&
			float.IsFinite(backgroundColor.Y) &&
			float.IsFinite(backgroundColor.Z) &&
			float.IsFinite(backgroundColor.W) &&
			backgroundColor.W > 0.0f;

		if (validRect && validBackground)
		{
			AddRectangle(
				rect,
				Vector4.Clamp(
					backgroundColor,
					Vector4.Zero,
					Vector4.One),
				width,
				height);
		}

		if (element is UILabel label &&
			!string.IsNullOrEmpty(label.Text))
		{
			AddText(label, width, height);
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
		DrawBatch batch = BeginBatch(_whiteResourceSet);

		Vector2 topLeft = ToScreen(rect.X, rect.Y, width, height);
		Vector2 topRight = ToScreen(
			rect.X + rect.Width,
			rect.Y,
			width,
			height);
		Vector2 bottomLeft = ToScreen(
			rect.X,
			rect.Y + rect.Height,
			width,
			height);
		Vector2 bottomRight = ToScreen(
			rect.X + rect.Width,
			rect.Y + rect.Height,
			width,
			height);

		AddQuad(
			batch,
			topLeft,
			topRight,
			bottomRight,
			bottomLeft,
			Vector3.Zero,
			Vector3.Zero,
			Vector3.Zero,
			Vector3.Zero,
			color);
	}

	private void AddText(
		UILabel label,
		int width,
		int height)
	{
		if (!float.IsFinite(label.Rect.X) ||
			!float.IsFinite(label.Rect.Y))
		{
			return;
		}

		if (label.FontSize is < 8 or > 128)
		{
			throw new ArgumentOutOfRangeException(
				nameof(label.FontSize),
				$"Invalid font size for UI label '{label.Name}'.");
		}

		Vector4 color = label.TextColor;

		if (!float.IsFinite(color.X) ||
			!float.IsFinite(color.Y) ||
			!float.IsFinite(color.Z) ||
			!float.IsFinite(color.W) ||
			color.W <= 0.0f)
		{
			return;
		}

		UIFontAtlas atlas = GetOrCreateFontAtlas(label);

		color = Vector4.Clamp(
			color,
			Vector4.Zero,
			Vector4.One);

		float startX = label.Rect.X;
		float penX = startX;
		float penY = label.Rect.Y + atlas.AscentPixels;

		DrawBatch batch = BeginBatch(atlas.ResourceSet);

		atlas.TryGetGlyph(
			' ',
			out StbTrueTypeSharp.StbTrueType.stbtt_bakedchar space,
			out _);

		string text = label.Text;

		for (int i = 0; i < text.Length; i++)
		{
			int codepoint = text[i];

			if (codepoint == '\r')
				continue;

			if (codepoint == '\n')
			{
				penX = startX;
				penY += atlas.LineHeightPixels;
				continue;
			}

			if (codepoint == '\t')
			{
				penX += space.xadvance * 4.0f;
				continue;
			}

			if (char.IsHighSurrogate(text[i]) &&
				i + 1 < text.Length &&
				char.IsLowSurrogate(text[i + 1]))
			{
				codepoint = char.ConvertToUtf32(
					text[i],
					text[++i]);
			}

			atlas.TryGetGlyph(
				codepoint,
				out StbTrueTypeSharp.StbTrueType.stbtt_bakedchar glyph,
				out int atlasOffsetX);

			int glyphWidth = glyph.x1 - glyph.x0;
			int glyphHeight = glyph.y1 - glyph.y0;

			if (glyphWidth > 0 && glyphHeight > 0)
			{
				float left = MathF.Floor(
					penX + glyph.xoff + 0.5f);

				float top = MathF.Floor(
					penY + glyph.yoff + 0.5f);

				float right = left + glyphWidth;
				float bottom = top + glyphHeight;

				float u0 =
					(atlasOffsetX + glyph.x0) /
					(float)atlas.AtlasWidth;

				float u1 =
					(atlasOffsetX + glyph.x1) /
					(float)atlas.AtlasWidth;

				float v0 = glyph.y0 / (float)atlas.AtlasHeight;
				float v1 = glyph.y1 / (float)atlas.AtlasHeight;

				AddQuad(
					batch,
					ToScreen(left, top, width, height),
					ToScreen(right, top, width, height),
					ToScreen(right, bottom, width, height),
					ToScreen(left, bottom, width, height),
					new Vector3(u0, v0, 1.0f),
					new Vector3(u1, v0, 1.0f),
					new Vector3(u1, v1, 1.0f),
					new Vector3(u0, v1, 1.0f),
					color);
			}

			penX += glyph.xadvance;
		}
	}

	private UIFontAtlas GetOrCreateFontAtlas(UILabel label)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(label.FontPath);

		string fullPath = Path.GetFullPath(label.FontPath);

		string cacheKey =
			fullPath + "\0" + label.FontSize.ToString(
				System.Globalization.CultureInfo.InvariantCulture);

		if (_fontAtlases.TryGetValue(
			cacheKey,
			out UIFontAtlas? existing))
		{
			return existing;
		}

		UIFontAtlas atlas = UIFontAtlas.Load(
			fullPath,
			label.FontSize,
			_factory,
			_graphicsDevice,
			_textureLayout,
			_sampler);

		_fontAtlases.Add(cacheKey, atlas);

		return atlas;
	}

	private DrawBatch BeginBatch(ResourceSet resources)
	{
		if (_currentBatch is not null &&
			ReferenceEquals(_currentBatch.Resources, resources))
		{
			return _currentBatch;
		}

		DrawBatch batch = new(resources, _vertexCount);

		_batches.Add(batch);
		_currentBatch = batch;

		return batch;
	}

	private void AddQuad(
		DrawBatch batch,
		Vector2 topLeft,
		Vector2 topRight,
		Vector2 bottomRight,
		Vector2 bottomLeft,
		Vector3 uvTopLeft,
		Vector3 uvTopRight,
		Vector3 uvBottomRight,
		Vector3 uvBottomLeft,
		Vector4 color)
	{
		EnsureVertexCapacity(checked(_vertexCount + 6));

		_vertices[_vertexCount++] = new UIVertex(
			topLeft, uvTopLeft, color);

		_vertices[_vertexCount++] = new UIVertex(
			topRight, uvTopRight, color);

		_vertices[_vertexCount++] = new UIVertex(
			bottomRight, uvBottomRight, color);

		_vertices[_vertexCount++] = new UIVertex(
			topLeft, uvTopLeft, color);

		_vertices[_vertexCount++] = new UIVertex(
			bottomRight, uvBottomRight, color);

		_vertices[_vertexCount++] = new UIVertex(
			bottomLeft, uvBottomLeft, color);

		batch.VertexCount += 6;
	}

	private static Vector2 ToScreen(
		float x,
		float y,
		int width,
		int height)
	{
		return new Vector2(
			x * (2.0f / width) - 1.0f,
			1.0f - y * (2.0f / height));
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

		foreach (UIFontAtlas atlas in _fontAtlases.Values)
			atlas.Dispose();

		_fontAtlases.Clear();

		_whiteResourceSet.Dispose();

		_pipeline.Dispose();
		_vertexBuffer.Dispose();

		_whiteTexture.Dispose();
		_sampler.Dispose();
		_textureLayout.Dispose();

		foreach (Shader shader in _shaders)
			shader.Dispose();

		_batches.Clear();
		_vertices = Array.Empty<UIVertex>();
	}
}