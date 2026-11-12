using System.Diagnostics;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Veonex.Assets;
using Veonex.Core.Logging;

namespace Veonex.Render;

public static unsafe class Ktx2
{
	private const uint VulkanFormatR8G8B8A8Unorm = 37;
	private const uint TextureCreateAllocateStorage = 1;

	private const uint UastcLevelDefault = 2;
	private const uint UastcFavorBc7Error = 16;

	private const int TranscodeBc7Rgba = 6;
	private const int TranscodeRgba32 = 13;

	private static readonly Lazy<NativeApi> Native = new(
		CreateNativeApi,
		LazyThreadSafetyMode.ExecutionAndPublication);

	private readonly record struct MipLevel(
		int Width,
		int Height,
		byte[] Pixels);

	[StructLayout(LayoutKind.Sequential)]
	private struct TextureCreateInfo
	{
		public uint GlInternalFormat;
		public uint VkFormat;
		public IntPtr Dfd;
		public uint BaseWidth;
		public uint BaseHeight;
		public uint BaseDepth;
		public uint NumDimensions;
		public uint NumLevels;
		public uint NumLayers;
		public uint NumFaces;
		public byte IsArray;
		public byte GenerateMipmaps;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BasisParams
	{
		public uint StructSize;
		public byte Uastc;
		public byte Verbose;
		public byte NoSse;
		public uint ThreadCount;
		public uint CompressionLevel;
		public uint QualityLevel;
		public uint MaxEndpoints;
		public float EndpointRdoThreshold;
		public uint MaxSelectors;
		public float SelectorRdoThreshold;
		public fixed byte InputSwizzle[4];
		public byte NormalMap;
		public byte SeparateRgToRgbA;
		public byte PreSwizzle;
		public byte NoEndpointRdo;
		public byte NoSelectorRdo;
		public uint UastcFlags;
		public byte UastcRdo;
		public float UastcRdoQualityScalar;
		public uint UastcRdoDictSize;
		public float UastcRdoMaxSmoothBlockErrorScale;
		public float UastcRdoMaxSmoothBlockStdDev;
		public byte UastcRdoDontFavorSimplerModes;
		public byte UastcRdoNoMultithreading;
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Texture2CreateDelegate(
		in TextureCreateInfo createInfo,
		uint storageAllocation,
		out IntPtr texture);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Texture2CompressBasisDelegate(
		IntPtr texture,
		ref BasisParams parameters);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Texture2TranscodeBasisDelegate(
		IntPtr texture,
		int targetFormat,
		uint flags);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Texture2GetImageOffsetDelegate(
		IntPtr texture,
		uint level,
		uint layer,
		uint faceSlice,
		out nuint offset);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate void Texture2DestroyDelegate(IntPtr texture);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate IntPtr TextureGetDataDelegate(IntPtr texture);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate nuint TextureGetDataSizeDelegate(IntPtr texture);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate IntPtr ErrorStringDelegate(int errorCode);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int SetImageFromMemoryDelegate(
		IntPtr texture,
		uint level,
		uint layer,
		uint faceSlice,
		IntPtr source,
		nuint sourceSize);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate nuint GetImageSizeDelegate(
		IntPtr texture,
		uint level);

	private sealed class NativeApi
	{
		public IntPtr Library { get; }
		public Texture2CreateDelegate CreateTexture2 { get; }
		public Texture2CompressBasisDelegate CompressBasis { get; }
		public Texture2TranscodeBasisDelegate TranscodeBasis { get; }
		public Texture2GetImageOffsetDelegate GetImageOffset { get; }
		public Texture2DestroyDelegate DestroyTexture2 { get; }
		public TextureGetDataDelegate GetTextureData { get; }
		public TextureGetDataSizeDelegate GetTextureDataSize { get; }
		public ErrorStringDelegate GetErrorString { get; }

		public NativeApi(IntPtr library)
		{
			Library = library;
			CreateTexture2 = LoadExport<Texture2CreateDelegate>("ktxTexture2_Create");
			CompressBasis = LoadExport<Texture2CompressBasisDelegate>("ktxTexture2_CompressBasisEx");
			TranscodeBasis = LoadExport<Texture2TranscodeBasisDelegate>("ktxTexture2_TranscodeBasis");
			GetImageOffset = LoadExport<Texture2GetImageOffsetDelegate>("ktxTexture2_GetImageOffset");
			DestroyTexture2 = LoadExport<Texture2DestroyDelegate>("ktxTexture2_Destroy");
			GetTextureData = LoadExport<TextureGetDataDelegate>("ktxTexture_GetData");
			GetTextureDataSize = LoadExport<TextureGetDataSizeDelegate>("ktxTexture_GetDataSize");
			GetErrorString = LoadExport<ErrorStringDelegate>("ktxErrorString");
		}

		private T LoadExport<T>(string name) where T : Delegate
		{
			IntPtr address = NativeLibrary.GetExport(Library, name);
			return Marshal.GetDelegateForFunctionPointer<T>(address);
		}
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		DecodedImage image,
		string sourceName,
		out int mipLevels)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(graphicsDevice);
		ArgumentNullException.ThrowIfNull(image);

		Stopwatch stopwatch = Stopwatch.StartNew();
		NativeApi api = Native.Value;
		List<MipLevel> levels = BuildMipChain(image);
		mipLevels = levels.Count;

		bool supportsBc7 = graphicsDevice.GetPixelFormatSupport(
			PixelFormat.BC7_UNorm,
			TextureType.Texture2D,
			TextureUsage.Sampled);

		PixelFormat pixelFormat;
		int transcodeFormat;

		if (supportsBc7)
		{
			pixelFormat = PixelFormat.BC7_UNorm;
			transcodeFormat = TranscodeBc7Rgba;
		}
		else
		{
			pixelFormat = PixelFormat.R8_G8_B8_A8_UNorm;
			transcodeFormat = TranscodeRgba32;

			if (!graphicsDevice.GetPixelFormatSupport(
				pixelFormat,
				TextureType.Texture2D,
				TextureUsage.Sampled))
			{
				throw new NotSupportedException(
					"The graphics device does not support either BC7 UNorm or RGBA8 UNorm sampled textures.");
			}

			Logger.Warning(
				"Textures",
				$"BC7 is not supported for '{Path.GetFileName(sourceName)}'. Using RGBA8 UNorm.");
		}

		TextureCreateInfo createInfo = new()
		{
			VkFormat = VulkanFormatR8G8B8A8Unorm,
			BaseWidth = (uint)image.Width,
			BaseHeight = (uint)image.Height,
			BaseDepth = 1,
			NumDimensions = 2,
			NumLevels = (uint)levels.Count,
			NumLayers = 1,
			NumFaces = 1,
			IsArray = 0,
			GenerateMipmaps = 0
		};

		int createResult = api.CreateTexture2(
			in createInfo,
			TextureCreateAllocateStorage,
			out IntPtr ktxTexture);

		if (createResult != 0)
		{
			if (ktxTexture != IntPtr.Zero)
				api.DestroyTexture2(ktxTexture);

			ThrowIfError(api, createResult, "ktxTexture2_Create");
		}

		if (ktxTexture == IntPtr.Zero)
			throw new InvalidOperationException("ktxTexture2_Create succeeded but returned a null texture.");

		Texture? texture = null;

		try
		{
			SetMipImages(api, ktxTexture, levels);

			BasisParams parameters = new()
			{
				StructSize = (uint)sizeof(BasisParams),
				Uastc = 1,
				ThreadCount = 1,
				CompressionLevel = 2,
				UastcFlags = UastcLevelDefault | UastcFavorBc7Error
			};

			if (Marshal.SizeOf<BasisParams>() != 76)
			{
				throw new PlatformNotSupportedException(
					$"Unexpected KTX Basis parameter layout: {Marshal.SizeOf<BasisParams>()} bytes. Expected 76 bytes.");
			}

			ThrowIfError(
				api,
				api.CompressBasis(ktxTexture, ref parameters),
				"ktxTexture2_CompressBasisEx");

			ThrowIfError(
				api,
				api.TranscodeBasis(ktxTexture, transcodeFormat, 0),
				"ktxTexture2_TranscodeBasis");

			texture = factory.CreateTexture(
				TextureDescription.Texture2D(
					(uint)image.Width,
					(uint)image.Height,
					(uint)levels.Count,
					1,
					pixelFormat,
					TextureUsage.Sampled));

			UploadMipImages(
				api,
				graphicsDevice,
				ktxTexture,
				texture,
				levels,
				pixelFormat == PixelFormat.BC7_UNorm);

			stopwatch.Stop();
			Logger.Info(
				"Textures",
				$"'{Path.GetFileName(sourceName)}': UASTC → {pixelFormat}, " +
				$"{levels.Count} mip level(s), {stopwatch.ElapsedMilliseconds} ms.");

			return texture;
		}
		catch
		{
			texture?.Dispose();
			throw;
		}
		finally
		{
			api.DestroyTexture2(ktxTexture);
		}
	}

