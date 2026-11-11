import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
export const ROOT_DIR = path.resolve(__dirname, '../../../');

export const PORT = 3000;

// Read version safely
let appVersion = '0.0.1';
try {
  const versionFile = path.join(ROOT_DIR, 'version.txt');
  if (fs.existsSync(versionFile)) {
    appVersion = fs.readFileSync(versionFile, 'utf8').trim();
  }
} catch (e) {
  // Fallback to default
}
export const VERSION = appVersion;

export const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.m3u': 'application/x-mpegurl; charset=utf-8',
  '.m3u8': 'application/vnd.apple.mpegurl; charset=utf-8'
};

export const DEFAULT_USER_AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0';
