import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { SearchAddon } from '@xterm/addon-search';
import { WebglAddon } from '@xterm/addon-webgl';

const terminalHostElement = document.getElementById('terminal-host');
const searchBarElement = document.getElementById('terminal-search');
const searchInputElement = document.getElementById('terminal-search-input');
const searchResultsElement = document.getElementById('terminal-search-results');
const searchCaseElement = document.getElementById('terminal-search-case');
const searchPreviousElement = document.getElementById('terminal-search-previous');
const searchNextElement = document.getElementById('terminal-search-next');
const searchCloseElement = document.getElementById('terminal-search-close');
const paneContextMenuElement = document.getElementById('pane-context-menu');
const terminals = new Map();
let activePaneId = null;
let resizeFrame = null;
let activeTheme = 'dark';

const terminalThemes = {
  dark: {
    background: '#1e1e1e',
    foreground: '#e6e9ef',
    cursor: '#e6e9ef',
    cursorAccent: '#1e1e1e',
    black: '#000000',
    red: '#cd3131',
    green: '#0dbc79',
    yellow: '#e5e510',
    blue: '#2472c8',
    magenta: '#bc3fbc',
    cyan: '#11a8cd',
    white: '#e5e5e5',
    brightBlack: '#666666',
    brightRed: '#f14c4c',
    brightGreen: '#23d18b',
    brightYellow: '#f5f543',
    brightBlue: '#3b8eea',
    brightMagenta: '#d670d6',
    brightCyan: '#29b8db',
    brightWhite: '#ffffff',
    selection: '#264f78',
    selectionForeground: '#ffffff',
    search: {
      matchBackground: '#515c6a',
      matchOverviewRuler: '#748496',
      activeMatchBackground: '#d18616',
      activeMatchColorOverviewRuler: '#d18616'
    }
  },
  darcula: {
    background: '#2b2b2b',
    foreground: '#a9b7c6',
    cursor: '#d0d8e0',
    cursorAccent: '#2b2b2b',
    black: '#202122',
    red: '#d0803d',
    green: '#7a9b68',
    yellow: '#d6aa5b',
    blue: '#6897bb',
    magenta: '#a985b9',
    cyan: '#6fa3a1',
    white: '#a9b7c6',
    brightBlack: '#92969b',
    brightRed: '#e18b52',
    brightGreen: '#91b57b',
    brightYellow: '#ffd58a',
    brightBlue: '#82b4d6',
    brightMagenta: '#b493c4',
    brightCyan: '#86c1bd',
    brightWhite: '#d3dae3',
    selection: '#214283',
    selectionForeground: '#d5e5f3',
    search: {
      matchBackground: '#4b4d50',
      matchOverviewRuler: '#707377',
      activeMatchBackground: '#cc7832',
      activeMatchColorOverviewRuler: '#cc7832'
    }
  },
  mintara: {
    background: '#161b1a',
    foreground: '#d7e0dc',
    cursor: '#70cfa9',
    cursorAccent: '#161b1a',
    black: '#202624',
    red: '#e06c75',
    green: '#70cfa9',
    yellow: '#d9b76e',
    blue: '#6fa8dc',
    magenta: '#b58bd2',
    cyan: '#68c5c0',
    white: '#c8d2ce',
    brightBlack: '#66736e',
    brightRed: '#ed858d',
    brightGreen: '#89ddbb',
    brightYellow: '#e6c985',
    brightBlue: '#86b8e5',
    brightMagenta: '#c5a0dc',
    brightCyan: '#82d4cf',
    brightWhite: '#eef3f1',
    selection: '#315c4d',
    selectionForeground: '#e4f2ec',
    search: {
      matchBackground: '#315c4d',
      matchOverviewRuler: '#53615c',
      activeMatchBackground: '#d9b76e',
      activeMatchColorOverviewRuler: '#d9b76e'
    }
  },
  vesper: {
    background: '#17151c',
    foreground: '#ddd7e3',
    cursor: '#b58ad7',
    cursorAccent: '#17151c',
    black: '#211d27',
    red: '#df707a',
    green: '#72b99a',
    yellow: '#d7ae69',
    blue: '#7697d0',
    magenta: '#a277c7',
    cyan: '#70b7bc',
    white: '#cfc8d5',
    brightBlack: '#716978',
    brightRed: '#ea8991',
    brightGreen: '#8ac9ac',
    brightYellow: '#e3c17f',
    brightBlue: '#8caae0',
    brightMagenta: '#bc91dc',
    brightCyan: '#89c9cd',
    brightWhite: '#f2edf5',
    selection: '#493665',
    selectionForeground: '#f1eaf7',
    search: {
      matchBackground: '#493665',
      matchOverviewRuler: '#62566e',
      activeMatchBackground: '#d7ae69',
      activeMatchColorOverviewRuler: '#d7ae69'
    }
  },
  abyss: {
    background: '#0d1117',
    foreground: '#d6e2ee',
    cursor: '#79b8ff',
    cursorAccent: '#0d1117',
    black: '#161d27',
    red: '#e06c75',
    green: '#65c89b',
    yellow: '#d7b66f',
    blue: '#58a6ff',
    magenta: '#a88bd4',
    cyan: '#56c7d9',
    white: '#c5d2df',
    brightBlack: '#617386',
    brightRed: '#eb858d',
    brightGreen: '#7cd7ae',
    brightYellow: '#e4c782',
    brightBlue: '#79b8ff',
    brightMagenta: '#bda1e0',
    brightCyan: '#75d8e6',
    brightWhite: '#f0f6fc',
    selection: '#234a70',
    selectionForeground: '#e6f2ff',
    search: {
      matchBackground: '#234a70',
      matchOverviewRuler: '#4b6075',
      activeMatchBackground: '#d7b66f',
      activeMatchColorOverviewRuler: '#d7b66f'
    }
  },
  light: {
    background: '#fafaf9',
    foreground: '#202428',
    cursor: '#246fa8',
    cursorAccent: '#ffffff',
    black: '#24292f',
    red: '#b23131',
    green: '#2e7d32',
    yellow: '#806000',
    blue: '#245ea8',
    magenta: '#80469a',
    cyan: '#087987',
    white: '#5f6872',
    brightBlack: '#68717b',
    brightRed: '#d13c3c',
    brightGreen: '#1f7a31',
    brightYellow: '#8d6200',
    brightBlue: '#2b69ad',
    brightMagenta: '#9250a8',
    brightCyan: '#087785',
    brightWhite: '#343b43',
    selection: '#c7ddf2',
    selectionForeground: '#15202b',
    search: {
      matchBackground: '#d7e6f4',
      matchOverviewRuler: '#7b9fbd',
      activeMatchBackground: '#f0c36a',
      activeMatchColorOverviewRuler: '#8a5700'
    }
  }
};