	private static void SetMipImages(
		NativeApi api,
		IntPtr texture,
		IReadOnlyList<MipLevel> levels)
	{
		IntPtr vtable = Marshal.ReadIntPtr(texture, IntPtr.Size);
		IntPtr function = Marshal.ReadIntPtr(vtable, 9 * IntPtr.Size);

		if (function == IntPtr.Zero)
			throw new InvalidOperationException("KTX SetImageFromMemory entry point is null.");

		SetImageFromMemoryDelegate setImage =
			Marshal.GetDelegateForFunctionPointer<SetImageFromMemoryDelegate>(function);

		for (int level = 0; level < levels.Count; level++)
		{
			byte[] pixels = levels[level].Pixels;

			fixed (byte* source = pixels)
			{
				int result = setImage(
					texture,
					(uint)level,
					0,
					0,
					(IntPtr)source,
					(nuint)pixels.Length);

				ThrowIfError(api, result, $"ktxTexture_SetImageFromMemory (mip {level})");
			}
		}
	}

	private static void UploadMipImages(
		NativeApi api,
		GraphicsDevice graphicsDevice,
		IntPtr ktxTexture,
		Texture texture,
		IReadOnlyList<MipLevel> levels,
		bool blockCompressed)
	{
		IntPtr vtable = Marshal.ReadIntPtr(ktxTexture, IntPtr.Size);
		IntPtr function = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);

