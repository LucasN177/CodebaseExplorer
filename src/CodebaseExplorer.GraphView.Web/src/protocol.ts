// Wire format of the .NET <-> web view protocol (GRAPH_ENGINE.md §5/§6).
// Mirrors src/CodebaseExplorer.Contracts/Graph (camelCase JSON). Every inbound message is validated
// here before it touches Cytoscape; anything unexpected is rejected, never partially applied.

export const PROTOCOL_VERSION = 1;

export const MessageTypes = {
  graphLoad: 'graph/load',
  graphPatch: 'graph/patch',
  graphFocus: 'graph/focus',
  viewTheme: 'view/theme',
  viewLayout: 'view/layout',
  ready: 'ready',
  selectionChanged: 'selection/changed',
  nodeExpandRequested: 'node/expandRequested',
  nodeCollapseRequested: 'node/collapseRequested',
  nodeOpenInspector: 'node/openInspector',
  error: 'error',
} as const;

export interface GraphNodeMetrics {
  fanIn: number;
  fanOut: number;
  size: number;
  changeCount?: number;
}

export interface GraphNode {
  id: string;
  parentId?: string;
  kind: string;
  label: string;
  sublabel?: string;
  language?: string;
  ecosystem?: string;
  isExpandable: boolean;
  isExpanded: boolean;
  childCount: number;
  metrics: GraphNodeMetrics;
  badges: string[];
}

export type EvidenceLevel = 'heuristic' | 'syntactic' | 'confirmed';

export interface GraphEdge {
  id: string;
  source: string;
  target: string;
  count: number;
  occurrences: number;
  kinds: Record<string, number>;
  maxLevel: EvidenceLevel;
  minLevel: EvidenceLevel;
  isDeclared: boolean;
}

export interface GraphView {
  schemaVersion: number;
  repositoryId: string;
  level: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export type ThemeMode = 'light' | 'dark';

/** Layouts of `view/layout` (GRAPH_ENGINE.md §8). */
export const LAYOUT_NAMES = ['hierarchical', 'force', 'concentric'] as const;

export type LayoutName = (typeof LAYOUT_NAMES)[number];

export interface Envelope {
  type: string;
  version: number;
  payload: Record<string, unknown>;
}

export type Parsed<T> = { ok: true; value: T } | { ok: false; error: string };

const EVIDENCE_LEVELS: readonly string[] = ['heuristic', 'syntactic', 'confirmed'];

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const isString = (value: unknown): value is string => typeof value === 'string';
const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0;
const fail = <T>(error: string): Parsed<T> => ({ ok: false, error });

/** Parses and validates an envelope `{ type, version, payload }`. */
export function parseEnvelope(json: string): Parsed<Envelope> {
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return fail('invalid JSON');
  }

  if (!isObject(value)) return fail('message is not an object');
  if (!isString(value.type) || value.type.length === 0) return fail('message has no type');
  if (value.version !== PROTOCOL_VERSION) return fail(`unsupported protocol version ${String(value.version)}`);
  if (!isObject(value.payload)) return fail('payload must be an object');

  return { ok: true, value: { type: value.type, version: value.version, payload: value.payload } };
}

/** Validates a `graph/load` payload (GraphViewDto). Edges must reference existing nodes. */
export function parseGraphView(payload: Record<string, unknown>): Parsed<GraphView> {
  if (payload.schemaVersion !== 1) return fail(`unsupported schema version ${String(payload.schemaVersion)}`);
  if (!isString(payload.repositoryId) || !isString(payload.level)) return fail('repositoryId and level are required');
  if (!Array.isArray(payload.nodes) || !Array.isArray(payload.edges)) return fail('nodes and edges must be arrays');

  const nodes: GraphNode[] = [];
  const ids = new Set<string>();
  for (const raw of payload.nodes) {
    const node = parseNode(raw);
    if (!node.ok) return node;
    if (ids.has(node.value.id)) return fail(`duplicate node id ${node.value.id}`);
    ids.add(node.value.id);
    nodes.push(node.value);
  }

  for (const node of nodes) {
    if (node.parentId !== undefined && !ids.has(node.parentId)) return fail(`node ${node.id} has an unknown parent`);
  }

  const edges: GraphEdge[] = [];
  const edgeIds = new Set<string>();
  for (const raw of payload.edges) {
    const edge = parseEdge(raw);
    if (!edge.ok) return edge;
    if (!ids.has(edge.value.source) || !ids.has(edge.value.target)) return fail(`edge ${edge.value.id} references an unknown node`);
    if (edgeIds.has(edge.value.id)) return fail(`duplicate edge id ${edge.value.id}`);
    edgeIds.add(edge.value.id);
    edges.push(edge.value);
  }

  return { ok: true, value: { schemaVersion: 1, repositoryId: payload.repositoryId, level: payload.level, nodes, edges } };
}

