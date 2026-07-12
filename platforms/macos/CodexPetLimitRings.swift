import AppKit
import Darwin
import Foundation
import SQLite3
import UserNotifications
import WebKit

// MARK: - Constants

private let LIVE_USAGE_REFRESH_INTERVAL: TimeInterval = 30
private let LIVE_USAGE_RETRY_INTERVAL: TimeInterval = 30
private let LIVE_USAGE_STALE_FALLBACK_INTERVAL: TimeInterval = 5 * 60
private let CACHED_USAGE_REFRESH_INTERVAL: TimeInterval = 5
private let RING_PULSE_DURATION: TimeInterval = 0.42
private let POTION_ORB_MIN_DIAMETER: CGFloat = 60
private let POTION_ORB_MAX_DIAMETER: CGFloat = 78
private let POTION_ORB_GAP: CGFloat = 10
private let POTION_FRAME_INSET: CGFloat = 8

private struct OverlaySettings: Codable, Equatable {
    var scale: Double
    var horizontalOffset: Double
    var verticalOffset: Double
    var potionGap: Double
    var usageAlertsEnabled: Bool
    var nativeNotificationsEnabled: Bool
    var alertThresholds: [Int]
    var autoCleanup: Bool
    var lastCleanupAt: TimeInterval?
    var lastFreedBytes: Int64

    static let defaults = OverlaySettings(
        scale: 1,
        horizontalOffset: 0,
        verticalOffset: 0,
        potionGap: Double(POTION_ORB_GAP),
        usageAlertsEnabled: true,
        nativeNotificationsEnabled: false,
        alertThresholds: [20, 10, 5],
        autoCleanup: true,
        lastCleanupAt: nil,
        lastFreedBytes: 0
    )

    private enum CodingKeys: String, CodingKey {
        case scale, horizontalOffset, verticalOffset, potionGap
        case usageAlertsEnabled, nativeNotificationsEnabled, alertThresholds
        case autoCleanup, lastCleanupAt, lastFreedBytes
    }

    init(
        scale: Double,
        horizontalOffset: Double,
        verticalOffset: Double,
        potionGap: Double,
        usageAlertsEnabled: Bool,
        nativeNotificationsEnabled: Bool,
        alertThresholds: [Int],
        autoCleanup: Bool,
        lastCleanupAt: TimeInterval?,
        lastFreedBytes: Int64
    ) {
        self.scale = scale
        self.horizontalOffset = horizontalOffset
        self.verticalOffset = verticalOffset
        self.potionGap = potionGap
        self.usageAlertsEnabled = usageAlertsEnabled
        self.nativeNotificationsEnabled = nativeNotificationsEnabled
        self.alertThresholds = alertThresholds
        self.autoCleanup = autoCleanup
        self.lastCleanupAt = lastCleanupAt
        self.lastFreedBytes = lastFreedBytes
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        scale = try container.decodeIfPresent(Double.self, forKey: .scale) ?? 1
        horizontalOffset = try container.decodeIfPresent(Double.self, forKey: .horizontalOffset) ?? 0
        verticalOffset = try container.decodeIfPresent(Double.self, forKey: .verticalOffset) ?? 0
        potionGap = try container.decodeIfPresent(Double.self, forKey: .potionGap) ?? Double(POTION_ORB_GAP)
        usageAlertsEnabled = try container.decodeIfPresent(Bool.self, forKey: .usageAlertsEnabled) ?? true
        nativeNotificationsEnabled = try container.decodeIfPresent(Bool.self, forKey: .nativeNotificationsEnabled) ?? false
        alertThresholds = try container.decodeIfPresent([Int].self, forKey: .alertThresholds) ?? [20, 10, 5]
        autoCleanup = try container.decodeIfPresent(Bool.self, forKey: .autoCleanup) ?? true
        lastCleanupAt = try container.decodeIfPresent(TimeInterval.self, forKey: .lastCleanupAt)
        lastFreedBytes = try container.decodeIfPresent(Int64.self, forKey: .lastFreedBytes) ?? 0
    }

    func normalized() -> OverlaySettings {
        var value = self
        value.scale = min(1.8, max(0.5, value.scale))
        value.horizontalOffset = min(400, max(-400, value.horizontalOffset))
        value.verticalOffset = min(300, max(-300, value.verticalOffset))
        value.potionGap = min(160, max(0, value.potionGap))
        value.alertThresholds = Array(Set(value.alertThresholds.filter { [20, 10, 5].contains($0) })).sorted(by: >)
        return value
    }
}

private struct ThresholdDeliveryState: Codable {
    var resetAt: Int64?
    var delivered: [Int]

    static let empty = ThresholdDeliveryState(resetAt: nil, delivered: [])
}

private struct UsageAlertState: Codable {
    var primary: ThresholdDeliveryState
    var secondary: ThresholdDeliveryState

    static let empty = UsageAlertState(primary: .empty, secondary: .empty)
}

private struct CleanupResult: Codable {
    let freedBytes: Int64
    let cleanedAt: TimeInterval
    let nextCleanupAt: TimeInterval?
}

private func potionOrbDiameter(for anchorSize: NSSize, settings: OverlaySettings) -> CGFloat {
    let base = min(POTION_ORB_MAX_DIAMETER, max(POTION_ORB_MIN_DIAMETER, max(anchorSize.width, anchorSize.height) * 0.62))
    return base * CGFloat(settings.scale)
}

private func potionGap(for settings: OverlaySettings) -> CGFloat {
    CGFloat(settings.potionGap * settings.scale)
}

private func potionSideReserve(for anchorSize: NSSize, settings: OverlaySettings) -> CGFloat {
    potionOrbDiameter(for: anchorSize, settings: settings) + potionGap(for: settings) + POTION_FRAME_INSET
}

private struct Anchor: Equatable {
    let x: CGFloat
    let y: CGFloat
    let width: CGFloat
    let height: CGFloat
    let displayX: CGFloat?
    let displayY: CGFloat?
    let displayWidth: CGFloat?
    let displayHeight: CGFloat?
    let displayID: Int?
}

private struct PetWindowFrame {
    let x: CGFloat
    let y: CGFloat
}

private struct StateFileStamp: Equatable {
    let modifiedAt: Date
    let size: Int
}

private struct OverlayPlacement {
    let frame: NSRect
    let renderSettings: OverlaySettings
}

private enum RingKind: Equatable {
    case outer
    case inner
}

private struct LimitUsage: Equatable {
    let primaryPercent: Double?
    let secondaryPercent: Double?
    let primaryReset: TimeInterval?
    let secondaryReset: TimeInterval?
    let source: String

    func tooltip(for ring: RingKind) -> String {
        var lines: [String] = []
        switch ring {
        case .outer:
            guard let primaryPercent else {
                return "기록을 기다리는 중\n5시간 창 데이터 없음\n초기화까지 확인 중"
            }
            lines.append("5시간 창 \(remainingText(primaryPercent))")
        case .inner:
            guard let secondaryPercent else {
                return "기록을 기다리는 중\n주간 창 데이터 없음\n초기화까지 확인 중"
            }
            lines.append("주간 창 \(remainingText(secondaryPercent))")
        }
        if source != "none" {
            lines.append(source == "live" ? "실시간 기준" : "최근 기록 기준")
        }
        lines.append("초기화까지 \(resetRemainingText(ring == .outer ? primaryReset : secondaryReset))")
        return lines.joined(separator: "\n")
    }

    var primaryRemainingPercent: Double? {
        remainingPercent(primaryPercent)
    }

    var secondaryRemainingPercent: Double? {
        remainingPercent(secondaryPercent)
    }

    private func resetRemainingText(_ timestamp: TimeInterval?) -> String {
        guard let timestamp else {
            return "확인 중"
        }
        let remainingSeconds = max(0, Int(timestamp - Date().timeIntervalSince1970))
        guard remainingSeconds > 0 else { return "곧 초기화" }

        let days = remainingSeconds / 86_400
        let hours = (remainingSeconds % 86_400) / 3_600
        let minutes = (remainingSeconds % 3_600) / 60
        if days > 0 { return "\(days)일 \(hours)시간" }
        if hours > 0 { return "\(hours)시간 \(minutes)분" }
        return "\(max(1, minutes))분"
    }

    private func remainingText(_ usedPercent: Double) -> String {
        let used = clamped(usedPercent)
        let remaining = 100 - used
        return "\(Int(remaining.rounded()))% 남음 (\(Int(used.rounded()))% 사용)"
    }

    private func remainingPercent(_ usedPercent: Double?) -> Double? {
        guard let usedPercent else {
            return nil
        }
        return 100 - clamped(usedPercent)
    }

    private func clamped(_ percent: Double) -> Double {
        max(0, min(percent, 100))
    }
}

private struct Particle {
    var x: CGFloat
    var y: CGFloat
    var vx: CGFloat
    var vy: CGFloat
    var life: CGFloat
    var maxLife: CGFloat
    var size: CGFloat
    var color: NSColor

    var alpha: CGFloat {
        return life / maxLife
    }

    mutating func update() {
        x += vx
        y += vy
        vy -= 0.02
        life -= 1
    }
}

private struct ParticleEmitter {
    var particles: [Particle] = []

    mutating func emit(at point: CGPoint, count: Int, color: NSColor) {
        for _ in 0..<count {
            let angle = CGFloat.random(in: 0..<CGFloat.pi * 2)
            let speed = CGFloat.random(in: 0.5..<2.0)
            let particle = Particle(
                x: point.x,
                y: point.y,
                vx: cos(angle) * speed,
                vy: sin(angle) * speed,
                life: CGFloat.random(in: 30...60),
                maxLife: CGFloat.random(in: 30...60),
                size: CGFloat.random(in: 2...4),
                color: color
            )
            particles.append(particle)
        }
    }

    mutating func update() {
        particles = particles.filter { $0.life > 0 }
        for i in 0..<particles.count {
            particles[i].update()
        }
    }
}

// MARK: - Auth & Payload Models

private struct AuthPayload: Decodable {
    let tokens: AuthTokens?
}

private struct AuthTokens: Decodable {
    let access_token: String?
}

private struct UsagePayload: Decodable {
    let plan_type: String?
    let rate_limit: RatePayload?
    let additional_rate_limits: AdditionalRateLimits?
}

private struct EventPayload: Decodable {
    let type: String
    let plan_type: String?
    let rate_limits: RatePayload?
    let additional_rate_limits: AdditionalRateLimits?
}

private struct RatePayload: Decodable {
    let primary: BucketPayload?
    let secondary: BucketPayload?
    let primary_window: BucketPayload?
    let secondary_window: BucketPayload?
}

private struct BucketPayload: Decodable {
    let used_percent: Double?
    let window_minutes: Double?
    let limit_window_seconds: Double?
    let reset_at: Double?
}

private struct AdditionalRateLimits: Decodable {
    let entries: [AdditionalRateLimit]

    init(from decoder: Decoder) throws {
        if var array = try? decoder.unkeyedContainer() {
            var entries: [AdditionalRateLimit] = []
            while !array.isAtEnd {
                entries.append(try array.decode(AdditionalRateLimit.self))
            }
            self.entries = entries
            return
        }

        let dictionary = try decoder.container(keyedBy: DynamicCodingKey.self)
        self.entries = try dictionary.allKeys.map { key in
            let payload = try dictionary.decode(RatePayload.self, forKey: key)
            return AdditionalRateLimit(name: key.stringValue, meteredFeature: nil, payload: payload)
        }
    }
}

