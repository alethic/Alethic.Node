import { hydrateRoot } from "react-dom/client";
import App, { loadPark } from "./App";
import { match } from "./router";

/**
 * The ASP.NET Core sample's browser entry: hydrates the document the server rendered.
 */

const path = window.location.pathname;
const matched = match(path);

// The same promise shape the server rendered against, off the same route table, so hydration matches.
const dataPromise = matched?.route.id === "park" ? loadPark(matched.params.parkRef) : null;

hydrateRoot(document.getElementById("app")!, <App path={path} dataPromise={dataPromise} />);