/** Validates a `view/theme` payload. */
export function parseTheme(payload: Record<string, unknown>): Parsed<ThemeMode> {
  return payload.mode === 'light' || payload.mode === 'dark' ? { ok: true, value: payload.mode } : fail('mode must be light or dark');
}

/** Validates a `view/layout` payload. */
export function parseLayout(payload: Record<string, unknown>): Parsed<LayoutName> {
  const name = payload.name;
  return isString(name) && (LAYOUT_NAMES as readonly string[]).includes(name)
    ? { ok: true, value: name as LayoutName }
    : fail(`name must be one of ${LAYOUT_NAMES.join(', ')}`);
}

/** Validates a payload `{ elementId }` (e.g. `graph/focus`). */
export function parseElementId(payload: Record<string, unknown>): Parsed<string> {
  return isString(payload.elementId) && payload.elementId.length > 0 ? { ok: true, value: payload.elementId } : fail('elementId is required');
}

function parseNode(raw: unknown): Parsed<GraphNode> {
  if (!isObject(raw)) return fail('node is not an object');
  const { id, kind, label, metrics } = raw;
  if (!isString(id) || id.length === 0 || !isString(kind) || !isString(label)) return fail('node needs id, kind and label');
  if (!isObject(metrics) || !isCount(metrics.fanIn) || !isCount(metrics.fanOut) || !isCount(metrics.size)) {
    return fail(`node ${id} has invalid metrics`);
  }

  const optional = (value: unknown): string | undefined => (isString(value) ? value : undefined);
  const badges = Array.isArray(raw.badges) ? raw.badges.filter(isString) : [];

  return {
    ok: true,
    value: {
      id,
      parentId: optional(raw.parentId),
      kind,
      label,
      sublabel: optional(raw.sublabel),
      language: optional(raw.language),
      ecosystem: optional(raw.ecosystem),
      isExpandable: raw.isExpandable === true,
      isExpanded: raw.isExpanded === true,
      childCount: isCount(raw.childCount) ? raw.childCount : 0,
      metrics: {
        fanIn: metrics.fanIn,
        fanOut: metrics.fanOut,
        size: metrics.size,
        changeCount: isCount(metrics.changeCount) ? metrics.changeCount : undefined,
      },
      badges,
    },
  };
}

function parseEdge(raw: unknown): Parsed<GraphEdge> {
  if (!isObject(raw)) return fail('edge is not an object');
  const { id, source, target, count, occurrences, kinds, maxLevel, minLevel } = raw;
  if (!isString(id) || !isString(source) || !isString(target)) return fail('edge needs id, source and target');
  if (!isCount(count) || !isCount(occurrences)) return fail(`edge ${id} has invalid counts`);
  if (!isString(maxLevel) || !EVIDENCE_LEVELS.includes(maxLevel) || !isString(minLevel) || !EVIDENCE_LEVELS.includes(minLevel)) {
    return fail(`edge ${id} has an invalid evidence level`);
  }

  const kindCounts: Record<string, number> = {};
  if (isObject(kinds)) {
    for (const [kind, value] of Object.entries(kinds)) {
      if (isCount(value)) kindCounts[kind] = value;
    }
  }

  return {
    ok: true,
    value: {
      id,
      source,
      target,
      count,
      occurrences,
      kinds: kindCounts,
      maxLevel: maxLevel as EvidenceLevel,
      minLevel: minLevel as EvidenceLevel,
      isDeclared: raw.isDeclared === true,
    },
  };
}

