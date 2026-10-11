using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// Finds where text is in an image, using the PP-OCR text detection model (DBNet).
/// </summary>
/// <remarks>
/// Only detection runs: the card needs to know where lettering is, not what it says.
/// Pre- and post-processing follow RapidOCR's defaults for this model (short side scaled to
/// 736, sides rounded to multiples of 32, BGR scaled to [-1, 1]; probability threshold 0.3,
/// 2x2 dilation, box score 0.5, unclip ratio 1.6).
/// The plugin ships ONNX Runtime's native library for Linux x64 and ARM64 only; elsewhere the
/// detector reports itself unavailable and cards are built without the text filter.
/// </remarks>
public sealed class TextDetector : IDisposable
{
    private const int LimitSideLength = 736;
    private const float BinaryThreshold = 0.3f;
    private const float BoxThreshold = 0.5f;
    private const float UnclipRatio = 1.6f;
    private const int MinSize = 3;
    private const int MaxCandidates = 1000;

    private static readonly object ResolverLock = new();
    private static bool _resolverSet;

    private readonly InferenceSession? _session;
    private readonly string _inputName = string.Empty;
    private readonly object _runLock = new();

    private TextDetector(InferenceSession? session, string? unavailableReason)
    {
        _session = session;
        UnavailableReason = unavailableReason;
        if (session is not null)
        {
            _inputName = session.InputMetadata.Keys.First();
        }
    }

    /// <summary>Gets a value indicating whether text detection can run here.</summary>
    public bool IsAvailable => _session is not null;

    /// <summary>Gets why detection is unavailable, when it is.</summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Loads the detection model. Never throws: on failure the detector is unavailable.
    /// </summary>
    /// <param name="logger">Logger for the outcome.</param>
    /// <returns>The detector.</returns>
    public static TextDetector Create(ILogger logger)
    {
        try
        {
            RegisterNativeLibrary();
            using var model = typeof(TextDetector).Assembly.GetManifestResourceStream("text-detection.onnx")
                ?? throw new InvalidOperationException("The text detection model is missing from the plugin.");
            using var buffer = new MemoryStream();
            model.CopyTo(buffer);
            using var options = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2) };
            var session = new InferenceSession(buffer.ToArray(), options);
            logger.LogInformation("Title Cards: text detection ready ({Os} {Arch})", RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
            return new TextDetector(session, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or OnnxRuntimeException or InvalidOperationException or BadImageFormatException or EntryPointNotFoundException)
        {
            var reason = $"text detection is not available on {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture} ({ex.GetType().Name}: {ex.Message})";
            logger.LogWarning("Title Cards: {Reason}; cards will be built without keeping text out from behind the title", reason);
            return new TextDetector(null, reason);
        }
    }

    /// <summary>
    /// Detects text in an image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>Text boxes in the image's coordinates; empty when detection is unavailable.</returns>
    public IReadOnlyList<Box> Detect(SKBitmap image)
    {
        if (_session is null)
        {
            return [];
        }

        var (resizedWidth, resizedHeight) = ResizedSize(image.Width, image.Height);
        var input = ToTensor(image, resizedWidth, resizedHeight);
        float[] probabilities;
        lock (_runLock)
        {
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
            probabilities = results.First().AsEnumerable<float>().ToArray();
        }

        return FindBoxes(probabilities, resizedWidth, resizedHeight, (float)image.Width / resizedWidth, (float)image.Height / resizedHeight);
    }

    /// <inheritdoc />
    public void Dispose() => _session?.Dispose();

    /// <summary>
    /// The model input size: short side scaled up to 736 when smaller, both sides rounded to 32.
    /// </summary>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <returns>The resized width and height.</returns>
    internal static (int Width, int Height) ResizedSize(int width, int height)
    {
        var ratio = Math.Min(width, height) < LimitSideLength ? (float)LimitSideLength / Math.Min(width, height) : 1f;
        var resizedWidth = (int)Math.Round((int)(width * ratio) / 32.0, MidpointRounding.ToEven) * 32;
        var resizedHeight = (int)Math.Round((int)(height * ratio) / 32.0, MidpointRounding.ToEven) * 32;
        return (Math.Max(32, resizedWidth), Math.Max(32, resizedHeight));
    }

