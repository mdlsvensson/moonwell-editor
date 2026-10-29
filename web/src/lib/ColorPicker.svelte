<script lang="ts">
/**
 * The tooltip editor's colour picker (Sven, 2026-10-29: WebView2's own picker could not pick a colour): a square of
 * saturation and value over a hue bar, both dragged with the mouse or stepped with the arrows, the colour's code to
 * read or type, and a button that takes the colour. The text keeps its selection while the square and bar are used.
 */
import { untrack } from "svelte";
import { type Hsv, hexOf, hsvOf, parseHex } from "./colour.ts";
import { t } from "./text.ts";

type Props = {
  /** The colour it starts at, `#rrggbb`. */
  value: string;
  /** The button's text (Add, Change). */
  action: string;
  onpick: (css: string) => void;
};

let { value, action, onpick }: Props = $props();

// It starts at `value`; the picker is opened anew for another colour.
let colour = $state<Hsv>(untrack(() => hsvOf(value)));
const hex = $derived(hexOf(colour));
// What is typed in the code box; null shows the colour's code.
let typed = $state<string | null>(null);

// A drag in the square or the bar: the pointer's place in the element, from 0 to 1 each way.
function drag(event: PointerEvent, set: (x: number, y: number) => void): void {
  event.preventDefault();
  const element = event.currentTarget as HTMLElement;
  element.setPointerCapture(event.pointerId);
  const at = (moved: PointerEvent): void => {
    const box = element.getBoundingClientRect();
    const x = Math.min(1, Math.max(0, (moved.clientX - box.left) / box.width));
    const y = Math.min(1, Math.max(0, (moved.clientY - box.top) / box.height));
    set(x, y);
    typed = null;
  };
  at(event);
  element.onpointermove = at;
  element.onpointerup = () => {
    element.onpointermove = null;
    element.onpointerup = null;
  };
}

function setSquare(x: number, y: number): void {
  colour = { ...colour, s: x, v: 1 - y };
}

function setHue(x: number): void {
  colour = { ...colour, h: Math.min(359.9, x * 360) };
}

// The arrows step the square by a twentieth and the hue by 5 degrees; Shift steps further.
function squareKey(event: KeyboardEvent): void {
  const step = event.shiftKey ? 0.2 : 0.05;
  const moves: Record<string, [number, number]> = {
    ArrowLeft: [-step, 0],
    ArrowRight: [step, 0],
    ArrowUp: [0, step],
    ArrowDown: [0, -step],
  };
  const move = moves[event.key];
  if (!move) return;
  event.preventDefault();
  colour = {
    ...colour,
    s: Math.min(1, Math.max(0, colour.s + move[0])),
    v: Math.min(1, Math.max(0, colour.v + move[1])),
  };
  typed = null;
}

function hueKey(event: KeyboardEvent): void {
  const step = event.shiftKey ? 30 : 5;
  const by =
    event.key === "ArrowLeft" || event.key === "ArrowDown"
      ? -step
      : event.key === "ArrowRight" || event.key === "ArrowUp"
        ? step
        : 0;
  if (by === 0) return;
  event.preventDefault();
  colour = { ...colour, h: Math.min(359.9, Math.max(0, colour.h + by)) };
  typed = null;
}

function type(text: string): void {
  typed = text;
  const parsed = parseHex(text);
  if (parsed) colour = hsvOf(parsed, colour.h);
}
</script>

<div class="flex w-[220px] flex-col gap-2 p-0.5">
  <div
    class="relative h-[140px] cursor-crosshair rounded outline-none focus-visible:ring-1 focus-visible:ring-accent"
    style:background-color="hsl({colour.h} 100% 50%)"
    role="slider"
    tabindex="0"
    aria-label={t("colourPicker.square")}
    aria-valuetext={hex}
    aria-valuenow={Math.round(colour.s * 100)}
    onpointerdown={(event) => drag(event, setSquare)}
    onkeydown={squareKey}
  >
    <div class="absolute inset-0 rounded bg-linear-to-r from-white to-transparent"></div>
    <div class="absolute inset-0 rounded bg-linear-to-t from-black to-transparent"></div>
    <div
      class="pointer-events-none absolute size-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white shadow-[0_0_0_1px_rgba(0,0,0,.6)]"
      style:left="{colour.s * 100}%"
      style:top="{(1 - colour.v) * 100}%"
    ></div>
  </div>
  <div
    class="relative h-3 cursor-pointer rounded outline-none focus-visible:ring-1 focus-visible:ring-accent"
    style:background="linear-gradient(to right, #f00, #ff0, #0f0, #0ff, #00f, #f0f, #f00)"
    role="slider"
    tabindex="0"
    aria-label={t("colourPicker.hue")}
    aria-valuemin={0}
    aria-valuemax={360}
    aria-valuenow={Math.round(colour.h)}
    onpointerdown={(event) => drag(event, (x) => setHue(x))}
    onkeydown={hueKey}
  >
    <div
      class="pointer-events-none absolute inset-y-[-2px] w-1.5 -translate-x-1/2 rounded-sm border border-white shadow-[0_0_0_1px_rgba(0,0,0,.6)]"
      style:left="{(colour.h / 360) * 100}%"
    ></div>
  </div>
  <div class="flex items-center gap-1.5">
    <span class="size-6 shrink-0 rounded border border-line" style:background-color={hex}></span>
    <input
      class="w-0 min-w-0 flex-1 rounded border px-1.5 py-[3px] font-mono text-[12.5px] text-fg outline-none focus:border-accent {typed !==
        null && !parseHex(typed)
        ? 'border-warn'
        : 'border-line'} bg-bg"
      value={typed ?? hex}
      spellcheck="false"
      aria-label={t("colourPicker.code")}
      oninput={(event) => type(event.currentTarget.value)}
      onkeydown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          onpick(hex);
        }
      }}
    />
    <button
      type="button"
      class="rounded-[5px] bg-accent px-2.5 py-[3px] text-[12.5px] font-bold text-bg hover:bg-accent-hi"
      onpointerdown={(event) => event.preventDefault()}
      onclick={() => onpick(hex)}
    >
      {action}
    </button>
  </div>
</div>
