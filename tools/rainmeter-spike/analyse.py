#!/usr/bin/env python3
"""Rainmeter suite analyser for the Sanduhr 3.8 spike (item H).

Read-only. Standard library only. Run it isolated:

    python -I tools/rainmeter-spike/analyse.py [SUITE_DIR] [--json]

SUITE_DIR is the folder that holds the skins and an @Resources folder (for
example ...\\Rainmeter\\Skins\\Knighthawk). With no argument it looks in the
usual Documents and OneDrive places.

What it does
  1. Parses every .ini/.inc (Rainmeter INI dialect, UTF-8 or UTF-16).
  2. Resolves @include (the including file's own options win) and #Var#.
  3. Resolves MeterStyle inheritance (MeterStyle=A | B, own options win).
  4. Classifies every meter and measure into a tier:
       tier 1  the look: fonts, colours, images, positions. No code runs.
       tier 2  a safe subset that maps to a Desk piece.
       tier 3  never: Lua, plugin DLLs, WebParser, RunCommand, bangs that
               launch programs.
     A measure that depends on a tier 3 measure is itself tier 3 ("tainted"),
     and a meter bound to one keeps its look (tier 1) but loses its data.
     Meter classes: T1 look only, T2 allowed type with safe data, T2i a Shape
     that is really a bar or ring (inferred), T3 never.
  5. Lists plugins, Lua, bangs, fonts, colour variables, skew matrices and
     the Desk piece each tier 2 item would become.

It never writes, moves or executes anything in the suite, and prints only
names, counts and short keys, not skin text.
"""
import json
import math
import os
import re
import sys
from collections import Counter, OrderedDict

T2_MEASURES = {"time", "cpu", "physicalmemory", "swapmemory", "freediskspace", "calc"}
# Safe built-ins with no Desk piece today (an importer would need a new field).
T2_GAP_MEASURES = {"netin", "netout", "nettotal", "uptime", "loop"}

BANG_RE = re.compile(r"\[\s*!([A-Za-z]+)([^\]]*)\]")
STATE_BANGS = {
    "setvariable", "setoption", "setoptiongroup", "updatemeter", "updatemeasure",
    "updatemetergroup", "updatemeasuregroup", "redraw", "update", "showmeter",
    "hidemeter", "togglemeter", "showmetergroup", "hidemetergroup", "enablemeasure",
    "disablemeasure", "togglemeasure", "refresh", "refreshapp", "writekeyvalue",
    "setvariablegroup", "enablemeasuregroup", "disablemeasuregroup",
}
LAYOUT_BANGS = {"move", "setwindowposition", "zpos", "draggable", "clickthrough",
                "setclip", "keeponscreen", "snapedges", "autoselectscreen", "setanchor",
                "movemeter", "fadeduration"}
LAUNCH_BANGS = {"execute", "run", "activateconfig"}
COLOR_VALUE_RE = re.compile(
    r"^\s*(\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}(\s*,\s*\d{1,3})?|[0-9a-fA-F]{6}([0-9a-fA-F]{2})?)\s*$")
REF_RE = re.compile(r"(?<![!\w])\[([A-Za-z_][\w .\-]*?)(?::[^\]]*)?\]")


