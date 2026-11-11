import { render, screen } from '@testing-library/react';

import { renderAt, status, stubApi } from '__tests__/render';
import App from 'App/App';
import AppRoutes from 'App/AppRoutes';
import { menuEntries } from 'App/navigation';
import Page from 'Components/Page/Page';

function shell(path: string) {
  return renderAt(
    <Page>
      <AppRoutes />
    </Page>,
    path,
  );
}

describe('the application', () => {
  it('shows the menu and says the API answers on the overview', async () => {
    stubApi({ '/system/status': status });

    shell('/');

    expect(await screen.findByText('API reachable')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main menu' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeInTheDocument();
  });

  it('says the API is unreachable when the call fails', async () => {
    stubApi({});

    shell('/');

    expect(await screen.findByText('Unreachable')).toBeInTheDocument();
  });

  it('shows the system status page with the version', async () => {
    stubApi({ '/system/status': status });

    shell('/system/status');

    expect(await screen.findByText('0.0.0-test')).toBeInTheDocument();
  });

  it('shows a page that says there is nothing at an unknown address', () => {
    stubApi({});

    shell('/nothing-here');

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('shows the whole shape of the product, with the pages that are not built marked "Soon" and not links', () => {
    stubApi({ '/system/status': status });

    shell('/');

    const menu = screen.getByRole('navigation', { name: 'Main menu' });
    expect(menu).toHaveTextContent('Connection');
    for (const entry of menuEntries.filter((candidate) => !candidate.available)) {
      expect(screen.queryByRole('link', { name: entry.label })).not.toBeInTheDocument();
    }
    expect(screen.getAllByText('Soon')).toHaveLength(menuEntries.filter((candidate) => !candidate.available).length);
    expect(screen.getByRole('link', { name: 'System' })).toHaveAttribute('href', '/system/status');
  });

  it('offers a way back to the overview from an unknown address', () => {
    stubApi({});

    shell('/nothing-here');

    expect(screen.getByRole('link', { name: 'Back to the overview' })).toHaveAttribute('href', '/');
  });
});

describe('App', () => {
  it('builds its own query client and reaches the API once for the overview', async () => {
    stubApi({ '/system/status': status });

    render(<App />);

    expect(await screen.findByText('API reachable')).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
