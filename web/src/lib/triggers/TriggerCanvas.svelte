<script lang="ts">
/**
 * A category as a Blueprint-style grid (Sven, 2026-10-31): a card for everything in it, which pans, zooms and fits. A card
 * that was never moved stands in columns; moving one keeps the whole grid's places, so nothing else jumps.
 */
import { t } from "../text.ts";
import type { CardMove, CardView, Catalog } from "./api.ts";
import {
  type Box,
  cardWidth,
  columnsFor,
  farScale,
  fitted,
  focused,
  grid,
  laidOut,
  openScale,
  placesToSave,
  snapped,
  type View,
  zoomedAt,
} from "./canvas.ts";
import type { CardActions } from "./cards.ts";
import TriggerCard from "./TriggerCard.svelte";

type Props = {
  /** The category shown. */
  parent: number;
  cards: CardView[];
  catalog: Catalog;
  mode: string;
  readOnly: boolean;
  selected: number | null;
  /** A card to bring into view; a new `count` brings it again. */
  flyTo: { id: number; count: number } | null;
  commenting: Record<number, boolean>;
  dropKey: string | null;
  actions: CardActions;
  onmoved: (places: CardMove[]) => void;
  onfold: (id: number, folded: boolean, places: CardMove[]) => void;
  /** A right click on the empty grid. */
  onmenu: (x: number, y: number) => void;
};

let {
  parent,
  cards,
  catalog,
  mode,
  readOnly,
  selected,
  flyTo,
  commenting,
  dropKey,
  actions,
  onmoved,
  onfold,
  onmenu,
}: Props = $props();

let element: HTMLDivElement | undefined = $state();
let width = $state(0);
let height = $state(0);
let view = $state<View>({ x: grid, y: grid, scale: 1 });
let gliding = $state(false);
let heights = $state<Record<number, number>>({});
// The columns are counted when a category first shows and when a card comes, so that a card growing does not reshape the
// grid.
let columns = $state(1);
let counted = 0;
// The view and the columns of each category seen, so going back finds it as it was.
const seen = new Map<number, { view: View; columns: number }>();
let shown: number | null = null;
let fitting = $state(false);
let flown = 0;
// The card being moved, at its place on the grid.
let moving = $state<{ id: number; x: number; y: number } | null>(null);
let panning: { x: number; y: number } | null = null;
// A right press, which pans once it moves, as in Blueprints; one that stays put opens the menu.
let pressed: { x: number; y: number; pointer: number } | null = null;
let panned = false;
let grabbed: { id: number; x: number; y: number; moved: boolean } | null = null;

const boxes = $derived(
  laidOut(
    cards.map((card) => ({
      id: card.id,
      place: card.place,
      height: heights[card.id] ?? 120,
    })),
    columns,
  ),
);
const measured = $derived(cards.every((card) => heights[card.id] !== undefined));

function boxOf(id: number): Box | undefined {
  return boxes.find((box) => box.id === id);
}

function glide(): void {
  gliding = true;
  setTimeout(() => (gliding = false), 260);
}

export function fit(): void {
  glide();
  view = fitted(boxes, width, height);
}

// Another category: its view as it was left, or all of it.
$effect(() => {
  if (shown === parent) return;
  if (shown !== null) seen.set(shown, { view, columns });
  shown = parent;
  const left = seen.get(parent);
  if (left) {
    view = left.view;
    columns = left.columns;
    counted = cards.length;
  } else {
    fitting = true;
  }
});

function columnsNow(): number {
  return columnsFor(
    cards.map((card) => heights[card.id] ?? 0),
    width / height,
  );
}

$effect(() => {
  if (!measured || width === 0) return;
  if (fitting) {
    fitting = false;
    counted = cards.length;
    columns = columnsNow();
    view = fitted(boxes, width, height, openScale);
  } else if (cards.length !== counted) {
    // A category filled from empty should not stay one long column.
    counted = cards.length;
    columns = Math.max(columns, columnsNow());
  }
});

$effect(() => {
  const target = flyTo;
  if (!target || target.count === flown || !measured || width === 0) return;
  const box = boxOf(target.id);
  if (!box) return;
  flown = target.count;
  glide();
  view = focused(box, width, height, Math.max(view.scale, 0.85));
});

function onpointerdown(event: PointerEvent): void {
  const target = event.target as HTMLElement;
  if (target.closest("[data-zoom]")) return;
  if (event.button === 2) {
    pressed = { x: event.clientX, y: event.clientY, pointer: event.pointerId };
    return;
  }

  if (event.button === 1 || (event.button === 0 && !target.closest("[data-card]"))) {
    event.preventDefault();
    panning = { x: event.clientX - view.x, y: event.clientY - view.y };
    element?.setPointerCapture(event.pointerId);
    return;
  }

  const grip = target.closest("[data-grip]");
  if (event.button !== 0 || !grip || target.closest("button") || readOnly) return;
  const id = Number(target.closest<HTMLElement>("[data-card]")?.dataset.card);
  const box = boxOf(id);
  const area = element?.getBoundingClientRect();
  if (!box || !area) return;
  event.preventDefault();
  grabbed = {
    id,
    x: (event.clientX - area.left - view.x) / view.scale - box.x,
    y: (event.clientY - area.top - view.y) / view.scale - box.y,
    moved: false,
  };
  element?.setPointerCapture(event.pointerId);
}

