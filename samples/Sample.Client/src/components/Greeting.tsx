import { useState } from "react";

/**
 * The smallest component worth hosting: a prop, and a callback that raises a command on the page and
 * shows what the page answered, or why it could not.
 */
export function Greeting({ name, onGreet }: { name: string; onGreet: (name: string) => Promise<string | undefined> }) {
    const [reply, setReply] = useState<string | null>(null);

    async function greet() {
        setReply("…");
        try {
            setReply((await onGreet(name)) ?? "(the page answered nothing)");
        } catch (e) {
            setReply(`The page failed: ${e instanceof Error ? e.message : String(e)}`);
        }
    }

    return (
        <div className="card">
            <p>Hello, <strong>{name}</strong>.</p>
            <button type="button" onClick={greet}>Greet the page</button>
            {reply !== null && <p className="reply">{reply}</p>}
        </div>
    );
}
