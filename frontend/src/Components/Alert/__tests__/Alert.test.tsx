import { render, screen } from '@testing-library/react';

import Alert from 'Components/Alert/Alert';

describe('Alert', () => {
  it('announces its text to screen readers as an alert', () => {
    render(<Alert>Something went wrong.</Alert>);

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong.');
  });
});
