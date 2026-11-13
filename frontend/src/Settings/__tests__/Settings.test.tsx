import { screen } from '@testing-library/react';

import { renderAt, stubApi } from '__tests__/render';
import Settings from 'Settings/Settings';

describe('Settings', () => {
  it('lists the settings by their variable, and marks a secret as such without showing more than set or not set', async () => {
    stubApi({
      '/settings': {
        settings: [
          { name: 'GEMINARR__DATAFOLDER', value: '/config', secret: false },
          { name: 'GEMINARR__AUTH__ADMINPASSWORDFILE', value: 'set', secret: true },
        ],
      },
    });

    renderAt(<Settings />);

    expect(await screen.findByText('GEMINARR__DATAFOLDER')).toBeInTheDocument();
    expect(screen.getByText('/config')).toBeInTheDocument();
    expect(screen.getByText('set (secret)')).toBeInTheDocument();
  });

  it('says they are changed in the manifest and not here', async () => {
    stubApi({ '/settings': { settings: [] } });

    renderAt(<Settings />);

    expect(await screen.findByText(/changed in its manifest, not here/)).toBeInTheDocument();
  });

  it('says it could not read them, and shows none, when the call fails', async () => {
    stubApi({});

    renderAt(<Settings />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not read the settings');
  });
});
