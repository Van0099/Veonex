using NeoVeldrid;
using StbTrueTypeSharp;

namespace Veonex.Render;

internal sealed unsafe class UIFontAtlas : IDisposable
{
	private const int AsciiStart = 32;
	private const int AsciiCount = 95;

	private const int LatinStart = 160;
	private const int LatinCount = 96;

	private const int CyrillicStart = 1024;
	private const int CyrillicCount = 256;

	private readonly GlyphPage[] _pages;

	private bool _disposed;

	public Texture Texture { get; }

	public ResourceSet ResourceSet { get; }

	public int PageSize { get; }

	public int AtlasWidth => PageSize * _pages.Length;

	public int AtlasHeight => PageSize;

	public float AscentPixels { get; }

	public float LineHeightPixels { get; }

	private sealed class GlyphPage
	{
		public int FirstCodepoint { get; }

		public StbTrueType.stbtt_bakedchar[] Glyphs { get; }

		public int AtlasOffsetX { get; }

		public GlyphPage(
			int firstCodepoint,
			StbTrueType.stbtt_bakedchar[] glyphs,
			int atlasOffsetX)
		{
			FirstCodepoint = firstCodepoint;
			Glyphs = glyphs;
			AtlasOffsetX = atlasOffsetX;
		}
	}

	private sealed class BakedPage
	{
		public int FirstCodepoint { get; }

		public StbTrueType.stbtt_bakedchar[] Glyphs { get; }

		public byte[] Pixels { get; }

		public BakedPage(
			int firstCodepoint,
			StbTrueType.stbtt_bakedchar[] glyphs,
			byte[] pixels)
		{
			FirstCodepoint = firstCodepoint;
			Glyphs = glyphs;
			Pixels = pixels;
		}
	}

	private readonly record struct BakedFont(
		int PageSize,
		byte[] Pixels,
		GlyphPage[] Pages);

	private UIFontAtlas(
		int fontSize,
		byte[] fontData,
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		ResourceLayout textureLayout,
		Sampler sampler)
	{
		StbTrueType.stbtt_fontinfo? fontInfo =
			StbTrueType.CreateFont(fontData, 0);

		if (fontInfo is null)
		{
			throw new InvalidDataException(
				"The specified TTF file is invalid or unsupported.");
		}

		int ascent = 0;
		int descent = 0;
		int lineGap = 0;
		float scale;

		using (fontInfo)
		{
			StbTrueType.stbtt_GetFontVMetrics(
				fontInfo,
				&ascent,
				&descent,
				&lineGap);

			scale = StbTrueType.stbtt_ScaleForPixelHeight(
				fontInfo,
				fontSize);
		}

		AscentPixels = ascent * scale;

		LineHeightPixels = MathF.Max(
			1.0f,
			(ascent - descent + lineGap) * scale);

		BakedFont bakedFont = BakeFont(fontData, fontSize);

		PageSize = bakedFont.PageSize;
		_pages = bakedFont.Pages;

		Texture texture = factory.CreateTexture(
			TextureDescription.Texture2D(
				(uint)(PageSize * _pages.Length),
				(uint)PageSize,
				1,
				1,
				PixelFormat.R8_UNorm,
				TextureUsage.Sampled));

		ResourceSet? resourceSet = null;

		try
		{
			graphicsDevice.UpdateTexture(
				texture,
				bakedFont.Pixels,
				0,
				0,
				0,
				(uint)(PageSize * _pages.Length),
				(uint)PageSize,
				1,
				0,
				0);

			resourceSet = factory.CreateResourceSet(
				new ResourceSetDescription(
					textureLayout,
					texture,
					sampler));

			Texture = texture;
			ResourceSet = resourceSet;
		}
		catch
		{
			resourceSet?.Dispose();
			texture.Dispose();
			throw;
		}
	}

