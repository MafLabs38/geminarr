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

  it('says why the picture of the menu is not used, and says nothing when it is', async () => {
    const identity = {
      name: 'Geminarr',
      logoUrl: null,
      version: '0.1.0',
      logoProblem: null,
      colors: { name: 'accent', version: 'accent' },
    };
    stubApi({
      '/settings': { settings: [] },
      '/identity': { ...identity, logoProblem: 'The logo file of the settings is not there.' },
    });
    const { unmount } = renderAt(<Settings />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The picture of the menu is not used. The logo file of the settings is not there.',
    );
    unmount();

    stubApi({ '/settings': { settings: [] }, '/identity': identity });
    renderAt(<Settings />);
    await screen.findByText(/changed in its manifest, not here/);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
