using AssetStudio;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ATAssetsManager = AssetsTools.NET.Extra.AssetsManager;
using AssetsTools.NET.Texture;
using StbImageSharp;
using System;
using System.IO;
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

            if (IsUnityBundle(sourcePath))
                ReplaceInBundle(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, outputPath, settings);
            else
                ReplaceInAssetsFile(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, outputPath, settings);
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
                if (bundle.originalCompression == AssetBundleCompressionType.None) bundle.file.Write(writer);
                else bundle.file.Pack(writer, bundle.originalCompression);
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
            if (info == null) throw new InvalidOperationException($"Texture2D with Path ID {pathId} was not found.");
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

            byte[] resizedData = ResizeRgba(rgbaData, sourceWidth, sourceHeight, settings.Width, settings.Height);
            if (settings.UseBgra)
                SwapRedBlueInplace(resizedData);

            int mipCount = settings.GenerateMipMaps ? Math.Max(1, settings.MipCount) : 1;
            bool useBgraForEncoder = settings.UseBgra && IsRawChannelFormat((ATTextureFormat)settings.Format);
            if (useBgraForEncoder && (ATTextureFormat)settings.Format != ATTextureFormat.BGRA32)
                SwapRedBlueInplace(resizedData);

            texture.EncodeTextureRaw(
                resizedData,
                settings.Width,
                settings.Height,
                (ATTextureFormat)settings.Format,
                mipCount,
                quality: 3,
                useBgra: useBgraForEncoder);

            if (!settings.GenerateMipMaps)
                texture.m_MipCount = 1;
            texture.m_MipMap = settings.GenerateMipMaps;
            texture.m_StreamingMipmaps = false;
            texture.m_StreamingMipmapsPriority = 0;

            texture.WriteTo(baseField);
            info.SetNewData(baseField);
        }

        private static bool IsRawChannelFormat(ATTextureFormat format)
        {
            return format == ATTextureFormat.RGBA32 ||
                   format == ATTextureFormat.BGRA32 ||
                   format == ATTextureFormat.RGB24 ||
                   format == ATTextureFormat.RGB565 ||
                   format == ATTextureFormat.R8 ||
                   format == ATTextureFormat.R16 ||
                   format == ATTextureFormat.RG16 ||
                   format == ATTextureFormat.RGBA4444 ||
                   format == ATTextureFormat.ARGB4444 ||
                   format == ATTextureFormat.Alpha8;
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

        private static byte[] ResizeRgba(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
                return (byte[])source.Clone();

            byte[] result = new byte[targetWidth * targetHeight * 4];
            for (int y = 0; y < targetHeight; y++)
            {
                int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
                for (int x = 0; x < targetWidth; x++)
                {
                    int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                    int sourceIndex = (sourceY * sourceWidth + sourceX) * 4;
                    int targetIndex = (y * targetWidth + x) * 4;
                    result[targetIndex] = source[sourceIndex];
                    result[targetIndex + 1] = source[sourceIndex + 1];
                    result[targetIndex + 2] = source[sourceIndex + 2];
                    result[targetIndex + 3] = source[sourceIndex + 3];
                }
            }
            return result;
        }

        private static void EnsureClassDatabase(ATAssetsManager manager, AssetsFileInstance fileInst)
        {
            if (fileInst.file.Metadata.TypeTreeEnabled || manager.ClassDatabase != null) return;
            string package = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
            if (!File.Exists(package))
                throw new InvalidOperationException("This file has no embedded TypeTree. Place classdata.tpk next to the executable.");
            manager.LoadClassPackage(package);
            manager.LoadClassDatabaseFromPackage(fileInst.file.Metadata.UnityVersion);
        }

        private static bool IsUnityBundle(string path)
        {
            using var stream = File.OpenRead(path);
            var signature = new byte[7];
            int read = stream.Read(signature, 0, signature.Length);
            return read == signature.Length && Encoding.ASCII.GetString(signature).StartsWith("Unity", StringComparison.Ordinal);
        }
    }
}