def read_text(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16", errors="replace")
    if raw.startswith(b"\xef\xbb\xbf"):
        return raw[3:].decode("utf-8", errors="replace")
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1252", errors="replace")


def parse_ini(text):
    """section_lower -> (display name, OrderedDict key_lower -> value)."""
    sections = OrderedDict()
    cur = None
    for line in text.splitlines():
        s = line.strip()
        if not s or s.startswith(";"):
            continue
        if s.startswith("[") and s.endswith("]") and not s.startswith("[!"):
            name = s[1:-1].strip()
            cur = sections.setdefault(name.lower(), (name, OrderedDict()))
            continue
        if cur is None or "=" not in s:
            continue
        k, v = s.split("=", 1)
        k = k.strip().lower()
        v = v.strip()
        if k.startswith("@include"):
            k = "%s#%d" % (k, len(cur[1]))
        cur[1][k] = v
    return sections


def subst(text, vars_, counter, depth=0):
    if "#" not in text or depth > 10:
        return text

    def rep(m):
        name = m.group(1)
        v = vars_.get(name.lower())
        if v is None:
            if counter is not None and name != "@":
                counter[name] += 1
            return m.group(0)
        return v

    out = re.sub(r"#([A-Za-z_@][\w .\-]*?)#", rep, text)
    return subst(out, vars_, counter, depth + 1) if out != text else out


class Skin:
    def __init__(self, rel):
        self.name = rel
        self.includes = []        # (raw value, resolved relative path or None)
        self.files = [rel]
        self.metadata = {}
        self.variables = OrderedDict()   # lower -> value
        self.var_names = {}              # lower -> display
        self.rainmeter = OrderedDict()
        self.measures = OrderedDict()    # id -> (display, opts)
        self.meters = OrderedDict()      # id -> (display, effective opts, styles)
        self.styles = OrderedDict()
        self.other = OrderedDict()
        self.unresolved = Counter()


def merge(dest, src):
    """Merge src into dest; src wins (callers order includes before the file)."""
    for lname, (disp, opts) in src.items():
        d = dest.setdefault(lname, (disp, OrderedDict()))
        for k, v in opts.items():
            if k.startswith("@include"):
                continue
            d[1][k] = v


def load_skin(ini_path, suite):
    res_dir = os.path.join(suite, "@Resources")
    rel = os.path.relpath(ini_path, suite).replace("\\", "/")
    skin = Skin(rel)
    main = parse_ini(read_text(ini_path))
    own_vars = {}
    if "variables" in main:
        own_vars = {k: v for k, v in main["variables"][1].items() if not k.startswith("@include")}
    merged = OrderedDict()
    seen = set()

    def include_all(sections, base_dir):
        for lname, (disp, opts) in sections.items():
            for k, v in opts.items():
                if not k.startswith("@include"):
                    continue
                p = v.replace("#@#", res_dir + os.sep).replace("#CURRENTPATH#", base_dir + os.sep)
                p = subst(p, own_vars, None).replace("\\", os.sep).replace("/", os.sep)
                if not os.path.isabs(p):
                    p = os.path.join(base_dir, p)
                ok = os.path.isfile(p)
                skin.includes.append((v, os.path.relpath(p, suite).replace("\\", "/") if ok else None))
                if ok and p not in seen:
                    seen.add(p)
                    skin.files.append(os.path.relpath(p, suite).replace("\\", "/"))
                    inc = parse_ini(read_text(p))
                    include_all(inc, os.path.dirname(p))
                    merge(merged, inc)

    include_all(main, os.path.dirname(ini_path))
    merge(merged, main)   # the skin's own options override its includes

    for k, v in merged.get("variables", (None, {}))[1].items():
        skin.variables[k] = v
    for _ in range(8):
        changed = False
        for k, v in list(skin.variables.items()):
            nv = subst(v, skin.variables, None)
            if nv != v:
                skin.variables[k] = nv
                changed = True
        if not changed:
            break

    skin.metadata = dict(merged.get("metadata", (None, {}))[1])
    skin.rainmeter = dict(merged.get("rainmeter", (None, {}))[1])

    for lname, (disp, opts) in merged.items():
        if lname in ("rainmeter", "variables", "metadata"):
            continue
        res = OrderedDict((k, subst(v, skin.variables, skin.unresolved)) for k, v in opts.items())
        if "measure" in res:
            skin.measures[lname] = (disp, res)
        elif "meter" in res:
            skin.meters[lname] = (disp, res, [])
        else:
            skin.styles[lname] = (disp, res)

    for lname, (disp, opts, _) in list(skin.meters.items()):
        eff = OrderedDict()
        names = [s.strip().lower() for s in opts.get("meterstyle", "").split("|") if s.strip()]
        for st in names:
            if st in skin.styles:
                eff.update(skin.styles[st][1])
        eff.update(opts)
        skin.meters[lname] = (disp, eff, names)
    return skin


def refs_in(value, names):
    return {m.group(1).strip().lower() for m in REF_RE.finditer(value)
            if m.group(1).strip().lower() in names}


def tilt_of(matrix):
    parts = [p.strip() for p in matrix.replace(",", ";").split(";")]
    try:
        return float(parts[1]), float(parts[2])
    except (ValueError, IndexError):
        return None


def classify_bangs(text):
    c = Counter()
    for m in BANG_RE.finditer(text):
        n = m.group(1).lower()
        if n in STATE_BANGS:
            c["state"] += 1
        elif n in LAYOUT_BANGS:
            c["layout"] += 1
        elif n == "commandmeasure":
            c["commandmeasure (tier 3)"] += 1
        elif n in LAUNCH_BANGS:
            c["launch (tier 3)"] += 1
        else:
            c["other:" + n] += 1
    return c


def runs_program(key, value):
    if not key.endswith("action"):
        return False
    for m in re.finditer(r"\[([^\]]*)\]", value):
        b = m.group(1).strip()
        if b and not b.startswith("!") and (b[0] in "\"'" or re.match(r"^[A-Za-z]:\\", b)
                                            or b.lower().startswith(("http", "file:", "powershell", "cmd"))):
            return True
    return False


def is_action_key(k):
    return k.endswith("action") or bool(re.match(r"f\d+$", k)) or k in ("on", "off")


def piece_for_measure(mtype, opts):
    t = mtype.lower()
    if t == "time":
        fmt = opts.get("format", "")
        return "clock" if (not fmt or re.search(r"%[HIMSlkpP]", fmt)) else "clock (date line)"
    if t in ("cpu", "physicalmemory", "swapmemory", "freediskspace"):
        return "telemetry"
    if t in ("netin", "netout", "nettotal"):
        return "none yet (network is a telemetry gap)"
    if t == "calc":
        return "derived (follows its inputs)"
    if t == "webparser":
        return "weather (by intent, rebuild)"
    if t == "plugin":
        pl = opts.get("plugin", "").lower()
        if "nowplaying" in pl or "player" in pl:
            return "nowPlaying (by intent, rebuild)"
        if pl == "audiolevel":
            return "none (audio visualiser)"
        if pl == "usagemonitor":
            return "telemetry (by intent, rebuild)"
        return "none"
    return "none"


def analyse_skin(skin):
    mids = set(skin.measures)
    measures = []
    for lname, (disp, opts) in skin.measures.items():
        mtype = opts.get("measure", "")
        t = mtype.lower()
        if t in T2_MEASURES:
            declared = 2
        elif t in T2_GAP_MEASURES:
            declared = 2
        else:
            declared = 3
        deps = set()
        for v in opts.values():
            deps |= refs_in(v, mids)
        if t == "calc":
            for tok in re.findall(r"[A-Za-z_]\w*", opts.get("formula", "")):
                if tok.lower() in mids:
                    deps.add(tok.lower())
        deps.discard(lname)
        measures.append({"id": lname, "name": disp, "type": mtype, "declared": declared, "deps": deps,
                         "gap": t in T2_GAP_MEASURES, "eff": None, "opts": opts,
                         "piece": piece_for_measure(mtype, opts)})
    byid = {m["id"]: m for m in measures}

    def eff(mid, stack=()):
        m = byid[mid]
        if m["eff"] is not None:
            return m["eff"]
        e = m["declared"]
        if mid not in stack:
            for d in m["deps"]:
                if d in byid and eff(d, stack + (mid,)) == 3:
                    e = 3
        m["eff"] = e
        return e

    for m in measures:
        eff(m["id"])

    meters = []
    for lname, (disp, opts, styles) in skin.meters.items():
        mtype = opts.get("meter", "")
        t = mtype.lower()
        deps = set()
        for k, v in opts.items():
            if k != "meter":
                deps |= refs_in(v, mids)
            if re.match(r"measurename\d*$", k) and v.strip().lower() in mids:
                deps.add(v.strip().lower())
        data_t3 = any(byid[d]["eff"] == 3 for d in deps)
        cls, piece, style, flags = "T1", "static art (look only)", None, []
        shape = opts.get("shape", "")
        if t == "shape":
            hit = bool(re.search(r"Fill Color\s+\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*[01]\s*(\||$)", shape)) \
                and "StrokeWidth 0" in shape
            if hit:
                flags.append("hit-box")
                piece = "none (invisible hit-box, dropped)"
            elif deps and not data_t3:
                cls, piece = "T2i", "meters/telemetry"
                style = "ring" if re.search(r"\b(Arc|Ellipse)\b", shape) else "slim"
                flags.append("shape-inferred")
            elif deps:
                flags.append("data is tier 3")
        elif t == "string":
            if deps and not data_t3:
                cls = "T2"
                pcs = sorted({byid[d]["piece"] for d in deps})
                piece = "/".join(pcs)
                if piece.startswith("derived"):
                    piece = "telemetry"
                style = "slim label" if "telemetry" in piece else None
            elif deps:
                flags.append("data is tier 3")
                piece = "static text (look only)"
            else:
                piece = "static text (look only)"
        elif t == "image":
            cls, piece = "T2", "sprite / own art (item C)"
            if deps and data_t3:
                flags.append("data is tier 3")
        elif t in ("bar", "roundline"):
            if data_t3:
                flags.append("data is tier 3")
            else:
                cls, piece = "T2", "meters/telemetry"
                style = "ring" if t == "roundline" else "bar/slim/ticks"
        else:
            piece = "unsupported meter type (look only)"
        bangs, runs = Counter(), 0
        for k, v in opts.items():
            if is_action_key(k) and "[" in v:
                bangs.update(classify_bangs(v))
                runs += runs_program(k, v)
        if runs and t == "image":
            piece = "launcher (action is tier 3; user picks the target)"
        meters.append({"id": lname, "name": disp, "type": mtype, "class": cls, "piece": piece,
                       "style": style, "deps": sorted(deps), "flags": flags, "styles": styles,
                       "run_commands": runs, "bangs": dict(bangs), "opts": opts})

    other_bangs = Counter()
    for _, (_, o) in skin.styles.items():
        for k, v in o.items():
            if "[!" in v:
                other_bangs.update(classify_bangs(v))
    for k, v in skin.rainmeter.items():
        if "[!" in v:
            other_bangs.update(classify_bangs(v))
    for m in measures:   # ActionTimer frames and IfTrueAction live on measures
        for k, v in m["opts"].items():
            if "[!" in v:
                other_bangs.update(classify_bangs(v))

    plugins = Counter(m["opts"].get("plugin", "?") for m in measures if m["type"].lower() == "plugin")
    fonts, sizes, skews = Counter(), Counter(), Counter()
    for me in meters:
        o = me["opts"]
        if "fontface" in o:
            fonts[o["fontface"]] += 1
        if "fontsize" in o:
            sizes[o["fontsize"]] += 1
        if "transformationmatrix" in o:
            tl = tilt_of(o["transformationmatrix"])
            if tl:
                skews["%s;%s" % tl] += 1
    return {
        "skin": skin.name, "title": skin.metadata.get("name"), "author": skin.metadata.get("author"),
        "license": skin.metadata.get("license"), "includes": skin.includes,
        "unresolved_vars": dict(skin.unresolved), "measures": measures, "meters": meters,
        "plugins": dict(plugins),
        "lua": sum(1 for m in measures if m["type"].lower() == "script" or "scriptfile" in m["opts"]),
        "webparser": sum(1 for m in measures if m["type"].lower() == "webparser"),
        "other_bangs": dict(other_bangs), "fonts": dict(fonts), "font_sizes": dict(sizes),
        "skews": dict(skews), "variables": dict(skin.variables), "files": skin.files,
    }


def find_suite(arg):
    home = os.path.expanduser("~")
    cands = [arg] if arg else []
    for base in (os.path.join(home, "OneDrive", "Documents", "Rainmeter", "Skins"),
                 os.path.join(home, "Documents", "Rainmeter", "Skins")):
        cands.append(os.path.join(base, "Knighthawk"))
    return next((c for c in cands if c and os.path.isdir(c)), None)


def skin_inis(suite):
    out = []
    for root, dirs, files in os.walk(suite):
        dirs[:] = [d for d in dirs if d != "@Resources"]
        out += [os.path.join(root, f) for f in files if f.lower().endswith(".ini")]
    return sorted(out)


def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


def report(suite, skins):
    P = print
    inv = Counter()
    for _, _, fs in os.walk(suite):
        for f in fs:
            inv[os.path.splitext(f)[1].lower().lstrip(".") or "(none)"] += 1
    fdir = os.path.join(suite, "@Resources", "Fonts")
    fonts = sorted(os.listdir(fdir)) if os.path.isdir(fdir) else []
    P("SUITE %s" % os.path.basename(suite))
    P("files by extension: " + ", ".join("%s=%d" % kv for kv in sorted(inv.items())))
    P("bundled fonts: " + ", ".join(fonts))
    P("")
    P("PER SKIN  (meter classes T1 T2 T2i T3 | measures declared T2/T3, effective T2/T3)")
    tm, td, te = Counter(), Counter(), Counter()
    row = "%-28s meters=%2d T1=%2d T2=%2d T2i=%2d T3=%2d | measures=%2d decl %2d/%2d eff %2d/%2d"
    for s in skins:
        mc = Counter(m["class"] for m in s["meters"])
        d = Counter(m["declared"] for m in s["measures"])
        e = Counter(m["eff"] for m in s["measures"])
        tm.update(mc); td.update(d); te.update(e)
        P(row % (s["skin"], len(s["meters"]), mc["T1"], mc["T2"], mc["T2i"], mc["T3"],
                 len(s["measures"]), d[2], d[3], e[2], e[3]))
    P(row % ("TOTAL", sum(tm.values()), tm["T1"], tm["T2"], tm["T2i"], tm["T3"],
             sum(te.values()), td[2], td[3], te[2], te[3]))
    P("")
    P("METERS by type and class")
    c = Counter((m["type"], m["class"]) for s in skins for m in s["meters"])
    for (t, k), n in sorted(c.items()):
        P("  %-10s %-4s %d" % (t, k, n))
    gate = Counter(f for s in skins for m in s["meters"] for f in m["flags"])
    P("  flags: " + ", ".join("%s x%d" % kv for kv in sorted(gate.items())))
    P("")
    P("MEASURES by type, declared and effective tier")
    mt = Counter()
    for s in skins:
        for m in s["measures"]:
            lab = m["type"] if m["type"].lower() != "plugin" else "Plugin=" + m["opts"].get("plugin", "?")
            mt[(lab, m["declared"], m["eff"])] += 1
    for (t, d, e), n in sorted(mt.items()):
        P("  %-28s declared T%d effective T%d  %d%s" % (t, d, e, n, "  (tainted)" if d != e else ""))
    P("")
    pl = Counter()
    for s in skins:
        pl.update(s["plugins"])
    P("PLUGINS: " + (", ".join("%s x%d" % kv for kv in sorted(pl.items())) or "none"))
    P("Lua/Script measures: %d" % sum(s["lua"] for s in skins))
    P("WebParser measures: %d" % sum(s["webparser"] for s in skins))
    P("")
    P("BANGS in action options, rainmeter section, frames and measures")
    allb, runs = Counter(), 0
    for s in skins:
        for m in s["meters"]:
            allb.update(m["bangs"]); runs += m["run_commands"]
        allb.update(s["other_bangs"])
    for k, v in sorted(allb.items()):
        P("  %-26s %d" % (k, v))
    P("  program-launching action values (bare command): %d" % runs)
    P("")
    P("FONTS used (resolved)")
    ft = Counter()
    for s in skins:
        ft.update(s["fonts"])
    bn = {norm(os.path.splitext(f)[0]) for f in fonts}
    for f, n in sorted(ft.items(), key=lambda kv: -kv[1]):
        P("  %-20s x%-3d %s" % (f, n, "bundled in @Resources" if norm(f) in bn else "not bundled"))
    sz = Counter()
    for s in skins:
        sz.update(s["font_sizes"])
    P("  sizes: " + ", ".join("%s x%d" % kv for kv in sorted(sz.items(), key=lambda kv: float(kv[0]) if re.match(r"^[\d.]+$", kv[0]) else 0)))
    P("")
    P("SKEW MATRICES (m12;m21 -> degrees)")
    sk = Counter()
    for s in skins:
        sk.update(s["skews"])
    for k, n in sk.items():
        a, b = (float(x) for x in k.split(";"))
        P("  %s -> y-skew %.1f, x-skew %.1f  x%d" % (k, math.degrees(math.atan(a)), math.degrees(math.atan(b)), n))
    P("")
    P("COLOUR AND THEME VARIABLES")
    seen = set()
    for s in skins:
        for k, v in s["variables"].items():
            if k not in seen:
                seen.add(k)
                if COLOR_VALUE_RE.match(v) or "grad" in k or re.match(r"^\d+\s*\|", v):
                    P("  %s=%s" % (k, v))
    P("")
    P("TIER 2 -> DESK PIECE")
    pc = Counter()
    for s in skins:
        for m in s["meters"]:
            if m["class"] in ("T2", "T2i"):
                pc[("meter", m["type"] + ("*" if m["class"] == "T2i" else ""), m["piece"], m["style"] or "-")] += 1
        for m in s["measures"]:
            if m["eff"] == 2:
                pc[("measure", m["type"], m["piece"], "-")] += 1
    for (k, t, p, st), n in sorted(pc.items()):
        P("  %-8s %-16s -> %-44s style=%-16s x%d" % (k, t, p, st, n))
    P("  (* = Shape inferred to be a bar or ring)")
    P("")
    P("TIER 3 INTENT (rebuilt by hand on a Desk piece, never imported)")
    ic = Counter()
    for s in skins:
        for m in s["measures"]:
            if m["declared"] == 3:
                lab = m["type"] if m["type"].lower() != "plugin" else "Plugin=" + m["opts"].get("plugin", "?")
                ic[(lab, m["piece"])] += 1
    for (t, p), n in sorted(ic.items()):
        P("  %-28s -> %-40s x%d" % (t, p, n))
    P("")
    P("INCLUDES")
    for s in skins:
        for raw, res in s["includes"]:
            P("  %-26s %s -> %s" % (s["skin"], raw, res or "UNRESOLVED (file missing)"))
    P("UNRESOLVED #VARS")
    u = [s for s in skins if s["unresolved_vars"]]
    for s in u:
        P("  %-26s %s" % (s["skin"], ", ".join("%s x%d" % kv for kv in s["unresolved_vars"].items())))
    if not u:
        P("  none")
    P("")
    P("AUTHOR / LICENSE")
    for s in skins:
        P("  %-26s author=%s license=%s" % (s["skin"], s["author"], s["license"]))


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    suite = find_suite(args[0] if args else None)
    if not suite:
        print("Suite not found. Pass the folder that holds the skins and @Resources.")
        return 2
    skins = [analyse_skin(load_skin(p, suite)) for p in skin_inis(suite)]
    if "--json" in argv:
        def clean(o):
            if isinstance(o, set):
                return sorted(o)
            raise TypeError(type(o))
        for s in skins:
            for k in ("measures", "meters"):
                s[k] = [{a: b for a, b in x.items() if a != "opts"} for x in s[k]]
        print(json.dumps({"suite": os.path.basename(suite), "skins": skins}, default=clean, indent=1))
        return 0
    report(suite, skins)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
