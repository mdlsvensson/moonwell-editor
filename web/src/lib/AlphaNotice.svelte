<script lang="ts" module>
const storageKey = "moonwell-editor.alpha-understood";

/** True when the user ticked "Don't show this again" before (or the storage cannot be read: then it shows). */
export function alphaUnderstood(): boolean {
  try {
    return localStorage.getItem(storageKey) === "yes";
  } catch {
    return false;
  }
}
</script>

<script lang="ts">
/**
 * The notice the editor opens with (Sven, 2026-10-27): it is alpha software, so keep backups, and the map must be
 * closed in World Editor and left alone by anything else while the editor has it open.
 */
import TriangleAlert from "@lucide/svelte/icons/triangle-alert";
import Dialog from "./Dialog.svelte";

let { onclose }: { onclose: () => void } = $props();

let never = $state(false);

function close(): void {
  if (never) {
    try {
      localStorage.setItem(storageKey, "yes");
    } catch {
      // It shows again next time.
    }
  }
  onclose();
}
</script>

<Dialog title="Moonwell Editor is alpha software" oncancel={close}>
  <div class="flex gap-3">
    <TriangleAlert size={22} class="mt-0.5 shrink-0 text-changed" />
    <div class="flex flex-col gap-2">
      <p class="m-0">
        It is early work and can still get things wrong. Do not trust it with a map you have no backup of: copy the
        map before you open it here.
      </p>
      <p class="m-0">
        Close the map in World Editor first, and do not change it from anywhere else (World Editor, another tool, a
        script) while it is open here: the editor saves its own view of the map over the files.
      </p>
    </div>
  </div>
  <label class="mt-3 flex items-center gap-2 text-[13px] text-muted">
    <input type="checkbox" bind:checked={never} />
    Don't show this again
  </label>
  {#snippet actions()}
    <button
      type="button"
      class="rounded-[5px] bg-accent px-3 py-1 font-bold text-bg hover:bg-accent-hi"
      data-primary
      onclick={close}
    >
      I understand
    </button>
  {/snippet}
</Dialog>
