import { beforeEach, describe, expect, it, vi } from 'vitest';

const terminalInstances = [];
const webglInstances = [];
const searchInstances = [];
const fitInstances = [];
let throwWebglConstructor = false;
let throwWebglDispose = false;
let searchResult = true;
let throwSearchDecorations = false;
let searchResultsDuringFind = null;

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
    refresh() { this.refreshed = true; this.refreshCount = (this.refreshCount ?? 0) + 1; }
    focus() { this.focused = true; }
    dispose() { this.disposed = true; }
  }
}));

vi.mock('@xterm/addon-fit', () => ({
  FitAddon: class {
    constructor() { this.fitCount = 0; fitInstances.push(this); }
    fit() { this.fitCount++; }
  }
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
    findNext(term, options) {
      this.next = { term, options };
      if (throwSearchDecorations && options.decorations) throw new Error('markers unavailable');
      if (searchResultsDuringFind) this.resultsHandler(searchResultsDuringFind);
      return searchResult;
    }
    findPrevious(term, options) {
      this.previous = { term, options };
      if (throwSearchDecorations && options.decorations) throw new Error('markers unavailable');
      if (searchResultsDuringFind) this.resultsHandler(searchResultsDuringFind);
      return searchResult;
    }
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
    </div>
    <div id="pane-context-menu" aria-hidden="true">
      <button data-command="splitRight"></button>
      <button data-command="closePane"></button>
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
    selectionBackground: '#264F78',
    theme: 'Default Dark'
  };
}

function relativeLuminance(hexColor) {
  const channels = hexColor.slice(1).match(/../g).map(channel => {
    const value = Number.parseInt(channel, 16) / 255;
    return value <= 0.04045
      ? value / 12.92
      : ((value + 0.055) / 1.055) ** 2.4;
  });
  return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
}

function contrastRatio(foreground, background) {
  const foregroundLuminance = relativeLuminance(foreground);
  const backgroundLuminance = relativeLuminance(background);
  return (Math.max(foregroundLuminance, backgroundLuminance) + 0.05)
    / (Math.min(foregroundLuminance, backgroundLuminance) + 0.05);
}

describe('terminal host bridge', () => {
  beforeEach(() => {
    terminalInstances.length = 0;
    webglInstances.length = 0;
    searchInstances.length = 0;
    fitInstances.length = 0;
    throwWebglConstructor = false;
    throwWebglDispose = false;
    searchResult = true;
    throwSearchDecorations = false;
    searchResultsDuringFind = null;
  });

  it('creates one isolated terminal per pane and forwards raw input', async () => {
    const { host, sent } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.create({ tabId: 'a', options: createOptions() });

    expect(terminalInstances).toHaveLength(1);
    const contextMenu = new MouseEvent('contextmenu', { cancelable: true });
    terminalInstances[0].element.dispatchEvent(contextMenu);
    expect(contextMenu.defaultPrevented).toBe(true);
    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#264F78');
    expect(terminalInstances[0].options.theme.selectionInactiveBackground).toBe('#264F78');
    expect(terminalInstances[0].options.theme.background).toBe('#1e1e1e');
    expect(document.documentElement.dataset.theme).toBe('dark');
    terminalInstances[0].dataHandler('\u0003');
    expect(sent).toContainEqual({ type: 'input', tabId: 'a', paneId: 'a', data: '\u0003' });
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
      { type: 'resize', tabId: 'a', paneId: 'a', columns: 100, rows: 24 }
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
      { type: 'writeComplete', tabId: 'missing', paneId: 'missing', token: 1, success: false },
      { type: 'writeComplete', tabId: 'a', paneId: 'a', token: 2, success: true },
      { type: 'writeComplete', tabId: 'a', paneId: 'a', token: 3, success: false }
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
    expect(key({ type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyT', repeat: false })).toBe(false);
    key({ type: 'keydown', ctrlKey: true, shiftKey: true, altKey: false,
      metaKey: false, code: 'KeyT', repeat: true });

    expect(sent.filter(message => message.command === 'newSession')).toHaveLength(1);
    expect(sent.filter(message => message.command === 'newTerminal')).toHaveLength(1);
  });

  it('lays out multiple panes without recreating terminal instances', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'b', tabId: 'tab', options: createOptions() });
    const firstElement = terminalInstances[0].element;
    const secondElement = terminalInstances[1].element;

    host.layout({
      tabId: 'tab',
      activePaneId: 'b',
      root: {
        type: 'split',
        orientation: 'vertical',
        ratio: .5,
        first: { type: 'terminal', paneId: 'a' },
        second: { type: 'terminal', paneId: 'b' }
      }
    });

    expect(terminalInstances).toHaveLength(2);
    expect(document.getElementById('terminal-host').classList.contains('has-splits')).toBe(true);
    expect(document.querySelector('.pane-split.vertical')).not.toBeNull();
    expect(document.querySelector('[data-pane-id="a"]')).toBe(firstElement);
    expect(document.querySelector('[data-pane-id="b"]')).toBe(secondElement);
    expect(secondElement.classList.contains('active')).toBe(true);
  });

  it('omits active-pane decoration when the tab has one terminal', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'other', tabId: 'other-tab', options: createOptions() });
    host.layout({
      tabId: 'tab',
      activePaneId: 'a',
      root: { type: 'terminal', paneId: 'a' }
    });

    expect(document.getElementById('terminal-host').classList.contains('has-splits')).toBe(false);
    expect(document.querySelector('[data-pane-id="a"]').classList.contains('active')).toBe(true);
  });

  it('preserves pane buffers and the existing WebGL renderer in split layout', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.activate('a');
    host.write('a', 1, 'persistent output');
    const originalRenderer = webglInstances[0];
    host.create({ paneId: 'b', tabId: 'tab', options: createOptions() });
    host.layout({
      tabId: 'tab',
      activePaneId: 'b',
      root: {
        type: 'split', orientation: 'vertical', ratio: .5,
        first: { type: 'terminal', paneId: 'a' },
        second: { type: 'terminal', paneId: 'b' }
      }
    });
    host.activate('a');
    host.activate('b');

    expect(terminalInstances[0].lastWrite).toBe('persistent output');
    expect(terminalInstances[0].disposed).toBeUndefined();
    expect(originalRenderer.disposed).toBeUndefined();
    expect(webglInstances).toHaveLength(1);
  });

  it('activates panes and forwards context menu commands', async () => {
    const { host, sent } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'b', tabId: 'tab', options: createOptions() });
    host.activate('a');

    terminalInstances[1].element.dispatchEvent(
      new MouseEvent('pointerdown', { bubbles: true }));
    expect(sent).toContainEqual({ type: 'paneActivated', tabId: 'tab', paneId: 'b' });

    terminalInstances[1].element.dispatchEvent(new MouseEvent('contextmenu', {
      bubbles: true, cancelable: true, clientX: 12, clientY: 34
    }));
    const menu = document.getElementById('pane-context-menu');
    expect(menu.classList.contains('open')).toBe(true);
    expect(menu.style.left).toBe('12px');
    expect(menu.style.top).toBe('34px');

    menu.querySelector('[data-command="splitRight"]').click();
    expect(sent).toContainEqual({
      type: 'applicationCommand', tabId: 'tab', paneId: 'b', command: 'splitRight'
    });
    expect(menu.getAttribute('aria-hidden')).toBe('true');

    terminalInstances[1].element.dispatchEvent(
      new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    menu.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    expect(menu.classList.contains('open')).toBe(true);
    document.body.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    expect(menu.classList.contains('open')).toBe(false);
  });

  it('reconfigures every pane belonging to one tab', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'b', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'other', tabId: 'other-tab', options: createOptions() });

    host.configureTab({
      tabId: 'tab',
      options: { ...createOptions(), fontSize: 18 }
    });

    expect(terminalInstances[0].options.fontSize).toBe(18);
    expect(terminalInstances[1].options.fontSize).toBe(18);
    expect(terminalInstances[2].options.fontSize).toBe(13);
  });

  it('resizes a pane divider and reports the persisted ratio', async () => {
    const { host, sent } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    host.create({ paneId: 'b', tabId: 'tab', options: createOptions() });
    host.layout({
      tabId: 'tab',
      activePaneId: 'a',
      root: {
        type: 'split', orientation: 'vertical', ratio: .5,
        first: { type: 'terminal', paneId: 'a' },
        second: { type: 'terminal', paneId: 'b' }
      }
    });

    const split = document.querySelector('.pane-split');
    const children = split.querySelectorAll('.pane-child');
    const divider = split.querySelector('.pane-divider');
    split.getBoundingClientRect = () => ({ left: 10, top: 20, width: 200, height: 100 });
    children[0].getBoundingClientRect = () => ({ width: 120, height: 100 });
    divider.setPointerCapture = vi.fn();
    divider.releasePointerCapture = vi.fn();
    let firstFlex;
    let secondFlex;
    Object.defineProperty(children[0].style, 'flex', {
      configurable: true, get: () => firstFlex, set: value => { firstFlex = value; }
    });
    Object.defineProperty(children[1].style, 'flex', {
      configurable: true, get: () => secondFlex, set: value => { secondFlex = value; }
    });
    const pointerEvent = (type, pointerId, clientX, clientY) => {
      const event = new MouseEvent(type, { bubbles: true, clientX, clientY });
      Object.defineProperty(event, 'pointerId', { value: pointerId });
      return event;
    };

    divider.dispatchEvent(pointerEvent('pointerdown', 7, 0, 0));
    divider.dispatchEvent(pointerEvent('pointermove', 7, 170, 0));
    expect(Number.parseFloat(firstFlex)).toBeCloseTo(.8);
    expect(Number.parseFloat(secondFlex)).toBeCloseTo(.2);
    divider.dispatchEvent(pointerEvent('pointerup', 7, 0, 0));

    expect(divider.setPointerCapture).toHaveBeenCalledWith(7);
    expect(divider.releasePointerCapture).toHaveBeenCalledWith(7);
    expect(sent).toContainEqual({
      type: 'paneRatio', tabId: 'tab', paneId: 'a', ratio: .6
    });
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
    expect(sent).toContainEqual({ type: 'copy', tabId: 'a', paneId: 'a', data: 'selected' });
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
    expect(sent).not.toContainEqual(expect.objectContaining({ command: 'searchTerminal' }));
    document.getElementById('terminal-search-close').focus();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(document.getElementById('terminal-search-input'));

    host.openSearch('a');
    const input = document.getElementById('terminal-search-input');
    searchResultsDuringFind = { resultIndex: 1, resultCount: 3 };
    input.value = 'Needle';
    input.dispatchEvent(new Event('input'));
    expect(searchInstances[0].next.term).toBe('Needle');
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

  it('reports no results and falls back when search decorations fail', async () => {
    const { host } = await loadHost();
    host.create({ tabId: 'a', options: createOptions() });
    host.activate('a');
    host.openSearch('a');
    const input = document.getElementById('terminal-search-input');

    searchResult = false;
    searchResultsDuringFind = { resultIndex: -1, resultCount: 0 };
    input.value = 'Missing';
    input.dispatchEvent(new Event('input'));
    expect(document.getElementById('terminal-search-results').textContent).toBe('No results');

    searchResult = true;
    throwSearchDecorations = true;
    input.value = 'Fallback';
    input.dispatchEvent(new Event('input'));
    expect(searchInstances[0].next.term).toBe('Fallback');
    expect(searchInstances[0].next.options.decorations).toBeUndefined();
    expect(document.getElementById('terminal-search-results').textContent).toBe('Found');
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

  it('blocks Ctrl+wheel zoom while preserving ordinary wheel scrolling', async () => {
    await loadHost();
    const zoomWheel = new WheelEvent('wheel', { ctrlKey: true, cancelable: true });
    const scrollWheel = new WheelEvent('wheel', { cancelable: true });

    window.dispatchEvent(zoomWheel);
    window.dispatchEvent(scrollWheel);

    expect(zoomWheel.defaultPrevented).toBe(true);
    expect(scrollWheel.defaultPrevented).toBe(false);
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
    host.configure({
      tabId: 'a',
      options: { ...createOptions(), fontSize: 18, selectionBackground: '#007ACC' }
    });

    expect(webglInstances).toHaveLength(0);
    expect(terminalInstances[0].options.fontSize).toBe(18);
    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#007ACC');
    expect(terminalInstances[0].options.theme.selectionInactiveBackground).toBe('#007ACC');
    expect(terminalInstances[1].focused).toBe(true);
    expect(document.querySelector('[data-tab-id="a"]')).toBe(terminalInstances[0].element);
    expect(terminalInstances[0].element.hidden).toBe(true);
    expect(terminalInstances[0].element.classList.contains('active')).toBe(false);
  });

  it('maps vertical Alt arrows to pane navigation', async () => {
    const { host, sent } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    const key = terminalInstances[0].keyHandler;
    const base = { type: 'keydown', altKey: true, ctrlKey: false, shiftKey: false,
      metaKey: false, repeat: false };

    expect(key({ ...base, code: 'ArrowDown' })).toBe(false);
    expect(key({ ...base, code: 'ArrowUp' })).toBe(false);

    expect(sent.map(message => message.command)).toEqual([
      'focusDownPane', 'focusUpPane'
    ]);
  });

  it('maps horizontal Alt arrows to directional pane navigation', async () => {
    const { host, sent } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab', options: createOptions() });
    const key = terminalInstances[0].keyHandler;
    const base = { type: 'keydown', altKey: true, ctrlKey: false, shiftKey: false,
      metaKey: false, repeat: false };

    expect(key({ ...base, code: 'ArrowRight' })).toBe(false);
    expect(key({ ...base, code: 'ArrowLeft' })).toBe(false);

    expect(sent.map(message => message.command)).toEqual([
      'focusRightPane', 'focusLeftPane'
    ]);
  });

  it('applies light palette when terminals are created and reconfigured', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: { ...createOptions(), theme: 'Default Light' }
    });

    expect(terminalInstances[0].options.theme.background).toBe('#fafaf9');
    expect(terminalInstances[0].options.theme.foreground).toBe('#202428');
    expect(terminalInstances[0].options.theme.cursor).toBe('#246fa8');
    expect(terminalInstances[0].options.theme.cursorAccent).toBe('#ffffff');
    expect(terminalInstances[0].options.theme.selectionForeground).toBe('#15202b');
    expect(terminalInstances[0].options.theme.brightBlue)
      .not.toBe(terminalInstances[0].options.theme.blue);
    expect(document.documentElement.dataset.theme).toBe('light');

    host.configure({
      tabId: 'a',
      options: { ...createOptions(), theme: 'Default Dark' }
    });

    expect(terminalInstances[0].options.theme.background).toBe('#1e1e1e');
    expect(document.documentElement.dataset.theme).toBe('dark');
  });

  it('applies Aurora palette, search, and theme selection', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Aurora',
        selectionBackground: 'Theme'
      }
    });

    const theme = terminalInstances[0].options.theme;
    expect(theme.background).toBe('#f5f8fa');
    expect(theme.foreground).toBe('#26343d');
    expect(theme.cursor).toBe('#3d8fbf');
    expect(theme.cursorAccent).toBe('#ffffff');
    expect(theme.red).toBe('#b9434e');
    expect(theme.green).toBe('#34785e');
    expect(theme.yellow).toBe('#956a22');
    expect(theme.blue).toBe('#2d719b');
    expect(theme.magenta).toBe('#815ea3');
    expect(theme.cyan).toBe('#257783');
    expect(theme.brightBlue).toBe('#176f9f');
    expect(theme.brightCyan).toBe('#157684');
    expect(theme.brightWhite).toBe('#354a55');
    expect(theme.blue).not.toBe(theme.cyan);
    expect(theme.green).not.toBe(theme.cyan);
    expect(theme.magenta).not.toBe(theme.blue);
    expect(theme.yellow).not.toBe(theme.foreground);
    expect(theme.brightBlue).not.toBe(theme.blue);
    expect(theme.brightCyan).not.toBe(theme.cyan);
    expect(theme.white).not.toBe(theme.brightWhite);
    for (const [normal, bright] of [
      ['black', 'brightBlack'],
      ['red', 'brightRed'],
      ['green', 'brightGreen'],
      ['yellow', 'brightYellow'],
      ['blue', 'brightBlue'],
      ['magenta', 'brightMagenta'],
      ['cyan', 'brightCyan'],
      ['white', 'brightWhite']
    ]) {
      expect(theme[normal]).not.toBe(theme[bright]);
    }
    for (const color of [
      'foreground',
      'black', 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan', 'white',
      'brightBlack', 'brightRed', 'brightGreen', 'brightYellow',
      'brightBlue', 'brightMagenta', 'brightCyan', 'brightWhite'
    ]) {
      expect(contrastRatio(theme[color], theme.background)).toBeGreaterThanOrEqual(4.5);
    }
    expect(theme.selectionForeground).toBe('#18384a');
    expect(theme.selectionBackground).toBe('#cde4f2');
    expect(theme.selectionInactiveBackground).toBe('#cde4f2');
    expect(theme.selection).toBeUndefined();
    expect(document.documentElement.dataset.theme).toBe('aurora');

    host.activate('a');
    document.getElementById('terminal-search-input').value = 'sky';
    host.openSearch('a');

    expect(searchInstances[0].next.options.decorations.matchBackground).toBe('#d8eaf4');
    expect(searchInstances[0].next.options.decorations.matchOverviewRuler).toBe('#69aad0');
    expect(searchInstances[0].next.options.decorations.activeMatchBackground).toBe('#f0d49a');
  });

  it('applies Darcula palette and follows its selection color', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Darcula',
        selectionBackground: 'Theme'
      }
    });

    expect(terminalInstances[0].options.theme.background).toBe('#2b2b2b');
    expect(terminalInstances[0].options.theme.foreground).toBe('#a9b7c6');
    expect(terminalInstances[0].options.theme.red).toBe('#d0803d');
    expect(terminalInstances[0].options.theme.magenta).toBe('#a985b9');
    expect(terminalInstances[0].options.theme.cyan).toBe('#6fa3a1');
    expect(terminalInstances[0].options.theme.cyan)
      .not.toBe(terminalInstances[0].options.theme.blue);
    expect(terminalInstances[0].options.theme.brightCyan)
      .not.toBe(terminalInstances[0].options.theme.cyan);
    expect(terminalInstances[0].options.theme.cursorAccent).toBe('#2b2b2b');
    expect(terminalInstances[0].options.theme.selectionForeground).toBe('#d5e5f3');
    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#214283');
    expect(terminalInstances[0].options.theme.selectionInactiveBackground).toBe('#214283');
    expect(terminalInstances[0].options.theme.selection).toBeUndefined();
    expect(document.documentElement.dataset.theme).toBe('darcula');

    host.configure({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Darcula',
        selectionBackground: '#275D4E'
      }
    });

    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#275D4E');
    expect(terminalInstances[0].options.theme.selectionInactiveBackground).toBe('#275D4E');
  });

  it('applies Mintara palette, search, and theme selection', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Mintara',
        selectionBackground: 'Theme'
      }
    });

    const theme = terminalInstances[0].options.theme;
    expect(theme.background).toBe('#161b1a');
    expect(theme.foreground).toBe('#d7e0dc');
    expect(theme.cursor).toBe('#70cfa9');
    expect(theme.cursorAccent).toBe('#161b1a');
    expect(theme.green).toBe('#70cfa9');
    expect(theme.blue).toBe('#6fa8dc');
    expect(theme.cyan).toBe('#68c5c0');
    expect(theme.brightGreen).toBe('#89ddbb');
    expect(theme.brightBlue).toBe('#86b8e5');
    expect(theme.brightCyan).toBe('#82d4cf');
    expect(theme.green).not.toBe(theme.cyan);
    expect(theme.blue).not.toBe(theme.cyan);
    expect(theme.brightGreen).not.toBe(theme.green);
    expect(theme.brightWhite).not.toBe(theme.white);
    expect(theme.foreground).not.toBe(theme.white);
    expect(theme.selectionForeground).toBe('#e4f2ec');
    expect(theme.selectionBackground).toBe('#315c4d');
    expect(theme.selectionInactiveBackground).toBe('#315c4d');
    expect(theme.selection).toBeUndefined();
    expect(document.documentElement.dataset.theme).toBe('mintara');

    host.activate('a');
    document.getElementById('terminal-search-input').value = 'mint';
    host.openSearch('a');

    expect(searchInstances[0].next.options.decorations.matchBackground).toBe('#315c4d');
    expect(searchInstances[0].next.options.decorations.matchOverviewRuler).toBe('#53615c');
    expect(searchInstances[0].next.options.decorations.activeMatchBackground).toBe('#d9b76e');
  });

  it('applies Vesper palette, search, and theme selection', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Vesper',
        selectionBackground: 'Theme'
      }
    });

    const theme = terminalInstances[0].options.theme;
    expect(theme.background).toBe('#17151c');
    expect(theme.foreground).toBe('#ddd7e3');
    expect(theme.cursor).toBe('#b58ad7');
    expect(theme.cursorAccent).toBe('#17151c');
    expect(theme.green).toBe('#72b99a');
    expect(theme.blue).toBe('#7697d0');
    expect(theme.magenta).toBe('#a277c7');
    expect(theme.cyan).toBe('#70b7bc');
    expect(theme.brightGreen).toBe('#8ac9ac');
    expect(theme.brightBlue).toBe('#8caae0');
    expect(theme.brightMagenta).toBe('#bc91dc');
    expect(theme.brightCyan).toBe('#89c9cd');
    expect(theme.blue).not.toBe(theme.magenta);
    expect(theme.magenta).not.toBe(theme.brightMagenta);
    expect(theme.green).not.toBe(theme.cyan);
    expect(theme.foreground).not.toBe(theme.white);
    expect(theme.white).not.toBe(theme.brightWhite);
    expect(theme.selectionForeground).toBe('#f1eaf7');
    expect(theme.selectionBackground).toBe('#493665');
    expect(theme.selectionInactiveBackground).toBe('#493665');
    expect(theme.selection).toBeUndefined();
    expect(document.documentElement.dataset.theme).toBe('vesper');

    host.activate('a');
    document.getElementById('terminal-search-input').value = 'violet';
    host.openSearch('a');

    expect(searchInstances[0].next.options.decorations.matchBackground).toBe('#493665');
    expect(searchInstances[0].next.options.decorations.matchOverviewRuler).toBe('#62566e');
    expect(searchInstances[0].next.options.decorations.activeMatchBackground).toBe('#d7ae69');
  });

  it('applies Abyss palette, search, and theme selection', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Abyss',
        selectionBackground: 'Theme'
      }
    });

    const theme = terminalInstances[0].options.theme;
    expect(theme.background).toBe('#0d1117');
    expect(theme.foreground).toBe('#d6e2ee');
    expect(theme.cursor).toBe('#79b8ff');
    expect(theme.cursorAccent).toBe('#0d1117');
    expect(theme.green).toBe('#65c89b');
    expect(theme.blue).toBe('#58a6ff');
    expect(theme.magenta).toBe('#a88bd4');
    expect(theme.cyan).toBe('#56c7d9');
    expect(theme.brightGreen).toBe('#7cd7ae');
    expect(theme.brightBlue).toBe('#79b8ff');
    expect(theme.brightCyan).toBe('#75d8e6');
    expect(theme.blue).not.toBe(theme.cyan);
    expect(theme.blue).not.toBe(theme.brightBlue);
    expect(theme.cyan).not.toBe(theme.brightCyan);
    expect(theme.green).not.toBe(theme.cyan);
    expect(theme.foreground).not.toBe(theme.white);
    expect(theme.white).not.toBe(theme.brightWhite);
    expect(theme.selectionForeground).toBe('#e6f2ff');
    expect(theme.selectionBackground).toBe('#234a70');
    expect(theme.selectionInactiveBackground).toBe('#234a70');
    expect(theme.selection).toBeUndefined();
    expect(document.documentElement.dataset.theme).toBe('abyss');

    host.activate('a');
    document.getElementById('terminal-search-input').value = 'ocean';
    host.openSearch('a');

    expect(searchInstances[0].next.options.decorations.matchBackground).toBe('#234a70');
    expect(searchInstances[0].next.options.decorations.matchOverviewRuler).toBe('#4b6075');
    expect(searchInstances[0].next.options.decorations.activeMatchBackground).toBe('#d7b66f');
  });

  it('follows each theme selection color when reconfigured', async () => {
    const { host } = await loadHost();
    host.create({
      tabId: 'a',
      options: {
        ...createOptions(),
        selectionBackground: 'Theme'
      }
    });

    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#264f78');

    host.configure({
      tabId: 'a',
      options: {
        ...createOptions(),
        theme: 'Default Light',
        selectionBackground: 'Theme'
      }
    });

    expect(terminalInstances[0].options.theme.selectionBackground).toBe('#c7ddf2');
    expect(terminalInstances[0].options.theme.selectionInactiveBackground).toBe('#c7ddf2');
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

  it('keeps inactive tabs mounted, hidden, buffered, and excluded from fit', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab-a', options: createOptions() });
    host.create({ paneId: 'b', tabId: 'tab-b', options: createOptions() });
    host.layout({
      tabId: 'tab-a', activePaneId: 'a',
      root: { type: 'terminal', paneId: 'a' }
    });
    await new Promise(resolve => setTimeout(resolve, 0));
    const inactiveFitCount = fitInstances[0].fitCount;

    host.layout({
      tabId: 'tab-b', activePaneId: 'b',
      root: { type: 'terminal', paneId: 'b' }
    });
    host.write('a', 1, 'background output');
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(document.querySelector('[data-pane-id="a"]')).toBe(terminalInstances[0].element);
    expect(terminalInstances[0].element.hidden).toBe(true);
    expect(document.querySelector('[data-pane-id="b"]')).not.toBeNull();
    expect(terminalInstances[1].element.hidden).toBe(false);
    expect(terminalInstances[0].lastWrite).toBe('background output');
    expect(fitInstances[0].fitCount).toBe(inactiveFitCount);
    expect(webglInstances).toHaveLength(0);
  });

  it('keeps the original WebGL renderer when a tab is restored', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab-a', options: createOptions() });
    host.activate('a');
    const firstRenderer = webglInstances[0];

    host.create({ paneId: 'b', tabId: 'tab-b', options: createOptions() });
    host.layout({
      tabId: 'tab-b', activePaneId: 'b',
      root: { type: 'terminal', paneId: 'b' }
    });

    expect(firstRenderer.disposed).toBeUndefined();

    const refreshCount = terminalInstances[0].refreshCount ?? 0;
    host.layout({
      tabId: 'tab-a', activePaneId: 'a',
      root: { type: 'terminal', paneId: 'a' }
    });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(document.querySelector('[data-pane-id="a"]')).toBe(terminalInstances[0].element);
    expect(terminalInstances[0].element.hidden).toBe(false);
    expect(terminalInstances[1].element.hidden).toBe(true);
    expect(terminalInstances[0].refreshCount).toBeGreaterThan(refreshCount);
    expect(webglInstances).toEqual([firstRenderer]);
  });

  it('does not recreate WebGL after the window has hosted multiple terminals', async () => {
    const { host } = await loadHost();
    host.create({ paneId: 'a', tabId: 'tab-a', options: createOptions() });
    host.activate('a');
    host.create({ paneId: 'b', tabId: 'tab-b', options: createOptions() });
    host.layout({
      tabId: 'tab-b', activePaneId: 'b',
      root: { type: 'terminal', paneId: 'b' }
    });
    host.dispose('a');
    host.layout({
      tabId: 'tab-b', activePaneId: 'b',
      root: { type: 'terminal', paneId: 'b' }
    });

    expect(webglInstances).toHaveLength(1);
    expect(webglInstances[0].disposed).toBe(true);
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
