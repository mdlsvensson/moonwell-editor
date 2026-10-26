<script lang="ts">
import FolderOpen from "@lucide/svelte/icons/folder-open";
import { onMount } from "svelte";
import { call } from "./lib/bridge.ts";

let status = $state("Connecting…");

onMount(async () => {
  try {
    status = await call<string>("ping");
  } catch (error) {
    status = error instanceof Error ? error.message : String(error);
  }
});
</script>

<div class="grid h-full grid-rows-[44px_1fr_26px]">
  <header class="flex items-center gap-2.5 border-b border-line bg-surface px-3">
    <button
      type="button"
      class="flex items-center gap-2 rounded-md border border-line bg-raised px-3 py-1 text-fg hover:border-accent"
    >
      <FolderOpen size={16} />
      Open map…
    </button>
  </header>

  <main class="grid min-h-0 grid-cols-[290px_1fr_300px] max-[1100px]:grid-cols-[230px_1fr_240px]">
    <aside class="min-h-0 overflow-auto border-r border-line bg-surface" aria-label="Objects"></aside>
    <section class="min-h-0 overflow-auto" aria-label="Details"></section>
    <aside class="min-h-0 overflow-auto border-l border-line bg-surface p-3" aria-label="Components"></aside>
  </main>

  <footer class="flex items-center gap-4 border-t border-line bg-surface px-3 text-xs text-muted">
    <span data-testid="status">{status}</span>
  </footer>
</div>
