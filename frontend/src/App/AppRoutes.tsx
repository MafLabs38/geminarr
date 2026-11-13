import { Route, Routes } from 'react-router-dom';

import Mnemarr from 'Mnemarr/Mnemarr';
import Overview from 'Overview/Overview';
import Settings from 'Settings/Settings';
import Status from 'System/Status/Status';

import NotFound from './NotFound';

export default function AppRoutes() {
  return (
    <Routes>
      <Route element={<Overview />} path="/" />
      <Route element={<Mnemarr />} path="/mnemarr" />
      <Route element={<Settings />} path="/settings" />
      <Route element={<Status />} path="/system/status" />
      <Route element={<NotFound />} path="*" />
    </Routes>
  );
}
