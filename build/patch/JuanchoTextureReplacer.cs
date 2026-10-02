using AssetStudio;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ATAssetsManager = AssetsTools.NET.Extra.AssetsManager;
using AssetsTools.NET.Texture;
using StbImageSharp;
using System;
using System.IO;
using System.Linq;
using System.Text;
using ATTextureFormat = AssetsTools.NET.Texture.TextureFormat;
using StbColorComponents = StbImageSharp.ColorComponents;

namespace AssetStudioGUI
{
    internal static class JuanchoTextureReplacer
    {
        public static void ReplaceTexture(AssetItem selectedAsset, string imagePath, string outputPath, JuanchoTextureSettings settings)
        {
            if (selectedAsset == null) throw new ArgumentNullException(nameof(selectedAsset));
            if (selectedAsset.Type != ClassIDType.Texture2D) throw new InvalidOperationException("The selected asset is not a Texture2D.");
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) throw new FileNotFoundException("Replacement image was not found.", imagePath);
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            ValidateSettings(settings);

            string sourcePath = string.IsNullOrWhiteSpace(selectedAsset.SourceFile.originalPath)
                ? selectedAsset.SourceFile.fullName
                : selectedAsset.SourceFile.originalPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("The original Unity file could not be found.", sourcePath);

            sourcePath = Path.GetFullPath(sourcePath);
            outputPath = Path.GetFullPath(outputPath);
            if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the modified file to a new path.");

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            string tempPath = outputPath + ".juancho.tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            try
            {
                if (IsUnityBundle(sourcePath))
                    ReplaceInBundle(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, tempPath, settings);
                else
                    ReplaceInAssetsFile(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, tempPath, settings);

                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    throw new IOException("The replacement produced an empty output file.");

                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                File.Move(tempPath, outputPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void ValidateSettings(JuanchoTextureSettings settings)
        {
            if (settings.Width < 1 || settings.Height < 1)
                throw new ArgumentOutOfRangeException(nameof(settings), "Texture width and height must be greater than zero.");

            if (!Enum.IsDefined(typeof(ATTextureFormat), settings.Format))
                throw new ArgumentException($"Texture format value {settings.Format} is not supported by this build.");

            int maxMipCount = GetMaxMipCount(settings.Width, settings.Height);
            if (settings.MipCount < 1 || settings.MipCount > maxMipCount)
                throw new ArgumentOutOfRangeException(nameof(settings), $"Mip count must be between 1 and {maxMipCount} for {settings.Width} × {settings.Height}.");

            if (!settings.GenerateMipMaps && settings.MipCount != 1)
                throw new ArgumentException("Mip count must be 1 when mip maps are disabled.");

            if (settings.AnisoLevel < 1 || settings.AnisoLevel > 16)
                throw new ArgumentOutOfRangeException(nameof(settings), "Anisotropic level must be between 1 and 16.");

            if (settings.WrapMode < 0 || settings.WrapMode > 3)
                throw new ArgumentOutOfRangeException(nameof(settings), "Wrap mode is invalid.");
        }

        private static void ReplaceInAssetsFile(string sourcePath, long pathId, string assetName, string imagePath, string outputPath, JuanchoTextureSettings settings)
        {
            var manager = new ATAssetsManager();
            try
            {
                var fileInst = manager.LoadAssetsFile(sourcePath, false);
                if (fileInst == null) throw new InvalidOperationException("Could not load the assets file.");
                ReplaceTextureInAssetsFile(manager, fileInst, pathId, assetName, imagePath, settings);
                using var writer = new AssetsFileWriter(outputPath);
                fileInst.file.Write(writer);
            }
            finally { manager.UnloadAll(); }
        }

        private static void ReplaceInBundle(string sourcePath, long pathId, string assetName, string imagePath, string outputPath, JuanchoTextureSettings settings)
        {
            var manager = new ATAssetsManager();
            BundleFileInstance bundle = null;
            try
            {
                bundle = manager.LoadBundleFile(sourcePath, true);
                if (bundle == null || bundle.file == null) throw new InvalidOperationException("Could not load the Unity bundle.");

                AssetsFileInstance targetFile = null;
                AssetFileInfo targetInfo = null;
                foreach (var directoryInfo in bundle.file.BlockAndDirInfo.DirectoryInfos)
                {
                    if (!directoryInfo.IsSerialized) continue;
                    var fileInst = manager.LoadAssetsFileFromBundle(bundle, directoryInfo.Name, false);
                    if (fileInst == null) continue;
                    var info = fileInst.file.GetAssetInfo(pathId);
                    if (info == null || info.GetTypeId(fileInst.file) != (int)AssetClassID.Texture2D) continue;

                    var baseField = manager.GetBaseField(fileInst, info);
                    string currentName = baseField["m_Name"].AsString;
                    if (!string.IsNullOrEmpty(assetName) && !string.Equals(currentName, assetName, StringComparison.Ordinal)) continue;

                    targetFile = fileInst;
                    targetInfo = info;
                    break;
                }

                if (targetFile == null || targetInfo == null)
                    throw new InvalidOperationException($"Texture2D with Path ID {pathId} was not found.");

                ReplaceTextureInAssetsFile(manager, targetFile, pathId, assetName, imagePath, settings, targetInfo);

                int directoryIndex = bundle.file.GetFileIndex(targetFile.name);
                if (directoryIndex < 0) throw new InvalidOperationException($"Bundle entry '{targetFile.name}' was not found.");

                bundle.file.BlockAndDirInfo.DirectoryInfos[directoryIndex].SetNewData(targetFile.file);

                using var writer = new AssetsFileWriter(outputPath);
                if (bundle.originalCompression == AssetBundleCompressionType.None)
                    bundle.file.Write(writer);
                else
                    bundle.file.Pack(writer, bundle.originalCompression);
            }
            finally
            {
                if (bundle != null) manager.UnloadBundleFile(bundle);
                else manager.UnloadAll();
            }
        }

        private static void ReplaceTextureInAssetsFile(
            ATAssetsManager manager,
            AssetsFileInstance fileInst,
            long pathId,
            string assetName,
            string imagePath,
            JuanchoTextureSettings settings,
            AssetFileInfo knownInfo = null)
        {
            EnsureClassDatabase(manager, fileInst);

            var info = knownInfo ?? fileInst.file.GetAssetInfo(pathId);
            if (info == null)
                throw new InvalidOperationException($"Texture2D with Path ID {pathId} was not found.");

            if (info.GetTypeId(fileInst.file) != (int)AssetClassID.Texture2D)
                throw new InvalidOperationException($"Path ID {pathId} is not a Texture2D.");

            var baseField = manager.GetBaseField(fileInst, info);
            string currentName = baseField["m_Name"].AsString;
            if (!string.IsNullOrEmpty(assetName) && !string.Equals(currentName, assetName, StringComparison.Ordinal))
                throw new InvalidOperationException($"The selected Texture2D resolved to '{currentName}'.");

            var texture = TextureFile.ReadTextureFile(baseField);
            texture.m_TextureFormat = settings.Format;

            texture.m_TextureSettings.m_FilterMode = settings.FilterMode;
            texture.m_TextureSettings.m_Aniso = settings.AnisoLevel;
            texture.m_TextureSettings.m_MipBias = settings.MipMapBias;
            texture.m_TextureSettings.m_WrapMode = settings.WrapMode;
            texture.m_TextureSettings.m_WrapU = settings.WrapMode;
            texture.m_TextureSettings.m_WrapV = settings.WrapMode;
            texture.m_TextureSettings.m_WrapW = settings.WrapMode;

            texture.m_PlatformBlob = Array.Empty<byte>();
            texture.m_StreamingMipmaps = false;
            texture.m_StreamingMipmapsPriority = 0;

            byte[] rgbaData;
            int sourceWidth;
            int sourceHeight;

            using (var stream = File.OpenRead(imagePath))
            {
                var image = ImageResult.FromStream(stream, StbColorComponents.RedGreenBlueAlpha);
                rgbaData = image.Data;
                sourceWidth = image.Width;
                sourceHeight = image.Height;
            }

            byte[] resizedData = ResizeRgbaBilinear(
                rgbaData,
                sourceWidth,
                sourceHeight,
                settings.Width,
                settings.Height);

            // StbImageSharp provides RGBA32. The channel choice controls the
            // byte order supplied to the encoder for formats where that distinction applies.
            if (settings.UseBgra)
                SwapRedBlueInplace(resizedData);

            int mipCount = settings.GenerateMipMaps ? settings.MipCount : 1;
            bool useBgraInput = settings.UseBgra;

            texture.EncodeTextureRaw(
                resizedData,
                settings.Width,
                settings.Height,
                (ATTextureFormat)settings.Format,
                mipCount,
                quality: 3,
                useBgra: useBgraInput);

            if (settings.GenerateMipMaps && settings.MipCount > 1 && texture.m_MipCount < settings.MipCount)
                throw new InvalidOperationException(
                    $"The selected encoder generated only {texture.m_MipCount} mip level(s), but {settings.MipCount} were requested. " +
                    "The bundled native encoder is required for multi-mip output.");

            texture.m_MipCount = settings.GenerateMipMaps ? texture.m_MipCount : 1;
            texture.m_MipMap = settings.GenerateMipMaps;

            texture.WriteTo(baseField);
            info.SetNewData(baseField);
        }

        private static byte[] ResizeRgbaBilinear(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
                return (byte[])source.Clone();

            byte[] result = new byte[targetWidth * targetHeight * 4];

            double xScale = sourceWidth / (double)targetWidth;
            double yScale = sourceHeight / (double)targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                double srcY = ((y + 0.5) * yScale) - 0.5;
                int y0 = Math.Max(0, Math.Min(sourceHeight - 1, (int)Math.Floor(srcY)));
                int y1 = Math.Min(sourceHeight - 1, y0 + 1);
                double fy = Math.Max(0, Math.Min(1, srcY - Math.Floor(srcY)));

                for (int x = 0; x < targetWidth; x++)
                {
                    double srcX = ((x + 0.5) * xScale) - 0.5;
                    int x0 = Math.Max(0, Math.Min(sourceWidth - 1, (int)Math.Floor(srcX)));
                    int x1 = Math.Min(sourceWidth - 1, x0 + 1);
                    double fx = Math.Max(0, Math.Min(1, srcX - Math.Floor(srcX)));

                    int p00 = (y0 * sourceWidth + x0) * 4;
                    int p10 = (y0 * sourceWidth + x1) * 4;
                    int p01 = (y1 * sourceWidth + x0) * 4;
                    int p11 = (y1 * sourceWidth + x1) * 4;
                    int dst = (y * targetWidth + x) * 4;

                    for (int channel = 0; channel < 4; channel++)
                    {
                        double top = source[p00 + channel] * (1 - fx) + source[p10 + channel] * fx;
                        double bottom = source[p01 + channel] * (1 - fx) + source[p11 + channel] * fx;
                        result[dst + channel] = (byte)Math.Max(0, Math.Min(255, Math.Round(top * (1 - fy) + bottom * fy)));
                    }
                }
            }

            return result;
        }

        private static void SwapRedBlueInplace(byte[] data)
        {
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                byte r = data[i];
                data[i] = data[i + 2];
                data[i + 2] = r;
            }
        }

        private static int GetMaxMipCount(int width, int height)
        {
            int maxDimension = Math.Max(1, Math.Max(width, height));
            int count = 1;
            while (maxDimension > 1)
            {
                maxDimension >>= 1;
                count++;
            }
            return count;
        }

        private static void EnsureClassDatabase(ATAssetsManager manager, AssetsFileInstance fileInst)
        {
            if (fileInst.file.Metadata.TypeTreeEnabled || manager.ClassDatabase != null) return;

            string package = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
            if (!File.Exists(package))
                throw new InvalidOperationException(
                    "This Unity file has no embedded TypeTree and classdata.tpk was not found. " +
                    "The Juancho package normally includes classdata.tpk; reinstall the complete package.");

            manager.LoadClassPackage(package);
            manager.LoadClassDatabaseFromPackage(fileInst.file.Metadata.UnityVersion);
        }

        private static bool IsUnityBundle(string path)
        {
            using var stream = File.OpenRead(path);
            var signature = new byte[7];
            int read = stream.Read(signature, 0, signature.Length);
            return read == signature.Length &&
                   Encoding.ASCII.GetString(signature).StartsWith("Unity", StringComparison.Ordinal);
        }
    }
}
