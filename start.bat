@echo off
echo Starting GitXO...
echo.

:: Check dotnet is installed
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
  echo ERROR: .NET SDK is not installed or not in PATH.
  echo Please install .NET 8 SDK from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

:: Check node is installed (for frontend)
where node >nul 2>&1
if %errorlevel% neq 0 (
  echo ERROR: Node.js is not installed or not in PATH.
  echo Please install Node.js from https://nodejs.org
  pause
  exit /b 1
)

:: Restore C# backend dependencies if needed
if not exist "backend\bin" (
  echo Restoring C# backend dependencies...
  cd backend
  dotnet restore
  cd ..
)

:: Install frontend deps if needed
if not exist "frontend\node_modules" (
  echo Installing frontend dependencies...
  cd frontend
  npm install
  cd ..
)

:: Start C# backend in a new window
echo Starting C# backend on http://localhost:3001
start "GitXO Backend" cmd /k "cd /d %~dp0backend && dotnet run"

:: Give backend a moment to start
timeout /t 3 /nobreak >nul

:: Start frontend in a new window
echo Starting frontend on http://localhost:3000
start "GitXO Frontend" cmd /k "cd /d %~dp0frontend && npm start"

echo.
echo GitXO is starting...
echo   Frontend: http://localhost:3000
echo   Backend:  http://localhost:3001
echo.
echo Close the backend/frontend windows to stop the servers.
pause
