import { describe, expect, it } from 'vitest';
import { createMessage, parseElementId, parseEnvelope, parseGraphView, parseLayout, parseTheme } from '../src/protocol';
import { edge, envelope, node, view } from './fixtures';

describe('parseEnvelope', () => {
  it('accepts a valid envelope', () => {
    const result = parseEnvelope(envelope('graph/load', { a: 1 }));

    expect(result).toEqual({ ok: true, value: { type: 'graph/load', version: 1, payload: { a: 1 } } });
  });

  it.each([
    ['not json', 'invalid JSON'],
    ['[]', 'message is not an object'],
    ['null', 'message is not an object'],
    ['{"version":1,"payload":{}}', 'message has no type'],
    ['{"type":"","version":1,"payload":{}}', 'message has no type'],
    ['{"type":"x","version":2,"payload":{}}', 'unsupported protocol version 2'],
    ['{"type":"x","payload":{}}', 'unsupported protocol version undefined'],
    ['{"type":"x","version":1,"payload":[1]}', 'payload must be an object'],
    ['{"type":"x","version":1}', 'payload must be an object'],
  ])('rejects %s', (json, error) => {
    expect(parseEnvelope(json)).toEqual({ ok: false, error });
  });

  it('round-trips messages created by createMessage', () => {
    const parsed = parseEnvelope(createMessage('ready', { protocolVersion: 1 }));

    expect(parsed).toEqual({ ok: true, value: { type: 'ready', version: 1, payload: { protocolVersion: 1 } } });
  });
});

describe('parseGraphView', () => {
  it('accepts a view as produced by the .NET projector', () => {
    const result = parseGraphView(view());

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value.nodes.map((n) => n.id)).toEqual(['a', 'b']);
      expect(result.value.edges[0]?.kinds).toEqual({ 'project-reference': 1 });
    }
  });

  it('ignores unknown extra fields and fills optional ones', () => {
    const result = parseGraphView(view({ nodes: [node('a', { extra: true, badges: undefined, childCount: -1 }), node('b')] }));

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.value.nodes[0]?.badges).toEqual([]);
      expect(result.value.nodes[0]?.childCount).toBe(0);
    }
  });

  it.each([
    ['wrong schema version', view({ schemaVersion: 2 }), 'unsupported schema version 2'],
    ['missing nodes array', view({ nodes: 'x' }), 'nodes and edges must be arrays'],
    ['node without label', view({ nodes: [node('a', { label: 5 })] , edges: [] }), 'node needs id, kind and label'],
    ['empty node id', view({ nodes: [node('')], edges: [] }), 'node needs id, kind and label'],
    ['negative metric', view({ nodes: [node('a', { metrics: { fanIn: -1, fanOut: 0, size: 0 } })], edges: [] }), 'node a has invalid metrics'],
    ['duplicate node', view({ nodes: [node('a'), node('a')], edges: [] }), 'duplicate node id a'],
    ['unknown parent', view({ nodes: [node('a', { parentId: 'zz' })], edges: [] }), 'node a has an unknown parent'],
    ['dangling edge', view({ edges: [edge('a', 'zz')] }), 'edge a|zz references an unknown node'],
    ['duplicate edge', view({ edges: [edge('a', 'b'), edge('a', 'b')] }), 'duplicate edge id a|b'],
    ['invalid level', view({ edges: [edge('a', 'b', { maxLevel: 'certain' })] }), 'edge a|b has an invalid evidence level'],
    ['fractional count', view({ edges: [edge('a', 'b', { count: 1.5 })] }), 'edge a|b has invalid counts'],
  ])('rejects %s', (_name, payload, error) => {
    expect(parseGraphView(payload)).toEqual({ ok: false, error });
  });
});

describe('small payloads', () => {
  it('parses theme modes', () => {
    expect(parseTheme({ mode: 'dark' })).toEqual({ ok: true, value: 'dark' });
    expect(parseTheme({ mode: 'blue' }).ok).toBe(false);
    expect(parseLayout({ name: 'force' })).toEqual({ ok: true, value: 'force' });
    expect(parseLayout({ name: 'hierarchical' })).toEqual({ ok: true, value: 'hierarchical' });
    expect(parseLayout({ name: 'cose' }).ok).toBe(false);
    expect(parseLayout({ name: 7 }).ok).toBe(false);
  });

  it('parses element ids', () => {
    expect(parseElementId({ elementId: 'abc' })).toEqual({ ok: true, value: 'abc' });
    expect(parseElementId({ elementId: '' }).ok).toBe(false);
    expect(parseElementId({}).ok).toBe(false);
  });
});
