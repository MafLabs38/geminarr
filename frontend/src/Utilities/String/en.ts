/**
 * The texts of the interface, in English (the source language). Every text goes through translate(): none is
 * written in a component. Keys are English PascalCase, as in the *arr apps.
 */
const en = {
  About: 'About',
  ApiReachable: 'API reachable',
  AppName: 'Geminarr',
  BackToOverview: 'Back to the overview',
  Loading: 'Loading…',
  MainMenu: 'Main menu',
  Connection: 'Connection',
  Conversations: 'Conversations',
  Import: 'Import',
  Mnemarr: 'Mnemarr',
  NotFoundCode: '404',
  NotJson: 'The server did not answer with data.',
  NothingToCollectYet: 'Nothing to collect yet.',
  Overview: 'Overview',
  OverviewIntro: 'Geminarr brings the history of your Gemini conversations into Mnemarr. Nothing is connected yet.',
  PageNotFound: 'Page not found',
  PageNotFoundHelp: 'There is nothing at this address.',
  Runs: 'Runs',
  Runtime: 'Runtime',
  ServerAnswered: 'The server answered {status}.',
  SkipToContent: 'Skip to content',
  Soon: 'Soon',
  Started: 'Started',
  Status: 'Status',
  System: 'System',
  SystemStatusError: 'Could not read the system status: {message}',
  Unreachable: 'Unreachable',
  Uptime: 'Uptime',
  Version: 'Version',
} as const;

export type TranslationKey = keyof typeof en;

export default en;
