/**
 * The editor's hotkeys (Sven, 2026-10-27): each action's keys, World Editor's where it has them, which the settings'
 * Hotkeys tab changes. A key is written as its modifiers and its key: `Ctrl+Shift+N`, `F2`, `Alt+ArrowLeft`. The
 * changes are remembered in the window's storage; the table's own keys (arrows, Enter, Ctrl A) are not here.
 */

import { t } from "./text.ts";

export type Action =
  | "save"
  | "undo"
  | "redo"
  | "newObject"
  | "copyObject"
  | "copy"
  | "cut"
  | "paste"
  | "rename"
  | "deleteObject"
  | "filterFields"
  | "fieldIds"
  | "back"
  | "forward";

/** Every action, in the order the settings list them, with its name and its keys out of the box. */
export const actionList: { action: Action; name: string; keys: string[] }[] = [
  { action: "save", name: t("hotkey.save"), keys: ["Ctrl+S"] },
  { action: "undo", name: t("hotkey.undo"), keys: ["Ctrl+Z"] },
  { action: "redo", name: t("hotkey.redo"), keys: ["Ctrl+Y", "Ctrl+Shift+Z"] },
  { action: "newObject", name: t("hotkey.newObject"), keys: ["Ctrl+Shift+N"] },
  { action: "copyObject", name: t("hotkey.copyObject"), keys: ["Ctrl+Shift+D"] },
  { action: "copy", name: t("hotkey.copy"), keys: ["Ctrl+C"] },
  { action: "cut", name: t("hotkey.cut"), keys: ["Ctrl+X"] },
  { action: "paste", name: t("hotkey.paste"), keys: ["Ctrl+V"] },
  { action: "rename", name: t("hotkey.rename"), keys: ["F2"] },
  { action: "deleteObject", name: t("hotkey.deleteObject"), keys: ["Delete"] },
  { action: "filterFields", name: t("hotkey.filterFields"), keys: ["Ctrl+F"] },
  { action: "fieldIds", name: t("hotkey.fieldIds"), keys: ["Ctrl+D"] },
  { action: "back", name: t("hotkey.back"), keys: ["Alt+ArrowLeft"] },
  { action: "forward", name: t("hotkey.forward"), keys: ["Alt+ArrowRight"] },
];

export type Keymap = Record<Action, string[]>;

const storageKey = "moonwell-editor.hotkeys";

/** The keys out of the box. */
export function defaultKeymap(): Keymap {
  return Object.fromEntries(actionList.map((each) => [each.action, [...each.keys]])) as Keymap;
}

/** The keymap with the user's changes over the defaults; the defaults when there are none or they cannot be read. */
export function loadKeymap(): Keymap {
  const keymap = defaultKeymap();
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) ?? "{}") as Partial<
      Record<string, unknown>
    >;
    for (const each of actionList) {
      const keys = saved[each.action];
      if (Array.isArray(keys) && keys.every((key) => typeof key === "string"))
        keymap[each.action] = keys;
    }
  } catch {
    // The defaults.
  }
  return keymap;
}

/** Remembers the keymap's changes (only the actions whose keys differ from the defaults). */
export function saveKeymap(keymap: Keymap): void {
  const defaults = defaultKeymap();
  const changed = Object.fromEntries(
    actionList
      .filter((each) => keymap[each.action].join("|") !== defaults[each.action].join("|"))
      .map((each) => [each.action, keymap[each.action]]),
  );
  try {
    localStorage.setItem(storageKey, JSON.stringify(changed));
  } catch {
    // Not remembered: the next start has the defaults.
  }
}

const modifierKeys = new Set(["Control", "Shift", "Alt", "Meta"]);

/** The key a key press is, as the keymap writes it; null for a modifier alone. */
export function keyOf(
  event: Pick<KeyboardEvent, "key" | "ctrlKey" | "shiftKey" | "altKey">,
): string | null {
  if (modifierKeys.has(event.key)) return null;
  const key = event.key.length === 1 ? event.key.toUpperCase() : event.key;
  return [event.ctrlKey && "Ctrl", event.altKey && "Alt", event.shiftKey && "Shift", key]
    .filter(Boolean)
    .join("+");
}

/** The action a key press asks for in the keymap; undefined for none. */
export function actionOf(
  event: Pick<KeyboardEvent, "key" | "ctrlKey" | "shiftKey" | "altKey">,
  keymap: Keymap,
): Action | undefined {
  const key = keyOf(event);
  if (!key) return undefined;
  return actionList.find((each) => keymap[each.action].includes(key))?.action;
}

/** The actions other than `action` that already use `key`. */
export function clashes(keymap: Keymap, action: Action, key: string): Action[] {
  return actionList
    .filter((each) => each.action !== action && keymap[each.action].includes(key))
    .map((each) => each.action);
}

/** A key as the settings show it: `Ctrl Shift N`, `Alt ←`. */
export function shownKey(key: string): string {
  return key
    .split("+")
    .map((part) =>
      part === "ArrowLeft"
        ? "←"
        : part === "ArrowRight"
          ? "→"
          : part === "ArrowUp"
            ? "↑"
            : part === "ArrowDown"
              ? "↓"
              : part,
    )
    .join(" ");
}
