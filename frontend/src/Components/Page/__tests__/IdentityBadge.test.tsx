import { fireEvent, screen } from '@testing-library/react';

import { renderAt, stubApi } from '__tests__/render';
import IdentityBadge from 'Components/Page/IdentityBadge';
import type { Identity } from 'Identity/identity';

const product: Identity = {
  name: 'Geminarr',
  logoUrl: null,
  version: '0.1.0',
  logoProblem: null,
  colors: { name: 'accent', version: 'accent' },
};

const lab: Identity = {
  name: 'Example Lab',
  logoUrl: '/api/v1/identity/logo?v=abc',
  version: '0.6.0',
  logoProblem: null,
  colors: { name: '#3ee0c5', version: 'inkMuted' },
};

const statusOf = (version: string) => ({
  version,
  runtimeVersion: '10',
  startedAt: '2026-11-13T09:00:00Z',
  uptimeSeconds: 5,
});

describe('IdentityBadge', () => {
  it("shows the product's own name, drawing and version when nothing else is set", async () => {
    stubApi({ '/identity': product, '/system/status': statusOf('1.2.3') });
    const { container } = renderAt(<IdentityBadge />);

    expect(await screen.findByText('Geminarr')).toHaveStyle({ color: 'var(--accent)' });
    expect(await screen.findByText('v1.2.3')).toHaveStyle({ color: 'var(--accent)' });
    expect(container.querySelector('svg')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('shows the name, the logo and the version of the lab, in the colors that are set', async () => {
    stubApi({ '/identity': lab, '/system/status': statusOf('0.6.1') });
    renderAt(<IdentityBadge />);

    const name = await screen.findByText('Example Lab');
    expect(name).toHaveStyle({ color: '#3ee0c5' });
    expect(screen.getByRole('img', { name: 'Example Lab' })).toHaveAttribute('src', '/api/v1/identity/logo?v=abc');
    expect(await screen.findByText('v0.6.1')).toHaveStyle({ color: 'var(--inkMuted)' });
    expect(name.closest('div')).toHaveAttribute('title', 'Example Lab');
  });

  it('keeps the version of the identity when the status cannot be read', async () => {
    stubApi({ '/identity': lab });
    renderAt(<IdentityBadge />);

    expect(await screen.findByText('v0.6.0')).toBeInTheDocument();
  });

  it('shows no version when the server has none to say', async () => {
    stubApi({ '/identity': { ...lab, version: '' }, '/system/status': statusOf('') });
    renderAt(<IdentityBadge />);

    await screen.findByText('Example Lab');

    expect(screen.queryByText(/^v/)).not.toBeInTheDocument();
  });

  it('falls back to the drawing of the product when the logo cannot be loaded, and says nothing about it', async () => {
    stubApi({ '/identity': lab, '/system/status': statusOf('0.6.1') });
    const { container } = renderAt(<IdentityBadge />);

    fireEvent.error(await screen.findByRole('img', { name: 'Example Lab' }));

    expect(screen.queryByRole('img', { name: 'Example Lab' })).not.toBeInTheDocument();
    expect(container.querySelector('svg')).toBeInTheDocument();
    expect(screen.getByText('Example Lab')).toBeInTheDocument();
  });

  it('says the name of the product when the identity cannot be read', async () => {
    stubApi({});
    renderAt(<IdentityBadge />);

    expect(await screen.findByText('Geminarr')).toBeInTheDocument();
  });

  it('draws nothing while the identity is being read', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<never>(() => undefined)),
    );
    const { container } = renderAt(<IdentityBadge />);

    expect(container).toBeEmptyDOMElement();
  });
});
