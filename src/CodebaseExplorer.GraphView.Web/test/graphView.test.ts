import { describe, expect, it } from 'vitest';
import { Bridge } from '../src/bridge';
import { toElements } from '../src/elements';
import { GraphViewController } from '../src/graphView';
import type { Renderer } from '../src/graphView';
import { parseGraphView } from '../src/protocol';
import type { GraphView, ThemeMode } from '../src/protocol';
import { envelope, node, view } from './fixtures';

class FakeRenderer implements Renderer {
  loaded: GraphView[] = [];
  themes: ThemeMode[] = [];
  focused: string[] = [];

  load(graph: GraphView): void {
    this.loaded.push(graph);
  }

  setTheme(mode: ThemeMode): void {
    this.themes.push(mode);
  }

  focus(elementId: string): boolean {
    this.focused.push(elementId);
    return elementId !== 'missing';
  }
}

function setup() {
  const sent: unknown[] = [];
  const renderer = new FakeRenderer();
  const controller = new GraphViewController(renderer, new Bridge((message) => sent.push(JSON.parse(message))));
  return { sent, renderer, controller };
}

describe('GraphViewController', () => {
  it('announces readiness with the protocol version', () => {
    const { sent, controller } = setup();

    controller.start();

    expect(sent).toEqual([{ type: 'ready', version: 1, payload: { protocolVersion: 1 } }]);
  });

  it('loads a valid graph', () => {
    const { sent, renderer, controller } = setup();

    expect(controller.receive(envelope('graph/load', view()))).toBe('ok');
    expect(renderer.loaded).toHaveLength(1);
    expect(sent).toEqual([]);
  });

  it('rejects an invalid graph without rendering and reports an error', () => {
    const { sent, renderer, controller } = setup();

    const status = controller.receive(envelope('graph/load', view({ schemaVersion: 9 })));

    expect(status).toBe('error: graph/load: unsupported schema version 9');
    expect(renderer.loaded).toHaveLength(0);
    expect(sent).toEqual([{ type: 'error', version: 1, payload: { message: 'graph/load: unsupported schema version 9' } }]);
  });

  it('applies themes and rejects invalid ones', () => {
    const { renderer, controller } = setup();

    expect(controller.receive(envelope('view/theme', { mode: 'dark' }))).toBe('ok');
    expect(controller.receive(envelope('view/theme', { mode: 'neon' }))).toMatch(/^error:/);
    expect(renderer.themes).toEqual(['dark']);
  });

  it('focuses known elements and reports unknown ones', () => {
    const { controller } = setup();

    expect(controller.receive(envelope('graph/focus', { elementId: 'a' }))).toBe('ok');
    expect(controller.receive(envelope('graph/focus', { elementId: 'missing' }))).toBe('error: graph/focus: unknown element missing');
  });

  it('reports unsupported message types and invalid JSON', () => {
    const { sent, controller } = setup();

    expect(controller.receive(envelope('graph/teleport', {}))).toBe('error: unsupported message type graph/teleport');
    expect(controller.receive('{oops')).toBe('error: invalid JSON');
    expect(sent).toHaveLength(2);
  });

  it('sends selection and inspector requests in protocol format', () => {
    const { sent, controller } = setup();

    controller.selectionChanged(['a'], ['a|b']);
    controller.openInspector('a');

    expect(sent).toEqual([
      { type: 'selection/changed', version: 1, payload: { nodeIds: ['a'], edgeIds: ['a|b'] } },
      { type: 'node/openInspector', version: 1, payload: { elementId: 'a' } },
    ]);
  });
});

describe('toElements', () => {
  it('keeps untrusted labels as plain data', () => {
    const hostile = '<img src=x onerror=alert(1)>';
    const parsed = parseGraphView(view({ nodes: [node('a', { label: hostile }), node('b')] }));
    if (!parsed.ok) throw new Error(parsed.error);

    const elements = toElements(parsed.value);

    expect(elements[0]?.data.label).toBe(hostile);
  });

  it('maps evidence level, declaration and missing badge for styling', () => {
    const parsed = parseGraphView(view({
      nodes: [node('a'), node('b', { badges: ['missing'] })],
      edges: [{ id: 'a|b', source: 'a', target: 'b', count: 2, occurrences: 3, kinds: {}, maxLevel: 'syntactic', minLevel: 'heuristic', isDeclared: false }],
    }));
    if (!parsed.ok) throw new Error(parsed.error);

    const [, b, ab] = toElements(parsed.value);

    expect(b?.data.missing).toBe(1);
    expect(ab?.data).toMatchObject({ source: 'a', target: 'b', level: 'syntactic', declared: 0, count: 2 });
  });
});
