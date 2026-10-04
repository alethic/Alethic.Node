# Alethic.Node.Http

The protocol by which a JavaScript application's `fetch` handler is served from .NET, shared by
[Alethic.Node.AspNetCore](https://www.nuget.org/packages/Alethic.Node.AspNetCore) and
[Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet), so one application is served by either.
Referenced through them; documented here because it is what the application sees.

The application is a CommonJS bundle whose default export has a `fetch` function, or is one:

```js
export default {
    fetch(request, env, ctx) { /* return a Response */ },
};
```

- `request`: a `Request` at `BaseUri` (`http://node.invalid/`) plus the path below the mount. The visitor's address
  is in `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-Prefix`, written by the host.
- `env`: the strings in `Environment`, new per request.
- `ctx`: `waitUntil(promise)` and `passThroughOnException()`.
- The `Response`'s status, headers and body become the host's, less `Content-Length` and `Transfer-Encoding`.

`ResponseBody` is `Streamed`, each chunk sent as produced, or `Buffered`, nothing sent until done, so a failure
partway through can still be answered.
