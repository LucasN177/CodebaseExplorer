// Static guards for SECURITY.md (graph web view): no HTML sinks or dynamic code in our sources and a
// strict CSP on the page. Cytoscape renders labels on a canvas, so data never becomes markup.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const root = join(__dirname, '..');
const sources = readdirSync(join(root, 'src')).filter((f) => f.endsWith('.ts'));

describe('graph web view security', () => {
  it.each(sources)('%s uses no HTML sinks or dynamic code', (file) => {
    const content = readFileSync(join(root, 'src', file), 'utf8');

    for (const forbidden of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function', 'fetch(', 'XMLHttpRequest', 'WebSocket']) {
      expect(content, `${file} contains ${forbidden}`).not.toContain(forbidden);
    }
  });

  it('index.html has a strict CSP and only loads the local bundle', () => {
    const html = readFileSync(join(root, 'public', 'index.html'), 'utf8');

    expect(html).toContain(
      `content="default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'none'"`,
    );
    expect(html).toContain('<script src="graph.js"></script>');
    expect(html).not.toMatch(/https?:\/\//);
    expect(html).not.toContain('type="module"');
  });
});
