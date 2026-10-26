<script lang="ts">
/**
 * The seven kinds of object as a row of icon buttons in the header, in the Object Editor's tab order. The kind's name
 * is the button's tooltip and its name for screen readers.
 */
import CircleArrowUp from "@lucide/svelte/icons/circle-arrow-up";
import Gem from "@lucide/svelte/icons/gem";
import Shrub from "@lucide/svelte/icons/shrub";
import Sparkles from "@lucide/svelte/icons/sparkles";
import TreePine from "@lucide/svelte/icons/tree-pine";
import Users from "@lucide/svelte/icons/users";
import WandSparkles from "@lucide/svelte/icons/wand-sparkles";
import type { Component } from "svelte";
import { type Kind, kinds } from "./api.ts";
import { kindNames } from "./tree.ts";

type Props = {
  kind: Kind;
  disabled: boolean;
  onkind: (kind: Kind) => void;
};

let { kind, disabled, onkind }: Props = $props();

const icons: Record<Kind, Component<{ size?: number }>> = {
  unit: Users,
  item: Gem,
  destructable: TreePine,
  doodad: Shrub,
  ability: WandSparkles,
  buff: Sparkles,
  upgrade: CircleArrowUp,
};
</script>

<div class="flex shrink-0 gap-0.5" role="group" aria-label="Kinds">
  {#each kinds as each (each)}
    {@const Icon = icons[each]}
    <button
      type="button"
      class="rounded-[5px] p-1.5 disabled:opacity-50 {each === kind
        ? 'bg-accent-lo text-accent-hi'
        : 'text-muted hover:bg-hover hover:text-fg'}"
      title={kindNames[each]}
      aria-label={kindNames[each]}
      aria-pressed={each === kind}
      {disabled}
      onclick={() => onkind(each)}
    >
      <Icon size={17} />
    </button>
  {/each}
</div>