function onpointermove(event: PointerEvent): void {
  if (pressed && Math.hypot(event.clientX - pressed.x, event.clientY - pressed.y) > 4) {
    panning = { x: pressed.x - view.x, y: pressed.y - view.y };
    element?.setPointerCapture(pressed.pointer);
    pressed = null;
    panned = true;
  }

  if (panning) {
    view = { ...view, x: event.clientX - panning.x, y: event.clientY - panning.y };
    return;
  }

  const area = element?.getBoundingClientRect();
  if (!grabbed || !area) return;
  grabbed.moved = true;
  moving = {
    id: grabbed.id,
    x: snapped((event.clientX - area.left - view.x) / view.scale - grabbed.x),
    y: snapped((event.clientY - area.top - view.y) / view.scale - grabbed.y),
  };
}

function swallowMenu(event: MouseEvent): void {
  event.preventDefault();
  event.stopPropagation();
}

function onpointerup(): void {
  panning = null;
  pressed = null;
  // The menu the right button's release would open, wherever the mouse ended up.
  if (panned) {
    panned = false;
    window.addEventListener("contextmenu", swallowMenu, { capture: true, once: true });
    setTimeout(() => window.removeEventListener("contextmenu", swallowMenu, { capture: true }), 100);
  }

  const done = moving;
  const dropped = grabbed;
  grabbed = null;
  moving = null;
  if (done && dropped?.moved) onmoved(placesToSave(boxes, done));
}

function onwheel(event: WheelEvent): void {
  const target = event.target as HTMLElement;
  if (target instanceof HTMLTextAreaElement && document.activeElement === target) return;
  event.preventDefault();
  const area = element?.getBoundingClientRect();
  if (!area) return;
  view = zoomedAt(
    view,
    event.clientX - area.left,
    event.clientY - area.top,
    event.deltaY < 0 ? 1.1 : 0.9,
  );
}

function foldCard(id: number, folded: boolean): void {
  onfold(
    id,
    folded,
    boxes.map(({ id: each, x, y }) => ({ id: each, x, y })),
  );
}

const lines =
  "linear-gradient(var(--line-major) 1px, transparent 1px), linear-gradient(90deg, var(--line-major) 1px, transparent 1px), linear-gradient(var(--line-minor) 1px, transparent 1px), linear-gradient(90deg, var(--line-minor) 1px, transparent 1px)";
const major = $derived(grid * 5 * view.scale);
const minor = $derived(grid * view.scale);
</script>

<div
  bind:this={element}
  bind:clientWidth={width}
  bind:clientHeight={height}
  class="relative h-full min-h-0 overflow-hidden bg-raised select-none [--line-major:color-mix(in_srgb,var(--bg)_70%,transparent)] [--line-minor:color-mix(in_srgb,var(--border)_55%,transparent)]"
  style:background-image={lines}
  style:background-size="{major}px {major}px, {major}px {major}px, {minor}px {minor}px, {minor}px {minor}px"
  style:background-position="{view.x}px {view.y}px"
  style:--line-minor={minor < 8 ? "transparent" : null}
  role="application"
  aria-label={t("triggers.tree")}
  data-testid="trigger-canvas"
  {onpointerdown}
  {onpointermove}
  {onpointerup}
  {onwheel}
  oncontextmenu={(event) => {
    event.preventDefault();
    if (!(event.target as HTMLElement).closest("[data-card]")) onmenu(event.clientX, event.clientY);
  }}
>
  <div
    class="absolute top-0 left-0 origin-top-left"
    class:transition-transform={gliding}
    class:duration-200={gliding}
    style:transform="translate({view.x}px, {view.y}px) scale({view.scale})"
  >
    {#each cards as card (card.id)}
      {@const box = boxOf(card.id)}
      {#if box}
        {@const x = moving?.id === card.id ? moving.x : box.x}
        {@const y = moving?.id === card.id ? moving.y : box.y}
        <div
          class="absolute top-0 left-0"
          class:z-10={moving?.id === card.id}
          class:z-[5]={selected === card.id}
          style:width="{cardWidth}px"
          style:transform="translate({x}px, {y}px)"
          data-card={card.id}
          bind:offsetHeight={heights[card.id]}
        >
          <TriggerCard
            {card}
            {catalog}
            {mode}
            {readOnly}
            selected={selected === card.id}
            folded={card.place?.folded ?? false}
            commenting={commenting[card.id] ?? false}
            {dropKey}
            {actions}
            onfold={(folded) => foldCard(card.id, folded)}
          />
          {#if view.scale < farScale}
            <div
              class="pointer-events-none absolute inset-x-0 top-0 line-clamp-2 rounded-[8px] bg-black/75 px-[0.3em] py-[0.15em] leading-[1.15] font-medium break-words text-white"
              style:font-size="{Math.min(14 / view.scale, 56)}px"
            >
              {card.name}
            </div>
          {/if}
        </div>
      {/if}
    {/each}
  </div>

  {#if cards.length === 0}
    <p class="pointer-events-none absolute inset-0 m-0 grid place-items-center text-muted">
      {t("triggers.emptyCategory")}
    </p>
  {/if}

  <div
    class="absolute top-2 right-2 flex items-center gap-2 rounded-md border border-black bg-surface px-2 py-0.5 text-xs text-muted"
    data-zoom
  >
    <span title={t("triggers.zoom")}>{Math.round(view.scale * 100)}%</span>
    <button type="button" class="text-fg hover:text-accent-hi" onclick={fit}>{t("triggers.fit")}</button>
  </div>
</div>
