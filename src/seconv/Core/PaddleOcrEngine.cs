using System.Diagnostics;
using System.Text;
using Nikse.SubtitleEdit.Core.Common;
using SkiaSharp;

namespace SeConv.Core;

/// <summary>
/// OCR via the PaddleOCR command-line tool. Prefers the standalone install the GUI downloads
/// (SE data folder, OCR/PaddleOCR3-7, with its bundled models), then a <c>paddleocr</c> from
/// <c>pip install paddleocr</c> on the system PATH.
/// Limited subset compared to SE's UI implementation; assumes a single image per invocation.
/// </summary>
internal sealed class PaddleOcrEngine : IOcrEngine
{
    public string Name => "paddleocr";

    public string ExecutablePath { get; }
    public string Language { get; }

    private readonly string _workDir;

    private PaddleOcrEngine(string executablePath, string language, string workDir)
    {
        ExecutablePath = executablePath;
        Language = language;
        _workDir = workDir;
    }

    /// <summary>
    /// Folder name of the GUI's standalone PaddleOCR install, under the SE "OCR" data folder.
    /// Keep in sync with <c>Se.PaddleOcrFolder</c> in the UI project.
    /// </summary>
    private const string StandaloneFolderName = "PaddleOCR3-7";

    /// <summary>
    /// Locates PaddleOCR. The GUI's standalone install is preferred (portable SE first, then
    /// the installed GUI's data folder), so seconv reuses the engine and models the user
    /// already downloaded; then falls back to a <c>paddleocr</c> on the system PATH.
    /// Returns null if missing.
    /// </summary>
    public static string? Detect()
    {
        return DetectStandalone() ?? DetectOnPath();
    }

    internal static string? DetectStandalone()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "OCR", StandaloneFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Subtitle Edit", "OCR", StandaloneFolderName),
        };

        foreach (var folder in candidates)
        {
            foreach (var name in new[] { "paddleocr.exe", "paddleocr.bin" })
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? DetectOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "paddleocr.exe" : "paddleocr";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// The standalone install ships its models in a "models" folder next to the binary
    /// (cls/det/rec sub-folders). When present, they are passed explicitly like the GUI does.
    /// </summary>
    internal static string? GetModelsFolder(string executablePath)
    {
        var folder = Path.GetDirectoryName(executablePath);
        if (folder == null)
        {
            return null;
        }

        var models = Path.Combine(folder, "models");
        return Directory.Exists(Path.Combine(models, "rec")) ? models : null;
    }

    public static PaddleOcrEngine Create(string language = "en")
    {
        var path = Detect()
            ?? throw new InvalidOperationException(
                "PaddleOCR not found. Download it via Subtitle Edit (OCR > PaddleOCR), or install it " +
                "(e.g. `pip install paddleocr`) and ensure the `paddleocr` binary is on PATH.");

        var workDir = Path.Combine(Path.GetTempPath(), "seconv_paddle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        return new PaddleOcrEngine(path, language, workDir);
    }

    public string Recognize(SKBitmap bitmap)
    {
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return string.Empty;
        }

        var pngPath = Path.Combine(_workDir, "in_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
            using (var fs = File.Create(pngPath))
            {
                data.SaveTo(fs);
            }

            var psi = new ProcessStartInfo(ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                // The tool may write relative to its current directory; make sure that is writable.
                WorkingDirectory = _workDir,
            };
            foreach (var arg in BuildArguments(pngPath))
            {
                psi.ArgumentList.Add(arg);
            }

            // StandardOutputEncoding only fixes the decoding side. paddleocr is a Python CLI, and
            // on Windows Python encodes a *redirected* stdout with the ANSI codepage (until UTF-8
            // becomes the default in Python 3.15, PEP 686) - so the producer side must be forced
            // to UTF-8 too, or non-ASCII text still arrives as mojibake. Same env vars the UI's
            // Paddle engine sets.
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            // Skip PaddleX's online model-source connectivity check - it can hang the run at
            // "Initializing..." when offline, and with explicit local model dirs it is pointless.
            psi.EnvironmentVariables["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start paddleocr process.");
            // Drain stderr concurrently — paddleocr is chatty on stderr, and reading stdout
            // to completion while stderr fills the pipe buffer would deadlock.
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            // Never wait forever: a wedged run (missing model, stuck initialisation, a worker
            // process that never returns) used to hang seconv with no output at all.
            if (!proc.WaitForExit(ProcessTimeout))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                throw new InvalidOperationException(
                    $"paddleocr did not finish within {ProcessTimeout.TotalMinutes:0} minutes and was killed.");
            }
            proc.WaitForExit(); // flush the redirected streams
            var stdout = stdoutTask.GetAwaiter().GetResult();
            if (proc.ExitCode != 0)
            {
                var err = stderrTask.GetAwaiter().GetResult();
                throw new InvalidOperationException($"paddleocr exited with code {proc.ExitCode}: {err}");
            }
            return ParseStdout(stdout);
        }
        finally
        {
            try { File.Delete(pngPath); } catch { /* best-effort */ }
        }
    }

    /// <summary>Upper bound for one paddleocr run (model load + one image).</summary>
    internal static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Command line for one image. The standalone install gets the GUI's full argument set
    /// with explicit model folders (it has no model download of its own); a PATH install
    /// keeps the plain invocation and lets paddleocr resolve models itself.
    /// </summary>
    internal List<string> BuildArguments(string imagePath)
    {
        var args = new List<string> { "ocr", "-i", imagePath, "--lang", Language };

        var modelsFolder = GetModelsFolder(ExecutablePath);
        if (modelsFolder == null)
        {
            args.AddRange(new[] { "--use_angle_cls", "false" });
            return args;
        }

        // "server" = the PP-OCRv6 medium tier, the more accurate of the two bundled sizes.
        var detName = PaddleOcrModels.GetDetectionName(Language, "server");
        var recName = PaddleOcrModels.GetRecName(Language, "server");
        args.AddRange(new[]
        {
            "--use_textline_orientation", "true",
            "--use_doc_orientation_classify", "false",
            "--use_doc_unwarping", "false",
            "--text_detection_model_dir", Path.Combine(modelsFolder, "det", detName),
            "--text_detection_model_name", detName,
            "--text_recognition_model_dir", Path.Combine(modelsFolder, "rec", recName),
            "--text_recognition_model_name", recName,
            "--textline_orientation_model_dir", Path.Combine(modelsFolder, "cls", PaddleOcrModels.TextlineOrientationModelName),
            "--textline_orientation_model_name", PaddleOcrModels.TextlineOrientationModelName,
        });
        return args;
    }

    /// <summary>
    /// Parses paddleocr's stdout. The CLI prints one or more <c>[bbox], (text, conf)</c>
    /// records; we extract just the recognised text from each, joining with newlines in
    /// vertical order.
    /// </summary>
    internal static string ParseStdout(string stdout)
    {
        // Match: ('text', 0.95)  -- the recognised text is before the comma in single quotes.
        var sb = new StringBuilder();
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var startIdx = line.IndexOf("('", StringComparison.Ordinal);
            if (startIdx < 0)
            {
                continue;
            }
            var endIdx = line.IndexOf("',", startIdx + 2, StringComparison.Ordinal);
            if (endIdx < 0)
            {
                continue;
            }
            var text = line[(startIdx + 2)..endIdx];
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.Append(text);
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
        }
        catch { /* ignore */ }
    }
}
