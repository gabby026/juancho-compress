import Foundation
import Combine

final class JuanchoCancellation {
    private let lock = NSLock()
    private var value = false
    func cancel() { lock.lock(); value = true; lock.unlock() }
    func isCancelled() -> Bool { lock.lock(); defer { lock.unlock() }; return value }
}

@MainActor
final class JuanchoEngine: ObservableObject {
    @Published var progress = 0.0
    @Published var status = "Ready"
    @Published var isWorking = false
    @Published var lastOutput: URL?
    @Published var errorMessage: String?

    private var cancellation = JuanchoCancellation()

    func cancel() { cancellation.cancel() }

    func compress(sourcePath: String, outputPath: String?) {
        guard !isWorking else { return }
        let source = URL(fileURLWithPath: sourcePath.trimmingCharacters(in: .whitespacesAndNewlines))
        let output = outputPath.map { URL(fileURLWithPath: $0) } ?? source.deletingLastPathComponent().appendingPathComponent(source.lastPathComponent + ".juancho")
        startWork()
        let token = cancellation
        Task.detached(priority: .userInitiated) { [weak self] in
            do {
                try JuanchoArchive.compressFolder(at: source, to: output, progress: { value, message in
                    Task { @MainActor in self?.updateProgress(value, message) }
                }, isCancelled: { token.isCancelled() })
                await MainActor.run {
                    self?.lastOutput = output
                    self?.status = "Completed"
                    self?.isWorking = false
                }
            } catch {
                await MainActor.run {
                    self?.errorMessage = error.localizedDescription
                    self?.status = "Failed"
                    self?.isWorking = false
                }
            }
        }
    }

    func extract(archivePath: String, outputPath: String?) {
        guard !isWorking else { return }
        let archive = URL(fileURLWithPath: archivePath.trimmingCharacters(in: .whitespacesAndNewlines))
        startWork()
        let token = cancellation
        Task.detached(priority: .userInitiated) { [weak self] in
            do {
                let manifest = try JuanchoArchive.readManifest(from: archive)
                let output = outputPath.map { URL(fileURLWithPath: $0) } ?? archive.deletingPathExtension().appendingPathComponent(manifest.rootName)
                try JuanchoArchive.extract(archiveURL: archive, to: output, progress: { value, message in
                    Task { @MainActor in self?.updateProgress(value, message) }
                }, isCancelled: { token.isCancelled() })
                await MainActor.run {
                    self?.lastOutput = output
                    self?.status = "Completed"
                    self?.isWorking = false
                }
            } catch {
                await MainActor.run {
                    self?.errorMessage = error.localizedDescription
                    self?.status = "Failed"
                    self?.isWorking = false
                }
            }
        }
    }

    private func startWork() {
        cancellation = JuanchoCancellation()
        progress = 0
        errorMessage = nil
        lastOutput = nil
        status = "Starting…"
        isWorking = true
    }

    private func updateProgress(_ value: Double, _ message: String) {
        progress = min(max(value, 0), 1)
        status = message
    }
}