function resolveTheme(theme) {
  if (typeof theme !== 'string') return 'dark';

  switch (theme.toLowerCase()) {
    case 'default light': return 'light';
    case 'darcula': return 'darcula';
    case 'mintara': return 'mintara';
    case 'vesper': return 'vesper';
    case 'abyss': return 'abyss';
    default: return 'dark';
  }
}

function resolveSelectionBackground(theme, selectionBackground) {
  const palette = terminalThemes[resolveTheme(theme)];
  return typeof selectionBackground === 'string'
    && selectionBackground.toLowerCase() === 'theme'
    ? palette.selection
    : selectionBackground;
}

function applyHostTheme(theme) {
  activeTheme = resolveTheme(theme);
  document.documentElement.dataset.theme = activeTheme;
  return terminalThemes[activeTheme];
}

function xtermTheme(palette) {
  const { search: _, selection: __, ...theme } = palette;
  return theme;
}

function send(message) {
  if (typeof invokeCSharpAction === 'function') {
    invokeCSharpAction(JSON.stringify(message));
  }
}

function createTerminal({ paneId, tabId, options }) {
  paneId ??= tabId;
  if (terminals.has(paneId)) {
    return;
  }

  const element = document.createElement('div');
  element.className = 'terminal-pane';
  element.dataset.paneId = paneId;
  element.dataset.tabId = tabId;
  element.addEventListener('contextmenu', event => {
    event.preventDefault();
    setActiveTerminal(paneId, true);
    if (paneContextMenuElement) {
      paneContextMenuElement.style.left = `${event.clientX}px`;
      paneContextMenuElement.style.top = `${event.clientY}px`;
      paneContextMenuElement.classList.add('open');
      paneContextMenuElement.setAttribute('aria-hidden', 'false');
    }
  });
  terminalHostElement.appendChild(element);

  const palette = applyHostTheme(options.theme);
  const selectionBackground = resolveSelectionBackground(
    options.theme,
    options.selectionBackground);
  const fitAddon = new FitAddon();
  const searchAddon = new SearchAddon();
  const terminal = new Terminal({
    cursorBlink: options.cursorBlink,
    cursorStyle: options.cursorStyle,
    fontFamily: options.fontFamily,
    fontSize: options.fontSize,
    lineHeight: 1.1,
    scrollback: 5000,
    allowTransparency: false,
    theme: {
      ...xtermTheme(palette),
      selectionBackground,
      selectionInactiveBackground: selectionBackground
    }
  });

  const state = {
    paneId,
    tabId,
    element,
    terminal,
    fitAddon,
    searchAddon,
    searchResultsDisposable: null,
    lastSearchResults: null,
    webglAddon: null,
    webglContextLossDisposable: null,
    webglDisabled: false,
    started: false,
    lastColumns: 0,
    lastRows: 0
  };

  terminal.loadAddon(fitAddon);
  terminal.loadAddon(searchAddon);
  state.searchResultsDisposable = searchAddon.onDidChangeResults(results => {
    state.lastSearchResults = results;
    if (activePaneId === paneId && searchBarElement.classList.contains('open')) {
      if (results.resultCount > 0 && results.resultIndex >= 0) {
        updateSearchResults(results);
      }
    }
  });
  terminal.open(element);
  terminal.onData(data => send({ type: 'input', tabId, paneId, data }));
  element.addEventListener('pointerdown', () => {
    if (activePaneId !== paneId) {
      setActiveTerminal(paneId, true);
    }
  });
  terminal.attachCustomKeyEventHandler(event => handleKeyEvent(state, event));
  terminals.set(paneId, state);
}

