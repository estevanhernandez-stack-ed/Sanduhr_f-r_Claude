"""
Sanduhr fur Claude - AI Usage Tracker
Glassmorphism-styled always-on-top desktop widget showing Claude.ai
subscription usage with burn-rate projections, pace markers, and sparklines.

"Sanduhr" is German for "hourglass" - watch your usage sand drain.
"fur" = "for" in German.

Setup: python sanduhr.py
       Paste your sessionKey on first run (F12 > Application > Cookies > claude.ai)

Author: 626 Labs LLC | 626labs.dev | github.com/626Labs-LLC
License: MIT
"""

import tkinter as tk
from tkinter import messagebox, simpledialog
import json, sys, threading, webbrowser, collections
from datetime import datetime, timezone
from pathlib import Path

try:
    import cloudscraper
except ImportError:
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install",
                           "cloudscraper", "--quiet", "--break-system-packages"])
    import cloudscraper
import requests

# ── Config ────────────────────────────────────────────────────────────────

CONFIG_DIR = Path.home() / ".claude-usage-widget"
CONFIG_FILE = CONFIG_DIR / "config.json"
HISTORY_FILE = CONFIG_DIR / "history.json"
API_BASE = "https://claude.ai/api"
REFRESH_MS = 5 * 60 * 1000
COUNTDOWN_MS = 30_000
WIDTH = 420
BAR_HEIGHT = 16
MAX_HISTORY = 24  # sparkline data points (24 x 5min = 2 hours)

TIER_LABELS = {
    "five_hour":           "Session (5hr)",
    "seven_day":           "Weekly - All Models",
    "seven_day_sonnet":    "Weekly - Sonnet",
    "seven_day_opus":      "Weekly - Opus",
    "seven_day_cowork":    "Weekly - Cowork",
    "seven_day_omelette":  "Weekly - Routines",
    "seven_day_oauth_apps":"Weekly - OAuth Apps",
    "iguana_necktie":      "Weekly - Special",
}

THEMES = {
    "obsidian": {
        "name": "Obsidian", "bg": "#0d0d0d", "glass": "#1c1c1c",
        "title_bg": "#161616", "border": "#333333", "text": "#e8e4dc",
        "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
        "accent": "#6c63ff", "bar_bg": "#2a2a2a", "footer_bg": "#111111",
        "pace_marker": "#ff6b6b", "sparkline": "#6c63ff",
    },
    "aurora": {
        "name": "Aurora", "bg": "#0a0f1a", "glass": "#161e30",
        "title_bg": "#0f172a", "border": "#334155", "text": "#e2e8f0",
        "text_secondary": "#94a3b8", "text_dim": "#64748b", "text_muted": "#475569",
        "accent": "#38bdf8", "bar_bg": "#1e293b", "footer_bg": "#0c1220",
        "pace_marker": "#f472b6", "sparkline": "#38bdf8",
    },
    "ember": {
        "name": "Ember", "bg": "#1a0a0a", "glass": "#261414",
        "title_bg": "#1f0e0e", "border": "#442222", "text": "#f5e6e0",
        "text_secondary": "#d4a89c", "text_dim": "#8b6b60", "text_muted": "#6b4b40",
        "accent": "#f97316", "bar_bg": "#2d1a1a", "footer_bg": "#150808",
        "pace_marker": "#fbbf24", "sparkline": "#f97316",
    },
    "mint": {
        "name": "Mint", "bg": "#0a1a14", "glass": "#122a1e",
        "title_bg": "#0c1f14", "border": "#22543d", "text": "#e0f5ec",
        "text_secondary": "#9cd4b8", "text_dim": "#5a9a78", "text_muted": "#3a7a58",
        "accent": "#34d399", "bar_bg": "#163020", "footer_bg": "#081510",
        "pace_marker": "#f472b6", "sparkline": "#34d399",
    },
    "matrix": {
        "name": "Matrix", "bg": "#020a02", "glass": "#0a140a",
        "title_bg": "#040d04", "border": "#0f2a0f", "text": "#00ff41",
        "text_secondary": "#00cc33", "text_dim": "#00802b", "text_muted": "#005a1e",
        "accent": "#00ff41", "bar_bg": "#0a1a0a", "footer_bg": "#020802",
        "pace_marker": "#ff0040", "sparkline": "#00ff41",
    },
}


