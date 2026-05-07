using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HVTTool;

internal static class Cli
{
    private const string CommandName = "\"ObsCure Texture Editor CLI\"";

    public static int Run(string[] args)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (args.Length == 0)
        {
            PrintHelpHint();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "extract" when args.Length == 3 => Extract(args[1], args[2]),
                "extract-all" when args.Length == 3 => ExtractAll(args[1], args[2]),
                "reinsert" when args.Length == 4 => ReinsertStandalone(args[1], args[2], args[3]),
                "reinsert-container" when args.Length == 5 => ReinsertContainer(args[1], args[2], args[3], args[4]),
                "batch-reinsert" when args.Length == 4 => BatchReinsert(args[1], args[2], args[3]),
                "list" when args.Length == 2 => ListContainer(args[1]),
                _ => UsageError(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Extract(string input, string output)
    {
        if (Directory.Exists(input)) return ExtractAll(input, output);
        if (!File.Exists(input)) throw new FileNotFoundException("Input file not found.", input);

        if (IsDic(input) || IsXbr(input))
        {
            Directory.CreateDirectory(output);
            return ExtractContainer(input, output);
        }

        var image = DecodeStandalone(input);
        string outPath = ResolvePngOutputPath(output, Path.GetFileNameWithoutExtension(input) + ".png");
        EnsureParentDirectory(outPath);
        PngCodec.SaveBgra(outPath, image.Bgra, image.Width, image.Height);
        Console.WriteLine(outPath);
        return 0;
    }

    private static int ExtractAll(string inputFolder, string outputFolder)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(inputFolder);

        int ok = 0;
        int fail = 0;
        foreach (string path in Directory.EnumerateFiles(inputFolder, "*.*", SearchOption.AllDirectories).Where(IsSupported))
        {
            string relative = Path.GetRelativePath(inputFolder, Path.GetDirectoryName(path)!);
            string outDir = relative == "." ? outputFolder : Path.Combine(outputFolder, relative);
            Directory.CreateDirectory(outDir);

            try
            {
                if (IsDic(path) || IsXbr(path))
                {
                    string containerOut = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path));
                    Directory.CreateDirectory(containerOut);
                    ExtractContainer(path, containerOut);
                }
                else
                {
                    var image = DecodeStandalone(path);
                    string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png");
                    PngCodec.SaveBgra(outPath, image.Bgra, image.Width, image.Height);
                    Console.WriteLine(outPath);
                }
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Console.Error.WriteLine($"fail: {path}: {ex.Message}");
            }
        }

