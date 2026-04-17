import React, { useState, useEffect } from 'react';

const AUTH_API_URL = process.env.REACT_APP_AUTH_API_URL || 'http://localhost:5000';
const REPORTS_API_URL = process.env.REACT_APP_REPORTS_API_URL || 'http://localhost:8000';

const ReportPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [user, setUser] = useState<{ username: string; roles: string[] } | null>(null);
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);

  useEffect(() => {
    checkAuth();
  }, []);

  const checkAuth = async () => {
    try {
      const response = await fetch(`${AUTH_API_URL}/api/auth/me`, {
        credentials: 'include'  // ← Отправляем cookie
      });

      if (response.ok) {
        const userData = await response.json();
        setUser(userData);
        setIsAuthenticated(true);
      } else {
        setIsAuthenticated(false);
      }
    } catch {
      setIsAuthenticated(false);
    }
  };

  const login = async (username: string, password: string) => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(`${AUTH_API_URL}/api/auth/login`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        credentials: 'include',  // ← Важно: принимаем cookie
        body: JSON.stringify({ username, password })
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || 'Login failed');
      }

      await checkAuth();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  const logout = async () => {
    try {
      await fetch(`${AUTH_API_URL}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include'
      });
      setIsAuthenticated(false);
      setUser(null);
    } catch (err) {
      console.error('Logout error:', err);
    }
  };

  const downloadReport = async () => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(`${AUTH_API_URL}/api/reports`, {
        credentials: 'include'  // ← Cookie автоматически отправляется
      });

      if (response.status === 401) {
        setIsAuthenticated(false);
        return;
      }

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const data = await response.json();
      
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `report-${new Date().toISOString().split('T')[0]}.json`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);

    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  const [loginForm, setLoginForm] = useState({ username: '', password: '' });
  const [isLoginMode, setIsLoginMode] = useState(true);

  if (isAuthenticated === null) {
    return <div className="flex items-center justify-center min-h-screen">Loading...</div>;
  }

  if (!isAuthenticated) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
        <div className="p-8 bg-white rounded-lg shadow-md w-96">
          <h1 className="text-2xl font-bold mb-6 text-center">BionicPRO Auth</h1>
          
          <div className="mb-4">
            <input
              type="text"
              placeholder="Username"
              className="w-full px-3 py-2 border rounded"
              value={loginForm.username}
              onChange={(e) => setLoginForm({ ...loginForm, username: e.target.value })}
            />
          </div>
          
          <div className="mb-4">
            <input
              type="password"
              placeholder="Password"
              className="w-full px-3 py-2 border rounded"
              value={loginForm.password}
              onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })}
            />
          </div>
          
          <button
            onClick={() => login(loginForm.username, loginForm.password)}
            disabled={loading}
            className="w-full px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600"
          >
            {loading ? 'Logging in...' : 'Login'}
          </button>

          {error && (
            <div className="mt-4 p-3 bg-red-100 text-red-700 rounded text-sm">
              {error}
            </div>
          )}
        </div>
      </div>
    );
  }

  return (
  <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
    <div className="p-8 bg-white rounded-lg shadow-md w-full max-w-md">
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold">Usage Reports</h1>
        <button onClick={logout} className="text-sm text-gray-500 hover:text-gray-700">
          Logout
        </button>
      </div>

      
      {user && (
        <div className="mb-4 p-3 bg-green-50 rounded text-sm">
          Logged in as: <strong>{user.username}</strong>
          {user.roles?.includes('administrator') && (
            <span className="ml-2 px-2 py-0.5 bg-purple-100 text-purple-700 rounded text-xs">
              Admin
            </span>
          )}
        </div>
      )}
      
      <button
        onClick={downloadReport}
        disabled={loading}
        className="w-full px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600"
      >
        {loading ? 'Generating Report...' : 'Download Report'}
      </button>

      {error && (
        <div className="mt-4 p-3 bg-red-100 text-red-700 rounded text-sm">
          {error}
        </div>
      )}
    </div>
  </div>
);
};

export default ReportPage;