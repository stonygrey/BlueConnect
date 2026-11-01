// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import AVFoundation
import SwiftUI

/// A camera view that reports the first QR code it sees. AVFoundation's
/// metadata output does the decoding on iOS and on the Mac's camera under
/// Mac Catalyst, so no library is needed.
struct QrScannerSheet: View {
    let onScan: (String) -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            QrCameraView(onScan: onScan)
                .ignoresSafeArea()
                .navigationTitle("Scan QR")
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Cancel") { dismiss() }
                    }
                }
        }
    }
}

struct QrCameraView: UIViewControllerRepresentable {
    let onScan: (String) -> Void

    func makeUIViewController(context: Context) -> QrCameraViewController {
        let controller = QrCameraViewController()
        controller.onScan = onScan
        return controller
    }

    func updateUIViewController(_ uiViewController: QrCameraViewController, context: Context) {}
}

final class QrCameraViewController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var onScan: ((String) -> Void)?
    private let session = AVCaptureSession()
    private let sessionQueue = DispatchQueue(label: "com.stonygrey.zaffre.swift.camera")
    private var preview: AVCaptureVideoPreviewLayer?
    private var delivered = false
    private let status = UILabel()

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        status.textColor = .white
        status.textAlignment = .center
        status.numberOfLines = 0
        status.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(status)
        NSLayoutConstraint.activate([
            status.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            status.centerYAnchor.constraint(equalTo: view.centerYAnchor),
            status.leadingAnchor.constraint(greaterThanOrEqualTo: view.leadingAnchor, constant: 16),
        ])
        AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
            DispatchQueue.main.async {
                guard let self else { return }
                if granted {
                    self.configure()
                } else {
                    self.status.text = "Camera access is needed to scan a QR code."
                }
            }
        }
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        preview?.frame = view.bounds
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        sessionQueue.async { [session] in
            if session.isRunning { session.stopRunning() }
        }
    }

    private func configure() {
        guard let device = AVCaptureDevice.default(for: .video), let input = try? AVCaptureDeviceInput(device: device) else {
            status.text = "No camera is available."
            return
        }
        let output = AVCaptureMetadataOutput()
        session.beginConfiguration()
        guard session.canAddInput(input), session.canAddOutput(output) else {
            session.commitConfiguration()
            status.text = "The camera cannot be used for scanning."
            return
        }
        session.addInput(input)
        session.addOutput(output)
        output.setMetadataObjectsDelegate(self, queue: .main)
        output.metadataObjectTypes = [.qr]
        session.commitConfiguration()
        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        layer.frame = view.bounds
        view.layer.insertSublayer(layer, at: 0)
        preview = layer
        status.text = nil
        sessionQueue.async { [session] in session.startRunning() }
    }

    /// The delegate protocol is not actor-bound, so the string is taken out
    /// here and handed to the main actor, where the controller's state is kept.
    nonisolated func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard let code = metadataObjects.compactMap({ $0 as? AVMetadataMachineReadableCodeObject }).first(where: { $0.type == .qr }),
            let text = code.stringValue
        else { return }
        Task { @MainActor in self.deliver(text) }
    }

    private func deliver(_ text: String) {
        guard !delivered else { return }
        delivered = true
        onScan?(text)
    }
}
