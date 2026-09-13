import Foundation

private func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("FAIL: \(message)\n".utf8))
    exit(1)
}

private func loadBounds(_ path: String) -> [String: Any] {
    guard let data = FileManager.default.contents(atPath: path),
          let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
          let bounds = root["electron-avatar-overlay-bounds"] as? [String: Any] else {
        fail("could not load fixture \(path)")
    }
    return bounds
}

guard CommandLine.arguments.count == 3 else {
    fail("expected legacy and current fixture paths")
}

let legacy = loadBounds(CommandLine.arguments[1])
guard case .legacy(let windowWidth, let windowHeight, let legacyAnchor)? = persistedPetGeometry(from: legacy) else {
    fail("legacy bounds were not classified as legacy")
}
guard windowWidth == 356, windowHeight == 320,
      legacyAnchor == PersistedPetAnchor(x: 211, y: 185, width: 117, height: 127) else {
    fail("legacy geometry changed")
}

let current = loadBounds(CommandLine.arguments[2])
guard case .direct(let currentAnchor)? = persistedPetGeometry(from: current) else {
    fail("current bounds were not classified as direct")
}
guard currentAnchor == PersistedPetAnchor(x: 1693, y: 480, width: 112, height: 121) else {
    fail("current geometry did not use the integrated app default mascot size")
}

let fresh: [String: Any] = ["x": 100 as NSNumber, "y": 200 as NSNumber]
guard persistedPetGeometry(from: fresh) == .direct(PersistedPetAnchor(x: 100, y: 200, width: 112, height: 121)) else {
    fail("fresh current state did not use the integrated app default mascot size")
}
guard persistedPetGeometry(from: ["x": 100 as NSNumber]) == nil else {
    fail("invalid direct state was accepted")
}

let directWithWindowSize: [String: Any] = [
    "x": 100 as NSNumber,
    "y": 200 as NSNumber,
    "width": 356 as NSNumber,
    "height": 320 as NSNumber
]
guard persistedPetGeometry(from: directWithWindowSize) == .direct(PersistedPetAnchor(x: 100, y: 200, width: 112, height: 121)) else {
    fail("direct state with optional window size was rejected")
}

let stringDisplayID: [String: Any] = [
    "x": 100 as NSNumber,
    "y": 200 as NSNumber,
    "displayId": "3",
    "byDisplayId": [
        "2": ["x": 0, "y": 0, "anchor": ["x": 0, "y": 0, "width": 80, "height": 87]],
        "3": ["x": 0, "y": 0, "anchor": ["x": 0, "y": 0, "width": 120, "height": 130]]
    ]
]
guard persistedPetGeometry(from: stringDisplayID) == .direct(PersistedPetAnchor(x: 100, y: 200, width: 120, height: 130)) else {
    fail("string displayId did not select the matching remembered size")
}
guard persistedDisplayID(from: stringDisplayID) == 3 else {
    fail("string displayId was not normalized for screen selection")
}

let anchor = PersistedPetAnchor(x: 100, y: 200, width: 112, height: 121)
let rootPetWindow: [String: Any] = [
    kCGWindowOwnerName as String: "ChatGPT",
    kCGWindowName as String: "ChatGPT",
    kCGWindowLayer as String: 3 as NSNumber,
    kCGWindowAlpha as String: 1 as NSNumber,
    kCGWindowIsOnscreen as String: true as NSNumber,
    kCGWindowBounds as String: ["X": 90, "Y": 194, "Width": 384, "Height": 127]
]
guard isVisibleIntegratedPetWindow(rootPetWindow, containing: anchor) else {
    fail("integrated root pet window was rejected")
}
var auxiliarySurface = rootPetWindow
auxiliarySurface[kCGWindowName as String] = "Pet Surface activity-slot-0"
auxiliarySurface[kCGWindowBounds as String] = ["X": 90, "Y": 194, "Width": 345, "Height": 54]
guard !isVisibleIntegratedPetWindow(auxiliarySurface, containing: anchor) else {
    fail("auxiliary ChatGPT surface was accepted as the root pet window")
}

if ProcessInfo.processInfo.environment["CODEX_PET_TEST_LIVE"] == "1" {
    guard let windowList = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements],
        kCGNullWindowID
    ) as? [[String: Any]],
          windowList.contains(where: { isVisibleIntegratedPetWindow($0, containing: currentAnchor) }) else {
        fail("live integrated pet window did not match the current fixture")
    }
}

