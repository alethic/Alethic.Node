import { useState } from "react";

/**
 * A count the page gives it, and one of its own: the page's comes with each render of the page, its own
 * lasts as long as the component does — which is until a partial postback renders its control again.
 */
export function Counter({ label, start, onReport }: { label: string; start: number; onReport?: (count: number) => Promise<string> }) {
    const [count, setCount] = useState(0);
    const [reply, setReply] = useState<string | null>(null);

    return (
        <div className="card">
            <p>{label}: the page says <strong>{start}</strong>; this component counts <strong>{count}</strong>.</p>
            <button type="button" onClick={() => setCount(c => c + 1)}>Count</button>
            {onReport && (
                <button type="button" onClick={async () => setReply(await onReport(count))}>Report to the page</button>
            )}
            {reply !== null && <p className="reply">{reply}</p>}
        </div>
    );
}
