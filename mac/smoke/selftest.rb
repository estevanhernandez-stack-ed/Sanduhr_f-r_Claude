#!/usr/bin/env ruby
# Offline checks for the smoke tools: node matching, dotted state lookups, defaults restore, the
# restore plan, the HTML gallery and whole scenario runs against a fixture snapshot. No running
# app and no real defaults. Exits non-zero on any failure.
require_relative 'lib/smoke_core'
require 'stringio'

FIXTURES = File.join(__dir__, 'fixtures')
SNAPSHOT = File.join(FIXTURES, 'snapshot')
$failures = 0
$checks = 0

def check(what, ok)
  $checks += 1
  return if ok
  $failures += 1
  warn "FAIL: #{what}"
end

def eq(what, got, want)
  check("#{what}: expected #{want.inspect}, got #{got.inspect}", got == want)
end

# Stands in for the running app: snapshots copy the fixture, actions change a copy of its state.
class FakeApp
  attr_reader :actions, :state_now

  def initialize(overrides = {})
    @state_now = YAML.safe_load(File.read(File.join(SNAPSHOT, 'state.yaml'))).merge(overrides)
    @actions = []
  end

  def snapshot(dir, screens: true)
    FileUtils.mkdir_p(dir)
    FileUtils.cp(File.join(SNAPSHOT, 'tree.yaml'), dir)
    File.write(File.join(dir, 'state.yaml'), YAML.dump(@state_now))
    File.write(File.join(dir, 'screen-widget.png'), '') if screens
    File.write(File.join(dir, 'done'), '')
    dir
  end

  def state(dir)
    YAML.safe_load(File.read(File.join(snapshot(dir, screens: false), 'state.yaml')))
  end

  def action(name, arg = nil)
    @actions << [name, arg].compact.join(' ')
    s = @state_now
    case name
    when 'desk' then s['desk_enabled'] = s['desk_running'] = (arg == 'on')
    when 'notch' then s['notch'] = (arg == 'on')
    when 'camera-light' then s['camera_light'] = (arg == 'on')
    when 'glow', 'pulse'
      s['glow_count'] = s['glow_count'].to_i + 1
      s['glow_shape'] = s['desk_running'] && s['notch'] ? 'island' : 'plain'
      s['pulse_count'] = s['pulse_count'].to_i + 1 if name == 'pulse'
    when 'show-widget' then s['widget_visible'] = true
    when 'hide-widget' then s['widget_visible'] = false
    when 'settings' then s['settings_open'] = true; s['settings_section'] = arg if arg
    when 'close-settings' then s['settings_open'] = false
    when 'theme' then s['theme'] = arg
    when 'tool'
      if arg == 'pacing' then s['pacing_pinned'] = !s['pacing_pinned']
      else s['active_tool'] = s['active_tool'] == arg ? nil : arg
      end
    end
  end
end

include Smoke

# --- The fixture parses as the app writes it -------------------------------------------------

tree = YAML.safe_load(File.read(File.join(SNAPSHOT, 'tree.yaml')))
state = YAML.safe_load(File.read(File.join(SNAPSHOT, 'state.yaml')))
eq('windows in the fixture', tree.map { |w| w['window'] }, %w[widget settings sheet sheet-2])
eq('quoted percent stays a string', tree[0]['tree'][0]['children'][1]['value'], '7%')
eq('quoted version stays a string', state['version'], '2.1.0')
eq('credentials store is a name, never a value', state['credentials_store'], 'file')

# --- Match -------------------------------------------------------------------------------------

eq('regex parse', Match.regex('/camera/i'), /camera/i)
eq('plain string is not a regex', Match.regex('camera'), nil)
check('number matches numeric string', Match.value?('1', 1))
check('true matches a switch reading 1', Match.value?(true, 1))
check('false matches a switch reading 0', Match.value?(false, 0))
check('false does not match 1', !Match.value?(false, 1))
check('nil matches only nil', Match.value?(nil, nil) && !Match.value?(nil, 0))
check('regex needs a value', !Match.value?('/x/', nil))

