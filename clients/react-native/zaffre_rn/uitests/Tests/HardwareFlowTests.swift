// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import XCTest

/// Drives the React Native app against the Verdigris DK over the real
/// radio: pair both tiers, run the High and Low commands, soak three loop
/// cycles, and unpair. It launches the installed app by bundle identifier,
/// so the app must be on the phone before the test runs, and it needs the
/// board advertising, the Bluetooth permission already granted, and a loop
/// on the host running `lib/device confirm` for the pairing steps (see
/// `confirmOnBoard`). The React Native `testID` props are the accessibility
/// identifiers.
final class HardwareFlowTests: XCTestCase {
    static let bundleIdentifier = "com.stonygrey.zaffre.react"

    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    func testPairCommandsLoopAndUnpair() throws {
        let app = XCUIApplication(bundleIdentifier: Self.bundleIdentifier)
        app.launch()

        // Scan: the board is the only Verdigris device in range.
        let row = element(app, "device-row")
        XCTAssertTrue(row.waitForExistence(timeout: 60), "no Verdigris device found")
        press(app, row)

        // Pair High, then Low, confirming each over USB. A tier the app still
        // holds from earlier use is unpaired first.
        openTab(app, "noise")
        for tier in ["high", "low"] {
            let unpair = element(app, "unpair-\(tier)")
            if unpair.waitForExistence(timeout: 3) {
                waitUntilEnabled(unpair)
                press(app, unpair)
                XCTAssertTrue(waitFor(element(app, "status-\(tier)"), toContain: "Not paired", timeout: 45), "\(tier) did not unpair first")
            }
            let pair = element(app, "pair-\(tier)")
            XCTAssertTrue(pair.waitForExistence(timeout: 10))
            waitUntilEnabled(pair)
            press(app, pair)
            confirmOnBoard()
            let status = element(app, "status-\(tier)")
            XCTAssertTrue(waitFor(status, toContain: "Sessions are encrypted", timeout: 45), "\(tier) did not pair: \(status.label)")
        }

        // High: serial read, LED read and toggle, echo.
        openTab(app, "high")
        tapAndExpectToast(app, "serial-read-high", containing: "Advertising as", timeout: 45)
        tapAndExpectToast(app, "serial-write-high", containing: "Now advertising as", timeout: 45)
        let led = element(app, "led-high")
        waitUntilEnabled(led)
        press(app, led)
        let ledLabel = element(app, "led-label-high")
        XCTAssertTrue(waitFor(ledLabel, toContain: "Tap to switch", timeout: 45), "LED state not read")
        let before = ledLabel.label
        waitUntilEnabled(led)
        press(app, led)
        XCTAssertTrue(waitForChange(ledLabel, from: before, timeout: 45), "LED did not toggle")
        tapAndExpectToast(app, "echo-write-high", containing: "Hello, Verdigris!", timeout: 45)

        // Low: reads work, echo is refused by the device.
        openTab(app, "low")
        tapAndExpectToast(app, "serial-read-low", containing: "Advertising as", timeout: 45)
        tapAndExpectToast(app, "echo-write-low", containing: "PERMISSION_DENIED", timeout: 45)

        // Loop: three cycles with no failures.
        openTab(app, "loop")
        let start = element(app, "loop-start")
        waitUntilEnabled(start)
        press(app, start)
        let tests = element(app, "loop-tests")
        XCTAssertTrue(waitFor(tests, atLeast: 3, timeout: 180), "loop ran \(tests.label) cycles")
        press(app, element(app, "loop-stop"))
        XCTAssertTrue(waitUntil(timeout: 60) { !element(app, "loop-stop").isEnabled })
        XCTAssertEqual(element(app, "loop-connection-failures").label, "0")
        XCTAssertEqual(element(app, "loop-command-failures").label, "0")

        // Unpair both tiers.
        openTab(app, "noise")
        for tier in ["high", "low"] {
            let unpair = element(app, "unpair-\(tier)")
            XCTAssertTrue(unpair.waitForExistence(timeout: 10))
            waitUntilEnabled(unpair)
            press(app, unpair)
            let status = element(app, "status-\(tier)")
            XCTAssertTrue(waitFor(status, toContain: "Not paired", timeout: 45), "\(tier) did not unpair: \(status.label)")
        }
    }

    // MARK: Helpers

    private func element(_ app: XCUIApplication, _ identifier: String) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    /// The tab bar buttons carry `tabBarButtonTestID` values `tab-<name>`.
    private func openTab(_ app: XCUIApplication, _ name: String) {
        let button = element(app, "tab-\(name)")
        XCTAssertTrue(button.waitForExistence(timeout: 10), "tab \(name) not found")
        press(app, button)
    }

    /// A target below the fold of a scroll view is swiped into view first,
    /// then the tap waits for its frame to settle.
    private func press(_ app: XCUIApplication, _ element: XCUIElement) {
        for _ in 0..<6 where element.exists && !element.isHittable {
            app.swipeUp()
            RunLoop.current.run(until: Date().addingTimeInterval(1.0))
        }
        var previous = element.exists ? element.frame : .zero
        for _ in 0..<10 {
            RunLoop.current.run(until: Date().addingTimeInterval(0.3))
            let current = element.exists ? element.frame : .zero
            if current == previous { break }
            previous = current
        }
        element.tap()
    }

    private func tapAndExpectToast(_ app: XCUIApplication, _ identifier: String, containing text: String, timeout: TimeInterval) {
        let button = element(app, identifier)
        XCTAssertTrue(button.waitForExistence(timeout: 10), "\(identifier) not found")
        waitUntilEnabled(button)
        press(app, button)
        let toast = element(app, "toast")
        XCTAssertTrue(waitFor(toast, toContain: text, timeout: timeout), "after \(identifier): toast was '\(toast.exists ? toast.label : "")'")
    }

    private func waitUntilEnabled(_ element: XCUIElement, timeout: TimeInterval = 60) {
        XCTAssertTrue(waitUntil(timeout: timeout) { element.exists && element.isEnabled }, "\(element) stayed disabled")
    }

    private func waitFor(_ element: XCUIElement, toContain text: String, timeout: TimeInterval) -> Bool {
        waitUntil(timeout: timeout) { element.exists && element.label.contains(text) }
    }

    private func waitFor(_ element: XCUIElement, atLeast count: Int, timeout: TimeInterval) -> Bool {
        waitUntil(timeout: timeout) { element.exists && (Int(element.label) ?? 0) >= count }
    }

    private func waitForChange(_ element: XCUIElement, from: String, timeout: TimeInterval) -> Bool {
        waitUntil(timeout: timeout) { element.exists && element.label != from && element.label.contains("Tap to switch") }
    }

    private func waitUntil(timeout: TimeInterval, _ condition: () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if condition() {
                return true
            }
            RunLoop.current.run(until: Date().addingTimeInterval(0.5))
        }
        return condition()
    }

    /// The matching button press is simulated over USB by whoever runs the
    /// test: `lib/device confirm` must run while the pairing window is armed.
    /// The test runner on the phone cannot start a process on the host, so
    /// the host loops the confirm (see `documents/native-clients.md`).
    private func confirmOnBoard() {
        RunLoop.current.run(until: Date().addingTimeInterval(1))
    }
}
