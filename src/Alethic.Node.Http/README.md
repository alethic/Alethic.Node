# Alethic.Node.Http

The protocol by which a JavaScript application's `fetch` handler is served from .NET, shared by the two web hosts so
that one application is served unchanged by either:

- [Alethic.Node.AspNetCore](https://www.nuget.org/packages/Alethic.Node.AspNetCore), from ASP.NET Core endpoints;
- [Alethic.Node.AspNet](https://www.nuget.org/packages/Alethic.Node.AspNet), from System.Web routes on .NET Framework.

You reference it through one of those; it has nothing to use on its own. It is documented here because what it defines
is what an application sees.

## The application

A self-contained CommonJS bundle whose default export has a `fetch` function, or is the function itself:

```js
export default {
    fetch(request, env, ctx) {
        return new Response("<!doctype html>...", { headers: { "content-type": "text/html" } });
    },
};
```

- **`request`** is the runtime's own `Request`. Its URL is the path below where the host mounted the application,
  under `BaseUri` (`http://node.invalid/` unless the host sets it): not the address the visitor used. Where the visitor
  was is in `X-Forwarded-Proto`, `X-Forwarded-Host` and, for an application mounted below the root,
  `X-Forwarded-Prefix`, which the host writes and a visitor cannot forge. `Host` is not passed.
- **`env`** is an object of strings the host supplies, from `Environment`, new for each request: what only the host
  knows, such as an internal API address.
- **`ctx`** has `waitUntil(promise)`, which keeps a rejection from going unobserved, and `passThroughOnException()`,
  which does nothing.
- **It returns** a `Response`, or a promise of one. Its status, headers and body become the host's response, less
  `Content-Length` and `Transfer-Encoding`, which the server sets itself.

Anything the application needs set up once per engine, it memoizes in module scope, which is per engine.

## The options

`FetchProtocolOptions` is what every host's handler options extend:

| Option | Default | |
| --- | --- | --- |
| `BaseUri` | `http://node.invalid/` | The address the application is asked at. A path on it is put ahead of the request's. |
| `Environment` | empty | The strings `env` carries. |
| `ResponseBody` | `Streamed` | `Streamed`: each chunk reaches the client as the application produces it, so a failure after the first can only truncate the response. `Buffered`: nothing is written until the application is done, so a failure is still the host's to answer. |
