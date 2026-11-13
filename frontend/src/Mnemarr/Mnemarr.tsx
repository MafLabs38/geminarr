import { type FormEvent, useEffect, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import Button from 'Components/Form/Button';
import TextField from 'Components/Form/TextField';
import LoadingIndicator from 'Components/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import { kinds } from 'Helpers/Props/kinds';
import { reasonKeyOf } from 'Overview/healthTexts';
import translate from 'Utilities/String/translate';

import styles from './Mnemarr.module.css';
import {
  useCancelPairing,
  useCheckPairing,
  useMnemarr,
  usePairing,
  useSetAddress,
  useStartPairing,
  useUnlink,
} from './useMnemarr';

/** How often a pairing that waits is looked at: Mnemarr says to ask about every three seconds. */
export const PAIRING_CHECK_MS = 3_000;

const hostOf = (address: string): string => {
  try {
    return new URL(address).host;
  } catch {
    return address;
  }
};

function PairingBlock({ hasAddress }: Readonly<{ hasAddress: boolean }>) {
  const pairing = usePairing();
  const start = useStartPairing();
  const check = useCheckPairing();
  const cancel = useCancelPairing();
  const pending = pairing.data?.state === 'pending';
  const { mutate: checkNow } = check;

  useEffect(() => {
    if (!pending) {
      return undefined;
    }
    const timer = setInterval(() => checkNow(), PAIRING_CHECK_MS);
    return () => clearInterval(timer);
  }, [pending, checkNow]);

  const failure = start.error ?? check.error;
  const ended = pairing.data?.state;
  return (
    <div className={styles.block}>
      {!pending && (
        <Button disabled={!hasAddress || start.isPending} onClick={() => start.mutate()}>
          {translate('MnemarrStartPairing')}
        </Button>
      )}
      {pending && pairing.data && (
        <>
          <p className={styles.code}>{translate('MnemarrApprove', { code: pairing.data.userCode ?? '' })}</p>
          {pairing.data.verificationUrl && (
            <a href={pairing.data.verificationUrl} rel="noopener noreferrer" target="_blank">
              {translate('MnemarrOpenApproval', { host: hostOf(pairing.data.verificationUrl) })}
            </a>
          )}
          <Button variant="quiet" onClick={() => cancel.mutate()}>
            {translate('CancelPairing')}
          </Button>
        </>
      )}
      {ended === 'expired' && <Alert>{translate('MnemarrPairingExpired')}</Alert>}
      {ended === 'rejected' && <Alert>{translate('MnemarrPairingRejected')}</Alert>}
      {failure && <Alert>{translate('MnemarrPairingFailed', { message: failure.message })}</Alert>}
    </div>
  );
}

function AddressForm({ current }: Readonly<{ current: string | null }>) {
  const [address, setAddress] = useState(current ?? '');
  const save = useSetAddress();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate(address.trim());
  };

  return (
    <form className={styles.form} onSubmit={submit}>
      <TextField
        autoComplete="off"
        label={translate('AddressOfMnemarr')}
        onChange={(event) => setAddress(event.target.value)}
        placeholder="https://mnemarr.homelab.example"
        spellCheck={false}
        type="url"
        value={address}
      />
      <Button disabled={save.isPending || address.trim().length === 0} type="submit">
        {translate('MnemarrSaveAddress')}
      </Button>
      {save.error && <Alert>{save.error.message}</Alert>}
    </form>
  );
}

function Unlink() {
  const [sure, setSure] = useState(false);
  const unlink = useUnlink();

  if (!sure) {
    return (
      <Button variant="danger" onClick={() => setSure(true)}>
        {translate('MnemarrUnlink')}
      </Button>
    );
  }
  return (
    <div className={styles.block}>
      <p>{translate('MnemarrUnlinkConfirm')}</p>
      <Button
        variant="danger"
        disabled={unlink.isPending}
        onClick={() => unlink.mutate(undefined, { onSettled: () => setSure(false) })}
      >
        {translate('MnemarrUnlinkNow')}
      </Button>
      <Button variant="quiet" onClick={() => setSure(false)}>
        {translate('CancelPairing')}
      </Button>
    </div>
  );
}

/** Where the conversations go: the Mnemarr, its address, the machine's name, how it was linked, and the way to unlink (SPEC §5.1). The key is never shown, nor held here. */
export default function Mnemarr() {
  const { data, error, isPending } = useMnemarr();
  const reason = reasonKeyOf(data?.problem ?? null);

  return (
    <PageContent title={translate('MnemarrTitle')}>
      <PageContentBody>
        <PageHeading scope={translate('AppName')} title={translate('MnemarrTitle')} />
        <Card title={translate('MnemarrTitle')}>
          <p>{translate('MnemarrIntro')}</p>
          {isPending && <LoadingIndicator />}
          {error && <Alert>{translate('MnemarrCouldNotRead', { message: error.message })}</Alert>}
          {data && !error && (
            <>
              <DescriptionList
                items={[
                  { title: translate('AddressOfMnemarr'), data: data.url ?? translate('NotSet'), mono: true },
                  { title: translate('MachineName'), data: data.machine, mono: true },
                  {
                    title: translate('Status'),
                    data: data.linked ? (
                      <StatusLabel kind={data.problem ? kinds.WARNING : kinds.SUCCESS}>
                        {translate('LinkedTo', { host: hostOf(data.url ?? '') })}
                      </StatusLabel>
                    ) : (
                      <StatusLabel kind={kinds.INFO}>{translate('MnemarrNotLinked')}</StatusLabel>
                    ),
                  },
                  {
                    title: translate('LastHeartbeat'),
                    data: data.lastHeartbeatAt ? new Date(data.lastHeartbeatAt).toLocaleString() : '—',
                    mono: true,
                  },
                  { title: translate('PendingLines'), data: String(data.pendingLines), mono: true },
                ]}
              />
              {data.problem && <p className={styles.problem}>{reason ? translate(reason) : data.problem}</p>}
              {data.addressFromSettings && <p>{translate('MnemarrFromSettings')}</p>}
            </>
          )}
        </Card>
        {data && !error && !data.linked && (
          <Card title={translate('MnemarrStartPairing')}>
            {!data.addressFromSettings && <AddressForm current={data.url} key={data.url ?? ''} />}
            <PairingBlock hasAddress={data.url !== null} />
          </Card>
        )}
        {data && !error && data.linked && (
          <Card title={translate('MnemarrUnlinkTitle')}>
            <Unlink />
          </Card>
        )}
      </PageContentBody>
    </PageContent>
  );
}
