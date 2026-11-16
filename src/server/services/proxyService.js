import http from 'http';
import https from 'https';
import { DEFAULT_USER_AGENT } from '../config/constants.js';

const MAX_REDIRECTS = 5;
const MAX_MANIFEST_BYTES = 5 * 1024 * 1024;
const REQUEST_TIMEOUT_MS = 20000;

function rewriteHlsManifest(manifestText, baseUrl) {
  const lines = manifestText.split(/\r?\n/);
  const rewritten = [];

  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed) {
      rewritten.push(line);
      continue;
    }

    if (
      trimmed.startsWith('#EXT-X-KEY') ||
      trimmed.startsWith('#EXT-X-MAP') ||
      trimmed.startsWith('#EXT-X-MEDIA')
    ) {
      const updated = line.replace(/URI="([^"]+)"/g, (match, uri) => {
        try {
          const abs = new URL(uri, baseUrl).toString();
          return `URI="/api/proxy?url=${encodeURIComponent(abs)}"`;
        } catch {
          return match;
        }
      });
      rewritten.push(updated);
      continue;
    }

    if (trimmed.startsWith('#')) {
      rewritten.push(line);
      continue;
    }

    try {
      const absUrl = new URL(trimmed, baseUrl).toString();
      rewritten.push(`/api/proxy?url=${encodeURIComponent(absUrl)}`);
    } catch {
      rewritten.push(line);
    }
  }

  return rewritten.join('\n');
}

function fetchAndProxy(req, res, targetUrl, redirectsLeft) {
  let parsed;
  try {
    parsed = new URL(targetUrl);
  } catch (e) {
    res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Geçersiz URL: ' + e.message);
    return;
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Yalnızca HTTP ve HTTPS protokolleri desteklenir');
    return;
  }

  const client = parsed.protocol === 'https:' ? https : http;
  const proxyHeaders = {
    'User-Agent': DEFAULT_USER_AGENT
  };
  if (req.headers.range) {
    proxyHeaders.range = req.headers.range;
  }

  const upstreamReq = client.get(
    targetUrl,
    { headers: proxyHeaders, timeout: REQUEST_TIMEOUT_MS },
    (proxyRes) => {
      const status = proxyRes.statusCode || 200;

      // Follow redirects
      if (status >= 300 && status < 400 && proxyRes.headers.location && redirectsLeft > 0) {
        proxyRes.resume();
        try {
          const nextUrl = new URL(proxyRes.headers.location, targetUrl).toString();
          fetchAndProxy(req, res, nextUrl, redirectsLeft - 1);
        } catch (err) {
          res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
          res.end('Yönlendirme URL hatası: ' + err.message);
        }
        return;
      }

      const contentType = String(proxyRes.headers['content-type'] || '');
      const isM3u8 =
        contentType.toLowerCase().includes('mpegurl') ||
        parsed.pathname.toLowerCase().endsWith('.m3u8');

      if (isM3u8 && status >= 200 && status < 300) {
        const chunks = [];
        let totalSize = 0;
        let aborted = false;

        proxyRes.on('data', (chunk) => {
          totalSize += chunk.length;
          if (totalSize > MAX_MANIFEST_BYTES) {
            aborted = true;
            proxyRes.destroy();
            if (!res.headersSent) {
              res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
              res.end('HLS manifest boyutu çok büyük');
            }
            return;
          }
          chunks.push(chunk);
        });

        proxyRes.on('end', () => {
          if (aborted || res.headersSent) return;
          const rawManifest = Buffer.concat(chunks).toString('utf8');
          const rewritten = rewriteHlsManifest(rawManifest, targetUrl);
          const bodyBuf = Buffer.from(rewritten, 'utf8');
          res.writeHead(200, {
            'Content-Type': 'application/vnd.apple.mpegurl; charset=utf-8',
            'Access-Control-Allow-Origin': '*',
            'Content-Length': bodyBuf.length
          });
          res.end(bodyBuf);
        });

        proxyRes.on('error', (err) => {
          if (!res.headersSent) {
            res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end('Manifest okuma hatası: ' + err.message);
          }
        });
        return;
      }

      const responseHeaders = {
        'Content-Type': contentType || 'application/octet-stream',
        'Access-Control-Allow-Origin': '*',
        'Accept-Ranges': 'bytes'
      };
      if (proxyRes.headers['content-range']) {
        responseHeaders['Content-Range'] = proxyRes.headers['content-range'];
      }
      if (proxyRes.headers['content-length']) {
        responseHeaders['Content-Length'] = proxyRes.headers['content-length'];
      }
      res.writeHead(status, responseHeaders);
      proxyRes.pipe(res);
    }
  );

  upstreamReq.on('timeout', () => {
    upstreamReq.destroy(new Error('Zaman aşımı'));
  });

  upstreamReq.on('error', (e) => {
    if (!res.headersSent) {
      res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('Proxy bağlantı hatası: ' + e.message);
    }
  });

  req.on('close', () => {
    if (!res.writableEnded) {
      upstreamReq.destroy();
    }
  });
}

/**
 * Proxies external video/HLS stream requests with redirect following and HLS manifest rewriting.
 */
export function handleStreamProxy(req, res, targetUrl) {
  if (!targetUrl) {
    res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('URL parametresi eksik');
    return;
  }

  fetchAndProxy(req, res, targetUrl, MAX_REDIRECTS);
}
