import { beforeEach, describe, expect, it, vi } from 'vitest';

const terminalInstances = [];
const webglInstances = [];
const searchInstances = [];
let throwWebglConstructor = false;
let throwWebglDispose = false;

vi.mock('@xterm/xterm', () => ({
  Terminal: class {
    constructor(options) {
      this.options = options;
      this.cols = 80;
      this.rows = 24;
      this.selection = '';
      this.throwOnWrite = false;
      terminalInstances.push(this);
    }

    loadAddon(addon) { addon.loaded = true; }
    open(element) { this.element = element; }
    onData(handler) { this.dataHandler = handler; }
    attachCustomKeyEventHandler(handler) { this.keyHandler = handler; }
    write(value, callback) {
      if (this.throwOnWrite) throw new Error('write failed');
      this.lastWrite = value;
      callback();
    }

    getSelection() { return this.selection; }
    clearSelection() { this.selection = ''; }
    refresh() { this.refreshed = true; }
    focus() { this.focused = true; }
    dispose() { this.disposed = true; }
  }
}));

vi.mock('@xterm/addon-fit', () => ({
  FitAddon: class { fit() { this.fitted = true; } }
}));

vi.mock('@xterm/addon-webgl', () => ({
  WebglAddon: class {
    constructor() {
      if (throwWebglConstructor) throw new Error('webgl unavailable');
      webglInstances.push(this);
    }
    onContextLoss(handler) {
      this.contextLossHandler = handler;
      return { dispose: () => { this.listenerDisposed = true; } };
    }
    dispose() {
      this.disposed = true;
      if (throwWebglDispose) throw new Error('already disposed');
    }
  }
}));

vi.mock('@xterm/addon-search', () => ({
  SearchAddon: class {
    constructor() { searchInstances.push(this); }
    onDidChangeResults(handler) {
      this.resultsHandler = handler;
      return { dispose: () => { this.listenerDisposed = true; } };
    }
    findNext(term, options) { this.next = { term, options }; return true; }
    findPrevious(term, options) { this.previous = { term, options }; return true; }
    clearDecorations() { this.cleared = true; }
  }
}));

async function loadHost({ width = 800, height = 600, bridge = true } = {}) {
  vi.resetModules();
  document.body.innerHTML = `
    <div id="terminal-host"></div>
    <div id="terminal-search" aria-hidden="true">
      <input id="terminal-search-input">
      <span id="terminal-search-results">0/0</span>
      <button id="terminal-search-case"></button>
      <button id="terminal-search-previous"></button>
      <button id="terminal-search-next"></button>
      <button id="terminal-search-close"></button>
    </div>`;
  const host = document.getElementById('terminal-host');
  host.getBoundingClientRect = () => ({ width, height });
  globalThis.ResizeObserver = class { observe() {} };
  globalThis.requestAnimationFrame = callback => { setTimeout(callback, 0); return 1; };
  const sent = [];
  if (bridge) {
    globalThis.invokeCSharpAction = body => sent.push(JSON.parse(body));
  } else {
    delete globalThis.invokeCSharpAction;
  }
  const module = await import('./terminal.js');
  return { host: window.terminalHost, sent, module };
}

function createOptions() {
  return {
    cursorBlink: true,
    cursorStyle: 'bar',
    fontFamily: 'Cascadia Mono',
    fontSize: 13,
    selectionBackground: '#264F78'
  };
}

