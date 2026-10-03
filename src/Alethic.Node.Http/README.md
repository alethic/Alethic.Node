# Alethic.Node.Http

Serving a JavaScript application over HTTP from Node embedded in a .NET process, whatever the .NET host. It holds the
protocol the hosts share, so an application written for one is served unchanged by the other:

- **Alethic.Node.AspNetCore** serves it from ASP.NET Core endpoints;
- **Alethic.Node.AspNet** serves it from System.Web routes on .NET Framework.

An application is a self-contained CommonJS bundle whose default export has a `fetch` function, or is the function
itself:

```js
export default {
    fetch(request, env, ctx) { /* return a Response, or a promise of one */ },
};
```

`FetchProtocol` is how a host puts a request to it and reads its answer:

- **The request** is the runtime's own `Request`. Its URL is the path below where the host mounted the application,
  under `BaseUri`, which is `http://node.invalid/` unless set: not where the caller was. The host drops `Host` and
  writes `X-Forwarded-Proto`, `X-Forwarded-Host` and, below the root, `X-Forwarded-Prefix` itself, so they say where
  the caller was and a caller cannot say otherwise.
- **`env`** holds the host's strings, from `Environment`, in an object made for each request.
- **`ctx`** has `waitUntil(promise)`, which only keeps a rejection from going unobserved, since a pooled engine keeps
  running, and `passThroughOnException()`, which does nothing.
- **The response's** status, headers and body are the host's to write, less `Content-Length` and `Transfer-Encoding`,
  which the server frames itself.

`FetchProtocolOptions` holds what every host is configured with: `BaseUri`, `Environment`, and `ResponseBody`, a
`BodyMode`:
- **`Streamed`**: each chunk reaches the client as the application produces it. A failure after the first can only
  truncate the response.
- **`Buffered`**: nothing is written until the application is done, so a failure is still one the host answers.

Each host's options extend it with what is its own.
