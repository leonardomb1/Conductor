import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
  if (event.url.pathname.startsWith('/api')) {
    const apiHost = process.env.API_HOST;
    const apiUrl = `http://${apiHost}${event.url.pathname}${event.url.search}`;
    
    try {
      const requestOptions: RequestInit = {
        method: event.request.method,
        headers: {
          ...Object.fromEntries(event.request.headers),
          'X-Forwarded-For': event.getClientAddress(),
        },
      };

      // Add body and duplex option for non-GET requests
      if (event.request.method !== 'GET' && event.request.method !== 'HEAD') {
        requestOptions.body = event.request.body;
        requestOptions.duplex = 'half';
      }

      const response = await fetch(apiUrl, requestOptions);

      return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers: response.headers,
      });
    } catch (error) {
      console.error('API proxy error:', error);
      return new Response(`API Error: ${error.message}`, { status: 500 });
    }
  }

  return resolve(event);
};