def usage_color(pct):
    if pct < 50: return "#4ade80"
    if pct < 75: return "#facc15"
    if pct < 90: return "#fb923c"
    return "#f87171"

def load_config():
    if CONFIG_FILE.exists():
        try:
            with open(CONFIG_FILE) as f: return json.load(f)
        except: return {}
    return {}

def save_config(cfg):
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    with open(CONFIG_FILE, "w") as f: json.dump(cfg, f, indent=2)


# ── History (for sparklines) ──────────────────────────────────────────────

def load_history():
    if HISTORY_FILE.exists():
        try:
            with open(HISTORY_FILE) as f: return json.load(f)
        except: return {}
    return {}

def save_history(h):
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    with open(HISTORY_FILE, "w") as f: json.dump(h, f)

def append_history(tier_key, util):
    h = load_history()
    if tier_key not in h: h[tier_key] = []
    h[tier_key].append({"t": datetime.now(timezone.utc).isoformat(), "v": util})
    h[tier_key] = h[tier_key][-MAX_HISTORY:]
    save_history(h)
    return h[tier_key]


# ── API ───────────────────────────────────────────────────────────────────

class ClaudeAPI:
    def __init__(self, session_key):
        self.session_key = session_key
        self.s = cloudscraper.create_scraper()
        self.s.headers["Accept"] = "application/json"
        self.org_id = None

    def _get(self, url):
        return self.s.get(url, headers={"Cookie": f"sessionKey={self.session_key}"}, timeout=15)

    def get_usage(self):
        if not self.org_id:
            r = self._get(f"{API_BASE}/organizations"); r.raise_for_status()
            orgs = r.json()
            if not orgs: raise ValueError("No orgs found")
            self.org_id = orgs[0]["uuid"]
        r = self._get(f"{API_BASE}/organizations/{self.org_id}/usage"); r.raise_for_status()
        return r.json()


# ── Helpers ───────────────────────────────────────────────────────────────

def time_until(iso_str):
    if not iso_str: return "--"
    try: rd = datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
    except: return "--"
    s = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    if s <= 0: return "now"
    d, r = divmod(int(s), 86400); h, r = divmod(r, 3600); m = r // 60
    p = []
    if d: p.append(f"{d}d")
    if h: p.append(f"{h}h")
    if m or not p: p.append(f"{m}m")
    return " ".join(p)

def reset_datetime_str(iso_str):
    if not iso_str: return ""
    try: rd = datetime.fromisoformat(iso_str.replace("Z", "+00:00"))
    except: return ""
    loc = rd.astimezone()
    now = datetime.now().astimezone()
    days = (loc.date() - now.date()).days
    t = loc.strftime("%I:%M %p").lstrip("0")
    if days <= 0: return f"Today {t}"
    if days == 1: return f"Tomorrow {t}"
    if days < 7: return f"{loc.strftime('%a')} {t}"
    return f"{loc.strftime('%a %b %d')} {t}"

def pace_frac(resets_at, tier_key):
    if not resets_at: return None
    try: rd = datetime.fromisoformat(resets_at.replace("Z", "+00:00"))
    except: return None
    rem = max(0, (rd - datetime.now(timezone.utc)).total_seconds())
    tot = 5*3600 if tier_key == "five_hour" else 7*86400
    return min(1.0, max(0.0, (tot - rem) / tot)) if tot > 0 else None

