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

print("macOS pet state compatibility tests passed")
