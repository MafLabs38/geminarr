import { screen } from '@testing-library/react';

import { renderAt, status, stubApi } from '__tests__/render';
import Status from 'System/Status/Status';

describe('Status', () => {
  it('shows the version, runtime, start and uptime of the running build', async () => {
    stubApi({ '/system/status': status });

    renderAt(<Status />);

    expect(await screen.findByText('0.0.0-test')).toBeInTheDocument();
    expect(screen.getByText('.NET 10.0.0')).toBeInTheDocument();
    expect(screen.getByText('Uptime')).toBeInTheDocument();
  });

  it('says it is loading until the server answers', () => {
    stubApi({ '/system/status': status });

    renderAt(<Status />);

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('shows the error and never stale values when the server cannot be read', async () => {
    stubApi({});

    renderAt(<Status />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not read the system status: The server answered 404.',
    );
    expect(screen.queryByText('Version')).not.toBeInTheDocument();
  });
});