function handleKeyEvent(state, event) {
  const paneKeyResult = handlePaneKeyEvent(state, event);
  if (paneKeyResult !== null) {
    return paneKeyResult;
  }

  if (event.altKey && !event.ctrlKey && !event.metaKey && event.code === 'F4') {
    if (event.type === 'keydown' && !event.repeat) {
      send({
        type: 'applicationCommand',
        tabId: state.tabId,
        paneId: state.paneId,
        command: 'closeWindow'
      });
    }
    return false;
  }

  if (event.ctrlKey && !event.altKey && !event.metaKey && event.code === 'Tab') {
    if (event.type === 'keydown' && !event.repeat) {
      send({
        type: 'applicationCommand',
        tabId: state.tabId,
        paneId: state.paneId,
        command: event.shiftKey ? 'previousTab' : 'nextTab'
      });
    }
    return false;
  }

  if (event.type === 'keydown' && event.ctrlKey && event.shiftKey) {
    const applicationCommands = {
      KeyT: 'newTerminal',
      KeyN: 'newSession',
      KeyO: 'openSession',
      KeyW: 'closePane',
      KeyB: 'toggleSidebar',
      KeyF: 'searchTerminal',
      KeyK: 'commandPalette',
      Comma: 'settings'
    };
    const command = applicationCommands[event.code];
    if (command) {
      if (!event.repeat) {
        send({
          type: 'applicationCommand',
          tabId: state.tabId,
          paneId: state.paneId,
          command
        });
      }
      return false;
    }
  }

  if (event.type === 'keydown' && event.ctrlKey && event.shiftKey && event.code === 'KeyC') {
    const selected = state.terminal.getSelection();
    if (selected) {
      send({ type: 'copy', tabId: state.tabId, paneId: state.paneId, data: selected });
      state.terminal.clearSelection();
    }
    return false;
  }

  return true;
}

function handlePaneKeyEvent(state, event) {
  if (event.type !== 'keydown' || event.ctrlKey || event.metaKey || !event.altKey) {
    return null;
  }

  const command = event.shiftKey
    ? event.code === 'ArrowRight' ? 'splitRight'
      : event.code === 'ArrowDown' ? 'splitDown' : null
    : event.code === 'ArrowRight' ? 'focusRightPane'
      : event.code === 'ArrowLeft' ? 'focusLeftPane'
        : event.code === 'ArrowDown' ? 'focusDownPane'
          : event.code === 'ArrowUp' ? 'focusUpPane' : null;
  if (command && !event.repeat) {
    send({ type: 'applicationCommand', tabId: state.tabId, paneId: state.paneId, command });
  }
  return command ? false : null;
}