		if (function == IntPtr.Zero)
			throw new InvalidOperationException("KTX GetImageSize entry point is null.");

		GetImageSizeDelegate getImageSize =
			Marshal.GetDelegateForFunctionPointer<GetImageSizeDelegate>(function);

		IntPtr data = api.GetTextureData(ktxTexture);
		nuint dataSize = api.GetTextureDataSize(ktxTexture);
		if (data == IntPtr.Zero || dataSize == 0)
			throw new InvalidOperationException("KTX returned an empty texture data buffer after transcoding.");

		for (int level = 0; level < levels.Count; level++)
		{
			MipLevel mip = levels[level];

			ThrowIfError(
				api,
				api.GetImageOffset(ktxTexture, (uint)level, 0, 0, out nuint offset),
				$"ktxTexture2_GetImageOffset (mip {level})");

			nuint nativeSize = getImageSize(ktxTexture, (uint)level);
			if (nativeSize == 0 || nativeSize > int.MaxValue || offset > int.MaxValue ||
				offset > dataSize || nativeSize > dataSize - offset)
			{
				throw new InvalidOperationException(
					$"KTX returned an invalid data range for mip {level}.");
			}

			int expectedSize = blockCompressed
				? checked(((mip.Width + 3) / 4) * ((mip.Height + 3) / 4) * 16)
				: checked(mip.Width * mip.Height * 4);

			if ((int)nativeSize != expectedSize)
			{
				throw new InvalidOperationException(
					$"Unexpected KTX mip size at level {level}: {nativeSize} bytes, expected {expectedSize}.");
			}

			byte[] pixels = new byte[expectedSize];
			Marshal.Copy(IntPtr.Add(data, checked((int)offset)), pixels, 0, pixels.Length);

			graphicsDevice.UpdateTexture(
				texture,
				pixels,
				0, 0, 0,
				(uint)mip.Width,
				(uint)mip.Height,
				1,
				(uint)level,
				0);
		}
	}

