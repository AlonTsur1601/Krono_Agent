import { describe, it, expect } from 'vitest'
import { mkdtemp, mkdir, writeFile, symlink, readFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { readFileTool, writeFileTool } from './files'
import type { ExecutionContext } from '../types'

describe('project file boundaries', () => {
  it('allows a new project file and rejects an outside read and junction escape', async () => {
    const base = await mkdtemp(join(tmpdir(), 'krono-scope-'))
    const project = join(base, 'project'), outside = join(base, 'outside')
    await mkdir(project); await mkdir(outside)
    await writeFile(join(outside, 'private.txt'), 'outside')
    const context: ExecutionContext = { projectDirs: [project], permissions: 'project', requestApproval: async () => ({ approved: false }), emitProgress: () => {} }
    expect((await writeFileTool.execute({ path: join(project, 'new.txt'), content: 'inside' }, context)).success).toBe(true)
    expect((await readFileTool.execute({ path: join(outside, 'private.txt') }, context)).success).toBe(false)
    await symlink(outside, join(project, 'escape'), process.platform === 'win32' ? 'junction' : 'dir')
    expect((await readFileTool.execute({ path: join(project, 'escape', 'private.txt') }, context)).success).toBe(false)
    expect((await writeFileTool.execute({ path: join(project, 'escape', 'new.txt'), content: 'blocked' }, context)).success).toBe(false)
  })
  it('does not alter a file when approval is denied', async () => {
    const base = await mkdtemp(join(tmpdir(), 'krono-denied-'))
    const file = join(base, 'original.txt'); await writeFile(file, 'original')
    const context: ExecutionContext = { projectDirs: [], permissions: 'ask', requestApproval: async () => ({ approved: false }), emitProgress: () => {} }
    expect((await writeFileTool.execute({ path: file, content: 'changed' }, context)).success).toBe(false)
    expect(await readFile(file, 'utf8')).toBe('original')
  })
})
