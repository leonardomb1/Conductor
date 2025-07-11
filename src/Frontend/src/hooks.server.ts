import type { Handle } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
  if (event.url.pathname.startsWith('/api')) {
    const apiHost = process.env.API_HOST;
    const apiUrl = `http://${apiHost}${event.url.pathname}${event.url.search}`;
    
    try {
      const response = await fetch(apiUrl, {
        method: event.request.method,
        headers: {
          ...Object.fromEntries(event.request.headers),
          'X-Forwarded-For': event.getClientAddress(),
        },
        body: event.request.method !== 'GET' ? event.request.body : undefined,
      });

      return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers: response.headers,
      });
    } catch (error) {
      console.error('API proxy error:', error);
      return new Response('API Error', { status: 500 });
    }
  }

  return resolve(event);
};