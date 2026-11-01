// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

struct Toast: Identifiable, Equatable, Sendable {
    enum Severity: Sendable {
        case info
        case warning
        case error
    }

    let id = UUID()
    let severity: Severity
    let text: String
}

/// The transient messages the app shows as snackbars: one at a time,
/// visible for 4 s, replaced by the next.
@MainActor
@Observable
final class Toasts {
    private(set) var current: Toast?
    private var dismissal: Task<Void, Never>?

    func info(_ text: String) { show(Toast(severity: .info, text: text)) }

    func warning(_ text: String) { show(Toast(severity: .warning, text: text)) }

    func error(_ text: String) { show(Toast(severity: .error, text: text)) }

    func dismiss() {
        dismissal?.cancel()
        current = nil
    }

    private func show(_ toast: Toast) {
        current = toast
        dismissal?.cancel()
        dismissal = Task { [weak self] in
            try? await Task.sleep(for: .seconds(4))
            guard let self, !Task.isCancelled, self.current?.id == toast.id else { return }
            self.current = nil
        }
    }
}
