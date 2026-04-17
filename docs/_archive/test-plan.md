# Test Plan — Sanduhr für Claude

## Scope

Sanduhr is a single-file desktop widget with no test framework. Testing is manual and visual. This plan documents what to verify and how.

## Test Environment

- Python 3.8+ on Windows (primary), macOS, or Linux
- A valid Claude Pro/Team/Enterprise session key
- The widget running against live claude.ai API

## Functional Tests

### F1: First Launch Setup
| Step | Expected |
|------|----------|
| Run `python sanduhr.py` with no config file | Setup dialog appears explaining how to get session key |
| Paste a valid session key | Widget appears, usage bars populate within seconds |
| Paste an invalid key | "Session expired" or HTTP error shown in status area |

### F2: Usage Display
| Step | Expected |
|------|----------|
| Launch with valid key | All active tiers display with percentage, colored bar, reset countdown |
| Tier at 0% usage | Bar shows minimal sliver, percentage reads "0%" |
| Tier at 90%+ | Bar is red, percentage is red |
| Tier with null utilization | Tier is hidden entirely |

### F3: Pacing Engine
| Step | Expected |
|------|----------|
| Usage within 5% of linear pace | "On pace" in green |
| Usage > 5% above pace | "X% ahead" in orange |
| Usage > 5% below pace | "X% under" in blue |
| Pace marker on bar | Colored tick at the expected position for current time |

### F4: Burn Rate Projection
| Step | Expected |
|------|----------|
| Usage rate would exhaust before reset | Warning: "At current pace, expires in Xh Ym" in red |
| Usage rate would NOT exhaust before reset | No burn rate warning shown |
| Usage at 0% | No burn rate warning |

### F5: Reset Countdown
| Step | Expected |
|------|----------|
| Any active tier | Shows "Resets in Xd Xh Xm" and date like "Sun 1:00 AM" |
| Wait 30 seconds | Countdown updates without full refresh |
| Reset imminent | Shows "Resets in 0m" then "now" |

### F6: Sparklines
| Step | Expected |
|------|----------|
| First launch (no history) | No sparkline rendered (needs 2+ data points) |
| After 2+ refreshes (10+ min) | Sparkline appears showing trend |
| Restart widget | Sparkline persists from history.json |

### F7: Themes
| Step | Expected |
|------|----------|
| Click each theme button | UI recolors completely — background, text, bars, accents |
| Restart widget | Last selected theme persists |
| All 5 themes render | No missing colors, no unreadable text |

### F8: Compact Mode
| Step | Expected |
|------|----------|
| Double-click title bar | Collapses to show only highest-usage tier |
| Double-click again | Expands to show all tiers |
| Footer in compact mode | Shows "Compact" indicator |

### F9: Window Controls
| Step | Expected |
|------|----------|
| Drag title bar | Widget moves freely |
| Click Pin | Toggles always-on-top (Pin text dims when unpinned) |
| Click X | Widget closes |
| Click Refresh | Status shows "Refreshing..." then updates data |
| Click Key | Session key dialog appears |
| Click "Use Sonnet" | Browser opens claude.ai with Sonnet selected |

### F10: Extra Usage
| Step | Expected |
|------|----------|
| Account with overage enabled | "Extra Usage" card shows spend amount |
| Account without overage | No extra usage card |

## Error Handling Tests

### E1: Session Expiration
| Step | Expected |
|------|----------|
| Log out of claude.ai while widget is running | Next refresh shows "Session expired - click Key" |
| Click Key and paste new key | Widget recovers and shows fresh data |

### E2: Network Failure
| Step | Expected |
|------|----------|
| Disconnect network while widget is running | Next refresh shows error message (truncated to 60 chars) |
| Reconnect network | Next auto-refresh recovers |

### E3: No Active Tiers
| Step | Expected |
|------|----------|
| API returns no utilization data | Status shows "No active tiers" |

## Regression Checklist

Run through before any release:

- [ ] F1: First launch setup flow works
- [ ] F2: All active tiers display correctly
- [ ] F3: Pacing labels and markers are accurate
- [ ] F4: Burn rate only warns when appropriate
- [ ] F5: Countdown ticks every 30s
- [ ] F6: Sparklines render after 2+ data points
- [ ] F7: All 5 themes render without visual issues
- [ ] F8: Compact mode toggles cleanly
- [ ] F9: All window controls work
- [ ] E1: Session key recovery works
