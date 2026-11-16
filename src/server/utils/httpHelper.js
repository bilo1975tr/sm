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
    req.on('data', (chunk) => {
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
      } catch {
        reject(new Error('Geçersiz JSON verisi'));
      }
    });
    req.on('error', reject);
  });
}
