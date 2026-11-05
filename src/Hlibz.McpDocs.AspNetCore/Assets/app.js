/* Hlibz.McpDocs — read-only docs page for MCP servers. No dependencies, no inline script. */
(function () {
  'use strict';

  var root = document.documentElement;
  var THEME_KEY = 'mcp-docs-theme';
  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) { return null; } }
  var saved = store(THEME_KEY);
  if (saved === 'dark' || saved === 'light') root.setAttribute('data-theme', saved);

  document.addEventListener('DOMContentLoaded', init);

  // ---------------------------------------------------------------- helpers
  function el(tag, attrs) {
    var n = document.createElement(tag);
    if (attrs) for (var k in attrs) {
      var v = attrs[k];
      if (v == null || v === false) continue;
      if (k === 'class') n.className = v;
      else if (k === 'text') n.textContent = v;
      else if (k === 'html') n.innerHTML = v; // only ever fed by md(), which escapes first
      else if (k.slice(0, 2) === 'on') n.addEventListener(k.slice(2), v);
      else n.setAttribute(k, v === true ? '' : v);
    }
    for (var i = 2; i < arguments.length; i++) append(n, arguments[i]);
    return n;
  }
  function append(n, c) {
    if (c == null || c === false) return;
    if (Array.isArray(c)) { c.forEach(function (x) { append(n, x); }); return; }
    n.appendChild(typeof c === 'string' || typeof c === 'number' ? document.createTextNode(String(c)) : c);
  }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  // Small, safe Markdown subset: paragraphs, "- " lists, **bold**, *italic*, `code`, [text](https://…)
  function inline(s) {
    var codes = [];
    s = esc(s).replace(/`([^`]+)`/g, function (_, c) { codes.push(c); return '\u0000' + (codes.length - 1) + '\u0000'; });
    s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
         .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>')
         .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" rel="noopener noreferrer" target="_blank">$1</a>');
    return s.replace(/\u0000(\d+)\u0000/g, function (_, i) { return '<code>' + codes[+i] + '</code>'; });
  }
  function md(text) {
    if (!text) return '';
    return String(text).replace(/\r\n/g, '\n').split(/\n{2,}/).map(function (block) {
      var lines = block.split('\n');
      if (lines.every(function (l) { return /^\s*[-*] /.test(l); }))
        return '<ul>' + lines.map(function (l) { return '<li>' + inline(l.replace(/^\s*[-*] /, '')) + '</li>'; }).join('') + '</ul>';
      return '<p>' + lines.map(inline).join('<br>') + '</p>';
    }).join('');
  }
  function mdEl(tag, cls, text) { return text ? el(tag, { class: cls ? cls + ' md' : 'md', html: md(text) }) : null; }

  function highlight(json) {
    var frag = document.createDocumentFragment();
    var re = /("(?:\\.|[^"\\])*")(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g;
    var last = 0, m;
    while ((m = re.exec(json))) {
      if (m.index > last) frag.appendChild(document.createTextNode(json.slice(last, m.index)));
      var cls = m[1] ? (m[2] ? 'k' : 's') : m[3] ? 'b' : 'n';
      frag.appendChild(el('span', { class: cls, text: m[1] || m[0] }));
      if (m[2]) frag.appendChild(document.createTextNode(m[2]));
      last = re.lastIndex;
    }
    if (last < json.length) frag.appendChild(document.createTextNode(json.slice(last)));
    return frag;
  }
  function codeBlock(text, isJson) { var pre = el('pre', { class: 'code' }); append(pre, isJson ? highlight(text) : text); return pre; }

  function copyButton(getText) {
    return el('button', { class: 'btn', type: 'button', onclick: function (e) {
      var b = e.currentTarget, t = getText();
      var done = function () { b.textContent = 'Copied'; setTimeout(function () { b.textContent = 'Copy'; }, 1400); };
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(t).then(done, function () {});
    } }, 'Copy');
  }
  function panelBlock(title, text, isJson) {
    return [el('div', { class: 'panel-title' }, el('span', { text: title }), copyButton(function () { return text; })), codeBlock(text, isJson)];
  }

  var ICON_SUN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>';
  var ICON_MOON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>';
  var ICON_MENU = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 6h16M4 12h16M4 18h16"/></svg>';
  var ICON_LOCK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>';

  function isDark() {
    var t = root.getAttribute('data-theme');
    return t ? t === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  // ---------------------------------------------------------------- JSON Schema
  function resolve(s, rootSchema) {
    var guard = 0;
    while (s && typeof s === 'object' && typeof s.$ref === 'string' && s.$ref.charAt(0) === '#' && guard++ < 20) {
      var target = s.$ref.slice(1).split('/').filter(Boolean).reduce(function (o, p) {
        return o == null ? o : o[p.replace(/~1/g, '/').replace(/~0/g, '~')];
      }, rootSchema);
      if (!target) break;
      var rest = Object.assign({}, s); delete rest.$ref;
      s = Object.assign({}, target, rest);
    }
    return s || {};
  }
  function refName(s) { if (!s || typeof s.$ref !== 'string') return null; return s.$ref === '#' ? 'Root' : s.$ref.split('/').pop(); }
  function types(s) { var t = s.type; return t == null ? [] : Array.isArray(t) ? t : [t]; }
  function nonNull(s) { return types(s).filter(function (t) { return t !== 'null'; }); }
  function isNullable(s) {
    return types(s).indexOf('null') >= 0 || ['anyOf', 'oneOf'].some(function (k) { return Array.isArray(s[k]) && s[k].some(function (x) { return x && x.type === 'null'; }); });
  }
  function variants(s) {
    var v = s.oneOf || s.anyOf;
    if (!Array.isArray(v)) return null;
    v = v.filter(function (x) { return !(x && x.type === 'null'); });
    return v.length > 1 ? v : null;
  }
  function single(s, rootSchema) { // unwrap anyOf:[X, null]
    var v = s.oneOf || s.anyOf;
    if (Array.isArray(v)) { var nn = v.filter(function (x) { return !(x && x.type === 'null'); }); if (nn.length === 1) return Object.assign({}, s, { oneOf: undefined, anyOf: undefined }, nn[0]); }
    return s;
  }

  function typeLabel(raw, rootSchema, depth) {
    depth = depth || 0;
    var name = refName(raw);
    var s = resolve(single(raw, rootSchema), rootSchema);
    if (depth > 4) return [el('span', { text: name || 'any' })];
    if (variants(s)) return [el('span', { text: 'one of' })];
    var t = nonNull(s);
    if (t[0] === 'array') {
      var it = s.items || {};
      return [el('span', { text: 'array of ' })].concat(typeLabel(it, rootSchema, depth + 1));
    }
    if ((t[0] === 'object' || !t.length) && s.additionalProperties && typeof s.additionalProperties === 'object' && !s.properties)
      return [el('span', { text: 'map of ' })].concat(typeLabel(s.additionalProperties, rootSchema, depth + 1));
    var out = [];
    if (name) out.push(el('span', { class: 'ref', text: name }));
    else out.push(el('span', { text: t.length ? t.join(' | ') : (s.enum ? 'enum' : s.const !== undefined ? 'const' : 'any') }));
    if (s.format) out.push(el('span', { text: ' <' + s.format + '>' }));
    return out;
  }

  function constraintChips(s) {
    var chips = [];
    function chip(label, val) { chips.push(el('span', { class: 'chip' }, label ? label + ' ' : '', el('code', { text: typeof val === 'string' ? val : JSON.stringify(val) }))); }
    if (Array.isArray(s.enum)) {
      var vals = s.enum.filter(function (x) { return x !== null; });
      chips.push(el('span', { class: 'chip' }, 'one of ', vals.map(function (v, i) { return [i ? ', ' : '', el('code', { text: JSON.stringify(v) })]; })));
    }
    if (s.const !== undefined) chip('=', s.const);
    if (s.default !== undefined) chip('default', s.default);
    if (s.minimum !== undefined) chip('≥', s.minimum);
    if (s.exclusiveMinimum !== undefined) chip('>', s.exclusiveMinimum);
    if (s.maximum !== undefined) chip('≤', s.maximum);
    if (s.exclusiveMaximum !== undefined) chip('<', s.exclusiveMaximum);
    if (s.minLength !== undefined) chip('min length', s.minLength);
    if (s.maxLength !== undefined) chip('max length', s.maxLength);
    if (s.pattern) chip('pattern', s.pattern);
    if (s.minItems !== undefined) chip('min items', s.minItems);
    if (s.maxItems !== undefined) chip('max items', s.maxItems);
    if (s.uniqueItems) chips.push(el('span', { class: 'chip', text: 'unique items' }));
    if (Array.isArray(s.examples) && s.examples.length) chip('example', s.examples[0]);
    return chips.length ? el('div', { class: 'chips' }, chips) : null;
  }

  // The object whose fields should be shown nested under a property (object, array of objects, map of objects).
  function childObject(raw, rootSchema) {
    var s = resolve(single(raw, rootSchema), rootSchema);
    if (s.properties) return s;
    if (nonNull(s)[0] === 'array' && s.items) { var it = resolve(single(s.items, rootSchema), rootSchema); if (it.properties || variants(it)) return it; }
    if (s.additionalProperties && typeof s.additionalProperties === 'object') { var ap = resolve(single(s.additionalProperties, rootSchema), rootSchema); if (ap.properties) return ap; }
    if (variants(s)) return s;
    return null;
  }

  function propsView(objSchema, rootSchema, seen, depth) {
    var list = el('div', { class: 'props' });
    var required = objSchema.required || [];
    var props = objSchema.properties || {};
    Object.keys(props).forEach(function (name) { list.appendChild(propView(name, props[name], required.indexOf(name) >= 0, rootSchema, seen, depth)); });
    return list;
  }

  function propView(name, raw, required, rootSchema, seen, depth) {
    var s = resolve(single(raw, rootSchema), rootSchema);
    var head = el('div', { class: 'prop-head' },
      el('span', { class: 'prop-name', text: name }),
      el('span', { class: 'prop-type' }, typeLabel(raw, rootSchema)),
      required ? el('span', { class: 'req', text: 'required' }) : null,
      isNullable(s) || isNullable(raw) ? el('span', { class: 'opt', text: 'nullable' }) : null);
    var row = el('div', { class: 'prop' }, head, mdEl('div', 'prop-desc', s.description || raw.description), constraintChips(s));

    var child = childObject(raw, rootSchema);
    if (child) {
      var key = refName(raw) || refName(s.items || {}) || null;
      if (key && seen.indexOf(key) >= 0) {
        row.appendChild(el('div', { class: 'chips' }, el('span', { class: 'chip', text: '↻ recursive: ' + key })));
      } else if (depth < 8) {
        var nextSeen = key ? seen.concat([key]) : seen;
        var kids = el('div', { class: 'kids' }, schemaBody(child, rootSchema, nextSeen, depth + 1));
        var n = Object.keys(child.properties || {}).length;
        var toggle = el('button', { class: 'toggle', type: 'button', onclick: function () { row.classList.toggle('collapsed'); } },
          n ? n + (n === 1 ? ' field' : ' fields') : 'variants');
        row.appendChild(toggle);
        row.appendChild(kids);
        if (depth >= 2) row.classList.add('collapsed');
      }
    }
    return row;
  }

  function schemaBody(s, rootSchema, seen, depth) {
    var v = variants(s);
    if (v) {
      return el('div', null, v.map(function (opt, i) {
        var r = resolve(opt, rootSchema);
        return [el('div', { class: 'variant-label', text: 'Option ' + (i + 1) + (refName(opt) ? ' · ' + refName(opt) : r.title ? ' · ' + r.title : '') }),
          r.properties ? propsView(r, rootSchema, seen, depth) : el('div', { class: 'prop-type' }, typeLabel(opt, rootSchema))];
      }));
    }
    return propsView(s, rootSchema, seen, depth);
  }

  function schemaView(schema) {
    if (!schema) return el('p', { class: 'none', text: 'No parameters.' });
    var s = resolve(schema, schema);
    if (!s.properties || !Object.keys(s.properties).length) {
      if (variants(s)) return schemaBody(s, schema, [], 0);
      return el('p', { class: 'none', text: 'No parameters.' });
    }
    return propsView(s, schema, ['Root'], 0);
  }

  // Example value from a schema — used for the request/response samples.
  function example(raw, rootSchema, depth, seen) {
    depth = depth || 0; seen = seen || [];
    var key = refName(raw);
    if (key && seen.indexOf(key) >= 0) return undefined;
    if (key) seen = seen.concat([key]);
    var s = resolve(single(raw, rootSchema), rootSchema);
    if (Array.isArray(s.examples) && s.examples.length) return s.examples[0];
    if (s.example !== undefined) return s.example;
    if (s.const !== undefined) return s.const;
    if (s.default !== undefined && s.default !== null) return s.default;
    if (Array.isArray(s.enum) && s.enum.length) return s.enum.filter(function (x) { return x !== null; })[0];
    var v = variants(s); if (v) return example(v[0], rootSchema, depth + 1, seen);
    var t = nonNull(s)[0] || (s.properties ? 'object' : null);
    switch (t) {
      case 'string':
        switch (s.format) {
          case 'date-time': return '2026-01-15T09:30:00Z';
          case 'date': return '2026-01-15';
          case 'time': return '09:30:00';
          case 'email': return 'user@example.com';
          case 'uri': case 'url': return 'https://example.com';
          case 'uuid': return '3fa85f64-5717-4562-b3fc-2c963f66afa6';
          case 'duration': return 'PT1H';
        }
        return 'string';
      case 'integer': return s.minimum !== undefined ? s.minimum : 0;
      case 'number': return s.minimum !== undefined ? s.minimum : 0;
      case 'boolean': return false;
      case 'array': { if (depth > 5) return []; var item = example(s.items || {}, rootSchema, depth + 1, seen); return item === undefined ? [] : [item]; }
      case 'object': {
        var o = {};
        if (depth > 5) return o;
        Object.keys(s.properties || {}).forEach(function (k) { var v = example(s.properties[k], rootSchema, depth + 1, seen); if (v !== undefined) o[k] = v; });
        if (!s.properties && s.additionalProperties && typeof s.additionalProperties === 'object') o.key = example(s.additionalProperties, rootSchema, depth + 1, seen);
        return o;
      }
    }
    return null;
  }

  // ---------------------------------------------------------------- annotations
  // Spec defaults: readOnly=false, destructive=true, idempotent=false, openWorld=true.
  function risk(tool) {
    var a = tool.annotations;
    if (!a) return { level: '', badges: [{ cls: 'plain', text: 'No annotations' }] };
    var b = [];
    var ro = a.readOnlyHint === true;
    if (ro) b.push({ cls: 'ok', text: 'Read-only' });
    else if (a.destructiveHint === false) b.push({ cls: 'warn', text: 'Modifies data' });
    else b.push({ cls: 'danger', text: 'Destructive' });
    if (!ro && a.idempotentHint === true) b.push({ cls: 'info', text: 'Idempotent' });
    b.push(a.openWorldHint === false ? { cls: 'plain', text: 'Closed world' } : { cls: 'info', text: 'Open world' });
    return { level: ro ? 'ok' : a.destructiveHint === false ? 'warn' : 'danger', badges: b };
  }
  var RISK_TITLES = {
    'Read-only': 'Does not modify its environment (readOnlyHint)',
    'Modifies data': 'May change state, but not destructively (destructiveHint: false)',
    'Destructive': 'May perform destructive updates (destructiveHint)',
    'Idempotent': 'Repeated calls with the same arguments have no extra effect (idempotentHint)',
    'Open world': 'Interacts with external systems (openWorldHint)',
    'Closed world': 'Works only within the server\u2019s own domain (openWorldHint: false)',
    'No annotations': 'The server does not describe this tool\u2019s side effects'
  };
  function badgeRow(list, extra) {
    return el('div', { class: 'badges' }, extra, list.map(function (b) { return el('span', { class: 'badge ' + b.cls, title: RISK_TITLES[b.text] || null, text: b.text }); }));
  }

  // ---------------------------------------------------------------- authorization
  // entry.authorization is present only on [Authorize]d primitives the viewer is allowed to see.
  // roles: inner array = any of, outer = all of (several [Authorize(Roles = …)]).
  function authParts(a) {
    var parts = [];
    (a.policies || []).forEach(function (p) { parts.push('policy ' + p); });
    (a.roles || []).forEach(function (set) { parts.push((set.length > 1 ? 'roles ' : 'role ') + set.join(' or ')); });
    if (a.custom) parts.push('custom requirements');
    return parts;
  }
  function authLabel(a) {
    var names = (a.roles || []).map(function (set) { return set.join(' | '); }).concat(a.policies || []);
    return names.length ? names.join(' · ') : a.custom ? 'Restricted' : 'Signed-in users';
  }
  function lockBadge(a) {
    if (!a) return null;
    var parts = authParts(a);
    return el('span', { class: 'badge lock', title: 'Requires authorization' + (parts.length ? ': ' + parts.join('; ') : ' (any signed-in user)') + '. Shown because you have access.' },
      el('span', { class: 'ic', html: ICON_LOCK }), authLabel(a));
  }
  function authSearch(a) { return a ? ['restricted', authLabel(a)].concat(authParts(a)).join(' ') : ''; }

  // ---------------------------------------------------------------- connect snippets
  function connectSnippets(cat) {
    var d = cat.docs || {};
    var url = new URL(d.mcpEndpoint || '/mcp', location.href).href;
    var key = d.serverKey || 'mcp-server';
    var h = d.authHeader, p = d.authPlaceholder || '<token>';
    var headers = h ? (function () { var o = {}; o[h] = p; return o; })() : undefined;
    function j(o) { return JSON.stringify(o, null, 2); }
    var srv = {};

    var claudeCode = 'claude mcp add --transport http ' + key + ' ' + url + (h ? ' \\\n  --header "' + h + ': ' + p + '"' : '');

    srv = {}; srv[key] = { type: 'http', url: url }; if (headers) srv[key].headers = headers;
    var vscode = j({ servers: srv });

    srv = {}; srv[key] = { url: url }; if (headers) srv[key].headers = headers;
    var cursor = j({ mcpServers: srv });

    srv = {}; srv[key] = { command: 'npx', args: ['-y', 'mcp-remote', url].concat(h ? ['--header', h + ': ' + p] : []) };
    var desktop = j({ mcpServers: srv });

    return { url: url, tabs: [
      { id: 'claude-code', label: 'Claude Code', hint: 'Run in a terminal:', text: claudeCode, json: false },
      { id: 'claude-desktop', label: 'Claude Desktop', hint: 'Settings → Connectors → Add custom connector with the URL above. Or add to <code>claude_desktop_config.json</code>:', text: desktop, json: true },
      { id: 'vscode', label: 'VS Code', hint: 'Add to <code>.vscode/mcp.json</code>:', text: vscode, json: true },
      { id: 'cursor', label: 'Cursor', hint: 'Add to <code>~/.cursor/mcp.json</code>:', text: cursor, json: true },
      { id: 'other', label: 'Other', hint: 'Any MCP client that supports the Streamable HTTP transport:', text: 'URL:        ' + url + '\nTransport:  Streamable HTTP' + (h ? '\nHeader:     ' + h + ': ' + p : ''), json: false }
    ] };
  }

  function connectPanel(cat) {
    var c = connectSnippets(cat);
    var current = c.tabs[0];
    var saved = store('mcp-docs-client');
    c.tabs.forEach(function (t) { if (t.id === saved) current = t; });
    var hint = el('p', { class: 'tab-hint' }), body = el('div', { id: 'connect-panel', role: 'tabpanel' });
    var tabs = el('div', { class: 'tabs', role: 'tablist' });
    function show(t) {
      current = t; store('mcp-docs-client', t.id);
      Array.prototype.forEach.call(tabs.children, function (b) {
        var on = b.dataset.id === t.id;
        b.setAttribute('aria-selected', String(on)); b.tabIndex = on ? 0 : -1;
      });
      body.setAttribute('aria-labelledby', 'connect-tab-' + t.id);
      hint.innerHTML = t.hint; // static strings above
      body.replaceChildren(codeBlock(t.text, t.json));
    }
    c.tabs.forEach(function (t) { tabs.appendChild(el('button', { class: 'tab', type: 'button', role: 'tab', id: 'connect-tab-' + t.id, 'aria-controls': 'connect-panel', 'data-id': t.id, onclick: function () { show(t); } }, t.label)); });
    tabs.addEventListener('keydown', function (e) {
      var i = c.tabs.indexOf(current), n = c.tabs.length, to = -1;
      if (e.key === 'ArrowRight') to = (i + 1) % n; else if (e.key === 'ArrowLeft') to = (i + n - 1) % n;
      else if (e.key === 'Home') to = 0; else if (e.key === 'End') to = n - 1;
      if (to < 0) return;
      e.preventDefault(); show(c.tabs[to]); tabs.children[to].focus();
    });
    tabs.setAttribute('aria-label', 'Client');
    var wrap = el('div', null,
      el('div', { class: 'panel-title' }, el('span', { text: 'Connect' }), copyButton(function () { return current.text; })),
      tabs, hint, body);
    show(current);
    return wrap;
  }

  // ---------------------------------------------------------------- sections
  function rpc(method, params) { return JSON.stringify({ jsonrpc: '2.0', id: 1, method: method, params: params }, null, 2); }

  function tokenBadge(t) {
    var n = toolTokens(t);
    return el('span', { class: 'badge ' + (n >= HEAVY_TOKENS ? 'warn' : 'plain'), title: 'Estimated size of this tool\u2019s definition in the model\u2019s context (name, description, input schema; about 4 characters per token)', text: '~' + fmtTokens(n) + ' tokens' });
  }

  function toolSection(entry) {
    var t = entry.definition;
    var r = risk(t);
    var title = t.title || (t.annotations && t.annotations.title);
    var left = el('div', { class: 'l' },
      el('h3', { class: 'op-name' }, t.name, el('a', { class: 'anchor', href: '#tool/' + encodeURIComponent(t.name), 'aria-label': 'Link to ' + t.name, text: '#' })),
      title ? el('p', { class: 'op-title', text: title }) : null,
      badgeRow(r.badges, [lockBadge(entry.authorization), issueBadge(entry), tokenBadge(t)]),
      mdEl('div', '', t.description),
      el('h4', null, 'Parameters'),
      schemaView(t.inputSchema),
      t.outputSchema ? [el('h4', null, 'Result', el('small', { text: 'structuredContent' })), schemaView(t.outputSchema)] : null,
      metaBlock(t, SHOW_META));

    var args = example(t.inputSchema || {}, t.inputSchema || {}, 0, ['Root']);
    var right = el('div', { class: 'r' }, panelBlock('Request', rpc('tools/call', { name: t.name, arguments: args || {} }), true));
    if (t.outputSchema) {
      var out = example(t.outputSchema, t.outputSchema, 0, ['Root']);
      append(right, panelBlock('Response', JSON.stringify({ jsonrpc: '2.0', id: 1, result: { content: [{ type: 'text', text: JSON.stringify(out) }], structuredContent: out } }, null, 2), true));
    } else {
      append(right, el('p', { class: 'panel-note', html: 'Returns <code>content</code> blocks (usually text). No output schema declared.' }));
    }
    return el('section', { class: 'row', id: 'tool/' + t.name, 'data-nav': 'tool/' + t.name }, left, right);
  }

  function promptSection(entry) {
    var p = entry.definition;
    var args = p.arguments || [];
    var left = el('div', { class: 'l' },
      el('h3', { class: 'op-name' }, p.name, el('a', { class: 'anchor', href: '#prompt/' + encodeURIComponent(p.name), text: '#' })),
      p.title ? el('p', { class: 'op-title', text: p.title }) : null,
      el('div', { class: 'badges' }, lockBadge(entry.authorization), issueBadge(entry), el('span', { class: 'badge plain', text: 'Prompt' })),
      mdEl('div', '', p.description),
      el('h4', null, 'Arguments'),
      args.length ? el('div', { class: 'props' }, args.map(function (a) {
        return el('div', { class: 'prop' },
          el('div', { class: 'prop-head' }, el('span', { class: 'prop-name', text: a.name }), el('span', { class: 'prop-type', text: 'string' }),
            a.required ? el('span', { class: 'req', text: 'required' }) : null),
          mdEl('div', 'prop-desc', a.description));
      })) : el('p', { class: 'none', text: 'No arguments.' }),
      metaBlock(p, SHOW_META));
    var ex = {}; args.forEach(function (a) { ex[a.name] = 'string'; });
    var right = el('div', { class: 'r' }, panelBlock('Request', rpc('prompts/get', { name: p.name, arguments: ex }), true));
    return el('section', { class: 'row', id: 'prompt/' + p.name, 'data-nav': 'prompt/' + p.name }, left, right);
  }

  function resourceSection(entry, templated) {
    var res = entry.definition;
    var kind = templated ? 'template' : 'resource';
    var addr = templated ? res.uriTemplate : res.uri;
    var vars = templated ? (addr.match(/\{[^}]+\}/g) || []).map(function (v) { return v.replace(/[{}+#./;?&*]/g, ''); }) : [];
    var left = el('div', { class: 'l' },
      el('h3', { class: 'op-name' }, res.name, el('a', { class: 'anchor', href: '#' + kind + '/' + encodeURIComponent(res.name), text: '#' })),
      res.title ? el('p', { class: 'op-title', text: res.title }) : null,
      el('div', { class: 'badges' }, lockBadge(entry.authorization), issueBadge(entry), el('span', { class: 'badge plain', text: templated ? 'Resource template' : 'Resource' }),
        res.mimeType ? el('span', { class: 'badge plain', text: res.mimeType }) : null),
      el('div', { class: 'endpoint' }, el('span', { class: 'tag', text: 'URI' }), el('code', { text: addr })),
      mdEl('div', '', res.description),
      annotationRows(res).length ? el('dl', { class: 'facts' }, annotationRows(res).map(function (r) { return [el('dt', { text: r[0] }), el('dd', { text: r[1] })]; })) : null,
      vars.length ? [el('h4', null, 'Variables'), el('div', { class: 'props' }, vars.map(function (v) {
        return el('div', { class: 'prop' }, el('div', { class: 'prop-head' }, el('span', { class: 'prop-name', text: v }), el('span', { class: 'prop-type', text: 'string' }), el('span', { class: 'req', text: 'required' })));
      }))] : null,
      metaBlock(res, SHOW_META));
    var sampleUri = templated ? addr.replace(/\{[^}]+\}/g, function (m) { return m.replace(/[{}+#./;?&*]/g, '') === '' ? '' : 'example'; }) : addr;
    var right = el('div', { class: 'r' }, panelBlock('Request', rpc('resources/read', { uri: sampleUri }), true));
    return el('section', { class: 'row', id: kind + '/' + res.name, 'data-nav': kind + '/' + res.name }, left, right);
  }

  // Rough cost of a tool in a model's context: name, description and input schema, ~4 characters per token.
  // An estimate (real tokenizers differ, clients send somewhat different fields), shown as such.
  function toolTokens(t) {
    return Math.ceil(JSON.stringify({ name: t.name, description: t.description, inputSchema: t.inputSchema }).length / 4);
  }
  function fmtTokens(n) { return n >= 10000 ? Math.round(n / 1000) + 'k' : n >= 1000 ? (n / 1000).toFixed(1) + 'k' : String(n); }
  var HEAVY_TOKENS = 1000;

  function metaBlock(def, show) {
    if (!show || !def._meta || typeof def._meta !== 'object' || !Object.keys(def._meta).length) return null;
    return [el('h4', null, 'Metadata', el('small', { text: '_meta' })), codeBlock(JSON.stringify(def._meta, null, 2), true)];
  }

  function fmtBytes(n) { return n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(1).replace(/\.0$/, '') + ' KB' : (n / 1048576).toFixed(1).replace(/\.0$/, '') + ' MB'; }
  function annotationRows(res) {
    var a = res.annotations || {}, rows = [];
    if (Array.isArray(a.audience) && a.audience.length) rows.push(['Audience', a.audience.join(', ')]);
    if (typeof a.priority === 'number') rows.push(['Priority', String(a.priority)]);
    if (a.lastModified) { var dt = new Date(a.lastModified); rows.push(['Modified', isNaN(dt) ? String(a.lastModified) : dt.toLocaleString()]); }
    if (typeof res.size === 'number') rows.push(['Size', fmtBytes(res.size)]);
    return rows;
  }

  function heading(id, label, count) {
    return el('div', { class: 'row heading', id: id }, el('div', { class: 'l' }, el('h2', { class: 'section', text: label + ' · ' + count })), el('div', { class: 'r' }));
  }

  // ---------------------------------------------------------------- page
  function groupTools(tools) {
    var groups = {}, order = [];
    tools.forEach(function (e) { var g = e.group || ''; if (!groups[g]) { groups[g] = []; order.push(g); } groups[g].push(e); });
    order.sort(function (a, b) { return a === '' ? 1 : b === '' ? -1 : a.localeCompare(b); });
    var named = order.some(function (g) { return g !== ''; });
    return order.map(function (g) { return { name: g || (named ? 'Other' : ''), items: groups[g] }; });
  }

  // MCP icons: { src, mimeType?, sizes?, theme? }. Only https: and data:image URLs are used (the CSP allows no more),
  // and an <img> never runs script, svg included.
  function iconUrl(icons) {
    if (!Array.isArray(icons)) return null;
    var dark = isDark();
    var ok = icons.filter(function (i) { return i && typeof i.src === 'string' && /^(https:\/\/|data:image\/(png|jpeg|gif|webp|svg\+xml)[;,])/i.test(i.src); });
    var pick = ok.filter(function (i) { return i.theme === (dark ? 'dark' : 'light'); })[0]
      || ok.filter(function (i) { return !i.theme; })[0] || ok[0];
    return pick ? pick.src : null;
  }
  function iconEl(icons, cls) {
    var src = iconUrl(icons);
    return src ? el('img', { class: 'mcp-icon ' + (cls || ''), src: src, alt: '', width: 16, height: 16, loading: 'lazy', decoding: 'async', referrerpolicy: 'no-referrer' }) : null;
  }

  var SHOW_META = false;
  // entry.issues: description checks from the server (missing descriptions, missing annotations).
  function issueBadge(entry) {
    var n = entry.issues && entry.issues.length;
    return n ? el('span', { class: 'badge warn', title: entry.issues.join('\n'), text: n + (n === 1 ? ' issue' : ' issues') }) : null;
  }
  function qualityPanel(cat) {
    var rows = [];
    [['tool', 'tools', function (d) { return d.name; }], ['prompt', 'prompts', function (d) { return d.name; }],
     ['resource', 'resources', function (d) { return d.name; }], ['template', 'resourceTemplates', function (d) { return d.name; }]].forEach(function (k) {
      (cat[k[1]] || []).forEach(function (e) {
        if (e.issues && e.issues.length) rows.push(el('li', null, el('a', { href: '#' + k[0] + '/' + encodeURIComponent(k[2](e.definition)), text: k[2](e.definition) }), ' ' + e.issues.join(' ')));
      });
    });
    if (!rows.length) return null;
    return el('details', { class: 'quality' }, el('summary', { text: 'Description check: ' + rows.length + (rows.length === 1 ? ' item needs' : ' items need') + ' attention' }),
      el('p', { class: 'none', text: 'The description is what a model reads to decide how to use a tool, so a missing one is a bug.' }), el('ul', null, rows));
  }
  function capChips(c) {
    if (!c) return null;
    var chips = [];
    if (c.completions) chips.push(['Completions', 'completion/complete for prompt arguments and resource templates']);
    if (c.subscribe) chips.push(['Subscriptions', 'Clients can subscribe to resource updates']);
    return chips.length ? el('div', { class: 'badges caps' }, chips.map(function (x) { return el('span', { class: 'badge plain', title: x[1], text: x[0] }); })) : null;
  }
  // Links from AddToolbarItem, plus the server's website. Only http(s) or same-site paths become links.
  function toolbar(server, d) {
    var links = (d.links || []).slice();
    if (typeof server.websiteUrl === 'string' && /^https?:\/\//i.test(server.websiteUrl)) links.unshift({ label: 'Website', url: server.websiteUrl, newTab: true });
    links = links.filter(function (l) { return l && typeof l.url === 'string' && /^(https?:\/\/|\/(?!\/))/i.test(l.url); });
    if (!links.length) return null;
    return el('nav', { class: 'toolbar', 'aria-label': 'Links' }, links.map(function (l) {
      return el('a', l.newTab ? { href: l.url, target: '_blank', rel: 'noopener noreferrer', text: l.label } : { href: l.url, text: l.label });
    }));
  }
  function render(app, cat) {
    var server = cat.server || {};
    var d = cat.docs || {};
    var tools = cat.tools || [], prompts = cat.prompts || [], resources = cat.resources || [], templates = cat.resourceTemplates || [];
    var groups = groupTools(tools);
    SHOW_META = d.showMeta === true;
    var title = d.title || server.title || server.name || 'MCP server';
    document.title = title;
    var fav = (typeof d.favicon === 'string' && d.favicon) || iconUrl(server.icons);
    if (fav) {
      var link = document.querySelector('link[rel~="icon"]') || document.head.appendChild(el('link', { rel: 'icon' }));
      link.setAttribute('href', fav);
    }

    // --- sidebar
    var navItems = [];
    function navItem(id, name, kind, dot, searchText, auth, icons) {
      var a = el('a', { class: 'nav-item', href: '#' + id, 'data-target': id, 'data-search': (name + ' ' + (searchText || '') + ' ' + authSearch(auth)).toLowerCase() },
        dot !== null ? el('span', { class: 'dot ' + (dot || '') }) : null,
        iconEl(icons),
        el('span', { class: 'nm', text: name, title: name }),
        auth ? el('span', { class: 'lock', title: 'Restricted: ' + authLabel(auth), html: ICON_LOCK }) : null,
        kind ? el('span', { class: 'kind', text: kind }) : null);
      navItems.push(a);
      return a;
    }
    var nav = el('nav', { class: 'nav', 'aria-label': 'Contents' });
    append(nav, el('div', { class: 'nav-section' }, navItem('overview', 'Overview & connect', null, null, 'connect setup install')));
    if (tools.length) append(nav, el('div', { class: 'nav-section', 'data-section': '' },
      el('div', { class: 'nav-label' }, el('span', { text: 'Tools' }), el('span', { text: String(tools.length) })),
      groups.map(function (g) {
        return el('div', { 'data-group': '' }, g.name ? el('div', { class: 'nav-group', text: g.name }) : null,
          g.items.map(function (e) { var t = e.definition; return navItem('tool/' + t.name, t.name, null, risk(t).level, [t.title, t.description, g.name].join(' '), e.authorization, t.icons); }));
      })));
    if (prompts.length) append(nav, el('div', { class: 'nav-section', 'data-section': '' },
      el('div', { class: 'nav-label' }, el('span', { text: 'Prompts' }), el('span', { text: String(prompts.length) })),
      el('div', { 'data-group': '' }, prompts.map(function (e) { var p = e.definition; return navItem('prompt/' + p.name, p.name, null, null, [p.title, p.description].join(' '), e.authorization, p.icons); }))));
    if (resources.length + templates.length) append(nav, el('div', { class: 'nav-section', 'data-section': '' },
      el('div', { class: 'nav-label' }, el('span', { text: 'Resources' }), el('span', { text: String(resources.length + templates.length) })),
      el('div', { 'data-group': '' },
        resources.map(function (e) { var r = e.definition; return navItem('resource/' + r.name, r.name, null, null, [r.title, r.description, r.uri].join(' '), e.authorization, r.icons); }),
        templates.map(function (e) { var r = e.definition; return navItem('template/' + r.name, r.name, '{ }', null, [r.title, r.description, r.uriTemplate].join(' '), e.authorization, r.icons); }))));
    var empty = el('div', { class: 'nav-empty', role: 'status', text: 'No matches.', hidden: true });
    append(nav, empty);

    var search = el('input', { type: 'search', placeholder: 'Search', 'aria-label': 'Search tools, prompts and resources', autocomplete: 'off' });
    search.addEventListener('input', function () {
      var q = search.value.trim().toLowerCase(), any = false;
      navItems.forEach(function (a) { var hit = !q || a.dataset.search.indexOf(q) >= 0; a.hidden = !hit; any = any || hit; });
      nav.querySelectorAll('[data-group]').forEach(function (g) { g.hidden = !g.querySelector('.nav-item:not([hidden])'); });
      nav.querySelectorAll('[data-section]').forEach(function (s) { s.hidden = !s.querySelector('.nav-item:not([hidden])'); });
      empty.hidden = any;
    });
    function visibleItems() { return navItems.filter(function (a) { return !a.hidden; }); }
    nav.addEventListener('keydown', function (e) {
      if (['ArrowDown', 'ArrowUp', 'Home', 'End'].indexOf(e.key) < 0) return;
      var items = visibleItems(), i = items.indexOf(document.activeElement);
      if (i < 0) return;
      e.preventDefault();
      if (e.key === 'ArrowUp' && i === 0) { search.focus(); return; }
      var to = e.key === 'ArrowDown' ? Math.min(i + 1, items.length - 1) : e.key === 'ArrowUp' ? i - 1 : e.key === 'Home' ? 0 : items.length - 1;
      items[to].focus();
    });
    search.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { var f = visibleItems()[0]; if (f) { e.preventDefault(); f.focus(); } }
      if (e.key === 'Escape') { search.value = ''; search.dispatchEvent(new Event('input')); search.blur(); }
      if (e.key === 'Enter') { var first = navItems.filter(function (a) { return !a.hidden; })[0]; if (first) { first.click(); search.blur(); } }
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === '/' && document.activeElement !== search && !/input|textarea/i.test(document.activeElement.tagName)) { e.preventDefault(); search.focus(); }
    });

    var themeBtn = el('button', { class: 'icon-btn', type: 'button', 'aria-label': 'Toggle dark mode', title: 'Toggle dark mode' });
    function paintThemeBtn() { themeBtn.innerHTML = isDark() ? ICON_SUN : ICON_MOON; themeBtn.setAttribute('aria-pressed', String(isDark())); }
    themeBtn.addEventListener('click', function () {
      var next = isDark() ? 'light' : 'dark';
      root.setAttribute('data-theme', next); store(THEME_KEY, next); paintThemeBtn();
    });
    paintThemeBtn();

    var sidebar = el('aside', { class: 'sidebar', id: 'sidebar', 'aria-label': 'Navigation' },
      el('div', { class: 'side-head' },
        el('div', { class: 'side-title' }, el('strong', { text: title }), server.version ? el('span', { text: 'v' + server.version }) : null),
        themeBtn),
      toolbar(server, d),
      el('div', { class: 'search' }, search, el('kbd', { text: '/' })),
      nav,
      el('div', { class: 'side-foot' }, el('a', { href: 'catalog.json', text: 'catalog.json' }), ' · read-only docs'));

    // --- content
    var counts = [
      tools.length ? tools.length + ' tools' : null,
      prompts.length ? prompts.length + ' prompts' : null,
      resources.length + templates.length ? (resources.length + templates.length) + ' resources' : null
    ].filter(Boolean);
    var endpointUrl = new URL(d.mcpEndpoint || '/mcp', location.href).href;
    var overviewLeft = el('div', { class: 'l' },
      el('h1', null, iconEl(server.icons, 'lg'), title),
      el('div', { class: 'meta' },
        server.version ? el('span', null, 'Version ', el('b', { text: server.version })) : null,
        server.name && server.name !== title ? el('span', null, 'Server ', el('b', { text: server.name })) : null,
        counts.length ? el('span', { text: counts.join(' · ') }) : null),
      capChips(cat.capabilities),
      tools.length ? el('p', { class: 'cost', title: 'Name, description and input schema of every tool, about 4 characters per token. An estimate.' },
        'Context cost: all tools together are about ', el('b', { text: '~' + fmtTokens(tools.reduce(function (n, e) { return n + toolTokens(e.definition); }, 0)) + ' tokens' }),
        ' in every conversation that enables them.') : null,
      qualityPanel(cat),
      mdEl('div', '', d.description || server.description),
      d.description && cat.instructions ? el('div', { class: 'callout' }, el('span', { class: 'lbl', text: 'Instructions for the model' }), mdEl('div', '', cat.instructions)) : null,
      !d.description && !server.description ? mdEl('div', '', cat.instructions) : null,
      el('div', { class: 'endpoint' }, el('span', { class: 'tag', text: 'HTTP' }), el('code', { text: endpointUrl }), copyButton(function () { return endpointUrl; })),
      el('p', { class: 'none' }, 'Transport: Streamable HTTP.' + (d.authHeader ? ' Requires the ' : ''), d.authHeader ? el('code', { text: d.authHeader }) : null, d.authHeader ? ' header.' : ''),
      d.authHint ? el('div', { class: 'callout' }, el('span', { class: 'lbl', text: 'Authentication' }), mdEl('div', '', d.authHint)) : null);
    var overview = el('section', { class: 'row', id: 'overview', 'data-nav': 'overview' }, overviewLeft, el('div', { class: 'r' }, connectPanel(cat)));

    var main = el('main', { class: 'content', id: 'main', tabindex: '-1' }, overview);
    if (tools.length) {
      append(main, heading('tools', 'Tools', tools.length));
      groups.forEach(function (g) {
        if (g.name) append(main, el('div', { class: 'row heading' }, el('div', { class: 'l' }, el('h3', { class: 'group', text: g.name })), el('div', { class: 'r' })));
        g.items.forEach(function (e) { append(main, toolSection(e)); });
      });
    }
    if (prompts.length) { append(main, heading('prompts', 'Prompts', prompts.length)); prompts.forEach(function (p) { append(main, promptSection(p)); }); }
    if (resources.length + templates.length) {
      append(main, heading('resources', 'Resources', resources.length + templates.length));
      resources.forEach(function (r) { append(main, resourceSection(r, false)); });
      templates.forEach(function (r) { append(main, resourceSection(r, true)); });
    }

    var layout = el('div', { class: 'layout' }, sidebar, el('div', { class: 'content-wrap', style: null }));
    var menuBtn = el('button', { class: 'icon-btn', type: 'button', 'aria-label': 'Menu', 'aria-expanded': 'false', 'aria-controls': 'sidebar', html: ICON_MENU, onclick: function () { menuBtn.setAttribute('aria-expanded', String(layout.classList.toggle('nav-open'))); } });
    var topbar = el('div', { class: 'topbar' }, menuBtn, el('strong', { text: title }));
    layout.replaceChild(el('div', { class: 'content' }, topbar, main), layout.lastChild);
    nav.addEventListener('click', function (e) { if (e.target.closest('.nav-item')) layout.classList.remove('nav-open'); menuBtn.setAttribute('aria-expanded', 'false'); });
    app.replaceChildren(el('a', { class: 'skip', href: '#main', onclick: function (e) { e.preventDefault(); main.focus(); main.scrollIntoView(); } , text: 'Skip to content' }), layout);

    // --- scroll spy + deep links
    var byTarget = {};
    navItems.forEach(function (a) { byTarget[a.dataset.target] = a; });
    var active = null;
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (!en.isIntersecting) return;
        var a = byTarget[en.target.dataset.nav];
        if (!a || a === active) return;
        if (active) { active.classList.remove('active'); active.removeAttribute('aria-current'); }
        active = a; a.classList.add('active'); a.setAttribute('aria-current', 'location');
        var nr = nav.getBoundingClientRect(), ar = a.getBoundingClientRect();
        if (ar.top < nr.top || ar.bottom > nr.bottom) a.scrollIntoView({ block: 'nearest' });
      });
    }, { rootMargin: '0px 0px -75% 0px' });
    main.querySelectorAll('[data-nav]').forEach(function (s) { observer.observe(s); });

    if (location.hash.length > 1) {
      var target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (target) target.scrollIntoView();
    }
  }

  function init() {
    var app = document.getElementById('app');
    fetch(app.getAttribute('data-catalog'), { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error(r.status + ' ' + r.statusText); return r.json(); })
      .then(function (cat) { render(app, cat); })
      .catch(function (e) { app.replaceChildren(el('div', { class: 'error', text: 'Could not load the server catalog: ' + e.message })); });
  }
})();
