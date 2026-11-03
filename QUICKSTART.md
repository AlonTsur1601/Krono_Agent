# Krono Agent - Quick Start

## Install & Run (5 minutes)

```bash
# 1. Install dependencies
npm install

# 2. Run the app
npm run dev
```

The app launches. You'll see:
1. **Login screen** → Register a new account (stored locally in SQLite)
2. **Main interface** → Sidebar with Chat and Games
3. **Play Snake** → Click Games, click Start Game, use arrow keys
4. **Switch themes** → Dark/Light/Halloween selector in sidebar

## What Works Right Now

✅ **Authentication**: Register → Login → Logout (SQLite, no password hashing yet)  
✅ **Theme System**: 3 themes + system detection, persists across sessions  
✅ **Database**: Per-account SQLite with migrations  
✅ **Snake Game**: Complete with scoring, speed progression, pause  
✅ **Layout**: Sidebar navigation, main content area  

## What's Next (You'll Build)

📋 **Priority 1 - Make It Useful:**
1. Add OpenAI provider in `src/agent/providers/openai.ts`
2. Build chat UI in `src/renderer/features/chat/`
3. Add file read tool in `src/agent/tools/files.ts`
4. Connect chat → model → tool execution

📋 **Priority 2 - Add Games:**
5. Implement remaining 23 games (templates in design.md)
6. Create game thumbnails
7. Build games catalog page

📋 **Priority 3 - Full Features:**
8. Supabase integration for cloud sync
9. Terminal (xterm.js + node-pty)
10. Browser panel
11. Desktop automation
12. Projects, memory, automations
13. Plugin system

## File Structure You Need to Know

```
src/
├── main/index.ts          ← Add new IPC handlers here
├── main/ipc/              ← Create new IPC modules here
├── renderer/App.tsx       ← Main UI entry
├── renderer/features/     ← Add features here (auth, chat, games)
├── renderer/stores/       ← Add Zustand stores here
├── shared/schemas/        ← Add Zod schemas for IPC validation
└── db/schema.ts           ← Add database migrations here
```

## Common Tasks

**Add a new IPC endpoint:**
1. Define schema in `src/shared/schemas/index.ts`
2. Create handler in `src/main/ipc/yourFeature.ts`
3. Register in `src/main/index.ts`
4. Call from renderer: `window.api.invoke('your:action', data)`

**Add a new UI feature:**
1. Create component in `src/renderer/features/yourFeature/`
2. Add store if needed in `src/renderer/stores/yourFeatureStore.ts`
3. Add route/view in `MainLayout.tsx`

**Add a database table:**
1. Add migration to `src/db/schema.ts` (increment version)
2. Create interfaces in `src/shared/types/index.ts`
3. Query in main process with `dbManager.getDb()`

## Architecture Quick Reference

**Electron process model:**
```
Main Process (Node.js)
  ↕ IPC (validated with Zod)
Renderer Process (Browser/React)
```

**Data flow:**
```
User clicks button
  → Store function called
  → window.api.invoke('action', data)
  → IPC handler validates with Zod
  → Main process executes (database, file system, etc.)
  → Returns result
  → Store updates
  → UI re-renders
```

**Game pattern:**
```typescript
// Canvas rendering in requestAnimationFrame loop
// State in refs (don't trigger re-renders)
// Controls via keyboard events
// Persist scores to database
```

## Testing Your Changes

```bash
npm run lint      # Check code style
npm test          # Run unit tests
npm run build     # Build for production
```

## Need Help?

1. **Read the docs:** `docs/DEVELOPMENT.md` has detailed guides
2. **Check .kiro specs:** All requirements and designs are there
3. **See IMPLEMENTATION_STATUS.md:** Shows what's done and what's not

## Ready to Build?

Start with **Priority 1** above. The foundation is solid. Add one feature at a time, test it, commit it. The architecture supports everything in the spec—you just need to fill it in.

Good luck! 🚀
