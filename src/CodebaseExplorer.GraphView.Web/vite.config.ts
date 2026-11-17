// Builds the graph web view as ONE classic script (IIFE) plus a static index.html.
// Classic script, not an ES module: Chromium (WebView2) applies CORS to module scripts and pages
// loaded from file:// have the origin "null"; a classic script avoids that (page loaded from file://,
// ADR-006 spike result, where classic scripts were verified).
// Output goes to the Desktop project, where it is committed so that the .NET build needs no Node.
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import { sourceHash } from './buildInfo.ts';

const root = fileURLToPath(new URL('.', import.meta.url));

export default defineConfig({
  root,
  publicDir: 'public',
  build: {
    outDir: '../CodebaseExplorer.Desktop/GraphView',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    lib: {
      entry: 'src/main.ts',
      formats: ['iife'],
      name: 'CodebaseExplorerGraph',
      fileName: () => 'graph.js',
    },
  },
  plugins: [
    {
      name: 'codebase-explorer-build-info',
      apply: 'build',
      generateBundle() {
        this.emitFile({
          type: 'asset',
          fileName: 'build-info.json',
          source: `${JSON.stringify({ sourceHash: sourceHash(root) }, null, 2)}\n`,
        });
      },
    },
  ],
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node',
  },
});
