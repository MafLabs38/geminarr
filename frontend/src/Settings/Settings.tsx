import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import LoadingIndicator from 'Components/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { useIdentity } from 'Identity/identity';
import translate from 'Utilities/String/translate';

import { useSettings } from './useSettings';

/** The settings the container runs with, read only: they are changed in its manifest. A secret is shown as set or not set. */
export default function Settings() {
  const { data, error, isPending } = useSettings();
  const identity = useIdentity();

  return (
    <PageContent title={translate('SettingsTitle')}>
      <PageContentBody>
        <PageHeading scope={translate('AppName')} title={translate('SettingsTitle')} />
        <Card title={translate('SettingsTitle')}>
          <p>{translate('SettingsIntro')}</p>
          {isPending && <LoadingIndicator />}
          {identity.data?.logoProblem && (
            <Alert>{translate('SettingsLogoNotUsed', { reason: identity.data.logoProblem })}</Alert>
          )}
          {error && <Alert>{translate('SettingsLoadError', { message: error.message })}</Alert>}
          {data && !error && (
            <DescriptionList
              items={data.settings.map((setting) => ({
                title: setting.name,
                data: setting.secret ? `${setting.value} (${translate('SettingsSecret')})` : setting.value,
                mono: true,
              }))}
            />
          )}
        </Card>
      </PageContentBody>
    </PageContent>
  );
}