def pace_info(util, resets_at, tier_key):
    f = pace_frac(resets_at, tier_key)
    if f is None or util is None: return None
    diff = util - f * 100
    if abs(diff) < 5: return ("On pace",              "#4ade80")
    if diff > 0:      return (f"{int(abs(diff))}% ahead", "#fb923c")
    return                    (f"{int(abs(diff))}% under", "#60a5fa")

def burn_projection(util, resets_at, tier_key):
    """Compare when you'd hit 100% vs when the limit resets.
    Returns (message, color) or None if no risk."""
    f = pace_frac(resets_at, tier_key)
    if f is None or util is None or util <= 0 or f <= 0: return None
    rate_per_frac = util / f  # projected usage% over full period
    if rate_per_frac <= 100: return None  # won't hit 100%

    try: rd = datetime.fromisoformat(resets_at.replace("Z", "+00:00"))
    except: return None
    now = datetime.now(timezone.utc)
    secs_until_reset = max(0, (rd - now).total_seconds())
    tot = 5*3600 if tier_key == "five_hour" else 7*86400

    frac_at_100 = 100 / rate_per_frac
    secs_until_100 = max(0, (frac_at_100 - f) * tot)

    if secs_until_100 <= 0:
        return ("Limit reached", "#f87171")
    if secs_until_100 >= secs_until_reset:
        return None  # resets before hitting limit — no warning needed

    # Will hit limit BEFORE reset — tell them when at current pace
    d, r = divmod(int(secs_until_100), 86400); h, r = divmod(r, 3600); m = r // 60
    parts = []
    if d: parts.append(f"{d}d")
    if h: parts.append(f"{h}h")
    if m or not parts: parts.append(f"{m}m")
    t_str = " ".join(parts)
    return (f"At current pace, expires in {t_str}", "#f87171")


# ── Sparkline Canvas ──────────────────────────────────────────────────────

def draw_sparkline(canvas, values, color, bg):
    """Draw a tiny sparkline on a tk.Canvas."""
    canvas.delete("all")
    w = canvas.winfo_width()
    h = canvas.winfo_height()
    if w < 10 or h < 4 or len(values) < 2: return

    mn = min(values)
    mx = max(values)
    rng = mx - mn if mx != mn else 1

    points = []
    for i, v in enumerate(values):
        x = (i / (len(values) - 1)) * w
        y = h - ((v - mn) / rng) * (h - 2) - 1
        points.append((x, y))

    # Draw line
    for i in range(len(points) - 1):
        canvas.create_line(points[i][0], points[i][1],
                           points[i+1][0], points[i+1][1],
                           fill=color, width=1.5, smooth=True)


# ── Widget ────────────────────────────────────────────────────────────────