    /// <summary>
    /// Turns the model's text-probability map into boxes (DB post-processing).
    /// </summary>
    /// <param name="probabilities">Row-major probability map.</param>
    /// <param name="width">Map width.</param>
    /// <param name="height">Map height.</param>
    /// <param name="scaleX">Map-to-image horizontal scale.</param>
    /// <param name="scaleY">Map-to-image vertical scale.</param>
    /// <returns>Text boxes in image coordinates.</returns>
    internal static IReadOnlyList<Box> FindBoxes(float[] probabilities, int width, int height, float scaleX, float scaleY)
    {
        // Binarise, then dilate with a 2x2 kernel.
        var binary = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                binary[index] = probabilities[index] > BinaryThreshold
                    || (x > 0 && probabilities[index - 1] > BinaryThreshold)
                    || (y > 0 && probabilities[index - width] > BinaryThreshold)
                    || (x > 0 && y > 0 && probabilities[index - width - 1] > BinaryThreshold);
            }
        }

        var boxes = new List<Box>();
        var visited = new bool[width * height];
        var stack = new Stack<int>();
        for (var start = 0; start < binary.Length && boxes.Count < MaxCandidates; start++)
        {
            if (!binary[start] || visited[start])
            {
                continue;
            }

            // One 8-connected region of text pixels.
            int left = width, top = height, right = -1, bottom = -1, count = 0;
            double sum = 0;
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                int x = index % width, y = index / width;
                sum += probabilities[index];
                count++;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var next = (ny * width) + nx;
                        if (binary[next] && !visited[next])
                        {
                            visited[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            float boxWidth = right - left + 1, boxHeight = bottom - top + 1;
            if (Math.Min(boxWidth, boxHeight) < MinSize)
            {
                continue;
            }

            // Region score: mean probability over the region's own pixels. RapidOCR averages over a
            // tight rotated box; an upright box would dilute slanted lettering with background.
            if (sum / count < BoxThreshold)
            {
                continue;
            }

            // Unclip: grow by area * ratio / perimeter, as DBNet shrank the text regions when training.
            var distance = boxWidth * boxHeight * UnclipRatio / (2 * (boxWidth + boxHeight));
            var grown = new Box(left, top, right + 1, bottom + 1).Inflate(distance);
            if (Math.Min(grown.Width, grown.Height) < MinSize + 2)
            {
                continue;
            }

            boxes.Add(new Box(
                Math.Clamp(grown.Left * scaleX, 0, width * scaleX),
                Math.Clamp(grown.Top * scaleY, 0, height * scaleY),
                Math.Clamp(grown.Right * scaleX, 0, width * scaleX),
                Math.Clamp(grown.Bottom * scaleY, 0, height * scaleY)));
        }

        return boxes;
    }

    private static DenseTensor<float> ToTensor(SKBitmap image, int width, int height)
    {
        using var resized = image.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidOperationException("Could not resize the image for text detection.");
        var tensor = new DenseTensor<float>([1, 3, height, width]);
        var pixels = resized.Pixels;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                // BGR channel order, scaled from [0, 255] to [-1, 1].
                tensor[0, 0, y, x] = (pixel.Blue / 127.5f) - 1f;
                tensor[0, 1, y, x] = (pixel.Green / 127.5f) - 1f;
                tensor[0, 2, y, x] = (pixel.Red / 127.5f) - 1f;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Points ONNX Runtime at the native library shipped in the plugin folder.
    /// </summary>
    /// <remarks>
    /// Plugins are not the host application, so .NET does not probe their runtimes folders for
    /// native libraries. Only Linux x64 and ARM64 libraries are packaged.
    /// </remarks>
    private static void RegisterNativeLibrary()
    {
        lock (ResolverLock)
        {
            if (_resolverSet)
            {
                return;
            }

            _resolverSet = true;
            var pluginFolder = Path.GetDirectoryName(typeof(TextDetector).Assembly.Location) ?? AppContext.BaseDirectory;
            var rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 when OperatingSystem.IsLinux() => "linux-x64",
                Architecture.Arm64 when OperatingSystem.IsLinux() => "linux-arm64",
                _ => null,
            };
            if (rid is null)
            {
                // Not a packaged platform: leave .NET's default probing (works in tests and dev builds).
                return;
            }

            var library = Path.Combine(pluginFolder, "runtimes", rid, "native", "libonnxruntime.so");
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, (name, _, _) =>
                    name == "onnxruntime" && File.Exists(library) ? NativeLibrary.Load(library) : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // A resolver is already registered for ONNX Runtime (e.g. by another plugin).
            }
        }
    }
}
