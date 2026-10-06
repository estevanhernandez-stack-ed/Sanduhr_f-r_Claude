// Sanduhr Time panel. Plain DOM, no framework. Every string from the model goes through
// textContent: project names are user data, so nothing here builds markup from data.
(function () {
  'use strict';
  var vscode = acquireVsCodeApi();
  var app = document.getElementById('app');

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  function button(label, onClick, cls) {
    var b = el('button', cls, label);
    b.type = 'button';
    b.addEventListener('click', onClick);
    return b;
  }

  function pct(f) {
    return (Math.round(f * 10000) / 100).toString() + '%';
  }

  function bar(fr, split, thin) {
    var b = el('div', 'bar' + (thin ? ' thin' : ''));
    b.setAttribute('role', 'img');
    b.setAttribute(
      'aria-label',
      'You ' + split.you + ', Claude ' + split.claude + ', Both ' + split.both
    );
    ['you', 'claude', 'both'].forEach(function (k) {
      var s = el('div', 'seg ' + k);
      s.style.width = pct(fr[k]);
      s.title = { you: 'You', claude: 'Claude', both: 'Both' }[k] + ' ' + split[k];
      b.appendChild(s);
    });
    return b;
  }

  function legend(split) {
    var ul = el('ul', 'split');
    [['you', 'You'], ['claude', 'Claude'], ['both', 'Both']].forEach(function (p) {
      var li = el('li');
      li.appendChild(el('span', 'swatch ' + p[0]));
      li.appendChild(el('span', null, p[1] + ' ' + split[p[0]]));
      ul.appendChild(li);
    });
    return ul;
  }

  function renderHeadline(m) {
    var h = el('section', 'headline');
    var top = el('div', 'headline-top');
    var left = el('div');
    left.appendChild(el('p', 'eyebrow', 'Today'));
    var total = el('p', 'headline-total', m.headline.total);
    left.appendChild(total);
    top.appendChild(left);

    var tools = el('div', 'toolbar');
    tools.appendChild(
      button(
        m.streamerMode ? 'Streamer mode: on' : 'Streamer mode: off',
        function () { vscode.postMessage({ type: 'toggleStreamer' }); },
        m.streamerMode ? 'primary' : ''
      )
    );
    tools.appendChild(button('Refresh', function () { vscode.postMessage({ type: 'refresh' }); }));
    top.appendChild(tools);
    h.appendChild(top);

    h.appendChild(legend(m.headline));
    h.appendChild(bar(m.headlineFractions, m.headline, false));
    return h;
  }

  function renderStrip(m) {
    var wrap = el('section');
    wrap.appendChild(el('h2', null, 'Last 7 days'));
    var strip = el('div', 'strip');
    m.strip.forEach(function (d) {
      var cell = el('div', 'day' + (d.isToday ? ' today' : ''));
      var col = el('div', 'day-col');
      var stack = el('div', 'stack' + (d.height > 0 ? '' : ' zero'));
      if (d.height > 0) {
        stack.style.height = Math.max(4, Math.round(d.height * 64)) + 'px';
        ['both', 'claude', 'you'].forEach(function (k) {
          var s = el('div', 'seg ' + k);
          s.style.height = pct(d.fractions[k]);
          stack.appendChild(s);
        });
      }
      stack.title = 'You ' + d.split.you + ', Claude ' + d.split.claude + ', Both ' + d.split.both;
      col.appendChild(stack);
      cell.appendChild(col);
      cell.appendChild(el('div', 'day-total', d.split.total));
      cell.appendChild(el('div', 'day-label', d.label));
      strip.appendChild(cell);
    });
    wrap.appendChild(strip);
    return wrap;
  }

  function renderProjects(m) {
    var wrap = el('section');
    wrap.appendChild(el('h2', null, 'Projects today'));
    if (m.projects.length === 0) {
      wrap.appendChild(el('p', 'muted', 'No project time yet today.'));
      return wrap;
    }
    var ul = el('ul', 'projects');
    m.projects.forEach(function (p) {
      var li = el('li', 'project');
      var head = el('div', 'project-head');
      head.appendChild(el('span', 'project-name', p.name));
      if (p.masked) head.appendChild(el('span', 'pill', 'masked'));
      head.appendChild(el('span', 'project-total', p.split.total));
      li.appendChild(head);

      li.appendChild(bar(p.fractions, p.split, true));
      li.appendChild(legend(p.split));

      var meta = el('div', 'project-meta');
      meta.appendChild(el('span', null, 'Agent time ' + p.agent));
      meta.appendChild(el('span', null, 'Your lines ' + p.linesYou));
      meta.appendChild(el('span', null, "Claude's lines " + p.linesClaude));
      if (p.languages.length > 0) {
        meta.appendChild(
          el(
            'span',
            null,
            p.languages
              .map(function (l) { return l.id + ' ' + l.time; })
              .join(' · ')
          )
        );
      }
      li.appendChild(meta);

      var actions = el('div', 'project-actions');
      actions.appendChild(
        button(p.maskFlag ? 'Unmask' : 'Mask', function () {
          vscode.postMessage({ type: 'mask', key: p.key, masked: !p.maskFlag });
        })
      );
      actions.appendChild(
        button('New alias', function () { vscode.postMessage({ type: 'reroll', key: p.key }); })
      );
      li.appendChild(actions);
      ul.appendChild(li);
    });
    wrap.appendChild(ul);
    return wrap;
  }

  function note(text, cls) {
    return el('p', 'note' + (cls ? ' ' + cls : ''), text);
  }

  function render(m) {
    app.textContent = '';
    if (m.remote) {
      app.appendChild(note(m.notice));
      app.appendChild(el('p', 'footer', m.footer));
      return;
    }
    if (m.notice) app.appendChild(note(m.notice));
    if (m.empty) {
      app.appendChild(el('div', 'empty', m.emptyMessage));
    } else {
      app.appendChild(renderHeadline(m));
      if (m.compare) app.appendChild(note(m.compare));
      app.appendChild(renderStrip(m));
      app.appendChild(renderProjects(m));
      if (m.caveats) app.appendChild(note(m.caveats));
    }
    app.appendChild(el('p', 'footer', m.footer));
  }

  window.addEventListener('message', function (event) {
    var msg = event.data;
    if (!msg) return;
    if (msg.type === 'model') render(msg.model);
    else if (msg.type === 'error') {
      var old = document.getElementById('error-note');
      if (old) old.remove();
      var n = note('Something went wrong: ' + msg.message, 'error');
      n.id = 'error-note';
      app.insertBefore(n, app.firstChild);
    }
  });

  vscode.postMessage({ type: 'ready' });
})();