private let application = NSApplication.shared
private let previousSettingsData = Data(#"{"scale":1.25,"horizontalOffset":-41,"verticalOffset":24,"potionGap":40,"usageAlertsEnabled":false,"nativeNotificationsEnabled":false,"alertThresholds":[20,5],"autoCleanup":false,"lastCleanupAt":123456,"lastFreedBytes":9876}"#.utf8)
private let previousSettings: OverlaySettings = {
    guard let settings = try? JSONDecoder().decode(OverlaySettings.self, from: previousSettingsData),
          settings.potionStyle == "classic" else {
        fail("existing settings did not migrate to the classic potion")
    }
    return settings
}()

for style in PotionStyle.allCases {
    var changed = previousSettings
    changed.potionStyle = style.rawValue
    guard let encoded = try? JSONEncoder().encode(changed.normalized()),
          let roundTrip = try? JSONDecoder().decode(OverlaySettings.self, from: encoded),
          roundTrip == changed else {
        fail("potion style round trip changed existing settings: \(style.rawValue)")
    }
    var restored = roundTrip
    restored.potionStyle = "classic"
    guard restored == previousSettings else { fail("potion selection changed unrelated settings") }

    let menu = makePotionStyleMenu(selectedID: style.rawValue, target: nil, action: NSSelectorFromString("selectPotionStyle:"))
    guard menu.items.count == 6,
          menu.items.compactMap({ $0.representedObject as? String }) == PotionStyle.allCases.map(\.rawValue),
          menu.items.map(\.title) == PotionStyle.allCases.map(\.name),
          menu.items.filter({ $0.state == .on }).count == 1,
          menu.items.first(where: { $0.state == .on })?.representedObject as? String == style.rawValue else {
        fail("menu selection was not reflected for \(style.rawValue)")
    }
}

do {
    guard PotionStyle.normalized("  ROSE-HEART\n") == .roseHeart,
          PotionStyle.normalized("future-potion") == .classic,
          PotionStyle.normalized(nil) == .classic,
          let invalidSettings = try? JSONDecoder().decode(OverlaySettings.self, from: Data(#"{"potionStyle":"future-potion"}"#.utf8)),
          invalidSettings.potionStyle == "classic" else {
        fail("potion style normalization or unknown ID fallback failed")
    }
}
guard potionRemainingText(nil) == "—", potionRemainingText(0) == "0%",
      potionRemainingText(50) == "50%", potionRemainingText(100) == "100%" else {
    fail("unknown, empty, half-full, and full values are not distinct")
}

for scale in [0.5, 1.0, 1.8] {
    var settings = OverlaySettings.defaults
    settings.scale = scale
    let diameter = potionOrbDiameter(for: NSSize(width: 112, height: 121), settings: settings)
    let bounds = NSRect(x: 0, y: 0, width: diameter, height: diameter + 14)
    let layout = PotionArtworkLayout(in: bounds)
    guard bounds.contains(layout.artwork), bounds.contains(layout.value), bounds.contains(layout.label),
          layout.artwork.maxY < layout.value.minY, layout.value.maxY <= layout.label.minY else {
        fail("potion artwork overlaps text at scale \(scale)")
    }
    let font = NSFont.monospacedDigitSystemFont(ofSize: min(12, max(10, diameter * 0.16)), weight: .heavy)
    let textSize = "100%".size(withAttributes: [.font: font])
    guard textSize.width <= layout.value.width, textSize.height <= layout.value.height else {
        fail("percentage text clips at scale \(scale)")
    }
}

private let potionAssetDirectory = URL(fileURLWithPath: CommandLine.arguments[1])
    .deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("settings", isDirectory: true)

private func changedLiquidPixels(_ image: CGImage, from empty: CGImage, mask: CGImage, kind: RingKind, minimumY: Int) -> Int {
    let bitmap = NSBitmapImageRep(cgImage: image)
    let baseline = NSBitmapImageRep(cgImage: empty)
    let maskBitmap = NSBitmapImageRep(cgImage: mask)
    var changed = 0
    var redDelta: CGFloat = 0
    var blueDelta: CGFloat = 0
    for y in 0..<96 {
        for x in 0..<96 {
            guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB),
                  let old = baseline.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB),
                  let maskColor = maskBitmap.colorAt(x: x, y: y) else {
                fail("could not inspect potion rendering pixels")
            }
            let delta = abs(color.redComponent - old.redComponent) + abs(color.greenComponent - old.greenComponent) + abs(color.blueComponent - old.blueComponent)
            guard delta > 0.02 else { continue }
            guard maskColor.alphaComponent > 0, y >= minimumY else {
                fail("liquid escaped its mask or filled from the wrong edge")
            }
            changed += 1
            redDelta += color.redComponent - old.redComponent
            blueDelta += color.blueComponent - old.blueComponent
        }
    }
    guard (kind == .outer ? redDelta > blueDelta : blueDelta > redDelta) else {
        fail("5H red / WK blue liquid colors were not preserved")
    }
    return changed
}

for style in PotionStyle.allCases where style != .classic {
    guard let artwork = PotionArtwork(style: style, assetDirectory: potionAssetDirectory),
          artwork.preview.size == NSSize(width: 96, height: 96),
          artwork.frame.width == 96, artwork.frame.height == 96,
          artwork.mask.width == 96, artwork.mask.height == 96 else {
        fail("missing or invalid potion preview/frame/mask: \(style.rawValue)")
    }
    for kind in [RingKind.outer, RingKind.inner] {
        guard let unknown = artwork.makeImage(remaining: nil, kind: kind),
              let empty = artwork.makeImage(remaining: 0, kind: kind),
              let half = artwork.makeImage(remaining: 50, kind: kind),
              let full = artwork.makeImage(remaining: 100, kind: kind) else {
            fail("could not render potion usage states")
        }
        guard NSBitmapImageRep(cgImage: unknown).representation(using: .png, properties: [:]) == NSBitmapImageRep(cgImage: empty).representation(using: .png, properties: [:]) else {
            fail("unknown usage must not invent liquid")
        }
        let midpoint = Int((style.chamber.top + style.chamber.bottom) / 2)
        let halfCount = changedLiquidPixels(half, from: empty, mask: artwork.mask, kind: kind, minimumY: midpoint)
        let fullCount = changedLiquidPixels(full, from: empty, mask: artwork.mask, kind: kind, minimumY: Int(style.chamber.top))
        guard halfCount > 20, fullCount > halfCount else {
            fail("usage did not change the liquid fill level: \(style.rawValue)")
        }
    }
}

private extension RingsView {
    func snapshotPNG() -> Data? {
        // 사용량 변경 효과를 멈춰 CI 화면 비교를 일정하게 유지한다.
        pulseTimer?.invalidate()
        pulseTimer = nil
        pulseAnimation = 0
        particleEmitter = ParticleEmitter()
        let scale: CGFloat = 2
        guard let context = CGContext(
            data: nil, width: Int(ceil(bounds.width * scale)), height: Int(ceil(bounds.height * scale)),
            bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }
        context.scaleBy(x: scale, y: scale)
        context.translateBy(x: 0, y: bounds.height)
        context.scaleBy(x: 1, y: -1)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        draw(bounds)
        NSGraphicsContext.restoreGraphicsState()
        guard let image = context.makeImage() else { return nil }
        return NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])
    }
}

