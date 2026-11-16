import fs from 'fs';
import path from 'path';
import { ROOT_DIR, MIME_TYPES } from '../config/constants.js';

const WEB_DIR = path.resolve(ROOT_DIR, 'Web');
const LOGOS_DIR = path.resolve(ROOT_DIR, 'logos');
const ALLOWED_DIRS = [WEB_DIR, LOGOS_DIR];

function isPathWithin(baseDir, targetPath) {
  const resolvedBase = path.resolve(baseDir);
  const resolvedTarget = path.resolve(targetPath);
  return resolvedTarget === resolvedBase || resolvedTarget.startsWith(resolvedBase + path.sep);
}

/**
 * Handles static file delivery strictly for whitelisted public Web and logo assets.
 */
export function handleStaticRoute(req, res, pathname) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    return false;
  }

  // 1. Serve Main Web Application UI
  if (pathname === '/' || pathname === '/index.html' || pathname === '/web' || pathname === '/web/') {
    try {
      const indexPath = path.join(WEB_DIR, 'index.html');
      if (fs.existsSync(indexPath)) {
        const htmlContent = fs.readFileSync(indexPath, 'utf8');
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(req.method === 'HEAD' ? undefined : htmlContent);
        return true;
      }
    } catch (e) {
      console.error('Web/index.html okuma hatası:', e);
    }
  }

  // 2. Favicon shortcut
  if (pathname === '/favicon.ico') {
    const iconPath = path.join(LOGOS_DIR, 'StreamMesh_Icon.ico');
    if (fs.existsSync(iconPath)) {
      res.writeHead(200, { 'Content-Type': MIME_TYPES['.ico'] || 'image/x-icon' });
      if (req.method === 'HEAD') {
        res.end();
      } else {
        fs.createReadStream(iconPath).pipe(res);
      }
      return true;
    }
  }

  // 3. Whitelisted static files only under /Web/ or /logos/
  let cleanRelPath;
  try {
    cleanRelPath = decodeURIComponent(pathname).replace(/^\/+/, '');
  } catch {
    return false;
  }

  const candidatePath = path.resolve(ROOT_DIR, cleanRelPath);
  const isAllowed = ALLOWED_DIRS.some((dir) => isPathWithin(dir, candidatePath));
  if (!isAllowed) {
    return false;
  }

  if (fs.existsSync(candidatePath) && fs.statSync(candidatePath).isFile()) {
    const ext = path.extname(candidatePath).toLowerCase();
    res.writeHead(200, { 'Content-Type': MIME_TYPES[ext] || 'application/octet-stream' });
    if (req.method === 'HEAD') {
      res.end();
    } else {
      fs.createReadStream(candidatePath).pipe(res);
    }
    return true;
  }

  return false;
}
