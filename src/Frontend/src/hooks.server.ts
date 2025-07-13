import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
  if (event.url.pathname.startsWith('/api')) {
    // const apiHost = process.env.API_HOST || 'conductor-api:8080';
    const apiHost = 'localhost:10000';
    const apiUrl = `http://${apiHost}${event.url.pathname}${event.url.search}`;
    
    try {
      // Build headers - CRITICAL: Preserve Authorization header
      const forwardedHeaders: HeadersInit = {
        'X-Forwarded-For': event.getClientAddress(),
        'accept-encoding': 'identity',
      };

      // Forward ALL important headers from the original request
      const headersToForward = [
        'authorization',
        'content-type', 
        'accept',
        'user-agent',
        'x-requested-with'
      ];

      headersToForward.forEach(headerName => {
        const headerValue = event.request.headers.get(headerName);
        if (headerValue) {
          forwardedHeaders[headerName] = headerValue;
        }
      });

      const requestOptions: RequestInit = {
        method: event.request.method,
        headers: forwardedHeaders,
      };

      // Add body for non-GET requests
      if (event.request.method !== 'GET' && event.request.method !== 'HEAD') {
        requestOptions.body = event.request.body;
        requestOptions.duplex = 'half';
      }

      const response = await fetch(apiUrl, requestOptions);
      
      // Read the response
      const responseBody = await response.arrayBuffer();
      
      // Create clean response headers
      const responseHeaders = new Headers();
      for (const [key, value] of response.headers.entries()) {
        if (!['content-encoding', 'content-length', 'transfer-encoding'].includes(key.toLowerCase())) {
          responseHeaders.set(key, value);
        }
      }

      return new Response(responseBody, {
        status: response.status,
        statusText: response.statusText,
        headers: responseHeaders,
      });
    } catch (error) {
      return new Response(`API Error: ${error.message}`, { status: 500 });
    }
  }

  return resolve(event);
};