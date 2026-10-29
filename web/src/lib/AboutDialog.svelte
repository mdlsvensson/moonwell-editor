<script lang="ts">
/**
 * The About page, from the header's menu (Sven, 2026-10-29): the editor's version and the versions of what it runs on
 * (for a bug report), and the libraries it is built with and their licences.
 */
import type { About } from "./api.ts";
import Dialog from "./Dialog.svelte";
import logo from "./logo.svg";
import { t } from "./text.ts";

type Props = {
  about: About | null;
  /** The game version of the loaded game data; null before it is loaded. */
  game: string | null;
  onclose: () => void;
};

let { about, game, onclose }: Props = $props();

// What the editor is built with, by name, and each one's licence.
const libraries: [string, string][] = [
  ["War3Net", "MIT"],
  ["Pfim", "MIT"],
  ["WebView2", "BSD-3-Clause"],
  ["Svelte", "MIT"],
  ["Bits UI", "MIT"],
  ["Tiptap", "MIT"],
  ["Tailwind CSS", "MIT"],
  ["Lucide", "ISC"],
  ["war3-model", "MIT"],
  ["gl-matrix", "MIT"],
  ["dds-parser", "MIT"],
  ["Inter", "OFL-1.1"],
  ["JetBrains Mono", "OFL-1.1"],
];
</script>

{#snippet row(label: string, value: string)}
  <div class="flex gap-3 py-0.5">
    <span class="w-32 shrink-0 text-muted">{label}</span>
    <span class="min-w-0 font-mono text-[12.5px] break-words text-fg">{value}</span>
  </div>
{/snippet}

<Dialog title={t("about.title")} oncancel={onclose} focus={false}>
  <div class="mb-3 flex items-center gap-3">
    <img src={logo} alt="" class="size-10 shrink-0" />
    <div>
      <p class="m-0 font-heading text-lg font-bold text-fg">
        {t("noMap.title", { version: about?.version ?? "" }).trim()}
      </p>
      <p class="m-0 text-muted">{t("about.text")}</p>
    </div>
  </div>

  {#if about}
    {@render row(t("about.version"), about.version)}
    {@render row(t("about.moonwell"), about.moonwell)}
  {/if}
  {#if game}
    {@render row(t("about.game"), game)}
  {/if}
  {#if about}
    {@render row(t("about.dotNet"), about.dotNet)}
    {@render row(t("about.webView"), about.webView2 ?? t("about.unknown"))}
  {/if}

  <h3 class="m-0 mt-4 mb-2 text-[12px] tracking-[.06em] text-muted uppercase">{t("about.builtWith")}</h3>
  <ul class="m-0 grid list-none grid-cols-2 gap-x-6 gap-y-0.5 p-0">
    {#each libraries as [name, licence] (name)}
      <li class="flex justify-between gap-2">
        <span class="text-body">{name}</span>
        <span class="font-mono text-[12px] text-faint">{licence}</span>
      </li>
    {/each}
  </ul>

  <p class="mt-4 mb-0 text-[12.5px] text-faint">{t("about.blizzard")}</p>

  {#snippet actions()}
    <button
      type="button"
      class="rounded-[5px] bg-accent px-3 py-1 font-bold text-bg hover:bg-accent-hi"
      data-primary
      onclick={onclose}
    >
      {t("settings.done")}
    </button>
  {/snippet}
</Dialog>
