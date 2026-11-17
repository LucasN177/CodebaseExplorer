// Test data shaped like the JSON produced by CodebaseExplorer.Contracts.Graph.GraphJsonContext.
export const node = (id: string, overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
  id,
  kind: 'project',
  label: `Project ${id}`,
  sublabel: `src/${id}`,
  language: 'csharp',
  ecosystem: 'dotnet',
  isExpandable: false,
  isExpanded: false,
  childCount: 0,
  metrics: { fanIn: 0, fanOut: 1, size: 3 },
  badges: [],
  ...overrides,
});

export const edge = (source: string, target: string, overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
  id: `${source}|${target}`,
  source,
  target,
  count: 1,
  occurrences: 1,
  kinds: { 'project-reference': 1 },
  maxLevel: 'confirmed',
  minLevel: 'confirmed',
  isDeclared: true,
  ...overrides,
});

export const view = (overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
  schemaVersion: 1,
  repositoryId: '0193a1b2c3d47e5f80718293a4b5c6d7',
  level: 'project',
  nodes: [node('a'), node('b')],
  edges: [edge('a', 'b')],
  breadcrumbs: [],
  ...overrides,
});

export const envelope = (type: string, payload: unknown, version: unknown = 1): string =>
  JSON.stringify({ type, version, payload });
