# Sample.Console

The pool with no web anywhere: a console application takes a lease on an engine and calls a plain JavaScript module,
`tools.cjs`, three ways.

```shell
dotnet run --project samples/Sample.Console
```

- `slugify`, a synchronous export: call it, take the string out.
- `digest`, an asynchronous export: the promise is awaited on the engine's thread, where it lives.
- `stats`, an export returning an object: its properties are read on the engine's thread, and plain .NET data comes
  back.

It registers one engine through `AddNodeEnginePool`, and everything else is `pool.RunAsync(module, exports => ...)`.
