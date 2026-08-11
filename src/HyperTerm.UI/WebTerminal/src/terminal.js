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
      background: '#1e1e1e',
      foreground: '#e6e9ef',
      cursor: '#e6e9ef',
      selectionBackground: options.selectionBackground
    }
  });

  const state = {
    tabId,
    element,
    terminal,
    fitAddon,
    searchAddon,
    searchResultsDisposable: null,
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
    if (activeTabId === tabId && searchBarElement.classList.contains('open')) {
      updateSearchResults(results);
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

  state.terminal.options.fontFamily = options.fontFamily;
  state.terminal.options.fontSize = options.fontSize;
  state.terminal.options.theme = {
    ...state.terminal.options.theme,
    selectionBackground: options.selectionBackground
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
  return {
    caseSensitive: searchCaseElement.classList.contains('active'),
    incremental: true,
    decorations: {
      matchBackground: '#515c6a',
      matchOverviewRuler: '#748496',
      activeMatchBackground: '#d18616',
      activeMatchColorOverviewRuler: '#d18616'
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

  return forward
    ? state.searchAddon.findNext(query, searchOptions())
    : state.searchAddon.findPrevious(query, searchOptions());
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
