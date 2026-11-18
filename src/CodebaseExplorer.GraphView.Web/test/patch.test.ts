// GRAPH-006: graph/patch validation and application, expand/collapse requests.
import { describe, expect, it } from 'vitest';
import { Bridge } from '../src/bridge';
import { nodeElement } from '../src/elements';
import { GraphViewController } from '../src/graphView';
import type { Renderer } from '../src/graphView';
import { applyPatch, parseGraphPatch, parseGraphView } from '../src/protocol';
import type { GraphPatch, GraphView } from '../src/protocol';
import { edge, envelope, node, view } from './fixtures';

const patch = (overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
  removedNodeIds: [],
  removedEdgeIds: [],
  addedNodes: [],
  updatedNodes: [],
  addedEdges: [],
  ...overrides,
});

function parsedView(raw: Record<string, unknown>): GraphView {
  const parsed = parseGraphView(raw);
  if (!parsed.ok) throw new Error(parsed.error);
  return parsed.value;
}

function parsedPatch(raw: Record<string, unknown>): GraphPatch {
  const parsed = parseGraphPatch(raw);
  if (!parsed.ok) throw new Error(parsed.error);
  return parsed.value;
}

class PatchRenderer implements Renderer {
  patches: GraphPatch[] = [];
  load(): void {}
  patch(value: GraphPatch): void {
    this.patches.push(value);
  }
  setTheme(): void {}
  setLayout(): void {}
  focus(): boolean {
    return true;
  }
}

function setup() {
  const sent: { type: string; payload: Record<string, unknown> }[] = [];
  const renderer = new PatchRenderer();
  const controller = new GraphViewController(renderer, new Bridge((message) => sent.push(JSON.parse(message))));
  return { sent, renderer, controller };
}

const expandable = view({ nodes: [node('a', { isExpandable: true }), node('b')], edges: [edge('a', 'b')] });

describe('applyPatch', () => {
  it('expands a node: children are added, the node is updated and edges move', () => {
    const expanded = applyPatch(
      parsedView(expandable),
      parsedPatch(
        patch({
          removedEdgeIds: ['a|b'],
          addedNodes: [node('m', { kind: 'module', parentId: 'a' })],
          updatedNodes: [node('a', { isExpandable: true, isExpanded: true })],
          addedEdges: [edge('m', 'b')],
        }),
      ),
    );

    expect(expanded.ok).toBe(true);
    if (!expanded.ok) return;
    expect(expanded.value.nodes.map((n) => [n.id, n.parentId, n.isExpanded])).toEqual([
      ['a', undefined, true],
      ['b', undefined, false],
      ['m', 'a', false],
    ]);
    expect(expanded.value.edges.map((e) => e.id)).toEqual(['m|b']);
  });

  it('removing a node also removes its edges', () => {
    const result = applyPatch(parsedView(view()), parsedPatch(patch({ removedNodeIds: ['b'] })));

    expect(result.ok && result.value.edges).toEqual([]);
  });

  it.each([
    ['unknown removed node', patch({ removedNodeIds: ['x'] })],
    ['unknown removed edge', patch({ removedEdgeIds: ['x|y'] })],
    ['duplicate added node', patch({ addedNodes: [node('a')] })],
    ['unknown updated node', patch({ updatedNodes: [node('x')] })],
    ['changed parent', patch({ addedNodes: [node('p')], updatedNodes: [node('b', { parentId: 'p' })] })],
    ['dangling parent', patch({ addedNodes: [node('c', { parentId: 'x' })] })],
    ['dangling edge', patch({ addedEdges: [edge('a', 'x')] })],
    ['child left without its parent', patch({ addedNodes: [node('c', { parentId: 'a' })], removedNodeIds: [] })],
  ])('rejects a patch that does not fit: %s', (name, raw) => {
    const base = name === 'child left without its parent'
      ? applyPatch(parsedView(view()), parsedPatch(raw))
      : undefined;
    if (base !== undefined) {
      // Adding a child is fine; removing only its parent afterwards must be rejected.
      expect(base.ok).toBe(true);
      if (!base.ok) return;
      expect(applyPatch(base.value, parsedPatch(patch({ removedNodeIds: ['a'] }))).ok).toBe(false);
      return;
    }

    expect(applyPatch(parsedView(view()), parsedPatch(raw)).ok).toBe(false);
  });

  it('does not change the input graph', () => {
    const graph = parsedView(view());
    applyPatch(graph, parsedPatch(patch({ removedNodeIds: ['b'] })));

    expect(graph.nodes).toHaveLength(2);
    expect(graph.edges).toHaveLength(1);
  });
});

describe('parseGraphPatch', () => {
  it.each([
    ['missing arrays', {}],
    ['invalid id', patch({ removedNodeIds: [''] })],
    ['invalid node', patch({ addedNodes: [{ id: 'x' }] })],
    ['invalid edge', patch({ addedEdges: [{ id: 'x' }] })],
  ])('rejects %s', (_, raw) => {
    expect(parseGraphPatch(raw as Record<string, unknown>).ok).toBe(false);
  });
});

describe('GraphViewController patch handling', () => {
  it('applies a valid patch to the displayed graph', () => {
    const { renderer, controller, sent } = setup();
    controller.receive(envelope('graph/load', expandable));

    expect(controller.receive(envelope('graph/patch', patch({ updatedNodes: [node('b', { label: 'B' })] })))).toBe('ok');
    expect(renderer.patches).toHaveLength(1);
    expect(sent).toEqual([]);
  });

  it('rejects a patch before any graph and a patch that does not fit, without rendering', () => {
    const { renderer, controller, sent } = setup();

    expect(controller.receive(envelope('graph/patch', patch()))).toMatch(/^error: graph\/patch: no graph/);
    controller.receive(envelope('graph/load', expandable));
    expect(controller.receive(envelope('graph/patch', patch({ removedNodeIds: ['x'] })))).toMatch(/^error: graph\/patch: cannot remove/);
    expect(renderer.patches).toEqual([]);
    expect(sent.map((m) => m.type)).toEqual(['error', 'error']);
  });

  it('double click expands, collapses or opens the inspector depending on the node state', () => {
    const { controller, sent } = setup();
    controller.receive(
      envelope('graph/load', view({ nodes: [node('a', { isExpandable: true }), node('b', { isExpandable: true, isExpanded: true }), node('c')], edges: [] })),
    );

    controller.activate('a');
    controller.activate('b');
    controller.activate('c');
    controller.activate('unknown');

    expect(sent).toEqual([
      { type: 'node/expandRequested', version: 1, payload: { elementId: 'a' } },
      { type: 'node/collapseRequested', version: 1, payload: { elementId: 'b' } },
      { type: 'node/openInspector', version: 1, payload: { elementId: 'c' } },
    ]);
  });

  it('node elements carry the expansion state for styling', () => {
    const parsed = parsedView(view({ nodes: [node('a', { isExpandable: true })], edges: [] }));

    expect(nodeElement(parsed.nodes[0]!).data).toMatchObject({ expandable: 1, expanded: 0 });
  });
});
