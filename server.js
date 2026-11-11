import http from 'http';
import { PORT } from './src/server/config/constants.js';
import { setCorsHeaders, sendError } from './src/server/utils/httpHelper.js';
import { handleApiRoute } from './src/server/routes/apiRoutes.js';
import { handleStreamRoute } from './src/server/routes/streamRoutes.js';
import { handleStaticRoute } from './src/server/routes/staticRoutes.js';

/**
 * StreamMesh Modular HTTP Server Entry Point
 */
const server = http.createServer(async (req, res) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost:3000'}`);
  const pathname = parsedUrl.pathname;

  // Apply Global CORS headers
  setCorsHeaders(res);

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  try {
    // 1. API Endpoints
    if (await handleApiRoute(req, res, pathname, parsedUrl)) return;

    // 2. Channel Streaming & AceStream redirection
    if (handleStreamRoute(req, res, pathname)) return;

    // 3. Static Web Portal & Media Assets
    if (handleStaticRoute(req, res, pathname)) return;

    // 4. Fallback Not Found
    sendError(res, 404, `Endpoint bulunamadı: ${pathname}`);
  } catch (err) {
    console.error('Server error handling request:', err);
    sendError(res, 500, 'Sunucu hatası: ' + (err.message || 'Bilinmeyen hata'));
  }
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh Modular Server running on http://0.0.0.0:${PORT}`);
});
