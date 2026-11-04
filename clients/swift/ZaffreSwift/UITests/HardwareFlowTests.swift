// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import XCTest

/// Drives the app against the Verdigris DK over the real radio: pair both
/// tiers, run the High and Low commands, soak three loop cycles, and unpair.
/// It needs the board advertising, the Bluetooth permission already granted
/// to the app, and a watcher that runs `lib/device confirm` over the board's
/// USB shell when the app logs that it has sent `Pair` (see
/// `confirmOnBoard`).
final class HardwareFlowTests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    func testPairCommandsLoopAndUnpair() throws {
        let app = XCUIApplication()
        app.launch()

        // Scan: the board is the only Verdigris device in range.
        let row = app.descendants(matching: .any).matching(identifier: "device-row").firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 60), "no Verdigris device found")
        press(row)

        // Pair High, then Low, confirming each over USB. A tier the app still
        // holds from earlier use is unpaired first.
        openTab(app, "Noise")
        for tier in ["high", "low"] {
            let unpair = app.descendants(matching: .any).matching(identifier: "unpair-\(tier)").firstMatch
            if unpair.exists {
                waitUntilEnabled(unpair)
                press(unpair)
                let status = app.descendants(matching: .any).matching(identifier: "status-\(tier)").firstMatch
                XCTAssertTrue(waitFor(status, toContain: "Not paired", timeout: 45), "\(tier) did not unpair first")
            }
            let pair = app.descendants(matching: .any).matching(identifier: "pair-\(tier)").firstMatch
            XCTAssertTrue(pair.waitForExistence(timeout: 10))
            waitUntilEnabled(pair)
            press(pair)
            confirmOnBoard()
            let status = app.descendants(matching: .any).matching(identifier: "status-\(tier)").firstMatch
            XCTAssertTrue(
                waitFor(status, toContain: "Sessions are encrypted", timeout: 45),
                "\(tier) did not pair: \(status.label)")
        }

        // High: serial read, LED read and toggle, echo.
        openTab(app, "High")
        tapAndExpectToast(app, "serial-read-high", containing: "Advertising as", timeout: 45)
        tapAndExpectToast(app, "serial-write-high", containing: "Now advertising as", timeout: 45)
        let led = app.descendants(matching: .any).matching(identifier: "led-high").firstMatch
        waitUntilEnabled(led)
        press(led)
        let ledLabel = app.descendants(matching: .any).matching(identifier: "led-label-high").firstMatch
        XCTAssertTrue(waitFor(ledLabel, toContain: "Tap to switch", timeout: 45), "LED state not read")
        let before = ledLabel.label
        waitUntilEnabled(led)
        press(led)
        XCTAssertTrue(waitForChange(ledLabel, from: before, timeout: 45), "LED did not toggle")
        tapAndExpectToast(app, "echo-write-high", containing: "Hello, Verdigris!", timeout: 45)

        // Low: reads work, echo is refused by the device.
        openTab(app, "Low")
        tapAndExpectToast(app, "serial-read-low", containing: "Advertising as", timeout: 45)
        tapAndExpectToast(app, "echo-write-low", containing: "permissionDenied", timeout: 45)

        // Loop: three cycles with no failures.
        openTab(app, "Loop")
        let start = app.descendants(matching: .any).matching(identifier: "loop-start").firstMatch
        waitUntilEnabled(start)
        press(start)
        let tests = app.descendants(matching: .any).matching(identifier: "loop-tests").firstMatch
        XCTAssertTrue(waitFor(tests, atLeast: 3, timeout: 180), "loop ran \(tests.label) cycles")
        press(app.descendants(matching: .any).matching(identifier: "loop-stop").firstMatch)
        XCTAssertTrue(waitUntil(timeout: 60) { !app.descendants(matching: .any).matching(identifier: "loop-stop").firstMatch.isEnabled })
        XCTAssertEqual(app.descendants(matching: .any).matching(identifier: "loop-connection-failures").firstMatch.label, "0")
        XCTAssertEqual(app.descendants(matching: .any).matching(identifier: "loop-command-failures").firstMatch.label, "0")

        // Unpair both tiers.
        openTab(app, "Noise")
        for tier in ["high", "low"] {
            let unpair = app.descendants(matching: .any).matching(identifier: "unpair-\(tier)").firstMatch
            XCTAssertTrue(unpair.waitForExistence(timeout: 10))
            waitUntilEnabled(unpair)
            press(unpair)
            let status = app.descendants(matching: .any).matching(identifier: "status-\(tier)").firstMatch
            XCTAssertTrue(waitFor(status, toContain: "Not paired", timeout: 45), "\(tier) did not unpair: \(status.label)")
        }
    }

    // MARK: Helpers

    /// Mac Catalyst puts the tabs in the window toolbar as radio buttons; iOS
    /// has a tab bar with buttons.
    private func openTab(_ app: XCUIApplication, _ name: String) {
        let radio = app.radioButtons[name].firstMatch
        if radio.waitForExistence(timeout: 5) {
            press(radio)
            return
        }
        let button = app.tabBars.buttons[name].firstMatch
        XCTAssertTrue(button.waitForExistence(timeout: 5), "tab \(name) not found")
        press(button)
    }

    /// A Mac Catalyst app driven from a macOS test runner needs mouse clicks;
    /// `tap()` is delivered but SwiftUI buttons there do not act on it. A
    /// target below the fold of the list is scrolled into view first.
    private func press(_ element: XCUIElement) {
        reveal(element)
        #if targetEnvironment(macCatalyst)
            element.click()
        #else
            element.tap()
        #endif
    }

    private func reveal(_ element: XCUIElement) {
        let app = XCUIApplication()
        for _ in 0..<6 where element.exists && !element.isHittable {
            #if targetEnvironment(macCatalyst)
                app.collectionViews.firstMatch.scroll(byDeltaX: 0, deltaY: -250)
            #else
                app.swipeUp()
            #endif
            RunLoop.current.run(until: Date().addingTimeInterval(1.0))
        }
        // A click is delivered to stale coordinates while the list is still moving:
        // wait until the element's frame has stopped changing.
        var previous = element.exists ? element.frame : .zero
        for _ in 0..<10 {
            RunLoop.current.run(until: Date().addingTimeInterval(0.3))
            let current = element.exists ? element.frame : .zero
            if current == previous { break }
            previous = current
        }
    }

    private func tapAndExpectToast(_ app: XCUIApplication, _ identifier: String, containing text: String, timeout: TimeInterval) {
        let button = app.descendants(matching: .any).matching(identifier: identifier).firstMatch
        XCTAssertTrue(button.waitForExistence(timeout: 10), "\(identifier) not found")
        waitUntilEnabled(button)
        press(button)
        let toast = app.descendants(matching: .any).matching(identifier: "toast").firstMatch
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
    /// A Mac Catalyst test runner cannot start a process, so the app logs
    /// `ZAFFRE-PAIR: armed <tier>` when it sends `Pair`, and a watcher on the
    /// unified log runs the confirm on that line (see
    /// `documents/clients.md`). On an iPhone, press the button.
    private func confirmOnBoard() {
        RunLoop.current.run(until: Date().addingTimeInterval(1))
    }
}

