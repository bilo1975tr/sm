import fs from 'fs';
import path from 'path';
import { ROOT_DIR, MIME_TYPES } from '../config/constants.js';

/**
 * Handles static file delivery for the Web client and assets.
 */
export function handleStaticRoute(req, res, pathname) {
  // 1. Serve Main Web Application UI
  if (pathname === '/' || pathname === '/index.html' || pathname === '/web' || pathname === '/web/') {
    try {
      const indexPath = path.join(ROOT_DIR, 'Web', 'index.html');
      if (fs.existsSync(indexPath)) {
        const htmlContent = fs.readFileSync(indexPath, 'utf8');
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(htmlContent);
        return true;
      }
    } catch (e) {
      console.error('Web/index.html okuma hatası:', e);
    }
  }

  // 2. Static file fallback for /Web, /logos or assets
  const staticPath = path.normalize(path.join(ROOT_DIR, pathname));
  if (staticPath.startsWith(ROOT_DIR) && fs.existsSync(staticPath) && fs.statSync(staticPath).isFile()) {
    const ext = path.extname(staticPath).toLowerCase();
    res.writeHead(200, { 'Content-Type': MIME_TYPES[ext] || 'application/octet-stream' });
    fs.createReadStream(staticPath).pipe(res);
    return true;
  }

  return false;
}