node, why = Match.in_window(tree, 'settings', { 'role' => 'AXCheckBox', 'label' => 'Extend the camera notch' })
check('finds the notch switch', node && node['value'] == 0 && why.nil?)
node, = Match.in_window(tree, 'settings', { 'label' => '/^text beside the camera/i', 'enabled' => false })
check('regex label and enabled false', !node.nil?)
node, = Match.in_window(tree, 'settings', { 'label' => 'Notch', 'value' => 1 })
check('finds a nested row', !node.nil?)
node, = Match.in_window(tree, 'settings', { 'label' => 'Extend the camera notch', 'value' => 1 })
check('every field must match', node.nil?)
node, = Match.in_window(tree, 'widget', { 'value' => '/%$/' })
eq('regex on values', node && node['value'], '7%')
node, why = Match.in_window(tree, 'notch', { 'label' => 'x' })
check('missing window says so', node.nil? && why == 'no notch window on screen')
node, = Match.in_window(tree, 'widget', { 'text' => '/^Session/' })
check('text matches a value', !node.nil?)
node, = Match.in_window(tree, 'settings', { 'text' => 'Sidebar' })
check('text matches a label', !node.nil?)
node, = Match.in_window(tree, 'sheet', { 'label' => 'Later' })
check('sheet matches its numbered duplicates', !node.nil?)
eq('window names with duplicates', Match.windows(tree, 'sheet').map { |w| w['window'] }, %w[sheet sheet-2])

# --- State -------------------------------------------------------------------------------------

eq('dotted list index', State.dig(state, 'meters.0.label'), [true, 'Session (5hr)'])
eq('dotted map', State.dig(state, 'alerts.delivery'), [true, 'banner'])
eq('present null', State.dig(state, 'meters.1.pace'), [true, nil])
eq('negative index', State.dig(state, 'meters.-1.tier'), [true, 'seven_day'])
eq('past the end', State.dig(state, 'meters.5.label'), [false, nil])
eq('missing key', State.dig(state, 'nope.x'), [false, nil])
eq('index into a map', State.dig(state, 'alerts.0'), [false, nil])
eq('state check passes', State.check(state, { 'settings_section' => 'notch', 'meters.0.percent' => 7, 'desk_running' => true }), [])
eq('state check failures', State.check(state, { 'meters.0.percent' => 8, 'gone' => 1 }),
   ['meters.0.percent: expected 8, got 7', 'gone: not in state'])

# --- Defaults restore --------------------------------------------------------------------------

mem = MemoryDefaults.new({ [DOMAIN, 'alertSound'] => %w[string Glass], [DESK_DOMAIN, 'layout'] => %w[array x] })
d = Defaults.new(mem)
d.write(nil, 'alertSound', 'string', 'Ping')
d.write('desk', 'notchWings', 'float', '50')
d.write(nil, 'alertSound', 'string', 'Basso')
eq('writes land', mem.read(DOMAIN, 'alertSound'), %w[string Basso])
eq('desk alias and cast', mem.read(DESK_DOMAIN, 'notchWings'), ['float', 50.0])
begin
  d.write('desk', 'layout', 'string', 'y')
  check('refuses to touch a type it cannot restore', false)
rescue Failure
  check('refuses to touch a type it cannot restore', mem.read(DESK_DOMAIN, 'layout') == %w[array x])
end
begin
  d.write(nil, 'k', 'date', 'x')
  check('refuses unknown write types', false)
rescue Failure
  check('refuses unknown write types', true)
end
done = d.restore!
eq('restore puts the first value back', mem.read(DOMAIN, 'alertSound'), %w[string Glass])
eq('restore deletes what was not there', mem.read(DESK_DOMAIN, 'notchWings'), nil)
eq('restore report', done, ["#{DESK_DOMAIN} notchWings deleted", "#{DOMAIN} alertSound = \"Glass\""])
eq('restore twice is a no-op', d.restore!, [])
eq('bool cast', Defaults.cast('bool', 'true'), true)
eq('defaults read-type parse', ShellDefaults.parse_type("Type is boolean\n"), 'boolean')