        Console.WriteLine($"done: {ok} ok, {fail} failed");
        return fail == 0 ? 0 : 1;
    }

    private static int ReinsertStandalone(string inputTexture, string pngPath, string outputTexture)
    {
        if (!File.Exists(inputTexture)) throw new FileNotFoundException("Texture file not found.", inputTexture);
        if (!File.Exists(pngPath)) throw new FileNotFoundException("PNG file not found.", pngPath);

        if (IsHvi(inputTexture))
        {
            var hvi = new HviFile(inputTexture);
            byte[] rgba = LoadPngForTexture(pngPath, hvi.Width, hvi.Height);
            var (palette, pixels) = Ps2Codec.EncodeFromRgba(hvi, rgba);
            hvi.SaveAs(outputTexture, palette, pixels);
        }
        else if (IsHvt(inputTexture) && FinalExamHvt.LooksLikeFinalExam(inputTexture))
        {
            var fx = new FinalExamHvt(inputTexture);
            byte[] rgba = LoadPngForTexture(pngPath, fx.Width, fx.Height);
            byte[] encoded = FinalExamCodec.EncodeFromRgba(fx, rgba);
            fx.SaveAs(outputTexture, encoded);
        }
        else if (IsHvt(inputTexture))
        {
            var hvt = new HvtFile(inputTexture);
            byte[] rgba = LoadPngForTexture(pngPath, hvt.Width, hvt.Height);
            byte[] encoded = WiiGcCodec.EncodeFromRgba(hvt, rgba);
            hvt.SaveAs(outputTexture, encoded);
        }
        else
        {
            throw new NotSupportedException("Use reinsert-container for .dic/.dip/.xbr files.");
        }

        Console.WriteLine(outputTexture);
        return 0;
    }

    private static int ReinsertContainer(string containerPath, string selector, string pngPath, string outputPath)
    {
        if (!File.Exists(containerPath)) throw new FileNotFoundException("Container file not found.", containerPath);
        if (!File.Exists(pngPath)) throw new FileNotFoundException("PNG file not found.", pngPath);

        if (IsDic(containerPath))
        {
            var dic = new DicFile(containerPath);
            var tex = Select(dic.Textures, selector, t => t.Index, t => t.Name);
            byte[] rgba = LoadPngForTexture(pngPath, tex.Width, tex.Height);
            byte[] encoded = DicCodec.EncodeFromRgba(tex, rgba);
            dic.ReplaceImageBytes(tex, encoded);
            dic.Save(outputPath);
        }
        else if (IsXbr(containerPath))
        {
            var xbr = new XbrFile(containerPath);
            var tex = Select(xbr.Textures, selector, t => t.Index, t => t.Name);
            byte[] rgba = LoadPngForTexture(pngPath, tex.Width, tex.Height);
            byte[] encoded = XbrCodec.EncodeFromRgba(tex, rgba);
            xbr.ReplaceImageBytes(tex, encoded, encoded.Length);
            xbr.Save(outputPath);
        }
        else
        {
            throw new NotSupportedException("Container must be .dic, .dip, or .xbr.");
        }

        Console.WriteLine(outputPath);
        return 0;
    }

    private static int BatchReinsert(string sourceFolder, string pngFolder, string outputFolder)
    {
        if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException(sourceFolder);
        if (!Directory.Exists(pngFolder)) throw new DirectoryNotFoundException(pngFolder);
        Directory.CreateDirectory(outputFolder);

        var index = Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
            .Where(p => IsHvt(p) || IsHvi(p))
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int ok = 0;
        int skipped = 0;
        int failed = 0;
        foreach (string png in Directory.EnumerateFiles(pngFolder, "*.png", SearchOption.TopDirectoryOnly))
        {
            string key = Path.GetFileNameWithoutExtension(png);
            if (!index.TryGetValue(key, out string? texture))
            {
                skipped++;
                Console.WriteLine($"skip: {Path.GetFileName(png)}");
                continue;
            }

            try
            {
                string relativeDir = Path.GetRelativePath(sourceFolder, Path.GetDirectoryName(texture)!);
                string outDir = relativeDir == "." ? outputFolder : Path.Combine(outputFolder, relativeDir);
                Directory.CreateDirectory(outDir);
                ReinsertStandalone(texture, png, Path.Combine(outDir, Path.GetFileName(texture)));
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"fail: {Path.GetFileName(png)}: {ex.Message}");
            }
        }

        Console.WriteLine($"done: {ok} reinserted, {skipped} skipped, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static int ListContainer(string path)
    {
        if (IsDic(path))
        {
            var dic = new DicFile(path);
            foreach (var t in dic.Textures)
                Console.WriteLine($"{t.Index:D3}\t{t.Name}\t{t.Width}x{t.Height}\t{t.FormatLabel}");
            return 0;
        }

        if (IsXbr(path))
        {
            var xbr = new XbrFile(path);
            foreach (var t in xbr.Textures)
                Console.WriteLine($"{t.Index:D3}\t{t.Name}\t{t.Width}x{t.Height}\t{t.FormatLabel}");
            return 0;
        }

        throw new NotSupportedException("list only supports .dic, .dip, and .xbr containers.");
    }

    private static int ExtractContainer(string path, string outputFolder)
    {
        int ok = 0;
        int fail = 0;
        if (IsDic(path))
        {
            var dic = new DicFile(path);
            foreach (var t in dic.Textures)
            {
                try
                {
                    byte[] bgra = DicCodec.DecodeToBgra(t);
                    string name = $"{t.Index:D3}_{SafeFilename(t.Name)}_{t.Width}x{t.Height}_{t.Format}.png";
                    string outPath = Path.Combine(outputFolder, name);
                    PngCodec.SaveBgra(outPath, bgra, t.Width, t.Height);
                    Console.WriteLine(outPath);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.Error.WriteLine($"fail: [{t.Index}] {t.Name}: {ex.Message}");
                }
            }
        }
        else
        {
            var xbr = new XbrFile(path);
            foreach (var t in xbr.Textures)
            {
                try
                {
                    byte[] bgra = XbrCodec.DecodeToBgra(t);
                    string name = $"{t.Index:D3}_{SafeFilename(t.Name)}_{t.Width}x{t.Height}_{t.Format}.png";
                    string outPath = Path.Combine(outputFolder, name);
                    PngCodec.SaveBgra(outPath, bgra, t.Width, t.Height);
                    Console.WriteLine(outPath);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.Error.WriteLine($"fail: [{t.Index}] {t.Name}: {ex.Message}");
                }
            }
        }

        return fail == 0 && ok > 0 ? 0 : 1;
    }

    private static (byte[] Bgra, int Width, int Height) DecodeStandalone(string path)
    {
        if (IsHvi(path))
        {
            var hvi = new HviFile(path);
            return (Ps2Codec.DecodeToBgra(hvi), hvi.Width, hvi.Height);
        }

        if (IsHvt(path) && FinalExamHvt.LooksLikeFinalExam(path))
        {
            var fx = new FinalExamHvt(path);
            return (FinalExamCodec.DecodeToBgra(fx), fx.Width, fx.Height);
        }

        if (IsHvt(path))
        {
            var hvt = new HvtFile(path);
            return (WiiGcCodec.DecodeToBgra(hvt), hvt.Width, hvt.Height);
        }

        throw new NotSupportedException("Input must be .hvt or .hvi.");
    }

    private static byte[] LoadPngForTexture(string path, int width, int height)
    {
        var png = PngCodec.LoadRgba(path);
        if (png.Width != width || png.Height != height)
        {
            Console.Error.WriteLine($"warning: resizing PNG {png.Width}x{png.Height} to {width}x{height}");
            return PngCodec.ResizeRgbaNearest(png.Rgba, png.Width, png.Height, width, height);
        }
        return png.Rgba;
    }

    private static T Select<T>(IEnumerable<T> items, string selector, Func<T, int> getIndex, Func<T, string> getName)
    {
        if (int.TryParse(selector, out int idx))
        {
            foreach (T item in items)
                if (getIndex(item) == idx)
                    return item;
        }

        foreach (T item in items)
            if (string.Equals(getName(item), selector, StringComparison.OrdinalIgnoreCase))
                return item;

        throw new InvalidOperationException($"Texture \"{selector}\" was not found.");
    }

    private static void EnsureParentDirectory(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string ResolvePngOutputPath(string output, string defaultName)
    {
        if (Directory.Exists(output) || Path.GetExtension(output).Length == 0)
            return Path.Combine(output, defaultName);
        return output;
    }

    private static string SafeFilename(string name)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            sb.Append(Array.IndexOf(bad, c) >= 0 || c < 32 ? '_' : c);
        return sb.Length > 0 ? sb.ToString() : "texture";
    }

    private static bool IsHvt(string path) => path.EndsWith(".hvt", StringComparison.OrdinalIgnoreCase);
    private static bool IsHvi(string path) => path.EndsWith(".hvi", StringComparison.OrdinalIgnoreCase);
    private static bool IsDic(string path) =>
        path.EndsWith(".dic", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dip", StringComparison.OrdinalIgnoreCase);
    private static bool IsXbr(string path) => path.EndsWith(".xbr", StringComparison.OrdinalIgnoreCase);
    private static bool IsSupported(string path) => IsHvt(path) || IsHvi(path) || IsDic(path) || IsXbr(path);

    private static bool IsHelp(string arg) => arg is "-h" or "--help";

    private static int UsageError(string command)
    {
        Console.WriteLine($"Unknown or incomplete command: {command}");
        PrintHelpHint();
        return 2;
    }

    private static void PrintHelpHint()
    {
        Console.WriteLine("ObsCure Texture Editor CLI by HeitorSpectre");
        Console.WriteLine();
        Console.WriteLine($"Use '{CommandName} -h' or '{CommandName} --help' for more information.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {CommandName} extract <file-or-folder> <png-or-output-folder>");
        Console.WriteLine($"  {CommandName} reinsert <texture.hvt|texture.hvi> <image.png> <output-file>");
        Console.WriteLine($"  {CommandName} reinsert-container <texture.dic/texture.dip/texture.dic> <index-or-name> <image.png> <output-file>");
        Console.WriteLine($"  {CommandName} batch-reinsert <source-folder> <png-folder> <output-folder>");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ObsCure Texture Editor CLI");
        Console.WriteLine();
        Console.WriteLine("Supported platforms and files:");
        Console.WriteLine("  PC:         .dic, .dip, Final Exam .hvt");
        Console.WriteLine("  PS2:        .dic, .hvi");
        Console.WriteLine("  PSP:        .dic");
        Console.WriteLine("  PS3:        Final Exam .hvt");
        Console.WriteLine("  Wii/GC:     .dic, .hvt");
        Console.WriteLine("  Xbox:       .xbr");
        Console.WriteLine("  Xbox 360:   Final Exam .hvt");
        Console.WriteLine("  Image I/O:  .png");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {CommandName} extract <file-or-folder> <png-or-output-folder>");
        Console.WriteLine($"  {CommandName} extract-all <source-folder> <output-folder>");
        Console.WriteLine($"  {CommandName} list <container.dic|container.dip|container.xbr>");
        Console.WriteLine($"  {CommandName} reinsert <texture.hvt|texture.hvi> <image.png> <output-file>");
        Console.WriteLine($"  {CommandName} reinsert-container <container> <index-or-name> <image.png> <output-file>");
        Console.WriteLine($"  {CommandName} batch-reinsert <source-folder> <png-folder> <output-folder>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  extract");
        Console.WriteLine("    Extracts one texture file to PNG, one container to a folder, or all supported files");
        Console.WriteLine("    from a folder while preserving subfolders.");
        Console.WriteLine($"    Example: {CommandName} extract npc8_psp.dic out/npc8");
        Console.WriteLine($"    Example: {CommandName} extract logo.hvt logo.png");
        Console.WriteLine();
        Console.WriteLine("  extract-all");
        Console.WriteLine("    Extracts every supported texture/container found recursively in a source folder.");
        Console.WriteLine($"    Example: {CommandName} extract-all ./textures ./png_out");
        Console.WriteLine();
        Console.WriteLine("  list");
        Console.WriteLine("    Lists texture indexes, names, dimensions, and formats inside .dic/.dip/.xbr containers.");
        Console.WriteLine($"    Example: {CommandName} list npc8_psp.dic");
        Console.WriteLine();
        Console.WriteLine("  reinsert");
        Console.WriteLine("    Reinserts one PNG into a standalone .hvt or .hvi file and writes a new file.");
        Console.WriteLine("    If the PNG size differs, it is resized to the texture dimensions.");
        Console.WriteLine($"    Example: {CommandName} reinsert logo.hvt logo.png logo_new.hvt");
        Console.WriteLine();
        Console.WriteLine("  reinsert-container");
        Console.WriteLine("    Reinserts one PNG into a texture inside .dic/.dip/.xbr by index or exact name.");
        Console.WriteLine($"    Example: {CommandName} reinsert-container npc8_psp.dic 0 body.png npc8_psp_new.dic");
        Console.WriteLine($"    Example: {CommandName} reinsert-container npc8_psp.dic Npc8_body body.png npc8_psp_new.dic");
        Console.WriteLine();
        Console.WriteLine("  batch-reinsert");
        Console.WriteLine("    Matches PNG filenames to standalone .hvt/.hvi filenames in a source folder, then writes");
        Console.WriteLine("    rebuilt files to the output folder.");
        Console.WriteLine($"    Example: {CommandName} batch-reinsert ./textures ./pngs ./rebuilt");
    }
}
