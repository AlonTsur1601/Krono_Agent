import { useEffect, useState, useCallback } from 'react'
import { useChatStore } from '../../stores/chatStore'
interface Project { id?: string; name: string; directories: string[]; instructions: string; memory: string; permissions: 'ask' | 'project' | 'full' }
const empty: Project = { name: '', directories: [], instructions: '', memory: '', permissions: 'ask' }
export function ProjectsPanel({ onOpenChat }: { onOpenChat: () => void }) {
  const [projects, setProjects] = useState<Project[]>([]), [draft, setDraft] = useState<Project>(empty), [global, setGlobal] = useState(''), [status, setStatus] = useState('')
  const { chats, loadChats, createChat } = useChatStore()
  const load = useCallback(() => window.api.invoke('projects:list').then(setProjects).catch(error => setStatus(error.message)), [])
  useEffect(() => { load(); window.api.invoke('memory:get-global').then(setGlobal); loadChats() }, [loadChats, load])
  const save = async () => { try { await window.api.invoke('projects:save', draft); setDraft(empty); await load(); setStatus('Project saved') } catch (error) { setStatus(error instanceof Error ? error.message : 'Could not save project') } }
  return <section style={{ padding: 24, overflow: 'auto', maxWidth: 900, margin: 'auto' }}>
    <h2>Projects & memory</h2><p style={{ color: 'var(--text-muted)' }}>Folders stay on this device. Instructions and accepted memories are applied to project conversations.</p>
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, margin: '16px 0' }}>{projects.map(project => <div key={project.id}><button onClick={() => { setDraft(project); setStatus('') }}>{project.name}</button> <button onClick={() => createChat(undefined, project.id).then(onOpenChat)}>New chat in {project.name}</button></div>)}<button onClick={() => setDraft(empty)}>New project</button></div>
    <form onSubmit={event => { event.preventDefault(); void save() }} style={{ display: 'grid', gap: 12 }}>
      <label>Project name <input aria-label="Project name" required value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>
      <label>Default permissions <select aria-label="Project permissions" value={draft.permissions} onChange={event => setDraft({ ...draft, permissions: event.target.value as Project['permissions'] })}><option value="ask">Ask</option><option value="project">Project</option><option value="full">Full computer</option></select></label>
      <div><button type="button" onClick={() => window.api.invoke('projects:pick-folders').then(directories => setDraft({ ...draft, directories: [...new Set([...draft.directories, ...directories])] })).catch(error => setStatus(error.message))}>Add folders</button>
        {draft.directories.map(directory => <div key={directory} style={{ overflowWrap: 'anywhere' }}>{directory} <button type="button" aria-label={`Remove ${directory}`} onClick={() => setDraft({ ...draft, directories: draft.directories.filter(item => item !== directory) })}>Remove</button></div>)}</div>
      <label>Project instructions<textarea aria-label="Project instructions" rows={4} value={draft.instructions} onChange={event => setDraft({ ...draft, instructions: event.target.value })} /></label>
      <label>Project memory<textarea aria-label="Project memory" rows={4} value={draft.memory} onChange={event => setDraft({ ...draft, memory: event.target.value })} /></label>
      <button type="submit">Save project</button>
    </form>
    <h3 style={{ marginTop: 24 }}>Conversation organization</h3>
    {chats.map(chat => <label key={chat.id} style={{ display: 'flex', gap: 12, margin: '8px 0' }}>{chat.name}<select aria-label={`Project for ${chat.name}`} value={chat.projectId ?? ''} onChange={event => window.api.invoke('projects:move-chat', { chatId: chat.id, projectId: event.target.value || null }).then(loadChats).catch(error => setStatus(error.message))}><option value="">No project</option>{projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>)}
    <h3 style={{ marginTop: 24 }}>Global memory</h3><textarea aria-label="Global memory" rows={4} value={global} onChange={event => setGlobal(event.target.value)} />
    <button onClick={() => window.api.invoke('memory:set-global', global).then(() => setStatus('Global memory saved')).catch(error => setStatus(error.message))}>Save global memory</button>
    {status && <p role="status">{status}</p>}
  </section>
}
