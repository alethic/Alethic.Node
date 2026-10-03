import { useState } from "react";

/**
 * Shows the props it was given, as JavaScript sees them, with each callback a function it can call.
 */
export function PropsView(props: Record<string, unknown>) {
    const [reply, setReply] = useState<string | null>(null);
    const callbacks = Object.entries(props).filter((e): e is [string, (...args: unknown[]) => Promise<unknown>] => typeof e[1] === "function");

    return (
        <div className="card">
            <pre>{JSON.stringify(props, (_, v) => typeof v === "function" ? `ƒ ${v.name}` : v, 2)}</pre>
            {callbacks.map(([name, callback]) => (
                <button key={name} type="button" onClick={async () => setReply(JSON.stringify(await callback(new Date().toISOString())) ?? "undefined")}>
                    Call {name}
                </button>
            ))}
            {reply !== null && <p className="reply">Answered: {reply}</p>}
        </div>
    );
}
