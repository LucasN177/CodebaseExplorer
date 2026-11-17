// Source hash of the graph web view. The build writes it to build-info.json next to graph.js; a .NET
// test recomputes it from the committed sources and fails if the committed bundle is stale.
// The algorithm is mirrored in tests/CodebaseExplorer.IntegrationTests/GraphView/GraphViewAssetsTests.cs.
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

/** Files whose content determines the bundle, relative to the project root. */
export const HASHED_ROOTS = ['src', 'public', 'package.json', 'package-lock.json', 'tsconfig.json', 'vite.config.ts', 'buildInfo.ts'];

function collect(root: string, entry: string, files: string[]): void {
  const path = join(root, entry);
  if (statSync(path).isDirectory()) {
    for (const child of readdirSync(path)) collect(root, join(entry, child), files);
  } else {
    files.push(entry);
  }
}

/** SHA-256 over `path + "\n" + content(LF) + "\n"` of all hashed files, ordered by path (ordinal). */
export function sourceHash(root: string): string {
  const files: string[] = [];
  for (const entry of HASHED_ROOTS) collect(root, entry, files);

  const normalized = files
    .map((file) => relative(root, join(root, file)).split(sep).join('/'))
    .sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));

  const hash = createHash('sha256');
  for (const file of normalized) {
    const content = readFileSync(join(root, file), 'utf8').replace(/\r\n/g, '\n');
    hash.update(`${file}\n${content}\n`, 'utf8');
  }

  return hash.digest('hex');
}
