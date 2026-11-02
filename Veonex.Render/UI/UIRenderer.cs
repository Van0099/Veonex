using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Veonex.Assets;
using Veonex.Mathematics;
using Veonex.UI;
using UITransform = Veonex.UI.RectTransform;

namespace Veonex.Render;

public sealed class UIRenderer : IDisposable
{
	private const int InitialVertexCapacity = 256;

	private readonly ResourceFactory _factory;
	private readonly GraphicsDevice _graphicsDevice;

	private readonly ResourceLayout _textureLayout;
	private readonly Sampler _sampler;

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

		document.ResolveLayout(width, height);

		CollectElement(
			document.Root,
			width,
			height);

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
		Element element,
		int width,
		int height)
	{
		if (element.TryGet<UITransform>(
				out UITransform? transform) &&
			element.TryGet<Text>(
				out Text? text) &&
			transform is not null &&
			text is not null)
		{
			AddText(
				text,
				transform.ResolvedRect,
				width,
				height);
		}

		foreach (Element child in element.Children)
		{
			CollectElement(child, width, height);
		}
	}

	private void AddText(
		Text text,
		RectD rect,
		int width,
		int height)
	{
		if (string.IsNullOrEmpty(text.Content))
			return;

		if (!double.IsFinite(rect.X) ||
			!double.IsFinite(rect.Y) ||
			!double.IsFinite(rect.Width) ||
			!double.IsFinite(rect.Height) ||
			rect.Width <= 0.0 ||
			rect.Height <= 0.0)
		{
			return;
		}

		DVector4 sourceColor = text.Color;

		if (!double.IsFinite(sourceColor.X) ||
			!double.IsFinite(sourceColor.Y) ||
			!double.IsFinite(sourceColor.Z) ||
			!double.IsFinite(sourceColor.W))
		{
			return;
		}

		Vector4 color = new(
			(float)sourceColor.X,
			(float)sourceColor.Y,
			(float)sourceColor.Z,
			(float)sourceColor.W);

		if (!float.IsFinite(color.X) ||
			!float.IsFinite(color.Y) ||
			!float.IsFinite(color.Z) ||
			!float.IsFinite(color.W))
		{
			return;
		}

		color = Vector4.Clamp(
			color,
			Vector4.Zero,
			Vector4.One);

		if (color.W <= 0.0f)
			return;

		UIFontAtlas atlas = GetOrCreateFontAtlas(text);

		atlas.TryGetGlyph(
			' ',
			out StbTrueTypeSharp.StbTrueType.stbtt_bakedchar space,
			out _);

		float tabAdvance = space.xadvance * 4.0f;

		DrawBatch batch = BeginBatch(atlas.ResourceSet);

		string content = text.Content;

		double penY = rect.Y + atlas.AscentPixels;
		int lineStart = 0;

		while (lineStart <= content.Length)
		{
			int lineEnd = content.IndexOf('\n', lineStart);

			if (lineEnd < 0)
				lineEnd = content.Length;

			float lineWidth = MeasureLine(
				content,
				lineStart,
				lineEnd,
				atlas,
				tabAdvance);

			double lineX = text.Alignment switch
			{
				TextAlignment.Left =>
					rect.X,

				TextAlignment.Center =>
					rect.X + (rect.Width - lineWidth) * 0.5,

				TextAlignment.Right =>
					rect.Right - lineWidth,

				_ => throw new ArgumentOutOfRangeException(
					nameof(text.Alignment),
					text.Alignment,
					"Unsupported text alignment.")
			};

			double penX = lineX;

			for (int i = lineStart; i < lineEnd; i++)
			{
				int codepoint = content[i];

				if (codepoint == '\r')
					continue;

				if (codepoint == '\t')
				{
					penX += tabAdvance;
					continue;
				}

				if (char.IsHighSurrogate(content[i]) &&
					i + 1 < lineEnd &&
					char.IsLowSurrogate(content[i + 1]))
				{
					codepoint = char.ConvertToUtf32(
						content[i],
						content[++i]);
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
						(float)(penX + glyph.xoff) + 0.5f);

					float top = MathF.Floor(
						(float)(penY + glyph.yoff) + 0.5f);

					float right = left + glyphWidth;
					float bottom = top + glyphHeight;

					float u0 =
						(atlasOffsetX + glyph.x0) /
						(float)atlas.AtlasWidth;

					float u1 =
						(atlasOffsetX + glyph.x1) /
						(float)atlas.AtlasWidth;

					float v0 =
						glyph.y0 / (float)atlas.AtlasHeight;

					float v1 =
						glyph.y1 / (float)atlas.AtlasHeight;

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

			if (lineEnd == content.Length)
				break;

			lineStart = lineEnd + 1;
			penY += atlas.LineHeightPixels;
		}
	}

	private static float MeasureLine(
		string content,
		int start,
		int end,
		UIFontAtlas atlas,
		float tabAdvance)
	{
		float width = 0.0f;

		for (int i = start; i < end; i++)
		{
			int codepoint = content[i];

			if (codepoint == '\r')
				continue;

			if (codepoint == '\t')
			{
				width += tabAdvance;
				continue;
			}

			if (char.IsHighSurrogate(content[i]) &&
				i + 1 < end &&
				char.IsLowSurrogate(content[i + 1]))
			{
				codepoint = char.ConvertToUtf32(
					content[i],
					content[++i]);
			}

			atlas.TryGetGlyph(
				codepoint,
				out StbTrueTypeSharp.StbTrueType.stbtt_bakedchar glyph,
				out _);

			width += glyph.xadvance;
		}

		return width;
	}

	private UIFontAtlas GetOrCreateFontAtlas(Text text)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text.FontPath);

		float fontSize = (float)text.FontSize;

		if (!float.IsFinite(fontSize) ||
			fontSize is < 8.0f or > 128.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(text.FontSize),
				"Font size must be between 8 and 128 pixels.");
		}

		string fullPath = Path.GetFullPath(text.FontPath);

		string cacheKey =
			fullPath + "\0" +
			fontSize.ToString("R", CultureInfo.InvariantCulture);

		if (_fontAtlases.TryGetValue(
			cacheKey,
			out UIFontAtlas? existing))
		{
			return existing;
		}

		UIFontAtlas atlas = UIFontAtlas.Load(
			fullPath,
			fontSize,
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
		double x,
		double y,
		int width,
		int height)
	{
		return new Vector2(
			(float)(x * (2.0 / width) - 1.0),
			(float)(1.0 - y * (2.0 / height)));
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

		_pipeline.Dispose();
		_vertexBuffer.Dispose();

		_sampler.Dispose();
		_textureLayout.Dispose();

		foreach (Shader shader in _shaders)
			shader.Dispose();

		_batches.Clear();
		_vertices = Array.Empty<UIVertex>();
	}
}