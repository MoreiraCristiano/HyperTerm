import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { WebglAddon } from '@xterm/addon-webgl';

const terminalHostElement = document.getElementById('terminal-host');
const terminals = new Map();
const textDecoder = new TextDecoder();
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
    webglAddon: null,
    webglDisabled: false,
    started: false,
    lastColumns: 0,
    lastRows: 0
  };

  terminal.loadAddon(fitAddon);
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

function activateTerminal(tabId) {
  const next = terminals.get(tabId);
  if (!next) {
    return;
  }

  if (activeTabId && activeTabId !== tabId) {
    const previous = terminals.get(activeTabId);
    if (previous) {
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

  disableWebgl(state);
  state.terminal.dispose();
  state.element.remove();
  terminals.delete(tabId);
  if (activeTabId === tabId) {
    activeTabId = null;
  }
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
    send({ type: 'writeComplete', tabId, token });
    return;
  }

  const bytes = Uint8Array.from(
    atob(value),
    character => character.charCodeAt(0));
  state.terminal.write(textDecoder.decode(bytes), () => {
    send({ type: 'writeComplete', tabId, token });
  });
}

function focusTerminal(tabId) {
  if (activeTabId === tabId) {
    terminals.get(tabId)?.terminal.focus();
  }
}

function enableWebgl(state) {
  if (state.webglAddon || state.webglDisabled) {
    return;
  }

  try {
    const webglAddon = new WebglAddon();
    state.webglAddon = webglAddon;
    webglAddon.onContextLoss(() => {
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
  if (!state.webglAddon) {
    return;
  }

  try {
    state.webglAddon.dispose();
  } catch {
    // The renderer may already be disposed after a context loss.
  }
  state.webglAddon = null;
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
  focus: focusTerminal
};

new ResizeObserver(scheduleFitActiveTerminal).observe(terminalHostElement);
window.addEventListener('load', () => send({ type: 'hostReady' }));