if let outputPath = ProcessInfo.processInfo.environment["CODEX_PET_RENDER_OUTPUT"], !outputPath.isEmpty {
    let directory = URL(fileURLWithPath: outputPath, isDirectory: true)
    do {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        func writeRender(style: PotionStyle, scale: Double, remaining: Double?, name: String) throws {
            var settings = OverlaySettings.defaults
            settings.potionStyle = style.rawValue
            settings.scale = scale
            let anchorSize = NSSize(width: 112, height: 121)
            let diameter = potionOrbDiameter(for: anchorSize, settings: settings)
            let reserve = potionSideReserve(for: anchorSize, settings: settings)
            let view = RingsView(frame: NSRect(
                x: 0, y: 0, width: anchorSize.width + reserve * 2,
                height: max(anchorSize.height, diameter + 14) + POTION_FRAME_INSET * 2
            ))
            view.anchorSize = anchorSize
            view.potionAssetDirectory = potionAssetDirectory
            view.overlaySettings = settings
            view.usage = LimitUsage(
                primaryPercent: remaining.map { 100 - $0 }, secondaryPercent: remaining.map { 100 - $0 },
                primaryReset: nil, secondaryReset: nil, source: "test"
            )
            guard let png = view.snapshotPNG() else { fail("could not capture HUD rendering") }
            try png.write(to: directory.appendingPathComponent(name + ".png"))
        }
        for style in PotionStyle.allCases {
            try writeRender(style: style, scale: 1, remaining: 50, name: style.rawValue + "-50")
        }
        let remainingSamples: [Double?] = [nil, 0, 100]
        for remaining in remainingSamples {
            let name = remaining.map { String(Int($0)) } ?? "unknown"
            try writeRender(style: .celestialOrb, scale: 1, remaining: remaining, name: "celestial-orb-" + name)
        }
        for scale in [0.5, 1.8] {
            try writeRender(style: .lunarCrescent, scale: scale, remaining: 50, name: "lunar-crescent-scale-\(scale)")
        }
    } catch {
        fail("could not save HUD rendering artifacts: \(error)")
    }
}