describe('terminal host bridge', () => {
  beforeEach(() => {
    terminalInstances.length = 0;
    webglInstances.length = 0;
    searchInstances.length = 0;
    throwWebglConstructor = false;
    throwWebglDispose = false;
  });

  it('creates one isolated terminal per tab and forwards raw input', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.create({ tabId: 'a', options: createOptions() });

    expect(terminalInstances).toHaveLength(1);
    const contextMenu = new MouseEvent('contextmenu', { cancelable: true });
    terminalInstances[0].element.dispatchEvent(contextMenu);
    expect(contextMenu.defaultPrevented).toBe(true);
    terminalInstances[0].dataHandler('\u0003');
    expect(sent).toContainEqual({ type: 'input', tabId: 'a', data: '\u0003' });
  });

  it('sends ready once and resize only when dimensions change', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.activate('a');
    host.configure({ tabId: 'a', options: createOptions() });
    terminalInstances[0].cols = 100;
    host.configure({ tabId: 'a', options: createOptions() });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(sent.filter(message => message.type === 'ready')).toHaveLength(1);
    expect(sent.filter(message => message.type === 'resize')).toEqual([
      { type: 'resize', tabId: 'a', columns: 100, rows: 24 }
    ]);
  });

  it('acknowledges writes and reports missing or failed terminals', async () => {
    const { host, sent } = await loadHost();
    host.write('missing', 1, 'x');
    host.create({ tabId: 'a', options: createOptions() });
    host.write('a', 2, 'ok');
    terminalInstances[0].throwOnWrite = true;
    host.write('a', 3, 'bad');

    expect(sent.filter(message => message.type === 'writeComplete')).toEqual([
      { type: 'writeComplete', tabId: 'missing', token: 1, success: false },
      { type: 'writeComplete', tabId: 'a', token: 2, success: true },
      { type: 'writeComplete', tabId: 'a', token: 3, success: false }
    ]);
  });

  it('suppresses repeated application shortcuts', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    const key = terminalInstances[0].keyHandler;

    expect(key({ type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyN', repeat: false })).toBe(false);
    key({ type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyN', repeat: true });

    expect(sent.filter(message => message.command === 'newSession')).toHaveLength(1);
  });

  it('copies selected text without forwarding the key', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    terminalInstances[0].selection = 'selected';

    const handled = terminalInstances[0].keyHandler({
      type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyC', repeat: false
    });

    expect(handled).toBe(false);
    expect(sent).toContainEqual({ type: 'copy', tabId: 'a', data: 'selected' });
    expect(terminalInstances[0].selection).toBe('');
  });

  it('opens search without forwarding the shortcut and navigates matches', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.activate('a');

    const handled = terminalInstances[0].keyHandler({
      type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyF', repeat: false
    });
    expect(handled).toBe(false);
    expect(sent).toContainEqual({
      type: 'applicationCommand', tabId: 'a', command: 'searchTerminal'
    });

    host.openSearch('a');
    const input = document.getElementById('terminal-search-input');
    input.value = 'Needle';
    input.dispatchEvent(new Event('input'));
    expect(searchInstances[0].next.term).toBe('Needle');

    searchInstances[0].resultsHandler({ resultIndex: 1, resultCount: 3 });
    expect(document.getElementById('terminal-search-results').textContent).toBe('2/3');

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
    expect(searchInstances[0].previous.term).toBe('Needle');

    document.getElementById('terminal-search-case').click();
    expect(searchInstances[0].next.options.caseSensitive).toBe(true);
    expect(document.getElementById('terminal-search-case').getAttribute('aria-pressed')).toBe('true');

    document.getElementById('terminal-search-previous').click();
    expect(searchInstances[0].previous.term).toBe('Needle');
    document.getElementById('terminal-search-next').click();
    expect(searchInstances[0].next.term).toBe('Needle');
    document.getElementById('terminal-search-close').click();
    expect(document.getElementById('terminal-search').classList.contains('open')).toBe(false);
  });

  it('clears search on escape, tab switch, and disposal', async () => {
    const { host } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.create({ tabId: 'b', options: createOptions() });
    host.activate('a');
    host.openSearch('a');

    document.getElementById('terminal-search-input').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(document.getElementById('terminal-search').classList.contains('open')).toBe(false);
    expect(searchInstances[0].cleared).toBe(true);

    host.openSearch('a');
    host.activate('b');
    expect(document.getElementById('terminal-search').classList.contains('open')).toBe(false);
    host.dispose('b');
    expect(searchInstances[1].listenerDisposed).toBe(true);
  });

  it('falls back permanently after WebGL context loss', async () => {
    const { host } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.activate('a');
    webglInstances[0].contextLossHandler();
    host.activate('a');

    expect(webglInstances).toHaveLength(1);
    expect(webglInstances[0].disposed).toBe(true);
    expect(terminalInstances[0].refreshed).toBe(true);
  });

  it('dispose is idempotent and clears the active tab', async () => {
    const { host } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.activate('a');
    host.dispose('a');
    host.dispose('a');

    expect(terminalInstances[0].disposed).toBe(true);
    expect(document.querySelector('[data-tab-id="a"]')).toBeNull();
  });

  it('handles window and tab shortcuts while passing ordinary keys through', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    const key = terminalInstances[0].keyHandler;
    const base = { type: 'keydown', altKey: false, ctrlKey: false, shiftKey: false,
      metaKey: false, repeat: false };

    expect(key({ ...base, altKey: true, code: 'F4' })).toBe(false);
    expect(key({ ...base, ctrlKey: true, code: 'Tab' })).toBe(false);
    expect(key({ ...base, ctrlKey: true, shiftKey: true, code: 'Tab' })).toBe(false);
    expect(key({ ...base, ctrlKey: true, shiftKey: true, code: 'KeyK' })).toBe(false);
    expect(key({ ...base, code: 'KeyA' })).toBe(true);

    expect(sent.map(message => message.command).filter(Boolean)).toEqual([
      'closeWindow', 'nextTab', 'previousTab', 'commandPalette'
    ]);
  });

  it('switches active tabs, updates options, and focuses only the active terminal', async () => {
    const { host } = await loadHost();
    host.activate('missing');
    host.configure({ tabId: 'missing', options: createOptions() });
    host.create({ tabId: 'a', options: createOptions() });
    host.create({ tabId: 'b', options: createOptions() });
    host.activate('a');
    host.activate('b');
    host.focus('a');
    host.focus('b');
    host.configure({ tabId: 'a', options: { ...createOptions(), fontSize: 18 } });

    expect(webglInstances[0].disposed).toBe(true);
    expect(terminalInstances[0].options.fontSize).toBe(18);
    expect(terminalInstances[1].focused).toBe(true);
    expect(document.querySelector('[data-tab-id="a"]').classList.contains('active')).toBe(false);
  });

  it('skips fit for hidden or invalid terminals', async () => {
    const hidden = await loadHost({ width: 0 });
    hidden.module.fitActiveTerminal();
    hidden.host.create({ tabId: 'a', options: createOptions() });
    hidden.host.activate('a');
    expect(hidden.sent).toEqual([]);

    terminalInstances[0].cols = 0;
    hidden.module.fitActiveTerminal();
    expect(hidden.sent).toEqual([]);
  });

  it('keeps DOM rendering when WebGL construction or disposal fails', async () => {
    throwWebglConstructor = true;
    const first = await loadHost();
    first.host.create({ tabId: 'a', options: createOptions() });
    first.host.activate('a');
    first.host.activate('a');
    expect(webglInstances).toHaveLength(0);

    throwWebglConstructor = false;
    throwWebglDispose = true;
    const second = await loadHost();
    second.host.create({ tabId: 'b', options: createOptions() });
    second.host.activate('b');
    expect(() => second.host.dispose('b')).not.toThrow();
  });

  it('ignores empty copy, repeated close, and unavailable host bridge', async () => {
    const { host, sent } = await loadHost({ bridge: false });
    host.create({ tabId: 'a', options: createOptions() });
    const key = terminalInstances[0].keyHandler;
    key({ type: 'keydown', altKey: true, ctrlKey: false, shiftKey: false,
      metaKey: false, code: 'F4', repeat: true });
    key({ type: 'keydown', altKey: false, ctrlKey: true, shiftKey: true,
      metaKey: false, code: 'KeyC', repeat: false });
    window.dispatchEvent(new Event('load'));

    expect(sent).toEqual([]);
  });
});
