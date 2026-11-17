// Transport to the .NET host. Avalonia NativeWebView injects a global `invokeCSharpAction(string)`
// (spike GRAPH-001); messages arrive in .NET as NativeWebView.WebMessageReceived.
import { createMessage } from './protocol';

declare global {
  // Injected by the host; absent when the page is opened in a normal browser.
  // eslint-disable-next-line no-var
  var invokeCSharpAction: ((message: string) => void) | undefined;
}

export type Transport = (message: string) => void;

/** The host transport, or a no-op when the page runs outside the host (e.g. in tests). */
export function hostTransport(): Transport {
  const invoke = globalThis.invokeCSharpAction;
  return typeof invoke === 'function' ? (message) => invoke(message) : () => undefined;
}

/** Sends typed messages over a transport. */
export class Bridge {
  constructor(private readonly transport: Transport) {}

  send(type: string, payload: Record<string, unknown>): void {
    this.transport(createMessage(type, payload));
  }
}
