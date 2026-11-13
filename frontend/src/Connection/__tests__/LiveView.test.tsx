import { act, fireEvent, render, screen } from '@testing-library/react';

import LiveView, { LEAVE_KEYS } from 'Connection/LiveView';
import type { LiveViewTicket } from 'Connection/useConnection';

const ticket: LiveViewTicket = {
  id: 'abc',
  token: 'a token/with+chars',
  path: '/api/v1/connection/live-views/abc',
  expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
  width: 1280,
  height: 800,
};

class FakeSocket {
  static instances: FakeSocket[] = [];
  static readonly OPEN = 1;
  readonly url: string;
  readyState = 1;
  binaryType = '';
  sent: string[] = [];
  closed = false;
  onmessage: ((message: { data: Blob | string }) => void) | null = null;
  onclose: (() => void) | null = null;

  constructor(url: string) {
    this.url = url;
    FakeSocket.instances.push(this);
  }

  send(data: string) {
    this.sent.push(data);
  }

  close() {
    this.closed = true;
  }

  receive(data: Blob | string) {
    act(() => this.onmessage?.({ data }));
  }
}

const open = (): FakeSocket => {
  const [first] = FakeSocket.instances;
  if (!first) {
    throw new Error('No socket was opened.');
  }
  return first;
};