# --- Restore plan ------------------------------------------------------------------------------

base = { 'desk_enabled' => true, 'notch' => false, 'widget_visible' => false, 'settings_open' => false,
         'settings_section' => 'general', 'active_tool' => nil, 'pacing_pinned' => false }
eq('nothing changed', Restore.plan(base, base), [])
eq('desk and notch back', Restore.plan(base, base.merge('desk_enabled' => false, 'notch' => true)),
   [%w[desk on], ['hide-widget', nil], %w[notch off]])
eq('desk back first, then the widget as it was',
   Restore.plan(base.merge('widget_visible' => true), base.merge('desk_enabled' => false, 'widget_visible' => true)),
   [%w[desk on], ['show-widget', nil]])
eq('a tool closes on a widget shown after the desk switch',
   Restore.plan(base.merge('widget_visible' => true),
                base.merge('desk_enabled' => false, 'widget_visible' => true, 'active_tool' => 'snake')),
   [%w[desk on], ['show-widget', nil], %w[tool snake], ['show-widget', nil]])
eq('close settings opened by the scenario', Restore.plan(base, base.merge('settings_open' => true)), [['close-settings', nil]])
eq('reopen settings at its section',
   Restore.plan(base.merge('settings_open' => true, 'settings_section' => 'alerts'),
                base.merge('settings_open' => true, 'settings_section' => 'notch')), [%w[settings alerts]])
eq('tool off, then hide the widget again',
   Restore.plan(base, base.merge('active_tool' => 'snake', 'widget_visible' => true)),
   [%w[tool snake], ['hide-widget', nil]])
eq('pacing on a hidden widget shows it first',
   Restore.plan(base, base.merge('pacing_pinned' => true)),
   [['show-widget', nil], %w[tool pacing], ['hide-widget', nil]])
eq('camera light switched on by hand goes off',
   Restore.plan(base, base.merge('camera_light' => true)), [%w[camera-light off]])
eq('theme picked by the scenario goes back',
   Restore.plan(base.merge('theme' => 'obsidian'), base.merge('theme' => 'match-desk')), [%w[theme obsidian]])
eq('theme unchanged or unknown before is left alone',
   [Restore.plan(base.merge('theme' => 'aurora'), base.merge('theme' => 'aurora')),
    Restore.plan(base, base.merge('theme' => 'match-desk'))], [[], []])
eq('camera light from a running camera is left alone',
   Restore.plan(base, base.merge('camera_light' => true, 'camera_in_use' => true)), [])

# --- Scenario runs against the fake app --------------------------------------------------------

out = File.join(Smoke::OUT, '.selftest')
FileUtils.rm_rf(out)
io = StringIO.new
app = FakeApp.new('settings_open' => false, 'settings_section' => 'general')
mem = MemoryDefaults.new({ [DOMAIN, 'alertSound'] => %w[string Glass] })
runner = Runner.new(app, mem, out, io: io, settle: 0, poll: 0.01, within: 0.05)

r = runner.run_file(File.join(FIXTURES, 'scenarios', 'pass.yaml'))
eq('pass scenario passes', [r['status'], r['reason']], ['pass', nil])
eq('pass scenario ran every step', r['steps'].map { |s| s['status'] }.uniq, ['pass'])
check('snap folder made', Dir.exist?(File.join(out, '01-pass', 'snap-08-notch-settings')))
eq('pass scenario defaults restored', [mem.read(DOMAIN, 'alertSound'), mem.read(DESK_DOMAIN, 'notchWings')], [%w[string Glass], nil])
eq('pass scenario actions, then restore', app.actions, ['settings notch', 'desk off', 'desk on', 'show-widget', 'close-settings'])
eq('app state back', [app.state_now['desk_enabled'], app.state_now['settings_open']], [true, false])

