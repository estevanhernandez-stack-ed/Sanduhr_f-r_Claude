# Smoke tools core: everything the CLI does that does not need the running app, so selftest.rb
# can exercise it offline. Ruby 2.6 stdlib only.
require 'yaml'
require 'json'
require 'fileutils'
require 'open3'
require 'time'

module Smoke
  ROOT = File.expand_path('..', __dir__)
  OUT = File.join(ROOT, 'out')
  SCENARIOS = File.join(ROOT, 'scenarios')
  DOMAIN = 'com.626labs.sanduhr'.freeze
  DESK_DOMAIN = 'com.626labs.sanduhr.desk'.freeze

  class Failure < StandardError; end

  # Node and value matching for `expect` steps.
  module Match
    module_function

    # "/regex/" or "/regex/i" as a Regexp, anything else nil.
    def regex(value)
      return nil unless value.is_a?(String)
      m = %r{\A/(.*)/([imx]*)\z}m.match(value)
      return nil unless m
      opts = 0
      opts |= Regexp::IGNORECASE if m[2].include?('i')
      opts |= Regexp::MULTILINE if m[2].include?('m')
      opts |= Regexp::EXTENDED if m[2].include?('x')
      Regexp.new(m[1], opts)
    end

    def numeric?(v)
      v.is_a?(Numeric) || (v.is_a?(String) && v =~ /\A-?\d+(\.\d+)?\z/)
    end

    # One expected value against one actual value. A regex matches the actual value as text;
    # numbers compare as numbers ("1" and 1 match); true and false also match 1 and 0, which is
    # how a switch reports its value.
    def value?(expected, actual)
      if (re = regex(expected))
        !actual.nil? && re.match?(actual.to_s)
      elsif expected.nil?
        actual.nil?
      elsif expected == true || expected == false
        actual == expected || (actual.is_a?(Numeric) && actual == (expected ? 1 : 0))
      elsif numeric?(expected) && numeric?(actual)
        expected.to_f == actual.to_f
      else
        expected.to_s == actual.to_s
      end
    end

    # Every given field matches the node. `text` is not a field of its own: it matches the
    # label or the value, since SwiftUI puts a Text's string in one or the other.
    def node?(node, spec)
      return false unless node.is_a?(Hash)
      spec.all? do |k, v|
        if k.to_s == 'text'
          value?(v, node['label']) || value?(v, node['value'])
        else
          value?(v, node[k.to_s])
        end
      end
    end

    # The first node, depth first, anywhere under `nodes` that matches.
    def find(nodes, spec)
      Array(nodes).each do |n|
        return n if node?(n, spec)
        found = find(n['children'], spec) if n.is_a?(Hash)
        return found if found
      end
      nil
    end

    # Windows called `name`, or its numbered duplicates (sheet, sheet-2).
    def windows(tree, name)
      Array(tree).select { |w| w['window'] == name || w['window'].to_s =~ /\A#{Regexp.escape(name)}-\d+\z/ }
    end

    # [found node or nil, reason when the window is not there].
    def in_window(tree, window, spec)
      wins = windows(tree, window)
      return [nil, "no #{window} window on screen"] if wins.empty?
      wins.each do |w|
        n = find(w['tree'], spec)
        return [n, nil] if n
      end
      [nil, nil]
    end
  end

  # Dotted lookups into state.yaml: "meters.0.label", "alerts.enabled".
  module State
    module_function

    # A path part that picks from a list by a field: `desk_frames[kind=meters]` is the first item
    # of `desk_frames` whose `kind` is `meters` (compared as text; no dots in the value).
    SELECT = /\A([^\[\]]*)\[([^=\]]+)=([^\]]*)\]\z/.freeze

    # [found, value].
    def dig(state, dotted)
      cur = state
      dotted.to_s.split('.').each do |part|
        if (m = SELECT.match(part))
          unless m[1].empty?
            return [false, nil] unless cur.is_a?(Hash) && cur.key?(m[1])
            cur = cur[m[1]]
          end
          return [false, nil] unless cur.is_a?(Array)
          cur = cur.find { |item| item.is_a?(Hash) && item[m[2]].to_s == m[3] }
          return [false, nil] if cur.nil?
        elsif cur.is_a?(Array) && part =~ /\A-?\d+\z/
          i = part.to_i
          return [false, nil] unless i < cur.length && i >= -cur.length
          cur = cur[i]
        elsif cur.is_a?(Hash) && cur.key?(part)
          cur = cur[part]
        else
          return [false, nil]
        end
      end
      [true, cur]
    end

    # Each expected key against the state: list of failure messages (empty when all match).
    def check(state, expected)
      expected.each_with_object([]) do |(key, want), errors|
        found, got = dig(state, key)
        if !found
          errors << "#{key}: not in state"
        elsif !Match.value?(want, got)
          errors << "#{key}: expected #{want.inspect}, got #{got.inspect}"
        end
      end
    end
  end

  # Reading and writing defaults through a backend; the real one shells out to `defaults`.
  class ShellDefaults
    TYPES = { 'boolean' => 'bool', 'integer' => 'int', 'float' => 'float', 'string' => 'string' }.freeze

    def self.parse_type(text)
      m = /Type is (\w+)/.match(text.to_s)
      m && m[1]
    end

    # [type, value] or nil when the key is not set. Type is bool, int, float, string or the
    # raw `defaults` type name for anything else.
    def read(domain, key)
      out, status = Open3.capture2e('defaults', 'read-type', domain, key)
      return nil unless status.success?
      raw = self.class.parse_type(out)
      type = TYPES[raw] || raw
      value, = Open3.capture2e('defaults', 'read', domain, key)
      [type, Defaults.cast(type, value.strip)]
    end

    def write(domain, key, type, value)
      arg = type == 'bool' ? (value ? 'true' : 'false') : value.to_s
      out, status = Open3.capture2e('defaults', 'write', domain, key, "-#{type}", arg)
      raise Failure, "defaults write #{domain} #{key} failed: #{out.strip}" unless status.success?
    end

    def delete(domain, key)
      Open3.capture2e('defaults', 'delete', domain, key)
    end
  end

  # In-memory backend for selftest.
  class MemoryDefaults
    attr_reader :store

    def initialize(store = {})
      @store = store
    end

    def read(domain, key)
      @store[[domain, key]]
    end

    def write(domain, key, type, value)
      @store[[domain, key]] = [type, value]
    end

    def delete(domain, key)
      @store.delete([domain, key])
    end
  end

  # Writes defaults for a scenario and puts every touched key back afterwards: the value it had,
  # or deleted when it had none.
  class Defaults
    WRITABLE = %w[bool int float string].freeze
    ALIASES = { 'desk' => DESK_DOMAIN, 'widget' => DOMAIN, 'sanduhr' => DOMAIN }.freeze

    def self.cast(type, value)
      case type
      when 'bool' then [true, 'true', '1', 'yes', 1].include?(value)
      when 'int' then value.to_i
      when 'float' then value.to_f
      else value.to_s
      end
    end

    def self.domain(name)
      ALIASES.fetch(name.to_s, name.to_s)
    end

    attr_reader :saved

    def initialize(backend)
      @backend = backend
      @saved = {} # [domain, key] => [type, value] or nil, in first-touched order
    end

    def write(domain, key, type, value)
      domain = self.class.domain(domain || DOMAIN)
      type = type.to_s
      raise Failure, "defaults type must be one of #{WRITABLE.join(', ')}" unless WRITABLE.include?(type)
      k = [domain, key.to_s]
      unless @saved.key?(k)
        before = @backend.read(domain, key.to_s)
        if before && !WRITABLE.include?(before[0])
          raise Failure, "#{domain} #{key} holds a #{before[0]}, which can't be restored; not touching it"
        end
        @saved[k] = before
      end
      @backend.write(domain, key.to_s, type, self.class.cast(type, value))
    end

    # Puts everything back, newest first. Returns what was done, for the report.
    def restore!
      done = []
      @saved.to_a.reverse_each do |(domain, key), before|
        if before
          @backend.write(domain, key, before[0], before[1])
          done << "#{domain} #{key} = #{before[1].inspect}"
        else
          @backend.delete(domain, key)
          done << "#{domain} #{key} deleted"
        end
      end
      @saved.clear
      done
    end
  end

  # The app state a scenario's actions can change, and the actions that put it back.
  module Restore
    module_function

    # Actions ([name, arg]) that bring `after` back to `before`.
    def plan(before, after)
      return [] unless before.is_a?(Hash) && after.is_a?(Hash)
      steps = []
      # Desk first: switching it can show or hide the widget (When the widget shows), so the
      # widget is then put back explicitly.
      desk_changed = !!before['desk_enabled'] != !!after['desk_enabled']
      steps << ['desk', before['desk_enabled'] ? 'on' : 'off'] if desk_changed
      # After a Desk switch the widget may have moved, so a tool closes on a widget shown first.
      visible = desk_changed ? false : after['widget_visible']
      tool_steps = []
      if before['active_tool'] != after['active_tool']
        # Choosing the open tool again closes it; choosing another switches to it.
        if before['active_tool']
          tool_steps << ['tool', before['active_tool']]
        elsif after['active_tool']
          tool_steps << ['tool', after['active_tool']]
        end
      end
      tool_steps << ['tool', 'pacing'] if !!before['pacing_pinned'] != !!after['pacing_pinned']
      unless tool_steps.empty?
        # A tool only turns off on a visible widget; otherwise choosing it shows the widget.
        unless visible
          steps << ['show-widget', nil]
          visible = true
        end
        steps.concat(tool_steps)
      end
      if desk_changed || !!before['widget_visible'] != !!visible
        steps << [before['widget_visible'] ? 'show-widget' : 'hide-widget', nil]
      end
      steps << ['notch', before['notch'] ? 'on' : 'off'] if !!before['notch'] != !!after['notch']
      # The camera light by hand, only when no camera was in use (a running camera lights it too).
      if !before['camera_in_use'] && !after['camera_in_use'] && !!before['camera_light'] != !!after['camera_light']
        steps << ['camera-light', before['camera_light'] ? 'on' : 'off']
      end
      # The widget theme a scenario picked goes back to the one in use before.
      if before['theme'] && before['theme'] != after['theme']
        steps << ['theme', before['theme']]
      end
      if before['settings_open']
        if !after['settings_open'] || before['settings_section'] != after['settings_section']
          steps << ['settings', before['settings_section']]
        end
      elsif after['settings_open']
        steps << ['close-settings', nil]
      end
      steps
    end
  end

  # What a scenario's `needs` asks for: nil when met, else the reason to skip.
  module Needs
    module_function

    def unmet(need, state)
      case need.to_s
      when 'desk'
        state['desk_running'] ? nil : 'Desk is off, and this scenario does not switch it on'
      when 'notch'
        state['has_notch'] ? nil : 'no notch island (no notched built-in screen, or Desk is off)'
      when 'credentials'
        Array(state['meters']).empty? ? 'no usage numbers yet (sign in first)' : nil
      when 'widget'
        state['widget_visible'] ? nil : 'the widget is hidden'
      when 'meters'
        found, = State.dig(state, 'desk_frames[kind=meters]')
        found ? nil : 'no meters on the Desk (not in the layout, or Desk is off)'
      else
        raise Failure, "unknown need: #{need} (desk, notch, credentials, widget, meters)"
      end
    end
  end

  # HTML pages: the gallery for `view` and the self-refreshing page for `watch`.
  module Html
    module_function

    def esc(s)
      s.to_s.gsub('&', '&amp;').gsub('<', '&lt;').gsub('>', '&gt;').gsub('"', '&quot;')
    end

    def node_line(n)
      parts = [n['role'] || '?']
      parts << n['subrole'] if n['subrole']
      parts << %("#{n['label']}") if n['label']
      parts << "= #{n['value'].inspect}" if n.key?('value')
      parts << 'disabled' if n['enabled'] == false
      parts << "[#{Array(n['frame']).join(', ')}]" if n['frame']
      esc(parts.join(' '))
    end

    def nodes(list, depth = 0)
      return '' if Array(list).empty?
      items = Array(list).map do |n|
        kids = Array(n['children'])
        if kids.empty?
          "<li>#{node_line(n)}</li>"
        else
          open = depth < 2 ? ' open' : ''
          "<li><details#{open}><summary>#{node_line(n)}</summary>#{nodes(kids, depth + 1)}</details></li>"
        end
      end
      "<ul>#{items.join}</ul>"
    end

    def tree(windows)
      Array(windows).map do |w|
        title = [w['window'], w['title'] && %("#{w['title']}"), w['frame'] && "[#{Array(w['frame']).join(', ')}]"].compact.join(' ')
        "<details open><summary><b>#{esc(title)}</b></summary>#{nodes(w['tree'])}</details>"
      end.join
    end

    # One section per folder holding a snapshot (tree.yaml, state.yaml or PNGs).
    def section(dir, base)
      rel = dir == base ? File.basename(dir) : dir.sub("#{base}/", '')
      pngs = Dir[File.join(dir, '*.png')].sort
      html = +"<section><h2>#{esc(rel)}</h2>"
      unless pngs.empty?
        html << '<div class="shots">'
        pngs.each do |p|
          src = p.sub("#{base}/", '')
          html << %(<figure><a href="#{esc(src)}"><img src="#{esc(src)}" alt=""></a><figcaption>#{esc(File.basename(p))}</figcaption></figure>)
        end
        html << '</div>'
      end
      state = File.join(dir, 'state.yaml')
      html << "<details><summary>state.yaml</summary><pre>#{esc(File.read(state))}</pre></details>" if File.exist?(state)
      tree_file = File.join(dir, 'tree.yaml')
      if File.exist?(tree_file)
        begin
          html << "<details><summary>tree.yaml</summary>#{tree(YAML.safe_load(File.read(tree_file)))}</details>"
        rescue StandardError => e
          html << "<p>tree.yaml did not parse: #{esc(e.message)}</p>"
        end
      end
      err = File.join(dir, 'error')
      html << "<p class=\"err\">error: #{esc(File.read(err))}</p>" if File.exist?(err)
      html << '</section>'
    end

    def page(base, refresh: nil, title: nil)
      dirs = ([base] + Dir[File.join(base, '**', '*/')].map { |d| d.chomp('/') })
             .reject { |d| File.basename(d).start_with?('.') }
             .select { |d| Dir[File.join(d, '{*.png,tree.yaml,state.yaml}')].any? }
             .sort
      report = File.join(base, 'report.yaml')
      body = +''
      body << "<section><h2>report.yaml</h2><pre>#{esc(File.read(report))}</pre></section>" if File.exist?(report)
      dirs.each { |d| body << section(d, base) }
      body << '<p>Nothing captured yet.</p>' if dirs.empty? && !File.exist?(report)
      meta = refresh ? %(<meta http-equiv="refresh" content="#{refresh.to_i}">) : ''
      <<~HTML
        <!doctype html>
        <html><head><meta charset="utf-8">#{meta}
        <title>#{esc(title || "Smoke #{File.basename(base)}")}</title>
        <style>
          :root { color-scheme: light dark; --bg: #fff; --fg: #111; --muted: #666; --line: #ddd; --chk: #f3f3f3; }
          @media (prefers-color-scheme: dark) { :root { --bg: #161616; --fg: #eee; --muted: #999; --line: #333; --chk: #222; } }
          body { font: 13px -apple-system, system-ui, sans-serif; background: var(--bg); color: var(--fg); margin: 16px; }
          h1 { font-size: 16px; } h2 { font-size: 14px; border-bottom: 1px solid var(--line); padding-bottom: 4px; }
          .shots { display: flex; flex-wrap: wrap; gap: 12px; }
          figure { margin: 0; max-width: 480px; }
          img { max-width: 100%; max-height: 360px; border: 1px solid var(--line);
                background: repeating-conic-gradient(var(--chk) 0 25%, transparent 0 50%) 0 0 / 16px 16px; }
          figcaption { color: var(--muted); font-size: 12px; }
          pre { background: var(--chk); padding: 8px; overflow-x: auto; }
          ul { list-style: none; padding-left: 16px; margin: 0; } summary { cursor: pointer; }
          .err { color: #d33; }
        </style></head>
        <body><h1>#{esc(title || base)}</h1>#{body}</body></html>
      HTML
    end

    def write(base, refresh: nil, title: nil)
      path = File.join(base, 'index.html')
      File.write("#{path}.tmp", page(base, refresh: refresh, title: title))
      File.rename("#{path}.tmp", path)
      path
    end
  end

  # Runs scenario files against an app adapter (the live app, or a fake in selftest).
  # The adapter answers snapshot(dir, screens:), action(name, arg) and state(dir).
  class Runner
    STEP_KINDS = %w[do defaults wait snap expect expect_state].freeze

    attr_reader :results

    def initialize(app, defaults_backend, out_dir, io: $stdout, settle: 0.4, poll: 0.5, within: 3.0)
      @app = app
      @backend = defaults_backend
      @out = out_dir
      @io = io
      @settle = settle
      @poll = poll
      @within = within
      @results = []
    end

    def load(path)
      doc = YAML.safe_load(File.read(path))
      raise Failure, "#{path}: not a scenario (needs name and steps)" unless doc.is_a?(Hash) && doc['steps'].is_a?(Array)
      doc
    end

    def run_file(path)
      sc = load(path)
      index = @results.length + 1
      folder = File.join(@out, format('%02d-%s', index, File.basename(path, '.*')))
      FileUtils.mkdir_p(folder)
      result = { 'name' => sc['name'] || File.basename(path), 'file' => path, 'status' => 'pass', 'steps' => [] }
      @results << result
      @io.puts "#{result['name']}  (#{File.basename(path)})"
      before = @app.state(File.join(folder, 'start'))
      Array(sc['needs']).each do |need|
        reason = Needs.unmet(need, before)
        next unless reason
        result['status'] = 'skip'
        result['reason'] = reason
        @io.puts "  SKIP  #{reason}"
        return result
      end
      defaults = Defaults.new(@backend)
      begin
        sc['steps'].each_with_index do |step, i|
          n = i + 1
          ok, detail = run_step(step, n, folder, defaults)
          result['steps'] << { 'n' => n, 'step' => describe(step), 'status' => ok ? 'pass' : 'fail', 'detail' => detail }.reject { |_, v| v.nil? }
          @io.puts format('  %-4s  %2d  %s%s', ok ? 'ok' : 'FAIL', n, describe(step), detail ? "  (#{detail})" : '')
          next if ok
          result['status'] = 'fail'
          (n...sc['steps'].length).each { |j| result['steps'] << { 'n' => j + 1, 'step' => describe(sc['steps'][j]), 'status' => 'not run' } }
          break
        end
      rescue Interrupt
        result['status'] = 'fail'
        result['reason'] = 'interrupted'
        raise
      rescue StandardError => e
        result['status'] = 'fail'
        result['reason'] = e.message
        @io.puts "  FAIL  #{e.message}"
      ensure
        result['restored'] = restore(before, defaults, folder)
      end
      result
    end

    def restore(before, defaults, folder)
      done = defaults.restore!
      after = @app.state(File.join(folder, 'end'))
      Restore.plan(before, after).each do |name, arg|
        @app.action(name, arg)
        done << "do #{name}#{arg ? " #{arg}" : ''}"
      end
      sleep(@settle) unless done.empty? || @settle.zero?
      @io.puts "  restored: #{done.join('; ')}" unless done.empty?
      done
    rescue StandardError => e
      @io.puts "  RESTORE FAILED: #{e.message}"
      done ||= []
      done << "restore failed: #{e.message}"
    end

    def describe(step)
      return step.to_s unless step.is_a?(Hash)
      kind = (step.keys & STEP_KINDS).first || step.keys.first
      value = step[kind]
      text = value.is_a?(Hash) || value.is_a?(Array) ? JSON.generate(value) : value.to_s
      text += " #{step['arg']}" if kind == 'do' && step['arg']
      "#{kind} #{text}"
    end

    def run_step(step, n, folder, defaults)
      raise Failure, "step #{n} is not a map" unless step.is_a?(Hash)
      kind = (step.keys & STEP_KINDS).first
      raise Failure, "step #{n}: unknown step (#{step.keys.join(', ')})" unless kind
      case kind
      when 'do'
        @app.action(step['do'].to_s, step['arg']&.to_s)
        sleep(@settle) unless @settle.zero?
        [true, nil]
      when 'defaults'
        d = step['defaults']
        raise Failure, "step #{n}: defaults needs key, type and value" unless d.is_a?(Hash) && d['key'] && d['type'] && d.key?('value')
        defaults.write(d['domain'], d['key'], d['type'], d['value'])
        [true, nil]
      when 'wait'
        sleep(step['wait'].to_f)
        [true, nil]
      when 'snap'
        dir = File.join(folder, format('snap-%02d-%s', n, step['snap'].to_s.gsub(/[^\w.-]+/, '-')))
        @app.snapshot(dir, screens: true)
        [true, dir.sub("#{@out}/", '')]
      when 'expect'
        expect_node(step['expect'], n, folder)
      when 'expect_state'
        expect_state(step['expect_state'], n, folder)
      end
    end

    # Polls until the condition holds or `within` seconds pass (the UI settles asynchronously).
    def poll(within)
      deadline = Time.now + within
      loop do
        ok, detail = yield
        return [ok, detail] if ok || Time.now >= deadline
        sleep(@poll)
      end
    end

    def expect_node(spec, n, folder)
      raise Failure, "step #{n}: expect needs window and contains or missing" unless spec.is_a?(Hash) && spec['window'] && (spec['contains'] || spec['missing'])
      dir = File.join(folder, format('step-%02d', n))
      poll(spec.fetch('within', @within).to_f) do
        tree = YAML.safe_load(File.read(File.join(@app.snapshot(dir, screens: false), 'tree.yaml')))
        if spec['contains']
          node, why = Match.in_window(tree, spec['window'], spec['contains'])
          node ? [true, nil] : [false, why || "no node like #{JSON.generate(spec['contains'])} in #{spec['window']}"]
        else
          node, = Match.in_window(tree, spec['window'], spec['missing'])
          node ? [false, "found #{JSON.generate(node.reject { |k, _| k == 'children' })}"] : [true, nil]
        end
      end
    end

    def expect_state(expected, n, folder)
      raise Failure, "step #{n}: expect_state needs a map of keys" unless expected.is_a?(Hash)
      dir = File.join(folder, format('step-%02d', n))
      poll(@within) do
        errors = State.check(@app.state(dir), expected)
        errors.empty? ? [true, nil] : [false, errors.join('; ')]
      end
    end

    def report
      { 'scenarios' => @results,
        'summary' => %w[pass fail skip].map { |s| [s, @results.count { |r| r['status'] == s }] }.to_h }
    end
  end
end
