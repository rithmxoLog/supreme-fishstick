import React from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthContext';
import Navbar from './components/Navbar';
import PrivateRoute from './components/PrivateRoute';
import ExplorePage from './pages/ExplorePage';
import LoginPage from './pages/LoginPage';
import SettingsPage from './pages/SettingsPage';
import RepoView from './components/RepoView';
import ActivityLog from './components/ActivityLog';
import './App.css';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Navbar />
        <div className="app-body">
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<PrivateRoute><ExplorePage /></PrivateRoute>} />
            <Route path="/repos/:repoName/*" element={<PrivateRoute><RepoView /></PrivateRoute>} />
            <Route path="/activity" element={<PrivateRoute><ActivityLog /></PrivateRoute>} />
            <Route path="/settings" element={<PrivateRoute><SettingsPage /></PrivateRoute>} />
          </Routes>
        </div>
      </BrowserRouter>
    </AuthProvider>
  );
}