describe('LiveView', () => {
  beforeEach(() => {
    FakeSocket.instances = [];
    vi.stubGlobal('WebSocket', FakeSocket);
    HTMLCanvasElement.prototype.getContext = vi.fn(() => ({ drawImage: vi.fn() })) as never;
  });

  it('connects to its own origin with the token, and says in words what it is and what account it expects', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    expect(open().url).toBe(
      `ws://${globalThis.location.host}/api/v1/connection/live-views/abc?token=a%20token%2Fwith%2Bchars`,
    );
    expect(screen.getByText('You are controlling the browser of the Geminarr container.')).toBeInTheDocument();
    expect(screen.getByText('Sign in with the dedicated account, not your main one.')).toBeInTheDocument();
    expect(screen.getByText('Opening the browser…')).toBeInTheDocument();
    expect(
      screen.getByText(`While the screen has the keyboard, ${LEAVE_KEYS} gives it back to the page.`),
    ).toBeInTheDocument();
  });

  it('shows the time left once the browser is ready, at the size of its screen', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    open().receive('{"t":"ready","width":1000,"height":600}');

    expect(screen.getByText(/Time left: (9|10):/)).toBeInTheDocument();
    const screenElement = screen.getByLabelText('The browser of the Geminarr container');
    expect(screenElement).toHaveAttribute('width', '1000');
    expect(screenElement).toHaveAttribute('height', '600');
  });

  it('draws the pictures and drops them', async () => {
    const draw = vi.fn();
    HTMLCanvasElement.prototype.getContext = vi.fn(() => ({ drawImage: draw })) as never;
    const close = vi.fn();
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ close }));
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    open().receive(new Blob(['x'], { type: 'image/jpeg' }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(draw).toHaveBeenCalledOnce();
    expect(close).toHaveBeenCalledOnce();
  });

  it('skips a picture that cannot be decoded and one that comes when nothing can be drawn', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn().mockRejectedValue(new Error('bad')));
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    open().receive(new Blob(['x']));
    await act(async () => {
      await Promise.resolve();
    });
    vi.stubGlobal('createImageBitmap', undefined);
    open().receive(new Blob(['y']));

    expect(screen.getByLabelText('The browser of the Geminarr container')).toBeInTheDocument();
  });

  it('sends the mouse, with the coordinates of the screen and not of the page, and the wheel', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);
    const screenElement = screen.getByLabelText('The browser of the Geminarr container');
    screenElement.getBoundingClientRect = () => ({ left: 100, top: 50, width: 640, height: 400 }) as DOMRect;

    fireEvent.pointerDown(screenElement, { clientX: 420, clientY: 250, button: 0 });
    fireEvent.pointerUp(screenElement, { clientX: 420, clientY: 250, button: 2 });
    fireEvent.pointerMove(screenElement, { clientX: 100, clientY: 50 });
    fireEvent.wheel(screenElement, { clientX: 100, clientY: 50, deltaX: 1, deltaY: -30 });

    const sent = open().sent.map((message) => JSON.parse(message) as Record<string, unknown>);
    expect(sent[0]).toEqual({ t: 'down', button: 'left', x: 640, y: 400 });
    expect(sent[1]).toEqual({ t: 'up', button: 'right', x: 640, y: 400 });
    expect(sent[2]).toEqual({ t: 'move', x: 0, y: 0 });
    expect(sent[3]).toEqual({ t: 'wheel', dx: 1, dy: -30, x: 0, y: 0 });
  });

  it('sends the keys and a paste, and keeps nothing of what is typed', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);
    const screenElement = screen.getByLabelText('The browser of the Geminarr container');

    fireEvent.keyDown(screenElement, { key: 'a' });
    fireEvent.keyUp(screenElement, { key: 'a' });
    fireEvent.paste(screenElement, { clipboardData: { getData: () => 'secret pasted' } });
    fireEvent.paste(screenElement, { clipboardData: { getData: () => '' } });

    expect(open().sent.map((message) => JSON.parse(message) as Record<string, unknown>)).toEqual([
      { t: 'keydown', key: 'a' },
      { t: 'keyup', key: 'a' },
      { t: 'text', text: 'secret pasted' },
    ]);
    expect(document.body).not.toHaveTextContent('secret pasted');
  });

  it('gives the keyboard back to the page with the key combination, and sends nothing for it', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);
    const screenElement = screen.getByLabelText('The browser of the Geminarr container');
    screenElement.focus();

    fireEvent.keyDown(screenElement, { key: 'Escape', ctrlKey: true, altKey: true });

    expect(open().sent).toHaveLength(0);
    expect(document.activeElement).not.toBe(screenElement);
  });

  it('does not send when the socket is not open', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);
    open().readyState = 3;

    fireEvent.keyDown(screen.getByLabelText('The browser of the Geminarr container'), { key: 'a' });

    expect(open().sent).toHaveLength(0);
  });

  it('says it is signed in and ends once, as signed in', () => {
    const onEnded = vi.fn();
    render(<LiveView onEnded={onEnded} ticket={ticket} />);

    open().receive('{"t":"signed-in"}');
    act(() => open().onclose?.());

    expect(screen.getByText('Signed in. The live view closes.')).toBeInTheDocument();
    expect(onEnded).toHaveBeenCalledOnce();
    expect(onEnded).toHaveBeenCalledWith(true);
  });

  it('says it is closed when the connection ends without a session', () => {
    const onEnded = vi.fn();
    render(<LiveView onEnded={onEnded} ticket={ticket} />);

    act(() => open().onclose?.());

    expect(screen.getByText('The live view is closed.')).toBeInTheDocument();
    expect(onEnded).toHaveBeenCalledWith(false);
  });

  it('says it is signed in when the operator says so, only once the browser is ready, and says when it is not yet', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);
    const done = screen.getByRole('button', { name: 'I am signed in' });
    expect(done).toBeDisabled();

    open().receive('{"t":"ready","width":1280,"height":800}');
    fireEvent.click(done);
    open().receive('{"t":"not-yet"}');

    expect(open().sent).toContain('{"t":"done"}');
    expect(screen.getByRole('alert')).toHaveTextContent("The browser is not on the assistant's page yet");

    fireEvent.click(done);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says the browser could not be started', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    open().receive('{"t":"error","reason":"The browser could not be started."}');

    expect(screen.getByRole('alert')).toHaveTextContent('The browser could not be started');
  });

  it('ignores a message it does not know and one that is not JSON', () => {
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    open().receive('{"t":"something-new"}');
    open().receive('not json');

    expect(screen.getByText('Opening the browser…')).toBeInTheDocument();
  });

  it('closes on request, and closes the socket when it goes away', () => {
    const onEnded = vi.fn();
    const view = render(<LiveView onEnded={onEnded} ticket={ticket} />);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    view.unmount();

    expect(onEnded).toHaveBeenCalledWith(false);
    expect(open().closed).toBe(true);
  });

  it('counts down the time left', () => {
    vi.useFakeTimers();
    const short = { ...ticket, expiresAt: new Date(Date.now() + 65_000).toISOString() };
    render(<LiveView onEnded={vi.fn()} ticket={short} />);
    open().receive('{"t":"ready","width":1280,"height":800}');
    expect(screen.getByText('Time left: 1:05')).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(10_000);
    });

    expect(screen.getByText('Time left: 0:55')).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('uses a secure socket on a secure page', () => {
    const original = globalThis.location;
    Object.defineProperty(globalThis, 'location', {
      value: { protocol: 'https:', host: 'geminarr.homelab.example' },
      configurable: true,
    });
    render(<LiveView onEnded={vi.fn()} ticket={ticket} />);

    expect(open().url.startsWith('wss://geminarr.homelab.example/')).toBe(true);
    Object.defineProperty(globalThis, 'location', { value: original, configurable: true });
  });
});
