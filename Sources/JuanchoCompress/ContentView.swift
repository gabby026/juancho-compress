import SwiftUI
import UniformTypeIdentifiers
import UIKit

struct ContentView: View {
    @StateObject private var engine = JuanchoEngine()
    @State private var mode: Mode = .compress
    @State private var sourcePath = ""
    @State private var outputPath = ""
    @State private var showingPicker = false
    @State private var pickerKind: PickerKind = .source
    @State private var manifestText = ""
    @State private var showingManifest = false

    enum Mode: String, CaseIterable { case compress = "Compress", extract = "Extract" }
    enum PickerKind { case source, output }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("JUANCHO archive").font(.title2.bold())
                        Text("Custom container for complete folders. Unity3D and other files are stored as raw binary data inside the archive.")
                            .foregroundStyle(.secondary)
                    }
                    Picker("Mode", selection: $mode) {
                        ForEach(Mode.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                    }.pickerStyle(.segmented)

                    VStack(alignment: .leading, spacing: 12) {
                        Text(mode == .compress ? "Source folder" : "JUANCHO file").font(.headline)
                        HStack {
                            TextField(mode == .compress ? "/var/mobile/.../Folder" : "/var/mobile/.../file.juancho", text: $sourcePath)
                                .textFieldStyle(.roundedBorder)
                                .autocorrectionDisabled()
                                .textInputAutocapitalization(.never)
                            Button("Browse") { pickerKind = .source; showingPicker = true }
                        }
                        Text(mode == .compress ? "Output .juancho (optional)" : "Extract to folder (optional)").font(.headline)
                        HStack {
                            TextField("Leave blank for automatic location", text: $outputPath)
                                .textFieldStyle(.roundedBorder)
                                .autocorrectionDisabled()
                                .textInputAutocapitalization(.never)
                            Button("Browse") { pickerKind = .output; showingPicker = true }
                        }
                        Text(mode == .compress
                             ? "Default: the .juancho file is created beside the source folder."
                             : "Default: extract beside the archive using the stored root folder name.")
                            .font(.caption).foregroundStyle(.secondary)
                    }

                    GroupBox {
                        ProgressView(value: engine.progress)
                        HStack {
                            Text(engine.status).font(.footnote)
                            Spacer()
                            Text("\(Int(engine.progress * 100))%").font(.footnote.monospacedDigit())
                        }
                    }

                    VStack(spacing: 10) {
                        Button {
                            if mode == .compress {
                                engine.compress(sourcePath: sourcePath, outputPath: outputPath.isEmpty ? nil : outputPath)
                            } else {
                                engine.extract(archivePath: sourcePath, outputPath: outputPath.isEmpty ? nil : outputPath)
                            }
                        } label: {
                            Label(mode == .compress ? "Compress Folder" : "Extract JUANCHO",
                                  systemImage: mode == .compress ? "archivebox" : "arrow.down.doc")
                                .frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(engine.isWorking || sourcePath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)

                        if engine.isWorking {
                            Button("Cancel") { engine.cancel() }.buttonStyle(.bordered)
                        }

                        if mode == .extract && !sourcePath.isEmpty {
                            Button("Read Manifest") {
                                do {
                                    let manifest = try JuanchoArchive.readManifest(from: URL(fileURLWithPath: sourcePath))
                                    let encoder = JSONEncoder()
                                    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
                                    manifestText = String(data: try encoder.encode(manifest), encoding: .utf8) ?? ""
                                    showingManifest = true
                                } catch { engine.errorMessage = error.localizedDescription }
                            }.buttonStyle(.bordered).disabled(engine.isWorking)
                        }
                    }

                    if let output = engine.lastOutput {
                        GroupBox("Last output") {
                            Text(output.path).font(.footnote).textSelection(.enabled)
                        }
                    }
                    if let error = engine.errorMessage {
                        Text(error).foregroundStyle(.red).font(.footnote)
                    }
                }.padding()
            }
            .navigationTitle("Juancho Compress")
            .sheet(isPresented: $showingPicker) {
                DocumentPicker { url in
                    if pickerKind == .source { sourcePath = url.path } else { outputPath = url.path }
                }
            }
            .sheet(isPresented: $showingManifest) {
                NavigationStack {
                    ScrollView { Text(manifestText).font(.system(.footnote, design: .monospaced)).padding() }
                        .navigationTitle("Manifest")
                }
            }
        }
    }
}

struct DocumentPicker: UIViewControllerRepresentable {
    let completion: (URL) -> Void

    func makeUIViewController(context: Context) -> UIDocumentPickerViewController {
        let controller = UIDocumentPickerViewController(forOpeningContentTypes: [UTType.data, UTType.folder], asCopy: false)
        controller.allowsMultipleSelection = false
        controller.delegate = context.coordinator
        return controller
    }

    func updateUIViewController(_ uiViewController: UIDocumentPickerViewController, context: Context) {}
    func makeCoordinator() -> Coordinator { Coordinator(completion: completion) }

    final class Coordinator: NSObject, UIDocumentPickerDelegate {
        let completion: (URL) -> Void
        init(completion: @escaping (URL) -> Void) { self.completion = completion }
        func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
            guard let url = urls.first else { return }
            completion(url)
        }
    }
}
