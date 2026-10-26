/// <reference types="vite/client" />

declare module "*.svelte" {
  import type { Component } from "svelte";

  const component: Component;
  export default component;
}

/** What WebView2 gives the page: a string channel to and from the C# app. */
interface Window {
  chrome?: {
    webview?: {
      postMessage(message: string): void;
      addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
      removeEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
    };
  };
}
