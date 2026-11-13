import {
  type ClipboardEvent,
  type KeyboardEvent,
  type PointerEvent,
  useEffect,
  useRef,
  useState,
  type WheelEvent,
} from 'react';

import Alert from 'Components/Alert/Alert';
import Button from 'Components/Form/Button';
import translate from 'Utilities/String/translate';

import styles from './LiveView.module.css';
import type { LiveViewTicket } from './useConnection';

/** How long the focus stays on the screen before a key combination gives it back to the page. */
export const LEAVE_KEYS = 'Ctrl+Alt+Escape';

/** At most this many pointer moves a second go to the browser. */
const MOVE_INTERVAL_MS = 50;

type Phase = 'connecting' | 'ready' | 'signed-in' | 'closed' | 'error';

/** What the operator says when they see the assistant's page and are signed in. */
const DONE_MESSAGE = JSON.stringify({ t: 'done' });

interface LiveViewProps {
  ticket: LiveViewTicket;
  /** Called once when the live view ends, and why. */
  onEnded: (signedIn: boolean) => void;
}

/** The address of the WebSocket, on this very origin (the Content-Security-Policy allows nothing else). */
function socketAddress(path: string, token: string): string {
  const scheme = globalThis.location.protocol === 'https:' ? 'wss:' : 'ws:';
  return `${scheme}//${globalThis.location.host}${path}?token=${encodeURIComponent(token)}`;
}

function secondsLeft(expiresAt: string): number {
  return Math.max(0, Math.ceil((new Date(expiresAt).getTime() - Date.now()) / 1000));
}

/**
 * The browser of the container, as a canvas: the pictures come over a WebSocket, and the keyboard, the mouse and a paste go back as small messages that the server checks again. Nothing that is shown or typed
 * is kept here: the pictures are drawn and dropped, and no text is put in the page. It says in words what it is, and gives the keyboard back with a key combination.
 */
