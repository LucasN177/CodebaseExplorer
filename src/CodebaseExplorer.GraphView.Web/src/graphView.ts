// Message handling of the graph web view. Independent of the DOM entry point so it can be tested
// with a fake renderer. All data, expansion and filtering decisions stay in .NET; this side only
// renders and reports user interaction.
import { Bridge } from './bridge';
import { applyPatch, MessageTypes, parseElementId, parseEnvelope, parseGraphPatch, parseGraphView, parseLayout, parseTheme, PROTOCOL_VERSION } from './protocol';
import type { GraphPatch, GraphView, LayoutName, ThemeMode } from './protocol';

/** What the message handler needs from the renderer (Cytoscape in production). */
export interface Renderer {
  load(view: GraphView): void;
  /** Applies a patch that `applyPatch` accepted for the displayed graph. */
  patch(patch: GraphPatch): void;
  setTheme(mode: ThemeMode): void;
  /** Remembers the layout for later loads and re-arranges the current graph, if any. */
  setLayout(name: LayoutName): void;
  focus(elementId: string): boolean;
}

export class GraphViewController {
  /** The displayed graph, kept to validate patches before anything is rendered. */
  private current: GraphView | undefined;

  constructor(
    private readonly renderer: Renderer,
    private readonly bridge: Bridge,
  ) {}

  /** Announces readiness to the host. */
  start(): void {
    this.bridge.send(MessageTypes.ready, { protocolVersion: PROTOCOL_VERSION });
  }

  /**
   * Entry point called by the host through InvokeScript. Returns a short status string; invalid
   * messages are reported to the host as `error` and never partially applied.
   */
  receive(json: string): string {
    const envelope = parseEnvelope(json);
    if (!envelope.ok) return this.reject(envelope.error);

    const { type, payload } = envelope.value;
    switch (type) {
      case MessageTypes.graphLoad: {
        const view = parseGraphView(payload);
        if (!view.ok) return this.reject(`graph/load: ${view.error}`);
        this.current = view.value;
        this.renderer.load(view.value);
        return 'ok';
      }
      case MessageTypes.graphPatch: {
        const patch = parseGraphPatch(payload);
        if (!patch.ok) return this.reject(`graph/patch: ${patch.error}`);
        if (this.current === undefined) return this.reject('graph/patch: no graph is displayed');
        const next = applyPatch(this.current, patch.value);
        if (!next.ok) return this.reject(`graph/patch: ${next.error}`);
        this.current = next.value;
        this.renderer.patch(patch.value);
        return 'ok';
      }
      case MessageTypes.viewTheme: {
        const mode = parseTheme(payload);
        if (!mode.ok) return this.reject(`view/theme: ${mode.error}`);
        this.renderer.setTheme(mode.value);
        return 'ok';
      }
      case MessageTypes.viewLayout: {
        const name = parseLayout(payload);
        if (!name.ok) return this.reject(`view/layout: ${name.error}`);
        this.renderer.setLayout(name.value);
        return 'ok';
      }
      case MessageTypes.graphFocus: {
        const id = parseElementId(payload);
        if (!id.ok) return this.reject(`graph/focus: ${id.error}`);
        return this.renderer.focus(id.value) ? 'ok' : this.reject(`graph/focus: unknown element ${id.value}`);
      }
      default:
        // Unknown types are ignored (forward compatibility) but reported for logging.
        return this.reject(`unsupported message type ${type}`);
    }
  }

  /** Reports the current selection to the host. */
  selectionChanged(nodeIds: string[], edgeIds: string[]): void {
    this.bridge.send(MessageTypes.selectionChanged, { nodeIds, edgeIds });
  }


  /**
   * Double click on a node: expandable nodes are expanded or collapsed (GRAPH-006, decided by .NET), others open the
   * inspector. Uses the displayed graph's state, not renderer data.
   */
  activate(elementId: string): void {
    const node = this.current?.nodes.find((n) => n.id === elementId);
    if (node === undefined) return;
    if (node.isExpanded) this.bridge.send(MessageTypes.nodeCollapseRequested, { elementId });
    else if (node.isExpandable) this.bridge.send(MessageTypes.nodeExpandRequested, { elementId });
    else this.openInspector(elementId);
  }
  /** Asks the host to open the inspector for an element (double click). */
  openInspector(elementId: string): void {
    this.bridge.send(MessageTypes.nodeOpenInspector, { elementId });
  }

  private reject(error: string): string {
    this.bridge.send(MessageTypes.error, { message: error });
    return `error: ${error}`;
  }
}
