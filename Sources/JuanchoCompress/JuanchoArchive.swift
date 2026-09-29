import Foundation
import Compression
import CryptoKit

struct JuanchoChunk: Codable {
    let offset: UInt64
    let compressedSize: UInt64
    let originalSize: UInt64
    let method: UInt8
}

struct JuanchoEntry: Codable {
    let path: String
    let originalSize: UInt64
    let sha256: String
    let chunks: [JuanchoChunk]
}

struct JuanchoManifest: Codable {
    let rootName: String
    let entries: [JuanchoEntry]
}

enum JuanchoError: LocalizedError {
    case invalidArchive
    case unsupportedVersion(UInt16)
    case unsafePath(String)
    case sourceMissing(String)
    case notDirectory(String)
    case outputExists(String)
    case compressionFailed
    case checksumMismatch(String)
    case cancelled

    var errorDescription: String? {
        switch self {
        case .invalidArchive: return "The file is not a valid JUANCHO archive."
        case .unsupportedVersion(let version): return "Unsupported JUANCHO version: \(version)."
        case .unsafePath(let path): return "Unsafe archive path: \(path)"
        case .sourceMissing(let path): return "Source does not exist: \(path)"
        case .notDirectory(let path): return "Source is not a directory: \(path)"
        case .outputExists(let path): return "Output already exists: \(path)"
        case .compressionFailed: return "Compression or decompression failed."
        case .checksumMismatch(let path): return "Checksum verification failed: \(path)"
        case .cancelled: return "Operation cancelled."
        }
    }
}

final class JuanchoArchive {
    static let magic = Data([74, 85, 65, 78, 67, 72, 79, 0])
    static let version: UInt16 = 1
    static let headerSize: UInt64 = 64
    static let chunkSize = 1024 * 1024

    static func compressFolder(at sourceURL: URL, to outputURL: URL,
                               progress: @escaping (Double, String) -> Void,
                               isCancelled: @escaping () -> Bool) throws {
        let fm = FileManager.default
        var isDirectory: ObjCBool = false
        guard fm.fileExists(atPath: sourceURL.path, isDirectory: &isDirectory) else {
            throw JuanchoError.sourceMissing(sourceURL.path)
        }
        guard isDirectory.boolValue else { throw JuanchoError.notDirectory(sourceURL.path) }
        if fm.fileExists(atPath: outputURL.path) { throw JuanchoError.outputExists(outputURL.path) }

        let rootName = sourceURL.lastPathComponent.isEmpty ? "Archive" : sourceURL.lastPathComponent
        let files = try collectFiles(in: sourceURL)
        let totalBytes = files.reduce(UInt64(0)) { $0 + $1.size }
        var processedBytes: UInt64 = 0
        var entries: [JuanchoEntry] = []

        fm.createFile(atPath: outputURL.path, contents: nil)
        let output = try FileHandle(forWritingTo: outputURL)
        defer { try? output.close() }
        try output.write(contentsOf: Data(repeating: 0, count: Int(headerSize)))

        for item in files {
            if isCancelled() { throw JuanchoError.cancelled }
            progress(totalBytes == 0 ? 1 : Double(processedBytes) / Double(totalBytes), "Compressing \(item.relativePath)")
            let input = try FileHandle(forReadingFrom: item.url)
            defer { try? input.close() }
            var chunks: [JuanchoChunk] = []
            var hasher = SHA256()
            var originalSize: UInt64 = 0

            while true {
                if isCancelled() { throw JuanchoError.cancelled }
                guard let data = try input.read(upToCount: chunkSize), !data.isEmpty else { break }
                hasher.update(data: data)
                originalSize += UInt64(data.count)

                let encoded = try compressChunk(data)
                let offset = output.offsetInFile
                try output.write(contentsOf: encoded.data)
                chunks.append(JuanchoChunk(offset: offset,
                                           compressedSize: UInt64(encoded.data.count),
                                           originalSize: UInt64(data.count),
                                           method: encoded.compressed ? 1 : 0))
                processedBytes += UInt64(data.count)
                progress(totalBytes == 0 ? 1 : Double(processedBytes) / Double(totalBytes), "Compressing \(item.relativePath)")
            }

            entries.append(JuanchoEntry(
                path: item.relativePath,
                originalSize: originalSize,
                sha256: hasher.finalize().map { String(format: "%02x", $0) }.joined(),
                chunks: chunks
            ))
        }

        let indexOffset = output.offsetInFile
        let manifest = JuanchoManifest(rootName: rootName, entries: entries)
        let indexData = try JSONEncoder().encode(manifest)
        try output.write(contentsOf: indexData)

        let header = makeHeader(entryCount: UInt64(entries.count),
                                indexOffset: indexOffset,
                                indexSize: UInt64(indexData.count))
        try output.seek(toOffset: 0)
        try output.write(contentsOf: header)
        progress(1, "Finished")
    }