private struct AdditionalRateLimit: Decodable {
    let name: String?
    let meteredFeature: String?
    let payload: RatePayload

    private enum CodingKeys: String, CodingKey {
        case id
        case name
        case model
        case model_slug
        case limit_name
        case metered_feature
        case rate_limit
    }

    init(name: String?, meteredFeature: String?, payload: RatePayload) {
        self.name = name
        self.meteredFeature = meteredFeature
        self.payload = payload
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        var decodedName = try container.decodeIfPresent(String.self, forKey: .limit_name)
        if decodedName == nil {
            decodedName = try container.decodeIfPresent(String.self, forKey: .model_slug)
        }
        if decodedName == nil {
            decodedName = try container.decodeIfPresent(String.self, forKey: .model)
        }
        if decodedName == nil {
            decodedName = try container.decodeIfPresent(String.self, forKey: .name)
        }
        if decodedName == nil {
            decodedName = try container.decodeIfPresent(String.self, forKey: .id)
        }
        self.name = decodedName
        self.meteredFeature = try container.decodeIfPresent(String.self, forKey: .metered_feature)
        if let nested = try container.decodeIfPresent(RatePayload.self, forKey: .rate_limit) {
            self.payload = nested
        } else {
            self.payload = try RatePayload(from: decoder)
        }
    }
}

private struct DynamicCodingKey: CodingKey {
    let stringValue: String
    let intValue: Int?

    init?(stringValue: String) {
        self.stringValue = stringValue
        self.intValue = nil
    }

    init?(intValue: Int) {
        self.stringValue = String(intValue)
        self.intValue = intValue
    }
}

private enum LiveUsageFetchResult {
    case success(LimitUsage)
    case failure(String)
}

// MARK: - Potion HUD View

private final class RingsView: NSView {
    var onPotionClick: ((RingKind, NSRect) -> Void)?

    var usage = LimitUsage(primaryPercent: nil, secondaryPercent: nil, primaryReset: nil, secondaryReset: nil, source: "none") {
        didSet {
            guard usage != oldValue else { return }
            checkForMilestone(previousPrimary: oldValue.primaryPercent)
            animatePulse()
            needsDisplay = true
        }
    }

    var anchorSize = NSSize(width: 80, height: 80) {
        didSet {
            if oldValue != anchorSize {
                needsDisplay = true
            }
        }
    }

    var overlaySettings = OverlaySettings.defaults {
        didSet {
            if oldValue != overlaySettings {
                needsDisplay = true
            }
        }
    }

    private var particleEmitter = ParticleEmitter()
    private var pulseAnimation: CGFloat = 0
    private var pulseTimer: Timer?
    private let primaryLiquidGradient = NSGradient(colors: [
        NSColor(calibratedRed: 0.22, green: 0.015, blue: 0.025, alpha: 1),
        NSColor(calibratedRed: 0.72, green: 0.035, blue: 0.055, alpha: 1),
        NSColor(calibratedRed: 1.0, green: 0.24, blue: 0.08, alpha: 1)
    ])
    private let secondaryLiquidGradient = NSGradient(colors: [
        NSColor(calibratedRed: 0.025, green: 0.07, blue: 0.24, alpha: 1),
        NSColor(calibratedRed: 0.04, green: 0.32, blue: 0.76, alpha: 1),
        NSColor(calibratedRed: 0.08, green: 0.78, blue: 0.92, alpha: 1)
    ])
    private let emptyGlassGradient = NSGradient(colors: [
        NSColor(calibratedWhite: 0.035, alpha: 0.92),
        NSColor(calibratedWhite: 0.18, alpha: 0.50),
        NSColor(calibratedWhite: 0.04, alpha: 0.86)
    ])

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        pulseTimer?.invalidate()
    }

    override var isFlipped: Bool { true }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
        true
    }

    private func checkForMilestone(previousPrimary: Double?) {
        guard let primary = usage.primaryPercent else { return }
        let previousPrimary = previousPrimary ?? 0

        let milestones = ["25", "50", "75", "100"]
        for milestone in milestones {
            let threshold = Double(milestone) ?? 0
            if primary >= threshold && previousPrimary < threshold {
                triggerMilestoneEffect(milestone: milestone)
            }
        }
    }

    private func triggerMilestoneEffect(milestone: String) {
        let potionBounds = potionBounds(for: .outer)
        let center = CGPoint(x: potionBounds.midX, y: potionBounds.midY)
        let color: NSColor
        switch milestone {
        case "25": color = NSColor.systemBlue
        case "50": color = NSColor.systemYellow
        case "75": color = NSColor.systemOrange
        case "100": color = NSColor.systemRed
        default: color = NSColor.systemGreen
        }
        particleEmitter.emit(at: center, count: 30, color: color)
    }

    func triggerLowUsageEffect(kind: RingKind, threshold: Int) {
        let bounds = potionBounds(for: kind)
        let center = CGPoint(x: bounds.midX, y: bounds.midY)
        let color: NSColor = threshold <= 5 ? .systemRed : (threshold <= 10 ? .systemOrange : .systemPurple)
        particleEmitter.emit(at: center, count: 38, color: color)
        animatePulse()
    }

    private func animatePulse() {
        pulseTimer?.invalidate()
        pulseAnimation = 1.0
        needsDisplay = true

        let startedAt = Date()
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }

            let elapsed = Date().timeIntervalSince(startedAt)
            let progress = min(1, elapsed / RING_PULSE_DURATION)
            let remaining = CGFloat(1 - progress)
            self.pulseAnimation = remaining * remaining
            self.needsDisplay = true

            if progress >= 1 {
                timer.invalidate()
                self.pulseTimer = nil
                self.pulseAnimation = 0
                self.needsDisplay = true
            }
        }
        timer.tolerance = 1.0 / 120.0
        RunLoop.main.add(timer, forMode: .common)
        pulseTimer = timer
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        potion(at: point) == nil ? nil : self
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard let kind = potion(at: point) else { return }
        onPotionClick?(kind, potionBounds(for: kind))
    }

    func potion(at point: NSPoint) -> RingKind? {
        for kind in [RingKind.outer, RingKind.inner] {
            let body = potionBodyBounds(for: kind)
            let radiusX = body.width / 2
            let radiusY = body.height / 2
            guard radiusX > 0, radiusY > 0 else { continue }

            let normalized = pow((point.x - body.midX) / radiusX, 2) + pow((point.y - body.midY) / radiusY, 2)
            let neck = potionBounds(for: kind).insetBy(dx: body.width * 0.34, dy: 0)
            if normalized <= 1 || neck.contains(point) {
                return kind
            }
        }
        return nil
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        NSColor.clear.setFill()
        dirtyRect.fill()

        particleEmitter.update()
        drawParticles()

        drawPotion(kind: .outer)
        drawPotion(kind: .inner)

    }

    private var hudAreaHeight: CGFloat {
        bounds.height
    }

    private var anchorDrawingBounds: NSRect {
        return NSRect(
            x: bounds.minX + potionSideReserve(for: anchorSize, settings: overlaySettings),
            y: bounds.minY + (hudAreaHeight - anchorSize.height) / 2,
            width: anchorSize.width,
            height: anchorSize.height
        )
    }

    private func potionBounds(for kind: RingKind) -> NSRect {
        let diameter = potionOrbDiameter(for: anchorSize, settings: overlaySettings)
        let height = diameter + 14
        let anchor = anchorDrawingBounds
        let x: CGFloat
        switch kind {
        case .outer:
            x = anchor.minX - potionGap(for: overlaySettings) - diameter
        case .inner:
            x = anchor.maxX + potionGap(for: overlaySettings)
        }
        return NSRect(
            x: x,
            y: bounds.minY + (hudAreaHeight - height) / 2 + 2,
            width: diameter,
            height: height
        )
    }

    private func potionBodyBounds(for kind: RingKind) -> NSRect {
        let potion = potionBounds(for: kind)
        return NSRect(x: potion.minX, y: potion.minY + 10, width: potion.width, height: potion.width)
    }

    private func drawPotion(kind: RingKind) {
        let potion = potionBounds(for: kind)
        let body = potionBodyBounds(for: kind)
        guard body.width > 10, body.height > 10 else { return }

        let remaining: Double?
        let liquidGradient: NSGradient?
        let liquidColor: NSColor
        let surfaceColor: NSColor
        let label: String
        let pulseScale: CGFloat
        switch kind {
        case .outer:
            remaining = usage.primaryRemainingPercent
            liquidGradient = primaryLiquidGradient
            liquidColor = NSColor(calibratedRed: 0.92, green: 0.06, blue: 0.08, alpha: 1)
            surfaceColor = NSColor(calibratedRed: 1.0, green: 0.42, blue: 0.12, alpha: 1)
            label = "5H"
            pulseScale = 1
        case .inner:
            remaining = usage.secondaryRemainingPercent
            liquidGradient = secondaryLiquidGradient
            liquidColor = NSColor(calibratedRed: 0.05, green: 0.48, blue: 0.92, alpha: 1)
            surfaceColor = NSColor(calibratedRed: 0.18, green: 0.88, blue: 1.0, alpha: 1)
            label = "WK"
            pulseScale = 0.65
        }

        let pulse = pulseAnimation * pulseScale
        let metal = NSColor(calibratedRed: 0.13, green: 0.12, blue: 0.11, alpha: 0.98)
        let metalEdge = NSColor(calibratedRed: 0.49, green: 0.43, blue: 0.34, alpha: 0.92)
        let cap = NSRect(x: potion.midX - potion.width * 0.20, y: potion.minY, width: potion.width * 0.40, height: 7)
        let neck = NSRect(x: potion.midX - potion.width * 0.15, y: cap.maxY - 1, width: potion.width * 0.30, height: 10)

        let leftBrace = NSBezierPath()
        leftBrace.move(to: NSPoint(x: body.minX + 9, y: body.minY + body.height * 0.16))
        leftBrace.line(to: NSPoint(x: body.minX - 5, y: body.minY + body.height * 0.42))
        leftBrace.line(to: NSPoint(x: body.minX - 2, y: body.minY + body.height * 0.62))
        leftBrace.line(to: NSPoint(x: body.minX + 10, y: body.maxY - 8))
        leftBrace.line(to: NSPoint(x: body.minX + 15, y: body.maxY - 13))
        leftBrace.line(to: NSPoint(x: body.minX + 6, y: body.midY))
        leftBrace.close()

        let rightBrace = NSBezierPath()
        rightBrace.move(to: NSPoint(x: body.maxX - 9, y: body.minY + body.height * 0.16))
        rightBrace.line(to: NSPoint(x: body.maxX + 5, y: body.minY + body.height * 0.42))
        rightBrace.line(to: NSPoint(x: body.maxX + 2, y: body.minY + body.height * 0.62))
        rightBrace.line(to: NSPoint(x: body.maxX - 10, y: body.maxY - 8))
        rightBrace.line(to: NSPoint(x: body.maxX - 15, y: body.maxY - 13))
        rightBrace.line(to: NSPoint(x: body.maxX - 6, y: body.midY))
        rightBrace.close()

        metal.setFill()
        metalEdge.withAlphaComponent(0.78).setStroke()
        for brace in [leftBrace, rightBrace] {
            brace.fill()
            brace.lineWidth = 1.4
            brace.stroke()
        }

        metal.setFill()
        NSBezierPath(roundedRect: neck, xRadius: 2, yRadius: 2).fill()
        metalEdge.setStroke()
        let neckPath = NSBezierPath(roundedRect: neck, xRadius: 2, yRadius: 2)
        neckPath.lineWidth = 1.2
        neckPath.stroke()

        NSColor(calibratedWhite: 0.055, alpha: 1).setFill()
        let capPath = NSBezierPath(roundedRect: cap, xRadius: 2, yRadius: 2)
        capPath.fill()
        metalEdge.withAlphaComponent(0.72).setStroke()
        capPath.lineWidth = 1
        capPath.stroke()

        if pulse > 0.01 {
            liquidColor.withAlphaComponent(0.18 + pulse * 0.12).setStroke()
            let glowPath = NSBezierPath(ovalIn: body.insetBy(dx: -2 - pulse * 2, dy: -2 - pulse * 2))
            glowPath.lineWidth = 3 + pulse * 2
            glowPath.stroke()
        }

        metal.setFill()
        NSBezierPath(ovalIn: body).fill()

        let glass = body.insetBy(dx: 5.5, dy: 5.5)
        let glassPath = NSBezierPath(ovalIn: glass)
        NSGraphicsContext.saveGraphicsState()
        glassPath.addClip()
        emptyGlassGradient?.draw(in: glass, angle: 0)

        if let remaining {
            let fraction = CGFloat(max(0, min(remaining, 100)) / 100)
            let liquidHeight = glass.height * fraction
            if liquidHeight > 0.5 {
                let liquidRect = NSRect(x: glass.minX, y: glass.maxY - liquidHeight, width: glass.width, height: liquidHeight)
                liquidGradient?.draw(in: liquidRect, angle: 90)

                if fraction < 0.995 {
                    surfaceColor.withAlphaComponent(0.92).setFill()
                    let surface = NSRect(x: glass.minX + 1, y: liquidRect.minY - 2.5, width: glass.width - 2, height: 5)
                    NSBezierPath(ovalIn: surface).fill()
                }

                NSColor.white.withAlphaComponent(0.30).setFill()
                let bubbleSize = max(2, glass.width * 0.07)
                let bubbleY = max(liquidRect.minY + 4, glass.maxY - glass.height * 0.28)
                NSBezierPath(ovalIn: NSRect(x: glass.minX + glass.width * 0.62, y: bubbleY, width: bubbleSize, height: bubbleSize)).fill()
            }
        }
        NSGraphicsContext.restoreGraphicsState()

        metalEdge.setStroke()
        let framePath = NSBezierPath(ovalIn: body.insetBy(dx: 2, dy: 2))
        framePath.lineWidth = 4.5 + pulse
        framePath.stroke()
        NSColor.black.withAlphaComponent(0.78).setStroke()
        let outerFrame = NSBezierPath(ovalIn: body)
        outerFrame.lineWidth = 1.2
        outerFrame.stroke()

        NSColor.white.withAlphaComponent(0.34).setFill()
        let highlight = NSRect(
            x: glass.minX + glass.width * 0.20,
            y: glass.minY + glass.height * 0.16,
            width: max(2, glass.width * 0.07),
            height: glass.height * 0.24
        )
        NSBezierPath(roundedRect: highlight, xRadius: highlight.width / 2, yRadius: highlight.width / 2).fill()

        let value = remaining.map { "\(Int(max(0, min($0, 100)).rounded()))%" } ?? "—"
        drawPotionText(value, size: 12, weight: .heavy, in: NSRect(x: body.minX, y: body.midY - 9, width: body.width, height: 18))

        let pedestal = NSRect(x: body.midX - body.width * 0.34, y: body.maxY - 9, width: body.width * 0.68, height: 13)
        metal.setFill()
        NSBezierPath(roundedRect: pedestal, xRadius: 3, yRadius: 3).fill()
        metalEdge.withAlphaComponent(0.82).setStroke()
        let pedestalPath = NSBezierPath(roundedRect: pedestal, xRadius: 3, yRadius: 3)
        pedestalPath.lineWidth = 1.2
        pedestalPath.stroke()

        let plaque = NSRect(x: body.midX - 17, y: body.maxY - 10, width: 34, height: 12)
        NSColor.black.withAlphaComponent(0.82).setFill()
        NSBezierPath(roundedRect: plaque, xRadius: 3, yRadius: 3).fill()
        metalEdge.withAlphaComponent(0.70).setStroke()
        let plaquePath = NSBezierPath(roundedRect: plaque, xRadius: 3, yRadius: 3)
        plaquePath.lineWidth = 0.8
        plaquePath.stroke()
        drawPotionText(label, size: 8, weight: .heavy, in: plaque)
    }

    private func drawPotionText(_ text: String, size: CGFloat, weight: NSFont.Weight, in rect: NSRect) {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight),
            .foregroundColor: NSColor.white.withAlphaComponent(0.92)
        ]
        let textSize = text.size(withAttributes: attributes)
        text.draw(
            at: NSPoint(x: rect.midX - textSize.width / 2, y: rect.midY - textSize.height / 2),
            withAttributes: attributes
        )
    }

    private func drawParticles() {
        for particle in particleEmitter.particles {
            let rect = NSRect(
                x: particle.x - particle.size / 2,
                y: particle.y - particle.size / 2,
                width: particle.size,
                height: particle.size
            )
            particle.color.withAlphaComponent(particle.alpha).setFill()
            NSBezierPath(ovalIn: rect).fill()
        }
    }

}

