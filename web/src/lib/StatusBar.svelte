<script lang="ts">
/**
 * The status bar: whether the map has unsaved edits, a short notice (what a save wrote, what went wrong), the map's
 * problems (a count; a click lists them), and the game data in use: the tables the map's data set reads.
 */
import TriangleAlert from "@lucide/svelte/icons/triangle-alert";
import X from "@lucide/svelte/icons/x";
import type { MapOptions, SessionState } from "./api.ts";
import type { GameDataState, GameDataView } from "./gameData.ts";
import { type TextPart, t, tCount, tParts } from "./text.ts";
import { noticeTitle } from "./values.ts";

/**
 * A short message in the status bar, such as what a save wrote or why it failed; its tooltip is the whole text (a long
 * one is cut off with an ellipsis), then `title` when given; `caution` is a warning shown after it in full.
 */
export type Notice = { text: string; title?: string; warn?: boolean; caution?: string };

type Props = {
  /** Where the open map stands; null with no map open. */
  session: SessionState | null;
  problems: string[];
  /** True while the problems list is open. */
  showProblems: boolean;
  data: GameDataState | null;
  dataView: GameDataView | null;
  /** The open map's game data option; null with no map, or none the editor can read (the base tables). */
  options?: MapOptions | null;
  /** Why the page cannot talk to the app; "" when it can. */
  connection: string;
  notice: Notice | null;
};

let {
  session,
  problems,
  showProblems = $bindable(),
  data,
  dataView,
  options = null,
  connection,
  notice,
}: Props = $props();

// The values come from the tables the map's data set reads, as World Editor's do (Sven, 2026-10-27).
const tables = $derived(
  !options || options.values === "base" ? t("status.baseTables") : options.values,
);
const dataTitle = $derived(
  dataView === "ready"
    ? `${data?.source ?? ""}\n` +
        (options
          ? t("status.dataTitle", {
              dataSet: options.dataSet,
              dataVersion: options.dataVersion,
              values: tables,
              objects: options.objects === "base" ? t("status.theBaseTables") : options.objects,
            })
          : t("status.dataTitleUnknown"))
    : (data?.source ?? ""),
);

const folderName = $derived(
  data?.source ? (data.source.split(/[\\/]/).filter(Boolean).pop() ?? "") : "",
);
</script>

{#snippet folderText(parts: TextPart[])}
  {#each parts as part, index (index)}{#if part.slot === "folder"}<span class="font-mono">{folderName}</span>{:else}{part.text}{/if}{/each}
{/snippet}

<footer class="relative flex items-center gap-4 border-t border-line bg-surface px-3 text-xs text-muted">
  {#if connection}
    <span class="text-warn" data-testid="status">{connection}</span>
  {/if}
  {#if session}
    {#if session.dirty}
      <span class="text-changed" data-testid="dirty">
        {tCount("status.changed", session.changedObjects)}
      </span>
    {:else}
      <span data-testid="dirty">{t("status.noChanges")}</span>
    {/if}
  {/if}
  {#if notice}
    <span
      class="min-w-0 truncate {notice.warn ? 'text-warn' : 'text-accent-hi'}"
      data-testid="notice"
      title={noticeTitle(notice)}
      role="status"
    >
      {notice.text}
    </span>
    {#if notice.caution}
      <span class="shrink-0 whitespace-nowrap text-warn" data-testid="notice-caution">
        {notice.caution}
      </span>
    {/if}
  {:else if session}
    <span class="whitespace-nowrap">{t("status.undoRedo")}</span>
  {/if}
  {#if problems.length > 0}
    <button
      type="button"
      class="flex items-center gap-1 text-warn hover:underline"
      aria-expanded={showProblems}
      onclick={() => (showProblems = !showProblems)}
    >
      <TriangleAlert size={13} />
      {tCount("status.problems", problems.length)}
    </button>
  {/if}
  <span
    class="ml-auto truncate"
    data-testid="game-data-status"
    title={dataTitle}
  >
    {#if dataView === "loading"}
      {@render folderText(tParts("status.dataLoading"))}
    {:else if dataView === "ready"}
      {@render folderText(tParts("status.dataReady", { tables }))}
    {:else if dataView === "failed"}
      <span class="text-warn">{t("status.dataFailed")}</span>
    {:else if dataView === "missing"}
      {t("status.dataMissing")}
    {/if}
  </span>

  {#if showProblems && problems.length > 0}
    <div
      class="absolute bottom-[30px] left-3 z-10 w-[560px] max-w-[calc(100%-24px)] rounded-lg border border-line bg-surface p-3 text-[13px] text-body shadow-[0_10px_40px_rgba(0,0,0,.5)]"
      role="dialog"
      aria-label={t("status.problemsTitle")}
    >
      <div class="mb-2 flex items-center">
        <span class="font-bold text-fg">{t("status.problemsTitle")}</span>
        <button
          type="button"
          class="ml-auto text-muted hover:text-fg"
          aria-label={t("status.close")}
          onclick={() => (showProblems = false)}
        >
          <X size={14} />
        </button>
      </div>
      <ul class="m-0 flex list-disc flex-col gap-1 pl-5">
        {#each problems as problem, index (index)}
          <li>{problem}</li>
        {/each}
      </ul>
    </div>
  {/if}
</footer>