	private static List<MipLevel> BuildMipChain(DecodedImage image)
	{
		List<MipLevel> levels = new();
		levels.Add(new MipLevel(image.Width, image.Height, image.Pixels));

		int width = image.Width;
		int height = image.Height;
		byte[] pixels = image.Pixels;

		while (width > 1 || height > 1)
		{
			int nextWidth = Math.Max(1, width / 2);
			int nextHeight = Math.Max(1, height / 2);
			byte[] nextPixels = DownsampleUnorm(pixels, width, height, nextWidth, nextHeight);

			levels.Add(new MipLevel(nextWidth, nextHeight, nextPixels));
			pixels = nextPixels;
			width = nextWidth;
			height = nextHeight;
		}

		return levels;
	}

	private static byte[] DownsampleUnorm(
		byte[] source,
		int sourceWidth,
		int sourceHeight,
		int destinationWidth,
		int destinationHeight)
	{
		byte[] destination = new byte[checked(destinationWidth * destinationHeight * 4)];

		for (int destinationY = 0; destinationY < destinationHeight; destinationY++)
		{
			double sourceY0 = (double)destinationY * sourceHeight / destinationHeight;
			double sourceY1 = (double)(destinationY + 1) * sourceHeight / destinationHeight;
			int startY = (int)Math.Floor(sourceY0);
			int endY = Math.Min(sourceHeight, (int)Math.Ceiling(sourceY1));

			for (int destinationX = 0; destinationX < destinationWidth; destinationX++)
			{
				double sourceX0 = (double)destinationX * sourceWidth / destinationWidth;
				double sourceX1 = (double)(destinationX + 1) * sourceWidth / destinationWidth;
				int startX = (int)Math.Floor(sourceX0);
				int endX = Math.Min(sourceWidth, (int)Math.Ceiling(sourceX1));

				double totalWeight = 0.0;
				double alphaWeight = 0.0;
				double red = 0.0;
				double green = 0.0;
				double blue = 0.0;

				for (int sourceY = startY; sourceY < endY; sourceY++)
				{
					double weightY = Math.Min(sourceY1, sourceY + 1.0) -
						Math.Max(sourceY0, sourceY);

					for (int sourceX = startX; sourceX < endX; sourceX++)
					{
						double weightX = Math.Min(sourceX1, sourceX + 1.0) -
							Math.Max(sourceX0, sourceX);
						double weight = weightX * weightY;
						int sourceIndex = (sourceY * sourceWidth + sourceX) * 4;

						double alpha = source[sourceIndex + 3] / 255.0;
						totalWeight += weight;
						alphaWeight += alpha * weight;
						red += source[sourceIndex] * alpha * weight;
						green += source[sourceIndex + 1] * alpha * weight;
						blue += source[sourceIndex + 2] * alpha * weight;
					}
				}

				int destinationIndex = (destinationY * destinationWidth + destinationX) * 4;

				if (alphaWeight > 1e-12)
				{
					destination[destinationIndex] = ToByte(red / alphaWeight);
					destination[destinationIndex + 1] = ToByte(green / alphaWeight);
					destination[destinationIndex + 2] = ToByte(blue / alphaWeight);
				}
				else
				{
					destination[destinationIndex] = 0;
					destination[destinationIndex + 1] = 0;
					destination[destinationIndex + 2] = 0;
				}

				destination[destinationIndex + 3] = (byte)Math.Clamp(
					(int)Math.Round(alphaWeight / totalWeight * 255.0),
					0,
					255);
			}
		}

		return destination;
	}

