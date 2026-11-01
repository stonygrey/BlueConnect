// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import SwiftUI

/// The bottom-centre banner that shows toasts.
struct ToastOverlay: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        if let toast = model.toasts.current {
            HStack {
                Image(systemName: icon(toast.severity))
                Text(toast.text)
                    .accessibilityIdentifier("toast")
                Button {
                    model.toasts.dismiss()
                } label: {
                    Image(systemName: "xmark")
                }
                .buttonStyle(.plain)
            }
            .padding()
            .background(color(toast.severity), in: RoundedRectangle(cornerRadius: 10))
            .foregroundStyle(.white)
            .padding(.bottom, 60)
            .transition(.move(edge: .bottom).combined(with: .opacity))
            .animation(.default, value: toast.id)
        }
    }

    private func icon(_ severity: Toast.Severity) -> String {
        switch severity {
        case .info: "info.circle"
        case .warning: "exclamationmark.triangle"
        case .error: "xmark.octagon"
        }
    }

    private func color(_ severity: Toast.Severity) -> Color {
        switch severity {
        case .info: .blue
        case .warning: .orange
        case .error: .red
        }
    }
}