function handleWheelEvent(event) {
  if (event.ctrlKey) {
    event.preventDefault();
  }
}

function setActiveTerminal(paneId, notify = false) {
  const next = terminals.get(paneId);
  if (!next) {
    return;
  }

  if (activePaneId && activePaneId !== paneId) {
    closeSearch(false);
    const previous = terminals.get(activePaneId);
    if (previous) {
      previous.element.classList.remove('active');
    }
  }

  activePaneId = paneId;
  next.element.classList.add('active');
  if (terminals.size === 1) {
    enableWebgl(next);
  }
  fitTerminal(next);
  next.terminal.refresh(0, next.terminal.rows - 1);
  next.terminal.focus();
  if (notify) {
    send({ type: 'paneActivated', tabId: next.tabId, paneId });
  }
}

function activateTerminal(paneId) {
  setActiveTerminal(paneId, false);
}

function disposeTerminal(paneId) {
  const state = terminals.get(paneId);
  if (!state) {
    return;
  }

  if (activePaneId === paneId) {
    closeSearch(false);
    activePaneId = null;
  }

  disableWebgl(state);
  state.searchResultsDisposable?.dispose();
  state.terminal.dispose();
  state.element.remove();
  terminals.delete(paneId);
}

function configureTerminal(state, options) {
  const palette = applyHostTheme(options.theme);
  const selectionBackground = resolveSelectionBackground(
    options.theme,
    options.selectionBackground);
  state.terminal.options.fontFamily = options.fontFamily;
  state.terminal.options.fontSize = options.fontSize;
  state.terminal.options.theme = {
    ...xtermTheme(palette),
    selectionBackground,
    selectionInactiveBackground: selectionBackground
  };
  state.terminal.options.cursorStyle = options.cursorStyle;
  state.terminal.options.cursorBlink = options.cursorBlink;
  scheduleFitAllTerminals();
}

function configureTab({ tabId, options }) {
  terminals.forEach(state => {
    if (state.tabId === tabId) configureTerminal(state, options);
  });
}

function configureLegacy({ tabId, options }) {
  const state = terminals.get(tabId);
  if (state) configureTerminal(state, options);
}

function writeTerminal(paneId, token, value) {
  const state = terminals.get(paneId);
  if (!state) {
    send({ type: 'writeComplete', tabId: paneId, paneId, token, success: false });
    return;
  }

  try {
    state.terminal.write(value, () => {
      send({ type: 'writeComplete', tabId: state.tabId, paneId, token, success: true });
    });
  } catch {
    send({ type: 'writeComplete', tabId: state.tabId, paneId, token, success: false });
  }
}

function focusTerminal(paneId) {
  if (activePaneId === paneId) {
    terminals.get(paneId)?.terminal.focus();
  }
}

function searchOptions() {
  const search = terminalThemes[activeTheme].search;
  return {
    caseSensitive: searchCaseElement.classList.contains('active'),
    incremental: true,
    decorations: {
      ...search
    }
  };
}

function openSearch(paneId = activePaneId) {
  if (!terminals.has(paneId)) {
    return;
  }

  searchBarElement.classList.add('open');
  searchBarElement.setAttribute('aria-hidden', 'false');
  searchInputElement.focus();
  searchInputElement.select();
  runSearch(true);
}

function closeSearch(restoreFocus = true) {
  const state = terminals.get(activePaneId);
  state?.searchAddon.clearDecorations();
  searchBarElement.classList.remove('open');
  searchBarElement.setAttribute('aria-hidden', 'true');
  searchInputElement.value = '';
  updateSearchResults({ resultIndex: -1, resultCount: 0 });
  if (restoreFocus) {
    state?.terminal.focus();
  }
}

