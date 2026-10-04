// The shapes of work a site gives its engines, each clocked rather than computed, so that a faster machine does not
// finish it sooner: what the pool learns from is how the engine's thread is occupied, not how fast it is.

// Keeps the thread busy for the time given: a render that computes.
exports.compute = ms => {
    const end = Date.now() + ms;
    while (Date.now() < end) { }
    return true;
};

// Leaves the thread free for the time given: a render that waits on a fetch.
exports.fetch = ms => new Promise(resolve => setTimeout(() => resolve(true), ms));

// Keeps the given megabytes on the heap, in place of whatever was kept before, and a quarter as much outside it.
let kept = null;
let buffer = null;
exports.allocate = mb => {
    kept = Array.from({ length: mb * 16384 }, (_, i) => ({ i, s: 'x' + i }));
    buffer = Buffer.alloc(mb * 256 * 1024);
    return true;
};

// Lets it go.
exports.release = () => {
    kept = null;
    buffer = null;
    return true;
};
