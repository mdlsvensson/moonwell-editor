/**
 * The page's colour themes (Sven, 2026-10-27): each sets the colour tokens app.css defines on `:root` (Kanagawa Dragon
 * there, and first here: the default, Sven, 2026-10-29), from its scheme's own palette. The choice is remembered in the window's storage.
 */

/** The colour tokens every theme sets, as app.css names them without the `--`. */
export const tokens = [
  "bg",
  "surface",
  "raised",
  "hover",
  "border",
  "fg",
  "text",
  "muted",
  "faint",
  "accent",
  "accent-hi",
  "accent-lo",
  "changed",
  "warn",
  "warn-lo",
  "cyan",
  "purple",
  "kind-unit",
  "kind-item",
  "kind-destructable",
  "kind-doodad",
  "kind-ability",
  "kind-buff",
  "kind-upgrade",
] as const;

export type Token = (typeof tokens)[number];

export type Theme = { id: string; name: string; colors: Record<Token, string> };

/** The palette's colours in the tokens' order: backgrounds, text, accent, states, then the seven kinds. */
function theme(id: string, name: string, values: string[]): Theme {
  if (values.length !== tokens.length) throw new Error(`${id} has ${values.length} colours`);
  const colors = Object.fromEntries(tokens.map((token, index) => [token, values[index]])) as Record<
    Token,
    string
  >;
  return { id, name, colors };
}

