import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { WebglAddon } from '@xterm/addon-webgl';

const fitAddon = new FitAddon();
const terminal = new Terminal({
  cursorBlink: true,
  cursorStyle: 'bar',
  fontFamily: 'Cascadia Mono, Consolas, monospace',
  fontSize: 13,
  lineHeight: 1.1,
  scrollback: 5000,
  allowTransparency: false,
  theme: {
    background: '#1e1e1e',
    foreground: '#e6e9ef',
    cursor: '#e6e9ef',
    selectionBackground: '#264f78'
  }
});

terminal.loadAddon(fitAddon);
terminal.open(document.getElementById('terminal'));

try {
  const webgl = new WebglAddon();
  webgl.onContextLoss(() => webgl.dispose());
  terminal.loadAddon(webgl);
} catch {
  // DOM renderer remains available when WebGL2 is unavailable.
}

function send(message) {
  if (typeof invokeCSharpAction === 'function') {
    invokeCSharpAction(JSON.stringify(message));
  }
}

function fitAndReport() {
  fitAddon.fit();
  send({ type: 'resize', columns: terminal.cols, rows: terminal.rows });
}

terminal.onData(data => send({ type: 'input', data }));
terminal.attachCustomKeyEventHandler(event => {
  if (event.type === 'keydown' && event.ctrlKey && event.shiftKey && event.code === 'KeyC') {
    const selected = terminal.getSelection();
    if (selected) {
      navigator.clipboard.writeText(selected);
      terminal.clearSelection();
    }
    return false;
  }
  if (event.type === 'keydown' && event.ctrlKey && event.shiftKey && event.code === 'KeyV') {
    navigator.clipboard.readText().then(text => text && send({ type: 'input', data: text }));
    return false;
  }
  return true;
});

window.terminalWriteBase64 = value => {
  const bytes = Uint8Array.from(atob(value), character => character.charCodeAt(0));
  terminal.write(new TextDecoder().decode(bytes));
};
window.terminalFocus = () => terminal.focus();
window.terminalConfigure = options => {
  terminal.options.fontFamily = options.fontFamily;
  terminal.options.fontSize = options.fontSize;
  terminal.options.cursorStyle = options.cursorStyle;
  terminal.options.cursorBlink = options.cursorBlink;
  fitAndReport();
};

new ResizeObserver(fitAndReport).observe(document.getElementById('terminal'));
window.addEventListener('load', () => {
  fitAndReport();
  terminal.focus();
  send({ type: 'ready', columns: terminal.cols, rows: terminal.rows });
});
