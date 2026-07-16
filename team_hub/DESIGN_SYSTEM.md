# Knowledge Atlas: Design System

This document outlines the core design language, tokens, and animation standards used in the Knowledge Atlas Team Hub. Use these guidelines when building future pages or extending the application to maintain a premium, unified aesthetic.

---

## 1. Color Palette

The color system is designed to be highly readable, academic, and slightly warm, avoiding harsh pure whites and blacks.

### Base Colors
- **Background:** `#f4f0e6` (Warm Cream) - Used for the main body background.
- **Surface:** `#ffffff` (Pure White) - Used for cards, tables, and isolated UI elements to make them pop against the cream background.
- **Text (Main):** `#2c3e38` (Deep Forest Black) - Used for all primary reading text and headings.
- **Text (Muted):** `#667570` (Slate Grey) - Used for secondary text, metadata, and breadcrumbs.
- **Borders:** `#e2ddd0` (Sand) - Used for subtle dividers and table borders.

### Accents
- **Primary/Action:** `#d35400` (Burnt Orange) - Used for links and interactive highlights.
- **Success/Loader:** `#228B22` (Forest Green) - Used for positive statuses and the primary cinematic loading ring.
- **Highlight:** `#ffecd2` (Soft Peach) - Used for subtle text highlighting or active states.

---

## 2. Typography

We use a modern font stack that relies on native system fonts for maximum performance and crisp rendering across all devices.

- **Sans-Serif (Body & UI):** `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif`
  - *Usage:* Body paragraphs, navigation, metadata, table data.
- **Serif (Headings):** `Georgia, Cambria, "Times New Roman", Times, serif`
  - *Usage:* Only for major page headings (`<h1>`, `<h2>`) to give an academic, atlas-like feel.
- **Monospace (Code):** `ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", Menlo, monospace`
  - *Usage:* Code blocks, terminal commands, and technical data.

---

## 3. Layout & Grid

- **Max Width:** The main container (`.container` or `.page-grid`) is constrained to a maximum width of `1200px` to keep line lengths readable.
- **Page Grid:** The standard layout is a CSS Grid with a `250px` left sidebar and a flexible `1fr` main content area.
- **Gap:** A generous `4rem` (64px) gap separates the sidebar from the main content to provide breathing room.

---

## 4. UI Components

### Callouts
Callouts are used to highlight important information without breaking the flow of reading.
- **`.callout` (Base):** Adds padding, border-radius, and a left accent border.
- **`.callout-blue`:** Light blue background (`#eef2f5`), blue border (`#3498db`).
- **`.callout-teal`:** Light teal background (`#eef7f5`), teal border (`#1abc9c`).
- **`.callout-amber`:** Light amber background (`#fff8e1`), amber border (`#f39c12`).

### Status Indicators
Used in tables to show progress.
- **`.status-indicator` (Base):** A pill-shaped badge with bold, small text.
- **`.status-done`:** Green background, dark green text.
- **`.status-progress`:** Blue background, dark blue text.
- **`.status-pending`:** Grey background, dark grey text.

---

## 5. Animation Engine (GSAP)

The project uses **GSAP (GreenSock Animation Platform)** to drive premium, smooth entrance animations.

### Constants
- **Ease:** `"power3.inOut"` - A smooth, cinematic easing curve that starts slow, accelerates, and gently decelerates.
- **Durations:** 
  - `fast`: 0.3s (Hover effects, micro-interactions)
  - `base`: 0.8s (Standard reveal animations)
  - `slow`: 1.2s (Cinematic loader sweep)

### The "Fail-Safe Cascade" Pattern
To prevent the "blank white screen" bug, **never hide elements using CSS `opacity: 0`**. 
Instead, let the HTML render completely visibly by default. Then, use JavaScript to intercept the elements and animate them downwards.

**Implementation Example:**
```javascript
// 1. Select all text elements on the page
const allElements = document.querySelectorAll("h1, h2, h3, p, li, .callout, table");

// 2. Animate them from an invisible, shifted state -> back to their visible default
gsap.from(allElements, {
  y: 30,             // Start 30px lower than normal
  opacity: 0,        // Start invisible
  duration: 0.8,     // 0.8 seconds long
  stagger: 0.05,     // 50ms delay between each element popping in (The Cascade)
  ease: "power3.inOut"
});
```
*Why this works:* If JavaScript fails to load, the user simply sees a normal, un-animated website. Nothing breaks.

### The Loader
The loader uses a fixed overlay with a massive z-index. An SVG circle uses `stroke-dashoffset` to draw a `#228B22` Forest Green ring. Once complete, it translates upwards (`yPercent: -100`) to reveal the content underneath.