class UsageWidget:
    def __init__(self):
        cfg = load_config()
        self.theme_key = cfg.get("theme", "obsidian")
        self.t = THEMES[self.theme_key]
        self.compact = False

        self.root = tk.Tk()
        self.root.title("Sanduhr f\u00fcr Claude")
        self.root.attributes("-topmost", True)
        self.root.attributes("-alpha", 0.94)
        self.root.resizable(False, False)
        self.root.overrideredirect(True)

        sw = self.root.winfo_screenwidth()
        sh = self.root.winfo_screenheight()
        self.root.geometry(f"+{max(0, sw - WIDTH - 24)}+{max(0, sh - 580)}")

        self.api = None
        self.usage_data = None
        self.last_updated = None
        self.pinned = True
        self.drag_xy = (0, 0)
        self.tier_widgets = {}
        self.extra_widget = None

        self._build()
        self._setup_auth()

    def _build(self):
        t = self.t
        self.root.configure(bg=t["border"])
        self.outer = tk.Frame(self.root, bg=t["border"], padx=1, pady=1)
        self.outer.pack(fill="both", expand=True)
        self.inner = tk.Frame(self.outer, bg=t["bg"])
        self.inner.pack(fill="both", expand=True)

        # Accent line
        tk.Frame(self.inner, bg=t["accent"], height=2).pack(fill="x")

        # Title bar
        tb = tk.Frame(self.inner, bg=t["title_bg"], height=34)
        tb.pack(fill="x"); tb.pack_propagate(False)
        for ev in ("<Button-1>", "<B1-Motion>"):
            tb.bind("<Button-1>", self._ds)
            tb.bind("<B1-Motion>", self._dm)
        tb.bind("<Double-Button-1>", lambda e: self._toggle_compact())

        lbl = tk.Label(tb, text="Sanduhr f\u00fcr Claude", font=("Segoe UI Semibold", 10),
                       bg=t["title_bg"], fg=t["text"], padx=10)
        lbl.pack(side="left")
        lbl.bind("<Button-1>", self._ds)
        lbl.bind("<B1-Motion>", self._dm)
        lbl.bind("<Double-Button-1>", lambda e: self._toggle_compact())

        bc = dict(font=("Segoe UI", 8), bg=t["title_bg"], bd=0,
                  activebackground=t["glass"], cursor="hand2", padx=5)

        self.pin_btn = tk.Button(tb, text="Pin", fg=t["text"], command=self._toggle_pin, **bc)
        self.pin_btn.pack(side="right")
        tk.Button(tb, text="X", fg="#f87171", command=self.root.destroy, **bc).pack(side="right")
        tk.Button(tb, text="Refresh", fg=t["text_dim"], command=self._refresh_async, **bc).pack(side="right")
        tk.Button(tb, text="Key", fg=t["text_dim"], command=self._show_settings, **bc).pack(side="right")

        # Theme strip — its own row below the title bar
        theme_strip = tk.Frame(self.inner, bg=t["glass"], height=26)
        theme_strip.pack(fill="x")
        theme_strip.pack_propagate(False)

        keys = list(THEMES.keys())
        for tk_key in keys:
            th = THEMES[tk_key]
            is_active = tk_key == self.theme_key
            btn = tk.Button(theme_strip, text=th["name"],
                            font=("Segoe UI", 8, "bold" if is_active else ""),
                            fg=th["accent"] if is_active else t["text_muted"],
                            bg=t["glass"], bd=0, activebackground=t["border"],
                            cursor="hand2", padx=8,
                            command=lambda k=tk_key: self._set_theme(k))
            btn.pack(side="left")

        # Separator below theme strip
        tk.Frame(self.inner, bg=t["border"], height=1).pack(fill="x")

        # Content
        self.content = tk.Frame(self.inner, bg=t["bg"], padx=14, pady=10)
        self.content.pack(fill="both", expand=True)
        self.status_label = tk.Label(self.content, text="Connecting...",
                                     font=("Segoe UI", 9), bg=t["bg"], fg=t["text_dim"])
        self.status_label.pack(anchor="w")
        self.bars_frame = tk.Frame(self.content, bg=t["bg"])
        self.bars_frame.pack(fill="x")

        # Footer
        ft = tk.Frame(self.inner, bg=t["footer_bg"], height=24)
        ft.pack(fill="x", side="bottom"); ft.pack_propagate(False)
        self.footer_label = tk.Label(ft, text="", font=("Segoe UI", 8),
                                     bg=t["footer_bg"], fg=t["text_muted"], padx=10)
        self.footer_label.pack(side="left")

        # Sonnet link in footer
        sonnet_link = tk.Label(ft, text="Use Sonnet", font=("Segoe UI", 8, "underline"),
                               bg=t["footer_bg"], fg=t["accent"], cursor="hand2", padx=10)
        sonnet_link.pack(side="right")
        sonnet_link.bind("<Button-1>", lambda e: webbrowser.open("https://claude.ai/new?model=claude-sonnet-4-6"))

    def _rebuild(self):
        for w in self.root.winfo_children(): w.destroy()
        self.tier_widgets = {}; self.extra_widget = None
        self._build()
        if self.usage_data: self._update_ui()

    # ── Controls ──

    def _ds(self, e): self.drag_xy = (e.x, e.y)
    def _dm(self, e):
        self.root.geometry(f"+{self.root.winfo_x()+e.x-self.drag_xy[0]}"
                           f"+{self.root.winfo_y()+e.y-self.drag_xy[1]}")

    def _toggle_pin(self):
        self.pinned = not self.pinned
        self.root.attributes("-topmost", self.pinned)
        self.pin_btn.configure(fg=self.t["text"] if self.pinned else self.t["text_muted"])

    def _toggle_compact(self):
        self.compact = not self.compact
        self._rebuild()

    def _cycle_theme(self):
        keys = list(THEMES.keys())
        self._set_theme(keys[(keys.index(self.theme_key) + 1) % len(keys)])

    def _set_theme(self, key):
        self.theme_key = key
        self.t = THEMES[key]
        cfg = load_config(); cfg["theme"] = key; save_config(cfg)
        self._rebuild()

    def _show_settings(self):
        key = simpledialog.askstring("Session Key", "Paste your claude.ai sessionKey cookie:", parent=self.root)
        if key and key.strip():
            cfg = load_config(); cfg["session_key"] = key.strip(); save_config(cfg)
            self.api = ClaudeAPI(key.strip()); self._refresh_async()

    def _setup_auth(self):
        sk = load_config().get("session_key")
        if not sk:
            self.root.after(500, lambda: (
                messagebox.showinfo("Setup",
                    "Welcome to Claude Usage Tracker!\n\n"
                    "1. Go to claude.ai and log in\n"
                    "2. Open DevTools (F12)\n"
                    "3. Application > Cookies > claude.ai\n"
                    "4. Copy 'sessionKey' value\n\n"
                    "Paste it in the next dialog.", parent=self.root),
                self._show_settings()))
        else:
            self.api = ClaudeAPI(sk); self._refresh_async(); self._schedule()

    # ── Refresh ──

    def _schedule(self):
        self.root.after(REFRESH_MS, lambda: (self._refresh_async(), self._schedule()))
        self.root.after(COUNTDOWN_MS, self._tick)

    def _tick(self):
        for key, w in self.tier_widgets.items():
            ra = w.get("resets_at")
            if ra:
                w["reset_label"].configure(text=f"Resets in {time_until(ra)}")
                f = pace_frac(ra, key)
                if f is not None and "pace_marker" in w:
                    w["pace_marker"].place(relx=f, rely=0, relheight=1.0, width=3, anchor="n")
                try:
                    u = int(w["pct_label"].cget("text").replace("%",""))
                    p = pace_info(u, ra, key)
                    w["pace_label"].configure(text=p[0] if p else "", fg=p[1] if p else self.t["text_dim"])
                except: pass
        self.root.after(COUNTDOWN_MS, self._tick)

    def _refresh_async(self):
        if not self.api: return
        self.status_label.configure(text="Refreshing...", fg=self.t["text_dim"])
        threading.Thread(target=self._fetch, daemon=True).start()

    def _fetch(self):
        try:
            self.usage_data = self.api.get_usage()
            self.last_updated = datetime.now()
            # Record history for sparklines
            for key in TIER_LABELS:
                tier = self.usage_data.get(key)
                if tier and tier.get("utilization") is not None:
                    append_history(key, int(tier["utilization"]))
            self.root.after(0, self._update_ui)
        except requests.exceptions.HTTPError as e:
            c = e.response.status_code if e.response is not None else "?"
            m = "Session expired - click Key" if c in (401, 403) else f"HTTP {c}"
            self.root.after(0, lambda m=m: self.status_label.configure(text=m, fg="#f87171"))
        except Exception as e:
            self.root.after(0, lambda: self.status_label.configure(text=str(e)[:60], fg="#f87171"))

    # ── Render ──

    def _update_ui(self):
        t = self.t; data = self.usage_data
        if not data: return
        self.status_label.configure(text="", fg=t["text_dim"])

        active = []
        for key, label in TIER_LABELS.items():
            tier = data.get(key)
            if tier and tier.get("utilization") is not None:
                active.append((key, label, int(tier["utilization"]), tier.get("resets_at")))
        if not active:
            self.status_label.configure(text="No active tiers"); return

        if self.compact:
            active = [max(active, key=lambda x: x[2])]

        stale = set(self.tier_widgets) - {a[0] for a in active}
        for k in stale: self.tier_widgets[k]["frame"].destroy(); del self.tier_widgets[k]

        for i, (key, label, util, ra) in enumerate(active):
            if key in self.tier_widgets: self._update_tier(key, util, ra)
            else: self._create_tier(key, label, util, ra, i > 0)

        if not self.compact:
            extra = data.get("extra_usage", {})
            if extra and extra.get("is_enabled"): self._update_extra(extra)

        if self.last_updated:
            ts = self.last_updated.strftime("%I:%M %p").lstrip("0")
            mode = "Compact" if self.compact else ("Pinned" if self.pinned else "Float")
            self.footer_label.configure(text=f"Updated {ts} | {mode}")

    def _create_tier(self, key, label, util, resets_at, sep):
        t = self.t
        frame = tk.Frame(self.bars_frame, bg=t["bg"])
        frame.pack(fill="x", pady=(8 if sep else 2, 0))
        if sep: tk.Frame(frame, bg=t["border"], height=1).pack(fill="x", pady=(0, 8))

        card = tk.Frame(frame, bg=t["glass"], padx=12, pady=10,
                        highlightbackground=t["border"], highlightthickness=1)
        card.pack(fill="x")

        # Row 1: label + sparkline + percentage
        hdr = tk.Frame(card, bg=t["glass"]); hdr.pack(fill="x")
        tk.Label(hdr, text=label, font=("Segoe UI Semibold", 9),
                 bg=t["glass"], fg=t["text_secondary"]).pack(side="left")

        color = usage_color(util)
        pct = tk.Label(hdr, text=f"{util}%", font=("Segoe UI Bold", 12),
                       bg=t["glass"], fg=color)
        pct.pack(side="right")

        # Sparkline canvas (between label and percentage)
        spark = tk.Canvas(hdr, width=50, height=16, bg=t["glass"],
                          highlightthickness=0, bd=0)
        spark.pack(side="right", padx=(0, 8))

        # Draw sparkline from history
        hist = load_history().get(key, [])
        vals = [h["v"] for h in hist]
        if len(vals) >= 2:
            spark.after(50, lambda s=spark, v=vals: draw_sparkline(s, v, t["sparkline"], t["glass"]))

        # Row 2: progress bar with pace marker
        bar_outer = tk.Frame(card, bg=t["bar_bg"], height=BAR_HEIGHT)
        bar_outer.pack(fill="x", pady=(6, 0))
        bar_fill = tk.Frame(bar_outer, bg=color, height=BAR_HEIGHT)
        bar_fill.place(relwidth=max(util/100, 0.008), relheight=1.0)

        # Pace marker — bright colored tick
        pace_marker = tk.Frame(bar_outer, bg=t["pace_marker"], width=3, height=BAR_HEIGHT)
        f = pace_frac(resets_at, key)
        if f is not None:
            pace_marker.place(relx=f, rely=0, relheight=1.0, width=3, anchor="n")

        # Row 3: countdown + pacing
        info = tk.Frame(card, bg=t["glass"]); info.pack(fill="x", pady=(4, 0))
        reset_lbl = tk.Label(info, text=f"Resets in {time_until(resets_at)}",
                             font=("Segoe UI", 8), bg=t["glass"], fg=t["text_dim"])
        reset_lbl.pack(side="left")
        pace = pace_info(util, resets_at, key)
        pace_lbl = tk.Label(info, text=pace[0] if pace else "",
                            font=("Segoe UI Semibold", 8), bg=t["glass"],
                            fg=pace[1] if pace else t["text_dim"])
        pace_lbl.pack(side="right")

        # Row 4: reset date + burn projection
        r4 = tk.Frame(card, bg=t["glass"]); r4.pack(fill="x", pady=(1, 0))
        reset_dt_lbl = tk.Label(r4, text=reset_datetime_str(resets_at),
                                font=("Segoe UI", 7), bg=t["glass"], fg=t["text_muted"])
        reset_dt_lbl.pack(side="left")

        burn = burn_projection(util, resets_at, key)
        burn_lbl = tk.Label(r4, text=burn[0] if burn else "",
                            font=("Segoe UI", 7), bg=t["glass"],
                            fg=burn[1] if burn else t["text_muted"])
        burn_lbl.pack(side="right")

        self.tier_widgets[key] = {
            "frame": frame, "card": card, "pct_label": pct,
            "bar_fill": bar_fill, "pace_marker": pace_marker, "sparkline": spark,
            "reset_label": reset_lbl, "reset_dt_label": reset_dt_lbl,
            "pace_label": pace_lbl, "burn_label": burn_lbl, "resets_at": resets_at,
        }

    def _update_tier(self, key, util, resets_at):
        w = self.tier_widgets[key]; t = self.t
        color = usage_color(util)
        w["pct_label"].configure(text=f"{util}%", fg=color)
        w["bar_fill"].configure(bg=color)
        w["bar_fill"].place(relwidth=max(util/100, 0.008), relheight=1.0)
        w["reset_label"].configure(text=f"Resets in {time_until(resets_at)}")
        w["reset_dt_label"].configure(text=reset_datetime_str(resets_at))
        w["resets_at"] = resets_at
        f = pace_frac(resets_at, key)
        if f is not None:
            w["pace_marker"].place(relx=f, rely=0, relheight=1.0, width=3, anchor="n")
        p = pace_info(util, resets_at, key)
        w["pace_label"].configure(text=p[0] if p else "", fg=p[1] if p else t["text_dim"])
        burn = burn_projection(util, resets_at, key)
        w["burn_label"].configure(text=burn[0] if burn else "",
                                  fg=burn[1] if burn else t["text_muted"])
        # Update sparkline
        hist = load_history().get(key, [])
        vals = [h["v"] for h in hist]
        if len(vals) >= 2:
            draw_sparkline(w["sparkline"], vals, t["sparkline"], t["glass"])

    def _update_extra(self, extra):
        t = self.t
        used = extra.get("used_credits") or 0; limit = extra.get("monthly_limit")
        text = f"${used:.2f} spent"
        if limit: text += f" / ${limit:.2f} limit"
        if self.extra_widget:
            self.extra_widget["label"].configure(text=text)
        else:
            fr = tk.Frame(self.bars_frame, bg=t["bg"]); fr.pack(fill="x", pady=(10, 0))
            tk.Frame(fr, bg=t["border"], height=1).pack(fill="x", pady=(0, 8))
            cd = tk.Frame(fr, bg=t["glass"], padx=12, pady=8,
                          highlightbackground=t["border"], highlightthickness=1)
            cd.pack(fill="x")
            tk.Label(cd, text="Extra Usage", font=("Segoe UI Semibold", 9),
                     bg=t["glass"], fg=t["text_secondary"]).pack(anchor="w")
            l = tk.Label(cd, text=text, font=("Segoe UI", 8), bg=t["glass"], fg=t["text_dim"])
            l.pack(anchor="w")
            self.extra_widget = {"frame": fr, "label": l}

    def run(self):
        self.root.mainloop()


if __name__ == "__main__":
    UsageWidget().run()