private struct UsageDetailSnapshot {
    let usage: LimitUsage
    let sourceText: String
    let refreshedAt: Date?
    let isRefreshing: Bool
    let errorText: String?
}

private final class UsageDetailViewController: NSViewController {
    var onRefresh: (() -> Void)?

    private let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ko_KR")
        formatter.dateFormat = "M월 d일 a h:mm"
        return formatter
    }()
    private let primaryValue = NSTextField(labelWithString: "—")
    private let primaryReset = NSTextField(labelWithString: "초기화 —")
    private let secondaryValue = NSTextField(labelWithString: "—")
    private let secondaryReset = NSTextField(labelWithString: "초기화 —")
    private let statusLabel = NSTextField(labelWithString: "데이터 확인 중")
    private let refreshButton = NSButton(title: "지금 갱신", target: nil, action: nil)

    override func loadView() {
        let root = NSView(frame: NSRect(x: 0, y: 0, width: 328, height: 248))
        root.wantsLayer = true

        let title = NSTextField(labelWithString: "🧪 Codex 포션 상세")
        title.font = .systemFont(ofSize: 15, weight: .bold)

        let subtitle = NSTextField(labelWithString: "실제 Codex 펫의 두 사용 한도")
        subtitle.font = .systemFont(ofSize: 11)
        subtitle.textColor = .secondaryLabelColor

        let primaryCard = usageCard(
            title: "5시간 포션",
            color: .systemRed,
            valueLabel: primaryValue,
            resetLabel: primaryReset
        )
        let secondaryCard = usageCard(
            title: "주간 포션",
            color: .systemBlue,
            valueLabel: secondaryValue,
            resetLabel: secondaryReset
        )

        statusLabel.font = .systemFont(ofSize: 10.5)
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.lineBreakMode = .byTruncatingTail

        refreshButton.bezelStyle = .rounded
        refreshButton.controlSize = .small
        refreshButton.target = self
        refreshButton.action = #selector(refreshPressed)

        let footer = NSStackView(views: [statusLabel, refreshButton])
        footer.orientation = .horizontal
        footer.alignment = .centerY
        footer.distribution = .fill
        footer.spacing = 10

        let stack = NSStackView(views: [title, subtitle, primaryCard, secondaryCard, footer])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 9
        stack.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 15),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -14),
            primaryCard.widthAnchor.constraint(equalTo: stack.widthAnchor),
            secondaryCard.widthAnchor.constraint(equalTo: stack.widthAnchor),
            footer.widthAnchor.constraint(equalTo: stack.widthAnchor)
        ])
        view = root
    }

    func update(snapshot: UsageDetailSnapshot) {
        primaryValue.stringValue = percentText(snapshot.usage.primaryRemainingPercent)
        primaryReset.stringValue = "초기화 \(dateText(snapshot.usage.primaryReset))"
        secondaryValue.stringValue = percentText(snapshot.usage.secondaryRemainingPercent)
        secondaryReset.stringValue = "초기화 \(dateText(snapshot.usage.secondaryReset))"

        var status = snapshot.sourceText
        if let refreshedAt = snapshot.refreshedAt {
            status += " · \(relativeText(refreshedAt))"
        }
        if let errorText = snapshot.errorText {
            status += " · 실패 \(errorText)"
        }
        statusLabel.stringValue = status
        refreshButton.title = snapshot.isRefreshing ? "갱신 중…" : "지금 갱신"
        refreshButton.isEnabled = !snapshot.isRefreshing
    }

    private func usageCard(
        title: String,
        color: NSColor,
        valueLabel: NSTextField,
        resetLabel: NSTextField
    ) -> NSView {
        let card = NSView()
        card.wantsLayer = true
        card.layer?.cornerRadius = 9
        card.layer?.borderWidth = 1
        card.layer?.borderColor = NSColor.separatorColor.withAlphaComponent(0.7).cgColor
        card.layer?.backgroundColor = NSColor.controlBackgroundColor.withAlphaComponent(0.55).cgColor

        let dot = NSTextField(labelWithString: "●")
        dot.textColor = color
        dot.font = .systemFont(ofSize: 11)
        let name = NSTextField(labelWithString: title)
        name.font = .systemFont(ofSize: 12, weight: .semibold)
        let heading = NSStackView(views: [dot, name])
        heading.orientation = .horizontal
        heading.spacing = 5

        valueLabel.font = .monospacedDigitSystemFont(ofSize: 18, weight: .bold)
        resetLabel.font = .systemFont(ofSize: 10.5)
        resetLabel.textColor = .secondaryLabelColor

        let content = NSStackView(views: [heading, valueLabel, resetLabel])
        content.orientation = .vertical
        content.alignment = .leading
        content.spacing = 3
        content.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 10),
            content.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -10),
            content.topAnchor.constraint(equalTo: card.topAnchor, constant: 8),
            content.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -8)
        ])
        return card
    }

    private func percentText(_ value: Double?) -> String {
        value.map { "\(Int(max(0, min($0, 100)).rounded()))% 남음" } ?? "데이터 없음"
    }

    private func dateText(_ timestamp: TimeInterval?) -> String {
        guard let timestamp else { return "—" }
        return dateFormatter.string(from: Date(timeIntervalSince1970: timestamp))
    }

    private func relativeText(_ date: Date) -> String {
        let seconds = max(0, Int(Date().timeIntervalSince(date)))
        return seconds < 60 ? "\(seconds)초 전" : "\(seconds / 60)분 전"
    }

    @objc private func refreshPressed() {
        onRefresh?()
    }
}

private final class UsagePopoverController {
    private let popover = NSPopover()
    private let contentController = UsageDetailViewController()

    var isShown: Bool { popover.isShown }

    init(onRefresh: @escaping () -> Void) {
        contentController.onRefresh = onRefresh
        popover.contentViewController = contentController
        popover.contentSize = NSSize(width: 328, height: 248)
        popover.behavior = .transient
        popover.animates = true
    }

