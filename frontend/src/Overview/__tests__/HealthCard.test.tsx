import { screen } from '@testing-library/react';

import { renderAt, stubApi } from '__tests__/render';
import HealthCard from 'Overview/HealthCard';

const check = (name: string, status: string, reason: string | null = null, description: string | null = null) => ({
  name,
  status,
  reason,
  description,
});

describe('HealthCard', () => {
  it('shows each check with its state in words', async () => {
    stubApi({
      '/health': { status: 'Healthy', checks: [check('data-folder', 'Healthy'), check('operator', 'Healthy')] },
    });

    renderAt(<HealthCard />);

    expect(await screen.findByText('Data folder')).toBeInTheDocument();
    expect(screen.getByText('Sign-in')).toBeInTheDocument();
    expect(screen.getAllByText('Healthy')).toHaveLength(2);
  });

  it('says what is wrong with the sentence of the reason code, not the words of the server', async () => {
    stubApi({
      '/health': {
        status: 'Degraded',
        checks: [check('operator', 'Degraded', 'operator-not-configured', 'some english')],
      },
    });

    renderAt(<HealthCard />);

    expect(await screen.findByText('Needs attention')).toBeInTheDocument();
    expect(screen.getByText(/No operator is configured, so nobody can sign in/)).toBeInTheDocument();
    expect(screen.queryByText('some english')).not.toBeInTheDocument();
  });

  it.each([
    ['data-folder-missing', /its volume is not mounted/],
    ['data-folder-read-only', /cannot be written/],
    ['operator-password-unreadable', /cannot be read/],
    ['operator-password-weak', /too short/],
  ])('has a sentence for the code %s', async (reason, sentence) => {
    stubApi({ '/health': { status: 'Unhealthy', checks: [check('data-folder', 'Unhealthy', reason)] } });

    renderAt(<HealthCard />);

    expect(await screen.findByText(sentence)).toBeInTheDocument();
    expect(screen.getByText('Not working')).toBeInTheDocument();
  });

  it('falls back to the words of the server for a code it does not know, and to the code when there are none', async () => {
    stubApi({
      '/health': {
        status: 'Degraded',
        checks: [
          check('later', 'Degraded', 'a-new-code', 'The new thing is wrong.'),
          check('other', 'Degraded', 'another-code'),
        ],
      },
    });

    renderAt(<HealthCard />);

    expect(await screen.findByText('The new thing is wrong.')).toBeInTheDocument();
    expect(screen.getByText('another-code')).toBeInTheDocument();
    expect(screen.getByText('later')).toBeInTheDocument();
  });

  it('says it could not read the health, and shows no check, when the call fails', async () => {
    stubApi({});

    renderAt(<HealthCard />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not read the health');
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });

  it('says it is loading until the server answers', async () => {
    stubApi({ '/health': { status: 'Healthy', checks: [] } });

    renderAt(<HealthCard />);

    expect(screen.getByText('Loading…')).toBeInTheDocument();
    await screen.findByRole('list');
  });
});
