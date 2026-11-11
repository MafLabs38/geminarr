import { screen } from '@testing-library/react';

import { renderAt, status, stubApi } from '__tests__/render';
import AppRoutes from 'App/AppRoutes';
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
});