private func waitForWebKit(_ description: String, timeout: TimeInterval = 20, until condition: () -> Bool) {
    let deadline = Date().addingTimeInterval(timeout)
    while !condition() {
        guard Date() < deadline else { fail("WebKit timeout after \(timeout)s: \(description)") }
        RunLoop.main.run(until: Date().addingTimeInterval(0.01))
    }
}

private final class SettingsWebProbe: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
    var ready = false
    var loaded = false
    var failures: [String] = []
    var saved: [OverlaySettings] = []

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any], let type = body["type"] as? String else {
            failures.append("invalid settings bridge message")
            return
        }
        if type == "ready" { ready = true }
        if type == "save" {
            guard let object = body["settings"], JSONSerialization.isValidJSONObject(object),
                  let data = try? JSONSerialization.data(withJSONObject: object),
                  let settings = try? JSONDecoder().decode(OverlaySettings.self, from: data) else {
                failures.append("invalid settings save payload")
                return
            }
            saved.append(settings)
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { loaded = true }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        failures.append(error.localizedDescription)
    }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        failures.append(error.localizedDescription)
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { failures.append("WebKit content process terminated") }
    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        let allowed = ["file", "about", "data"].contains(action.request.url?.scheme ?? "")
        if !allowed { failures.append("non-local navigation was blocked") }
        decisionHandler(allowed ? .allow : .cancel)
    }
}

