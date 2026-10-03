import { Suspense, use, useState } from "react";
import { match } from "./router";

/** A park, as the sample's data source has it. */
export type Park = { name: string; city: string; state: string };

/**
 * The sample's data access. On the server the promise resolves before rendering completes, so the
 * park's name lands in the markup; in the browser the same component hydrates over it.
 */
function ParkView({ promise }: { promise: Promise<Park> }) {
    const park = use(promise);
    return (
        <article>
            <h1>{park.name}</h1>
            <p>{park.city}, {park.state}</p>
        </article>
    );
}

/** A little interactivity, to prove hydration produces a live page rather than an inert one. */
function Clicker() {
    const [count, setCount] = useState(0);
    return <button onClick={() => setCount(count + 1)}>clicked {count}</button>;
}

/**
 * The ASP.NET Core sample's application, rendered whole for a path.
 * @param props
 */
export default function App({ path, dataPromise }: { path: string; dataPromise: Promise<Park> | null }) {
    // The same table the server dispatched on and the host mapped its endpoints from.
    const matched = match(path);

    switch (matched?.route.id) {
        case "park":
            return (
                <main>
                    <Suspense fallback={<p>loading…</p>}>
                        <ParkView promise={dataPromise!} />
                    </Suspense>
                    <Clicker />
                </main>
            );

        case "about":
            return (
                <main>
                    <h1>About</h1>
                    <p>A sample React application rendered inside a .NET process.</p>
                    <Clicker />
                </main>
            );

        case "home":
            return (
                <main>
                    <h1>Home</h1>
                    <p>Try <a href="/parks/enchanted-rock">a park</a> or <a href="/about">about</a>.</p>
                    <Clicker />
                </main>
            );

        default:
            return (
                <main>
                    <h1>Not found</h1>
                    <p>No route matched <code>{path}</code>.</p>
                </main>
            );
    }
}

/**
 * Stands in for a real data source; the server and the browser both go through it.
 * @param parkRef the park's reference
 */
export function loadPark(parkRef: string): Promise<Park> {
    return new Promise(resolve => setTimeout(() => resolve({
        name: parkRef.replace(/-/g, " ").replace(/\b\w/g, c => c.toUpperCase()),
        city: "Fredericksburg",
        state: "Texas",
    }), 25));
}
