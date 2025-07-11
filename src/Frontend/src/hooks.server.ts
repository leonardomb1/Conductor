import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
  if (event.url.pathname.startsWith('/api')) {
    const apiHost = process.env.API_HOST;
    const apiUrl = `http://${apiHost}${event.url.pathname}${event.url.search}`;
    
    console.log('API_HOST:', apiHost);
    console.log('Proxying to:', apiUrl);
    console.log('Method:', event.request.method);
    
    try {
      const requestOptions: RequestInit = {
        method: event.request.method,
        headers: {
          ...Object.fromEntries(event.request.headers),
          'X-Forwarded-For': event.getClientAddress(),
          // Remove problematic headers that can cause encoding issues
          'accept-encoding': 'identity',
        },
      };

      // Add body and duplex option for non-GET requests
      if (event.request.method !== 'GET' && event.request.method !== 'HEAD') {
        requestOptions.body = event.request.body;
        requestOptions.duplex = 'half';
      }

      const response = await fetch(apiUrl, requestOptions);
      
      // Read the response as text/arrayBuffer to avoid encoding issues
      const responseBody = await response.arrayBuffer();
      
      // Create clean headers without compression-related ones
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
      console.error('API proxy error:', error);
      return new Response(`API Error: ${error.message}`, { status: 500 });
    }
  }

  return resolve(event);
};