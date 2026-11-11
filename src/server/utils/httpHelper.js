import http from 'http';
import https from 'https';
import { DEFAULT_USER_AGENT } from '../config/constants.js';

/**
 * Fetches remote text with timeout and redirect following.
 */
export function fetchRemoteText(targetUrl, timeoutMs = 15000) {
  return new Promise((resolve, reject) => {
    try {
      const parsed = new URL(targetUrl);
      const client = parsed.protocol === 'https:' ? https : http;
      const req = client.get(targetUrl, {
        timeout: timeoutMs,
        headers: {
          'User-Agent': DEFAULT_USER_AGENT
        }
      }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          return resolve(fetchRemoteText(res.headers.location, timeoutMs));
        }
        if (res.statusCode < 200 || res.statusCode >= 300) {
          return reject(new Error(`HTTP Hata: ${res.statusCode}`));
        }
        let data = '';
        res.on('data', chunk => {
          data += chunk;
          if (data.length > 20 * 1024 * 1024) {
            res.destroy();
            resolve(data);
          }
        });
        res.on('end', () => resolve(data));
      });
      req.on('timeout', () => { req.destroy(); reject(new Error('Zaman aşımı')); });
      req.on('error', (err) => reject(err));
    } catch (e) {
      reject(e);
    }
  });
}

/**
 * Applies universal CORS headers.
 */
export function setCorsHeaders(res) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS, HEAD');
  res.setHeader('Access-Control-Allow-Headers', '*');
}

/**
 * Sends a structured JSON response.
 */
export function sendJson(res, statusCode, data) {
  res.writeHead(statusCode, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(data));
}

/**
 * Sends an error response.
 */
export function sendError(res, statusCode, message) {
  sendJson(res, statusCode, { error: message });
}

/**
 * Safely parses JSON from incoming request body.
 */
export function parseJsonBody(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      if (body.length > 1024 * 1024) {
        req.destroy();
        reject(new Error('İstek gövdesi çok büyük'));
      }
    });
    req.on('end', () => {
      try {
        const parsed = body ? JSON.parse(body) : {};
        resolve(parsed);
      } catch (err) {
        reject(new Error('Geçersiz JSON verisi'));
      }
    });
    req.on('error', reject);
  });
}
