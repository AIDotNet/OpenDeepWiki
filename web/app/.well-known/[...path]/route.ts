import { NextRequest, NextResponse } from 'next/server';

/**
 * Proxies /.well-known/* requests to the backend API.
 * Claude.ai and other MCP clients fetch OAuth metadata from these paths at the origin,
 * but only /api/* is handled by the main proxy. This route bridges the gap.
 */

function getApiProxyUrl(): string {
  if (process.env.API_PROXY_URL) {
    return process.env.API_PROXY_URL;
  }
  return '';
}

async function proxyRequest(request: NextRequest) {
  const apiUrl = getApiProxyUrl();
  if (!apiUrl) {
    return NextResponse.json(
      { error: 'API_PROXY_URL_NOT_CONFIGURED' },
      { status: 503 }
    );
  }

  const pathname = request.nextUrl.pathname;
  const searchParams = request.nextUrl.search;
  const targetUrl = `${apiUrl}${pathname}${searchParams}`;

  try {
    const headers = new Headers();
    request.headers.forEach((value, key) => {
      if (!['host', 'connection'].includes(key.toLowerCase())) {
        headers.set(key, value);
      }
    });

    const response = await fetch(targetUrl, {
      method: request.method,
      headers,
      body: request.body,
      // @ts-expect-error duplex is required for streaming body
      duplex: 'half',
    });

    const responseHeaders = new Headers();
    response.headers.forEach((value, key) => {
      if (!['content-encoding', 'transfer-encoding'].includes(key.toLowerCase())) {
        responseHeaders.set(key, value);
      }
    });

    return new NextResponse(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers: responseHeaders,
    });
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : 'Unknown error';
    return NextResponse.json(
      { error: 'PROXY_ERROR', message: errorMessage },
      { status: 502 }
    );
  }
}

export async function GET(request: NextRequest) {
  return proxyRequest(request);
}

export async function POST(request: NextRequest) {
  return proxyRequest(request);
}
