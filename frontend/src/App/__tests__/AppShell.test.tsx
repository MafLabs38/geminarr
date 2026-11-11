import { render, screen } from '@testing-library/react';

import { status, stubApi } from '__tests__/render';
import App from 'App/App';

describe('App', () => {
  it('shows the page shell with the overview and reaches the API once', async () => {
    stubApi({ '/system/status': status });

    render(<App />);

    expect(await screen.findByText('API reachable')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'System' })).toHaveAttribute('href', '/system/status');
  });
});
