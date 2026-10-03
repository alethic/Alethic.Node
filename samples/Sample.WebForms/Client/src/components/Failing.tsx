import { useCached } from "../providers";

/**
 * Fails while it renders, in the way its mode says, so the page shows what becomes of it:
 *
 * - `throw`: it throws.
 * - `reject`: it calls a command whose handler throws, and leaves the rejection unhandled.
 * - `await`: it calls a command whose handler throws, and waits for its answer.
 */
export function Failing({ mode, onFail }: { mode: string; onFail: () => Promise<unknown> }) {
    switch (mode) {
        case "throw":
            throw new Error("The component threw while it rendered.");

        case "reject":
            void onFail();
            return <p>Called the command and walked away.</p>;

        case "await":
            useCached("fail", onFail);
            return <p>Unreachable: the command failed.</p>;

        default:
            return <p>Pick a way to fail.</p>;
    }
}