app2 = FakeApp.new
runner2 = Runner.new(app2, mem, out, io: io, settle: 0, poll: 0.01, within: 0.05)
r = runner2.run_file(File.join(FIXTURES, 'scenarios', 'fail.yaml'))
eq('fail scenario fails', r['status'], 'fail')
eq('fail scenario stops at the failing step', r['steps'].map { |s| s['status'] }, ['pass', 'pass', 'fail', 'not run'])
check('failure says why', r['steps'][2]['detail'].to_s.include?('meters.0.percent: expected 99, got 7'))
eq('fail scenario still restores defaults', mem.read(DOMAIN, 'alertSound'), %w[string Glass])
eq('fail scenario still restores the notch', app2.actions, ['notch on', 'notch off'])

app4 = FakeApp.new('theme' => 'obsidian')
runner4 = Runner.new(app4, mem, out, io: io, settle: 0, poll: 0.01, within: 0.05)
r = runner4.run_file(File.join(FIXTURES, 'scenarios', 'theme-fail.yaml'))
eq('theme scenario fails after picking a theme', [r['status'], app4.actions.first], ['fail', 'theme match-desk'])
eq('failed theme scenario puts the theme back', [app4.actions.last, app4.state_now['theme']], ['theme obsidian', 'obsidian'])

app3 = FakeApp.new('meters' => [])
runner3 = Runner.new(app3, mem, out, io: io, settle: 0, poll: 0.01, within: 0.05)
r = runner3.run_file(File.join(FIXTURES, 'scenarios', 'skip.yaml'))
eq('skip scenario skips with a reason', [r['status'], r['reason']], ['skip', 'no usage numbers yet (sign in first)'])
eq('skip runs nothing', app3.actions, [])
eq('summary', runner.report['summary'], { 'pass' => 1, 'fail' => 0, 'skip' => 0 })

# --- Shipped scenarios parse and use known steps -----------------------------------------------

Dir[File.join(Smoke::SCENARIOS, '*.yaml')].sort.each do |f|
  doc = YAML.safe_load(File.read(f))
  name = File.basename(f)
  check("#{name}: has name and steps", doc.is_a?(Hash) && doc['name'].is_a?(String) && doc['steps'].is_a?(Array))
  Array(doc['needs']).each { |n| check("#{name}: known need #{n}", %w[desk notch credentials widget].include?(n)) }
  Array(doc['steps']).each_with_index do |s, i|
    kinds = s.is_a?(Hash) ? s.keys & Runner::STEP_KINDS : []
    check("#{name}: step #{i + 1} has one known kind", kinds.length == 1)
    next unless kinds == ['do']
    known = %w[show-widget hide-widget settings close-settings refresh test-alert pulse tool desk notch camera-light glow theme demo]
    check("#{name}: step #{i + 1} action #{s['do']}", known.include?(s['do']))
  end
end
# Item 32: a smoke run must never wipe real credentials, so no step may sign out.
check('no scenario signs out', Dir[File.join(Smoke::SCENARIOS, '*.yaml')].none? { |f| File.read(f) =~ /do:\s*sign[-_ ]?out/i })
check('scenarios exist', Dir[File.join(Smoke::SCENARIOS, '*.yaml')].length >= 8)

# --- Gallery -----------------------------------------------------------------------------------

page = Html.write(out)
html = File.read(page)
check('gallery lists snapshots', html.include?('snap-08-notch-settings') && html.include?('Extend the camera notch'))
check('gallery escapes', Html.esc('<a&"b">') == '&lt;a&amp;&quot;b&quot;&gt;')
FileUtils.rm_rf(out)
Dir.rmdir(Smoke::OUT) if Dir.exist?(Smoke::OUT) && Dir.empty?(Smoke::OUT)

if $failures.zero?
  puts "selftest: #{$checks} checks passed"
  exit 0
else
  puts "selftest: #{$failures} of #{$checks} checks failed"
  exit 1
end
