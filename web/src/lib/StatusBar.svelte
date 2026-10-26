<script lang="ts">
/**
 * The status bar: whether the map has unsaved edits, its problems (a count; a click lists them), and the game data
 * in use.
 */
import TriangleAlert from "@lucide/svelte/icons/triangle-alert";
import X from "@lucide/svelte/icons/x";
import type { SessionState } from "./api.ts";
import type { GameDataState, GameDataView } from "./gameData.ts";

type Props = {
  /** Where the open map stands; null with no map open. */
  session: SessionState | null;
  problems: string[];
  /** True while the problems list is open. */
  showProblems: boolean;
  data: GameDataState | null;
  dataView: GameDataView | null;
  /** Why the page cannot talk to the app; "" when it can. */
  connection: string;
};

let { session, problems, showProblems = $bindable(), data, dataView, connection }: Props = $props();

const folderName = $derived(
  data?.source ? (data.source.split(/[\\/]/).filter(Boolean).pop() ?? "") : "",
);
</script>

<footer class="relative flex items-center gap-4 border-t border-line bg-surface px-3 text-xs text-muted">
  {#if connection}
    <span class="text-warn" data-testid="status">{connection}</span>
  {/if}
  {#if session}
    {#if session.dirty}
      <span class="text-changed" data-testid="dirty">
        ● {session.changedObjects}
        {session.changedObjects === 1 ? "object" : "objects"} changed, not saved
      </span>
    {:else}
      <span data-testid="dirty">No unsaved changes</span>
    {/if}
  {/if}
  {#if problems.length > 0}
    <button
      type="button"
      class="flex items-center gap-1 text-warn hover:underline"
      aria-expanded={showProblems}
      onclick={() => (showProblems = !showProblems)}
    >
      <TriangleAlert size={13} />
      {problems.length}
      {problems.length === 1 ? "map problem" : "map problems"}
    </button>
  {/if}
  <span class="ml-auto truncate" data-testid="game-data-status" title={data?.source ?? ""}>
    {#if dataView === "loading"}
      Loading the game data from <span class="font-mono">{folderName}</span>…
    {:else if dataView === "ready"}
      Game data <span class="font-mono">{folderName}</span>
    {:else if dataView === "failed"}
      <span class="text-warn">Game data failed to load</span>
    {:else if dataView === "missing"}
      No game data folder chosen
    {/if}
  </span>

  {#if showProblems && problems.length > 0}
    <div
      class="absolute bottom-[30px] left-3 z-10 w-[560px] max-w-[calc(100%-24px)] rounded-lg border border-line bg-surface p-3 text-[13px] text-body shadow-[0_10px_40px_rgba(0,0,0,.5)]"
      role="dialog"
      aria-label="Map problems"
    >
      <div class="mb-2 flex items-center">
        <span class="font-bold text-fg">Map problems</span>
        <button
          type="button"
          class="ml-auto text-muted hover:text-fg"
          aria-label="Close"
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
