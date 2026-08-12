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
const terminals = new Map();
let activeTabId = null;
let resizeFrame = null;
let activeTheme = 'dark';

const terminalThemes = {
  dark: {
    background: '#1e1e1e',
    foreground: '#e6e9ef',
    cursor: '#e6e9ef',
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
    search: {
      matchBackground: '#515c6a',
      matchOverviewRuler: '#748496',
      activeMatchBackground: '#d18616',
      activeMatchColorOverviewRuler: '#d18616'
    }
  },
  light: {
    background: '#ffffff',
    foreground: '#1f1f1f',
    cursor: '#1f1f1f',
    black: '#000000',
    red: '#cd3131',
    green: '#008000',
    yellow: '#795e26',
    blue: '#0451a5',
    magenta: '#af00db',
    cyan: '#0598bc',
    white: '#e5e5e5',
    brightBlack: '#666666',
    brightRed: '#cd3131',
    brightGreen: '#14ce14',
    brightYellow: '#b5ba00',
    brightBlue: '#0451a5',
    brightMagenta: '#bc05bc',
    brightCyan: '#0598bc',
    brightWhite: '#a5a5a5',
    search: {
      matchBackground: '#c8def5',
      matchOverviewRuler: '#6b9ac4',
      activeMatchBackground: '#f6c177',
      activeMatchColorOverviewRuler: '#c77700'
    }
  }
};

function resolveTheme(theme) {
  return typeof theme === 'string' && theme.toLowerCase() === 'default light'
    ? 'light'
    : 'dark';
}

function applyHostTheme(theme) {
  activeTheme = resolveTheme(theme);
  document.documentElement.dataset.theme = activeTheme;
  return terminalThemes[activeTheme];
}

function xtermTheme(palette) {
  const { search: _, ...theme } = palette;
  return theme;
}

function send(message) {
  if (typeof invokeCSharpAction === 'function') {
    invokeCSharpAction(JSON.stringify(message));
  }
}

function createTerminal({ tabId, options }) {
  if (terminals.has(tabId)) {
    return;
  }

  const element = document.createElement('div');
  element.className = 'terminal-pane';
  element.dataset.tabId = tabId;
  element.addEventListener('contextmenu', event => event.preventDefault());
  terminalHostElement.appendChild(element);

  const palette = applyHostTheme(options.theme);
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
      selectionBackground: options.selectionBackground,
      selectionInactiveBackground: options.selectionBackground
    }
  });

  const state = {
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
    if (activeTabId === tabId && searchBarElement.classList.contains('open')) {
      if (results.resultCount > 0 && results.resultIndex >= 0) {
        updateSearchResults(results);
      }
    }
  });
  terminal.open(element);
  terminal.onData(data => send({ type: 'input', tabId, data }));
  terminal.attachCustomKeyEventHandler(event => handleKeyEvent(state, event));
  terminals.set(tabId, state);
}

function handleKeyEvent(state, event) {
  if (event.altKey && !event.ctrlKey && !event.metaKey && event.code === 'F4') {
    if (event.type === 'keydown' && !event.repeat) {
      send({
        type: 'applicationCommand',
        tabId: state.tabId,
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
      KeyW: 'closeTab',
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
          command
        });
      }
      return false;
    }
  }

  if (event.type === 'keydown' && event.ctrlKey && event.shiftKey && event.code === 'KeyC') {
    const selected = state.terminal.getSelection();
    if (selected) {
      send({ type: 'copy', tabId: state.tabId, data: selected });
      state.terminal.clearSelection();
    }
    return false;
  }

  return true;
}

function handleWheelEvent(event) {
  if (event.ctrlKey) {
    event.preventDefault();
  }
}

function activateTerminal(tabId) {
  const next = terminals.get(tabId);
  if (!next) {
    return;
  }

  if (activeTabId && activeTabId !== tabId) {
    closeSearch(false);
    const previous = terminals.get(activeTabId);
    if (previous) {
      disableWebgl(previous);
      previous.element.classList.remove('active');
    }
  }

  activeTabId = tabId;
  next.element.classList.add('active');
  enableWebgl(next);
  fitActiveTerminal();
  next.terminal.refresh(0, next.terminal.rows - 1);
  next.terminal.focus();
}

function disposeTerminal(tabId) {
  const state = terminals.get(tabId);
  if (!state) {
    return;
  }

  if (activeTabId === tabId) {
    closeSearch(false);
    activeTabId = null;
  }

  disableWebgl(state);
  state.searchResultsDisposable?.dispose();
  state.terminal.dispose();
  state.element.remove();
  terminals.delete(tabId);
}

function configureTerminal({ tabId, options }) {
  const state = terminals.get(tabId);
  if (!state) {
    return;
  }

  const palette = applyHostTheme(options.theme);
  state.terminal.options.fontFamily = options.fontFamily;
  state.terminal.options.fontSize = options.fontSize;
  state.terminal.options.theme = {
    ...xtermTheme(palette),
    selectionBackground: options.selectionBackground,
    selectionInactiveBackground: options.selectionBackground
  };
  state.terminal.options.cursorStyle = options.cursorStyle;
  state.terminal.options.cursorBlink = options.cursorBlink;
  if (activeTabId === tabId) {
    scheduleFitActiveTerminal();
  }
}

function writeTerminal(tabId, token, value) {
  const state = terminals.get(tabId);
  if (!state) {
    send({ type: 'writeComplete', tabId, token, success: false });
    return;
  }

  try {
    state.terminal.write(value, () => {
      send({ type: 'writeComplete', tabId, token, success: true });
    });
  } catch {
    send({ type: 'writeComplete', tabId, token, success: false });
  }
}

function focusTerminal(tabId) {
  if (activeTabId === tabId) {
    terminals.get(tabId)?.terminal.focus();
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

function openSearch(tabId = activeTabId) {
  if (!terminals.has(tabId)) {
    return;
  }

  searchBarElement.classList.add('open');
  searchBarElement.setAttribute('aria-hidden', 'false');
  searchInputElement.focus();
  searchInputElement.select();
  runSearch(true);
}

function closeSearch(restoreFocus = true) {
  const state = terminals.get(activeTabId);
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
  const state = terminals.get(activeTabId);
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

function scheduleFitActiveTerminal() {
  if (resizeFrame !== null) {
    return;
  }

  resizeFrame = requestAnimationFrame(() => {
    resizeFrame = null;
    fitActiveTerminal();
  });
}

function fitActiveTerminal() {
  const state = terminals.get(activeTabId);
  if (!state) {
    return;
  }

  const bounds = terminalHostElement.getBoundingClientRect();
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
    send({ type: 'ready', tabId: state.tabId, columns, rows });
  } else if (changed) {
    send({ type: 'resize', tabId: state.tabId, columns, rows });
  }
}

window.terminalHost = {
  create: createTerminal,
  activate: activateTerminal,
  dispose: disposeTerminal,
  configure: configureTerminal,
  write: writeTerminal,
  focus: focusTerminal,
  openSearch
};

new ResizeObserver(scheduleFitActiveTerminal).observe(terminalHostElement);
window.addEventListener('wheel', handleWheelEvent, { passive: false });
window.addEventListener('load', () => send({ type: 'hostReady' }));

export {
  activateTerminal,
  configureTerminal,
  createTerminal,
  disableWebgl,
  disposeTerminal,
  enableWebgl,
  fitActiveTerminal,
  focusTerminal,
  handleKeyEvent,
  handleWheelEvent,
  openSearch,
  closeSearch,
  runSearch,
  scheduleFitActiveTerminal,
  writeTerminal
};