	public static UIFontAtlas Load(
		string path,
		int fontSize,
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		ResourceLayout textureLayout,
		Sampler sampler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (fontSize is < 8 or > 128)
		{
			throw new ArgumentOutOfRangeException(
				nameof(fontSize),
				"Font size must be between 8 and 128 pixels.");
		}

		if (!string.Equals(
			Path.GetExtension(path),
			".ttf",
			StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				"Veonex.UI currently supports TTF font files only.");
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException(
				"The UI font file could not be found.",
				path);
		}

		byte[] fontData = File.ReadAllBytes(path);

		return new UIFontAtlas(
			fontSize,
			fontData,
			factory,
			graphicsDevice,
			textureLayout,
			sampler);
	}

	public bool TryGetGlyph(
		int codepoint,
		out StbTrueType.stbtt_bakedchar glyph,
		out int atlasOffsetX)
	{
		int pageIndex;

		if (codepoint >= AsciiStart &&
			codepoint < AsciiStart + AsciiCount)
		{
			pageIndex = 0;
		}
		else if (codepoint >= LatinStart &&
				 codepoint < LatinStart + LatinCount)
		{
			pageIndex = 1;
		}
		else if (codepoint >= CyrillicStart &&
				 codepoint < CyrillicStart + CyrillicCount)
		{
			pageIndex = 2;
		}
		else
		{
			codepoint = '?';
			pageIndex = 0;
		}

		GlyphPage page = _pages[pageIndex];

		glyph = page.Glyphs[
			codepoint - page.FirstCodepoint];

		atlasOffsetX = page.AtlasOffsetX;

		return true;
	}

	private static BakedFont BakeFont(
		byte[] fontData,
		int fontSize)
	{
		(int First, int Count)[] ranges =
		[
			(AsciiStart, AsciiCount),
			(LatinStart, LatinCount),
			(CyrillicStart, CyrillicCount)
		];

		foreach (int pageSize in new[] { 512, 1024, 2048 })
		{
			List<BakedPage> bakedPages = [];
			bool success = true;

			foreach ((int first, int count) in ranges)
			{
				byte[] pixels = new byte[
					checked(pageSize * pageSize)];

				StbTrueType.stbtt_bakedchar[] glyphs =
					new StbTrueType.stbtt_bakedchar[count];

				int result = BakePage(
					fontData,
					fontSize,
					pixels,
					pageSize,
					first,
					count,
					glyphs);

				if (result <= 0)
				{
					success = false;
					break;
				}

				bakedPages.Add(
					new BakedPage(first, glyphs, pixels));
			}

			if (!success)
				continue;

			int atlasWidth = pageSize * bakedPages.Count;

			byte[] atlasPixels = new byte[
				checked(atlasWidth * pageSize)];

			GlyphPage[] pages =
				new GlyphPage[bakedPages.Count];

			for (int pageIndex = 0;
				 pageIndex < bakedPages.Count;
				 pageIndex++)
			{
				BakedPage page = bakedPages[pageIndex];

				for (int y = 0; y < pageSize; y++)
				{
					Buffer.BlockCopy(
						page.Pixels,
						y * pageSize,
						atlasPixels,
						y * atlasWidth + pageIndex * pageSize,
						pageSize);
				}

				pages[pageIndex] = new GlyphPage(
					page.FirstCodepoint,
					page.Glyphs,
					pageIndex * pageSize);
			}

			return new BakedFont(
				pageSize,
				atlasPixels,
				pages);
		}

		throw new InvalidOperationException(
			$"Could not fit the font atlas for size {fontSize}.");
	}

	private static int BakePage(
		byte[] fontData,
		float fontSize,
		byte[] pixels,
		int pageSize,
		int firstCodepoint,
		int characterCount,
		StbTrueType.stbtt_bakedchar[] glyphs)
	{
		fixed (byte* fontPointer = fontData)
		fixed (byte* pixelsPointer = pixels)
		fixed (StbTrueType.stbtt_bakedchar* glyphPointer = glyphs)
		{
			return StbTrueType.stbtt_BakeFontBitmap(
				fontPointer,
				0,
				fontSize,
				pixelsPointer,
				pageSize,
				pageSize,
				firstCodepoint,
				characterCount,
				glyphPointer);
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		ResourceSet.Dispose();
		Texture.Dispose();
	}
}