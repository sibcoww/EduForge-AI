# EduForge application UX contract

## Business-context sources

| Concern | Source | UI consequence |
|---|---|---|
| Product and audience | README.md; ROADMAP.md | Teachers prepare lessons/tests and work with groups and sessions. |
| Permissions and privacy | backend/EduForge.Api/Authentication.cs, Program.cs, MediaEndpoints.cs | Cookie login; private teacher data; admin navigation only for admin. UI does not change authorization. |
| Deletion and lifecycle | TeachingEndpoints.cs, AdminEndpoints.cs, LibraryEndpoints.cs | Explicit confirmation before deletion; do not promise Undo or delete protection not provided by API. |
| Generation and review | ControlledLearningEndpoints.cs, SourceDocumentEndpoints.cs | AI output is a draft; retain teacher review and coverage gates. |
| Billing/legal | No billing or legal-consent flow exists | No pricing, consent or payment policy is introduced by the redesign. |

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Select/Listbox | Native select styled by globals.css | DESIGN.md | OS-owned popup | keyboard + open popup in browser |
| Date | Native inputs where present, Intl displays | Existing API timestamps | local display, no new date semantics | browser |
| Form | components/Form.tsx | This contract | create/edit/auth | validation + correction + failed save |
| Scrollbar | globals.css | DESIGN.md | nested table/source regions | computed style + narrow viewport |
| Toast | Existing inline role=status / role=alert regions | This contract | success/error | live region + preserved form |
| CRUD | Existing workspaces + CreationDialog + ui-dialog | Verified API behavior | save-and-stay editor; deletion returns to list | full browser CRUD |
| Search | SearchField | This contract | local catalogs; community remote search | clear + no-results + stale response |
| Navigation | TeacherNavigation + WorkspaceHeader | Route map | sticky sidebar / mobile scroll strip | active link + keyboard + unsaved guard |

There is no row-selection/bulk action capability; Table Selection is not applicable. Feedback remains inline rather than introducing a second global toast system.

## Flow ledger

Create opens the same CreationDialog as edit. Successful save stays in that editor (existing product convention), replaces the draft baseline and refreshes the list, with «Материал сохранён» / «Тест сохранён» / «Группа сохранена». Save failure preserves all input, shows an actionable error, and permits retry. Mutation buttons are disabled while the request is active. Do not change API routes, persisted data, scoring or permissions for a visual redesign.

Cancel/navigation checks unsaved work through the shared app-owned confirmation. A destructive action names the affected object/scope and awaits explicit confirmation; Cancel receives initial focus. Actual hard deletion waits for server acknowledgement. Errors remain in the owning workspace; do not claim success before acknowledgement. Browser beforeunload is allowed only for actual page unload.

## Lists and search

Materials/tests/groups retain the existing API-fed local search model; queries are transient, private working state rather than shareable URLs in this visual migration. Shared SearchField clears immediately and returns focus. Existing public-library server pagination, filters and stale-response guards remain; do not invent totals. Large-data pagination beyond current API behavior is a separate API-backed task. No table receives a height constraint through the shared shell. Community search uses 300ms delay and ignores stale results.

## Forms and recovery

Form suppresses native validation bubbles, checks constraint metadata, exposes app-owned Russian errors with aria-invalid/aria-describedby and focuses the first invalid control. Passwords remain masked with an optional show button and retain password-manager autocomplete. Textareas are non-resizable with sufficient default space, scrolling and content growth. Native select popup ownership is intentional: the OS owns geometry and keyboard behavior. No new authored calendar/combobox is introduced.

Existing authenticated requests own session errors; no new token storage or automatic retry of writes. Failed/offline operations retain input. Existing conflict handling and AI timeouts remain. This is not a promise of offline editing, cross-device sync or automatic merge. Admin/security policy stays server-owned.

## Accessibility and overlays

Russian accessible names, visible focus, text contrast and keyboard operation are baseline. App-owned native dialog surfaces provide modal isolation, Escape and focus restoration; nested screenshot editing preserves the editor beneath it. Horizontal mobile navigation exposes all routes. No color-only action/status. Screenshot markup retains existing colors. Pointer annotation drawing retains its existing tool UI; numerical placement controls are the non-drag alternative when this surface is changed. Reduced-motion and forced-colors remain usable.

## Migration and verification

Visual migration proceeds through tokens → navigation/header/auth → catalogs/editor → secondary screens; shared owners prevent screen-local copies. Retire old CSS literals in replaced foundations. No database migration. Legacy flows outside the visual migration retain server behavior; future functional changes require their own evidence.

Required: npm test, npm run typecheck, npm run build; strict premium audit; DESIGN.md lint. Browser evidence must cover materials, tests, groups, community, history, profile and authentication at desktop/mobile, plus success, failed save/retry, no-results/clear, keyboard select, unsaved cancellation, delete confirmation and reduced motion. Tests/verification records belong under frontend/eduforge-web/tests and .local/reports (private screenshots contain only synthetic QA data).

## Access and deletion — 17 November 2026

ContentAccessControls is the canonical access editor for materials and tests. Backend/EduForge.Api/ContentSharing.cs owns the verified permission rules; ContentDeletion.cs / TeachingEndpoints.cs own deletion. User decision: shared links require an authenticated teacher. Private is owner-only; Unlisted is not discoverable in catalogs, filters, copy routes or update tracking; Public uses the existing teacher community and moderator approval. Link views show questions without correctness flags, model answers or grounds. Existing public-library teacher copies retain their established answer-key behavior.

Sharing uses explicit saved snapshots. Public links serve only approved publication content; pending revisions do not replace the approved version. Private revokes the token, removes publication and supersedes pending review; reopening access creates a new token. Version checks prevent stale settings or moderator approval from reopening a revoked version. Existing downloaded/copied content cannot be revoked. ContentAccessControls confirms the scope before changing access and preserves selection/error recovery.

Images are authorized independently: owner, valid unlisted token for the image owner's shared snapshot, approved public snapshot, or authorized moderator preview. An arbitrary image reference in another teacher's document does not grant access. Share previews omit original screenshot URLs/annotation editing metadata and private AI history.

Deleting a material keeps linked tests as manual tests without source binding/grounding and explains that consequence before confirmation. Deleting either content type removes its publication, link and review requests. Session payloads/results and other teachers' existing copies remain. Deletion feedback remains visible in the list after the dialog closes.