export default function LiveView({ ticket, onEnded }: Readonly<LiveViewProps>) {
  const view = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const socket = useRef<WebSocket | null>(null);
  const lastMove = useRef(0);
  const ended = useRef(false);
  const [phase, setPhase] = useState<Phase>('connecting');
  const [notYet, setNotYet] = useState(false);
  const [left, setLeft] = useState(() => secondsLeft(ticket.expiresAt));
  const [size, setSize] = useState({ width: ticket.width, height: ticket.height });

  useEffect(() => {
    const ws = new WebSocket(socketAddress(ticket.path, ticket.token));
    ws.binaryType = 'blob';
    socket.current = ws;
    const finish = (signedIn: boolean) => {
      if (!ended.current) {
        ended.current = true;
        onEnded(signedIn);
      }
    };
    ws.onmessage = (message: MessageEvent<Blob | string>) => {
      if (typeof message.data !== 'string') {
        void draw(canvas.current, message.data);
        return;
      }
      const control = parse(message.data);
      if (control?.t === 'ready') {
        setSize({ width: Number(control.width) || ticket.width, height: Number(control.height) || ticket.height });
        setPhase('ready');
      } else if (control?.t === 'signed-in') {
        setPhase('signed-in');
        finish(true);
      } else if (control?.t === 'not-yet') {
        setNotYet(true);
      } else if (control?.t === 'error') {
        setPhase('error');
      }
    };
    ws.onclose = () => {
      setPhase((current) => (current === 'signed-in' || current === 'error' ? current : 'closed'));
      finish(false);
    };
    return () => {
      ws.onclose = null;
      ws.close();
      finish(false);
    };
    // The live view is opened once for its ticket.
  }, [ticket, onEnded]);

  useEffect(() => {
    // The screen is the point of the page when it opens: it is brought into view, whole as far as it fits.
    view.current?.scrollIntoView?.({ block: 'start' });
  }, []);

  useEffect(() => {
    const timer = setInterval(() => setLeft(secondsLeft(ticket.expiresAt)), 1_000);
    return () => clearInterval(timer);
  }, [ticket.expiresAt]);

  const send = (message: object) => {
    if (socket.current?.readyState === WebSocket.OPEN) {
      socket.current.send(JSON.stringify(message));
    }
  };

  const point = (event: PointerEvent<HTMLCanvasElement> | WheelEvent<HTMLCanvasElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return {
      x: Math.round(((event.clientX - rect.left) * size.width) / (rect.width || size.width)),
      y: Math.round(((event.clientY - rect.top) * size.height) / (rect.height || size.height)),
    };
  };

  const buttonOf = (event: PointerEvent<HTMLCanvasElement>) =>
    (['left', 'middle', 'right'] as const)[event.button] ?? 'left';

  const onMove = (event: PointerEvent<HTMLCanvasElement>) => {
    const now = Date.now();
    if (now - lastMove.current >= MOVE_INTERVAL_MS) {
      lastMove.current = now;
      send({ t: 'move', ...point(event) });
    }
  };

  const onKey = (event: KeyboardEvent<HTMLCanvasElement>, type: 'keydown' | 'keyup') => {
    if (event.ctrlKey && event.altKey && event.key === 'Escape') {
      event.currentTarget.blur();
      return;
    }
    event.preventDefault();
    send({ t: type, key: event.key });
  };

  const onPaste = (event: ClipboardEvent<HTMLCanvasElement>) => {
    event.preventDefault();
    const text = event.clipboardData.getData('text');
    if (text) {
      send({ t: 'text', text });
    }
  };

  return (
    <div className={styles.view} ref={view}>
      <p className={styles.notice}>{translate('LiveViewNotice')}</p>
      <p className={styles.notice}>{translate('LiveViewAccount')}</p>
      <canvas
        aria-label={translate('LiveViewScreen')}
        className={styles.screen}
        height={size.height}
        onContextMenu={(event) => event.preventDefault()}
        onKeyDown={(event) => onKey(event, 'keydown')}
        onKeyUp={(event) => onKey(event, 'keyup')}
        onPaste={onPaste}
        onPointerDown={(event) => {
          // The place of the click is read before the focus moves, and the focus does not scroll the page under the pointer.
          const target = { t: 'down', button: buttonOf(event), ...point(event) };
          event.currentTarget.focus({ preventScroll: true });
          send(target);
        }}
        onPointerMove={onMove}
        onPointerUp={(event) => send({ t: 'up', button: buttonOf(event), ...point(event) })}
        onWheel={(event) => send({ t: 'wheel', dx: event.deltaX, dy: event.deltaY, ...point(event) })}
        ref={canvas}
        tabIndex={0}
        width={size.width}
      />
      <p className={styles.status} role="status">
        {phase === 'connecting' && translate('LiveViewConnecting')}
        {phase === 'ready' &&
          translate('LiveViewTimeLeft', {
            minutes: Math.floor(left / 60),
            seconds: String(left % 60).padStart(2, '0'),
          })}
        {phase === 'signed-in' && translate('LiveViewSignedIn')}
        {phase === 'closed' && translate('LiveViewClosed')}
      </p>
      {phase === 'error' && <Alert>{translate('LiveViewError')}</Alert>}
      {notYet && <Alert>{translate('LiveViewNotYet')}</Alert>}
      <p className={styles.help}>{translate('LiveViewLeave', { keys: LEAVE_KEYS })}</p>
      <div className={styles.buttons}>
        <Button
          disabled={phase !== 'ready'}
          onClick={() => {
            setNotYet(false);
            socket.current?.send(DONE_MESSAGE);
          }}
        >
          {translate('LiveViewDone')}
        </Button>
        <Button variant="quiet" onClick={() => onEnded(false)}>
          {translate('LiveViewClose')}
        </Button>
      </div>
    </div>
  );
}

function parse(text: string): { t?: string; width?: number; height?: number } | null {
  try {
    return JSON.parse(text) as { t?: string; width?: number; height?: number };
  } catch {
    return null;
  }
}

/** Draws a picture of the screen and drops it. A browser that cannot decode it shows the last picture that it could. */
async function draw(target: HTMLCanvasElement | null, picture: Blob): Promise<void> {
  const context = target?.getContext('2d');
  if (!target || !context || typeof createImageBitmap !== 'function') {
    return;
  }
  try {
    const bitmap = await createImageBitmap(picture);
    context.drawImage(bitmap, 0, 0, target.width, target.height);
    bitmap.close();
  } catch {
    // A picture that cannot be decoded is skipped; the next one replaces it.
  }
}
