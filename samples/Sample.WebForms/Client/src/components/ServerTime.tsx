import { useCached } from "../providers";

type Time = { now: string; visitor: string | null; visits: number };

/**
 * Fetches the site's own handler. Rendered on the server, the fetch is answered in process, as the
 * visitor; in the browser it is an ordinary request. Either way it suspends until the answer is there.
 */
export function ServerTime() {
    const time = useCached("time", async () => {
        const response = await fetch("/Time.ashx");
        if (response.ok === false) {
            throw new Error(`Time.ashx answered ${response.status}.`);
        }

        return await response.json() as Time;
    });

    return (
        <div className="card">
            <p>The site's time is <strong>{time.now}</strong>.</p>
            <p>Asked as visitor <code>{time.visitor ?? "(no session)"}</code>, on visit {time.visits}.</p>
            <p className="note">Rendered {typeof window === "undefined" ? "on the server" : "in the browser"}.</p>
        </div>
    );
}
