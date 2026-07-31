import { build } from 'esbuild';
import { copyFile, mkdir } from 'node:fs/promises';

await mkdir('dist', { recursive: true });
await build({
  entryPoints: ['src/terminal.js'],
  bundle: true,
  minify: true,
  sourcemap: false,
  outfile: 'dist/terminal.js',
  platform: 'browser',
  target: ['chrome120']
});
await copyFile('src/index.html', 'dist/index.html');
await copyFile('node_modules/@xterm/xterm/css/xterm.css', 'dist/xterm.css');
