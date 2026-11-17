---
version: alpha
name: EduForge AI
description: A teacher's lesson workshop, with violet document spines and quiet working surfaces.
colors:
  primary: "#5B4BD8"
  ink: "#25263B"
  muted: "#666B80"
  background: "#F5F6FA"
  surface: "#FFFFFF"
  lavender: "#EFECFF"
  border: "#DFE2ED"
  success: "#197057"
  danger: "#B42338"
typography:
  display:
    fontFamily: "Unbounded, Arial, sans-serif"
  body:
    fontFamily: "Golos Text, Arial, sans-serif"
  mono:
    fontFamily: "Consolas, monospace"
rounded:
  DEFAULT: "12px"
  panel: "20px"
  control: "10px"
spacing:
  panel: "24px"
  page-max: "1600px"
components:
  button:
    height: "42px"
  card:
    rounded: "16px"
  dialog:
    rounded: "20px"
---

# EduForge design system

## Overview

The product is a lesson workshop for teachers: write a material, prepare a test, teach a group, review results. Sources: README.md, ROADMAP.md, TESTING.md and the verified API implementation. This is an application, including its sign-in screen, rather than a marketing website.

Russian is the interface language; authored lesson content may be Russian, Kazakh or English. The user works in Kazakhstan, but the application makes no country-specific legal or administrative claims. No Japanese-market scope. Desktop Chrome is the main preparation environment; mobile must retain every action.

The signature is the **document spine**: a narrow subject-colored edge and a folded-page icon on lesson cards. Expressive Unbounded headings appear sparingly; actual lessons use a calm, readable text face. Keep the existing violet brand, but replace inconsistent purple boxes, emoji navigation and dense toolbars. Avoid marketing dashboards, fictitious statistics, dark neon, cream/serif templates and decorative charts.

## Colors

The palette combines cool working paper, ink and violet. Runtime ownership is Model B: `frontend/eduforge-web/src/app/design-system.css`, imported by globals.css, owns semantic custom properties. This file mirrors accepted values. `colors.primary → --color-primary`, `ink → --color-ink`, `muted → --color-muted`, `background → --color-background`, `surface → --color-surface`, `lavender → --color-lavender`, `border → --color-border`, `success → --color-success`, `danger → --color-danger`. All foundational components consume these variables. Annotation colors remain literal document data and must not be recolored by the theme.

Only a light theme is implemented. Error, warning and success always include text, not color alone. Respect forced colors. Focus uses a visible violet ring. Pale lavender is a selected/AI surface, not a default background for every panel.

## Typography

Self-hosted variable Golos Text is the body/control face; Unbounded is the restrained display face. Both include Cyrillic. Their OFL licenses live with the fonts under `public/fonts`. No runtime font CDN. Headings: 38px home display, 28px route heading, 19px panel heading. Body: 15px/1.6; lesson prose: 16px/1.85; supporting labels: 12–13px, never extremely faint. Consolas handles source code. Technical data use tabular numerals. Allow long localized titles to wrap.

## Layout

Desktop: 240px sticky sidebar, natural document scroll, 40px horizontal content gutter, maximum workspace width 1600px. At 1100px reduce sidebar/gutters. At 760px use a compact horizontal navigation strip with every route reachable by scrolling; no hidden desktop-only actions. Cards use responsive 3/2/1 columns, with minimum width 240px. Forms remain single-column or paired related fields; do not fix the entire shell to viewport height. Tables scroll within their own horizontal container. Modal bodies scroll; all actions remain reachable.

## Elevation & Depth

Borders separate quiet white panels from cool paper. Use a restrained shadow on dialogs and a small hover elevation on clickable cards; no shadow on each text field. A tinted sheet may distinguish AI settings. No background animation or glass effects behind work.

## Shapes

Controls use 10px corners, cards 16px and large panels/dialogs 20px. Small semantic badges may be pills. Icons use consistent 1.7px round strokes; only the document-spine signature uses an expressive filled shape.

## Components

Shared owners and behavior live in UX-CONTRACT.md. Sidebar links have clear active, hover and keyboard focus states, plus complete Russian names. All routes share WorkspaceHeader. Catalog cards share spacing, icons, title hierarchy and metadata. Primary create/save actions are violet; destructive actions are quiet red until final confirmation. Disabled actions use a non-interactive cursor, stable dimensions and readable text.

Forms use shared Form validation and native selects; OS popup geometry is accepted. SearchField owns clear/search adornments. CreationDialog owns editor modals. Confirmation and URL input use app-owned dialogs, never browser prompts. Alerts remain next to the affected form; transient inline statuses use role=status. The generation disclosure is initially closed, uses human-readable parameters and keeps detailed model/tokens secondary. Answer grounds also use a native, initially closed disclosure with a visible review-status badge. ContentAccessControls presents three native radio choices on desktop and stacks them on mobile; a link appears only after access is saved.

Scrollbar ownership: global standards-based scrollbar-color/width, WebKit fallback, stable root gutter, normal forced-color behavior. Motion: short 140ms hover/focus transitions; reduced-motion disables transitions and scrolling animation. No ornamental entrance sequences. Long lesson content, screenshots, code and validation take priority over compact visual density.

## Do's and Don'ts

- Use real counts, real lesson titles and actual metadata. Never seed production merely to decorate screenshots.
- Keep safe and destructive actions distinct, preserve unsaved work and server authorization.
- Keep complex technical generation data in an optional disclosure/history.
- Do not hide navigation, controls or long forms on mobile.
- Do not create another token palette, modal or navigation implementation for a single screen.
