#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Install frontend deps
if [ ! -d "$SCRIPT_DIR/frontend/node_modules" ]; then
  echo "Installing frontend dependencies..."
  (cd "$SCRIPT_DIR/frontend" && npm install)
fi

# Restore C# backend deps
if [ ! -d "$SCRIPT_DIR/backend/bin" ]; then
  echo "Restoring C# backend dependencies..."
  (cd "$SCRIPT_DIR/backend" && dotnet restore)
fi

echo "Starting GitXO C# backend on http://localhost:3001"
(cd "$SCRIPT_DIR/backend" && dotnet run) &
BACKEND_PID=$!

sleep 2

echo "Starting GitXO frontend on http://localhost:3000"
(cd "$SCRIPT_DIR/frontend" && npm start) &
FRONTEND_PID=$!

echo ""
echo "  Frontend: http://localhost:3000"
echo "  Backend:  http://localhost:3001"
echo ""
echo "Press Ctrl+C to stop both servers."

trap "kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; exit" INT TERM
wait
