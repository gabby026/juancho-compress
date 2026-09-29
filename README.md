# Juancho Compress

A native iOS Swift app that creates and extracts a custom `.juancho` archive format.

## What it does

- Accepts a real filesystem path such as `/var/mobile/Containers/Shared/AppGroup/.../Documents/My Folder`.
- Recursively archives every regular file and preserves relative paths.
- Treats `.unity3d`, `.png`, `.json`, `.bytes`, and other files as opaque bytes.
- Compresses data in 1 MiB chunks using Apple's Compression framework (zlib), falling back to raw chunks when compression is larger.
- Stores a manifest with root name, relative path, original size, chunk offsets/sizes, compression method, and SHA-256 checksums.
- Extracts safely without allowing `..` path traversal.
- Registers `.juancho` with iOS so it can be opened/shared into the app.
- Handles large files with chunked I/O instead of loading an entire file into memory.

## Important filesystem note

A pasted `/var/mobile/...` path is usable only when the installed app process has permission to access that location. TrollStore installation by itself does not grant every app access to every other app's App Group container. If the source is another app's App Group, the app must have appropriate access/entitlements or the folder must be handed to the app through an iOS-supported file-sharing/document flow.

The archive does **not** store the original absolute source path. It stores the source folder's name and safe relative file paths.

## Building

The repository includes `project.yml` for XcodeGen and a GitHub Actions workflow that builds an unsigned iOS `.ipa` artifact. The unsigned package is intended for environments such as TrollStore; it is not App Store signed.

## Format

The archive starts with a fixed `JUANCHO\0` header and version. Compressed file chunks are stored first, followed by a JSON manifest/index containing each file's relative path, original size, SHA-256, compression method, and chunk locations. This is a custom container format and is not a renamed ZIP file.