function runSearch(forward) {
  const state = terminals.get(activePaneId);
  const query = searchInputElement.value;
  if (!state || !query) {
    state?.searchAddon.clearDecorations();
    updateSearchResults({ resultIndex: -1, resultCount: 0 });
    return false;
  }

  const find = options => forward
    ? state.searchAddon.findNext(query, options)
    : state.searchAddon.findPrevious(query, options);
  state.lastSearchResults = null;
  let found;
  try {
    found = find(searchOptions());
  } catch {
    state.searchAddon.clearDecorations();
    state.lastSearchResults = null;
    try {
      found = find({
        caseSensitive: searchCaseElement.classList.contains('active'),
        incremental: true
      });
    } catch {
      found = false;
    }
  }

  const results = state.lastSearchResults;
  if (results?.resultCount > 0 && results.resultIndex >= 0) {
    updateSearchResults(results);
  } else {
    searchResultsElement.textContent = found ? 'Found' : 'No results';
  }
  return found;
}

function updateSearchResults({ resultIndex, resultCount }) {
  searchResultsElement.textContent = resultCount > 0 && resultIndex >= 0
    ? `${resultIndex + 1}/${resultCount}`
    : '0/0';
}

searchInputElement.addEventListener('input', () => runSearch(true));
searchInputElement.addEventListener('keydown', event => {
  if (event.key === 'Escape') {
    closeSearch();
    event.preventDefault();
  } else if (event.key === 'Enter') {
    runSearch(!event.shiftKey);
    event.preventDefault();
  }
});
searchCaseElement.addEventListener('click', () => {
  searchCaseElement.classList.toggle('active');
  searchCaseElement.setAttribute(
    'aria-pressed',
    String(searchCaseElement.classList.contains('active')));
  runSearch(true);
});
searchPreviousElement.addEventListener('click', () => runSearch(false));
searchNextElement.addEventListener('click', () => runSearch(true));
searchCloseElement.addEventListener('click', () => closeSearch());
paneContextMenuElement?.addEventListener('click', event => {
  const command = event.target?.dataset?.command;
  const state = terminals.get(activePaneId);
  if (command && state) {
    send({ type: 'applicationCommand', tabId: state.tabId, paneId: state.paneId, command });
  }
  paneContextMenuElement.classList.remove('open');
  paneContextMenuElement.setAttribute('aria-hidden', 'true');
});
document.addEventListener('pointerdown', event => {
  if (paneContextMenuElement && !paneContextMenuElement.contains(event.target)) {
    paneContextMenuElement.classList.remove('open');
    paneContextMenuElement.setAttribute('aria-hidden', 'true');
  }
});

function enableWebgl(state) {
  if (state.webglAddon || state.webglDisabled) {
    return;
  }

  try {
    const webglAddon = new WebglAddon();
    state.webglAddon = webglAddon;
    state.webglContextLossDisposable = webglAddon.onContextLoss(() => {
      if (state.webglAddon !== webglAddon) {
        return;
      }

      state.webglDisabled = true;
      disableWebgl(state);
      state.terminal.refresh(0, state.terminal.rows - 1);
    });
    state.terminal.loadAddon(webglAddon);
  } catch {
    disableWebgl(state);
    state.webglDisabled = true;
  }
}

function disableWebgl(state) {
  const webglAddon = state.webglAddon;
  if (!webglAddon) {
    return;
  }

  state.webglAddon = null;
  state.webglContextLossDisposable?.dispose();
  state.webglContextLossDisposable = null;

  try {
    webglAddon.dispose();
  } catch {
    // The renderer may already be disposed after a context loss.
  }
}

function scheduleFitAllTerminals() {
  if (resizeFrame !== null) {
    return;
  }

  resizeFrame = requestAnimationFrame(() => {
    resizeFrame = null;
    terminals.forEach(fitTerminal);
  });
}

function applyRendererPolicy() {
  if (terminals.size !== 1) return;

  const state = terminals.get(activePaneId) ?? terminals.values().next().value;
  if (state) enableWebgl(state);
}

function fitTerminal(state) {
  const paneBounds = state.element.getBoundingClientRect();
  const bounds = paneBounds.width > 0 && paneBounds.height > 0
    ? paneBounds
    : terminalHostElement.getBoundingClientRect();
  if (bounds.width <= 0 || bounds.height <= 0) {
    return;
  }

  state.fitAddon.fit();
  const columns = state.terminal.cols;
  const rows = state.terminal.rows;
  if (columns <= 0 || rows <= 0) {
    return;
  }

  const changed = columns !== state.lastColumns || rows !== state.lastRows;
  state.lastColumns = columns;
  state.lastRows = rows;

  if (!state.started) {
    state.started = true;
    send({ type: 'ready', tabId: state.tabId, paneId: state.paneId, columns, rows });
  } else if (changed) {
    send({ type: 'resize', tabId: state.tabId, paneId: state.paneId, columns, rows });
  }
}