    func show(relativeTo rect: NSRect, of view: NSView, snapshot: UsageDetailSnapshot) {
        contentController.update(snapshot: snapshot)
        if !popover.isShown {
            popover.show(relativeTo: rect, of: view, preferredEdge: .maxY)
        }
    }

    func update(snapshot: UsageDetailSnapshot) {
        guard popover.isShown else { return }
        contentController.update(snapshot: snapshot)
    }

    func close() {
        popover.close()
    }
}

private struct UsageAlertMessage {
    let kind: RingKind
    let threshold: Int
    let title: String
    let body: String
    let identifier: String
}

// MARK: - RingsApp

private final class RingsApp: NSObject, NSApplicationDelegate, NSMenuDelegate, UNUserNotificationCenterDelegate {
    private let stateURL = URL(fileURLWithPath: NSHomeDirectory())
        .appendingPathComponent(".codex/.codex-global-state.json")
    private let configURL = URL(fileURLWithPath: NSHomeDirectory())
        .appendingPathComponent(".codex/config.toml")
    private let authURL = URL(fileURLWithPath: NSHomeDirectory())
        .appendingPathComponent(".codex/auth.json")
    private let logsURL = URL(fileURLWithPath: NSHomeDirectory())
        .appendingPathComponent(".codex/logs_2.sqlite")
    private let usageURL = URL(string: "https://chatgpt.com/backend-api/wham/usage")!
    private lazy var usageSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.urlCache = nil
        return URLSession(configuration: config)
    }()
    private let settingsDirectoryURL = FileManager.default
        .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("CodexPetLimitRings", isDirectory: true)
    private lazy var alertStateURL = settingsDirectoryURL.appendingPathComponent("alert-state.json")
    private var window: NSWindow?
    private var tooltipWindow: NSWindow?
    private var usageAlertWindow: NSWindow?
    private var usageAlertDismissWorkItem: DispatchWorkItem?
    private var pendingUsageAlerts: [UsageAlertMessage] = []
    private var usagePopoverController: UsagePopoverController?
    private var settingsWindowController: SettingsWindowController?
    private var pointerMonitors: [Any] = []
    private var timer: Timer?
    private var cleanupTimer: Timer?
    private var lastAnchor: Anchor?
    private var lastLiveUsage: LimitUsage?
    private var lastLiveUsageFetch: Date?
    private var lastCachedUsageFetch: Date?
    private var lastLiveUsageAttempt: Date?
    private var statusItem: NSStatusItem?
    private var lastMenuUsage: LimitUsage?
    private var liveUsageFetchInFlight = false
    private var lastLiveUsageError: String?
    private var anchorMissing = true
    private var activeModelCache: String?
    private var activeModelCheckedAt = Date.distantPast
    private var cachedUsageSnapshot: LimitUsage?
    private var cachedUsageReadAt = Date.distantPast
    private var cachedStateStamp: StateFileStamp?
    private var cachedStateRoot: [String: Any]?
    private var petWindowID: CGWindowID?
    private var overlaySettings = OverlaySettings.defaults
    private var alertState = UsageAlertState.empty
    private var cleanupInFlight = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        loadOverlaySettings()
        saveOverlaySettings()
        loadAlertState()
        UNUserNotificationCenter.current().delegate = self
        NSApp.setActivationPolicy(.accessory)
        ensureStatusItem()
        installPointerMonitors()
        updateWindow()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.updateWindow()
        }
        timer?.tolerance = 0.1
        scheduleCleanupIfNeeded()
    }

    func applicationWillTerminate(_ notification: Notification) {
        for monitor in pointerMonitors {
            NSEvent.removeMonitor(monitor)
        }
        pointerMonitors.removeAll()
        timer?.invalidate()
        timer = nil
        usageSession.invalidateAndCancel()
        cleanupTimer?.invalidate()
        cleanupTimer = nil
        settingsWindowController?.close()
        settingsWindowController = nil
        usagePopoverController?.close()
        usagePopoverController = nil
        usageAlertDismissWorkItem?.cancel()
        usageAlertWindow?.orderOut(nil)
        usageAlertWindow = nil
        removeStatusItemIfNeeded()
    }

    private func installPointerMonitors() {
        if let globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved, handler: { [weak self] _ in
            DispatchQueue.main.async {
                self?.updatePointerState()
            }
        }) {
            pointerMonitors.append(globalMonitor)
        }
        if let localMonitor = NSEvent.addLocalMonitorForEvents(matching: .mouseMoved, handler: { [weak self] event in
            self?.updatePointerState()
            return event
        }) {
            pointerMonitors.append(localMonitor)
        }
    }

    private func ensureStatusItem() {
        guard statusItem == nil else { return }

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = nil
        item.button?.title = "🐱"
        item.button?.toolTip = "Codex 포션 HUD"
        item.menu = makeStatusMenu()
        statusItem = item
    }

    private func removeStatusItemIfNeeded() {
        guard let statusItem else { return }
        NSStatusBar.system.removeStatusItem(statusItem)
        self.statusItem = nil
    }

    private func makeStatusMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self

        let header = NSMenuItem(title: "🐱 Codex 포션 HUD", action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(NSMenuItem(title: statusLine(), action: nil, keyEquivalent: ""))
        menu.items.last?.isEnabled = false
        menu.addItem(NSMenuItem(title: liveStatusLine(), action: nil, keyEquivalent: ""))
        menu.items.last?.isEnabled = false
        menu.addItem(.separator())

        let scaleItem = NSMenuItem(title: "오버레이 크기", action: nil, keyEquivalent: "")
        let scaleMenu = NSMenu(title: "오버레이 크기")
        for percent in [75, 100, 125, 150] {
            let item = NSMenuItem(
                title: "\(percent)%",
                action: #selector(selectOverlayScale(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = percent
            item.state = abs(overlaySettings.scale - Double(percent) / 100) < 0.001 ? .on : .off
            scaleMenu.addItem(item)
        }
        scaleItem.submenu = scaleMenu
        menu.addItem(scaleItem)

        let gapItem = NSMenuItem(title: "포션 간격", action: nil, keyEquivalent: "")
        let gapMenu = NSMenu(title: "포션 간격")
        for (title, value) in [("가깝게", 0), ("기본", 10), ("넓게", 40), ("아주 넓게", 80)] {
            let item = NSMenuItem(
                title: title,
                action: #selector(selectPotionGap(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = value
            item.state = overlaySettings.potionGap == Double(value) ? .on : .off
            gapMenu.addItem(item)
        }
        gapItem.submenu = gapMenu
        menu.addItem(gapItem)

        let resetPositionItem = NSMenuItem(
            title: "오버레이 위치 초기화",
            action: #selector(resetOverlayPosition),
            keyEquivalent: ""
        )
        resetPositionItem.target = self
        resetPositionItem.isEnabled = overlaySettings.horizontalOffset != 0 || overlaySettings.verticalOffset != 0
        menu.addItem(resetPositionItem)
        menu.addItem(.separator())

        let alertsItem = NSMenuItem(
            title: "잔여량 알림",
            action: #selector(toggleUsageAlerts(_:)),
            keyEquivalent: ""
        )
        alertsItem.target = self
        alertsItem.state = overlaySettings.usageAlertsEnabled ? .on : .off
        menu.addItem(alertsItem)

        let refreshItem = NSMenuItem(
            title: liveUsageFetchInFlight ? "사용량 갱신 중…" : "사용량 지금 갱신",
            action: #selector(refreshUsageNow),
            keyEquivalent: "r"
        )
        refreshItem.target = self
        refreshItem.isEnabled = !liveUsageFetchInFlight
        menu.addItem(refreshItem)
        menu.addItem(.separator())

        let cleanupItem = NSMenuItem(
            title: "매일 캐시 자동 정리",
            action: #selector(toggleAutomaticCleanup(_:)),
            keyEquivalent: ""
        )
        cleanupItem.target = self
        cleanupItem.state = overlaySettings.autoCleanup ? .on : .off
        menu.addItem(cleanupItem)

        let cleanupNowItem = NSMenuItem(
            title: cleanupInFlight ? "캐시 정리 중…" : "캐시 지금 정리",
            action: #selector(cleanupNow),
            keyEquivalent: ""
        )
        cleanupNowItem.target = self
        cleanupNowItem.isEnabled = !cleanupInFlight
        menu.addItem(cleanupNowItem)
        menu.addItem(.separator())

        let settingsItem = NSMenuItem(title: "설정 열기…", action: #selector(openSettingsPage), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        let refreshed = makeStatusMenu()
        menu.removeAllItems()
        while let item = refreshed.items.first {
            refreshed.removeItem(item)
            menu.addItem(item)
        }
        menu.delegate = self
    }

    @objc private func selectOverlayScale(_ sender: NSMenuItem) {
        guard let percent = sender.representedObject as? Int else { return }
        var settings = overlaySettings
        settings.scale = Double(percent) / 100
        applyOverlaySettings(settings)
    }

    @objc private func selectPotionGap(_ sender: NSMenuItem) {
        guard let gap = sender.representedObject as? Int else { return }
        var settings = overlaySettings
        settings.potionGap = Double(gap)
        applyOverlaySettings(settings)
    }

    @objc private func resetOverlayPosition() {
        var settings = overlaySettings
        settings.horizontalOffset = 0
        settings.verticalOffset = 0
        applyOverlaySettings(settings)
    }

    @objc private func toggleUsageAlerts(_ sender: NSMenuItem) {
        var settings = overlaySettings
        settings.usageAlertsEnabled.toggle()
        applyOverlaySettings(settings)
    }

    @objc private func refreshUsageNow() {
        startLiveUsageRefreshIfNeeded(force: true)
        updateUsagePopoverIfShown()
    }

    @objc private func toggleAutomaticCleanup(_ sender: NSMenuItem) {
        var settings = overlaySettings
        settings.autoCleanup.toggle()
        applyOverlaySettings(settings)
    }

    @objc private func cleanupNow() {
        runCleanup(completion: nil)
    }

    @objc private func openSettingsPage() {
        if let settingsWindowController {
            settingsWindowController.show(settings: overlaySettings, cleanup: cleanupStatus())
            return
        }

        guard let assetDirectory = settingsAssetDirectory() else {
            NSLog("CodexPetLimitRings settings assets missing")
            return
        }
        let controller = SettingsWindowController(
            assetDirectory: assetDirectory,
            onSave: { [weak self] settings in
                self?.applyOverlaySettings(settings)
            },
            onCleanup: { [weak self] completion in
                self?.runCleanup(completion: completion)
            },
            onClose: { [weak self] in
                self?.settingsWindowController = nil
            }
        )
        settingsWindowController = controller
        controller.show(settings: overlaySettings, cleanup: cleanupStatus())
    }

    private var settingsURL: URL {
        settingsDirectoryURL.appendingPathComponent("settings.json")
    }

    private func loadOverlaySettings() {
        guard let data = try? Data(contentsOf: settingsURL),
              let decoded = try? JSONDecoder().decode(OverlaySettings.self, from: data) else {
            overlaySettings = .defaults
            return
        }
        overlaySettings = decoded.normalized()
    }

    private func applyOverlaySettings(_ incoming: OverlaySettings) {
        let shouldRequestNotifications = incoming.nativeNotificationsEnabled && !overlaySettings.nativeNotificationsEnabled
        var value = incoming.normalized()
        value.lastCleanupAt = overlaySettings.lastCleanupAt
        value.lastFreedBytes = overlaySettings.lastFreedBytes
        overlaySettings = value
        saveOverlaySettings()
        scheduleCleanupIfNeeded()
        updateWindow()
        settingsWindowController?.update(settings: overlaySettings, cleanup: cleanupStatus())
        if shouldRequestNotifications {
            requestNativeNotificationPermission()
        }
    }

    private func saveOverlaySettings() {
        do {
            try FileManager.default.createDirectory(
                at: settingsDirectoryURL,
                withIntermediateDirectories: true,
                attributes: [.posixPermissions: 0o700]
            )
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(overlaySettings).write(to: settingsURL, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: settingsURL.path)
        } catch {
            NSLog("CodexPetLimitRings settings save failed: %@", error.localizedDescription)
        }
    }

    private func loadAlertState() {
        guard let data = try? Data(contentsOf: alertStateURL),
              let decoded = try? JSONDecoder().decode(UsageAlertState.self, from: data) else {
            alertState = .empty
            return
        }
        alertState = decoded
    }

    private func saveAlertState() {
        do {
            try FileManager.default.createDirectory(
                at: settingsDirectoryURL,
                withIntermediateDirectories: true,
                attributes: [.posixPermissions: 0o700]
            )
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(alertState).write(to: alertStateURL, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: alertStateURL.path)
        } catch {
            NSLog("CodexPetLimitRings alert state save failed: %@", error.localizedDescription)
        }
    }

    private func requestNativeNotificationPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { [weak self] granted, _ in
            guard !granted else { return }
            DispatchQueue.main.async {
                guard let self else { return }
                self.overlaySettings.nativeNotificationsEnabled = false
                self.saveOverlaySettings()
                self.settingsWindowController?.update(settings: self.overlaySettings, cleanup: self.cleanupStatus())
            }
        }
    }

    private func settingsAssetDirectory() -> URL? {
        var candidates: [URL] = []
        if let resources = Bundle.main.resourceURL {
            candidates.append(resources.appendingPathComponent("settings", isDirectory: true))
            candidates.append(resources.appendingPathComponent("assets/settings", isDirectory: true))
        }
        if let executableDirectory = Bundle.main.executableURL?.deletingLastPathComponent() {
            candidates.append(executableDirectory.appendingPathComponent("settings", isDirectory: true))
            candidates.append(executableDirectory.appendingPathComponent("assets/settings", isDirectory: true))
            candidates.append(
                executableDirectory
                    .deletingLastPathComponent()
                    .deletingLastPathComponent()
                    .deletingLastPathComponent()
                    .appendingPathComponent("assets/settings", isDirectory: true)
            )
        }
        candidates.append(
            URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
                .appendingPathComponent("shared/settings", isDirectory: true)
        )
        candidates.append(
            URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
                .appendingPathComponent("assets/settings", isDirectory: true)
        )
        return candidates.first { FileManager.default.fileExists(atPath: $0.appendingPathComponent("index.html").path) }
    }

    private func cleanupStatus() -> CleanupResult {
        let nextCleanup: TimeInterval?
        if overlaySettings.autoCleanup {
            nextCleanup = (overlaySettings.lastCleanupAt ?? Date().timeIntervalSince1970) + 24 * 60 * 60
        } else {
            nextCleanup = nil
        }
        return CleanupResult(
            freedBytes: overlaySettings.lastFreedBytes,
            cleanedAt: overlaySettings.lastCleanupAt ?? 0,
            nextCleanupAt: nextCleanup
        )
    }

    private func scheduleCleanupIfNeeded() {
        cleanupTimer?.invalidate()
        cleanupTimer = nil
        guard overlaySettings.autoCleanup else { return }

        let now = Date().timeIntervalSince1970
        let next = (overlaySettings.lastCleanupAt ?? 0) + 24 * 60 * 60
        if next <= now {
            runCleanup(completion: nil)
            return
        }

        let timer = Timer(timeInterval: max(60, next - now), repeats: false) { [weak self] _ in
            self?.runCleanup(completion: nil)
        }
        timer.tolerance = 60
        RunLoop.main.add(timer, forMode: .common)
        cleanupTimer = timer
    }

    private func runCleanup(completion: ((CleanupResult) -> Void)?) {
        guard !cleanupInFlight else {
            completion?(cleanupStatus())
            return
        }
        cleanupInFlight = true

        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self else { return }
            let freedBytes = self.performCleanup()
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.cleanupInFlight = false
                self.overlaySettings.lastCleanupAt = Date().timeIntervalSince1970
                self.overlaySettings.lastFreedBytes = freedBytes
                self.saveOverlaySettings()
                self.scheduleCleanupIfNeeded()
                let status = self.cleanupStatus()
                self.settingsWindowController?.update(settings: self.overlaySettings, cleanup: status)
                completion?(status)
                NSLog("CodexPetLimitRings cleanup freed=%lld", freedBytes)
            }
        }
    }

    private func performCleanup() -> Int64 {
        let fileManager = FileManager.default
        var freedBytes: Int64 = 0

        if Bundle.main.bundleURL.pathExtension == "app" {
            let root = Bundle.main.bundleURL
                .deletingLastPathComponent()
                .deletingLastPathComponent()
            let stderrURL = root.appendingPathComponent("logs/stderr.log")
            if let size = fileSize(at: stderrURL), size > 1_048_576,
               let handle = try? FileHandle(forWritingTo: stderrURL) {
                handle.truncateFile(atOffset: 0)
                try? handle.close()
                freedBytes += size
            }
        }

        let home = URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
        let httpStorage = home.appendingPathComponent("Library/HTTPStorages", isDirectory: true)
        let cacheStorage = home.appendingPathComponent("Library/Caches", isDirectory: true)
        let targets = [
            httpStorage.appendingPathComponent("CodexPetLimitRings", isDirectory: true),
            httpStorage.appendingPathComponent("CodexPetLimitRings.binarycookies"),
            httpStorage.appendingPathComponent("com.appcaster.codex-pet-limit-rings", isDirectory: true),
            httpStorage.appendingPathComponent("com.appcaster.codex-pet-limit-rings.binarycookies"),
            cacheStorage.appendingPathComponent("CodexPetLimitRings", isDirectory: true),
            cacheStorage.appendingPathComponent("com.appcaster.codex-pet-limit-rings", isDirectory: true),
            fileManager.temporaryDirectory.appendingPathComponent("CodexPetLimitRings", isDirectory: true),
            fileManager.temporaryDirectory.appendingPathComponent("com.appcaster.codex-pet-limit-rings", isDirectory: true)
        ]

        for target in targets where fileManager.fileExists(atPath: target.path) {
            freedBytes += directorySize(at: target)
            try? fileManager.removeItem(at: target)
        }
        return freedBytes
    }

    private func fileSize(at url: URL) -> Int64? {
        let attributes = try? FileManager.default.attributesOfItem(atPath: url.path)
        return (attributes?[.size] as? NSNumber)?.int64Value
    }

    private func directorySize(at url: URL) -> Int64 {
        guard let enumerator = FileManager.default.enumerator(
            at: url,
            includingPropertiesForKeys: [.fileSizeKey],
            options: [.skipsHiddenFiles]
        ) else {
            return fileSize(at: url) ?? 0
        }
        var total: Int64 = 0
        for case let fileURL as URL in enumerator {
            let values = try? fileURL.resourceValues(forKeys: [.fileSizeKey])
            total += Int64(values?.fileSize ?? 0)
        }
        return total
    }

    private func statusLine() -> String {
        if anchorMissing {
            return "상태: Codex pet 꺼짐"
        }
        guard let usage = lastMenuUsage else {
            return "상태: 데이터 확인 중"
        }
        let primary = remainingSummary(usage.primaryPercent)
        let secondary = remainingSummary(usage.secondaryPercent)
        let source = usageSourceLabel(for: usage)
        return "사용량: 5시간 \(primary) · 주간 \(secondary) · \(source)"
    }

    private func liveStatusLine() -> String {
        if liveUsageFetchInFlight {
            return "Live: 갱신 중"
        }
        if let fetchedAt = lastLiveUsageFetch {
            let age = max(0, Int(Date().timeIntervalSince(fetchedAt).rounded()))
            return "Live: \(age)초 전 갱신"
        }
        if let lastLiveUsageError {
            return "Live: 실패 \(lastLiveUsageError)"
        }
        return "Live: 대기"
    }

    private func usageSourceLabel(for usage: LimitUsage) -> String {
        if usage.source == "live" {
            return liveUsageFetchInFlight ? "Live · 갱신 중" : "Live"
        }
        if liveUsageFetchInFlight {
            return "Cached · Live 갱신 중"
        }
        if lastLiveUsageError != nil {
            return "Cached · Live 실패"
        }
        return usage.source == "none" ? "대기" : "Cached"
    }

    private func remainingSummary(_ usedPercent: Double?) -> String {
        guard let usedPercent else { return "--" }
        let remaining = max(0, min(100, 100 - usedPercent))
        return "\(Int(remaining.rounded()))%"
    }

    private func updateWindow() {
        guard let anchor = readAnchor() else {
            hideHUDForPet()
            return
        }
        let wasHidden = anchorMissing
        anchorMissing = false
        ensureStatusItem()
        let usage = readUsage()

        guard let placement = placement(for: anchor) else { return }
        let frame = placement.frame
        let ringsWindow = window ?? makeWindow(frame: frame)
        if ringsWindow.frame != frame {
            ringsWindow.setFrame(frame, display: true)
        }
        if let ringsView = ringsWindow.contentView as? RingsView {
            ringsView.anchorSize = NSSize(width: anchor.width, height: anchor.height)
            ringsView.overlaySettings = placement.renderSettings

            ringsView.usage = usage
            evaluateUsageAlerts(usage, ringsView: ringsView)
            updateStatusItem(using: usage)
            updateUsagePopoverIfShown()
        }
        if !ringsWindow.isVisible {
            ringsWindow.orderFrontRegardless()
        }
        window = ringsWindow
        updatePointerState()

        if wasHidden {
            NSLog("CodexPetLimitRings pet trigger visible; HUD shown")
            statusItem?.menu = makeStatusMenu()
        }

        if anchor != lastAnchor {
            lastAnchor = anchor
            NSLog(
                "CodexPetLimitRings anchor=(%.0f,%.0f %.0fx%.0f) frame=(%.0f,%.0f %.0fx%.0f)",
                anchor.x,
                anchor.y,
                anchor.width,
                anchor.height,
                frame.origin.x,
                frame.origin.y,
                frame.width,
                frame.height
            )
        }
    }

    private func hideHUDForPet() {
        let wasVisible = !anchorMissing
        anchorMissing = true
        window?.ignoresMouseEvents = true
        window?.orderOut(nil)
        hideTooltip()
        usagePopoverController?.close()
        usageAlertDismissWorkItem?.cancel()
        usageAlertDismissWorkItem = nil
        usageAlertWindow?.orderOut(nil)
        usageAlertWindow = nil
        pendingUsageAlerts.removeAll()
        lastAnchor = nil
        if wasVisible {
            NSLog("CodexPetLimitRings pet trigger hidden; HUD hidden")
            statusItem?.menu = makeStatusMenu()
        }
    }

    private func updateStatusItem(using usage: LimitUsage) {
        lastMenuUsage = usage
        guard let button = statusItem?.button else { return }
        button.toolTip = statusLine()
    }

    private func showUsagePopover(relativeTo rect: NSRect, of ringsView: RingsView) {
        hideTooltip()
        if usagePopoverController == nil {
            usagePopoverController = UsagePopoverController(onRefresh: { [weak self] in
                self?.startLiveUsageRefreshIfNeeded(force: true)
                self?.updateUsagePopoverIfShown()
            })
        }
        usagePopoverController?.show(relativeTo: rect, of: ringsView, snapshot: usageDetailSnapshot())
    }

    private func updateUsagePopoverIfShown() {
        usagePopoverController?.update(snapshot: usageDetailSnapshot())
    }

    private func usageDetailSnapshot() -> UsageDetailSnapshot {
        let usage = lastMenuUsage ?? emptyUsage()
        let refreshedAt = usage.source == "live" ? lastLiveUsageFetch : lastCachedUsageFetch
        return UsageDetailSnapshot(
            usage: usage,
            sourceText: usageSourceLabel(for: usage),
            refreshedAt: refreshedAt,
            isRefreshing: liveUsageFetchInFlight,
            errorText: lastLiveUsageError
        )
    }

    private func evaluateUsageAlerts(_ usage: LimitUsage, ringsView: RingsView) {
        guard overlaySettings.usageAlertsEnabled, !overlaySettings.alertThresholds.isEmpty else { return }
        let candidates: [(RingKind, Double?, TimeInterval?)] = [
            (.outer, usage.primaryRemainingPercent, usage.primaryReset),
            (.inner, usage.secondaryRemainingPercent, usage.secondaryReset)
        ]
        for (kind, remaining, resetAt) in candidates {
            guard let message = usageAlertDecision(kind: kind, remaining: remaining, resetAt: resetAt) else { continue }
            ringsView.triggerLowUsageEffect(kind: kind, threshold: message.threshold)
            enqueueUsageAlert(message)
            deliverNativeAlertIfEnabled(message)
        }
    }

    private func usageAlertDecision(
        kind: RingKind,
        remaining: Double?,
        resetAt: TimeInterval?
    ) -> UsageAlertMessage? {
        guard let remaining,
              let resetAt,
              resetAt > Date().timeIntervalSince1970 else { return nil }

        let resetID = Int64(resetAt.rounded())
        var delivery = kind == .outer ? alertState.primary : alertState.secondary
        let resetChanged = delivery.resetAt != resetID
        if resetChanged {
            delivery = ThresholdDeliveryState(resetAt: resetID, delivered: [])
        }

        let crossed = overlaySettings.alertThresholds.filter {
            remaining <= Double($0) && !delivery.delivered.contains($0)
        }
        guard let urgent = crossed.min() else {
            if resetChanged {
                setDeliveryState(delivery, for: kind)
                saveAlertState()
            }
            return nil
        }

        let allCrossed = overlaySettings.alertThresholds.filter { remaining <= Double($0) }
        delivery.delivered = Array(Set(delivery.delivered + allCrossed)).sorted(by: >)
        setDeliveryState(delivery, for: kind)
        saveAlertState()

        let label = kind == .outer ? "5시간 포션" : "주간 포션"
        return UsageAlertMessage(
            kind: kind,
            threshold: urgent,
            title: "\(label) \(urgent)% 이하",
            body: "현재 \(Int(max(0, remaining).rounded()))% 남았어요. 필요한 작업을 미리 마무리해 주세요.",
            identifier: "usage-\(kind == .outer ? "primary" : "secondary")-\(resetID)-\(urgent)"
        )
    }

    private func setDeliveryState(_ delivery: ThresholdDeliveryState, for kind: RingKind) {
        switch kind {
        case .outer:
            alertState.primary = delivery
        case .inner:
            alertState.secondary = delivery
        }
    }

    private func enqueueUsageAlert(_ message: UsageAlertMessage) {
        pendingUsageAlerts.append(message)
        showNextUsageAlertIfNeeded()
    }

    private func showNextUsageAlertIfNeeded() {
        guard usageAlertWindow == nil, !pendingUsageAlerts.isEmpty else { return }
        let message = pendingUsageAlerts.removeFirst()
        let alertWidth: CGFloat = 320
        let alertHeight: CGFloat = 78
        let alertWindow = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: alertWidth, height: alertHeight),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        let visual = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: alertWidth, height: alertHeight))
        visual.material = .hudWindow
        visual.blendingMode = .behindWindow
        visual.state = .active
        visual.wantsLayer = true
        visual.layer?.cornerRadius = 13
        visual.layer?.borderWidth = 1
        visual.layer?.borderColor = NSColor.white.withAlphaComponent(0.16).cgColor

        let icon = NSTextField(labelWithString: message.threshold <= 5 ? "🧪" : "⚗️")
        icon.font = .systemFont(ofSize: 25)
        icon.alignment = .center
        icon.frame = NSRect(x: 13, y: 21, width: 42, height: 36)
        visual.addSubview(icon)

        let title = NSTextField(labelWithString: message.title)
        title.font = .systemFont(ofSize: 13.5, weight: .bold)
        title.textColor = .white
        title.frame = NSRect(x: 63, y: 43, width: 242, height: 20)
        visual.addSubview(title)

        let body = NSTextField(wrappingLabelWithString: message.body)
        body.font = .systemFont(ofSize: 10.5)
        body.textColor = NSColor.white.withAlphaComponent(0.72)
        body.frame = NSRect(x: 63, y: 13, width: 242, height: 30)
        visual.addSubview(body)

        alertWindow.contentView = visual
        alertWindow.backgroundColor = .clear
        alertWindow.isOpaque = false
        alertWindow.hasShadow = true
        alertWindow.ignoresMouseEvents = true
        alertWindow.level = .screenSaver
        alertWindow.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]

        let reference = window?.frame ?? NSScreen.main?.visibleFrame ?? .zero
        let targetScreen = screen(containing: NSPoint(x: reference.midX, y: reference.midY))
        let visible = targetScreen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? .zero
        var x = reference.midX - alertWidth / 2
        var y = reference.maxY + 10
        x = min(max(x, visible.minX + 8), visible.maxX - alertWidth - 8)
        if y + alertHeight > visible.maxY {
            y = max(visible.minY + 8, reference.minY - alertHeight - 10)
        }
        alertWindow.setFrameOrigin(NSPoint(x: x, y: y))
        alertWindow.alphaValue = 0
        alertWindow.orderFrontRegardless()
        usageAlertWindow = alertWindow
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.2
            alertWindow.animator().alphaValue = 1
        }

        let workItem = DispatchWorkItem { [weak self, weak alertWindow] in
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.25
                alertWindow?.animator().alphaValue = 0
            } completionHandler: { [weak self, weak alertWindow] in
                alertWindow?.orderOut(nil)
                self?.usageAlertWindow = nil
                self?.showNextUsageAlertIfNeeded()
            }
        }
        usageAlertDismissWorkItem?.cancel()
        usageAlertDismissWorkItem = workItem
        DispatchQueue.main.asyncAfter(deadline: .now() + 4.8, execute: workItem)
    }

    private func deliverNativeAlertIfEnabled(_ message: UsageAlertMessage) {
        guard overlaySettings.nativeNotificationsEnabled else { return }
        let content = UNMutableNotificationContent()
        content.title = message.title
        content.body = message.body
        content.sound = .default
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: message.identifier, content: content, trigger: nil)
        )
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound])
    }

    private func makeWindow(frame: NSRect) -> NSWindow {
        let ringsWindow = NSWindow(
            contentRect: frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        ringsWindow.backgroundColor = .clear
        let ringsView = RingsView(frame: NSRect(origin: .zero, size: frame.size))
        ringsView.usage = readUsage()
        ringsView.overlaySettings = overlaySettings
        ringsView.onPotionClick = { [weak self, weak ringsView] _, rect in
            guard let self, let ringsView else { return }
            self.showUsagePopover(relativeTo: rect, of: ringsView)
        }
        ringsWindow.contentView = ringsView
        ringsWindow.acceptsMouseMovedEvents = true
        ringsWindow.hasShadow = false
        ringsWindow.ignoresMouseEvents = true
        ringsWindow.isOpaque = false
        ringsWindow.level = .statusBar
        ringsWindow.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        return ringsWindow
    }

    private func updatePointerState() {
        guard let ringsWindow = window,
              ringsWindow.isVisible,
              let ringsView = ringsWindow.contentView as? RingsView else {
            window?.ignoresMouseEvents = true
            hideTooltip()
            return
        }
        let mouseScreen = NSEvent.mouseLocation
        let windowPoint = ringsWindow.convertPoint(fromScreen: mouseScreen)
        let viewPoint = ringsView.convert(windowPoint, from: nil)
        guard let ring = ringsView.potion(at: viewPoint) else {
            ringsWindow.ignoresMouseEvents = true
            hideTooltip()
            return
        }

        ringsWindow.ignoresMouseEvents = false
        if usagePopoverController?.isShown == true {
            hideTooltip()
        } else {
            showTooltip(ringsView.usage.tooltip(for: ring), near: mouseScreen)
        }
    }

    private func showTooltip(_ text: String, near point: NSPoint) {
        let tooltip = tooltipWindow ?? makeTooltipWindow()
        let label = tooltip.contentView as? NSTextField
        label?.stringValue = text

        let width: CGFloat = 300
        let height: CGFloat = 64
        label?.frame = NSRect(x: 12, y: 8, width: width - 24, height: height - 16)

        var x = point.x - width - 18
        var y = point.y + 16
        if let screen = screen(containing: point) {
            x = max(x, screen.visibleFrame.minX + 8)
            x = min(x, screen.visibleFrame.maxX - width - 8)
            y = min(y, screen.visibleFrame.maxY - height - 8)
        }

        tooltip.setFrame(NSRect(x: x, y: y, width: width, height: height), display: true)
        tooltip.orderFrontRegardless()
        tooltipWindow = tooltip
    }

    private func hideTooltip() {
        tooltipWindow?.orderOut(nil)
    }

    private func makeTooltipWindow() -> NSWindow {
        let tooltip = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 220, height: 52),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        let label = NSTextField(frame: NSRect(x: 12, y: 8, width: 306, height: 66))
        label.isEditable = false
        label.isBordered = false
        label.drawsBackground = true
        label.backgroundColor = NSColor.black.withAlphaComponent(0.82)
        label.textColor = .white
        label.font = .systemFont(ofSize: 12, weight: .medium)
        label.lineBreakMode = .byWordWrapping
        label.usesSingleLineMode = false
        label.maximumNumberOfLines = 0
        tooltip.contentView = label
        tooltip.backgroundColor = .clear
        tooltip.hasShadow = true
        tooltip.ignoresMouseEvents = true
        tooltip.isOpaque = false
        tooltip.level = .screenSaver
        tooltip.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        return tooltip
    }

    private func screen(containing point: NSPoint) -> NSScreen? {
        NSScreen.screens.first { $0.frame.contains(point) } ?? NSScreen.main
    }

    private func placement(for anchor: Anchor) -> OverlayPlacement? {
        guard let screen = screen(for: anchor) else { return nil }
        let displayX = anchor.displayX ?? 0
        let displayY = anchor.displayY ?? 0
        let localX = anchor.x - displayX
        let localTop = anchor.y - displayY
        let anchorRect = NSRect(
            x: screen.frame.minX + localX,
            y: screen.frame.maxY - localTop - anchor.height,
            width: anchor.width,
            height: anchor.height
        )
        let anchorSize = NSSize(width: anchor.width, height: anchor.height)
        let visible = screen.visibleFrame
        var renderSettings = overlaySettings

        var unitSettings = renderSettings
        unitSettings.scale = 1
        let unitSide = potionOrbDiameter(for: anchorSize, settings: unitSettings) + CGFloat(renderSettings.potionGap)
        let availableForScaledSides = max(0, visible.width - anchor.width - POTION_FRAME_INSET * 2)
        if unitSide > 0 {
            let scaleCap = availableForScaledSides / (unitSide * 2)
            renderSettings.scale = min(renderSettings.scale, max(0.5, Double(scaleCap)))
        }

        let symmetricSideBudget = max(
            0,
            min(anchorRect.minX - visible.minX, visible.maxX - anchorRect.maxX)
        )
        let orbDiameter = potionOrbDiameter(for: anchorSize, settings: renderSettings)
        let scaledGap = potionGap(for: renderSettings)
        let maximumScaledGap = max(0, symmetricSideBudget - orbDiameter - POTION_FRAME_INSET)
        if scaledGap > maximumScaledGap {
            renderSettings.potionGap = Double(maximumScaledGap / max(CGFloat(renderSettings.scale), 0.5))
        }

        let sideReserve = potionSideReserve(for: anchorSize, settings: renderSettings)
        let hudHeight = max(anchor.height, orbDiameter + 14) + POTION_FRAME_INSET * 2
        let width = anchor.width + sideReserve * 2
        let height = hudHeight
        var frame = NSRect(
            x: anchorRect.minX - sideReserve + CGFloat(overlaySettings.horizontalOffset),
            y: anchorRect.midY - hudHeight / 2 + CGFloat(overlaySettings.verticalOffset),
            width: width,
            height: height
        )

        if frame.width <= visible.width {
            frame.origin.x = min(max(frame.minX, visible.minX), visible.maxX - frame.width)
        } else {
            frame.origin.x = visible.minX
        }
        if frame.height <= visible.height {
            frame.origin.y = min(max(frame.minY, visible.minY), visible.maxY - frame.height)
        } else {
            frame.origin.y = visible.minY
        }
        return OverlayPlacement(frame: frame, renderSettings: renderSettings)
    }

    private func screen(for anchor: Anchor) -> NSScreen? {
        if let displayID = anchor.displayID,
           let matched = NSScreen.screens.first(where: {
               ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.intValue == displayID
           }) {
            return matched
        }
        if let displayWidth = anchor.displayWidth,
           let displayHeight = anchor.displayHeight,
           let matched = NSScreen.screens.first(where: {
               abs($0.frame.width - displayWidth) < 1 && abs($0.frame.height - displayHeight) < 1
           }) {
            return matched
        }
        return NSScreen.main ?? NSScreen.screens.first
    }

    private func readAnchor() -> Anchor? {
        guard let root = readStateRoot() else {
            petWindowID = nil
            return nil
        }
        guard
            let bounds = root["electron-avatar-overlay-bounds"] as? [String: Any],
            let anchor = bounds["anchor"] as? [String: Any],
            let x = anchor["x"] as? NSNumber,
            let y = anchor["y"] as? NSNumber,
            let width = anchor["width"] as? NSNumber,
            let height = anchor["height"] as? NSNumber
        else {
            return nil
        }
        guard let open = root["electron-avatar-overlay-open"] as? NSNumber,
              open.boolValue else {
            petWindowID = nil
            return nil
        }

        guard width.doubleValue > 0,
              height.doubleValue > 0,
              let petWindow = visiblePetWindow(matching: bounds) else {
            return nil
        }

        let boundsX = (bounds["x"] as? NSNumber).map { CGFloat(truncating: $0) }
        let boundsY = (bounds["y"] as? NSNumber).map { CGFloat(truncating: $0) }
        let mascot = bounds["mascot"] as? [String: Any]
        let anchorOffsetX = (mascot?["left"] as? NSNumber).map { CGFloat(truncating: $0) }
            ?? boundsX.map { CGFloat(truncating: x) - $0 }
        let anchorOffsetY = (mascot?["top"] as? NSNumber).map { CGFloat(truncating: $0) }
            ?? boundsY.map { CGFloat(truncating: y) - $0 }
        guard let anchorOffsetX, let anchorOffsetY else { return nil }

        let displayBounds = bounds["displayBounds"] as? [String: Any]
        let displayX = displayBounds?["x"] as? NSNumber
        let displayY = displayBounds?["y"] as? NSNumber
        let displayWidth = displayBounds?["width"] as? NSNumber
        let displayHeight = displayBounds?["height"] as? NSNumber
        let displayID = bounds["displayId"] as? NSNumber

        return Anchor(
            x: petWindow.x + anchorOffsetX,
            y: petWindow.y + anchorOffsetY,
            width: CGFloat(truncating: width),
            height: CGFloat(truncating: height),
            displayX: displayX.map { CGFloat(truncating: $0) },
            displayY: displayY.map { CGFloat(truncating: $0) },
            displayWidth: displayWidth.map { CGFloat(truncating: $0) },
            displayHeight: displayHeight.map { CGFloat(truncating: $0) },
            displayID: displayID?.intValue
        )
    }

    private func readStateRoot() -> [String: Any]? {
        guard let values = try? stateURL.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey]),
              let modifiedAt = values.contentModificationDate,
              let size = values.fileSize else {
            cachedStateStamp = nil
            cachedStateRoot = nil
            return nil
        }
        let stamp = StateFileStamp(modifiedAt: modifiedAt, size: size)
        if stamp == cachedStateStamp {
            return cachedStateRoot
        }
        guard let data = try? Data(contentsOf: stateURL),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            cachedStateStamp = nil
            cachedStateRoot = nil
            return nil
        }
        cachedStateStamp = stamp
        cachedStateRoot = root
        return root
    }

    private func visiblePetWindow(matching bounds: [String: Any]) -> PetWindowFrame? {
        guard let expectedWidth = bounds["width"] as? NSNumber,
              let expectedHeight = bounds["height"] as? NSNumber,
              expectedWidth.doubleValue > 0,
              expectedHeight.doubleValue > 0 else {
            return nil
        }

        let expectedX = (bounds["x"] as? NSNumber)?.doubleValue ?? 0
        let expectedY = (bounds["y"] as? NSNumber)?.doubleValue ?? 0
        let candidate: ([String: Any]) -> (CGWindowID, PetWindowFrame, Double)? = { info in
            let owner = (info[kCGWindowOwnerName as String] as? String)?.lowercased() ?? ""
            guard owner == "chatgpt" || owner == "codex" else { return nil }
            guard (info[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0 > 0 else { return nil }
            guard (info[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 0 > 0 else { return nil }
            guard (info[kCGWindowIsOnscreen as String] as? NSNumber)?.boolValue == true else { return nil }
            guard let frame = info[kCGWindowBounds as String] as? [String: Any],
                  let number = info[kCGWindowNumber as String] as? NSNumber,
                  let x = frame["X"] as? NSNumber,
                  let y = frame["Y"] as? NSNumber,
                  let width = frame["Width"] as? NSNumber,
                  let height = frame["Height"] as? NSNumber,
                  abs(width.doubleValue - expectedWidth.doubleValue) <= 4,
                  abs(height.doubleValue - expectedHeight.doubleValue) <= 4 else {
                return nil
            }
            let distance = hypot(x.doubleValue - expectedX, y.doubleValue - expectedY)
            return (
                number.uint32Value,
                PetWindowFrame(
                    x: CGFloat(truncating: x),
                    y: CGFloat(truncating: y)
                ),
                distance
            )
        }

        if let petWindowID,
           let info = (CGWindowListCopyWindowInfo(
               [.optionIncludingWindow, .excludeDesktopElements],
               petWindowID
           ) as? [[String: Any]])?.first,
           let match = candidate(info) {
            return match.1
        }

        petWindowID = nil
        guard let windowList = CGWindowListCopyWindowInfo(
            [.optionOnScreenOnly, .excludeDesktopElements],
            kCGNullWindowID
        ) as? [[String: Any]],
              let match = windowList.compactMap(candidate).min(by: { $0.2 < $1.2 }) else {
            return nil
        }
        petWindowID = match.0
        return match.1
    }

    private func readUsage() -> LimitUsage {
        let now = Date()
        if let usage = lastLiveUsage,
           let fetchedAt = lastLiveUsageFetch,
           now.timeIntervalSince(fetchedAt) < LIVE_USAGE_REFRESH_INTERVAL {
            return usage
        }

        startLiveUsageRefreshIfNeeded()

        if now.timeIntervalSince(cachedUsageReadAt) >= CACHED_USAGE_REFRESH_INTERVAL {
            if let cachedUsage = readCachedUsage() {
                cachedUsageSnapshot = cachedUsage
                lastCachedUsageFetch = now
            }
            cachedUsageReadAt = now
        }
        if let usage = cachedUsageSnapshot {
            return usage
        }
        if let usage = lastLiveUsage,
           let fetchedAt = lastLiveUsageFetch,
           now.timeIntervalSince(fetchedAt) < LIVE_USAGE_STALE_FALLBACK_INTERVAL {
            return usage
        }
        return emptyUsage()
    }

    private func startLiveUsageRefreshIfNeeded(force: Bool = false) {
        let now = Date()
        guard !liveUsageFetchInFlight else { return }
        guard force || lastLiveUsageAttempt == nil || now.timeIntervalSince(lastLiveUsageAttempt!) >= LIVE_USAGE_RETRY_INTERVAL else {
            return
        }

        lastLiveUsageAttempt = now
        liveUsageFetchInFlight = true
        lastLiveUsageError = nil
        updateUsagePopoverIfShown()
        statusItem?.menu = makeStatusMenu()
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let result = self?.readLiveUsage() ?? .failure("internal")
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.liveUsageFetchInFlight = false
                switch result {
                case .success(let usage):
                    self.lastLiveUsage = usage
                    self.lastLiveUsageFetch = Date()
                    self.lastLiveUsageError = nil
                    self.updateWindow()
                case .failure(let reason):
                    self.lastLiveUsageError = reason
                    NSLog("CodexPetLimitRings live usage refresh failed reason=%@", reason)
                    self.updateWindow()
                }
                self.statusItem?.menu = self.makeStatusMenu()
            }
        }
    }

    private func readLiveUsage() -> LiveUsageFetchResult {
        guard
            let token = readAccessToken()
        else {
            return .failure("auth")
        }

        var request = URLRequest(url: usageURL)
        request.httpMethod = "GET"
        request.timeoutInterval = 6
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        let semaphore = DispatchSemaphore(value: 0)
        var resultData: Data?
        var resultResponse: URLResponse?
        var resultError: Error?
        usageSession.dataTask(with: request) { data, response, error in
            resultData = data
            resultResponse = response
            resultError = error
            semaphore.signal()
        }.resume()

        guard semaphore.wait(timeout: .now() + 7) == .success else {
            return .failure("timeout")
        }
        if resultError != nil {
            return .failure("network")
        }
        guard let http = resultResponse as? HTTPURLResponse else {
            return .failure("response")
        }
        guard (200..<300).contains(http.statusCode) else {
            return .failure("HTTP \(http.statusCode)")
        }
        guard let data = resultData else {
            return .failure("empty")
        }
        let payload: UsagePayload
        do {
            payload = try JSONDecoder().decode(UsagePayload.self, from: data)
        } catch {
            return .failure("decode")
        }

        guard let usage = usage(
            from: payload.rate_limit,
            additional: payload.additional_rate_limits,
            source: "live"
        ) else {
            return .failure("missing")
        }
        return .success(usage)
    }

    private func readAccessToken() -> String? {
        guard
            let data = try? Data(contentsOf: authURL),
            let payload = try? JSONDecoder().decode(AuthPayload.self, from: data),
            let token = payload.tokens?.access_token,
            !token.isEmpty
        else {
            return nil
        }
        return token
    }

    private func readCachedUsage() -> LimitUsage? {
        guard FileManager.default.fileExists(atPath: logsURL.path) else {
            return nil
        }

        var db: OpaquePointer?
        guard sqlite3_open_v2(logsURL.path, &db, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK, let db else {
            return nil
        }
        defer { sqlite3_close(db) }

        let sql = """
        SELECT feedback_log_body
        FROM logs INDEXED BY idx_logs_ts
        ORDER BY ts DESC, ts_nanos DESC, id DESC
        LIMIT 2000
        """

        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            return nil
        }
        defer { sqlite3_finalize(statement) }

        while sqlite3_step(statement) == SQLITE_ROW {
            guard let cText = sqlite3_column_text(statement, 0) else { continue }
            let body = String(cString: cText)
            guard body.contains(#""type":"codex.rate_limits""#),
                  let json = extractRateLimitJSON(from: body),
                  let data = json.data(using: .utf8),
                  let payload = try? JSONDecoder().decode(EventPayload.self, from: data) else {
                continue
            }
            return usage(
                from: payload.rate_limits,
                additional: payload.additional_rate_limits,
                source: "log"
            )
        }
        return nil
    }

    private func extractRateLimitJSON(from body: String) -> String? {
        guard let start = body.range(of: "{\"type\":\"codex.rate_limits\"")?.lowerBound else {
            return nil
        }

        var depth = 0
        var inString = false
        var escaping = false
        var endIndex: String.Index?
        var index = start

        while index < body.endIndex {
            let char = body[index]
            if inString {
                if escaping {
                    escaping = false
                } else if char == "\\" {
                    escaping = true
                } else if char == "\"" {
                    inString = false
                }
            } else if char == "\"" {
                inString = true
            } else if char == "{" {
                depth += 1
            } else if char == "}" {
                depth -= 1
                if depth == 0 {
                    endIndex = body.index(after: index)
                    break
                }
            }
            index = body.index(after: index)
        }

        guard let endIndex else {
            return nil
        }
        return String(body[start..<endIndex])
    }

    private func usage(
        from payload: RatePayload?,
        additional: AdditionalRateLimits?,
        source: String
    ) -> LimitUsage? {
        let selectedPayload = selectedRatePayload(from: payload, additional: additional)
        let primary = selectedPayload?.primary ?? selectedPayload?.primary_window
        let secondary = selectedPayload?.secondary ?? selectedPayload?.secondary_window
        guard primary?.used_percent != nil || secondary?.used_percent != nil else {
            return nil
        }

        return LimitUsage(
            primaryPercent: primary?.used_percent,
            secondaryPercent: secondary?.used_percent,
            primaryReset: primary?.reset_at,
            secondaryReset: secondary?.reset_at,
            source: source
        )
    }

    private func selectedRatePayload(
        from payload: RatePayload?,
        additional: AdditionalRateLimits?
    ) -> RatePayload? {
        if let activeModelPayload = additionalPayload(for: activeUsageModel(), in: additional) {
            return activeModelPayload
        }
        if hasUsageData(payload) {
            return payload
        }
        return additionalPayload(for: "spark", in: additional)
    }

    private func additionalPayload(for model: String?, in additional: AdditionalRateLimits?) -> RatePayload? {
        guard let model, let additional else {
            return nil
        }
        let target = normalizedModelName(model)
        if let exact = additional.entries.first(where: { matchesAdditional($0, target: target) }) {
            return exact.payload
        }
        guard target.contains("spark") else {
            return nil
        }
        return additional.entries.first { entry in
            additionalNames(entry).contains { normalizedModelName($0).contains("spark") }
        }?.payload
    }

    private func matchesAdditional(_ entry: AdditionalRateLimit, target: String) -> Bool {
        additionalNames(entry).contains { name in
            let candidate = normalizedModelName(name)
            return !candidate.isEmpty && (candidate == target || candidate.contains(target) || target.contains(candidate))
        }
    }

    private func additionalNames(_ entry: AdditionalRateLimit) -> [String] {
        [entry.name, entry.meteredFeature].compactMap { $0 }
    }

    private func hasUsageData(_ payload: RatePayload?) -> Bool {
        let primary = payload?.primary ?? payload?.primary_window
        let secondary = payload?.secondary ?? payload?.secondary_window
        return primary?.used_percent != nil || secondary?.used_percent != nil
    }

    private func activeUsageModel() -> String? {
        let now = Date()
        guard now.timeIntervalSince(activeModelCheckedAt) >= 2 else {
            return activeModelCache
        }
        activeModelCheckedAt = now
        activeModelCache = latestCodexModelFromLogs() ?? configuredCodexModel()
        return activeModelCache
    }

    private func latestCodexModelFromLogs() -> String? {
        guard FileManager.default.fileExists(atPath: logsURL.path) else {
            return nil
        }

        var db: OpaquePointer?
        guard sqlite3_open_v2(logsURL.path, &db, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK, let db else {
            return nil
        }
        defer { sqlite3_close(db) }

        let sql = """
        SELECT feedback_log_body
        FROM logs INDEXED BY idx_logs_ts
        ORDER BY ts DESC, ts_nanos DESC, id DESC
        LIMIT 2000
        """

        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            return nil
        }
        defer { sqlite3_finalize(statement) }

        while sqlite3_step(statement) == SQLITE_ROW {
            guard let cText = sqlite3_column_text(statement, 0) else { continue }
            let body = String(cString: cText)
            guard body.contains("run_sampling_request"), body.contains("model=") else { continue }
            return modelName(in: body)
        }
        return nil
    }

    private func configuredCodexModel() -> String? {
        guard let config = try? String(contentsOf: configURL, encoding: .utf8) else {
            return nil
        }
        let pattern = #"(?m)^\s*model\s*=\s*"([^"]+)""#
        guard
            let regex = try? NSRegularExpression(pattern: pattern),
            let match = regex.firstMatch(
                in: config,
                range: NSRange(config.startIndex..<config.endIndex, in: config)
            ),
            let range = Range(match.range(at: 1), in: config)
        else {
            return nil
        }
        return String(config[range])
    }

    private func modelName(in body: String) -> String? {
        let pattern = #"model=([A-Za-z0-9._:-]+)"#
        guard
            let regex = try? NSRegularExpression(pattern: pattern),
            let match = regex.firstMatch(
                in: body,
                range: NSRange(body.startIndex..<body.endIndex, in: body)
            ),
            let range = Range(match.range(at: 1), in: body)
        else {
            return nil
        }
        return String(body[range])
    }

    private func normalizedModelName(_ model: String) -> String {
        model
            .lowercased()
            .filter { $0.isLetter || $0.isNumber }
    }

    private func emptyUsage() -> LimitUsage {
        LimitUsage(primaryPercent: nil, secondaryPercent: nil, primaryReset: nil, secondaryReset: nil, source: "none")
    }
}

private final class SettingsWindowController: NSObject, WKScriptMessageHandler, WKNavigationDelegate, NSWindowDelegate {
    private let assetDirectory: URL
    private let onSave: (OverlaySettings) -> Void
    private let onCleanup: (@escaping (CleanupResult) -> Void) -> Void
    private let onClose: () -> Void
    private var window: NSWindow?
    private var webView: WKWebView?
    private var latestSettings = OverlaySettings.defaults
    private var latestCleanup = CleanupResult(freedBytes: 0, cleanedAt: 0, nextCleanupAt: nil)
    private var pageLoaded = false

    init(
        assetDirectory: URL,
        onSave: @escaping (OverlaySettings) -> Void,
        onCleanup: @escaping (@escaping (CleanupResult) -> Void) -> Void,
        onClose: @escaping () -> Void
    ) {
        self.assetDirectory = assetDirectory
        self.onSave = onSave
        self.onCleanup = onCleanup
        self.onClose = onClose
    }

    func show(settings: OverlaySettings, cleanup: CleanupResult) {
        latestSettings = settings
        latestCleanup = cleanup
        if window == nil {
            makeWindow()
        }
        update(settings: settings, cleanup: cleanup)
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }

    func update(settings: OverlaySettings, cleanup: CleanupResult) {
        latestSettings = settings
        latestCleanup = cleanup
        guard pageLoaded, let webView else { return }

        struct PageState: Codable {
            let settings: OverlaySettings
            let cleanup: CleanupResult
        }
        guard let data = try? JSONEncoder().encode(PageState(settings: settings, cleanup: cleanup)),
              let json = String(data: data, encoding: .utf8) else {
            return
        }
        webView.evaluateJavaScript("window.applyNativeState(\(json))")
    }

    func close() {
        window?.close()
    }

    private func makeWindow() {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        configuration.userContentController.add(self, name: "settings")

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = self

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 980, height: 680),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "포션 펫 설정"
        window.minSize = NSSize(width: 820, height: 600)
        window.contentView = webView
        window.delegate = self
        window.isReleasedWhenClosed = false

        self.webView = webView
        self.window = window
        let indexURL = assetDirectory.appendingPathComponent("index.html")
        webView.loadFileURL(indexURL, allowingReadAccessTo: assetDirectory.deletingLastPathComponent())
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        pageLoaded = true
        update(settings: latestSettings, cleanup: latestCleanup)
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "settings",
              let body = message.body as? [String: Any],
              let type = body["type"] as? String else {
            return
        }

        switch type {
        case "ready":
            update(settings: latestSettings, cleanup: latestCleanup)
        case "save":
            guard let object = body["settings"],
                  JSONSerialization.isValidJSONObject(object),
                  let data = try? JSONSerialization.data(withJSONObject: object),
                  let settings = try? JSONDecoder().decode(OverlaySettings.self, from: data) else {
                return
            }
            onSave(settings)
        case "reset":
            onSave(.defaults)
        case "cleanup":
            onCleanup { [weak self] result in
                guard let self else { return }
                self.latestCleanup = result
                self.update(settings: self.latestSettings, cleanup: result)
                self.webView?.evaluateJavaScript("window.cleanupFinished?.()")
            }
        case "close":
            close()
        default:
            break
        }
    }

    func windowWillClose(_ notification: Notification) {
        webView?.stopLoading()
        webView?.configuration.userContentController.removeScriptMessageHandler(forName: "settings")
        webView?.navigationDelegate = nil
        webView = nil
        window = nil
        pageLoaded = false
        onClose()
    }
}

// MARK: - Entry Point

let app = NSApplication.shared
private let delegate = RingsApp()
app.delegate = delegate
app.run()