    static func extract(archiveURL: URL, to destinationURL: URL,
                        progress: @escaping (Double, String) -> Void,
                        isCancelled: @escaping () -> Bool) throws {
        let input = try FileHandle(forReadingFrom: archiveURL)
        defer { try? input.close() }

        let header = try input.read(upToCount: Int(headerSize)) ?? Data()
        guard header.count == Int(headerSize), header.prefix(8) == magic else { throw JuanchoError.invalidArchive }
        guard header.readUInt16LE(at: 8) == version else { throw JuanchoError.invalidArchive }
        let indexOffset = header.readUInt64LE(at: 24)
        let indexSize = header.readUInt64LE(at: 32)
        try input.seek(toOffset: indexOffset)
        guard let indexData = try input.read(upToCount: Int(indexSize)),
              indexData.count == Int(indexSize) else { throw JuanchoError.invalidArchive }
        let manifest = try JSONDecoder().decode(JuanchoManifest.self, from: indexData)

        let fm = FileManager.default
        try fm.createDirectory(at: destinationURL, withIntermediateDirectories: true)
        let totalBytes = manifest.entries.reduce(UInt64(0)) { $0 + $1.originalSize }
        var processedBytes: UInt64 = 0

        for entry in manifest.entries {
            if isCancelled() { throw JuanchoError.cancelled }
            let safeURL = try safeDestinationURL(root: destinationURL, relativePath: entry.path)
            try fm.createDirectory(at: safeURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            fm.createFile(atPath: safeURL.path, contents: nil)
            let output = try FileHandle(forWritingTo: safeURL)
            defer { try? output.close() }
            var hasher = SHA256()

            for chunk in entry.chunks {
                if isCancelled() { throw JuanchoError.cancelled }
                try input.seek(toOffset: chunk.offset)
                guard let compressed = try input.read(upToCount: Int(chunk.compressedSize)),
                      compressed.count == Int(chunk.compressedSize) else { throw JuanchoError.invalidArchive }

                let decoded: Data
                if chunk.method == 0 {
                    guard compressed.count == Int(chunk.originalSize) else { throw JuanchoError.invalidArchive }
                    decoded = compressed
                } else if chunk.method == 1 {
                    decoded = try decompressChunk(compressed, expectedSize: Int(chunk.originalSize))
                } else {
                    throw JuanchoError.invalidArchive
                }

                hasher.update(data: decoded)
                try output.write(contentsOf: decoded)
                processedBytes += UInt64(decoded.count)
                progress(totalBytes == 0 ? 1 : Double(processedBytes) / Double(totalBytes), "Extracting \(entry.path)")
            }

            let actualHash = hasher.finalize().map { String(format: "%02x", $0) }.joined()
            guard actualHash == entry.sha256 else { throw JuanchoError.checksumMismatch(entry.path) }
        }
        progress(1, "Finished")
    }

    static func readManifest(from archiveURL: URL) throws -> JuanchoManifest {
        let input = try FileHandle(forReadingFrom: archiveURL)
        defer { try? input.close() }
        let header = try input.read(upToCount: Int(headerSize)) ?? Data()
        guard header.count == Int(headerSize), header.prefix(8) == magic else { throw JuanchoError.invalidArchive }
        guard header.readUInt16LE(at: 8) == version else { throw JuanchoError.invalidArchive }
        let indexOffset = header.readUInt64LE(at: 24)
        let indexSize = header.readUInt64LE(at: 32)
        try input.seek(toOffset: indexOffset)
        guard let data = try input.read(upToCount: Int(indexSize)), data.count == Int(indexSize) else {
            throw JuanchoError.invalidArchive
        }
        return try JSONDecoder().decode(JuanchoManifest.self, from: data)
    }

    private struct SourceFile { let url: URL; let relativePath: String; let size: UInt64 }

    private static func collectFiles(in root: URL) throws -> [SourceFile] {
        let fm = FileManager.default
        guard let enumerator = fm.enumerator(at: root,
                                             includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .isSymbolicLinkKey],
                                             options: [.skipsHiddenFiles]) else { return [] }
        var result: [SourceFile] = []
        for case let url as URL in enumerator {
            let values = try url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .isSymbolicLinkKey])
            if values.isSymbolicLink == true { enumerator.skipDescendants(); continue }
            guard values.isRegularFile == true else { continue }
            let relative = url.path.replacingOccurrences(of: root.path + "/", with: "")
            guard isSafeRelativePath(relative) else { throw JuanchoError.unsafePath(relative) }
            result.append(SourceFile(url: url, relativePath: relative, size: UInt64(values.fileSize ?? 0)))
        }
        return result.sorted { $0.relativePath < $1.relativePath }
    }

    private static func isSafeRelativePath(_ path: String) -> Bool {
        guard !path.isEmpty, !path.hasPrefix("/"), !path.contains("\\") else { return false }
        return !path.split(separator: "/").contains { $0 == ".." || $0.isEmpty }
    }

    private static func safeDestinationURL(root: URL, relativePath: String) throws -> URL {
        guard isSafeRelativePath(relativePath) else { throw JuanchoError.unsafePath(relativePath) }
        let candidate = root.appendingPathComponent(relativePath)
        let rootPath = root.standardizedFileURL.path.hasSuffix("/") ? root.standardizedFileURL.path : root.standardizedFileURL.path + "/"
        guard candidate.standardizedFileURL.path.hasPrefix(rootPath) else { throw JuanchoError.unsafePath(relativePath) }
        return candidate
    }

    private static func compressChunk(_ data: Data) throws -> (data: Data, compressed: Bool) {
        let capacity = max(data.count + 128, data.count * 2)
        var destination = Data(count: capacity)
        let count = destination.withUnsafeMutableBytes { dst in
            data.withUnsafeBytes { src in
                compression_encode_buffer(dst.bindMemory(to: UInt8.self).baseAddress!,
                                           capacity,
                                           src.bindMemory(to: UInt8.self).baseAddress!,
                                           data.count,
                                           nil,
                                           COMPRESSION_ZLIB)
            }
        }
        if count == 0 { throw JuanchoError.compressionFailed }
        if count >= data.count { return (data, false) }
        destination.count = count
        return (destination, true)
    }

    private static func decompressChunk(_ data: Data, expectedSize: Int) throws -> Data {
        var destination = Data(count: expectedSize)
        let count = destination.withUnsafeMutableBytes { dst in
            data.withUnsafeBytes { src in
                compression_decode_buffer(dst.bindMemory(to: UInt8.self).baseAddress!,
                                           expectedSize,
                                           src.bindMemory(to: UInt8.self).baseAddress!,
                                           data.count,
                                           nil,
                                           COMPRESSION_ZLIB)
            }
        }
        if count != expectedSize { throw JuanchoError.compressionFailed }
        return destination
    }

    private static func makeHeader(entryCount: UInt64, indexOffset: UInt64, indexSize: UInt64) -> Data {
        var data = Data()
        data.append(magic)
        data.appendUInt16LE(version)
        data.appendUInt16LE(0)
        data.appendUInt64LE(entryCount)
        data.appendUInt64LE(indexOffset)
        data.appendUInt64LE(indexSize)
        data.append(Data(repeating: 0, count: 64 - data.count))
        return data
    }
}

private extension Data {
    mutating func appendUInt16LE(_ value: UInt16) { var v = value.littleEndian; append(Data(bytes: &v, count: 2)) }
    mutating func appendUInt64LE(_ value: UInt64) { var v = value.littleEndian; append(Data(bytes: &v, count: 8)) }
    func readUInt16LE(at offset: Int) -> UInt16 { UInt16(littleEndian: withUnsafeBytes { $0.loadUnaligned(fromByteOffset: offset, as: UInt16.self) }) }
    func readUInt64LE(at offset: Int) -> UInt64 { UInt64(littleEndian: withUnsafeBytes { $0.loadUnaligned(fromByteOffset: offset, as: UInt64.self) }) }
}
