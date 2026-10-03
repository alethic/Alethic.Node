import { defineConfig } from "vitest/config";

export default defineConfig({
    test: {
        // The server render listens for unhandled rejections itself, to fail the component that left one: a test of that
        // leaves one on purpose, and asserts what became of it.
        dangerouslyIgnoreUnhandledErrors: true,
    },
});