/** Builds an outbound envelope as JSON. */
export function createMessage(type: string, payload: Record<string, unknown>): string {
  return JSON.stringify({ type, version: PROTOCOL_VERSION, payload });
}

/** Payload of `graph/patch` (GRAPH-006): the difference to the displayed graph after an expand or collapse. */
export interface GraphPatch {
  removedNodeIds: string[];
  removedEdgeIds: string[];
  addedNodes: GraphNode[];
  updatedNodes: GraphNode[];
  addedEdges: GraphEdge[];
}

const parseIds = (value: unknown): string[] | undefined =>
  Array.isArray(value) && value.every((id) => isString(id) && id.length > 0) ? (value as string[]) : undefined;

/** Validates the structure of a `graph/patch` payload; consistency with the displayed graph is checked by `applyPatch`. */
export function parseGraphPatch(payload: Record<string, unknown>): Parsed<GraphPatch> {
  const removedNodeIds = parseIds(payload.removedNodeIds);
  const removedEdgeIds = parseIds(payload.removedEdgeIds);
  if (removedNodeIds === undefined || removedEdgeIds === undefined) return fail('removedNodeIds and removedEdgeIds must be arrays of ids');
  if (!Array.isArray(payload.addedNodes) || !Array.isArray(payload.updatedNodes) || !Array.isArray(payload.addedEdges)) {
    return fail('addedNodes, updatedNodes and addedEdges must be arrays');
  }

  const addedNodes: GraphNode[] = [];
  for (const raw of payload.addedNodes) {
    const node = parseNode(raw);
    if (!node.ok) return node;
    addedNodes.push(node.value);
  }

  const updatedNodes: GraphNode[] = [];
  for (const raw of payload.updatedNodes) {
    const node = parseNode(raw);
    if (!node.ok) return node;
    updatedNodes.push(node.value);
  }

  const addedEdges: GraphEdge[] = [];
  for (const raw of payload.addedEdges) {
    const edge = parseEdge(raw);
    if (!edge.ok) return edge;
    addedEdges.push(edge.value);
  }

  return { ok: true, value: { removedNodeIds, removedEdgeIds, addedNodes, updatedNodes, addedEdges } };
}

/**
 * Applies a patch to a graph without touching the input (pure). Rejects patches that do not fit the graph — unknown
 * ids, duplicates, changed parents, dangling parents or edges — so a patch is either applied completely or not at all.
 * Removing a node also removes its edges.
 */
export function applyPatch(view: GraphView, patch: GraphPatch): Parsed<GraphView> {
  const nodes = new Map(view.nodes.map((n) => [n.id, n]));
  const edges = new Map(view.edges.map((e) => [e.id, e]));

  for (const id of patch.removedNodeIds) {
    if (!nodes.delete(id)) return fail(`cannot remove unknown node ${id}`);
  }

  for (const [id, edge] of edges) {
    if (!nodes.has(edge.source) || !nodes.has(edge.target)) edges.delete(id);
  }

  for (const id of patch.removedEdgeIds) {
    const known = view.edges.some((e) => e.id === id);
    if (!known) return fail(`cannot remove unknown edge ${id}`);
    edges.delete(id);
  }

  for (const node of patch.addedNodes) {
    if (nodes.has(node.id)) return fail(`node ${node.id} already exists`);
    nodes.set(node.id, node);
  }

  for (const node of patch.updatedNodes) {
    const existing = nodes.get(node.id);
    if (existing === undefined) return fail(`cannot update unknown node ${node.id}`);
    if (existing.parentId !== node.parentId) return fail(`node ${node.id} cannot change its parent`);
    nodes.set(node.id, node);
  }

  for (const node of nodes.values()) {
    if (node.parentId !== undefined && !nodes.has(node.parentId)) return fail(`node ${node.id} has an unknown parent`);
  }

  for (const edge of patch.addedEdges) {
    if (edges.has(edge.id)) return fail(`edge ${edge.id} already exists`);
    if (!nodes.has(edge.source) || !nodes.has(edge.target)) return fail(`edge ${edge.id} references an unknown node`);
    edges.set(edge.id, edge);
  }

  return { ok: true, value: { ...view, nodes: [...nodes.values()], edges: [...edges.values()] } };
}
