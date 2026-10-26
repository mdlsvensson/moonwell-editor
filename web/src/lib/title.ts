/** The window's title: the app's name, after the open map's path when a map is open. */

/** The app's name, as the window and the page show it. */
export const appName = "Moonwell Editor";

/** The window title for the open map's path, or for no map. */
export function windowTitle(path: string | null): string {
  return path ? `${path} — ${appName}` : appName;
}