	private static byte ToByte(double value) =>
		(byte)Math.Clamp((int)Math.Round(value), 0, 255);

	private static NativeApi CreateNativeApi()
	{
		List<string> candidates = [];
		string? configuredLibrary = Environment.GetEnvironmentVariable("VEONEX_KTX_LIBRARY");

		if (!string.IsNullOrWhiteSpace(configuredLibrary))
		{
			string configuredPath = Path.GetFullPath(configuredLibrary);
			if (Directory.Exists(configuredPath))
				AddLibraryNames(candidates, configuredPath);
			else
				candidates.Add(configuredPath);
		}

		string baseDirectory = AppContext.BaseDirectory;
		AddLibraryNames(candidates, baseDirectory);

		string runtimeDirectory = OperatingSystem.IsWindows()
			? "win-x64"
			: OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";

		AddLibraryNames(candidates, Path.Combine(baseDirectory, "Native", runtimeDirectory));
		AddLibraryNames(candidates, Path.Combine(baseDirectory, "runtimes", runtimeDirectory, "native"));
		AddLibrarySearchNames(candidates);

		List<string> errors = [];

		foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (!TryLoadLibrary(candidate, out IntPtr library))
			{
				errors.Add(candidate);
				continue;
			}

			try
			{
				return new NativeApi(library);
			}
			catch
			{
				NativeLibrary.Free(library);
				throw;
			}
		}

		throw new DllNotFoundException(
			"KTX-Software 4.4.2 native library was not found. " +
			"Place ktx.dll and its runtime dependencies in Veonex.Render/Native/win-x64 " +
			"(or the matching runtime folder), or set VEONEX_KTX_LIBRARY to the library path. " +
			$"Searched: {string.Join("; ", errors)}");
	}

	private static bool TryLoadLibrary(string candidate, out IntPtr library)
	{
		if (OperatingSystem.IsWindows() && Path.IsPathFullyQualified(candidate))
		{
			const uint LoadLibrarySearchDllLoadDir = 0x00000100;
			const uint LoadLibrarySearchDefaultDirs = 0x00001000;
			library = LoadLibraryEx(
				candidate,
				IntPtr.Zero,
				LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
			return library != IntPtr.Zero;
		}

		return NativeLibrary.TryLoad(candidate, out library);
	}

	[DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryEx(
		string fileName,
		IntPtr file,
		uint flags);

	private static void AddLibraryNames(List<string> candidates, string directory)
	{
		foreach (string name in GetLibraryNames())
			candidates.Add(Path.Combine(directory, name));
	}

	private static void AddLibrarySearchNames(List<string> candidates)
	{
		foreach (string name in GetLibraryNames())
			candidates.Add(name);
	}

	private static string[] GetLibraryNames() => OperatingSystem.IsWindows()
		? ["ktx.dll", "libktx.dll"]
		: OperatingSystem.IsMacOS()
			? ["libktx.dylib"]
			: ["libktx.so", "libktx.so.4"];

	private static void ThrowIfError(NativeApi api, int errorCode, string operation)
	{
		if (errorCode == 0)
			return;

		IntPtr errorPointer = api.GetErrorString(errorCode);
		string message = Marshal.PtrToStringUTF8(errorPointer) ?? $"KTX error {errorCode}";
		throw new InvalidOperationException($"{operation} failed: {message} ({errorCode}).");
	}
}
