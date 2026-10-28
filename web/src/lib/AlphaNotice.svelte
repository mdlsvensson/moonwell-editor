<script lang="ts">
/**
 * The notice the editor opens with (Sven, 2026-10-27): it is alpha software, so keep backups, and the map must be
 * closed in World Editor and left alone by anything else while the editor has it open. The warning sign is on the
 * title's row, and only Settings stops it opening (Sven, 2026-10-28).
 */
import TriangleAlert from "@lucide/svelte/icons/triangle-alert";
import Dialog from "./Dialog.svelte";
import { t } from "./text.ts";

let { onclose }: { onclose: () => void } = $props();
</script>

<Dialog title={t("alpha.title")} icon={TriangleAlert} oncancel={onclose}>
  <div class="flex flex-col gap-2">
    <p class="m-0">{t("alpha.backup")}</p>
    <p class="m-0">{t("alpha.closeFirst")}</p>
  </div>
  {#snippet actions()}
    <button
      type="button"
      class="rounded-[5px] bg-accent px-3 py-1 font-bold text-bg hover:bg-accent-hi"
      data-primary
      onclick={onclose}
    >
      {t("alpha.understood")}
    </button>
  {/snippet}
</Dialog>
