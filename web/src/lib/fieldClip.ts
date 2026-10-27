/**
 * Copying a field's value and pasting it into another field, as World Editor does (Sven, 2026-10-27): a value only
 * goes into a field that can hold it.
 */
import type { FieldView } from "./api.ts";
import { listIds, listKinds } from "./fields.ts";

/** A copied field value, with what kind of value it is. */
export type FieldClip = {
  label: string;
  type: string;
  choice: string | null;
  listOf: string | null;
  value: string;
};

let kept: FieldClip | null = null;

/** The field value copied last, kept from object to object; null before any copy. */
export function copiedField(): FieldClip | null {
  return kept;
}

/** Keeps a field's value for a paste. */
export function copyField(field: FieldView): FieldClip {
  kept = {
    label: field.label,
    type: field.type,
    choice: field.choice,
    listOf: field.listOf,
    value: field.value,
  };
  return kept;
}

const numbers = ["int", "float"];
const lists = ["objectList", "objectRef"];

/**
 * Whether a copied value can go into a field: the same kind of value (the same choice list; objects of kinds the
 * field holds; one object for a field that holds one), or a number into a number field (a whole number for `int`).
 */
export function fits(clip: FieldClip, field: FieldView): boolean {
  if (numbers.includes(clip.type) && numbers.includes(field.type)) {
    return field.type === "float" || /^-?\d+$/.test(clip.value.trim());
  }
  if (lists.includes(clip.type) && lists.includes(field.type)) {
    const from = listKinds(clip.listOf);
    const to = listKinds(field.listOf);
    if (from.length === 0 || !from.every((kind) => to.includes(kind))) return false;
    return field.type === "objectList" || listIds(clip.value).length <= 1;
  }
  if (clip.type !== field.type) return false;
  if (clip.type === "choice") return clip.choice === field.choice;
  return true;
}
