// Entry point of graph.js (classic script, loaded from file:// by the Avalonia NativeWebView).
import cytoscape from 'cytoscape';
import type { Core } from 'cytoscape';
import dagre from 'cytoscape-dagre';
import fcose from 'cytoscape-fcose';
import { Bridge, hostTransport } from './bridge';
import { edgeElement, nodeElement, toElements } from './elements';
import { GraphViewController } from './graphView';
import type { Renderer } from './graphView';
import { applyFocusDimming } from './highlight';
import { DEFAULT_LAYOUT, layoutOptions } from './layouts';
import type { LayoutName, ThemeMode } from './protocol';
import { backgroundOf, stylesheet } from './styles';

declare global {
  // The host calls ceReceive(json) through NativeWebView.InvokeScript.
  // eslint-disable-next-line no-var
  var ceReceive: ((json: string) => string) | undefined;
}

cytoscape.use(dagre);
cytoscape.use(fcose);

class CytoscapeRenderer implements Renderer {
  private layout: LayoutName = DEFAULT_LAYOUT;

  constructor(
    private readonly cy: Core,
    private readonly container: HTMLElement,
  ) {}

  load(view: Parameters<Renderer['load']>[0]): void {
    this.cy.batch(() => {
      this.cy.elements().remove();
      this.cy.add(toElements(view));
    });
    this.arrange();
  }

  patch(patch: Parameters<Renderer['patch']>[0]): void {
    this.cy.batch(() => {
      // Removing a node also removes its edges and descendants; removing an already removed edge is a no-op.
      for (const id of patch.removedNodeIds) this.cy.getElementById(id).remove();
      for (const id of patch.removedEdgeIds) this.cy.getElementById(id).remove();
      this.cy.add(patch.addedNodes.map(nodeElement));
      for (const node of patch.updatedNodes) this.cy.getElementById(node.id).data(nodeElement(node).data);
      this.cy.add(patch.addedEdges.map(edgeElement));
    });
    this.arrange();
  }

  setTheme(mode: ThemeMode): void {
    this.container.style.backgroundColor = backgroundOf(mode);
    this.cy.style(stylesheet(mode));
  }

  setLayout(name: LayoutName): void {
    const changed = name !== this.layout;
    this.layout = name;
    if (changed && this.cy.elements().nonempty()) this.arrange();
  }

  focus(elementId: string): boolean {
    const element = this.cy.getElementById(elementId);
    if (element.empty()) return false;
    this.cy.elements().unselect();
    element.select();
    this.cy.animate({ center: { eles: element }, duration: 200 });
    return true;
  }

  private arrange(): void {
    if (this.cy.elements().empty()) return;
    this.cy.layout(layoutOptions(this.layout, this.cy.elements().length)).run();
  }
}

function start(): void {
  const container = document.getElementById('graph');
  if (container === null) return;

  // Zoom per mouse-wheel step: 1.5 × Cytoscape's default (0.3 made zooming far too slow in the WebView).
  const cy = cytoscape({ container, elements: [], style: stylesheet('light'), wheelSensitivity: 1.5 });
  const renderer = new CytoscapeRenderer(cy, container);
  const controller = new GraphViewController(renderer, new Bridge(hostTransport()));

  const reportSelection = (): void =>
    controller.selectionChanged(cy.nodes(':selected').map((n) => n.id()), cy.edges(':selected').map((e) => e.id()));
  cy.on('select unselect', () => {
    applyFocusDimming(cy);
    reportSelection();
  });
  // Double click: expand/collapse an expandable node (GRAPH-006), otherwise open the inspector.
  cy.on('dbltap', 'node', (event) => controller.activate(event.target.id()));

  globalThis.ceReceive = (json: string) => controller.receive(json);
  controller.start();
}

start();
