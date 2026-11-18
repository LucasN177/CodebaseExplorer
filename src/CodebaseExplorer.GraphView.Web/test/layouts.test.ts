// Runs the real layout extensions on headless Cytoscape (no DOM needed).
import cytoscape from 'cytoscape';
import type { Core, ElementDefinition } from 'cytoscape';
import dagre from 'cytoscape-dagre';
import fcose from 'cytoscape-fcose';
import { beforeAll, describe, expect, it } from 'vitest';
import { applyFocusDimming, DIMMED_CLASS } from '../src/highlight';
import { DEFAULT_LAYOUT, FORCE_REFINEMENT_LIMIT, layoutOptions } from '../src/layouts';
import { LAYOUT_NAMES } from '../src/protocol';

beforeAll(() => {
  cytoscape.use(dagre);
  cytoscape.use(fcose);
});

/** A layered dependency graph: app → services → domain, plus one isolated node. */
function layeredGraph(): ElementDefinition[] {
  const nodes = ['app', 'svc1', 'svc2', 'domain', 'lonely'].map((id) => ({ group: 'nodes' as const, data: { id } }));
  const edges = [
    ['app', 'svc1'],
    ['app', 'svc2'],
    ['svc1', 'domain'],
    ['svc2', 'domain'],
  ].map(([source, target]) => ({ group: 'edges' as const, data: { id: `${source}|${target}`, source, target } }));
  return [...nodes, ...edges];
}

function headless(elements: ElementDefinition[]): Core {
  return cytoscape({ headless: true, styleEnabled: true, elements, style: [{ selector: 'node', style: { width: 28, height: 18 } }] });
}

describe('layoutOptions', () => {
  it('uses the force layout by default', () => {
    expect(DEFAULT_LAYOUT).toBe('force');
  });

  it.each(LAYOUT_NAMES)('%s places every node at a distinct finite position', (name) => {
    const cy = headless(layeredGraph());

    cy.layout(layoutOptions(name, cy.elements().length)).run();

    const positions = cy.nodes().map((n) => n.position());
    for (const { x, y } of positions) {
      expect(Number.isFinite(x) && Number.isFinite(y)).toBe(true);
    }
    expect(new Set(positions.map(({ x, y }) => `${Math.round(x)},${Math.round(y)}`)).size).toBe(positions.length);
  });

  it('hierarchical puts dependencies below their dependents', () => {
    const cy = headless(layeredGraph());

    cy.layout(layoutOptions('hierarchical', cy.elements().length)).run();

    const y = (id: string): number => cy.getElementById(id).position().y;
    expect(y('app')).toBeLessThan(y('svc1'));
    expect(y('svc1')).toBe(y('svc2'));
    expect(y('svc1')).toBeLessThan(y('domain'));
  });

  it('concentric puts the most connected node in the center', () => {
    const cy = headless(layeredGraph());

    cy.layout(layoutOptions('concentric', cy.elements().length)).run();

    const box = cy.nodes().boundingBox({});
    const center = { x: (box.x1 + box.x2) / 2, y: (box.y1 + box.y2) / 2 };
    const distance = (id: string): number => Math.hypot(cy.getElementById(id).position().x - center.x, cy.getElementById(id).position().y - center.y);
    // app, svc1/svc2 and domain all have degree 2; the isolated node (degree 0) is on the outer ring.
    expect(distance('lonely')).toBeGreaterThan(distance('app'));
  });

  it('refines the force layout only up to the element limit', () => {
    expect(layoutOptions('force', FORCE_REFINEMENT_LIMIT)).toMatchObject({ name: 'fcose', quality: 'default' });
    expect(layoutOptions('force', FORCE_REFINEMENT_LIMIT + 1)).toMatchObject({ name: 'fcose', quality: 'draft' });
  });

  it('never uses the slow built-in cose layout', () => {
    for (const name of LAYOUT_NAMES) {
      expect(layoutOptions(name, 10).name).not.toBe('cose');
    }
  });
});

describe('applyFocusDimming', () => {
  it('keeps a selected node, its edges and neighbors visible and dims the rest', () => {
    const cy = headless(layeredGraph());

    cy.getElementById('svc1').select();
    applyFocusDimming(cy);

    const dimmed = cy.elements(`.${DIMMED_CLASS}`).map((e) => e.id()).sort();
    expect(dimmed).toEqual(['app|svc2', 'lonely', 'svc2', 'svc2|domain']);
  });

  it('keeps a selected edge and its endpoints visible', () => {
    const cy = headless(layeredGraph());

    cy.getElementById('app|svc1').select();
    applyFocusDimming(cy);

    const visible = cy.elements().filter((e) => !e.hasClass(DIMMED_CLASS)).map((e) => e.id()).sort();
    expect(visible).toEqual(['app', 'app|svc1', 'svc1']);
  });

  it('removes the dimming when nothing is selected', () => {
    const cy = headless(layeredGraph());
    cy.getElementById('svc1').select();
    applyFocusDimming(cy);

    cy.elements().unselect();
    applyFocusDimming(cy);

    expect(cy.elements(`.${DIMMED_CLASS}`).length).toBe(0);
  });
});
