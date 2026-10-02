using AssetStudio;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using System;
using System.IO;
using System.Text;
using ATTextureFormat = AssetsTools.NET.Texture.TextureFormat;

namespace AssetStudioGUI
{
    internal static class JuanchoTextureReplacer
    {
        public static void ReplaceTexture(AssetItem selectedAsset, string imagePath, string outputPath)
        {
            if (selectedAsset == null) throw new ArgumentNullException(nameof(selectedAsset));
            if (selectedAsset.Type != ClassIDType.Texture2D) throw new InvalidOperationException("The selected asset is not a Texture2D.");
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) throw new FileNotFoundException("Replacement image was not found.", imagePath);
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

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
                ReplaceInBundle(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, outputPath);
            else
                ReplaceInAssetsFile(sourcePath, selectedAsset.m_PathID, selectedAsset.Text, imagePath, outputPath);
        }

        private static void ReplaceInAssetsFile(string sourcePath, long pathId, string assetName, string imagePath, string outputPath)
        {
            var manager = new AssetsManager();
            try
            {
                var fileInst = manager.LoadAssetsFile(sourcePath, false);
                if (fileInst == null) throw new InvalidOperationException("Could not load the assets file.");
                ReplaceTextureInAssetsFile(manager, fileInst, pathId, assetName, imagePath);
                using var writer = new AssetsFileWriter(outputPath);
                fileInst.file.Write(writer);
            }
            finally { manager.UnloadAll(); }
        }

        private static void ReplaceInBundle(string sourcePath, long pathId, string assetName, string imagePath, string outputPath)
        {
            var manager = new AssetsManager();
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

                ReplaceTextureInAssetsFile(manager, targetFile, pathId, assetName, imagePath, targetInfo);
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
            AssetsManager manager,
            AssetsFileInstance fileInst,
            long pathId,
            string assetName,
            string imagePath,
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
            texture.m_TextureFormat = (int)ATTextureFormat.RGBA32;
            texture.EncodeTextureImage(imagePath, 1);
            texture.m_MipCount = 1;
            texture.m_MipMap = false;
            texture.m_PlatformBlob = Array.Empty<byte>();
            texture.WriteTo(baseField);
            info.SetNewData(baseField);
        }

        private static void EnsureClassDatabase(AssetsManager manager, AssetsFileInstance fileInst)
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