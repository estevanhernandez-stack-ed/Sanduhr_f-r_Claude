import Foundation

/// Decoded body of `GET /api/organizations/{id}/usage`.
/// Keyed by `Tier` so we can iterate in display order without caring which
/// fields the server included.
struct UsageResponse {
    var tiers: [Tier: TierUsage] = [:]
    var extraUsage: ExtraUsage?
}

struct TierUsage: Decodable, Equatable {
    let utilization: Double?    // 0–100 (server may send Int or Double)
    let resetsAt: String?       // ISO-8601, e.g. "2026-04-15T18:00:00Z"

    enum CodingKeys: String, CodingKey {
        case utilization
        case resetsAt = "resets_at"
    }
}

struct ExtraUsage: Decodable, Equatable {
    let isEnabled: Bool
    let usedCredits: Double?
    let monthlyLimit: Double?

    enum CodingKeys: String, CodingKey {
        case isEnabled    = "is_enabled"
        case usedCredits  = "used_credits"
        case monthlyLimit = "monthly_limit"
    }
}

extension UsageResponse: Decodable {
    /// We use a dynamic container because the JSON has one key per tier plus
    /// an `extra_usage` object, rather than a nested array we can Codable-map
    /// with a single CodingKeys enum. Any unknown tier keys are ignored.
    private struct DynKey: CodingKey {
        let stringValue: String
        init(stringValue: String) { self.stringValue = stringValue }
        var intValue: Int? { nil }
        init?(intValue: Int) { nil }
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: DynKey.self)
        var tiers: [Tier: TierUsage] = [:]
        for key in c.allKeys {
            if let tier = Tier(rawValue: key.stringValue) {
                if let t = try? c.decode(TierUsage.self, forKey: key) {
                    tiers[tier] = t
                }
            }
        }
        self.tiers = tiers
        self.extraUsage = try? c.decode(ExtraUsage.self,
            forKey: DynKey(stringValue: "extra_usage"))
    }
}

/// Minimal shape of `GET /api/organizations` — we only need the first uuid.
struct Organization: Decodable {
    let uuid: String
}
