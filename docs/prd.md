# Sanduhr fur Claude - Product Requirements Document

## Product Overview
A standalone desktop widget that shows Claude.ai subscription usage with pacing indicators, burn-rate projections, and sparklines. Distributed as a single Python file via GitHub for power users who live in Claude all day.

## User Stories

### US-1: View Current Usage
**As a** Claude power user
**I want to** see my subscription usage at a glance without opening a browser
**So that** I can pace myself and avoid hitting limits unexpectedly

**Acceptance Criteria:**
- Shows all active usage tiers (Session, Weekly All Models, Sonnet, Opus, Cowork, Routines)
- Displays utilization as whole-number percentages
- Color-coded: green (<50%), yellow (50-75%), orange (75-90%), red (>90%)
- Hides tiers that are null/inactive

### US-2: Understand Pacing
**As a** user who needs to manage weekly limits
**I want to** know if I'm ahead, behind, or on pace
**So that** I can adjust my usage behavior

**Acceptance Criteria:**
- Linear pace calculation comparing elapsed time fraction to usage fraction
- Text indicator: "On pace" / "X% ahead" / "X% under"
- Visual pace marker on the progress bar showing the "on pace" position
- Burn rate projection warns if usage will exhaust before reset

### US-3: Know When Limits Reset
**As a** user hitting usage limits
**I want to** see both a countdown and the actual reset date/time
**So that** I can plan when I'll have access again

**Acceptance Criteria:**
- Countdown: "3d 5h 44m" format
- Date/time: "Sun 1:00 AM" or "Tomorrow 3:00 PM" in local timezone

### US-4: Track Usage Trends
**As a** frequent user
**I want to** see a sparkline of my recent usage velocity
**So that** I can tell if I'm accelerating or steady

**Acceptance Criteria:**
- Tiny inline sparkline in each tier card header
- Last 24 data points (2 hours at 5-min refresh)
- Persisted to disk so trends survive restarts

### US-5: Customize Appearance
**As a** user with aesthetic preferences
**I want to** choose from multiple themes
**So that** the widget fits my desktop environment

**Acceptance Criteria:**
- 5 themes: Obsidian, Aurora, Ember, Mint, Matrix
- Theme strip below title bar with clickable buttons
- Active theme persisted between sessions

### US-6: Compact Mode
**As a** user with limited screen space
**I want to** collapse the widget to show only the most critical tier
**So that** it takes minimal space when I just need a quick check

**Acceptance Criteria:**
- Double-click title bar toggles compact/full
- Compact shows only the highest-utilization tier
- Footer shows "Compact" mode indicator

### US-7: Quick Access to Sonnet
**As a** user who wants to conserve expensive model usage
**I want to** quickly open Claude with Sonnet selected
**So that** I can use the cheaper model when appropriate

**Acceptance Criteria:**
- "Use Sonnet" link in footer
- Opens claude.ai/new with Sonnet model preselected

## Non-Functional Requirements

- **Single file**: Entire app is one Python file, no build step
- **Auto-install deps**: cloudscraper installs itself on first run
- **Session key storage**: ~/.claude-usage-widget/config.json (outside repo)
- **Refresh interval**: 5 minutes (API), 30 seconds (countdown timers)
- **No flicker**: Graceful in-place UI updates, no destroy/recreate
- **Cross-platform**: Works on Windows, macOS, Linux (tkinter is stdlib)
