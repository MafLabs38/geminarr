import { Route, Routes } from 'react-router-dom';

import Connection from 'Connection/Connection';
import Conversations from 'Conversations/Conversations';
import Mnemarr from 'Mnemarr/Mnemarr';
import Overview from 'Overview/Overview';
import Runs from 'Runs/Runs';
import Settings from 'Settings/Settings';
import Status from 'System/Status/Status';

import NotFound from './NotFound';

export default function AppRoutes() {
  return (
    <Routes>
      <Route element={<Overview />} path="/" />
      <Route element={<Connection />} path="/connection" />
      <Route element={<Conversations />} path="/conversations" />
      <Route element={<Runs />} path="/runs" />
      <Route element={<Mnemarr />} path="/mnemarr" />
      <Route element={<Settings />} path="/settings" />
      <Route element={<Status />} path="/system/status" />
      <Route element={<NotFound />} path="*" />
    </Routes>
  );
}
