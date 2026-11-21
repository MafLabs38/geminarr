import { render, screen, within } from '@testing-library/react';

import { renderAt, session, status, stubApi } from '__tests__/render';
import App from 'App/App';
import AppRoutes from 'App/AppRoutes';
import { menuEntries } from 'App/navigation';
import Page from 'Components/Page/Page';

// The channel that announces changes is tested on its own: here a page is shown, not a WebSocket opened.
vi.mock('Live/LiveUpdates', () => ({ default: () => null }));

const health = {
  status: 'Healthy',
  checks: [{ name: 'data-folder', status: 'Healthy', reason: null, description: null }],
};

function shell(path: string) {
  return renderAt(
    <Page>
      <AppRoutes />
    </Page>,
    path,
  );
}

describe('the application', () => {
  it('shows the menu, who is signed in and says the API answers on the overview', async () => {
    stubApi({ '/system/status': status, '/health': health });

    shell('/');

    expect(await screen.findByText('API reachable')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main menu' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeInTheDocument();
    expect(screen.getByText(session.name)).toBeInTheDocument();
  });

  it('says the API is unreachable when the call fails', async () => {
    stubApi({ '/health': health });

    shell('/');

    expect(await screen.findByText('Unreachable')).toBeInTheDocument();
  });

  it('shows the system status page with the version', async () => {
    stubApi({ '/system/status': status });

    shell('/system/status');

    expect(await screen.findByText('0.0.0-test')).toBeInTheDocument();
  });

  it('shows the settings page', async () => {
    stubApi({ '/settings': { settings: [{ name: 'GEMINARR__DATAFOLDER', value: '/config', secret: false }] } });

    shell('/settings');

    expect(await screen.findByText('GEMINARR__DATAFOLDER')).toBeInTheDocument();
  });

  it('shows a page that says there is nothing at an unknown address', () => {
    stubApi({});

    shell('/nothing-here');

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('shows a menu of pages that are all built: every entry is a link, and none is marked "Soon"', () => {
    stubApi({ '/system/status': status, '/health': health });

    shell('/');

    const menu = within(screen.getByRole('navigation', { name: 'Main menu' }));
    for (const entry of menuEntries) {
      expect(menu.getByRole('link', { name: entry.label })).toHaveAttribute('href', entry.to);
    }
    expect(screen.queryByText('Soon')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Import' })).not.toBeInTheDocument();
  });

  it('offers a way back to the overview from an unknown address', () => {
    stubApi({});

    shell('/nothing-here');

    expect(screen.getByRole('link', { name: 'Back to the overview' })).toHaveAttribute('href', '/');
  });
});

describe('App', () => {
  it('asks who is signed in first and shows the page once somebody is', async () => {
    stubApi({ '/auth/session': session, '/system/status': status, '/health': health });

    render(<App />);

    expect(await screen.findByText('API reachable')).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/session', expect.anything());
  });

  it('shows the sign-in page, and nothing of the application, when nobody is signed in', async () => {
    const { answer } = await import('__tests__/render');
    stubApi({ '/auth/session': answer(401) });

    render(<App />);

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Main menu' })).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
