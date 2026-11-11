import http from 'http';
import https from 'https';
import { DEFAULT_USER_AGENT } from '../config/constants.js';

/**
 * Proxies external video/HLS stream requests to bypass CORS and mixed-content issues.
 */
export function handleStreamProxy(req, res, targetUrl) {
  if (!targetUrl) {
    res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('URL parametresi eksik');
    return;
  }

  try {
    const parsed = new URL(targetUrl);
    const client = parsed.protocol === 'https:' ? https : http;
    const proxyHeaders = {
      'User-Agent': DEFAULT_USER_AGENT
    };
    if (req.headers.range) {
      proxyHeaders.range = req.headers.range;
    }

    client.get(targetUrl, { headers: proxyHeaders }, (proxyRes) => {
      const responseHeaders = {
        'Content-Type': proxyRes.headers['content-type'] || 'application/vnd.apple.mpegurl',
        'Access-Control-Allow-Origin': '*',
        'Accept-Ranges': 'bytes'
      };
      if (proxyRes.headers['content-range']) {
        responseHeaders['Content-Range'] = proxyRes.headers['content-range'];
      }
      if (proxyRes.headers['content-length']) {
        responseHeaders['Content-Length'] = proxyRes.headers['content-length'];
      }
      res.writeHead(proxyRes.statusCode || 200, responseHeaders);
      proxyRes.pipe(res);
    }).on('error', (e) => {
      res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('Proxy bağlantı hatası: ' + e.message);
    });
  } catch (e) {
    res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Geçersiz URL: ' + e.message);
  }
}
