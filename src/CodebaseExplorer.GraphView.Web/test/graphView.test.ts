import { describe, expect, it } from 'vitest';
import { Bridge } from '../src/bridge';
import { edgeWidth, nodeScale, toElements } from '../src/elements';
import { GraphViewController } from '../src/graphView';
import type { Renderer } from '../src/graphView';
import { parseGraphView } from '../src/protocol';
import type { GraphView, LayoutName, ThemeMode } from '../src/protocol';
import { envelope, node, view } from './fixtures';

class FakeRenderer implements Renderer {
  loaded: GraphView[] = [];
  themes: ThemeMode[] = [];
  layouts: LayoutName[] = [];
  focused: string[] = [];

  setLayout(name: LayoutName): void {
    this.layouts.push(name);
  }

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

  it('applies layouts and rejects unknown ones without changing the layout', () => {
    const { sent, renderer, controller } = setup();

    expect(controller.receive(envelope('view/layout', { name: 'hierarchical' }))).toBe('ok');
    expect(controller.receive(envelope('view/layout', { name: 'concentric' }))).toBe('ok');
    expect(controller.receive(envelope('view/layout', { name: 'cose' }))).toBe('error: view/layout: name must be one of hierarchical, force, concentric');
    expect(controller.receive(envelope('view/layout', {}))).toMatch(/^error: view\/layout:/);
    expect(renderer.layouts).toEqual(['hierarchical', 'concentric']);
    expect(sent).toHaveLength(2);
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
    expect(ab?.data).toMatchObject({ source: 'a', target: 'b', level: 'syntactic', declared: 0, count: 2, width: 2.5 });
  });

  it('maps project and package ecosystems to one color family', () => {
    const parsed = parseGraphView(view({
      nodes: [node('a'), node('b', { ecosystem: 'npm', kind: 'external-dependency' }), node('c', { ecosystem: 'pypi' }), node('d', { ecosystem: undefined })],
      edges: [],
    }));
    if (!parsed.ok) throw new Error(parsed.error);

    expect(toElements(parsed.value).map((e) => e.data.ecosystem)).toEqual(['dotnet', 'node', 'python', 'other']);
  });
});

describe('visual scales', () => {
  it('widens edges logarithmically with the relationship count and caps the width', () => {
    expect(edgeWidth(1)).toBe(1.5);
    expect(edgeWidth(4)).toBe(3.5);
    expect(edgeWidth(1024)).toBe(7);
    expect(edgeWidth(0)).toBe(1.5);
  });

  it('grows nodes logarithmically with their size and caps the factor', () => {
    expect(nodeScale(0)).toBe(1);
    expect(nodeScale(99)).toBeCloseTo(1 + 2 / 3);
    expect(nodeScale(1_000_000)).toBe(2);
  });
});