private func testSettingsWebView() {
    let probe = SettingsWebProbe()
    let configuration = WKWebViewConfiguration()
    configuration.websiteDataStore = .nonPersistent()
    configuration.userContentController.add(probe, name: "settings")
    configuration.userContentController.addUserScript(WKUserScript(source: """
        window.__settingsTestErrors = [];
        window.addEventListener('error', event => {
          window.__settingsTestErrors.push(event.message || 'resource: ' + (event.target.src || event.target.href || 'unknown'));
        }, true);
        window.addEventListener('unhandledrejection', event => window.__settingsTestErrors.push(String(event.reason)));
        """, injectionTime: .atDocumentStart, forMainFrameOnly: true))
    let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 980, height: 680), configuration: configuration)
    webView.navigationDelegate = probe
    webView.autoresizingMask = [.width, .height]
    // 단독 테스트 창만 만들고 실제 앱의 설정 저장·알림·자동 시작 코드는 호출하지 않는다.
    let window = NSWindow(contentRect: webView.frame, styleMask: [.borderless], backing: .buffered, defer: false)
    window.isReleasedWhenClosed = false
    window.contentView = webView
    window.orderFront(nil)
    defer {
        webView.stopLoading()
        webView.navigationDelegate = nil
        configuration.userContentController.removeScriptMessageHandler(forName: "settings")
        window.orderOut(nil)
        window.close()
    }

    func evaluate(_ source: String) -> Any? {
        var finished = false
        var value: Any?
        var failure: Error?
        webView.evaluateJavaScript(source) { result, error in
            value = result
            failure = error
            finished = true
        }
        waitForWebKit("JavaScript evaluation") { finished || !probe.failures.isEmpty }
        if let failure { fail("WebKit JavaScript failed: \(failure.localizedDescription)") }
        guard probe.failures.isEmpty else { fail("WebKit failed: \(probe.failures.joined(separator: "; "))") }
        return value
    }

    func applyNative(_ settings: OverlaySettings) {
        guard let data = try? JSONEncoder().encode(settings), let json = String(data: data, encoding: .utf8) else {
            fail("could not encode native test settings")
        }
        _ = evaluate("window.applyNativeState({settings: \(json)}); true")
        waitForWebKit("native state presentation") { evaluate("!applyingNativeState") as? Bool == true }
    }

    func verifyVisibleCards() {
        guard evaluate("""
            (() => {
              const cards = Array.from(document.querySelectorAll('.potion-card'));
              return cards.length === 6 && cards.every(card => {
                const rect = card.getBoundingClientRect();
                return rect.width > 0 && rect.height > 0 && rect.top >= 0 && rect.left >= 0 &&
                  rect.bottom <= innerHeight && rect.right <= innerWidth;
              });
            })()
            """) as? Bool == true else {
            fail("six potion cards are not visible in the first WebKit viewport")
        }
    }

    func snapshot(_ name: String) {
        waitForWebKit("potion preview fitting inside its stage") { evaluate("""
            (() => {
              const stage = document.getElementById('previewStage').getBoundingClientRect();
              const hud = document.getElementById('hudPreview').getBoundingClientRect();
              return hud.left >= stage.left && hud.right <= stage.right &&
                hud.top >= stage.top && hud.bottom <= stage.bottom;
            })()
            """) as? Bool == true
        }
        let snapshotConfiguration = WKSnapshotConfiguration()
        snapshotConfiguration.rect = webView.bounds
        snapshotConfiguration.afterScreenUpdates = true
        var finished = false
        var captured: NSImage?
        var failure: Error?
        webView.takeSnapshot(with: snapshotConfiguration) { image, error in
            captured = image
            failure = error
            finished = true
        }
        waitForWebKit("settings snapshot", timeout: 30) { finished || !probe.failures.isEmpty }
        if let failure { fail("WebKit snapshot failed: \(failure.localizedDescription)") }
        guard probe.failures.isEmpty, let captured,
              let tiff = captured.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
              bitmap.pixelsWide >= Int(webView.bounds.width), bitmap.pixelsHigh >= Int(webView.bounds.height),
              let png = bitmap.representation(using: .png, properties: [:]) else {
            fail("WebKit returned an empty or incomplete settings snapshot")
        }
        if let outputPath = ProcessInfo.processInfo.environment["CODEX_PET_RENDER_OUTPUT"], !outputPath.isEmpty {
            let directory = URL(fileURLWithPath: outputPath, isDirectory: true)
            do {
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
                try png.write(to: directory.appendingPathComponent(name + ".png"))
            } catch { fail("could not save WebKit snapshot: \(error)") }
        }
    }

    webView.loadFileURL(potionAssetDirectory.appendingPathComponent("index.html"), allowingReadAccessTo: potionAssetDirectory)
    waitForWebKit("local settings page and ready bridge") { (probe.loaded && probe.ready) || !probe.failures.isEmpty }
    guard probe.failures.isEmpty else { fail("WebKit page load failed: \(probe.failures.joined(separator: "; "))") }
    guard evaluate("document.styleSheets.length > 0 && document.querySelectorAll('input[name=\"potionStyle\"]').length === 6") as? Bool == true else {
        fail("local settings CSS or six potion controls did not load")
    }
    applyNative(previousSettings)
    verifyVisibleCards()
    _ = evaluate("document.querySelector('input[name=\"potionStyle\"][value=\"rose-heart\"]').click(); true")
    waitForWebKit("potion selection save bridge") { !probe.saved.isEmpty || !probe.failures.isEmpty }
    var expected = previousSettings
    expected.potionStyle = "rose-heart"
    guard probe.failures.isEmpty, probe.saved.last == expected else {
        fail("WebKit potion selection did not save its ID or preserve previous settings")
    }
    let saveCount = probe.saved.count
    expected.potionStyle = "verdant-leaf"
    applyNative(expected)
    guard evaluate("document.querySelector('input[name=\"potionStyle\"]:checked').value === 'verdant-leaf' && document.getElementById('selectedPotionStyle').textContent.includes('신록 잎새')") as? Bool == true,
          probe.saved.count == saveCount else {
        fail("native potion selection did not update WebKit or caused a save feedback loop")
    }
    waitForWebKit("local potion preview images") {
        evaluate("Array.from(document.images).every(image => !image.getAttribute('src') || (image.complete && image.naturalWidth > 0))") as? Bool == true
    }
    guard evaluate("Array.from(document.querySelectorAll('.potion-mask')).every(mask => getComputedStyle(mask).getPropertyValue('-webkit-mask-image').includes('data:image/png'))") as? Bool == true else {
        fail("WebKit local potion masks did not resolve to embedded image data")
    }
    guard let errors = evaluate("window.__settingsTestErrors") as? [String], errors.isEmpty else {
        fail("settings page reported a JavaScript or resource loading error")
    }
    snapshot("wk-settings")

    window.setContentSize(NSSize(width: 820, height: 578))
    webView.setFrameSize(NSSize(width: 820, height: 578))
    webView.layoutSubtreeIfNeeded()
    waitForWebKit("minimum settings viewport") { evaluate("innerWidth === 820 && innerHeight === 578") as? Bool == true }
    verifyVisibleCards()
    snapshot("wk-settings-minimum")
}

testSettingsWebView()
print("macOS pet state compatibility, potion settings/menu, resources, fill, layout, and WebKit bridge/snapshot tests passed")
