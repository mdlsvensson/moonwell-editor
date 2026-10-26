<script lang="ts">
/**
 * The seven kinds of object as a row of icon buttons in the header, in the Object Editor's tab order. The kind's name
 * is the button's tooltip and its name for screen readers. The icons follow World Editor's tabs: a knight for its
 * helmet, a box for its chest, a pine, a castle for its tower, a fist, a flexed arm (World Editor uses the fist
 * twice), a shield.
 */
import BicepsFlexed from "@lucide/svelte/icons/biceps-flexed";
import Castle from "@lucide/svelte/icons/castle";
import ChessKnight from "@lucide/svelte/icons/chess-knight";
import HandFist from "@lucide/svelte/icons/hand-fist";
import Package from "@lucide/svelte/icons/package";
import Shield from "@lucide/svelte/icons/shield";
import TreePine from "@lucide/svelte/icons/tree-pine";
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
  unit: ChessKnight,
  item: Package,
  destructable: TreePine,
  doodad: Castle,
  ability: HandFist,
  buff: BicepsFlexed,
  upgrade: Shield,
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