function fitActiveTerminal() {
  const state = terminals.get(activePaneId);
  if (state) fitTerminal(state);
}

function scheduleFitActiveTerminal() {
  scheduleFitAllTerminals();
}

function createLayoutNode(node, tabId) {
  if (node.type === 'terminal') {
    return terminals.get(node.paneId)?.element ?? document.createElement('div');
  }

  const split = document.createElement('div');
  split.className = `pane-split ${node.orientation}`;
  const first = document.createElement('div');
  const second = document.createElement('div');
  const divider = document.createElement('div');
  first.className = 'pane-child';
  second.className = 'pane-child';
  divider.className = 'pane-divider';
  first.style.flex = `${node.ratio} 1 0`;
  second.style.flex = `${1 - node.ratio} 1 0`;
  first.appendChild(createLayoutNode(node.first, tabId));
  second.appendChild(createLayoutNode(node.second, tabId));
  split.append(first, divider, second);
  bindDivider(split, divider, first, second, node.first, tabId);
  return split;
}

function firstPaneId(node) {
  return node.type === 'terminal' ? node.paneId : firstPaneId(node.first);
}

function bindDivider(split, divider, first, second, firstNode, tabId) {
  divider.addEventListener('pointerdown', event => {
    divider.setPointerCapture(event.pointerId);
    const move = moveEvent => {
      const bounds = split.getBoundingClientRect();
      const position = split.classList.contains('vertical')
        ? (moveEvent.clientX - bounds.left) / bounds.width
        : (moveEvent.clientY - bounds.top) / bounds.height;
      const ratio = Math.max(.1, Math.min(.9, position));
      first.style.flex = `${ratio} 1 0`;
      second.style.flex = `${1 - ratio} 1 0`;
      scheduleFitAllTerminals();
    };
    const up = upEvent => {
      divider.releasePointerCapture(upEvent.pointerId);
      divider.removeEventListener('pointermove', move);
      divider.removeEventListener('pointerup', up);
      const firstSize = split.classList.contains('vertical')
        ? first.getBoundingClientRect().width : first.getBoundingClientRect().height;
      const total = split.classList.contains('vertical')
        ? split.getBoundingClientRect().width : split.getBoundingClientRect().height;
      send({ type: 'paneRatio', tabId, paneId: firstPaneId(firstNode), ratio: firstSize / total });
    };
    divider.addEventListener('pointermove', move);
    divider.addEventListener('pointerup', up);
  });
}

function layoutTerminals({ tabId, activePaneId: nextActivePaneId, root }) {
  terminalHostElement.classList.toggle('has-splits', terminals.size > 1);
  terminalHostElement.replaceChildren();
  if (root) terminalHostElement.appendChild(createLayoutNode(root, tabId));
  applyRendererPolicy();
  if (nextActivePaneId) setActiveTerminal(nextActivePaneId, false);
  scheduleFitAllTerminals();
}

window.terminalHost = {
  create: createTerminal,
  activate: activateTerminal,
  dispose: disposeTerminal,
  configureTab,
  configure: configureLegacy,
  layout: layoutTerminals,
  write: writeTerminal,
  focus: focusTerminal,
  openSearch
};

new ResizeObserver(scheduleFitAllTerminals).observe(terminalHostElement);
window.addEventListener('wheel', handleWheelEvent, { passive: false });
window.addEventListener('load', () => send({ type: 'hostReady' }));

export {
  activateTerminal,
  configureTerminal,
  createTerminal,
  disableWebgl,
  disposeTerminal,
  enableWebgl,
  fitTerminal,
  fitActiveTerminal,
  focusTerminal,
  handleKeyEvent,
  handleWheelEvent,
  openSearch,
  closeSearch,
  runSearch,
  scheduleFitAllTerminals,
  scheduleFitActiveTerminal,
  writeTerminal
};