// bg, surface, raised, hover, border, fg, text, muted, faint, accent, accent-hi, accent-lo, changed, warn, warn-lo,
// cyan, purple, then unit, item, destructable, doodad, ability, buff, upgrade.
// biome-ignore format: nine colours to a line keep each palette readable
export const themes: Theme[] = [
  // github.com/rebelot/kanagawa.nvim
  theme("kanagawa-dragon", "Kanagawa Dragon", [
    "#0d0c0c", "#181616", "#282727", "#282727", "#393836", "#c5c9c5", "#b4b8b4", "#a6a69c", "#737c73",
    "#8ba4b0", "#8ea4a2", "#2a3438", "#c4b28a", "#c4746e", "#3a2626", "#8ea4a2", "#8992a7",
    "#8ba4b0", "#b6927b", "#87a987", "#c4b28a", "#8ea4a2", "#a292a3", "#c4746e",
  ]),
  theme("kanagawa-wave", "Kanagawa Wave", [
    "#16161d", "#1f1f28", "#2a2a37", "#2a2a37", "#363646", "#dcd7ba", "#c8c093", "#938aa9", "#727169",
    "#7e9cd8", "#7fb4ca", "#223249", "#e6c384", "#e46876", "#43242b", "#7aa89f", "#957fb8",
    "#7e9cd8", "#ffa066", "#98bb6c", "#c0a36e", "#7aa89f", "#957fb8", "#d27e99",
  ]),
  // github.com/antfu/vscode-theme-vitesse
  theme("vitesse-dark", "Vitesse Dark", [
    "#121212", "#181818", "#1e1e1e", "#222222", "#262626", "#dbd7ca", "#bfbaaa", "#959da5", "#6b6f73",
    "#4d9375", "#80a665", "#144620", "#e6cc77", "#cb7676", "#43242b", "#5eaab5", "#6872ab",
    "#6394bf", "#d4976c", "#80a665", "#b8a965", "#5eaab5", "#a28dcd", "#c98a7d",
  ]),
  // github.com/morhetz/gruvbox
  theme("gruvbox-dark", "Gruvbox Dark", [
    "#1d2021", "#282828", "#32302f", "#3c3836", "#3c3836", "#ebdbb2", "#d5c4a1", "#a89984", "#7c6f64",
    "#8ec07c", "#b8bb26", "#2c3a2a", "#fabd2f", "#fb4934", "#3c1f1e", "#8ec07c", "#d3869b",
    "#83a598", "#fe8019", "#b8bb26", "#d79921", "#8ec07c", "#d3869b", "#ea6962",
  ]),
  // draculatheme.com
  theme("dracula", "Dracula", [
    "#191a21", "#21222c", "#282a36", "#343746", "#343746", "#f8f8f2", "#e2e2dc", "#9da5c4", "#6272a4",
    "#bd93f9", "#d6bcfa", "#3b2f5a", "#f1fa8c", "#ff5555", "#4a2030", "#8be9fd", "#bd93f9",
    "#8be9fd", "#ffb86c", "#50fa7b", "#f1fa8c", "#bd93f9", "#ff79c6", "#ff6e6e",
  ]),
  // catppuccin.com/palette
  theme("catppuccin-mocha", "Catppuccin Mocha", [
    "#11111b", "#181825", "#1e1e2e", "#313244", "#313244", "#cdd6f4", "#bac2de", "#9399b2", "#6c7086",
    "#cba6f7", "#b4befe", "#3a2f55", "#f9e2af", "#f38ba8", "#45273a", "#94e2d5", "#cba6f7",
    "#89b4fa", "#fab387", "#a6e3a1", "#f9e2af", "#94e2d5", "#f5c2e7", "#eba0ac",
  ]),
  theme("catppuccin-macchiato", "Catppuccin Macchiato", [
    "#181926", "#1e2030", "#24273a", "#363a4f", "#363a4f", "#cad3f5", "#b8c0e0", "#939ab7", "#6e738d",
    "#c6a0f6", "#b7bdf8", "#3b3058", "#eed49f", "#ed8796", "#462a3a", "#8bd5ca", "#c6a0f6",
    "#8aadf4", "#f5a97f", "#a6da95", "#eed49f", "#8bd5ca", "#f5bde6", "#ee99a0",
  ]),
  // github.com/folke/tokyonight.nvim
  theme("tokyo-night", "Tokyo Night", [
    "#16161e", "#1a1b26", "#24283b", "#292e42", "#292e42", "#c0caf5", "#a9b1d6", "#737aa2", "#565f89",
    "#7aa2f7", "#7dcfff", "#283457", "#e0af68", "#f7768e", "#3b2030", "#7dcfff", "#bb9af7",
    "#7aa2f7", "#ff9e64", "#9ece6a", "#e0af68", "#7dcfff", "#bb9af7", "#f7768e",
  ]),
  // nordtheme.com
  theme("nord", "Nord", [
    "#242933", "#2e3440", "#3b4252", "#3b4252", "#3b4252", "#eceff4", "#d8dee9", "#9aa5b8", "#616e88",
    "#88c0d0", "#8fbcbb", "#2f4552", "#ebcb8b", "#bf616a", "#43303a", "#8fbcbb", "#b48ead",
    "#81a1c1", "#d08770", "#a3be8c", "#ebcb8b", "#88c0d0", "#b48ead", "#bf616a",
  ]),
  // github.com/atom/atom/tree/master/packages/one-dark-ui
  theme("one-dark", "One Dark", [
    "#21252b", "#282c34", "#2c313a", "#2c313a", "#333842", "#dcdfe4", "#abb2bf", "#8b929e", "#5c6370",
    "#61afef", "#7fc1f5", "#1f3550", "#e5c07b", "#e06c75", "#3f2628", "#56b6c2", "#c678dd",
    "#61afef", "#d19a66", "#98c379", "#e5c07b", "#56b6c2", "#c678dd", "#e06c75",
  ]),
  // rosepinetheme.com
  theme("rose-pine", "Rosé Pine", [
    "#191724", "#1f1d2e", "#26233a", "#26233a", "#2c2a3f", "#e0def4", "#cfcbe6", "#908caa", "#6e6a86",
    "#c4a7e7", "#ebbcba", "#352f4d", "#f6c177", "#eb6f92", "#3d2232", "#9ccfd8", "#c4a7e7",
    "#3e8fb0", "#ebbcba", "#9ccfd8", "#f6c177", "#c4a7e7", "#eb6f92", "#e0a39b",
  ]),
  // github.com/sainnhe/everforest
  theme("everforest-dark", "Everforest Dark", [
    "#232a2e", "#2d353b", "#343f44", "#343f44", "#3d484d", "#d3c6aa", "#c5b9a0", "#9da9a0", "#7a8478",
    "#a7c080", "#83c092", "#3c4a33", "#dbbc7f", "#e67e80", "#4c3743", "#83c092", "#d699b6",
    "#7fbbb3", "#e69875", "#a7c080", "#dbbc7f", "#83c092", "#d699b6", "#e67e80",
  ]),
  // ethanschoonover.com/solarized
  theme("solarized-dark", "Solarized Dark", [
    "#00212b", "#002b36", "#073642", "#073642", "#0b3c48", "#eee8d5", "#93a1a1", "#839496", "#586e75",
    "#268bd2", "#2aa198", "#0a3d5a", "#b58900", "#dc322f", "#3a1c1c", "#2aa198", "#6c71c4",
    "#268bd2", "#cb4b16", "#859900", "#b58900", "#2aa198", "#6c71c4", "#d33682",
  ]),
];

/** The theme the page starts with when none is chosen. */
export const defaultTheme = "kanagawa-dragon";

const storageKey = "moonwell-editor.theme";

/** The theme with this id, or the default one. */
export function themeOf(id: string | null | undefined): Theme {
  return themes.find((each) => each.id === id) ?? (themes[0] as Theme);
}

/** Puts a theme's colours on the page. */
export function applyTheme(chosen: Theme, root: HTMLElement = document.documentElement): void {
  for (const token of tokens) root.style.setProperty(`--${token}`, chosen.colors[token]);
  root.dataset.theme = chosen.id;
}

/** The theme chosen last; the default one when none was, or the storage cannot be read. */
export function savedTheme(): Theme {
  try {
    return themeOf(localStorage.getItem(storageKey));
  } catch {
    return themeOf(defaultTheme);
  }
}

/** Remembers the theme chosen (the page still shows it when the storage cannot be written). */
export function saveTheme(chosen: Theme): void {
  try {
    localStorage.setItem(storageKey, chosen.id);
  } catch {
    // Not remembered: the next start shows the default theme.
  }
}
