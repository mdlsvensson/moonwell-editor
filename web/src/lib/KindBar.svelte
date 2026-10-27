<script lang="ts">
/**
 * The seven kinds of object as a row of icon buttons in the header, in the Object Editor's tab order. The kind's name
 * is the button's tooltip and its name for screen readers; each kind has its own icon and colour (Sven, 2026-10-27).
 */
import ChessRook from "@lucide/svelte/icons/chess-rook";
import FlaskRound from "@lucide/svelte/icons/flask-round";
import HandFist from "@lucide/svelte/icons/hand-fist";
import ShieldPlus from "@lucide/svelte/icons/shield-plus";
import Sparkles from "@lucide/svelte/icons/sparkles";
import TreePalm from "@lucide/svelte/icons/tree-palm";
import User from "@lucide/svelte/icons/user";
import type { Component } from "svelte";
import { type Kind, kinds } from "./api.ts";
import { t } from "./text.ts";
import { kindNames, kindText } from "./tree.ts";

type Props = {
  kind: Kind;
  disabled: boolean;
  onkind: (kind: Kind) => void;
};

let { kind, disabled, onkind }: Props = $props();

const icons: Record<Kind, Component<{ size?: number }>> = {
  unit: User,
  item: FlaskRound,
  destructable: TreePalm,
  doodad: ChessRook,
  ability: HandFist,
  buff: Sparkles,
  upgrade: ShieldPlus,
};
</script>

<div class="flex shrink-0 gap-0.5" role="group" aria-label={t("kindBar.label")}>
  {#each kinds as each (each)}
    {@const Icon = icons[each]}
    <button
      type="button"
      class="rounded-[5px] p-1.5 disabled:opacity-50 {kindText[each]} {each === kind
        ? 'bg-raised ring-1 ring-current'
        : 'opacity-70 hover:bg-hover hover:opacity-100'}"
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
